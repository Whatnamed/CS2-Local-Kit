using System;
using System.Linq;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;

namespace NadeSystem;

public partial class NadeSystemPlugin
{
    private readonly NadeEvidence _evidence = new();
    private bool Skip(string reason) => _evidence.Skip(reason);
    private string EvidenceJson(string reason) => JsonSerializer.Serialize(new {
        Kind = "localkit-nades-v2", Reason = reason, Map = Server.MapName,
        _evidence.Session, _evidence.Epoch, Mode = _botNadesMode,
        Count = _evidence.Count, DatabaseCount = _db.Count, MapCount = _mapNades.Count,
        MapTypes = _mapNades.GroupBy(g => g.GrenadeType).ToDictionary(g => g.Key, g => g.Count()),
        DataPath = DataDir, ByType = _evidence.ByType, Skips = _evidence.Skips,
        Failures = _evidence.Failures,
        Scope = "NadeSystem projectile creation only; detonation and Valve AI require observation"
    });
    private void LogEvidence(string reason) => Logger.LogInformation("{NadeEvidence}", EvidenceJson(reason));
    private void SetEvidenceMode(string mode)
    {
        if (mode is not ("off" or "less" or "normal" or "more" or "max"))
            throw new ArgumentException("Invalid nade mode");
        LogEvidence("mode-ending");
        _evidence.Reset(mode); _botNadesMode = mode;
        LogEvidence("mode-started");
    }
    private void RecordCreated(NadeEvidence.Ticket ticket, CBaseEntity entity)
    {
        if (!entity.IsValid) { _evidence.Failed(ticket, "invalid_after_spawn"); return; }
        _evidence.Created(ticket);
        // Bounded event logging: first three creations of each type per mode/map.
        if (_evidence.ByType[ticket.Type].Created <= 3)
            Logger.LogInformation("[localkit-nades-created] {Evidence}", JsonSerializer.Serialize(new {
                _evidence.Session, ticket.Epoch, Mode = _botNadesMode, ticket.Type,
                ticket.Route, ticket.Id, ticket.Bot, Entity = entity.Index,
                DesignerName = entity.DesignerName
            }));
    }
    private void RegisterLocalKitDiagnostics()
    {
        // Retain the existing match-setup capability contract without installing its adapter.
        Capabilities.RegisterPluginCapability(new PluginCapability<Tuple<Action<string>, Func<string>>>("localkit:nades:v1"),
            () => Tuple.Create<Action<string>, Func<string>>(SetEvidenceMode, () => EvidenceJson("capability-read")));
        AddCommand("css_localkit_nades_status", "Read actual NadeSystem counters", (player, info) => {
            info.ReplyToCommand(EvidenceJson("status")); LogEvidence("status");
        });
        AddCommand("css_localkit_nades_points", "Read current-map lineups: [type|exact-id]", (player, info) => {
            var filter = info.ArgCount > 1 ? info.GetArg(1) : "";
            var rows = _mapNades.Where(g => filter == "" || g.GrenadeType == filter || g.Id == filter).ToList();
            info.ReplyToCommand($"[NadeSystem] map={Server.MapName}; matching={rows.Count}; showing first 12; read-only");
            foreach (var g in rows.Take(12))
            {
                info.ReplyToCommand(JsonSerializer.Serialize(new {
                    g.Id, g.GrenadeType, g.Description, g.TeamTag, g.ProjectilePosition,
                    g.ProjectileVelocity, g.LandingPosition, g.ZoneRadius,
                    BotFeetZ = g.ProjectilePosition.Z - 64f,
                    ThrowYaw = MathF.Atan2(g.ProjectileVelocity.Y, g.ProjectileVelocity.X) * 180f / MathF.PI
                }));
                if (filter != g.Id) continue;
                foreach (var bot in Utilities.GetPlayers().Where(p => p.IsValid && p.IsBot).Take(12))
                {
                    var pawn = bot.PlayerPawn?.Value;
                    var pos = pawn?.AbsOrigin;
                    if (pawn == null || !pawn.IsValid || pos == null) continue;
                    float dx = pos.X - g.ZoneX, dy = pos.Y - g.ZoneY;
                    float yaw = pawn.EyeAngles.Y * MathF.PI / 180f;
                    float dot = MathF.Cos(yaw) * g.ProjectileVelocity.X + MathF.Sin(yaw) * g.ProjectileVelocity.Y;
                    _roundNadeMoneyPerBot.TryGetValue((uint)bot.Index, out var budget);
                    info.ReplyToCommand(JsonSerializer.Serialize(new {
                        bot.Slot, bot.PlayerName, bot.TeamNum, bot.PawnIsAlive,
                        bot.HasBeenControlledByPlayerThisRound, NativeBot = pawn.Bot != null,
                        Position = new { pos.X, pos.Y, pos.Z }, DistanceXY = MathF.Sqrt(dx*dx + dy*dy),
                        HeightDelta = pos.Z + 64f - g.ProjectilePosition.Z, FacingDot = dot,
                        Money = bot.InGameMoneyServices?.Account, FreezeEndBudget = budget
                    }));
                }
            }
        });
    }
}
