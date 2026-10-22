using System.Collections.Immutable;
using CS2LocalKit.Core.MatchSetup;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class MatchSetupDomainTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "match-domain-" + Guid.NewGuid().ToString("N"));
    private readonly RosterCatalog _catalog = RosterCatalog.LoadBuiltIn();
    private MatchSetupStore Store => new(_root, _catalog);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private MatchSetupPreset Pro()
    {
        var roster = _catalog.Rosters.Single(r => r.Id == "team-13-2024-03-31");
        return MatchSetupPreset.Default() with { Friendly = _catalog.Select(roster), Enemy = _catalog.Select(roster) };
    }
    [Fact] public void PinnedCatalogHasFortyTeamsAndSourcedHistory()
    {
        Assert.Equal(40, _catalog.Teams.Length); Assert.Equal(25, _catalog.Teams.Count(t => t.Recommended));
        Assert.Equal(59, _catalog.Rosters.Length); Assert.Equal(4861, _catalog.Players.Count(p => p.Enabled));
        Assert.Equal(515, _catalog.Players.Count(p => !p.Enabled));
        Assert.All(_catalog.Rosters, r => { Assert.Equal(5, r.Slots.Length); Assert.StartsWith("https://", r.Source); Assert.True(DateOnly.TryParse(r.RepresentativeDate, out _)); });
        Assert.All(_catalog.Players.Where(p => p.Enabled), p => Assert.Equal(76561197960265728UL + uint.Parse(p.PersonaId), p.SteamId64));
    }
    [Theory] [InlineData(MatchSide.CT)] [InlineData(MatchSide.T)]
    public void HumanReplacesExactlyOneSeatOnEitherSide(MatchSide side)
    {
        var p = Pro() with { HumanSide = side, ReplacedSlot = 2 };
        var result = _catalog.Resolve(p);
        Assert.Equal(9, result.Bots.Length); Assert.Equal(5, result.CtCount); Assert.Equal(5, result.TCount);
        Assert.DoesNotContain(result.Bots, b => b.Side == side && b.Seat == 2);
    }
    [Theory] [InlineData(0,0)] [InlineData(1,2)] [InlineData(4,5)]
    public void CountsNeverProduceAnExtraHumanSeat(int friendly, int enemy)
    {
        var result = _catalog.Resolve(Pro() with { FriendlyBots = friendly, EnemyBots = enemy });
        Assert.Equal(friendly + enemy + 1, result.CtCount + result.TCount);
    }
    [Fact] public void SameTeamAndTransferredPlayersDoNotDuplicatePersona()
    {
        var r = _catalog.Resolve(Pro()); var ids = r.Bots.Where(b => b.PersonaId is not null).Select(b => b.PersonaId).ToArray();
        Assert.Equal(ids.Distinct().Count(), ids.Length); Assert.Contains(r.Warnings, s => s.Contains("重复 persona"));
        Assert.Equal(r.Bots.Length, r.Bots.Select(b => b.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var vitality = _catalog.Rosters.Single(v => v.Id == "team-2-2025-05-04");
        var mixed = _catalog.Resolve(Pro() with { Enemy = _catalog.Select(vitality), ReplacedSlot = 0 });
        Assert.Contains(mixed.Warnings, s => s.Contains("ropz") && s.Contains("重复 persona"));
    }
    [Fact] public void MissingNamesAndDisabledIdsFallBackWithoutInventingPersona()
    {
        var disabled = _catalog.Players.First(p => !p.Enabled);
        var slots = Pro().Enemy.Slots.SetItem(0,new("Techno")).SetItem(1,new("my custom bot")).SetItem(2,new("disabled slot",disabled.PersonaId));
        var result = _catalog.Resolve(Pro() with { Enemy = Pro().Enemy with { Slots = slots } });
        Assert.All(result.Bots.Where(b => b.Side == MatchSide.T && b.Seat < 3), b => Assert.Null(b.PersonaId));
        Assert.Contains(result.Warnings,s=>s.Contains("disabled"));
    }
    [Fact] public void AmbiguousNicknamesRequireExplicitIds()
    {
        var group = _catalog.Players.Where(p=>p.Enabled).GroupBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).First(g=>g.Count()>1);
        var p = Pro(); var slots=p.Enemy.Slots.SetItem(0,new(group.Key));
        var a = _catalog.Resolve(p with { Enemy=p.Enemy with { Slots=slots } });
        Assert.Null(a.Bots.Single(b=>b.Side==MatchSide.T&&b.Seat==0).PersonaId);
        var selected=group.First(); slots=slots.SetItem(0,new(group.Key, selected.PersonaId));
        var b = _catalog.Resolve(p with { Enemy=p.Enemy with { Slots=slots } });
        Assert.Equal(selected.PersonaId,b.Bots.Single(b=>b.Side==MatchSide.T&&b.Seat==0).PersonaId);
    }
    [Theory] [InlineData(-1,5,0)] [InlineData(5,5,0)] [InlineData(4,6,0)] [InlineData(4,5,5)]
    public void InvalidCountsAndReplacementRefuse(int friendly,int enemy,int replaced) => Assert.Throws<InvalidDataException>(()=>_catalog.Resolve(Pro() with { FriendlyBots=friendly,EnemyBots=enemy,ReplacedSlot=replaced }));
    [Theory] [InlineData("bad;bot_kick")] [InlineData("bad\nname")] [InlineData("bad\"name")] [InlineData("一二三四五六七八九十一二")]
    public void NamesCannotInjectCommandsOrOverflowBotHider(string value)
    {
        var p=Pro(); Assert.Throws<InvalidDataException>(()=>_catalog.Resolve(p with { Enemy=p.Enemy with { Slots=p.Enemy.Slots.SetItem(0,new(value)) } }));
    }
    [Fact] public void PresetsCustomRostersFavoritesRecentAndLastPersistSeparately()
    {
        var p=Pro() with { Favorite=true }; var custom=new CustomRoster("custom-1","自由组队",p.Friendly.Slots,p.Friendly.RosterId);
        Store.Save(new(1,[custom],[p],[p.Id],p));
        var loaded=Store.Load();Assert.Equal(p.Id,loaded.LastUsed!.Id);Assert.True(loaded.Presets[0].Favorite);Assert.Single(loaded.CustomRosters);
        var q=Store.Queue(p);Assert.Equal(q,Store.ReadRequest()! with { Preset=q.Preset });
        Assert.Equal(MatchSetupStore.Digest(p),q.PresetSha256);
    }
    [Fact] public void ImportMergesIdenticalIdsAndRejectsConflictsWithoutChanges()
    {
        var p=Pro();Store.Save(new(1,[],[p],[])); var json=Store.Export(); Store.Import(json);Assert.Single(Store.Load().Presets);
        Assert.Throws<InvalidDataException>(()=>Store.Import(MatchJson.Write(new MatchLibrary(1,[],[p with { Name="conflict" }],[]))));
        Assert.Equal(json,Store.Export());
    }
    [Fact] public void BuiltInReadOnlyAndFutureVersionCannotOverwriteOrGuessMigration()
    {
        var p=Pro(); Store.Save(new(1,[],[p],[]));var before=Store.Export();
        Assert.Throws<InvalidDataException>(()=>Store.Save(new(1,[new(p.Friendly.RosterId!,"illegal",p.Friendly.Slots)],[p],[])));
        Assert.Throws<InvalidDataException>(()=>Store.Import(before.Replace("\"schemaVersion\": 1","\"schemaVersion\": 9")));
        Assert.Equal(before,Store.Export());
    }
    [Fact] public void SavingCreatesRecoverableBackupAndTamperedQueueFails()
    {
        Store.Save(MatchLibrary.Empty); var first=Store.Export();Store.Save(new(1,[],[Pro()],[]));
        Assert.Equal(first,File.ReadAllText(Directory.GetFiles(_root,"library.json.backup-*").Single()));
        Store.Queue(Pro()); File.WriteAllText(Store.RequestPath,File.ReadAllText(Store.RequestPath).Replace("\"friendlyBots\": 4","\"friendlyBots\": 3"));
        Assert.Throws<InvalidDataException>(()=>Store.ReadRequest());
    }
    [Fact] public void CorruptDataAndDuplicateIdsArePreservedForRecovery()
    {
        Directory.CreateDirectory(_root);File.WriteAllText(Store.LibraryPath,"corrupt");
        Assert.ThrowsAny<Exception>(()=>Store.Load());Assert.Equal("corrupt",File.ReadAllText(Store.LibraryPath));
        var p=Pro();Assert.Throws<InvalidDataException>(()=>Store.Save(new(1,[],[p,p],[])));
    }
}
