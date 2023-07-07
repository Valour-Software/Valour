namespace Valour.Shared.Models;

public class ReportReason
{
    public ReportReasonCode Code { get; set; }
    public string Description { get; set; }
}

public static class ReportReasons
{
    public static readonly ReportReason PromotingIllegal = new()
    {
        Code = ReportReasonCode.IsPromotingIllegal,
        Description = "User is promoting, supporting, or describing engagement in illegal activity."
    };
    
    public static readonly ReportReason MinorSexualContent = new()
    {
        Code = ReportReasonCode.IsMinorSexualContent,
        Description = "User is posting or sharing sexual content including a minor, whether real or simulated."
    };

    public static readonly ReportReason TerroristContent = new()
    {
        Code = ReportReasonCode.IsTerroristContent,
        Description = "User is supporting, engaging in, or threatening terrorist activity."
    };

    public static readonly ReportReason TargetedHarassment = new()
    {
        Code = ReportReasonCode.IsTargetedHarassment,
        Description = "User is repeatedly harassing another user beyond banter or comedy after being asked to stop."
    };

    public static readonly ReportReason IsUnderageUser = new()
    {
        Code = ReportReasonCode.IsUnderageUser,
        Description =
            "User lied during registration, or account belongs to a user that is under 13 or legally required age to use Valour."
    };
    
    public static readonly ReportReason ThreatsOrViolence = new()
    {
        Code = ReportReasonCode.IsThreatsOrViolence,
        Description = "User is engaging in violent activity or is threatening to harm another user or individual."
    };

    public static readonly ReportReason Spam = new()
    {
        Code = ReportReasonCode.IsSpam,
        Description = "User is a spam bot or engages in spam or similar activities."
    };
    
    public static readonly ReportReason ScamOrFraud = new()
    {
        Code = ReportReasonCode.IsScamOrFraud,
        Description = "User is attempting to scam or defraud other users."
    };
    
    public static readonly ReportReason BanEvasion = new()
    {
        Code = ReportReasonCode.IsBanEvasion,
        Description = "User is evading a ban or suspension."
    };
    
    public static readonly ReportReason NonConsensualImages = new()
    {
        Code = ReportReasonCode.IsNonConsensualImages,
        Description = "User is posting or sharing non-consensual images or videos, not limited to sexual material."
    };
    
    public static readonly ReportReason ValourBugAbuse = new()
    {
        Code = ReportReasonCode.IsValourBugAbuse,
        Description = "User is abusing a bug or exploit in Valour. Please detail the bug as best as possible."
    };
    
    public static readonly ReportReason Other = new()
    {
        Code = ReportReasonCode.Other,
        Description = "Another issue which breaks the Valour Platform Rules or Terms of Service"
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
public interface ISharedReportModel
{
    /// <summary>
    /// Guid Id of the report
    /// </summary>
    string Id { get; set; }
    
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
    long MessageId { get; set; }
    
    /// <summary>
    /// The channel id (if any) the report applies to
    /// </summary>
    long ChannelId { get; set; }
    
    /// <summary>
    /// The planet id (if any) the report applies to
    /// </summary>
    long PlanetId { get; set; }
    
    /// <summary>
    /// The category-code of the reason of the report
    /// </summary>
    ReportReason ReasonCode { get; set; }
    
    /// <summary>
    /// The user-written reason for the report
    /// </summary>
    string LongReason { get; set; }
}