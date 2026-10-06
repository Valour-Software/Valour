namespace Valour.Client.Utility;

public static class AnimatedImageStyle
{
    public const string HoverClass = "anim-hover";

    public static string Build(string imageUrl, string fallbackUrl, string animatedUrl = null)
    {
        if (string.IsNullOrWhiteSpace(animatedUrl))
            return $"background-image: url({imageUrl}), url({fallbackUrl});";

        return $"--anim-image: url({animatedUrl}); background-image: var(--anim-active, none), url({imageUrl}), url({fallbackUrl});";
    }
}
