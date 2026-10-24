using NadeSystem;
using System.Text.Json;

static void Check(bool condition, string reason)
{
    if (!condition) throw new Exception(reason);
}

var e = new NadeEvidence();
var session = e.Session;
e.Reset("max");
var pending = e.Begin("smoke", "lineup", "real-id", 1);
Check(e.Count == 0, "Request/command feedback cannot imply creation");
e.Failed(pending, "null_or_invalid_entity");
Check(e.Count == 0 && e.ByType["smoke"].Attempts == 1 && e.ByType["smoke"].Failed == 1,
    "A failed native factory must not count as Created");
foreach (var type in new[] { "flash", "he", "molotov", "smoke" })
{
    var ticket = e.Begin(type, "lineup", "real-id", 1);
    Check(e.CanCreate(ticket), "Current Max request should proceed");
    e.Created(ticket);
    Check(e.ByType[type].Created == 1, "Type attribution");
}
Check(e.Count == 4, "Four successful entities, excluding failure");
e.Skip("no_live_enemy"); e.Skip("freeze_end_budget");
Check(e.Count == 4 && e.Skips["no_live_enemy"] == 1, "Rejections cannot imply creation");
var queued = e.Begin("flash", "instant", "special", 1);
var oldEpoch = e.Epoch;
e.Reset("off");
Check(e.Session == session && e.Epoch > oldEpoch && e.Count == 0, "Mode epoch reset, same map");
Check(!e.CanCreate(queued), "Queued Max callback must not create after switching Off");
e.Created(queued); e.Failed(queued, "stale");
Check(e.Count == 0 && e.ByType["flash"].Failed == 0, "Prior epoch cannot contaminate Off counters");
Check(!e.CanCreate(e.Begin("smoke", "lineup", "id", 1)), "Even direct requests cannot create in Off");
e.Reset("max", true);
Check(e.Session != session && e.Count == 0, "Map session must change and reset");
var inc = e.Begin("incgrenade", "lineup", "id", 1);e.Created(inc);
Check(e.ByType["molotov"].Created == 1, "CT incendiary belongs to fire category");
var beforeInvalid = JsonSerializer.Serialize(new { e.Epoch, e.Mode, e.Count });
try { e.Reset("invalid");throw new Exception("Expected validation rejection"); }
catch (ArgumentException) { }
Check(beforeInvalid == JsonSerializer.Serialize(new { e.Epoch, e.Mode, e.Count }), "Invalid mode must retain counters");
for (int i = 0; i < 1000; i++) e.Skip("reason-" + i);
Check(e.Skips.Count <= 49 && e.Skips.Values.Sum() == 1000, "Reason cardinality and totals are bounded");
var json = JsonSerializer.Serialize(new { ByType = e.ByType, e.Skips, e.Failures });
Check(json.Contains("\"Created\":1") && json.Contains("molotov"), "Structured diagnostics retain evidence");
Console.WriteLine("PASS: factory failures, four types, skip/command separation, Off queue cancellation, map/mode epochs, CT fire, invalid mode, bounded counters and JSON");
