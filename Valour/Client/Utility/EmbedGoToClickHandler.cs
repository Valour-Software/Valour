using Valour.Sdk.Models.Messages.Embeds.Items;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Client.Components.Messages.Embeds;
using Valour.Sdk.Client;
using System.Net.Http.Json;
using Blazored.Modal.Services;
using Valour.Client.Modals;
using Microsoft.JSInterop;
using Blazored.Modal;
using Valour.Client.Components.Menus.Modals;
using Valour.Sdk.Models;
using Valour.Sdk.Nodes;

namespace Valour.Client.Utility;

internal static class EmbedGoToClickHandler
{
    public static async Task HandleClick(EmbedItem embedItem, EmbedComponent embedComponent, IModalService modal, IJSRuntime jsRuntime)
    {
		var item = (IClickable)embedItem;

        if (item.ClickTarget.Type == TargetType.Event)
        {
			var interaction = new EmbedInteractionEvent()
			{
				EventType = EmbedIteractionEventType.ItemClicked,
				MessageId = embedComponent.Message.Id,
				ChannelId = embedComponent.Message.ChannelId,
				TimeInteracted = DateTime.UtcNow,
				ElementId = ((EmbedEventTarget)item.ClickTarget).EventElementId
			};

			if (embedComponent.Message.PlanetId is not null)
			{
				var selfMember = await PlanetMember.FindAsyncByUser(ValourClient.Self.Id, embedComponent.Message.PlanetId.Value);

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
			EmbedLinkTarget target = (EmbedLinkTarget)item.ClickTarget;
			ConfirmModalData modalData =
			new($"This link will take you to {target.Href}",
				"Are you sure?",
				"Continue",
				"Cancel", 
				async () =>
				{
					await jsRuntime.InvokeAsync<object>("open", target.Href, "_blank");
				},
				async () =>
				{
					Console.WriteLine($"Cancelled going to link: {target.Href}");
				}
			);

			ModalParameters modParams = new();
			modParams.Add("Data", modalData);

			modal.Show<ConfirmModalComponent>("Confirm", modParams, new ModalOptions() { Class = "modal-shrink-fit" });
		}
    }
}