using System.Text.Json;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
namespace BotAimImprover;

public partial class BotAimImprover
{
    private long _localKitCount, _localKitEpoch, _hookCalls, _controllerMisses, _visibilityMisses, _writeFailures;
    private string _session = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string,long> _points = new();
    private readonly Dictionary<int,long> _hitgroups = new();
    private object? _lastWrite;
    private void SetLocalKitMode(string mode)
    {
        _aimMode = mode switch { "head"=>AimMode.HEAD,"body"=>AimMode.BODY,"mixed"=>AimMode.MIXED,_=>throw new ArgumentException("Invalid aim mode") };
        _localKitEpoch++;_localKitCount=0;_hookCalls=0;_controllerMisses=0;_visibilityMisses=0;_writeFailures=0;
        _points.Clear();_hitgroups.Clear();_lastWrite=null;_firstOverrideLogged=false;
        Logger.LogInformation("{AimEvidence}",LocalKitEvidence());
    }
    private string LocalKitEvidence()=>JsonSerializer.Serialize(new {
        Kind="localkit-aim-v2",Session=_session,Epoch=_localKitEpoch,Mode=_aimMode.ToString().ToLowerInvariant(),
        Count=_localKitCount,HookCalls=_hookCalls,ControllerMisses=_controllerMisses,VisibilityMisses=_visibilityMisses,
        WriteFailures=_writeFailures,Points=_points,Hitgroups=_hitgroups,LastWrite=_lastWrite,
        Scope="PostHook target memory readback and separate damage observations; gameplay comparison still required"
    });
    private void RegisterLocalKitDiagnostics()
    {
        Capabilities.RegisterPluginCapability(new PluginCapability<Tuple<Action<string>,Func<string>>>("localkit:aim:v1"),
            ()=>Tuple.Create<Action<string>,Func<string>>(SetLocalKitMode,LocalKitEvidence));
        RegisterListener<Listeners.OnMapStart>(_=>{_session=Guid.NewGuid().ToString("N");SetLocalKitMode(_aimMode.ToString().ToLowerInvariant());});
        RegisterEventHandler<EventPlayerHurt>((ev,_)=>{
            if(ev.Attacker is {IsValid:true,IsBot:true}) { _hitgroups.TryGetValue(ev.Hitgroup,out var n);_hitgroups[ev.Hitgroup]=n+1; }
            return HookResult.Continue;
        });
        AddCommand("css_localkit_aim_status","Read Hook entries, actual target writes and observed damage",(_,info)=>{
            info.ReplyToCommand(LocalKitEvidence());Logger.LogInformation("{AimEvidence}",LocalKitEvidence());
        });
    }
    private unsafe void RecordLocalKitWrite(IntPtr bot,int point,string? weapon,int slot,int? userId,int enemy,float x,float y,float z)
    {
        float* result=(float*)(bot+_off.TargetSpot).ToPointer();
        if(result[0]!=x||result[1]!=y||result[2]!=z){_writeFailures++;return;}
        _localKitCount++;var name=_aimPoints[point].Name;_points.TryGetValue(name,out var n);_points[name]=n+1;
        _lastWrite=new {Point=name,Weapon=weapon,Slot=slot,UserId=userId,Enemy=enemy,X=x,Y=y,Z=z,Readback=true};
        if(n<3)Logger.LogInformation("[localkit-aim-write] {AimEvidence}",LocalKitEvidence());
    }
}
