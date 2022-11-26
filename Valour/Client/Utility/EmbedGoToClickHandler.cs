using Valour.Api.Items.Messages.Embeds.Items;
using Valour.Api.Items.Messages.Embeds;
using Valour.Client.Components.Messages.Embeds;
using Valour.Api.Items.Messages;
using Valour.Api.Items.Planets.Members;
using Valour.Api.Client;
using System.Net.Http.Json;
using Blazored.Modal.Services;
using Valour.Client.Modals;
using Microsoft.JSInterop;
using Blazored.Modal;
using Valour.Client.Components.Menus.Modals;

namespace Valour.Client.Utility;

internal static class EmbedGoToClickHandler
{
    public static async Task HandleClick(EmbedItem _item, EmbedComponent embedComponent, IModalService Modal, IJSRuntime JS)
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
					JS.InvokeAsync<object>("open", target.Href, "_blank");

				},
				async () =>
				{
					Console.WriteLine($"Cancelled going to link: {target.Href}");
				}
			);

			ModalParameters modParams = new();
			modParams.Add("Data", modalData);

			Modal.Show<ConfirmModalComponent>("Confirm", modParams, new ModalOptions() { Class = "modal-shrink-fit" });
		}
    }
}