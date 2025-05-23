﻿namespace Valour.Shared.Models;

public interface ISharedPlanetBan : ISharedPlanetModel<long>
{
    /// <summary>
    /// The user that was banned
    /// </summary>
    long TargetId { get; set; }

    /// <summary>
    /// The user that banned the user
    /// </summary>
    long IssuerId { get; set; }

    /// <summary>
    /// The reason for the ban
    /// </summary>
    string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time the ban expires. Null for permanent.
    /// </summary>
    DateTime? TimeExpires { get; set; }

    /// <summary>
    /// True if the ban never expires
    /// </summary>
    public bool Permanent => TimeExpires == null;
}

