using System.Text.RegularExpressions;
using Xunit;

namespace CS2LocalKit.Core.Tests;

/// <summary>
/// Private-data boundary: no real SteamID may enter the tracked tree via fixtures or
/// golden files. The only permitted 17-digit id in test fixtures is the fake one.
/// </summary>
public class PrivateDataBoundaryTests
{
    [Fact]
    public void CommittedFixtures_ContainOnlyTheFakeSteamId()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(TestFixtures.FixturesDir, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"7656\d{13}"))
            {
                if (m.Value != TestFixtures.FakeSteamId64)
                    offenders.Add($"{file}: {m.Value}");
            }
        }
        Assert.Empty(offenders);
    }
}
