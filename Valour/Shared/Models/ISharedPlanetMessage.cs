namespace Valour.Shared.Models;

public interface ISharedPlanetMessage : ISharedMessage, ISharedPlanetItem
{
    /// <summary>
    /// The author's member ID
    /// </summary>
    long AuthorMemberId { get; set; }
}

