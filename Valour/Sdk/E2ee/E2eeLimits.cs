namespace Valour.Sdk.E2ee;

/// <summary>
/// Limits the SDK and the server share, so neither sends or accepts more than
/// the other expects.
/// </summary>
public static class E2eeLimits
{
    /// <summary>
    /// How many of a channel's newest search key generations a search covers.
    /// The server lists at most this many with a channel's keys.
    /// </summary>
    public const int MaxSearchIndexGenerations = 32;

    /// <summary>
    /// The most term sets one search request may carry. A search hashes the
    /// query twice with each search key generation: once with the key for
    /// messages members encrypted and once with the key for messages the
    /// server sealed.
    /// </summary>
    public const int MaxSearchTermSets = 2 * MaxSearchIndexGenerations;

    /// <summary>The most terms one search term set may carry.</summary>
    public const int MaxTermsPerSearchSet = 32;

    /// <summary>The most server-sealed messages one request for unindexed messages returns.</summary>
    public const int MaxUnindexedBatch = 100;

    /// <summary>The most candidate messages one encrypted search returns.</summary>
    public const int MaxSearchResults = 100;

    /// <summary>
    /// The most channel key boxes one request carries, and the most key
    /// generation records one request returns. A new generation carries this
    /// many boxes; the rest are shared in later requests.
    /// </summary>
    public const int MaxBoxesPerRequest = 200;

    /// <summary>
    /// The most members a new channel key is sealed to when it is created.
    /// Others ask for it and receive it from any member who holds it.
    /// </summary>
    public const int MaxKeyCandidates = 1000;

    /// <summary>The most users whose key logs one request fetches.</summary>
    public const int MaxUsersPerKeyLogRequest = 500;

    /// <summary>The most term sets one automod trigger uploads for a channel.</summary>
    public const int MaxAutomodAlternatives = 500;

    /// <summary>The most links one encrypted message asks the server to preview.</summary>
    public const int MaxPreviewUrls = 5;
}
