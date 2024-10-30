using Valour.Sdk.Models.Messages.Embeds.Items;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Client.Components.Messages.Embeds;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using Valour.Client.Components.Menus.Modals;
using Valour.Client.Modals;

namespace Valour.Client.Utility;

internal static class EmbedGoToClickHandler
{
    public static async Task HandleClick(EmbedItem embedItem, EmbedComponent embedComponent, ModalRoot modalRoot, IJSRuntime jsRuntime)
    {
		var item = (IClickable)embedItem;
		var message = embedComponent.Message;

        if (item.ClickTarget.Type == TargetType.Event)
        {
			var interaction = new EmbedInteractionEvent()
			{
				EventType = EmbedIteractionEventType.ItemClicked,
				MessageId = message.Id,
				ChannelId = message.ChannelId,
				TimeInteracted = DateTime.UtcNow,
				ElementId = ((EmbedEventTarget)item.ClickTarget).EventElementId
			};

			if (message.PlanetId is not null)
			{
				var selfMember = await message.Planet.FetchMemberByUserAsync(message.Client.Self.Id);
				interaction.PlanetId = selfMember.PlanetId;
				interaction.Author_MemberId = embedComponent.Message.AuthorMemberId!.Value;
				interaction.MemberId = selfMember.Id;
			}

			var response = await embedComponent.Message.Node.HttpClient.PostAsJsonAsync($"api/embed/interact", interaction);

			Console.WriteLine(response.Content.ReadAsStringAsync());
		}

        else if (item.ClickTarget.Type == TargetType.EmbedPage)
        {
			embedItem.Embed.currentPage = ((EmbedPageTarget)item.ClickTarget).PageNumber;
            embedComponent.UpdateItems();
        }

		else if (item.ClickTarget.Type == TargetType.Link)
		{
			var target = (EmbedLinkTarget)item.ClickTarget;
			
			var modalData = new ConfirmModalComponent.ModalParams(
				$"This link will take you to {target.Href}",
				"Are you sure?",
				"Continue",
				"Cancel", 
				async () =>
				{
					await jsRuntime.InvokeAsync<object>("open", target.Href, "_blank");
				},
				() => Task.CompletedTask
			);
			
			modalRoot.OpenModal<ConfirmModalComponent>(modalData);
		}
    }
}