using System.Text.Json;

namespace CS2LocalKit.Core.Runtime;

/// <summary>Machine-readable runtime pin (runtime/inventory-simulator.lock.json).</summary>
public sealed class InventorySimulatorLock
{
    public required string Repository { get; init; }
    public required string Ref { get; init; }
    public required string Tag { get; init; }
    public required string Status { get; init; }
    public required string? PatchVersion { get; init; }
    public required string? ClientVersion { get; init; }
    public required string? BuildId { get; init; }
    public required string? PatchedDllSha256 { get; init; }
    public required string MetaModVersion { get; init; }
    public required string CounterStrikeSharpVersion { get; init; }
    public string? CandidatePatchVersion { get; init; }
    public string? CandidateClientVersion { get; init; }
    public string? CandidateBuildId { get; init; }
    public string? CandidateMetaModVersion { get; init; }
    public string? CandidateCounterStrikeSharpVersion { get; init; }
    public IReadOnlyDictionary<string, string> CandidateMetaModFiles { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> CandidateCounterStrikeSharpFiles { get; init; } = new Dictionary<string, string>();

    public static InventorySimulatorLock Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;

        string? patch = null, client = null, buildId = null;
        if (r.TryGetProperty("testedCs2Build", out var build) && build.ValueKind == JsonValueKind.Object)
        {
            patch = build.TryGetProperty("patchVersion", out var pv) ? pv.GetString() : null;
            client = build.TryGetProperty("clientVersion", out var cv) ? cv.GetString() : null;
            buildId = build.TryGetProperty("buildId", out var bi) ? bi.GetString() : null;
        }

        string? patchedDll = null;
        if (r.TryGetProperty("acceptedRuntime", out var ar) && ar.ValueKind == JsonValueKind.Object
            && ar.TryGetProperty("patchedDllSha256", out var pd))
            patchedDll = pd.GetString();

        string metamod = "", css = "";
        if (r.TryGetProperty("framework", out var fw) && fw.ValueKind == JsonValueKind.Object)
        {
            if (fw.TryGetProperty("metamod", out var mm) && mm.TryGetProperty("version", out var mv))
                metamod = mv.GetString() ?? "";
            if (fw.TryGetProperty("counterstrikesharp", out var cs) && cs.TryGetProperty("version", out var cv2))
                css = cv2.GetString() ?? "";
        }

        JsonElement candidate = default, candidateBuild = default, candidateMm = default, candidateCss = default;
        if (r.TryGetProperty("compatibilityCandidate", out candidate)
            && candidate.TryGetProperty("status", out var candidateStatus) && candidateStatus.GetString() == "candidate")
        {
            candidate.TryGetProperty("targetCs2Build", out candidateBuild);
            if (candidate.TryGetProperty("framework", out var candidateFramework))
            {
                candidateFramework.TryGetProperty("metamod", out candidateMm);
                candidateFramework.TryGetProperty("counterstrikesharp", out candidateCss);
            }
        }

        return new InventorySimulatorLock
        {
            Repository = r.TryGetProperty("repository", out var repo) ? repo.GetString() ?? "" : "",
            Ref = r.TryGetProperty("ref", out var rf) ? rf.GetString() ?? "" : "",
            Tag = r.TryGetProperty("tag", out var tg) ? tg.GetString() ?? "" : "",
            Status = r.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
            PatchVersion = patch,
            ClientVersion = client,
            BuildId = buildId,
            PatchedDllSha256 = patchedDll,
            MetaModVersion = metamod,
            CounterStrikeSharpVersion = css,
            CandidatePatchVersion = OptionalString(candidateBuild, "patchVersion"),
            CandidateClientVersion = OptionalString(candidateBuild, "clientVersion"),
            CandidateBuildId = OptionalString(candidateBuild, "buildId"),
            CandidateMetaModVersion = OptionalString(candidateMm, "version"),
            CandidateCounterStrikeSharpVersion = OptionalString(candidateCss, "version"),
            CandidateMetaModFiles = InstalledFiles(candidateMm),
            CandidateCounterStrikeSharpFiles = InstalledFiles(candidateCss),
        };
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static IReadOnlyDictionary<string, string> InstalledFiles(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("installedFiles", out var files))
            return new Dictionary<string, string>();
        return files.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }
}
