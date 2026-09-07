using System.Net;
using System.Net.Http.Json;
using System.Text;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

[Collection("ApiCollection")]
public class PlanetListLayoutApiTests(LoginTestFixture fixture)
{
    [Theory]
    [InlineData("{\"planets\":null,\"folderIds\":[]}")]
    [InlineData("{\"planets\":[],\"folderIds\":null}")]
    [InlineData("{\"planets\":[null],\"folderIds\":[]}")]
    public async Task NullLayoutCollections_ReturnBadRequest(string json)
    {
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await fixture.Client.Http.PostAsync("api/users/me/planet-list-layout", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FolderLifecycle_PersistsRenameAndRejectsUnknownPlacement()
    {
        var http = fixture.Client.Http;
        using var created = await http.PostAsJsonAsync("api/users/me/planet-list-folders", new { Name = "Release QA" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var folder = (await created.Content.ReadFromJsonAsync<PlanetListFolder>())!;
        try
        {
            using var renamed = await http.PostAsJsonAsync($"api/users/me/planet-list-folders/{folder.Id}/rename", new { Name = "Renamed QA" });
            Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
            var layout = (await http.GetFromJsonAsync<PlanetListLayout>("api/users/me/planet-list-layout"))!;
            Assert.Contains(layout.Folders, x => x.Id == folder.Id && x.Name == "Renamed QA");
            using var invalid = await http.PostAsJsonAsync("api/users/me/planet-list-layout", new SavePlanetListLayoutRequest
            {
                FolderIds = [folder.Id], Planets = [new() { PlanetId = long.MaxValue, FolderId = folder.Id }]
            });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally
        {
            using var deleted = await http.DeleteAsync($"api/users/me/planet-list-folders/{folder.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
        var final = (await http.GetFromJsonAsync<PlanetListLayout>("api/users/me/planet-list-layout"))!;
        Assert.DoesNotContain(final.Folders, x => x.Id == folder.Id);
    }
}
