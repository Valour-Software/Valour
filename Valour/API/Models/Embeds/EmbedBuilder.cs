using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using Valour.Api.Models.Messages.Embeds.Items;
using Valour.Api.Models.Messages.Embeds.Styles;
using Valour.Api.Nodes;
using Valour.Api.Models.Messages.Embeds.Styles.Basic;
using Valour.Api.Models.Messages.Embeds.Styles.Flex;
using Valour.Api.Models.Messages.Embeds.Styles.Bootstrap;
using System.Collections.Concurrent;

namespace Valour.Api.Models.Messages.Embeds;

public class EmbedBuilder
{
    public static List<EmbedItemType> AllowedTypesForNameStyles = new() { EmbedItemType.Text, EmbedItemType.DropDownMenu, EmbedItemType.InputBox };

    public Embed embed;

    public EmbedItem LastItem;

    public EmbedFormItem formItem;

    public EmbedProgressBar progressBar;

    public EmbedProgress progress;

    /// <summary>
    /// The current item that we are "in"
    /// </summary>
    public IParentItem CurrentParent;
    public ConcurrentDictionary<string, object> Data { get; set; }

    public EmbedBuilder()
    {
        embed = new();
        embed.Pages = new();
        embed.KeepPageOnUpdate = true;
        embed.StartPage = 0;
        progressBar = null;
        Data = new();
    }

    public EmbedPage CurrentPage
    {
        get
        {
            return embed.Pages.Last();
        }
    }

    public EmbedBuilder AddPage(string title = null, string footer = null)
    {
        EmbedPage page = new()
        {
            Title = title,
            Footer = footer,
            Children = new()
        };
        embed.Pages.Add(page);
        CurrentParent = page;
        LastItem = page;

        return this;
    }

	/// <summary>
	/// Adds a row
	/// </summary>
	/// <returns></returns>
	public EmbedBuilder AddRow()
    {
        var row = new EmbedRow()
        {
            Children = new()
        };

        if (CurrentParent.ItemType != EmbedItemType.EmbedRow)
        {
            row.Parent = CurrentParent;
            CurrentParent.Children.Add(row);
        }
        else
        {
            if (formItem is null)
            {
                row.Parent = CurrentPage;
                CurrentPage.Children.Add(row);
            }
            else
            {
                row.Parent = formItem;
                formItem.Children.Add(row);
            }
        }

		CurrentParent = row;
        LastItem = row;
        return this;
    }

	public EmbedBuilder WithRow()
	{
		var row = new EmbedRow()
		{
			Children = new(),
            Parent = CurrentParent
		};

		CurrentParent.Children.Add(row);
        CurrentParent = row;
        LastItem = row;
        return this;
	}

	/// <summary>
	/// You should only call this function if you are closing a row that was added with WithRow
	/// </summary>
	/// <returns></returns>
	public EmbedBuilder CloseRow()
    {
        CurrentParent = CurrentParent.Parent;
        return this;
    }

    public EmbedBuilder Close()
    {
        CurrentParent = CurrentParent.Parent;
        return this;
    }

	internal void AddItem(EmbedItem item)
    {
        item.Parent = CurrentParent;
        CurrentParent.Children.Add(item);
		LastItem = item;
    }

    /// <summary>
    /// Adds a button item to the current row of the current page.
    /// </summary>
    /// <returns></returns>
    public EmbedBuilder AddButton(string text = null)
    {
        var item = new EmbedButtonItem()
        {
            Children = new() { new EmbedTextItem(text) }
        };

        AddItem(item);
        return this;
    }

    /// <summary>
    /// Adds a button item to the current row of the current page. Make sure you call .Close() after you are done adding items to this button!
    /// </summary>
    /// <returns></returns>
    public EmbedBuilder AddButtonWithNoText()
    {
        var item = new EmbedButtonItem()
        {
            Children = new()
        };

        AddItem(item);
        CurrentParent = item;
        return this;
    }

    /// <summary>
    /// Tells the builder to stop adding new items to the current form.
    /// </summary>
    /// <returns></returns>

    public EmbedBuilder EndForm()
    {
		CurrentParent = formItem.Parent;
        formItem = null;
		return this;
    }

    /// <summary>
    /// Adds a form item; until EndForm() is called, all items will be added to the form instead of the builder.
    /// </summary>
    /// <returns></returns>
    public EmbedBuilder AddForm(string id)
    {
        var item = new EmbedFormItem()
        {
            Id = id,
            Children = new()
        };

        AddItem(item);
        CurrentParent = item;
        formItem = item;

		return this;
    }

    /// <summary>
    /// Adds a inputbox item to the current row of the current form. If FreelyBased, then
    /// this will add a inputbox item to the current form.
    /// </summary>
    /// <returns></returns>
    public EmbedBuilder AddInputBox(string id = null, string name = null, string placeholder = null, string value = null, bool? keepvalueonupdate = null)
    {
        var item = new EmbedInputBoxItem()
        {
            Value = value,
            Id = id,
            Placeholder = placeholder,
            KeepValueOnUpdate = keepvalueonupdate
        };

        if (name is not null)
            item.NameItem = new EmbedTextItem(name);

        AddItem(item);
        return this;
    }

    public EmbedBuilder AddText(string name, string text)
    {
        var item = new EmbedTextItem()
        {
            Text = text
        };

        if (name is not null)
        {
			item.NameItem = new EmbedTextItem(name)
			{
				Styles = new() { FontWeight.Bold }
			};
		}

		AddItem(item);
        return this;
    }

    public EmbedBuilder AddText(string text)
    {
        var item = new EmbedTextItem()
        {
            Text = text
        };

		AddItem(item);
        return this;
    }

	public EmbedBuilder WithName(string text = null)
	{
        var item = new EmbedTextItem(text);
		((INameable)LastItem).NameItem = item;
        item.Parent = LastItem;
        LastItem = item;

		return this;
	}

	public EmbedBuilder WithText(string text = null)
	{
		var item = new EmbedTextItem(text);
        LastItem.Children.Add(item);
		item.Parent = LastItem;
		LastItem = item;

        return this;
	}

	/// <summary>
	/// Adds a piece of media to the embed. Width and Height can be overriden by the Height and Width styles.
	/// </summary>
	/// <param name="width"></param>
	/// <param name="height"></param>
	/// <param name="mimetype"></param>
	/// <param name="filename"></param>
	/// <param name="location">Must be either from https://media.tenor.com or https://cdn.valour.gg</param>
	/// <returns></returns>
	public EmbedBuilder AddMedia(int width, int height, string mimetype, string filename, string location)
    {
        var item = new EmbedMediaItem()
        {
            Attachment = new()
            {
                Width = width,
                Height = height,
                MimeType = mimetype,
                FileName = filename,
                Location = location
            }
        };

        AddItem(item);
        return this;
    }

    public EmbedBuilder AddDropDownMenu(string id, string name = null, string value = "")
	{
		var item = new EmbedDropDownMenuItem()
		{
			Id = id,
            Value = value,
            Children = new()
		};

		if (name is not null)
			item.NameItem = new EmbedTextItem(name);

		AddItem(item);
        CurrentParent = item;
		return this;
	}

    public EmbedBuilder EndDropDownMenu()
    {
        CurrentParent = CurrentParent.Parent;
        return this;
    }

	public EmbedBuilder WithDropDownItem(string text = null)
	{
        if (CurrentParent.ItemType != EmbedItemType.DropDownMenu)
			throw new ArgumentException("You can not add a dropdown item without a drop down menu!");

		var item = new EmbedDropDownItem()
		{
			Children = new() { new EmbedTextItem(text) },
            Parent = CurrentParent
		};

		CurrentParent.Children.Add(item);
		LastItem = item;
        return this;
	}

    /// <summary>
    /// This function is intended for you to use WithText, etc afterwards
    /// </summary>
    /// <returns></returns>
	public EmbedBuilder WithDropDownItem()
	{
		if (CurrentParent.ItemType != EmbedItemType.DropDownMenu)
			throw new ArgumentException("You can not add a dropdown item without a drop down menu!");

		var item = new EmbedDropDownItem()
		{
			Children = new(),
            Parent = CurrentParent
		};

		CurrentParent.Children.Add(item);
        LastItem = item;
        return this;
	}

    public EmbedBuilder OnClickGoToEmbedPage(int page)
    {
        ((IClickable)LastItem).ClickTarget = new EmbedPageTarget()
        {
            Type = TargetType.EmbedPage,
            PageNumber = page
		};
        return this;
    }

	public EmbedBuilder OnCLickGoToLink(string href)
	{
		((IClickable)LastItem).ClickTarget = new EmbedLinkTarget()
		{
			Type = TargetType.Link,
			Href = href
		};
		return this;
	}

    /// <summary>
    /// 
    /// </summary>
    /// <param name="ElementId">The eventid that the Interaction will use</param>
    /// <returns></returns>
	public EmbedBuilder OnClickSendInteractionEvent(string ElementId)
	{
		((IClickable)LastItem).ClickTarget = new EmbedEventTarget()
		{
			Type = TargetType.Event,
			EventElementId = ElementId
		};
		return this;
	}


    /// <summary>
    /// 
    /// </summary>
    /// <param name="ElementId">The id that the button will use</param>
    /// <returns></returns>
	public EmbedBuilder OnClickSubmitForm(string ElementId = null)
	{
		((IClickable)LastItem).ClickTarget = new EmbedFormSubmitTarget()
		{
			Type = TargetType.SubmitForm,
            EventElementId = ElementId
		};
		return this;
	}

	public EmbedBuilder WithTitleStyles(params StyleBase[] styles)
	{
		var page = embed.Pages.Last();
		if (page.TitleStyles is null)
			page.TitleStyles = new();
		page.TitleStyles.AddRange(styles);
		return this;
	}

	public EmbedBuilder WithFooterStyles(params StyleBase[] styles)
    {
        var page = embed.Pages.Last();
        if (page.FooterStyles is null)
            page.FooterStyles = new();
        page.FooterStyles.AddRange(styles);
        return this;
    }

    public EmbedBuilder WithStyles(params StyleBase[] styles)
    {
        if (LastItem.Styles is null)
            LastItem.Styles = new();
        LastItem.Styles.AddRange(styles);
		return this;
    }

    /// <summary>
    /// Make sure to call .Close() after you are done adding a progressbar(s)!
    /// </summary>
    public EmbedBuilder AddProgress()
    {
        var item = new EmbedProgress()
        {
            Children = new()
        };

        progress = item;
        AddItem(item);
        CurrentParent = item;
        return this;
    }

    /// <summary>
    /// </summary>
    /// <param name="value">A number between 0 and 100</param>
    /// <returns></returns>
    public EmbedBuilder WithProgressBar(int value, bool showLabel = false)
    {
        var item = new EmbedProgressBar()
        {
            Value = value,
            ShowLabel = showLabel
        };

        progress.Children.Add(item);
        item.Parent = progress;
        LastItem = item;

        progressBar = item;
        return this;
    }

    public EmbedBuilder WithStripes()
    {
        if (progressBar is null)
            throw new ArgumentException("You can not add stripes to an item which is not a progress bar!");
        progressBar.IsStriped = true;
        return this;
    }

    public EmbedBuilder WithAnimatedStripes()
    {
        if (progressBar is null)
            throw new ArgumentException("You can not add animated stripes to an item which is not a progress bar!");
        progressBar.IsAnimatedStriped = true;
        progressBar.IsStriped = true;
        return this;
    }

    public EmbedBuilder WithBootStrapClasses(params BootstrapClass[] classes)
    {
        if (LastItem.Classes is null)
            LastItem.Classes = new();
        foreach (var _class in classes)
            LastItem.Classes.Add(_class);
        return this;
    }
}