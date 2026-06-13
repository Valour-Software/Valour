using System.Text.Json.Serialization;

namespace Valour.Client.Components.Windows.ThreadWindows;

/// <summary>
/// Persisted state for the threads feed window.
/// A null PlanetId means the aggregated feed across all joined planets.
/// </summary>
public class ThreadsWindowData
{
    public long? PlanetId { get; set; }

    /// <summary>
    /// Set when navigating back from a thread view, so the feed plays a
    /// backward (left-to-right) shared-axis entrance instead of none.
    /// Transient - never persisted.
    /// </summary>
    [JsonIgnore]
    public bool SlideBack { get; set; }
}

/// <summary>
/// Persisted state for a thread detail window.
/// </summary>
public class ThreadWindowData
{
    public long PlanetId { get; set; }
    public long ThreadId { get; set; }

    /// <summary>
    /// Feed the user navigated from, for back navigation.
    /// Null means the aggregated all-planets feed.
    /// </summary>
    public long? BackPlanetId { get; set; }
}
