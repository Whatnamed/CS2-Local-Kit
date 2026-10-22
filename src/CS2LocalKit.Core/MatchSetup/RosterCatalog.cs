using System.Collections.Immutable;

namespace CS2LocalKit.Core.MatchSetup;

public sealed record RosterCatalogData(int SchemaVersion, string BotRef, string BotHiderRef, ImmutableArray<Team> Teams, ImmutableArray<RosterSnapshot> Rosters);
public sealed class RosterCatalog
{
    public ImmutableArray<Team> Teams { get; }
    public ImmutableArray<RosterSnapshot> Rosters { get; }
    public ImmutableArray<PlayerIdentity> Players { get; }
    private readonly Dictionary<string, PlayerIdentity> _byId;
    public RosterCatalog(RosterCatalogData data, ImmutableArray<PlayerIdentity> players)
    {
        if (data.SchemaVersion != 1 || data.Teams.IsDefaultOrEmpty || data.Rosters.IsDefaultOrEmpty || players.IsDefaultOrEmpty) throw new InvalidDataException("阵容清单版本或数据无效");
        Teams = data.Teams; Rosters = data.Rosters; Players = players;
        _byId = players.ToDictionary(p => p.PersonaId, StringComparer.Ordinal);
        if (Teams.Select(t => t.Id).Distinct().Count() != Teams.Length || Rosters.Select(r => r.Id).Distinct().Count() != Rosters.Length) throw new InvalidDataException("清单有重复 ID");
        foreach (var r in Rosters) { MatchJson.Id(r.Id); MatchJson.Slots(r.Slots); if (!Teams.Any(t => t.Id == r.TeamId) || !Uri.IsWellFormedUriString(r.Source, UriKind.Absolute)) throw new InvalidDataException("阵容来源或队伍无效"); }
    }
    public static RosterCatalog LoadBuiltIn()
    {
        string Read(string name)
        {
            using var s = typeof(RosterCatalog).Assembly.GetManifestResourceStream("CS2LocalKit.MatchSetup." + name) ?? throw new InvalidDataException("缺少阵容内置资源");
            using var reader = new StreamReader(s); return reader.ReadToEnd();
        }
        return new(MatchJson.Read<RosterCatalogData>(Read("rosters.json")), MatchJson.Read<ImmutableArray<PlayerIdentity>>(Read("players.json")));
    }
    public PlayerIdentity? Player(string id) => _byId.GetValueOrDefault(id);
    public ImmutableArray<PlayerIdentity> Search(string query) => Players.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.PersonaId == query).OrderByDescending(p => p.Enabled).ThenBy(p => p.Name).ThenBy(p => p.PersonaId).Take(60).ToImmutableArray();
    public ImmutableArray<PlayerIdentity> Exact(string name) => Players.Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Enabled).ToImmutableArray();
    public MatchTeamSelection Select(RosterSnapshot roster)
    {
        var team = Teams.Single(t => t.Id == roster.TeamId);
        return new(team.Name, team.Logo, roster.Slots, roster.Id);
    }
    public MatchLineup Resolve(MatchSetupPreset preset)
    {
        if (preset.SchemaVersion != 1) throw new InvalidDataException("不支持的对局 schemaVersion；保留原配置");
        MatchJson.Id(preset.Id); MatchJson.Name(preset.Name, 120);
        if (!Enum.IsDefined(preset.HumanSide) || !Enum.IsDefined(preset.Profile) || !Enum.IsDefined(preset.Mode) || !Enum.IsDefined(preset.Aim) || !Enum.IsDefined(preset.Nades)
            || preset.FriendlyBots is < 0 or > 4 || preset.EnemyBots is < 0 or > 5 || preset.ReplacedSlot is < 0 or > 4) throw new InvalidDataException("阵营、人数、替代席位或行为档位无效");
        if (string.IsNullOrEmpty(preset.Map) || !System.Text.RegularExpressions.Regex.IsMatch(preset.Map, "^(de_|cs_|ar_)[a-z0-9_]{1,40}$"))
            throw new InvalidDataException("地图名称无效；只能选择本机游戏地图");
        var bots = ImmutableArray.CreateBuilder<ResolvedBot>(); var warnings = ImmutableArray.CreateBuilder<string>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal); var usedNames = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        void Add(MatchTeamSelection selection, MatchSide side, int count, bool friendly)
        {
            if (selection is null) throw new InvalidDataException("缺少阵容");
            MatchJson.Name(selection.Name, 80); MatchJson.Slots(selection.Slots);
            if (selection.Logo is null || selection.Logo.Length > 24 || selection.Logo.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) throw new InvalidDataException("队徽 ID 无效");
            foreach (var seat in Enumerable.Range(0,5).Where(i => !friendly || i != preset.ReplacedSlot).Take(count))
            {
                var slot = selection.Slots[seat]; PlayerIdentity? player = null;
                if (slot.PersonaId is not null)
                {
                    player = Player(slot.PersonaId);
                    if (player is null || !player.Enabled) { warnings.Add($"{slot.Name}：persona 不存在或 disabled，使用自定义名称"); player = null; }
                }
                else
                {
                    var candidates = Exact(slot.Name);
                    if (candidates.Length == 1) player = candidates[0];
                    else warnings.Add(candidates.Length > 1 ? $"{slot.Name}：同名有 {candidates.Length} 个 ID，未指定时使用名称降级" : $"{slot.Name}：自定义名称，无已绑定 persona");
                }
                if (player is not null && !usedIds.Add(player.PersonaId))
                {
                    warnings.Add($"{slot.Name}：双方重复 persona，后出现的席位使用名称降级"); player = null;
                }
                usedNames.TryGetValue(slot.Name, out var n); usedNames[slot.Name] = ++n;
                var name = n == 1 ? slot.Name : $"{slot.Name} ({n})";
                if (n > 1) { MatchJson.Name(name); warnings.Add($"{slot.Name}：同名显示为 {name}"); }
                bots.Add(new(side, seat, name, player?.PersonaId, player?.SteamId64));
            }
        }
        Add(preset.Friendly, preset.HumanSide, preset.FriendlyBots, true);
        Add(preset.Enemy, preset.HumanSide == MatchSide.CT ? MatchSide.T : MatchSide.CT, preset.EnemyBots, false);
        return new(preset, bots.ToImmutable(), warnings.ToImmutable());
    }
}
