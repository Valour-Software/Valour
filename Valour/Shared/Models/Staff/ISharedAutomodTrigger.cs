namespace Valour.Shared.Models.Staff;

public enum AutomodTriggerType
{
    Blacklist = 0,
    Spam = 1,
    Join = 2, // For when a user joins the server. Not necessarily a bad thing, but can be used to trigger automod actions!
    Command = 3, // Allows custom commands to be triggered by automod
}

public interface ISharedAutomodTrigger
{
    /// <summary>
    /// The id of this automod trigger
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// The id of the member that added this trigger
    /// </summary>
    public long MemberAddedBy { get; set; }
    
    /// <summary>
    /// The automod trigger type
    /// </summary>
    public AutomodTriggerType Type { get; set; }
    
    /// <summary>
    /// The name of this automod trigger
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// The words, if applicable, that will trigger this automod trigger (comma separated)
    /// For commands, this will be the command name (no need to include the slash)
    /// </summary>
    public string? TriggerWords { get; set; }
}