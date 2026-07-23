using Valour.Sdk.Models.Embeds;
using Valour.Sdk.Models.Embeds.Items;

namespace Valour.Tests.Sdk.Embeds;

public class EmbedBuilderTests
{
    [Fact]
    public void Build_ProducesExpectedTreeShape()
    {
        var embed = new EmbedBuilder()
            .AddPage("Title")
                .AddRow()
                    .AddButton("OK")
                    .AddInputBox("input-1")
                .EndRow()
                .AddText("after row")
            .Build();

        var page = Assert.Single(embed.Pages);
        Assert.Equal(2, page.Children.Count);

        var row = Assert.IsType<EmbedRowItem>(page.Children[0]);
        Assert.Equal(2, row.Children.Count);
        Assert.IsType<EmbedButtonItem>(row.Children[0]);
        Assert.IsType<EmbedInputBoxItem>(row.Children[1]);

        Assert.IsType<EmbedTextItem>(page.Children[1]);
    }

    [Fact]
    public void Build_FormContainsRowContents()
    {
        var embed = new EmbedBuilder()
            .AddPage()
                .AddForm("form-1")
                    .AddRow()
                        .AddInputBox("a")
                        .AddInputBox("b")
                    .EndRow()
                    .AddButton("Submit").OnClickSubmitForm("submit-event")
                .EndForm()
            .Build();

        var form = Assert.IsType<EmbedFormItem>(Assert.Single(embed.Pages[0].Children));
        Assert.Equal(2, form.Children.Count);
        var row = Assert.IsType<EmbedRowItem>(form.Children[0]);
        Assert.Equal(2, row.Children.Count);
        Assert.IsType<EmbedButtonItem>(form.Children[1]);
    }

    [Fact]
    public void AddOption_AttachesToLastDropDown()
    {
        var embed = new EmbedBuilder()
            .AddPage()
                .AddDropDown("dd-1")
                    .AddOption("One")
                    .AddOption("Two", "two-value")
            .Build();

        var dropDown = Assert.IsType<EmbedDropDownItem>(Assert.Single(embed.Pages[0].Children));
        Assert.Equal(2, dropDown.Options.Count);
        Assert.Equal("One", dropDown.Options[0].EffectiveValue);
        Assert.Equal("two-value", dropDown.Options[1].EffectiveValue);
    }

    [Fact]
    public void AddProgressBar_AttachesToLastProgress()
    {
        var embed = new EmbedBuilder()
            .AddPage()
                .AddProgress("Loading")
                    .AddProgressBar(30).WithLabel()
                    .AddProgressBar(140).Striped()
            .Build();

        var progress = Assert.IsType<EmbedProgressItem>(Assert.Single(embed.Pages[0].Children));
        Assert.Equal(2, progress.Bars.Count);
        Assert.True(progress.Bars[0].ShowLabel);
        Assert.Equal(100, progress.Bars[1].Value); // clamped
        Assert.True(progress.Bars[1].IsStriped);
        Assert.Equal("Loading", progress.NameItem?.Text);
    }

    [Fact]
    public void Animated_ImpliesStriped()
    {
        var embed = new EmbedBuilder()
            .AddPage().AddProgress().AddProgressBar(10).Animated()
            .Build();

        var bar = ((EmbedProgressItem)embed.Pages[0].Children[0]).Bars[0];
        Assert.True(bar.IsAnimated);
        Assert.True(bar.IsStriped);
    }

    [Fact]
    public void ClickTargets_AreSetOnLastItem()
    {
        var embed = new EmbedBuilder()
            .AddPage()
                .AddButton("a").OnClickLink("https://valour.gg")
                .AddButton("b").OnClickPage(2)
                .AddButton("c").OnClickEvent("evt")
                .AddButton("d").OnClickSubmitForm("sub")
            .Build();

        var buttons = embed.Pages[0].Children.Cast<EmbedButtonItem>().ToList();
        Assert.IsType<EmbedLinkTarget>(buttons[0].ClickTarget);
        Assert.Equal(2, Assert.IsType<EmbedPageTarget>(buttons[1].ClickTarget).PageIndex);
        Assert.Equal("evt", Assert.IsType<EmbedEventTarget>(buttons[2].ClickTarget).EventId);
        Assert.Equal("sub", Assert.IsType<EmbedFormSubmitTarget>(buttons[3].ClickTarget).EventId);
    }

    [Fact]
    public void AddPage_ClosesOpenContainers()
    {
        var embed = new EmbedBuilder()
            .AddPage().AddRow().AddText("in row")
            .AddPage().AddText("on second page")
            .Build();

        Assert.Equal(2, embed.Pages.Count);
        Assert.IsType<EmbedTextItem>(Assert.Single(embed.Pages[1].Children));
    }

    [Fact]
    public void AddItem_BeforeAddPage_Throws()
    {
        var builder = new EmbedBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.AddText("orphan"));
    }

    [Fact]
    public void NestedRows_Throw()
    {
        var builder = new EmbedBuilder().AddPage().AddRow();
        Assert.Throws<InvalidOperationException>(() => builder.AddRow());
    }

    [Fact]
    public void EndRow_WithoutRow_Throws()
    {
        var builder = new EmbedBuilder().AddPage();
        Assert.Throws<InvalidOperationException>(() => builder.EndRow());
    }

    [Fact]
    public void EndRow_WhenFormIsOpen_Throws()
    {
        var builder = new EmbedBuilder().AddPage().AddForm("f");
        Assert.Throws<InvalidOperationException>(() => builder.EndRow());
    }

    [Fact]
    public void AddOption_WithoutDropDown_Throws()
    {
        var builder = new EmbedBuilder().AddPage().AddText("text");
        Assert.Throws<InvalidOperationException>(() => builder.AddOption("opt"));
    }

    [Fact]
    public void WithName_OnUnnameableItem_Throws()
    {
        var builder = new EmbedBuilder().AddPage().AddButton("b");
        Assert.Throws<InvalidOperationException>(() => builder.WithName("nope"));
    }

    [Fact]
    public void OnClick_OnUnclickableItem_Throws()
    {
        var builder = new EmbedBuilder().AddPage().AddInputBox("in");
        Assert.Throws<InvalidOperationException>(() => builder.OnClickLink("https://valour.gg"));
    }

    [Fact]
    public void Build_WithoutPages_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new EmbedBuilder().Build());
    }

    [Fact]
    public void Build_WithDuplicateIds_Throws()
    {
        var builder = new EmbedBuilder()
            .AddPage()
                .AddText("a").WithId("dupe")
                .AddText("b").WithId("dupe");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }
}
