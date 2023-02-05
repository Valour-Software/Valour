using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Bootstrap;

[JsonDerivedType(typeof(BootstrapBackgroundColorClass), typeDiscriminator: 1)]
public abstract class BootstrapClass
{
}