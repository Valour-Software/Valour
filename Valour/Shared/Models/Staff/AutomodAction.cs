namespace Valour.Shared.Models.Staff;

public enum AutomodActionType
{
    Kick,
    Ban,
    AddRole,
    RemoveRole,
    DeleteMessage,
    Respond
}

public interface ISharedAutomodAction
{
    /// <summary>
    /// The id of the action
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// How many strikes a user must have before this action fires
    /// </summary>
    public int Strikes { get; set; }

    /// <summary>
    /// If true, strikes are counted across all triggers
    /// </summary>
    public bool UseGlobalStrikes { get; set; }
    
    /// <summary>
    /// The id of the trigger that runs this action
    /// </summary>
    public Guid TriggerId { get; set; }
    
    /// <summary>
    /// The id of the member that added this action
    /// </summary>
    public long MemberAddedBy { get; set; }
    
    /// <summary>
    /// The type of action to take
    /// </summary>
    public AutomodActionType ActionType { get; set; }
    
    /// <summary>
    /// The id of the planet this action is for
    /// </summary>
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The id of the member this action is for
    /// </summary>
    public long TargetMemberId { get; set; }
    
    /// <summary>
    /// The id of the message, if any, this action is for
    /// </summary>
    public long? MessageId { get; set; }
    
    /// <summary>
    /// The role to add or remove 
    /// </summary>
    public long? RoleId { get; set; }
    
    /// <summary>
    /// When the action should expire, if applicable
    /// </summary>
    public DateTime? Expires { get; set; }
    
    
    /// <summary>
    /// The message to send to the chat, if applicable
    /// </summary>
    public string Message { get; set; }
}