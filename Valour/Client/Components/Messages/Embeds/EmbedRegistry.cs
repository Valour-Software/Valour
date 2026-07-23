using Valour.Client.Components.Messages.Embeds.Items;

namespace Valour.Client.Components.Messages.Embeds;

/// <summary>
/// Maps item ids to their rendered components for one embed instance,
/// so targeted live updates can refresh exactly the affected components.
/// </summary>
public sealed class EmbedRegistry
{
    private readonly Dictionary<string, EmbedItemComponentBase> _components = new();

    public void Register(string id, EmbedItemComponentBase component) =>
        _components[id] = component;

    public void Unregister(string id, EmbedItemComponentBase component)
    {
        // Only remove if this component is still the registered one; a
        // replacement may have registered before the old one disposed
        if (_components.TryGetValue(id, out var current) && ReferenceEquals(current, component))
            _components.Remove(id);
    }

    public bool TryGet(string id, out EmbedItemComponentBase component) =>
        _components.TryGetValue(id, out component!);
}
