namespace Valour.Tests.Contracts;

/// <summary>
/// A change that can break a client built against the baseline contract.
/// <see cref="Id"/> is stable across runs and is what accepted-breaks.txt lists.
/// </summary>
public sealed record WireContractBreak(string Id, string Detail)
{
    public override string ToString() => Detail is null ? Id : $"{Id} ({Detail})";
}

/// <summary>
/// Finds the changes between a baseline contract and the current one that an
/// installed client could notice. Additions are allowed: older clients ignore
/// routes, events, and JSON properties they do not know. Removing or retyping
/// something a client sends or reads, or requiring a new value from it, is not.
/// </summary>
public static class WireContractComparer
{
    public static List<WireContractBreak> FindBreaks(WireContract baseline, WireContract current)
    {
        var breaks = new List<WireContractBreak>();

        foreach (var (key, old) in baseline.Endpoints)
        {
            if (!current.Endpoints.TryGetValue(key, out var now))
            {
                breaks.Add(new($"endpoint removed: {key}", null));
                continue;
            }

            CompareEndpoint(key, old, now, breaks);
        }

        foreach (var (name, old) in baseline.HubMethods)
        {
            if (!current.HubMethods.TryGetValue(name, out var now))
            {
                breaks.Add(new($"hub method removed: {name}", null));
                continue;
            }

            var oldSignature = $"({string.Join(", ", old.Parameters)}) -> {old.Returns}";
            var newSignature = $"({string.Join(", ", now.Parameters)}) -> {now.Returns}";
            if (oldSignature != newSignature)
                breaks.Add(new($"hub method changed: {name}", $"{oldSignature} became {newSignature}"));
        }

        foreach (var name in baseline.Events)
        {
            if (!current.Events.Contains(name))
                breaks.Add(new($"event removed: {name}", null));
        }

        foreach (var (name, old) in baseline.Types)
        {
            if (!current.Types.TryGetValue(name, out var now))
            {
                breaks.Add(new($"type removed: {name}", null));
                continue;
            }

            CompareType(name, old, now, breaks);
        }

        return breaks;
    }

    private static void CompareEndpoint(string key, WireEndpoint old, WireEndpoint now, List<WireContractBreak> breaks)
    {
        // The route key already fixes the number of path parameters.
        for (var i = 0; i < old.Path.Count && i < now.Path.Count; i++)
        {
            if (old.Path[i] != now.Path[i])
                breaks.Add(new($"endpoint path parameter changed: {key} #{i + 1}", $"{old.Path[i]} became {now.Path[i]}"));
        }

        foreach (var (name, oldParameter) in old.Parameters)
        {
            // An older client still sends a parameter the server stopped reading,
            // which the server ignores.
            if (!now.Parameters.TryGetValue(name, out var newParameter))
                continue;

            if (oldParameter.Type != newParameter.Type)
                breaks.Add(new($"endpoint parameter changed: {key} {name}", $"{oldParameter.Type} became {newParameter.Type}"));
            else if (!oldParameter.Required && newParameter.Required)
                breaks.Add(new($"endpoint parameter now required: {key} {name}", null));
        }

        foreach (var (name, newParameter) in now.Parameters)
        {
            if (newParameter.Required && !old.Parameters.ContainsKey(name))
                breaks.Add(new($"endpoint parameter added: {key} {name}", $"required {newParameter.Type}"));
        }

        if (old.Body is not null && now.Body is not null)
        {
            if (old.Body.Type != now.Body.Type)
                breaks.Add(new($"endpoint body changed: {key}", $"{old.Body.Type} became {now.Body.Type}"));
            else if (!old.Body.Required && now.Body.Required)
                breaks.Add(new($"endpoint body now required: {key}", null));
        }
        else if (old.Body is null && now.Body is { Required: true })
        {
            breaks.Add(new($"endpoint body added: {key}", $"required {now.Body.Type}"));
        }
    }

    private static void CompareType(string name, WireType old, WireType now, List<WireContractBreak> breaks)
    {
        if (old.Kind != now.Kind)
        {
            breaks.Add(new($"type kind changed: {name}", $"{old.Kind} became {now.Kind}"));
            return;
        }

        if (old.Properties is not null)
        {
            foreach (var (property, oldType) in old.Properties)
            {
                if (now.Properties is null || !now.Properties.TryGetValue(property, out var newType))
                    breaks.Add(new($"property removed: {name}.{property}", null));
                else if (oldType != newType)
                    breaks.Add(new($"property changed: {name}.{property}", $"{oldType} became {newType}"));
            }
        }

        if (old.Format != now.Format)
            breaks.Add(new($"enum format changed: {name}", $"{old.Format} became {now.Format}"));

        if (old.Values is not null)
        {
            foreach (var (member, oldValue) in old.Values)
            {
                if (now.Values is null || !now.Values.TryGetValue(member, out var newValue))
                    breaks.Add(new($"enum value removed: {name}.{member}", null));
                else if (oldValue != newValue)
                    breaks.Add(new($"enum value changed: {name}.{member}", $"{oldValue} became {newValue}"));
            }
        }

        if (old.Discriminator != now.Discriminator)
            breaks.Add(new($"discriminator changed: {name}", $"{old.Discriminator ?? "none"} became {now.Discriminator ?? "none"}"));

        if (old.DerivedTypes is not null)
        {
            foreach (var (discriminator, oldType) in old.DerivedTypes)
            {
                if (now.DerivedTypes is null || !now.DerivedTypes.TryGetValue(discriminator, out var newType))
                    breaks.Add(new($"derived type removed: {name} \"{discriminator}\"", null));
                else if (oldType != newType)
                    breaks.Add(new($"derived type changed: {name} \"{discriminator}\"", $"{oldType} became {newType}"));
            }
        }
    }
}
