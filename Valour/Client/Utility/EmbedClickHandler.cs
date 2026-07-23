using Microsoft.JSInterop;
using Valour.Client.Components.Messages.Embeds;
using Valour.Client.Components.Messages.Embeds.Items;
using Valour.Client.Components.Menus.Modals;
using Valour.Client.Modals;
using Valour.Sdk.Models.Embeds;
using Valour.Sdk.Models.Embeds.Items;

namespace Valour.Client.Utility;

/// <summary>
/// Executes the click target of an embed item: link confirmation, page
/// navigation, or an interaction event relayed to the authoring bot.
/// </summary>
internal static class EmbedClickHandler
{
    public static async Task HandleClickAsync(
        EmbedItem item,
        EmbedComponent root,
        EmbedFormComponent? enclosingForm,
        IJSRuntime jsRuntime)
    {
        if (item is not IClickableItem { ClickTarget: not null } clickable)
            return;

        switch (clickable.ClickTarget)
        {
            case EmbedPageTarget page:
                root.GoToPage(page.PageIndex);
                break;

            case EmbedLinkTarget link:
                ConfirmAndOpenLink(link, jsRuntime);
                break;

            case EmbedEventTarget eventTarget:
                await root.SendInteractionAsync(EmbedInteractionEventType.ItemClicked, eventTarget.EventId);
                break;

            case EmbedFormSubmitTarget submit:
                var form = enclosingForm?.Form;
                await root.SendInteractionAsync(
                    EmbedInteractionEventType.FormSubmitted,
                    submit.EventId,
                    form?.Id,
                    form?.GetFormData());
                break;
        }
    }

    private static void ConfirmAndOpenLink(EmbedLinkTarget link, IJSRuntime jsRuntime)
    {
        var modalData = new ConfirmModalComponent.ModalParams(
            $"This link will take you to {link.Href}",
            "Are you sure?",
            "Continue",
            "Cancel",
            async () =>
            {
                await jsRuntime.InvokeAsync<object>("open", link.Href, "_blank");
            },
            () => Task.CompletedTask
        );

        ModalRoot.Instance.OpenModal<ConfirmModalComponent>(modalData);
    }
}
