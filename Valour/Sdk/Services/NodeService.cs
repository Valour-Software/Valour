using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public class NodeService : ServiceBase
{
    public HybridEvent<Node> NodeReconnected;
    
    private readonly ValourClient _client;
    
    private static readonly LogOptions LogOptions = new (
        "Node Service",
        "#036bfc",
        "#fc0356",
        "#fc8403"
    );
    
    public NodeService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }
    
    public async Task SetupPrimaryNodeAsync()
    {
        string nodeName = null;

        do
        {
            // Get primary node identity
            var nodeNameResponse = await _client.Http.GetAsync("api/node/name");
            var msg = await nodeNameResponse.Content.ReadAsStringAsync();
            if (!nodeNameResponse.IsSuccessStatusCode)
            {
                LogError("Failed to get primary node name... trying again in three seconds. Network issues? \n \n" + msg);
                await Task.Delay(3000);
            }
            else
            {
                nodeName = msg;
            }
        } while (nodeName is null);
        
        // Initialize primary node
        _client.PrimaryNode = new Node(_client);
        await _client.PrimaryNode.InitializeAsync(nodeName, true);
    }
}