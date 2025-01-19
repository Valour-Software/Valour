namespace Valour.Shared.Models;

public class ReportReason
{
    public ReportReasonCode Code { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
}

public static class ReportReasons
{
    public static readonly ReportReason PromotingIllegal = new()
    {
        Title = "Promoting Illegal Activity",
        Code = ReportReasonCode.IsPromotingIllegal,
        Description = "User is promoting, supporting, or describing engagement in illegal activity."
    };
    
    public static readonly ReportReason MinorSexualContent = new()
    {
        Title = "Minor Sexual Content",
        Code = ReportReasonCode.IsMinorSexualContent,
        Description = "User is posting or sharing sexual content including a minor, whether real or simulated."
    };

    public static readonly ReportReason TerroristContent = new()
    {
        Title = "Terrorist Content",
        Code = ReportReasonCode.IsTerroristContent,
        Description = "User is supporting, engaging in, or threatening terrorist activity."
    };

    public static readonly ReportReason TargetedHarassment = new()
    {
        Title = "Targeted Harassment",
        Code = ReportReasonCode.IsTargetedHarassment,
        Description = "User is repeatedly harassing another user beyond banter or comedy after being asked to stop."
    };

    public static readonly ReportReason UnderageUser = new()
    {
        Title = "Underage User",
        Code = ReportReasonCode.IsUnderageUser,
        Description =
            "User lied during registration, or account belongs to a user that is under 13 or legally required age to use Valour."
    };
    
    public static readonly ReportReason ThreatsOrViolence = new()
    {
        Title = "Threats or Violence",
        Code = ReportReasonCode.IsThreatsOrViolence,
        Description = "User is engaging in violent activity or is threatening to harm another user or individual."
    };

    public static readonly ReportReason Spam = new()
    {
        Title = "Spam",
        Code = ReportReasonCode.IsSpam,
        Description = "User is a spam bot or engages in spam or similar activities."
    };
    
    public static readonly ReportReason ScamOrFraud = new()
    {
        Title = "Scam or Fraud",
        Code = ReportReasonCode.IsScamOrFraud,
        Description = "User is attempting to scam or defraud other users."
    };
    
    public static readonly ReportReason BanEvasion = new()
    {
        Title = "Ban Evasion",
        Code = ReportReasonCode.IsBanEvasion,
        Description = "User is evading a ban or suspension."
    };
    
    public static readonly ReportReason NonConsensualImages = new()
    {
        Title = "Non Consensual Images",
        Code = ReportReasonCode.IsNonConsensualImages,
        Description = "User is posting or sharing non-consensual images or videos, not limited to sexual material."
    };
    
    public static readonly ReportReason ValourBugAbuse = new()
    {
        Title = "Valour Bug Abuse",
        Code = ReportReasonCode.IsValourBugAbuse,
        Description = "User is abusing a bug or exploit in Valour. Please detail the bug as best as possible."
    };
    
    public static readonly ReportReason Other = new()
    {
        Title = "Other Issues",
        Code = ReportReasonCode.Other,
        Description = "Another issue which breaks the Valour Platform Rules or Terms of Service"
    };
    
    public static readonly ReportReason[] Reasons = new[]
    {
        PromotingIllegal,
        MinorSexualContent,
        TerroristContent,
        TargetedHarassment,
        UnderageUser,
        ThreatsOrViolence,
        Spam,
        ScamOrFraud,
        BanEvasion,
        NonConsensualImages,
        ValourBugAbuse,
        Other
    };

}

public enum ReportReasonCode : long
{
    IsPromotingIllegal      = 0x00,
    IsMinorSexualContent    = 0x01,
    IsTerroristContent      = 0x02,
    IsTargetedHarassment    = 0x04,
    IsUnderageUser          = 0x08,
    IsThreatsOrViolence     = 0x10,
    IsSpam                  = 0x20,
    IsScamOrFraud           = 0x40,
    IsBanEvasion            = 0x80,
    IsNonConsensualImages   = 0x100,
    IsValourBugAbuse        = 0x200,
    Other                   = 0x1000000,
}

/// <summary>
/// Reports are used to keep the community safe for everyone
/// </summary>
public interface ISharedReport : ISharedModel<string>
{
    /// <summary>
    /// The time the report was created
    /// </summary>
    DateTime TimeCreated { get; set; }
    
    /// <summary>
    /// The user who sent the report
    /// </summary>
    long ReportingUserId { get; set; }
    
    /// <summary>
    /// The message id (if any) the report applies to
    /// </summary>
    long? MessageId { get; set; }
    
    /// <summary>
    /// The channel id (if any) the report applies to
    /// </summary>
    long? ChannelId { get; set; }
    
    /// <summary>
    /// The planet id (if any) the report applies to
    /// </summary>
    long? PlanetId { get; set; }
    
    /// <summary>
    /// The category-code of the reason of the report
    /// </summary>
    ReportReasonCode ReasonCode { get; set; }
    
    /// <summary>
    /// The user-written reason for the report
    /// </summary>
    string LongReason { get; set; }
    
    /// <summary>
    /// If the report has been reviewed by a moderator
    /// </summary>
    bool Reviewed { get; set; }
}