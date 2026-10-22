using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CS2LocalKit.Core.MatchSetup;

public enum MatchSide { T = 2, CT = 3 }
public enum AimSetting { Mixed, Head, Body }
public enum NadeSetting { Off, Less, Normal, More, Max }
public enum InstalledProfile { Low, Medium, High }
public enum MatchPlayMode { NormalHuman, EnhancedHuman }
public enum MatchApplyState { Saved, Pending, Received, InsufficientEvidence, BehaviorObserved, Failed }
public sealed record MatchRuntimeStatus(int SchemaVersion, string SessionId, string? RequestId, string? PresetSha256, string Map, DateTimeOffset UpdatedAt,
    MatchApplyState State, string Reason, int CtCount = 0, int TCount = 0, string? AimReadback = null, string? NadesReadback = null, int ProcessId = 0, DateTimeOffset ProcessStartUtc = default);
public sealed record Team(string Id, string Name, string Logo, bool Recommended);
// PersonaId is BotHider's account ID, not a uniquely inferred nickname or a real-user attribution.
public sealed record PlayerIdentity(string PersonaId, string Name, ulong SteamId64, bool Enabled);
public sealed record PlayerSlot(string Name, string? PersonaId = null);
public sealed record RosterSnapshot(string Id, string TeamId, string Name, string RepresentativeDate, string Event, string Source, ImmutableArray<PlayerSlot> Slots);
public sealed record CustomRoster(string Id, string Name, ImmutableArray<PlayerSlot> Slots, string? CopiedFrom = null);
public sealed record MatchTeamSelection(string Name, string Logo, ImmutableArray<PlayerSlot> Slots, string? RosterId = null)
{
    public static MatchTeamSelection Random(string name) => new(name, "", Enumerable.Range(1, 5).Select(i => new PlayerSlot($"{name} {i}")).ToImmutableArray());
}
public sealed record MatchSetupPreset(int SchemaVersion, string Id, string Name, MatchSide HumanSide, int FriendlyBots, int EnemyBots,
    int ReplacedSlot, MatchTeamSelection Friendly, MatchTeamSelection Enemy, InstalledProfile Profile = InstalledProfile.Medium,
    AimSetting Aim = AimSetting.Mixed, NadeSetting Nades = NadeSetting.Normal, bool Favorite = false,
    MatchPlayMode Mode = MatchPlayMode.EnhancedHuman, string Map = "de_dust2")
{
    public static MatchSetupPreset Default() => new(1, Guid.NewGuid().ToString("N"), "我 + 4 vs 5", MatchSide.CT, 4, 5, 0,
        MatchTeamSelection.Random("我方"), MatchTeamSelection.Random("对手"));
}
public sealed record ResolvedBot(MatchSide Side, int Seat, string Name, string? PersonaId, ulong? SteamId64);
public sealed record MatchLineup(MatchSetupPreset Preset, ImmutableArray<ResolvedBot> Bots, ImmutableArray<string> Warnings)
{
    public int CtCount => Bots.Count(b => b.Side == MatchSide.CT) + (Preset.HumanSide == MatchSide.CT ? 1 : 0);
    public int TCount => Bots.Count(b => b.Side == MatchSide.T) + (Preset.HumanSide == MatchSide.T ? 1 : 0);
}

public static partial class MatchJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, Converters = { new JsonStringEnumConverter() }, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string text) => JsonSerializer.Deserialize<T>(text, Options) ?? throw new InvalidDataException("配置为空");
    [GeneratedRegex("^[a-zA-Z0-9_-]{1,80}$")] private static partial Regex IdPattern();
    public static void Id(string value) { if (value is null || !IdPattern().IsMatch(value)) throw new InvalidDataException("无效的稳定 ID"); }
    public static void Name(string value, int bytes = 31)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || Encoding.UTF8.GetByteCount(value) > bytes || value.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.Format || c is '"' or ';' or '\\'))
            throw new InvalidDataException($"名称须为 1–{bytes} UTF-8 bytes，不能包含控制字符、引号、分号或反斜杠");
    }
    public static void Slots(ImmutableArray<PlayerSlot> slots)
    {
        if (slots.IsDefault || slots.Length != 5) throw new InvalidDataException("阵容必须包含五个席位");
        foreach (var s in slots) { if (s is null) throw new InvalidDataException("席位为空"); Name(s.Name); if (s.PersonaId is not null) Id(s.PersonaId); }
    }
}
