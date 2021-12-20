using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets;

public interface ISharedPlanetBan
{
    /// <summary>
    /// The user that was panned
    /// </summary>
    [JsonPropertyName("User_Id")]
    public ulong User_Id { get; set; }

    /// <summary>
    /// The planet the user was within
    /// </summary>
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The user that banned the user
    /// </summary>
    [JsonPropertyName("Banner_Id")]
    public ulong Banner_Id { get; set; }

    /// <summary>
    /// The reason for the ban
    /// </summary>
    [JsonPropertyName("Reason")]
    public string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    [JsonPropertyName("Time")]
    public DateTime Time { get; set; }

    /// <summary>
    /// The length of the ban
    /// </summary>
    [JsonPropertyName("Minutes")]
    public uint? Minutes { get; set; }

    /// <summary>
    /// True if the ban never expires
    /// </summary>
    [NotMapped]
    public bool Permanent => Minutes == null;
}

