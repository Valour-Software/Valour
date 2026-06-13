namespace Valour.Client.Components.Windows.ThreadWindows;

/// <summary>
/// Persisted state for the threads feed window.
/// A null PlanetId means the aggregated feed across all joined planets.
/// </summary>
public class ThreadsWindowData
{
    public long? PlanetId { get; set; }
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
