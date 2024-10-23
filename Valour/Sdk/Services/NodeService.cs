using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public static class NodeService
{
    public static HybridEvent<Node> NodeReconnected;
    
    public static async Task SetupPrimaryNodeAsync()
    {
        TaskResult<string> nodeName;

        do
        {
            // Get primary node identity
            nodeName = await ValourClient.GetAsync("api/node/name");

            if (!nodeName.Success)
            {
                Console.WriteLine("Failed to get primary node name... trying again in three seconds.");
                Console.WriteLine("(Possible network issues)");
                await Task.Delay(3000);
            }
            
        } while (!nodeName.Success);
        
        // Initialize primary node
        ValourClient.PrimaryNode = new Node();
        await ValourClient.PrimaryNode.InitializeAsync(nodeName.Data, true);
    }
}