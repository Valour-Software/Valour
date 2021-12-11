using System;
using System.Collections.Generic;

namespace Valour.Api.Items.Messages;
public class EmbedFormDataItem
{
    public string ElementId { get; set; }
    public string Value { get; set; }
    public EmbedItemType Type { get; set; }
}

public class InteractionEvent
{
    public string Event { get; set; }
    public string ElementId { get; set; }
    public ulong PlanetId { get; set; }
    public ulong MessageId { get; set; }
    public ulong AuthorMemberId { get; set; }
    public ulong MemberId { get; set; }
    public ulong ChannelId { get; set; }
    public DateTime TimeInteracted { get; set; }
    public List<EmbedFormDataItem> FormData { get; set; }
}

public class Color
{
    public int R;
    public int G;
    public int B;
    public Color(int r, int g, int b)
    {
        R = r;
        G = g;
        B = b;
    }
}

public enum EmbedSize
{
    Big,
    Normal,
    Small,
    VerySmall,
    Short,
    VeryShort
}

public enum EmbedItemType
{
    Text,
    Button,
    InputBox
}

public class ClientEmbedItem
{

    /// <summary>
    /// The type of this embed item
    /// </summary>
    public EmbedItemType Type { get; set; }

    /// <summary>
    /// The text within the embed.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Name of the embed. Not required.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// If this component should be inlined
    /// </summary>
    public bool Inline { get; set; }

    /// <summary>
    /// The link this component leads to
    /// </summary>
    public string Link { get; set; }

    /// <summary>
    /// Must be in hex format, example: "ffffff"
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// The color (hex) of this embed item's text
    /// </summary>
    public string TextColor { get; set; }

    /// <summary>
    /// True if this item should be centered
    /// </summary>
    public bool Center { get; set; }

    /// <summary>
    /// The size of this embed item
    /// </summary>
    public EmbedSize Size { get; set; }

    /// <summary>
    /// Used to identify this embed item for events and more
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The input value
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// The placeholder text for inputs
    /// </summary>
    public string Placeholder { get; set; }
}
/// <summary>
/// This class exists render embeds
/// </summary>
public class ClientEmbed
{
    public List<ClientEmbedItem> Items = new();
    public List<List<ClientEmbedItem>> Pages = new();
    public ClientEmbed()
    {
    }
}

public class EmbedPageBuilder
{
    public List<ClientEmbedItem> Items = new();

    public EmbedPageBuilder AddText(string name = "", string text = "", bool inline = false, string textColor = "ffffff")
    {
        ClientEmbedItem item = new()
        {
            Type = EmbedItemType.Text,
            Text = text,
            Inline = inline,
            TextColor = textColor
        };
        if (string.IsNullOrEmpty(name)) item.Name = name;
        Items.Add(item);
        return this;
    }

    public EmbedPageBuilder AddButton(string id = "", string text = "", string name = "", string link = "", string color = "000000", string textColor = "ffffff", EmbedSize size = EmbedSize.Normal, bool center = false, bool inline = false)
    {
        ClientEmbedItem item = new()
        {
            Type = EmbedItemType.Button,
            Text = text,
            Color = color,
            Inline = inline,
            TextColor = textColor,
            Size = size,
            Center = center,
            Id = id
        };
        if (string.IsNullOrEmpty(name)) item.Name = name;
        if (string.IsNullOrEmpty(link)) item.Link = link;
        Items.Add(item);
        return this;
    }

    public EmbedPageBuilder AddInputBox(string placeholder = "", string name = "", string nameTextColor = "", string id = "", bool inline = false, EmbedSize size = EmbedSize.Normal)
    {
        ClientEmbedItem item = new()
        {
            Type = EmbedItemType.InputBox,
            Placeholder = placeholder,
            Inline = inline,
            TextColor = nameTextColor,
            Id = id,
            Size = size
        };
        if (string.IsNullOrEmpty(name)) item.Name = name;
        Items.Add(item);
        return this;
    }
}

public class EmbedBuilder
{
    public ClientEmbed Embed = new();

    public EmbedBuilder()
    {
    }

    public EmbedBuilder AddPage(EmbedPageBuilder page)
    {
        Embed.Pages.Add(page.Items);
        return this;
    }

    public EmbedBuilder AddText(string name = "", string text = "", bool inline = false, string textColor = "ffffff")
    {
        ClientEmbedItem item = new()
        {
            Type = EmbedItemType.Text,
            Text = text,
            Inline = inline,
            TextColor = textColor
        };
        if (string.IsNullOrEmpty(name)) item.Name = name;
        Embed.Items.Add(item);
        return this;
    }

    public EmbedBuilder AddButton(string id = "", string text = "", string name = "", string link = "", string color = "000000", string textColor = "ffffff", EmbedSize size = EmbedSize.Normal, bool center = false, bool inline = false)
    {
        ClientEmbedItem item = new()
        {
            Type = EmbedItemType.Button,
            Text = text,
            Color = color,
            Inline = inline,
            TextColor = textColor,
            Size = size,
            Center = center,
            Id = id
        };
        if (string.IsNullOrEmpty(name)) item.Name = name;
        if (string.IsNullOrEmpty(link)) item.Link = link;
        Embed.Items.Add(item);
        return this;
    }

    public EmbedBuilder AddInputBox(string placeholder = "", string name = "", string nameTextColor = "", string id = "", bool inline = false, EmbedSize size = EmbedSize.Normal)
    {
        ClientEmbedItem item = new()
        {
            Type = EmbedItemType.InputBox,
            Placeholder = placeholder,
            Inline = inline,
            TextColor = nameTextColor,
            Id = id,
            Size = size
        };
        if (string.IsNullOrEmpty(name)) item.Name = name;

        Embed.Items.Add(item);
        return this;
    }
}