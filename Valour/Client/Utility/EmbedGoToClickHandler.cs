using Valour.Api.Items.Messages.Embeds.Items;
using Valour.Api.Items.Messages.Embeds;
using Valour.Client.Components.Messages.Embeds;
using Valour.Api.Items.Messages;
using Valour.Api.Items.Planets.Members;
using Valour.Api.Client;
using System.Net.Http.Json;

namespace Valour.Client.Utility;

internal static class EmbedGoToClickHandler
{
    public static async Task HandleClick(EmbedItem item, EmbedComponent embedComponent)
    {
        //await Task.Delay(10);
        if (item.OnClickEventName is not null)
        {
            var interaction = new EmbedInteractionEvent()
            {
                EventType = EmbedIteractionEventType.TextClicked,
                MessageId = embedComponent.Message.Message.Id,
                ChannelId = embedComponent.Message.Message.ChannelId,
                TimeInteracted = DateTime.UtcNow,
                ElementId = item.OnClickEventName
            };

            if (embedComponent.Message.Message is PlanetMessage)
            {
                var planetMessage = embedComponent.Message.Message as PlanetMessage;
                PlanetMember SelfMember = await PlanetMember.FindAsyncByUser(ValourClient.Self.Id, planetMessage.PlanetId);

                interaction.PlanetId = SelfMember.PlanetId;
                interaction.Author_MemberId = planetMessage.AuthorMemberId;
                interaction.MemberId = SelfMember.Id;
            }

            var response = await ValourClient.Http.PostAsJsonAsync($"api/embed/interact", interaction);

            Console.WriteLine(response.Content.ReadAsStringAsync());
        }
        else if (item.Page is not null)
        {
            item.Embed.currentPage = (int)item.Page;
            embedComponent.UpdateItems();
        }
    }
}