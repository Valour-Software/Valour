using Microsoft.AspNetCore.Components;
using Valour.Sdk.Models.Embeds.Items;

namespace Valour.Client.Components.Messages.Embeds.Items;

/// <summary>
/// Base for all embed item components. Handles registry bookkeeping so
/// targeted live updates can refresh individual items by id.
/// </summary>
public abstract class EmbedItemComponentBase : ComponentBase, IDisposable
{
    [Parameter]
    public EmbedItem Item { get; set; } = null!;

    [CascadingParameter]
    public EmbedComponent Root { get; set; } = null!;

    [CascadingParameter]
    public EmbedFormComponent? EnclosingForm { get; set; }

    private string? _registeredId;

    protected override void OnParametersSet()
    {
        if (Item?.Id == _registeredId)
            return;

        if (_registeredId is not null)
            Root.Registry.Unregister(_registeredId, this);

        _registeredId = Item?.Id;
        if (_registeredId is not null)
            Root.Registry.Register(_registeredId, this);
    }

    public void Dispose()
    {
        if (_registeredId is not null)
            Root.Registry.Unregister(_registeredId, this);
    }

    /// <summary>
    /// Re-resolves this component's item from the embed model (a targeted
    /// update swaps the model node) and re-renders.
    /// </summary>
    public void RefreshFromModel()
    {
        if (Item?.Id is not null)
        {
            var updated = Root.Embed?.FindItem(Item.Id);
            if (updated is not null && updated.GetType() == Item.GetType())
                Item = updated;
        }

        OnItemRefreshed();
        StateHasChanged();
    }

    /// <summary>
    /// Hook for components that derive state from the item (e.g. dropdown labels).
    /// </summary>
    protected virtual void OnItemRefreshed() { }
}
