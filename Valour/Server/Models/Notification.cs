﻿using Valour.Shared.Models;

namespace Valour.Server.Models;

public class Notification : ISharedNotification
{
    /// <summary>
    /// The id of the notification
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// The user the notification was sent to
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// The planet (if any) the notification came from
    /// </summary>
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The channel (if any) the notification came from
    /// </summary>
    public long? ChannelId { get; set; }
    
    /// <summary>
    /// The source (if any) the notification came from
    /// This can represent different things, depending on the NotificationSource
    /// For example, if the source is a message, this will be the message id
    /// If it's a friend request, this will be the friend user's id
    /// </summary>
    public long? SourceId { get; set; }
    
    /// <summary>
    /// The source of the notification
    /// </summary>
    public NotificationSource Source { get; set; }
    
    /// <summary>
    /// The time at which the notification was sent
    /// </summary>
    public DateTime TimeSent { get; set; }
    
    /// <summary>
    /// The time at which the notification was read
    /// </summary>
    public DateTime? TimeRead { get; set; }
    
    /// <summary>
    /// The title of the notification
    /// </summary>
    public string Title { get; set; }
    
    /// <summary>
    /// The body of the notification
    /// </summary>
    public string Body { get; set; }
    
    /// <summary>
    /// The image url of the notification
    /// </summary>
    public string ImageUrl { get; set; }
    
    /// <summary>
    /// The url the user is brought to when the notification is clicked
    /// </summary>
    public string ClickUrl { get; set; }
}