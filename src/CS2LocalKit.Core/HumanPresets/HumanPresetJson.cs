using System.Text;
using System.Text.Json;

namespace CS2LocalKit.Core.HumanPresets;

/// <summary>
/// Parsing/serialization for the canonical HumanPreset v1 JSON format.
/// Strict by design: unknown properties (including loadoutIdentity, uid, hash,
/// steamid) are rejected - they are not part of v1 and must never silently enter
/// the canonical format. Collections are normalized to numeric defindex order on
/// parse, and written back in that same deterministic order.
/// </summary>
public static class HumanPresetJson
{
    private static readonly JsonSerializerOptions WriterOptions = new()
    {
        WriteIndented = true, // 2-space indent, matches the accepted C2 fixture style
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static HumanPreset Parse(string json)
    {
        var problems = new List<string>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new HumanPresetFormatException("preset root must be a JSON object");

        string? kind = null;
        int? schemaVersion = null;
        TeamPreset? ct = null, t = null;
        int? musicKitId = null;

        foreach (var prop in root.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "kind":
                    kind = ReadString(prop);
                    break;
                case "schemaVersion":
                    schemaVersion = ReadInt(prop, "schemaVersion", problems);
                    break;
                case "ct":
                    ct = ReadTeam(prop, "ct", problems);
                    break;
                case "t":
                    t = ReadTeam(prop, "t", problems);
                    break;
                case "musicKitId":
                    musicKitId = prop.Value.ValueKind == JsonValueKind.Null
                        ? null
                        : ReadInt(prop, "musicKitId", problems);
                    break;
                default:
                    problems.Add($"unknown top-level property '{prop.Name}'");
                    break;
            }
        }

        if (kind != HumanPreset.ExpectedKind)
            problems.Add($"kind must be '{HumanPreset.ExpectedKind}' (got '{kind}')");
        if (schemaVersion != HumanPreset.ExpectedSchemaVersion)
            problems.Add($"schemaVersion must be {HumanPreset.ExpectedSchemaVersion} (got '{schemaVersion}')");
        if (ct is null) problems.Add("missing team object 'ct'");
        if (t is null) problems.Add("missing team object 't'");
        if (musicKitId is < 0) problems.Add($"musicKitId must be a non-negative integer or null (got '{musicKitId}')");

        if (problems.Count > 0)
            throw new HumanPresetFormatException("invalid HumanPreset v1:\n  " + string.Join("\n  ", problems));

        return new HumanPreset
        {
            Kind = kind!,
            SchemaVersion = schemaVersion!.Value,
            Ct = ct!,
            T = t!,
            MusicKitId = musicKitId,
        };
    }

    /// <summary>Serializes in canonical form (sorted defindexes, 2-space indent, LF newlines).</summary>
    public static string Write(HumanPreset preset)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", preset.Kind);
            writer.WriteNumber("schemaVersion", preset.SchemaVersion);
            WriteTeam(writer, "ct", preset.Ct);
            WriteTeam(writer, "t", preset.T);
            if (preset.MusicKitId is { } music)
                writer.WriteNumber("musicKitId", music);
            else
                writer.WriteNull("musicKitId");
            writer.WriteEndObject();
        }

        // Normalize to CRLF to match the machine-accepted fixture family, and terminate
        // the file with a trailing newline like every other generated artifact.
        var text = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n").Replace("\n", "\r\n");
        return text + "\r\n";
    }

    private static void WriteTeam(Utf8JsonWriter writer, string name, TeamPreset team)
    {
        writer.WriteStartObject(name);
        writer.WriteStartObject("weapons");
        foreach (var w in team.Weapons)
        {
            writer.WriteStartObject(w.DefIndex.ToString());
            WriteCosmetic(writer, w.Preset);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();

        writer.WriteStartObject("knife");
        writer.WriteNumber("selected", team.Knife.Selected);
        writer.WriteStartObject("presets");
        foreach (var k in team.Knife.Presets)
        {
            writer.WriteStartObject(k.DefIndex.ToString());
            WriteCosmetic(writer, k.Preset);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartObject("gloves");
        writer.WriteBoolean("enabled", team.Gloves.Enabled);
        writer.WriteNumber("defindex", team.Gloves.DefIndex);
        writer.WriteNumber("paint", team.Gloves.Paint);
        writer.WriteNumber("seed", team.Gloves.Seed);
        writer.WritePropertyName("wear");
        Projection.InventorySimulatorProjector.WritePowerShellCompatibleDouble(writer, team.Gloves.Wear);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteCosmetic(Utf8JsonWriter writer, CosmeticPreset c)
    {
        writer.WriteNumber("paint", c.Paint);
        writer.WriteNumber("seed", c.Seed);
        writer.WritePropertyName("wear");
        Projection.InventorySimulatorProjector.WritePowerShellCompatibleDouble(writer, c.Wear);
        writer.WriteString("nameTag", c.NameTag ?? "");
        if (c.StatTrak is { } stattrak)
            writer.WriteNumber("statTrak", stattrak);
        else
            writer.WriteNull("statTrak");
    }

    private static TeamPreset ReadTeam(JsonProperty prop, string team, List<string> problems)
    {
        if (prop.Value.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{team} must be an object");
            return new TeamPreset
            {
                Weapons = [],
                Knife = new KnifeSection { Selected = 0, Presets = [] },
                Gloves = new GlovesPreset { Enabled = false },
            };
        }

        IReadOnlyList<DefIndexPreset>? weapons = null;
        KnifeSection? knife = null;
        GlovesPreset? gloves = null;

        foreach (var p in prop.Value.EnumerateObject())
        {
            switch (p.Name)
            {
                case "weapons":
                    weapons = ReadPresetMap(p, $"{team}.weapons", problems);
                    break;
                case "knife":
                    knife = ReadKnife(p, team, problems);
                    break;
                case "gloves":
                    gloves = ReadGloves(p, team, problems);
                    break;
                case "loadoutIdentity":
                    problems.Add($"{team}.loadoutIdentity is not part of HumanPreset v1 (weapon identity is decided by the CS2 loadout; legacy sharing info belongs in the migration report)");
                    break;
                default:
                    problems.Add($"unknown {team} property '{p.Name}'");
                    break;
            }
        }

        weapons ??= [];
        knife ??= new KnifeSection { Selected = 0, Presets = [] };
        gloves ??= new GlovesPreset { Enabled = false };
        return new TeamPreset { Weapons = weapons, Knife = knife, Gloves = gloves };
    }

    private static IReadOnlyList<DefIndexPreset> ReadPresetMap(JsonProperty prop, string path, List<string> problems)
    {
        if (prop.Value.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path} must be an object");
            return [];
        }

        var list = new List<DefIndexPreset>();
        foreach (var p in prop.Value.EnumerateObject())
        {
            if (!int.TryParse(p.Name, out var defIndex) || defIndex < 0)
            {
                problems.Add($"{path} key '{p.Name}' is not a numeric defindex");
                continue;
            }
            list.Add(new DefIndexPreset { DefIndex = defIndex, Preset = ReadCosmetic(p.Value, $"{path}[{p.Name}]", problems) });
        }
        return list.OrderBy(x => x.DefIndex).ToList();
    }

    private static KnifeSection ReadKnife(JsonProperty prop, string team, List<string> problems)
    {
        if (prop.Value.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{team}.knife must be an object");
            return new KnifeSection { Selected = 0, Presets = [] };
        }

        int? selected = null;
        IReadOnlyList<DefIndexPreset>? presets = null;
        foreach (var p in prop.Value.EnumerateObject())
        {
            switch (p.Name)
            {
                case "selected":
                    selected = ReadInt(p, $"{team}.knife.selected", problems);
                    break;
                case "presets":
                    presets = ReadPresetMap(p, $"{team}.knife.presets", problems);
                    break;
                default:
                    problems.Add($"unknown {team}.knife property '{p.Name}'");
                    break;
            }
        }

        presets ??= [];
        if (selected is null)
        {
            problems.Add($"{team}.knife.selected missing or not numeric");
            selected = 0;
        }
        else if (presets.All(x => x.DefIndex != selected.Value))
        {
            problems.Add($"{team}.knife.selected ({selected}) has no preset in {team}.knife.presets");
        }

        return new KnifeSection { Selected = selected.Value, Presets = presets };
    }

    private static GlovesPreset ReadGloves(JsonProperty prop, string team, List<string> problems)
    {
        if (prop.Value.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{team}.gloves must be an object");
            return new GlovesPreset { Enabled = false };
        }

        bool? enabled = null;
        int? defindex = null, paint = null, seed = null;
        double? wear = null;
        foreach (var p in prop.Value.EnumerateObject())
        {
            switch (p.Name)
            {
                case "enabled":
                    if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) enabled = p.Value.GetBoolean();
                    else problems.Add($"{team}.gloves.enabled must be a boolean");
                    break;
                case "defindex": defindex = ReadInt(p, $"{team}.gloves.defindex", problems); break;
                case "paint": paint = ReadInt(p, $"{team}.gloves.paint", problems); break;
                case "seed": seed = ReadInt(p, $"{team}.gloves.seed", problems); break;
                case "wear": wear = ReadDouble(p, $"{team}.gloves.wear", problems); break;
                default:
                    problems.Add($"unknown {team}.gloves property '{p.Name}'");
                    break;
            }
        }

        if (enabled is null) problems.Add($"{team}.gloves.enabled missing or not boolean");
        if (enabled == true)
        {
            if (defindex is null) problems.Add($"{team}.gloves.defindex required when gloves enabled");
            if (paint is null) problems.Add($"{team}.gloves.paint required when gloves enabled");
            if (seed is null) problems.Add($"{team}.gloves.seed required when gloves enabled");
            if (wear is null) problems.Add($"{team}.gloves.wear required when gloves enabled");
        }
        if (wear is { } w && (w < 0 || w > 1)) problems.Add($"{team}.gloves.wear out of range 0..1");

        return new GlovesPreset
        {
            Enabled = enabled ?? false,
            DefIndex = defindex ?? 0,
            Paint = paint ?? 0,
            Seed = seed ?? 0,
            Wear = wear ?? 0,
        };
    }

    private static CosmeticPreset ReadCosmetic(JsonElement element, string path, List<string> problems)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{path} must be an object");
            return new CosmeticPreset { Paint = 0, Seed = 0, Wear = 0 };
        }

        int? paint = null, seed = null;
        double? wear = null;
        string? nameTag = null;
        int? statTrak = null;

        foreach (var p in element.EnumerateObject())
        {
            switch (p.Name)
            {
                case "paint": paint = ReadInt(p, $"{path}.paint", problems); break;
                case "seed": seed = ReadInt(p, $"{path}.seed", problems); break;
                case "wear": wear = ReadDouble(p, $"{path}.wear", problems); break;
                case "nameTag":
                    if (p.Value.ValueKind == JsonValueKind.String) nameTag = p.Value.GetString();
                    else problems.Add($"{path}.nameTag must be a string");
                    break;
                case "statTrak":
                    statTrak = p.Value.ValueKind == JsonValueKind.Null ? null : ReadInt(p, $"{path}.statTrak", problems);
                    break;
                default:
                    problems.Add($"unknown cosmetic property '{p.Name}' at {path}");
                    break;
            }
        }

        if (paint is null) problems.Add($"{path}.paint missing");
        if (seed is null) problems.Add($"{path}.seed missing");
        if (wear is null) problems.Add($"{path}.wear missing");
        if (paint is { } pi && pi < 0) problems.Add($"{path}.paint must be a non-negative integer");
        if (statTrak is { } st && st < 0) problems.Add($"{path}.statTrak must be null or a non-negative integer");
        if (wear is { } w && (w < 0 || w > 1)) problems.Add($"{path}.wear out of range 0..1");

        return new CosmeticPreset
        {
            Paint = paint ?? 0,
            Seed = seed ?? 0,
            Wear = wear ?? 0,
            NameTag = nameTag ?? "",
            StatTrak = statTrak,
        };
    }

    private static string ReadString(JsonProperty prop)
        => prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : "";

    private static int? ReadInt(JsonProperty prop, string path, List<string> problems)
    {
        if (prop.Value.ValueKind is JsonValueKind.Number)
        {
            if (prop.Value.TryGetInt32(out var i)) return i;
            problems.Add($"{path} must be an integer");
            return null;
        }
        problems.Add($"{path} must be a number");
        return null;
    }

    private static double? ReadDouble(JsonProperty prop, string path, List<string> problems)
    {
        if (prop.Value.ValueKind == JsonValueKind.Number)
        {
            prop.Value.TryGetDouble(out var d);
            return d;
        }
        problems.Add($"{path} must be a number");
        return null;
    }
}

/// <summary>Thrown when a preset file does not conform to HumanPreset v1.</summary>
public sealed class HumanPresetFormatException : Exception
{
    public HumanPresetFormatException(string message) : base(message) { }
}
