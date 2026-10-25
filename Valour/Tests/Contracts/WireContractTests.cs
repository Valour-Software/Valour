using System.Text.RegularExpressions;

namespace Valour.Tests.Contracts;

[CollectionDefinition(Name, DisableParallelization = true)]
public class WireContractCollection
{
    // Building the contract briefly sets the static config singletons, so it
    // must not overlap with tests that start the server.
    public const string Name = "WireContractCollection";
}

public sealed class WireContractFixture : IAsyncLifetime
{
    public WireContract Current { get; private set; }

    public async ValueTask InitializeAsync() => Current = await WireContractBuilder.BuildAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Checks that the current server still serves clients of the baseline release
/// recorded in baseline.json. Needs no database or Redis. Set
/// VALOUR_UPDATE_WIRE_CONTRACT=1 to replace the baseline with the current
/// contract instead; see Docs/ClientCompatibility.md for when to do that.
/// </summary>
[Collection(WireContractCollection.Name)]
public partial class WireContractTests : IClassFixture<WireContractFixture>
{
    private const string UpdateVariable = "VALOUR_UPDATE_WIRE_CONTRACT";

    private static readonly string ContractDirectory =
        Path.Combine(RepositoryRoot.Path, "Valour", "Tests", "Contracts");

    private static readonly string BaselinePath = Path.Combine(ContractDirectory, "baseline.json");
    private static readonly string AcceptedBreaksPath = Path.Combine(ContractDirectory, "accepted-breaks.txt");

    private readonly WireContract _current;

    public WireContractTests(WireContractFixture fixture)
    {
        _current = fixture.Current;
    }

    [Fact]
    public void CurrentServerKeepsBaselineContract()
    {
        // Written on every run so a failure can be inspected or diffed.
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "wire-contract-current.json"), _current.ToJson());

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(BaselinePath, _current.ToJson());
            return;
        }

        var baseline = WireContract.FromJson(File.ReadAllText(BaselinePath));
        var accepted = ReadAcceptedBreaks();
        var breaks = WireContractComparer.FindBreaks(baseline, _current);

        var unexpected = breaks.Where(b => !accepted.ContainsKey(b.Id)).ToList();
        var stale = accepted.Keys.Where(id => breaks.All(b => b.Id != id)).ToList();

        var problems = new List<string>();
        if (unexpected.Count > 0)
        {
            problems.Add(
                $"These changes can break clients of the baseline release {baseline.Version}. Restore what they " +
                "removed, or add the line before each parenthesis to accepted-breaks.txt with a reason:");
            problems.AddRange(unexpected.Select(b => "  " + b));
        }

        if (stale.Count > 0)
        {
            problems.Add("accepted-breaks.txt lists changes that no longer differ from the baseline. Remove them:");
            problems.AddRange(stale.Select(id => "  " + id));
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void BaselineReleaseIsStillSupported()
    {
        var baseline = WireContract.FromJson(File.ReadAllText(BaselinePath));

        Assert.True(baseline.ClientProtocol >= _current.OldestSupportedClientProtocol,
            $"The baseline release {baseline.Version} speaks client protocol {baseline.ClientProtocol}, but the " +
            $"server now requires {_current.OldestSupportedClientProtocol}. Replace baseline.json with the contract " +
            "of the oldest release that is still supported, using Tools/Contracts/update-baseline.sh.");
    }

    /// <summary>
    /// Event names in the contract come from <see cref="Valour.Shared.HubEvents"/>.
    /// A name written as a string at a send or listen site would bypass it.
    /// </summary>
    [Fact]
    public void HubEventsAreNamedThroughHubEvents()
    {
        var violations = new List<string>();
        violations.AddRange(FindInSources(Path.Combine("Valour", "Server"), ServerSendWithLiteral()));
        violations.AddRange(FindInSources(Path.Combine("Valour", "Sdk"), ClientListenWithLiteral()));
        violations.AddRange(FindInSources(Path.Combine("Valour", "Client"), ClientListenWithLiteral()));

        Assert.True(violations.Count == 0,
            "Use a HubEvents constant for these realtime event names:\n" + string.Join("\n", violations));
    }

    private static Dictionary<string, string> ReadAcceptedBreaks()
    {
        var accepted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(AcceptedBreaksPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf(" # ", StringComparison.Ordinal);
            Assert.True(separator > 0 && line[(separator + 3)..].Trim().Length > 0,
                $"Each line in accepted-breaks.txt needs a reason after \" # \": {line}");

            accepted[line[..separator].Trim()] = line[(separator + 3)..].Trim();
        }

        return accepted;
    }

    private static IEnumerable<string> FindInSources(string relativeDirectory, Regex pattern)
    {
        var directory = Path.Combine(RepositoryRoot.Path, relativeDirectory);
        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(directory, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in pattern.Matches(text))
            {
                var line = text.AsSpan(0, match.Index).Count('\n') + 1;
                yield return $"  {Path.GetRelativePath(RepositoryRoot.Path, file)}:{line}";
            }
        }
    }

    [GeneratedRegex(@"\b(?:SendAsync|SendCoreAsync)\(\s*\$?@?""")]
    private static partial Regex ServerSendWithLiteral();

    [GeneratedRegex(@"\.On(?:<[^<>()]+(?:<[^<>()]+>)?[^<>()]*>)?\(\s*\$?@?""")]
    private static partial Regex ClientListenWithLiteral();
}

/// <summary>Finds the checkout the tests were built from.</summary>
internal static class RepositoryRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Directory.Build.props")) &&
                Directory.Exists(System.IO.Path.Combine(directory.FullName, "Valour", "Tests")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException($"No Valour checkout contains {AppContext.BaseDirectory}.");
    }
}
