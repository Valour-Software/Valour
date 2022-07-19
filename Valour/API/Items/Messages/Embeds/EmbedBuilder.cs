using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Items;

namespace Valour.Api.Items.Messages.Embeds;

public class EmbedBuilder
{
    public Embed embed;

    public EmbedFormItem FormItem;

    public EmbedBuilder(EmbedItemPlacementType embedType)
    {
        embed = new()
        {
            EmbedType = embedType
        };
    }

    [JsonIgnore]
    public EmbedPage CurrentPage
    {
        get
        {
            return embed.Pages.Last();
        }
    }

    public EmbedBuilder AddPage(string title = null, string footer = null, string titlecolor = null, string footercolor = null)
    {
        EmbedPage page = new()
        {
            Title = title,
            Footer = footer,
            TitleColor = titlecolor,
            FooterColor = footercolor
        };
        embed.Pages.Add(page);
        return this;
    }

    public EmbedBuilder AddRow(params EmbedItem[] items)
    {
        var row = new EmbedRow()
        {
            Items = items.ToList()
        };
        if (FormItem is not null)
            FormItem.Rows.Add(row);
        else
            embed.Pages.Last().Rows.Add(row);
        return this;
    }

    /// <summary>
    /// Adds a row to the current page.
    /// </summary>
    /// <returns></returns>

    public EmbedBuilder AddRow()
    {
        if (FormItem is not null)
            FormItem.Rows.Add(new());
        else
            embed.Pages.Last().Rows.Add(new());
        return this;
    }

    /// <summary>
    /// Adds a text item to the current row of the current page. If FreelyBased, then
    /// this will add a text item to the current page.
    /// </summary>
    /// <returns></returns>
    public EmbedBuilder AddText(string name = null, string text = null, string textColor = "eeeeee", int x = 0, int y = 0)
    {
        var item = new EmbedTextItem()
        {
            Name = name,
            Text = text,
            TextColor = textColor
        };

        if (embed.EmbedType == EmbedItemPlacementType.FreelyBased || (FormItem is not null && FormItem.ItemPlacementType == EmbedItemPlacementType.FreelyBased))
        {
            item.X = x;
            item.Y = y;
            if (FormItem is not null)
                FormItem.AddItem(item);
            else
                embed.Pages.Last().Items.Add(item);
        }
        else
        {
            if (FormItem is not null)
                FormItem.AddItem(item);
            else
                embed.Pages.Last().Rows.Last().Items.Add(item);
        }
        return this;
    }
}