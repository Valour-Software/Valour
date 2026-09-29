using System.Net;
using System.Reflection;
using Valour.Sdk.Client;
using Valour.Sdk.Cdn;
using Valour.Sdk.Models;
using Valour.Sdk.Nodes;
using Valour.Sdk.Services;
using Valour.Shared.Hosting;
using Valour.Shared.Models;

namespace Valour.Tests.Sdk.Models;

public class MessageAttachmentSignedUrlTests
{
    private static ValueTask<string?> SignedUrlOf(string location, Node? node = null) =>
        new MessageAttachment(MessageAttachmentType.Image) { Location = location }.GetSignedUrl(null!, node!);

    [Fact]
    public async Task ProxiedMedia_StaysAbsoluteOnTheCdn()
    {
        var location = $"{ValourHosts.ContentCdnBaseUrl}/proxy/ea2baae3c938386c.png";

        Assert.Equal(location, await SignedUrlOf(location));
    }

    [Fact]
    public async Task ProxiedMedia_OnAnotherHost_IsLoadedFromTheCdn()
    {
        Assert.Equal($"{ValourHosts.ContentCdnBaseUrl}/proxy/abc.png",
            await SignedUrlOf("https://attacker.example/proxy/abc.png"));
    }

    [Fact]
    public async Task CommunityNodeMedia_LoadsFromTheNodesCdn()
    {
        var node = CreateCommunityNode("community.example");

        Assert.Equal("https://community.example/proxy/abc.png",
            await SignedUrlOf("https://community.example/proxy/abc.png", node));
        Assert.Equal("https://community.example/proxy/abc.png",
            await SignedUrlOf("https://attacker.example/proxy/abc.png", node));

        // History moved from the hub keeps its hub locations.
        var hubLocation = $"{ValourHosts.ContentCdnBaseUrl}/proxy/abc.png";
        Assert.Equal(hubLocation, await SignedUrlOf(hubLocation, node));
    }

    [Fact]
    public async Task CommunityNode_UsesTheCdnHostFromItsManifest()
    {
        var node = CreateCommunityNode("community.example");
        SetHttpClient(node, """{"clientProtocol":99,"hosts":{"contentCdn":"media.community.example"}}""");

        Assert.Equal("community.example", node.ContentCdnHost);
        await node.CheckAcceptsEncryptedMessagesAsync();
        Assert.Equal("media.community.example", node.ContentCdnHost);

        Assert.Equal("https://media.community.example/proxy/abc.png",
            await SignedUrlOf("https://community.example/proxy/abc.png", node));
    }

    [Fact]
    public void EmbedMedia_FromACommunityNodesCdn_IsAllowedOnlyForThatNode()
    {
        var attachment = new MessageAttachment(MessageAttachmentType.Image)
        {
            Location = "https://community.example/proxy/abc.png",
        };

        Assert.False(MediaUriHelper.ScanMediaUri(attachment).Success);
        Assert.True(MediaUriHelper.ScanMediaUri(attachment, CreateCommunityNode("community.example")).Success);
        Assert.False(MediaUriHelper.ScanMediaUri(attachment, CreateCommunityNode("other.example")).Success);
    }

    private static Node CreateCommunityNode(string domain)
    {
        var node = new Node(new ValourClient("https://hub.example/"));
        typeof(Node).GetField("_externalBaseUrl", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(node, $"https://{domain}/");
        typeof(Node).GetField("<Name>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(node, domain);
        return node;
    }

    private static void SetHttpClient(Node node, string manifestJson) =>
        typeof(Node).GetProperty(nameof(Node.HttpClient))!.SetValue(node,
            new HttpClient(new ManifestHandler(manifestJson)) { BaseAddress = new Uri($"https://{node.Name}/") });

    private sealed class ManifestHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
