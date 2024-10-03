using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds.Items;

public enum TargetType
{
	Link,
	EmbedPage,
	Event,
	SubmitForm
}

[JsonDerivedType(typeof(EmbedLinkTarget), typeDiscriminator: 1)]
[JsonDerivedType(typeof(EmbedPageTarget), typeDiscriminator: 2)]
[JsonDerivedType(typeof(EmbedEventTarget), typeDiscriminator: 3)]
[JsonDerivedType(typeof(EmbedFormSubmitTarget), typeDiscriminator: 4)]
public abstract class EmbedClickTargetBase 
{
	[JsonPropertyName("t")]
	public TargetType Type { get; set; }

	public EmbedClickTargetBase() { }
}

public class EmbedLinkTarget : EmbedClickTargetBase
{
	[JsonPropertyName("h")]
	public string Href { get; set; }
}

public class EmbedPageTarget : EmbedClickTargetBase
{
	[JsonPropertyName("p")]
	public int PageNumber { get; set; }
}

public class EmbedEventTarget : EmbedClickTargetBase
{
	[JsonPropertyName("e")]
	public string EventElementId { get; set; }
}

public class EmbedFormSubmitTarget : EmbedClickTargetBase
{
    [JsonPropertyName("e")]
    public string EventElementId { get; set; }
}