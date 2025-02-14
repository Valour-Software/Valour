using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class UserProfile : ClientModel<UserProfile, long>, ISharedUserProfile
{
    public override string BaseRoute => "api/userProfiles";

    /// <summary>
    /// The 'headline' is the short top text in the profile
    /// </summary>
    public string Headline { get; set; }
    
    /// <summary>
    /// The bio of the profile. Supports markdown.
    /// </summary>
    public string Bio { get; set; }
    
    /// <summary>
    /// The simple border color of the profile.
    /// </summary>
    public string BorderColor { get; set; }
    
    /// <summary>
    /// The glow color of the profile
    /// </summary>
    public string GlowColor { get; set; }
    
    /// <summary>
    /// Primary color, used in border and other details
    /// </summary>
    public string PrimaryColor { get; set; }
    
    /// <summary>
    /// Secondary color, used in border and other details
    /// </summary>
    public string SecondaryColor { get; set; }
    
    /// <summary>
    /// Tertiary color, used in border and other details
    /// </summary>
    public string TertiaryColor { get; set; }
    
    /// <summary>
    /// The color of the main text
    /// </summary>
    public string TextColor { get; set; }
    
    /// <summary>
    /// True if the border should be animated
    /// </summary>
    public bool AnimatedBorder { get; set; }
    
    /// <summary>
    /// The background image for the profile (should be 300x400)
    /// </summary>
    public string BackgroundImage { get; set; }

    public override UserProfile AddToCache()
    {
        return Client.Cache.UserProfiles.Put(Id, this);
    }
    
    public override UserProfile RemoveFromCache()
    {
        return Client.Cache.UserProfiles.TakeAndRemove(Id);
    }
}