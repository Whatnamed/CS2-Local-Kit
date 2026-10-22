namespace CS2LocalKit.Core.MatchSetup;

public enum PreMatchStep { Wait, SelectHumanSide, ClearDefaultBots, AddBots, StageIdentities, BindIdentities, ApplyBehavior, Observe, Stop }
public enum PreMatchPhase { Waiting, AligningSide, Clearing, Adding, Staging, Binding, Behavior, Complete, Failed }
public sealed record PreMatchObservation(bool EnvironmentValid, bool PluginsReady, int HumanCount, MatchSide? HumanSide,
    int CtBots, int TBots, bool SafeBeforeScore, bool IdentitiesMatch = false, bool ModesMatch = false, bool StagedIdentitiesMatch = false, int OtherBots = 0);

/// <summary>One map's bounded pre-match application policy. No rounds, scores or AI are owned here.</summary>
public sealed class PreMatchSession(MatchSetupRequest request, MatchLineup lineup)
{
    public MatchSetupRequest Request { get; } = request;
    public MatchLineup Lineup { get; } = lineup;
    public PreMatchPhase Phase { get; private set; }
    public string Reason { get; private set; } = "插件/地图就绪后等待真人选边";
    public PreMatchStep Fail(string reason) { Phase = PreMatchPhase.Failed; Reason = reason; return PreMatchStep.Stop; }
    public PreMatchStep Advance(PreMatchObservation o)
    {
        if (Phase == PreMatchPhase.Failed) return PreMatchStep.Stop;
        // Never re-add, kick or re-assign identities on spawn/halftime/later rounds.
        if (Phase == PreMatchPhase.Complete) return PreMatchStep.Observe;
        if (!o.EnvironmentValid) return Fail("游戏/build、安装身份或恢复事务漂移；开局配置停止");
        if (!o.SafeBeforeScore) return Fail("计分/首回合已经开始，禁止重建阵容；配置保留供下图使用");
        if (!o.PluginsReady) { Reason = "等待 BotHider、Aim、NadeSystem 就绪"; return PreMatchStep.Wait; }
        if (o.HumanCount > 1) return Fail("仅支持一个本地真人，检测到其他真人，停止自动应用");
        if (o.HumanCount == 0 || o.HumanSide is null) { Reason = "等待唯一真人完成选边"; return PreMatchStep.Wait; }
        if (o.HumanSide != Request.Preset.HumanSide)
        {
            if (Phase != PreMatchPhase.Waiting && Phase != PreMatchPhase.AligningSide) return Fail("初始化期间真人阵营变化，停止重建");
            if (Phase == PreMatchPhase.AligningSide) return PreMatchStep.Wait;
            Phase = PreMatchPhase.AligningSide; Reason = "开局前选择预设 CT/T 阵营"; return PreMatchStep.SelectHumanSide;
        }
        switch (Phase)
        {
            case PreMatchPhase.Waiting:
            case PreMatchPhase.AligningSide:
                Phase = PreMatchPhase.Clearing; Reason = "仅在开局前清理默认 Bot，一次性初始化"; return PreMatchStep.ClearDefaultBots;
            case PreMatchPhase.Clearing:
                if (o.CtBots + o.TBots + o.OtherBots != 0) return PreMatchStep.Wait;
                Phase = PreMatchPhase.Adding; Reason = "正在生成目标 Bot；不自动重试添加"; return PreMatchStep.AddBots;
            case PreMatchPhase.Adding:
                if (o.CtBots != Lineup.Bots.Count(b=>b.Side==MatchSide.CT) || o.TBots != Lineup.Bots.Count(b=>b.Side==MatchSide.T) || o.OtherBots!=0) return PreMatchStep.Wait;
                Phase = PreMatchPhase.Staging; Reason = "释放默认 persona，先读回独立临时身份以避免跨 Bot 占用冲突"; return PreMatchStep.StageIdentities;
            case PreMatchPhase.Staging:
                if (!o.StagedIdentitiesMatch) return PreMatchStep.Wait;
                Phase = PreMatchPhase.Binding; Reason = "校验数量后绑定 Bot 身份"; return PreMatchStep.BindIdentities;
            case PreMatchPhase.Binding:
                if (!o.IdentitiesMatch) return PreMatchStep.Wait;
                Phase = PreMatchPhase.Behavior; Reason = "身份读回通过，应用 Aim/Nades 和队名"; return PreMatchStep.ApplyBehavior;
            case PreMatchPhase.Behavior:
                if (!o.IdentitiesMatch) return Fail("身份在确认期间漂移；不重新生成 Bot");
                if (!o.ModesMatch) return PreMatchStep.Wait;
                Phase = PreMatchPhase.Complete; Reason = "运行时已接收，人数/身份/模式已读回；玩法效果待实机验证"; return PreMatchStep.Observe;
            default: return PreMatchStep.Wait;
        }
    }
}
