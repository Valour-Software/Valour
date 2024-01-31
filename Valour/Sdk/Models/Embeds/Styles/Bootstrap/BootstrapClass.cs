using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds.Styles.Bootstrap;

[JsonDerivedType(typeof(BootstrapBackgroundColorClass), typeDiscriminator: 1)]
public abstract class BootstrapClass
{
}