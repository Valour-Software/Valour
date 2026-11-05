namespace Valour.Tests.Contracts;

public class WireContractComparerTests
{
    private const string Route = "GET api/channels/{long}";
    private const string Model = "Valour.Server.Models.Channel";

    private static WireContract Baseline() => new()
    {
        Endpoints =
        {
            [Route] = new WireEndpoint
            {
                Path = ["int64"],
                Parameters = { ["query count"] = new WireParameter { Type = "int32", Required = false } },
            },
        },
        HubMethods = { ["JoinChannel"] = new WireHubMethod { Parameters = ["int64"], Returns = "Valour.Shared.TaskResult" } },
        Events = { "Relay" },
        Types =
        {
            [Model] = new WireType
            {
                Kind = "object",
                Properties = new(StringComparer.Ordinal) { ["id"] = "int64", ["name"] = "string" },
            },
            ["Valour.Shared.Models.ChannelTypeEnum"] = new WireType
            {
                Kind = "enum",
                Format = "number",
                Values = new(StringComparer.Ordinal) { ["PlanetChat"] = 0, ["DirectChat"] = 1 },
            },
        },
    };

    private static List<string> Breaks(Action<WireContract> change)
    {
        var current = Baseline();
        change(current);
        return WireContractComparer.FindBreaks(Baseline(), current).Select(b => b.Id).ToList();
    }

    [Fact]
    public void UnchangedContractHasNoBreaks() => Assert.Empty(Breaks(_ => { }));

    [Fact]
    public void AdditionsAreAllowed()
    {
        var breaks = Breaks(c =>
        {
            c.Endpoints["POST api/channels"] = new WireEndpoint();
            c.Endpoints[Route].Parameters["query before"] = new WireParameter { Type = "int64", Required = false };
            c.Events.Add("Relay-Batch");
            c.Types[Model].Properties["description"] = "string";
            c.Types["Valour.Shared.Models.ChannelTypeEnum"].Values["GroupChat"] = 2;
        });

        Assert.Empty(breaks);
    }

    [Fact]
    public void RemovedEndpointIsABreak() =>
        Assert.Equal([$"endpoint removed: {Route}"], Breaks(c => c.Endpoints.Remove(Route)));

    [Fact]
    public void NewRequiredParameterIsABreak() =>
        Assert.Equal([$"endpoint parameter added: {Route} query before"], Breaks(c =>
            c.Endpoints[Route].Parameters["query before"] = new WireParameter { Type = "int64", Required = true }));

    [Fact]
    public void OptionalParameterBecomingRequiredIsABreak() =>
        Assert.Equal([$"endpoint parameter now required: {Route} query count"], Breaks(c =>
            c.Endpoints[Route].Parameters["query count"].Required = true));

    [Fact]
    public void RetypedPathParameterIsABreak() =>
        Assert.Equal([$"endpoint path parameter changed: {Route} #1"], Breaks(c => c.Endpoints[Route].Path[0] = "string"));

    [Fact]
    public void ChangedHubMethodIsABreak() =>
        Assert.Equal(["hub method changed: JoinChannel"], Breaks(c => c.HubMethods["JoinChannel"].Parameters.Add("boolean")));

    [Fact]
    public void RemovedEventIsABreak() =>
        Assert.Equal(["event removed: Relay"], Breaks(c => c.Events.Remove("Relay")));

    [Fact]
    public void RemovedOrRetypedPropertyIsABreak()
    {
        var breaks = Breaks(c =>
        {
            c.Types[Model].Properties.Remove("name");
            c.Types[Model].Properties["id"] = "int64?";
        });

        Assert.Equal([$"property changed: {Model}.id", $"property removed: {Model}.name"], breaks.Order().ToList());
    }

    [Fact]
    public void RemovedOrRenumberedEnumValueIsABreak()
    {
        const string name = "Valour.Shared.Models.ChannelTypeEnum";
        var breaks = Breaks(c =>
        {
            c.Types[name].Values.Remove("DirectChat");
            c.Types[name].Values["PlanetChat"] = 5;
        });

        Assert.Equal([$"enum value changed: {name}.PlanetChat", $"enum value removed: {name}.DirectChat"], breaks.Order().ToList());
    }

    [Fact]
    public void ContractSurvivesTheFileFormat()
    {
        var baseline = Baseline();
        var roundTripped = WireContract.FromJson(baseline.ToJson());

        Assert.Empty(WireContractComparer.FindBreaks(baseline, roundTripped));
        Assert.Equal(baseline.ToJson(), roundTripped.ToJson());
    }
}
