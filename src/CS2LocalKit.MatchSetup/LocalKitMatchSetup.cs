using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using BotHiderApi;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CS2LocalKit.Core;
using CS2LocalKit.Core.MatchSetup;
using Microsoft.Extensions.Logging;

namespace CS2LocalKit.MatchSetup.Adapter;

/// <summary>Local, one-human, pre-score adapter. No Tick listener, restart or score writes.</summary>
public sealed class LocalKitMatchSetup : BasePlugin
{
    public override string ModuleName => "LocalKitMatchSetup";
    public override string ModuleVersion => "1.0.0-candidate";
    public override string ModuleAuthor => "CS2 Local Kit";
    private static readonly PluginCapability<IBotHiderApi> Hider = new("bothider:api");
    private static readonly PluginCapability<Tuple<Action<string>,Func<string>>> Aim = new("localkit:aim:v1"), Nades = new("localkit:nades:v1");
    private readonly RosterCatalog _catalog = RosterCatalog.LoadBuiltIn();
    private MatchSetupStore _store = null!;
    private string _map = "", _sessionId = "", _root = "";
    private bool _mapActive, _liveRoundStarted, _checking, _verifiedEnvironment, _unloading;
    private int _attempts;
    private MatchInstallation? _installation;
    private PreMatchSession? _session;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _timer;
    private long _aimEpoch, _nadeEpoch;
    private readonly List<Binding> _bindings = [];
    private readonly int _pid = Environment.ProcessId;
    private readonly DateTimeOffset _processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private sealed record Binding(int Slot, IntPtr Handle, int? UserId, ResolvedBot Bot, ulong Sid);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr GetCommandLineW();

    public override void Load(bool hotReload)
    {
        _root=Path.GetFullPath(Path.Combine(Server.GameDirectory,"../.."));
        _store=new(Path.Combine(CorePaths.Cs2ModRoot,"app-data/match-setup"),_catalog);
        RegisterListener<Listeners.OnMapStart>(StartMap);
        RegisterListener<Listeners.OnMapEnd>(()=> { _mapActive=false; _timer?.Kill(); _timer=null; _session=null; _bindings.Clear(); _verifiedEnvironment=false; });
        RegisterEventHandler<EventPlayerTeam>((_,_)=> { Schedule(); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerConnectFull>((_,_)=> { Schedule(); return HookResult.Continue; });
        RegisterEventHandler<EventRoundStart>((_,_)=> { if (_session?.Phase==PreMatchPhase.Complete) Observe(); else Schedule(); return HookResult.Continue; });
        RegisterEventHandler<EventRoundFreezeEnd>((_,_)=> { if (Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules?.WarmupPeriod!=false) return HookResult.Continue; _liveRoundStarted=true; if (_session is not null && _session.Phase!=PreMatchPhase.Complete) Stop("首回合已开始，未完成的配置停止；不会踢 Bot 或重开"); return HookResult.Continue; });
        AddCommand("css_localkit_status","Read the exact current map/request diagnostics",(caller,info)=>
        {
            if (caller is not null && IsBot(caller)) return;
            if (_session?.Phase==PreMatchPhase.Complete) Observe();
            info.ReplyToCommand(_session is null ? "[LocalKit] No queued request received on this map; save a setup and load a new local map." : $"[LocalKit] {_sessionId} | {_session.Request.RequestId} | {_session.Phase}: {_session.Reason}");
        });
        if (hotReload)
        {
            // Late attachment never gains permission to initialize the current match.
            _mapActive=true;_map=Server.MapName;_sessionId=NewSessionId();_liveRoundStarted=true;
            Status(MatchApplyState.Failed,"适配层热加载：本图禁止自动重建，请保存配置后重新载入地图");
        }
    }
    public override void Unload(bool hotReload) { _unloading=true;_timer?.Kill(); }
    private string NewSessionId() => $"{_pid}-{_processStart.UtcTicks}-{Guid.NewGuid():N}";
    private void StartMap(string name)
    {
        if (_mapActive && _map==name) return; // Duplicate notification cannot reset one-shot initialization.
        _timer?.Kill();_timer=null;_mapActive=true;_map=name;_sessionId=NewSessionId();_liveRoundStarted=false;
        _attempts=0;_session=null;_bindings.Clear();_installation=null;_verifiedEnvironment=false;
        try
        {
            var request=_store.ClaimForMap(name,_sessionId); if (request is null) return; // No matching fresh one-shot request.
            _session=new(request,_catalog.Resolve(request.Preset));
            Status(MatchApplyState.Pending,"地图已收到保存请求，正在校验当前 build/安装身份");
            if (_checking) { Stop("上次环境校验尚未结束，请重新载入地图"); return; }
            _checking=true;var token=_sessionId;
            _=Task.Run(()=>
            {
                MatchInstallation? result=null;string? error=null;
                try { result=MatchRuntimeContract.Validate(_root,CorePaths.Cs2ModRoot); } catch(Exception ex) { error=ex.Message; }
                Server.NextFrame(()=>
                {
                    _checking=false;if (_unloading || token!=_sessionId || _session is null) return;
                    var commandLine=Marshal.PtrToStringUni(GetCommandLineW())??"";
                    if (error is not null) { Stop(error);return; }
                    if (!Regex.IsMatch(commandLine,@"(?:^|\s)-insecure(?:\s|$)",RegexOptions.IgnoreCase) || Regex.IsMatch(commandLine,@"(?:^|\s)-dedicated(?:\s|$)",RegexOptions.IgnoreCase)) { Stop("仅支持以 -insecure 启动的本地 listen server");return; }
                    _installation=result;
                    if (_installation!.Profile!=_session.Request.Preset.Profile) { Stop("预设强度与已安装 profile 不符，不能热切；先通过原事务恢复并安装对应 profile");return; }
                    _verifiedEnvironment=true;Schedule();
                });
            });
        }
        catch(Exception ex) { Stop(ex.Message); }
    }
    private void Schedule()
    {
        if (!_mapActive || !_verifiedEnvironment || _timer is not null || _session is null || _session.Phase is PreMatchPhase.Complete or PreMatchPhase.Failed) return;
        var token=_sessionId;
        _timer=AddTimer(1f,()=>
        {
            _timer=null; if (_unloading || token!=_sessionId || _session is null) return;
            try
            {
                if (++_attempts>24) { Stop("开局应用超时；未达到目标人数/身份/插件就绪状态，不重复添加或踢 Bot");return; }
                Advance();
                if (_session.Phase==PreMatchPhase.Waiting && Ready() && Players().Where(p=>!IsBot(p)).All(p=>p.TeamNum is not (2 or 3))) { _attempts=0;return; }
            }
            catch(Exception ex) { Stop(ex.Message); }
            Schedule();
        },TimerFlags.STOP_ON_MAPCHANGE);
    }
    private IBotHiderApi? Api() { try { return Hider.Get(); } catch { return null; } }
    private bool IsBot(CCSPlayerController p) => p.IsBot || (Api()?.IsManagedBot(p.Slot)??false);
    private bool Ready()
    {
        try { var api=Hider.Get();return api is not null && api.GetSignatures().Length>0 && api.GetSignatures().All(p=>p.Addr!=0) && Aim.Get() is not null && Nades.Get() is not null; } catch { return false; }
    }
    private List<CCSPlayerController> Players() => Utilities.GetPlayers().Where(p=>p.IsValid && !p.IsHLTV).ToList();
    private bool SafeBeforeScore()
    {
        var rules=Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        return !_liveRoundStarted && rules is not null && rules.TotalRoundsPlayed==0 && (rules.WarmupPeriod || rules.FreezePeriod)
            && Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager").All(t=>t.Score==0);
    }
    private static ulong ReservedSid(int slot) => 76561197960265728UL+0xf0000000UL+(ulong)slot;
    private bool BoundIdentitiesMatch(bool staging=false,bool allowSideSwitch=false)
    {
        var api=Api(); if (api is null || _session is null || _bindings.Count!=_session.Lineup.Bots.Length) return false;
        return _bindings.All(b=>
        {
            var p=Utilities.GetPlayerFromSlot(b.Slot);
            return p is not null && p.IsValid && p.Handle==b.Handle && p.UserId==b.UserId && api.IsManagedBot(b.Slot)
                && (allowSideSwitch || p.TeamNum==(int)b.Bot.Side) && api.GetBotSteamId(b.Slot)==(staging?ReservedSid(b.Slot):b.Sid) && api.GetPersonaName(b.Slot)==b.Bot.Name && p.PlayerName==b.Bot.Name;
        });
    }
    private JsonElement ReadMode(bool aim) { using var document=JsonDocument.Parse((aim?Aim:Nades).Get()!.Item2());return document.RootElement.Clone(); }
    private bool ModesMatch()
    {
        if (_session is null || _aimEpoch==0 || _nadeEpoch==0) return false;
        var a=ReadMode(true);var n=ReadMode(false);
        return a.GetProperty("Epoch").GetInt64()==_aimEpoch && n.GetProperty("Epoch").GetInt64()==_nadeEpoch
            && a.GetProperty("Mode").GetString()==_session.Request.Preset.Aim.ToString().ToLowerInvariant()
            && n.GetProperty("Mode").GetString()==_session.Request.Preset.Nades.ToString().ToLowerInvariant()
            && (_session.Request.Preset.Nades==NadeSetting.Off || n.GetProperty("DatabaseCount").GetInt32()>0);
    }
    private bool PresentationMatches()
    {
        var p=_session!.Request.Preset;var ct=p.HumanSide==MatchSide.CT?p.Friendly:p.Enemy;var t=p.HumanSide==MatchSide.T?p.Friendly:p.Enemy;
        return ConVar.Find("mp_teamname_1")?.StringValue==ct.Name && ConVar.Find("mp_teamname_2")?.StringValue==t.Name
            && ConVar.Find("mp_teamlogo_1")?.StringValue==ct.Logo && ConVar.Find("mp_teamlogo_2")?.StringValue==t.Logo;
    }
    private void Advance()
    {
        var s=_session!;var players=Players();var humans=players.Where(p=>!IsBot(p)).ToList();var bots=players.Where(IsBot).ToList();
        var human=humans.Count==1?humans[0]:null;
        MatchSide? side=human?.TeamNum is 2 or 3?(MatchSide)human.TeamNum:null;
        var ready=Ready();
        var step=s.Advance(new(_verifiedEnvironment,ready,humans.Count,side,bots.Count(b=>b.TeamNum==3),bots.Count(b=>b.TeamNum==2),SafeBeforeScore(),ready&&BoundIdentitiesMatch(),ready&&s.Phase==PreMatchPhase.Behavior&&ModesMatch()&&PresentationMatches(),ready&&BoundIdentitiesMatch(staging:true),bots.Count(b=>b.TeamNum is not (2 or 3))));
        switch(step)
        {
            case PreMatchStep.SelectHumanSide: human!.ChangeTeam((CsTeam)(int)s.Request.Preset.HumanSide);break;
            case PreMatchStep.ClearDefaultBots:
                // These mutations are emitted once, before scoring only; never reset a round.
                Server.ExecuteCommand("bot_quota_mode normal; bot_quota 0; mp_autoteambalance 0; mp_limitteams 0; bot_auto_vacate 0");
                if (bots.Count>0) Server.ExecuteCommand("bot_kick");
                break;
            case PreMatchStep.AddBots:
                foreach(var bot in s.Lineup.Bots) Server.ExecuteCommand($"bot_add_{(bot.Side==MatchSide.CT?"ct":"t")} \"{bot.Name}\"");
                break;
            case PreMatchStep.StageIdentities:
                var api=Api()!;
                foreach(var team in s.Lineup.Bots.GroupBy(b=>b.Side))
                {
                    var actual=bots.Where(b=>b.TeamNum==(int)team.Key).OrderBy(b=>b.UserId).ToArray();var expected=team.ToArray();
                    for(int i=0;i<expected.Length;i++)
                    {
                        var p=actual[i];var b=expected[i];
                        if (!api.IsManagedBot(p.Slot)) throw new InvalidOperationException("BotHider 未认领新生成的 Bot，停止身份写入");
                        // Reserved synthetic identities for custom/duplicate fallback names.
                        var sid=b.SteamId64??ReservedSid(p.Slot);
                        if (players.Any(x=>x.Slot!=p.Slot && !IsBot(x) && x.SteamID==sid)) throw new InvalidOperationException("目标 persona 与真人身份冲突");
                        if (!api.SetBotSteamId(p.Slot,ReservedSid(p.Slot))||!api.SetPersonaName(p.Slot,b.Name)) throw new InvalidOperationException("BotHider 拒绝临时身份命令；未将队列接收视为生效");
                        _bindings.Add(new(p.Slot,p.Handle,p.UserId,b,sid));
                    }
                }
                break;
            case PreMatchStep.BindIdentities:
                var hider=Api()!;
                foreach(var binding in _bindings)
                    if (!hider.SetBotSteamId(binding.Slot,binding.Sid)||!hider.SetPersonaName(binding.Slot,binding.Bot.Name)) throw new InvalidOperationException("BotHider 拒绝目标身份，停止且不重复生成 Bot");
                break;
            case PreMatchStep.ApplyBehavior:
                var pset=s.Request.Preset;Aim.Get()!.Item1(pset.Aim.ToString().ToLowerInvariant());Nades.Get()!.Item1(pset.Nades.ToString().ToLowerInvariant());
                _aimEpoch=ReadMode(true).GetProperty("Epoch").GetInt64();_nadeEpoch=ReadMode(false).GetProperty("Epoch").GetInt64();
                var ct=pset.HumanSide==MatchSide.CT?pset.Friendly:pset.Enemy;var t=pset.HumanSide==MatchSide.T?pset.Friendly:pset.Enemy;
                Server.ExecuteCommand($"mp_teamname_1 \"{ct.Name}\"; mp_teamname_2 \"{t.Name}\"; mp_teamlogo_1 \"{ct.Logo}\"; mp_teamlogo_2 \"{t.Logo}\"");
                break;
            case PreMatchStep.Observe:
                var library=_store.Load();_store.Save(library with { LastUsed=s.Request.Preset, Recent=library.Recent.Prepend(s.Request.Preset.Id).Distinct().Take(10).ToImmutableArray() });
                Observe();return;
            case PreMatchStep.Stop: Stop(s.Reason);return;
        }
        Status(MatchApplyState.Pending,s.Reason);
    }
    private void Observe()
    {
        try
        {
            if (_session is null) return;
            if (!Ready()||!ModesMatch()||!BoundIdentitiesMatch(allowSideSwitch:true)) { Stop("插件/身份/本局模式或 epoch 已改变；未自动重新应用");return; }
            var a=ReadMode(true);var n=ReadMode(false);
            var nadesEnabled=_session.Request.Preset.Nades!=NadeSetting.Off;
            var observed=a.GetProperty("Count").GetInt64()>0 && nadesEnabled && n.GetProperty("MapCount").GetInt32()>0 && n.GetProperty("Count").GetInt64()>0;
            var reason=observed?"内部 aim 写入与投掷已观察；玩法和 off/max 差异仍需实机验收":"运行时已接收且人数/身份/模式读回通过；行为证据不足，需实机对照";
            if (n.GetProperty("MapCount").GetInt32()==0 && nadesEnabled) reason="运行时已接收，但当前地图无 Nade lineup；投掷效果验证不足";
            if (!nadesEnabled) reason="Nades 关闭已读回；无投掷为预期，实际行为仍待实机对照";
            Status(observed?MatchApplyState.BehaviorObserved:MatchApplyState.InsufficientEvidence,reason,a.GetRawText(),n.GetRawText());
        }
        catch(Exception ex) { Stop(ex.Message); }
    }
    private void Stop(string reason)
    {
        _session?.Fail(reason);_timer?.Kill();_timer=null;Status(MatchApplyState.Failed,reason);
        Server.PrintToChatAll("[LocalKit] 配置应用停止："+reason);
    }
    private void Status(MatchApplyState state,string reason,string? aim=null,string? nades=null)
    {
        try
        {
            var players=Players();
            var status=new MatchRuntimeStatus(1,_sessionId,_session?.Request.RequestId,_session?.Request.PresetSha256,_map,DateTimeOffset.UtcNow,state,reason,
                players.Count(p=>p.TeamNum==3),players.Count(p=>p.TeamNum==2),aim,nades,_pid,_processStart);
            _store.WriteStatus(status);
            using var process=Process.GetCurrentProcess();
            Logger.LogInformation("[LocalKit] {Status} | round-counts CT={CT} T={T} privateBytes={Private} workingSet={Working}",MatchJson.Write(status),status.CtCount,status.TCount,process.PrivateMemorySize64,process.WorkingSet64);
        }
        catch(Exception ex) { Logger.LogError(ex,"[LocalKit] Unable to persist runtime diagnostics; no gameplay success claimed"); }
    }
}
