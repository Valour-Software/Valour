namespace Valour.Sdk.Models;

public enum FriendEventType
{
    FetchedAll, // We fetched all friends
    AddedThem, // We added someone
    RemovedThem, // We removed someone
    DeclinedThem, // We declined a friend request
    CancelledThem, // We cancelled a friend request
    AddedMe, // Someone added us
    RemovedMe, // Someone removed us
    DeclinedMe, // Someone declined our friend request
    CancelledMe // Someone cancelled a friend request to us
}


public class FriendEventData
{
    /// <summary>
    /// The user the event is about
    /// </summary>
    public User User { get; set; }
    
    /// <summary>
    /// The type of friend event
    /// </summary>
    public FriendEventType Type { get; set; }
}