using Valour.Shared;
using Valour.Shared.Items.Planets;

namespace Valour.Server.Models;

public class Planet : Item, ISharedPlanet
{
    ///////////////
    // Constants //
    ///////////////

    /// <summary>
    /// The maximum planets a user is allowed to have. This will increase after 
    /// the alpha period is complete.
    /// </summary>
    [JsonIgnore] 
    public const int MaxOwnedPlanets = 5;

    /// <summary>
    /// The maximum planets a user is allowed to join. This will increase after the 
    /// alpha period is complete.
    /// </summary>
    [JsonIgnore] 
    public const int MaxJoinedPlanets = 20;

    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    public long OwnerId { get; set; }

    /// <summary>
    /// The name of this planet
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The image url for the planet 
    /// </summary>
    public string IconUrl { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// If the server requires express allowal to join a planet
    /// </summary>
    public bool Public { get; set; }

    /// <summary>
    /// If the server should show up on the discovery tab
    /// </summary>
    public bool Discoverable { get; set; }

    /// <summary>
    /// The default role for the planet
    /// </summary>
    public long DefaultRoleId { get; set; }

    /// <summary>
    /// The id of the main channel of the planet
    /// </summary>
    public long PrimaryChannelId { get; set; }

    [JsonIgnore] public static Regex nameRegex = new Regex(@"^[\.a-zA-Z0-9 _-]+$");

    /// <summary>
    /// Validates that a given name is allowable for a planet
    /// </summary>
    public static TaskResult ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new TaskResult(false, "Planet names cannot be empty.");
        }

        if (name.Length > 32)
        {
            return new TaskResult(false, "Planet names must be 32 characters or less.");
        }

        if (!nameRegex.IsMatch(name))
        {
            return new TaskResult(false, "Planet names may only include letters, numbers, dashes, and underscores.");
        }

        return new TaskResult(true, "The given name is valid.");
    }

    /// <summary>
    /// Validates that a given description is alloweable for a planet
    /// </summary>
    public static TaskResult ValidateDescription(string description)
    {
        if (description is not null && description.Length > 128)
        {
            return new TaskResult(false, "Description must be under 128 characters.");
        }

        return TaskResult.SuccessResult;
    }

}