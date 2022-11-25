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
    public static async Task HandleClick(EmbedItem _item, EmbedComponent embedComponent)
    {
		var item = (IClickable)_item;

        if (item.ClickTarget.Type == TargetType.Event)
        {
			var interaction = new EmbedInteractionEvent()
			{
				EventType = EmbedIteractionEventType.ItemClicked,
				MessageId = embedComponent.MessageWrapper.Message.Id,
				ChannelId = embedComponent.MessageWrapper.Message.ChannelId,
				TimeInteracted = DateTime.UtcNow,
				ElementId = ((EmbedEventTarget)item.ClickTarget).EventElementId
			};

			if (embedComponent.MessageWrapper.Message is PlanetMessage)
			{
				var planetMessage = embedComponent.MessageWrapper.Message as PlanetMessage;
				PlanetMember SelfMember = await PlanetMember.FindAsyncByUser(ValourClient.Self.Id, planetMessage.PlanetId);

				interaction.PlanetId = SelfMember.PlanetId;
				interaction.Author_MemberId = planetMessage.AuthorMemberId;
				interaction.MemberId = SelfMember.Id;
			}

			var response = await ValourClient.Http.PostAsJsonAsync($"api/embed/interact", interaction);

			Console.WriteLine(response.Content.ReadAsStringAsync());
		}

        else if (item.ClickTarget.Type == TargetType.EmbedPage)
        {
			_item.Embed.currentPage = ((EmbedPageTarget)item.ClickTarget).PageNumber;
            embedComponent.UpdateItems();
        }
    }
}