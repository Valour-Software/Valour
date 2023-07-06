using Valour.Api.Models;

namespace Valour.Client.Windows;

/// <summary>
/// Interface for windows that relate to a specific planet
/// </summary>
public interface IPlanetWindow
{
    public Planet Planet { get; set; }
}