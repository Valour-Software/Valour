using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Items;

namespace Valour.Api.Items.Messages.Embeds;

public class EmbedBuilder
{
    public Embed embed;

    public EmbedFormItem FormItem;

    public EmbedBuilder(EmbedItemPlacementType embedType, int? width = null, int? height = null)
    {
        embed = new()
        {
            EmbedType = embedType
        };
        embed.Pages = new();
        if (embedType == EmbedItemPlacementType.FreelyBased)
        {
            if (width is null)
                throw new ArgumentException("Width cannot be null if the placement type if FreelyBased!");
            if (height is null)
                throw new ArgumentException("Height cannot be null if the placement type if FreelyBased!");
            embed.Width = width;
            embed.Height = height;
        }
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
        if (embed.EmbedType == EmbedItemPlacementType.RowBased)
            page.Rows = new();
        else 
            page.Items = new();
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
    /// Adds a button item to the current row of the current page. If FreelyBased, then
    /// this will add a button item to the current page.
    /// </summary>
    /// <returns></returns>
    public EmbedBuilder AddButton(string id = null, string text = null, string color = null, string textColor = null, string link = null, string itemEvent = null, EmbedItemSize size = EmbedItemSize.Normal, int? x = null, int? y = null, bool? isSubmitButton = null)
    {
        var item = new EmbedButtonItem()
        {
            Id = id,
            Text = text,
            TextColor = textColor,
            Color = color,
            Link = link,
            IsSubmitButton = isSubmitButton,
            Event = itemEvent,
            Size = size,
        };

        AddItem(item, x, y);
        return this;
    }

    /// <summary>
    /// Tells the builder to stop adding new items to the current form.
    /// </summary>
    /// <returns></returns>

    public EmbedBuilder EndForm()
    {
        FormItem = null;
        return this;
    }

    /// <summary>
    /// Adds a form item; until EndForm() is called, all items will be added to the form instead of the builder.
    /// </summary>
    /// <returns></returns>
    public EmbedBuilder AddForm(EmbedItemPlacementType placementtype, string id)
    {
        var item = new EmbedFormItem()
        {
            ItemPlacementType = placementtype,
            Id = id
        };

        if (placementtype == EmbedItemPlacementType.RowBased)
            item.Rows = new();
        else
            item.Items = new();

        AddItem(item, null, null);

        FormItem = item;
        return this;
    }

    /// <summary>
    /// Adds a inputbox item to the current row of the current form. If FreelyBased, then
    /// this will add a inputbox item to the current form.
    /// </summary>
    /// <returns></returns>
    public EmbedBuilder AddInputBox(string id = null, string name = null, string placeholder = null, EmbedItemSize size = EmbedItemSize.Normal, string namecolor = null, string value = null, int? x = null, int? y = null)
    {
        var item = new EmbedInputBoxItem()
        {
            Value = value,
            Id = id,
            Size = size,
            Placeholder = placeholder,
            Name = name,
            NameColor = namecolor
        };

        AddItem(item, x, y);
        return this;
    }

    /// <summary>
    /// Adds a text item to the current row of the current page/form. If FreelyBased, then
    /// this will add a text item to the current page/form.
    /// </summary>
    /// <returns></returns>
    public EmbedBuilder AddText(string name = null, string text = null, string textColor = null, string link = null, int? x = null, int? y = null)
    {
        var item = new EmbedTextItem()
        {
            Name = name,
            Text = text,
            TextColor = textColor,
            Link = link
        };

        AddItem(item, x, y);
        return this;
    }

    internal void AddItem(EmbedItem item, int? x, int? y)
    {
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
    }
}