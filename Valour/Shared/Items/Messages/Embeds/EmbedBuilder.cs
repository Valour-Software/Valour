namespace Valour.Shared.Items.Messages.Embeds;

public class EmbedPageBuilder
{
    public List<EmbedItem> Items = new();

    public EmbedPageBuilder AddText(string name = "", string text = "", bool inline = false, string textColor = "eeeeee")
    {
        EmbedItem item = new()
        {
            Type = EmbedItemType.Text,
            Text = text,
            Inline = inline,
            TextColor = textColor
        };
        if (!string.IsNullOrEmpty(name)) item.Name = name;
        Items.Add(item);
        return this;
    }

    public EmbedPageBuilder AddButton(string id = "", string text = "", string name = "", string link = "", string color = "000000", string textColor = "eeeeee", EmbedItemSize size = EmbedItemSize.Normal, bool center = false, bool inline = false)
    {
        EmbedItem item = new()
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

    public EmbedPageBuilder AddInputBox(string placeholder = "", string name = "", string nameTextColor = "", string id = "", bool inline = false, EmbedItemSize size = EmbedItemSize.Normal)
    {
        EmbedItem item = new()
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
    public List<EmbedItem> Items = new();
    public List<List<EmbedItem>> Pages = new();

    public string? Title { get; set; }
    public string? Footer { get; set; }

    /// <summary>
    /// The color (hex) of this embed item's text
    /// </summary>
    public string TitleColor = "eeeeee";

    /// <summary>
    /// The color (hex) of this embed item's text
    /// </summary>
    public string FooterColor = "eeeeee";

    public EmbedBuilder()
    {
    }

    public Embed Generate()
    {
        EmbedItem[][] pages = new EmbedItem[Pages.Count][];

        for (int i = 0; i < Pages.Count; i++)
        {
            pages[i] = Pages[i].ToArray();
        }

        return new Embed()
        {
            Pages = pages,
            Title = Title,
            TitleColor = TitleColor,
            Footer = Footer,
            FooterColor = FooterColor
        };
    }

    public EmbedBuilder AddPage(EmbedPageBuilder page)
    {
        Pages.Add(page.Items);
        return this;
    }

    public EmbedBuilder AddText(string name = "", string text = "", bool inline = false, string textColor = "eeeeee")
    {
        EmbedItem item = new()
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

    public EmbedBuilder AddButton(string id = "", string text = "", string name = "", string link = "", string color = "000000", string textColor = "eeeeee", EmbedItemSize size = EmbedItemSize.Normal, bool center = false, bool inline = false)
    {
        EmbedItem item = new()
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

    public EmbedBuilder AddInputBox(string placeholder = "", string name = "", string nameTextColor = "", string id = "", bool inline = false, EmbedItemSize size = EmbedItemSize.Normal)
    {
        EmbedItem item = new()
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

