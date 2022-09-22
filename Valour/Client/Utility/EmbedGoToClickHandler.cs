using Valour.Api.Items.Messages.Embeds.Items;
using Valour.Client.Components.Messages.Embeds;

namespace Valour.Client.Utility;

internal static class EmbedGoToClickHandler
{
    public static async Task HandleClick(EmbedItem item, EmbedComponent embedComponent)
    {
        //await Task.Delay(10);
        if (item.Page is not null)
        {
            item.Embed.currentPage = (int)item.Page;
            embedComponent.UpdateItems();
        }
    }
}