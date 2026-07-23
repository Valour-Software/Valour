using Valour.Sdk.Models.Embeds;
using Valour.Sdk.Models.Embeds.Items;

namespace Valour.Tests.Sdk.Embeds;

public class EmbedFormDataTests
{
    [Fact]
    public void GetFormData_CollectsInputsAcrossNestedRows()
    {
        var embed = new EmbedBuilder()
            .AddPage()
                .AddForm("form-1")
                    .AddRow()
                        .AddInputBox("first", value: "Alice")
                    .EndRow()
                    .AddInputBox("second", value: "Bob")
                    .AddDropDown("color", value: "Red")
                        .AddOption("Red")
                .EndForm()
            .Build();

        var form = (EmbedFormItem)embed.Pages[0].Children[0];
        var data = form.GetFormData();

        Assert.Equal(3, data.Count);
        Assert.Contains(data, d => d.ElementId == "first" && d.Value == "Alice" && d.Type == EmbedItemType.InputBox);
        Assert.Contains(data, d => d.ElementId == "second" && d.Value == "Bob");
        Assert.Contains(data, d => d.ElementId == "color" && d.Value == "Red" && d.Type == EmbedItemType.DropDown);
    }

    [Fact]
    public void GetFormData_TruncatesLongValues()
    {
        var form = new EmbedFormItem
        {
            Id = "f",
            Children =
            {
                new EmbedInputBoxItem { Id = "big", Value = new string('x', 5000) },
            },
        };

        var data = Assert.Single(form.GetFormData());
        Assert.Equal(EmbedFormItem.MaxInputValueLength, data.Value!.Length);
    }

    [Fact]
    public void GetFormData_SkipsInputsWithoutIds()
    {
        var form = new EmbedFormItem
        {
            Id = "f",
            Children =
            {
                new EmbedInputBoxItem { Value = "no id" },
                new EmbedInputBoxItem { Id = "has-id", Value = "yes" },
            },
        };

        var data = Assert.Single(form.GetFormData());
        Assert.Equal("has-id", data.ElementId);
    }

    [Fact]
    public void FindItem_LocatesNestedItems()
    {
        var embed = new EmbedBuilder()
            .AddPage()
                .AddRow()
                    .AddButton("b").WithId("deep-button")
                .EndRow()
            .Build();

        Assert.IsType<EmbedButtonItem>(embed.FindItem("deep-button"));
        Assert.Null(embed.FindItem("missing"));
    }

    [Fact]
    public void ReplaceItem_SwapsNestedItemById()
    {
        var embed = new EmbedBuilder()
            .AddPage()
                .AddRow()
                    .AddText("old").WithId("target")
                .EndRow()
            .Build();

        var replaced = embed.ReplaceItem(new EmbedTextItem("new") { Id = "target" });

        Assert.True(replaced);
        Assert.Equal("new", ((EmbedTextItem)embed.FindItem("target")!).Text);
    }

    [Fact]
    public void ReplaceItem_ReplacesNameItems()
    {
        var embed = new EmbedBuilder()
            .AddPage()
                .AddInputBox("input-1").WithName("Old Name")
            .Build();

        var input = (EmbedInputBoxItem)embed.FindItem("input-1")!;
        input.NameItem!.Id = "input-1-name";

        var replaced = embed.ReplaceItem(new EmbedTextItem("New Name") { Id = "input-1-name" });

        Assert.True(replaced);
        Assert.Equal("New Name", ((EmbedInputBoxItem)embed.FindItem("input-1")!).NameItem!.Text);
    }

    [Fact]
    public void ReplaceItem_ReturnsFalse_ForUnknownOrNullId()
    {
        var embed = new EmbedBuilder().AddPage().AddText("t").Build();

        Assert.False(embed.ReplaceItem(new EmbedTextItem("x") { Id = "unknown" }));
        Assert.False(embed.ReplaceItem(new EmbedTextItem("x")));
    }

    [Fact]
    public void ReplaceItem_SwapsProgressBars()
    {
        var embed = new EmbedBuilder()
            .AddPage()
                .AddProgress()
                    .AddProgressBar(10).WithId("bar")
            .Build();

        Assert.True(embed.ReplaceItem(new EmbedProgressBarItem { Id = "bar", Value = 90 }));
        Assert.Equal(90, ((EmbedProgressBarItem)embed.FindItem("bar")!).Value);
    }
}
