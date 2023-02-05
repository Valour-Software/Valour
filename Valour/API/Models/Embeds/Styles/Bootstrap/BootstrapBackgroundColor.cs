using System.Text.Json.Serialization;
using Valour.Api.Models.Messages.Embeds.Styles.Basic;

namespace Valour.Api.Models.Messages.Embeds.Styles.Bootstrap;

public enum BackgroundColorType
{
    Success,
    Secondary,
    Primary,
    Danger,
    Warning,
    Info,
    Light,
    Dark
}

public class BootstrapBackgroundColorClass : BootstrapClass
{
    public static readonly BootstrapBackgroundColorClass Success = new BootstrapBackgroundColorClass(BackgroundColorType.Success);
    public static readonly BootstrapBackgroundColorClass Secondary = new BootstrapBackgroundColorClass(BackgroundColorType.Secondary);
    public static readonly BootstrapBackgroundColorClass Primary = new BootstrapBackgroundColorClass(BackgroundColorType.Primary);
    public static readonly BootstrapBackgroundColorClass Danger = new BootstrapBackgroundColorClass(BackgroundColorType.Danger);
    public static readonly BootstrapBackgroundColorClass Warning = new BootstrapBackgroundColorClass(BackgroundColorType.Warning);
    public static readonly BootstrapBackgroundColorClass Info = new BootstrapBackgroundColorClass(BackgroundColorType.Info);
    public static readonly BootstrapBackgroundColorClass Light = new BootstrapBackgroundColorClass(BackgroundColorType.Light);
    public static readonly BootstrapBackgroundColorClass Dark = new BootstrapBackgroundColorClass(BackgroundColorType.Dark);

    private readonly string[] _strings = new string[]
    {
        "bg-success",
        "bg-secondary",
        "bg-primary",
        "bg-danger",
        "bg-warning",
        "bg-info",
        "bg-light",
        "bg-dark"
    };

    [JsonPropertyName("v")]
    public BackgroundColorType Value { get; set; }
    
    public BootstrapBackgroundColorClass(BackgroundColorType value)
    {
        Value = value;
    }

    public override string ToString()
    {
        // Protect from updates or malformed data
        // causing exceptions by just ignoring
        // unknown styles
        if ((int)Value >= _strings.Length)
            return string.Empty;

        return _strings[(int)Value];
    }
}