namespace Valour.Shared.Models;

public enum FriendEventType
{
    FetchedAll,      // We fetched all friends
    AddedThem,       // We added someone
    RemovedThem,     // We removed someone
    DeclinedThem,    // We declined a friend request
    CancelledThem,   // We cancelled a friend request
    AddedMe,         // Someone added us
    RemovedMe,       // Someone removed us
    DeclinedMe,      // Someone declined our friend request
    CancelledMe      // Someone cancelled a friend request to us
}
