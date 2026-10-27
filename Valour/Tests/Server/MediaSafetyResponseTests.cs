using Valour.Server.Cdn;
using Valour.Shared.Models;

namespace Valour.Tests.Server;

/// <summary>
/// The parser decides whether an upload is treated as known abuse material,
/// so a response it cannot read must be an error, never a clean result.
/// </summary>
public class MediaSafetyResponseTests
{
    [Fact]
    public void Match_ReadsMatchIdFromMatchFlags()
    {
        const string body = """
            {
              "Status": { "Code": 3000, "Description": "OK", "Exception": null },
              "ContentId": null,
              "IsMatch": true,
              "MatchDetails": {
                "AdvancedInfo": [],
                "MatchFlags": [
                  { "AdvancedInfo": [ { "Key": "MatchId", "Value": "117721" } ], "Source": "Test", "Violations": [ "A1" ] }
                ]
              },
              "XPartnerCustomerId": null,
              "TrackingId": "WUS_418b5903425346a1b1451821c5cd06ee_57c7457ae3a97812ecf8bde9_ddba296dab39454aa00cf0b17e0eb7bf"
            }
            """;

        var result = MediaSafetyService.ParsePhotoDnaHashMatchResponse(body);

        Assert.Equal(MediaSafetyHashMatchState.Matched, result.State);
        Assert.Equal("117721", result.MatchId);
    }

    [Fact]
    public void NoMatch_IsClean()
    {
        const string body = """
            { "Status": { "Code": 3000, "Description": "OK" }, "IsMatch": false, "MatchDetails": { "MatchFlags": [] }, "TrackingId": "abc" }
            """;

        var result = MediaSafetyService.ParsePhotoDnaHashMatchResponse(body);

        Assert.Equal(MediaSafetyHashMatchState.NoMatch, result.State);
    }

    [Fact]
    public void PropertyNamesAreMatchedWithoutCase()
    {
        const string body = """{ "status": { "code": 3000 }, "isMatch": true }""";

        Assert.Equal(MediaSafetyHashMatchState.Matched, MediaSafetyService.ParsePhotoDnaHashMatchResponse(body).State);
    }

    [Theory]
    [InlineData("""{ "Status": { "Code": 3002, "Description": "Invalid or missing image" }, "IsMatch": false }""")]
    [InlineData("""{ "Status": { "Code": 3000 } }""")]
    [InlineData("""{ "unexpected": true }""")]
    [InlineData("not json")]
    [InlineData("")]
    public void UnreadableOrFailedResponses_AreErrors(string body)
    {
        Assert.Equal(MediaSafetyHashMatchState.Error, MediaSafetyService.ParsePhotoDnaHashMatchResponse(body).State);
    }
}
