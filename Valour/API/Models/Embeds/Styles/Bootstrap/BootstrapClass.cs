using System.Text.Json.Serialization;

namespace Valour.Api.Models.Messages.Embeds.Styles.Bootstrap;

[JsonDerivedType(typeof(BootstrapBackgroundColorClass), typeDiscriminator: 1)]
public abstract class BootstrapClass
{
}