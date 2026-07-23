using Valour.Sdk.Models.Embeds.Items;
using Valour.Sdk.Models.Embeds.Styles;
using Valour.Shared.Models;

namespace Valour.Sdk.Models.Embeds;

/// <summary>
/// Fluent builder for embeds. Items are added to the most recently opened
/// container (page, row, or form); modifiers like <see cref="WithStyle"/> and
/// <see cref="OnClickLink"/> apply to the most recently added item.
/// </summary>
/// <example>
/// var embed = new EmbedBuilder()
///     .AddPage("Signup")
///         .AddForm("signup-form")
///             .AddInputBox("name", name: "Your Name")
///             .AddButton("Submit").OnClickSubmitForm("signup-submitted")
///         .EndForm()
///     .Build();
/// </example>
public sealed class EmbedBuilder
{
    private readonly Embed _embed = new();
    private EmbedPage? _currentPage;
    private enum ContainerKind { Page, Row, Form }

    private readonly Stack<(ContainerKind Kind, List<EmbedItem> Children)> _containers = new();
    private EmbedItem? _lastItem;
    private EmbedDropDownItem? _currentDropDown;
    private EmbedProgressItem? _currentProgress;
    private EmbedChartItem? _currentChart;

    /// <summary>
    /// Finishes the embed: closes any open containers, validates, and
    /// returns the built <see cref="Embed"/>. Throws if validation fails.
    /// </summary>
    public Embed Build()
    {
        _containers.Clear();

        var result = EmbedParser.Validate(_embed);
        if (!result.Success)
            throw new InvalidOperationException($"Embed is invalid: {result.Message}");

        return _embed;
    }

    // ---- Embed-level settings ----

    public EmbedBuilder WithEmbedId(string id)
    {
        _embed.Id = id;
        return this;
    }

    public EmbedBuilder WithEmbedName(string name)
    {
        _embed.Name = name;
        return this;
    }

    public EmbedBuilder WithStartPage(int pageIndex)
    {
        _embed.StartPage = pageIndex;
        return this;
    }

    public EmbedBuilder HidePageArrows()
    {
        _embed.HideChangePageArrows = true;
        return this;
    }

    public EmbedBuilder KeepPageOnUpdate(bool keep)
    {
        _embed.KeepPageOnUpdate = keep;
        return this;
    }

    /// <summary>
    /// Sets the revision used to order live updates for this embed.
    /// </summary>
    public EmbedBuilder WithRevision(long revision)
    {
        _embed.Revision = revision;
        return this;
    }

    // ---- Pages and containers ----

    /// <summary>
    /// Starts a new page. Closes any open row or form.
    /// </summary>
    public EmbedBuilder AddPage(string? title = null, string? footer = null)
    {
        _containers.Clear();

        var page = new EmbedPage
        {
            Title = title,
            Footer = footer,
        };

        _embed.Pages.Add(page);
        _currentPage = page;
        _containers.Push((ContainerKind.Page, page.Children));
        _lastItem = null;
        return this;
    }

    /// <summary>
    /// Opens a row (horizontal flex container). Close it with <see cref="EndRow"/>.
    /// </summary>
    public EmbedBuilder AddRow()
    {
        if (_containers.Count > 0 && _containers.Peek().Kind == ContainerKind.Row)
            throw new InvalidOperationException("Rows cannot be nested. Call EndRow() before adding another row.");

        var row = new EmbedRowItem();
        AddItemInternal(row);
        _containers.Push((ContainerKind.Row, row.Children));
        return this;
    }

    public EmbedBuilder EndRow()
    {
        PopContainer(ContainerKind.Row, nameof(EndRow));
        return this;
    }

    /// <summary>
    /// Opens a form. All inputs added before <see cref="EndForm"/> are
    /// collected when a submit button inside it is clicked.
    /// </summary>
    public EmbedBuilder AddForm(string id)
    {
        var form = new EmbedFormItem { Id = id };
        AddItemInternal(form);
        _containers.Push((ContainerKind.Form, form.Children));
        return this;
    }

    public EmbedBuilder EndForm()
    {
        PopContainer(ContainerKind.Form, nameof(EndForm));
        return this;
    }

    // ---- Items ----

    /// <summary>
    /// Adds a text item. Rendered as markdown.
    /// </summary>
    public EmbedBuilder AddText(string? text)
    {
        AddItemInternal(new EmbedTextItem(text));
        return this;
    }

    /// <summary>
    /// Adds a text item with a bolded name displayed above it.
    /// </summary>
    public EmbedBuilder AddText(string? name, string? text)
    {
        var item = new EmbedTextItem(text);
        if (name is not null)
            item.NameItem = new EmbedTextItem(name);

        AddItemInternal(item);
        return this;
    }

    /// <summary>
    /// Adds a button. When text is given it becomes the button's label;
    /// otherwise chain <see cref="WithText"/> (or leave it empty).
    /// </summary>
    public EmbedBuilder AddButton(string? text = null)
    {
        var button = new EmbedButtonItem();
        if (text is not null)
            button.Children.Add(new EmbedTextItem(text));

        AddItemInternal(button);
        return this;
    }

    /// <summary>
    /// Adds a text input.
    /// </summary>
    public EmbedBuilder AddInputBox(string id, string? name = null, string? placeholder = null, string? value = null)
    {
        var item = new EmbedInputBoxItem
        {
            Id = id,
            Placeholder = placeholder,
            Value = value,
        };

        if (name is not null)
            item.NameItem = new EmbedTextItem(name);

        AddItemInternal(item);
        return this;
    }

    /// <summary>
    /// Adds a dropdown. Add its options with <see cref="AddOption"/>.
    /// </summary>
    public EmbedBuilder AddDropDown(string id, string? name = null, string? value = null)
    {
        var item = new EmbedDropDownItem
        {
            Id = id,
            Value = value,
        };

        if (name is not null)
            item.NameItem = new EmbedTextItem(name);

        AddItemInternal(item);
        _currentDropDown = item;
        return this;
    }

    /// <summary>
    /// Adds an option to the most recently added dropdown.
    /// </summary>
    public EmbedBuilder AddOption(string text, string? value = null)
    {
        if (_currentDropDown is null)
            throw new InvalidOperationException("Add a dropdown with AddDropDown() before adding options.");

        var option = new EmbedDropDownOptionItem
        {
            Text = text,
            Value = value,
        };

        _currentDropDown.Options.Add(option);
        _lastItem = option;
        return this;
    }

    /// <summary>
    /// Adds a progress track. Add bars with <see cref="AddProgressBar"/>.
    /// </summary>
    public EmbedBuilder AddProgress(string? name = null)
    {
        var item = new EmbedProgressItem();
        if (name is not null)
            item.NameItem = new EmbedTextItem(name);

        AddItemInternal(item);
        _currentProgress = item;
        return this;
    }

    /// <summary>
    /// Adds a bar to the most recently added progress track.
    /// </summary>
    /// <param name="value">Fill percentage, 0-100.</param>
    public EmbedBuilder AddProgressBar(int value)
    {
        if (_currentProgress is null)
            throw new InvalidOperationException("Add a progress track with AddProgress() before adding bars.");

        var bar = new EmbedProgressBarItem { Value = value };
        _currentProgress.Bars.Add(bar);
        _lastItem = bar;
        return this;
    }

    /// <summary>
    /// Adds a chart, rendered client-side as inline SVG. Add data with
    /// <see cref="AddChartSeries"/> and labels with <see cref="WithChartLabels"/>.
    /// </summary>
    public EmbedBuilder AddChart(ChartKind kind, string? title = null)
    {
        var chart = new EmbedChartItem
        {
            Kind = kind,
            Title = title,
        };

        AddItemInternal(chart);
        _currentChart = chart;
        return this;
    }

    /// <summary>
    /// Adds a data series to the most recently added chart.
    /// Pie charts read only the first series.
    /// </summary>
    public EmbedBuilder AddChartSeries(string? name, params double[] values)
    {
        if (_currentChart is null)
            throw new InvalidOperationException("Add a chart with AddChart() before adding series.");

        _currentChart.Series.Add(new EmbedChartSeries
        {
            Name = name,
            Values = values.ToList(),
        });

        return this;
    }

    /// <summary>
    /// Sets the category labels of the most recently added chart
    /// (x-axis for line/bar charts, slice names for pie charts).
    /// </summary>
    public EmbedBuilder WithChartLabels(params string[] labels)
    {
        RequireChart(nameof(WithChartLabels)).Labels = labels.ToList();
        return this;
    }

    /// <summary>
    /// Sets the color of the most recently added chart series.
    /// </summary>
    public EmbedBuilder WithSeriesColor(string hexColor)
    {
        var chart = RequireChart(nameof(WithSeriesColor));
        if (chart.Series.Count == 0)
            throw new InvalidOperationException("Add a series with AddChartSeries() before setting its color.");

        chart.Series[^1].Color = hexColor;
        return this;
    }

    /// <summary>
    /// Shows the legend on the most recently added chart.
    /// </summary>
    public EmbedBuilder WithLegend()
    {
        RequireChart(nameof(WithLegend)).ShowLegend = true;
        return this;
    }

    /// <summary>
    /// Adds a media item. The attachment location must be from an allowed
    /// provider or the Valour CDN.
    /// </summary>
    public EmbedBuilder AddMedia(MessageAttachment attachment)
    {
        AddItemInternal(new EmbedMediaItem { Attachment = attachment });
        return this;
    }

    /// <summary>
    /// Adds a media item from attachment details. The location must be from
    /// an allowed provider or the Valour CDN.
    /// </summary>
    public EmbedBuilder AddMedia(MessageAttachmentType type, int width, int height, string mimeType, string fileName, string location)
    {
        return AddMedia(new MessageAttachment(type)
        {
            Width = width,
            Height = height,
            MimeType = mimeType,
            FileName = fileName,
            Location = location,
        });
    }

    // ---- Modifiers for the last-added item ----

    /// <summary>
    /// Sets the id of the last-added item. Required for items targeted by
    /// live updates or interaction events.
    /// </summary>
    public EmbedBuilder WithId(string id)
    {
        RequireLastItem(nameof(WithId)).Id = id;
        return this;
    }

    /// <summary>
    /// Sets a bolded name displayed above the last-added item.
    /// </summary>
    public EmbedBuilder WithName(string name)
    {
        if (RequireLastItem(nameof(WithName)) is not INamedItem named)
            throw new InvalidOperationException($"{_lastItem!.ItemType} items cannot have a name.");

        named.NameItem = new EmbedTextItem(name);
        return this;
    }

    /// <summary>
    /// Applies styles to the name of the last-added item. Set the name first.
    /// </summary>
    public EmbedBuilder WithNameStyle(params StyleValue[] styles)
    {
        if (RequireLastItem(nameof(WithNameStyle)) is not INamedItem { NameItem: not null } named)
            throw new InvalidOperationException("Set a name with WithName() (or a name argument) before styling it.");

        named.NameItem.Style = Append(named.NameItem.Style, styles);
        return this;
    }

    /// <summary>
    /// Adds a text child to the last-added button.
    /// </summary>
    public EmbedBuilder WithText(string text)
    {
        if (RequireLastItem(nameof(WithText)) is not EmbedButtonItem button)
            throw new InvalidOperationException("WithText() adds label text to a button; use AddText() for standalone text.");

        button.Children.Add(new EmbedTextItem(text));
        return this;
    }

    /// <summary>
    /// Applies styles to the last-added item.
    /// </summary>
    public EmbedBuilder WithStyle(params StyleValue[] styles)
    {
        var item = RequireLastItem(nameof(WithStyle));
        item.Style = Append(item.Style, styles);
        return this;
    }

    /// <summary>
    /// Adds CSS classes to the last-added item.
    /// </summary>
    public EmbedBuilder WithClasses(params string[] classes)
    {
        var item = RequireLastItem(nameof(WithClasses));
        var joined = string.Join(' ', classes);
        item.Classes = item.Classes is null ? joined : $"{item.Classes} {joined}";
        return this;
    }

    /// <summary>
    /// Controls whether the last-added input keeps the user's typed value
    /// when a live update arrives. Defaults to true.
    /// </summary>
    public EmbedBuilder KeepValueOnUpdate(bool keep)
    {
        if (RequireLastItem(nameof(KeepValueOnUpdate)) is not IFormInputItem input)
            throw new InvalidOperationException($"{_lastItem!.ItemType} items do not hold a form value.");

        input.KeepValueOnUpdate = keep;
        return this;
    }

    /// <summary>
    /// Shows the percentage label on the last-added progress bar.
    /// </summary>
    public EmbedBuilder WithLabel()
    {
        RequireProgressBar(nameof(WithLabel)).ShowLabel = true;
        return this;
    }

    /// <summary>
    /// Renders the last-added progress bar with stripes.
    /// </summary>
    public EmbedBuilder Striped()
    {
        RequireProgressBar(nameof(Striped)).IsStriped = true;
        return this;
    }

    /// <summary>
    /// Animates the stripes of the last-added progress bar.
    /// </summary>
    public EmbedBuilder Animated()
    {
        var bar = RequireProgressBar(nameof(Animated));
        bar.IsStriped = true;
        bar.IsAnimated = true;
        return this;
    }

    // ---- Click targets ----

    /// <summary>
    /// Makes the last-added item open a link when clicked (after confirmation).
    /// </summary>
    public EmbedBuilder OnClickLink(string href)
    {
        SetClickTarget(new EmbedLinkTarget { Href = href });
        return this;
    }

    /// <summary>
    /// Makes the last-added item navigate the embed to another page when clicked.
    /// </summary>
    public EmbedBuilder OnClickPage(int pageIndex)
    {
        SetClickTarget(new EmbedPageTarget { PageIndex = pageIndex });
        return this;
    }

    /// <summary>
    /// Makes the last-added item send an interaction event to the bot when clicked.
    /// </summary>
    public EmbedBuilder OnClickEvent(string eventId)
    {
        SetClickTarget(new EmbedEventTarget { EventId = eventId });
        return this;
    }

    /// <summary>
    /// Makes the last-added item submit its enclosing form when clicked.
    /// </summary>
    public EmbedBuilder OnClickSubmitForm(string eventId)
    {
        SetClickTarget(new EmbedFormSubmitTarget { EventId = eventId });
        return this;
    }

    // ---- Page modifiers ----

    public EmbedBuilder WithFooter(string footer)
    {
        RequirePage(nameof(WithFooter)).Footer = footer;
        return this;
    }

    public EmbedBuilder WithTitleStyle(params StyleValue[] styles)
    {
        var page = RequirePage(nameof(WithTitleStyle));
        page.TitleStyle = Append(page.TitleStyle, styles);
        return this;
    }

    public EmbedBuilder WithFooterStyle(params StyleValue[] styles)
    {
        var page = RequirePage(nameof(WithFooterStyle));
        page.FooterStyle = Append(page.FooterStyle, styles);
        return this;
    }

    public EmbedBuilder WithPageStyle(params StyleValue[] styles)
    {
        var page = RequirePage(nameof(WithPageStyle));
        page.Style = Append(page.Style, styles);
        return this;
    }

    // ---- Internals ----

    private void AddItemInternal(EmbedItem item)
    {
        if (_currentPage is null)
            throw new InvalidOperationException("Add a page with AddPage() before adding items.");

        _containers.Peek().Children.Add(item);
        _lastItem = item;

        if (item is not EmbedDropDownOptionItem)
            _currentDropDown = null;
        if (item is not EmbedProgressBarItem)
            _currentProgress = null;
        if (item is not EmbedChartItem)
            _currentChart = null;
    }

    private void PopContainer(ContainerKind expected, string caller)
    {
        var kind = _containers.Count > 0 ? _containers.Peek().Kind : ContainerKind.Page;
        if (kind == ContainerKind.Page)
            throw new InvalidOperationException($"{caller}() called with no open {expected.ToString().ToLowerInvariant()}.");

        if (kind != expected)
            throw new InvalidOperationException($"{caller}() called but the open container is a {kind.ToString().ToLowerInvariant()}.");

        _containers.Pop();
        _lastItem = null;
    }

    private EmbedItem RequireLastItem(string caller)
    {
        if (_lastItem is null)
            throw new InvalidOperationException($"{caller}() must follow an Add* call.");
        return _lastItem;
    }

    private EmbedProgressBarItem RequireProgressBar(string caller)
    {
        if (RequireLastItem(caller) is not EmbedProgressBarItem bar)
            throw new InvalidOperationException($"{caller}() only applies to progress bars.");
        return bar;
    }

    private EmbedChartItem RequireChart(string caller)
    {
        if (_currentChart is null)
            throw new InvalidOperationException($"{caller}() must follow AddChart().");
        return _currentChart;
    }

    private EmbedPage RequirePage(string caller)
    {
        if (_currentPage is null)
            throw new InvalidOperationException($"{caller}() must follow AddPage().");
        return _currentPage;
    }

    private void SetClickTarget(EmbedClickTarget target)
    {
        if (RequireLastItem("OnClick*") is not IClickableItem clickable)
            throw new InvalidOperationException($"{_lastItem!.ItemType} items are not clickable.");

        clickable.ClickTarget = target;
    }

    private static string Append(string? existing, StyleValue[] styles)
    {
        var compiled = StyleValue.Compile(styles);
        return existing is null ? compiled : $"{existing} {compiled}";
    }
}
