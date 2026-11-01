using System.Text.Json;

static class Check
{
    public static void That(bool condition) { if (!condition) throw new Exception("Performance behavior check failed"); }
    static void Main()
    {
        FlashChecks.Run(); NadeChecks.Run();
        var allocations = PoolChecks.Run();
        Console.WriteLine(JsonSerializer.Serialize(new { managedCacheChecks = "PASS", poolAllocationBytes = allocations,
            measurementScope = "Synthetic selection loop only; not game FPS or frame pacing" }));
    }
}

class CEntityInstance { }
class CBaseEntity : CEntityInstance
{
    public bool IsValid = true;
    public CHandle<CBaseEntity> EntityHandle;
    public CBaseEntity(uint raw) { EntityHandle = new(raw); Utilities.Entities[raw] = this; }
}
class CCSPlayerController(uint raw) : CBaseEntity(raw) { }
readonly record struct CHandle<T>(uint Raw) where T : CBaseEntity
{
    public T? Value => Utilities.Entities.GetValueOrDefault(Raw) as T;
}
static class Utilities
{
    public static Dictionary<uint,CBaseEntity> Entities = [];
    public static List<CBaseEntity> Flashes = [], Controllers = [];
    public static int Calls;
    public static IEnumerable<T> FindAllEntitiesByDesignerName<T>(string name) where T : CBaseEntity
    {
        Calls++; return (name == "flashbang_projectile" ? Flashes : Controllers).Cast<T>();
    }
}
partial class FlashChecks
{
    private readonly List<CHandle<CBaseEntity>> _flashProjectiles = new();
    private readonly List<CHandle<CCSPlayerController>> _flashControllers = new();
    private bool _flashEntitiesDirty = true;
    public static void Run()
    {
        var c = new FlashChecks(); c.RefreshFlashEntities();
        Check.That(Utilities.Calls == 1 && c._flashControllers.Count == 0);
        for (int i=0;i<1000;i++) c.RefreshFlashEntities();
        Check.That(Utilities.Calls == 1);
        Utilities.Flashes = [new(30), new(10), new(20) { IsValid=false }];
        Utilities.Controllers = [new CCSPlayerController(5), new CCSPlayerController(2)];
        c.InvalidateFlashEntities(new()); c.RefreshFlashEntities();
        Check.That(Utilities.Calls == 3 && c._flashProjectiles.Select(h=>h.Raw).SequenceEqual(new uint[]{30,10}));
        Check.That(c._flashControllers.Select(h=>h.Raw).SequenceEqual(new uint[]{5,2}));
        Utilities.Entities.Remove(30); Check.That(c._flashProjectiles[0].Value is null);
        Utilities.Flashes.Clear(); c.ClearFlashEntities(); c.RefreshFlashEntities();
        Check.That(Utilities.Calls == 4 && c._flashControllers.Count == 0 && c._flashProjectiles.Count == 0);
    }
}

record GrenadeData(string Id, string GrenadeType, float ZoneX=0, float ZoneY=0, float ZoneRadius=10);
partial class NadeChecks
{
    private List<GrenadeData> _mapNades = new();
    private readonly Dictionary<(string Id, string? Type), GrenadeData> _mapNadesById = new();
    private const float GrenadeZoneGridSize = 256f;
    private Dictionary<(int X, int Y), List<GrenadeData>> _grenadeZoneGrid = new();
    public static void Run()
    {
        var c = new NadeChecks();
        c._mapNades = [new("same","flash",-1,-1), new("same","smoke"), new("same","smoke"), new("other","decoy")];
        c.BuildGrenadeIndexes();
        foreach(var id in new[]{"same","other","missing"})
            foreach(var type in new string?[]{null,"flash","smoke","decoy"})
                Check.That(ReferenceEquals(c.FindMapGrenade(id,type), c._mapNades.FirstOrDefault(g=>g.Id==id && (type is null || g.GrenadeType==type))));
        Check.That(GetGrenadeZoneGridCell(-1,-257) == (-1,-2));
        foreach(var cell in c._grenadeZoneGrid)
        {
            var expected = c._mapNades.Where(g=> {
                var r=g.GrenadeType=="decoy" ? 200f:g.ZoneRadius;
                var min=GetGrenadeZoneGridCell(g.ZoneX-r,g.ZoneY-r);var max=GetGrenadeZoneGridCell(g.ZoneX+r,g.ZoneY+r);
                return cell.Key.X>=min.X && cell.Key.X<=max.X && cell.Key.Y>=min.Y && cell.Key.Y<=max.Y;
            });
            Check.That(cell.Value.SequenceEqual(expected));
        }
        c._mapNades = [new("next-map","smoke")]; c.BuildGrenadeIndexes();
        Check.That(c.FindMapGrenade("same") is null && c.FindMapGrenade("next-map") is not null);
        c._mapNades.Clear(); c.BuildGrenadeIndexes(); Check.That(c._grenadeZoneGrid.Count==0 && c._mapNadesById.Count==0);
    }
}

record PaintCatalogEntry(int Id, int Rarity);
record KnifePaintCatalogEntry(int Id, string Finish);
static class RandomizerAssets { public static int GetWeaponRarityWeight(int rarity) => rarity+1; }
class Catalog { public int GetKnifeFinishWeight(string finish) => finish=="A" ? 1:3; }
partial class PoolChecks
{
    private readonly Random _random = new(123);
    private readonly Catalog _catalog = new();
    private readonly Dictionary<IReadOnlyList<PaintCatalogEntry>, IReadOnlyList<PaintCatalogEntry>[]> _weaponRarityPools = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IReadOnlyList<KnifePaintCatalogEntry>, IReadOnlyList<KnifePaintCatalogEntry>[]> _knifeFinishPools = new(ReferenceEqualityComparer.Instance);
    public static object Run()
    {
        var cached=new PoolChecks();var original=new PoolChecks();
        PaintCatalogEntry[] guns=[new(1,2),new(2,1),new(3,2)];
        KnifePaintCatalogEntry[] knives=[new(1,"B"),new(2,"A"),new(3,"B")];
        for(int i=0;i<1000;i++)
        {
            Check.That(cached.PickWeaponPaint(guns)==original.OldWeapon(guns));
            Check.That(cached.PickKnifePaint(knives)==original.OldKnife(knives));
        }
        var pool=cached._weaponRarityPools[guns]; cached.PickWeaponPaint(guns);
        Check.That(ReferenceEquals(pool,cached._weaponRarityPools[guns]));
        cached.PickWeaponPaint(guns.ToArray()); Check.That(cached._weaponRarityPools.Count==2 && cached._knifeFinishPools.Count==1);
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++) { cached.PickWeaponPaint(guns); cached.PickKnifePaint(knives); }
        long afterCache=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++) { original.OldWeapon(guns); original.OldKnife(knives); }
        long afterOld=GC.GetAllocatedBytesForCurrentThread();
        Check.That(afterCache-allocated < afterOld-afterCache);
        return new { iterations=10000, cached=afterCache-allocated, original=afterOld-afterCache };
    }
}
