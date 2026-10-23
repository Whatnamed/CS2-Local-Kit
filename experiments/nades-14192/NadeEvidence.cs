using System;
using System.Collections.Generic;
using System.Linq;

namespace NadeSystem;

// Counters describe this plugin's factories, not Valve's autonomous grenade AI.
// Game-thread only. No timers, native objects, file writes, or per-tick logging.
public sealed class NadeEvidence
{
    public sealed record Ticket(long Epoch, string Type, string Route, string Id, uint Bot);
    public sealed class Counts
    {
        public long Attempts { get; set; }
        public long Created { get; set; }
        public long Failed { get; set; }
    }
    private readonly Dictionary<string, Counts> _types = new();
    private readonly Dictionary<string, long> _skips = new();
    private readonly Dictionary<string, long> _failures = new();
    public long Epoch { get; private set; }
    public string Mode { get; private set; } = "normal";
    public string Session { get; private set; } = Guid.NewGuid().ToString("N");
    public IReadOnlyDictionary<string, Counts> ByType => _types;
    public IReadOnlyDictionary<string, long> Skips => _skips;
    public IReadOnlyDictionary<string, long> Failures => _failures;
    public long Count => _types.Values.Sum(v => v.Created);
    public NadeEvidence() => Reset("normal", true);
    public void Reset(string mode, bool newMap = false)
    {
        if (mode is not ("off" or "less" or "normal" or "more" or "max"))
            throw new ArgumentException("Invalid nade mode");
        Epoch++;
        Mode = mode;
        if (newMap) Session = Guid.NewGuid().ToString("N");
        _types.Clear(); _skips.Clear(); _failures.Clear();
        foreach (var type in new[] { "flash", "he", "molotov", "smoke", "decoy", "other" })
            _types[type] = new Counts();
    }
    private string TypeKey(string type) => type == "incgrenade" ? "molotov" : _types.ContainsKey(type) ? type : "other";
    private static void Increment(Dictionary<string, long> table, string reason)
    {
        // Reasons are fixed call-site labels; cap cardinality even for exceptions.
        if (!table.ContainsKey(reason) && table.Count >= 48) reason = "other";
        table.TryGetValue(reason, out var count); table[reason] = count + 1;
    }
    public bool Skip(string reason) { Increment(_skips, reason); return true; }
    public Ticket Begin(string type, string route, string id, uint bot)
    {
        var key = TypeKey(type); _types[key].Attempts++;
        return new Ticket(Epoch, key, route, id, bot);
    }
    public bool CanCreate(Ticket ticket)
    {
        if (ticket.Epoch == Epoch && Mode != "off") return true;
        Skip("queued_mode_or_map_changed"); return false;
    }
    public void Created(Ticket ticket)
    {
        if (ticket.Epoch == Epoch) _types[ticket.Type].Created++;
    }
    public void Failed(Ticket ticket, string reason)
    {
        if (ticket.Epoch != Epoch) return;
        _types[ticket.Type].Failed++; Increment(_failures, ticket.Type + ":" + reason);
    }
}
