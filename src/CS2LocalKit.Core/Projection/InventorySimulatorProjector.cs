using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CS2LocalKit.Core.HumanPresets;

namespace CS2LocalKit.Core.Projection;

/// <summary>
/// Deterministic projection HumanPreset + SteamID64 -> InventorySimulator EquippedV5.
///
/// Ported 1:1 from the C2-accepted PowerShell projector (Convert-HumanPresetToInventorySimulator.ps1):
/// CT -> team byte 3, T -> 2; weapons keyed by their REAL defindex (identity is never
/// rewritten); the selected knife identity per team; gloves only when enabled; musicKit
/// by id. uid is a stable 1..n sequence over the fixed iteration order, hash is
/// "hp1-" + first 16 hex chars of SHA-256 over the preset coordinate string. No random,
/// no clock - the same preset + SteamID always produces byte-identical output.
/// </summary>
public sealed class InventorySimulatorProjector
{
    public const string TeamCt = "3";
    public const string TeamT = "2";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Project(HumanPreset preset, string steamId64)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(steamId64, @"^\d{17}$"))
            throw new ArgumentException($"SteamID64 '{steamId64}' is not a 17-digit id.", nameof(steamId64));

        var uid = 0;

        OrderedItem NextItem(List<KeyValuePair<string, object?>> fields, string coordinate)
        {
            uid++;
            return new OrderedItem(fields, uid, "hp1-" + Hash16(coordinate));
        }

        var ctWeapons = new List<KeyValuePair<string, OrderedItem>>();
        var tWeapons = new List<KeyValuePair<string, OrderedItem>>();
        var knives = new List<KeyValuePair<string, OrderedItem>>();
        var gloves = new List<KeyValuePair<string, OrderedItem>>();
        OrderedItem? musicKit = null;

        foreach (var (team, teamByte, teamPreset) in new[]
                 {
                     (Team: "ct", Byte: TeamCt, Preset: preset.Ct),
                     (Team: "t", Byte: TeamT, Preset: preset.T),
                 })
        {
            foreach (var w in teamPreset.Weapons)
            {
                var item = WeaponItem($"{team}/weapon/{w.DefIndex}", w.DefIndex, w.Preset, NextItem);
                (team == "ct" ? ctWeapons : tWeapons).Add(new(w.DefIndex.ToString(), item));
            }

            var selected = teamPreset.Knife.Presets.Single(x => x.DefIndex == teamPreset.Knife.Selected);
            knives.Add(new(teamByte, WeaponItem($"{team}/knife/{selected.DefIndex}", selected.DefIndex, selected.Preset, NextItem)));

            if (teamPreset.Gloves.Enabled)
            {
                var g = teamPreset.Gloves;
                var fields = new List<KeyValuePair<string, object?>>
                {
                    new("def", g.DefIndex),
                    new("paint", g.Paint),
                    new("seed", g.Seed),
                    new("wear", g.Wear),
                };
                gloves.Add(new(teamByte, NextItem(fields, $"{team}/gloves")));
            }
        }

        if (preset.MusicKitId is { } musicId)
        {
            var fields = new List<KeyValuePair<string, object?>> { new("musicId", musicId), new("stattrak", -1) };
            musicKit = NextItem(fields, $"music/{musicId}");
        }

        // Serialize. OrderedItem preserves field/insertion order; writer options match the
        // accepted C2 fixture: 2-space indent, CRLF, literal non-ASCII, trailing CRLF.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartObject(steamId64);

            writer.WriteStartObject("ctWeapons");
            foreach (var (key, item) in ctWeapons) WriteItem(writer, key, item);
            writer.WriteEndObject();

            writer.WriteStartObject("tWeapons");
            foreach (var (key, item) in tWeapons) WriteItem(writer, key, item);
            writer.WriteEndObject();

            writer.WriteStartObject("knives");
            foreach (var (key, item) in knives) WriteItem(writer, key, item);
            writer.WriteEndObject();

            writer.WriteStartObject("gloves");
            foreach (var (key, item) in gloves) WriteItem(writer, key, item);
            writer.WriteEndObject();

            writer.WritePropertyName("musicKit");
            if (musicKit is { } mk)
            {
                writer.WriteStartObject();
                WriteOrderedItem(writer, mk);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        var text = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n").Replace("\n", "\r\n");
        return text + "\r\n";
    }

    private static OrderedItem WeaponItem(string coordinate, int defIndex, CosmeticPreset preset, Func<List<KeyValuePair<string, object?>>, string, OrderedItem> nextItem)
    {
        var fields = new List<KeyValuePair<string, object?>>
        {
            new("def", defIndex),
            new("paint", preset.Paint),
            new("seed", preset.Seed),
            new("wear", preset.Wear),
            new("nametag", preset.NameTag ?? ""),
            new("stattrak", preset.StatTrak ?? -1),
        };
        return nextItem(fields, coordinate);
    }

    private static void WriteItem(Utf8JsonWriter writer, string key, OrderedItem item)
    {
        writer.WriteStartObject(key);
        WriteOrderedItem(writer, item);
        writer.WriteEndObject();
    }

    private static void WriteOrderedItem(Utf8JsonWriter writer, OrderedItem item)
    {
        foreach (var (name, value) in item.Fields)
        {
            writer.WritePropertyName(name);
            switch (value)
            {
                case int i: writer.WriteNumberValue(i); break;
                case double d: WritePowerShellCompatibleDouble(writer, d); break;
                case string s: writer.WriteStringValue(s); break;
                case null: writer.WriteNullValue(); break;
                default: throw new InvalidOperationException($"unsupported projection value type {value.GetType()} for '{name}'");
            }
        }
        writer.WriteNumber("uid", item.Uid);
        writer.WriteString("hash", item.Hash);
    }

    /// <summary>
    /// PowerShell ConvertTo-Json (the C2-accepted serialization) writes integral doubles
    /// as "0.0", while System.Text.Json writes "0" - force the PS-compatible form so the
    /// .NET projection stays byte-identical with the accepted fixture.
    /// </summary>
    internal static void WritePowerShellCompatibleDouble(Utf8JsonWriter writer, double value)
    {
        if (double.IsFinite(value) && value % 1 == 0)
            writer.WriteRawValue(value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        else
            writer.WriteRawValue(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string Hash16(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    /// <summary>Preserves field insertion order for deterministic output.</summary>
    private sealed class OrderedItem(List<KeyValuePair<string, object?>> fields, int uid, string hash)
    {
        public List<KeyValuePair<string, object?>> Fields { get; } = fields;
        public int Uid { get; } = uid;
        public string Hash { get; } = hash;
    }
}
