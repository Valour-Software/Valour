using Valour.Api.Items.Messages.Embeds.Items;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Text.Json.Serialization;
using System.Xml;

namespace Valour.Api.Items.Messages.Embeds;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public enum EmbedItemSize
{
    VerySmall,
    Small,
    Normal,
    Large
}

public enum EmbedItemPlacementType
{
    /// <summary>
    /// Based on rows
    /// </summary>
    RowBased = 1,

    /// <summary>
    /// Every item will have a x, y position
    /// </summary>
    FreelyBased = 2
}

public enum EmbedAlignType
{
    Left = 1,
    Center = 2,
    Right = 3
}

public class EmbedRow
{
    public List<EmbedItem> Items { get; set; }

    public EmbedAlignType Align { get; set; }

    public EmbedRow(List<EmbedItem> items = null)
    {
        if (items is not null)
        {
            Items = items;
        }
        else
        {
            Items = new();
        }
    }
}

public class EmbedPage 
{
    /// <summary>
    /// Items in this page. This should be null if embed is not FreelyBased
    /// </summary>
    public List<EmbedItem>? Items { get; set; }

    public List<EmbedRow> Rows { get; set; }

    public string? Title { get; set; }

    public string? Footer { get; set; }

    /// <summary>
    /// The color (hex) of this page's title
    /// </summary>
    public string TitleColor { get; set; }

    /// <summary>
    /// The color (hex) of this page's footer
    /// </summary>
    public string FooterColor { get; set; }

    /// <summary>
    /// The width of the page. Only works in FreelyBased embed/form type
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// The height of the page. Only works in FreelyBased embed/form type
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// If Freely based, items must have a x and y position.
    /// </summary>
    public EmbedItemPlacementType EmbedType { get; set; }

    /// <summary>
    /// Takes in JsonNode and builds the EmbedPage from it
    /// </summary>
    /// <param name="Node">JsonNode of embedpage</param>
    public void BuildFromJson(JsonNode Node, Embed embed)
    {
        Title = (string)Node["Title"];
        Footer = (string)Node["Footer"];
        EmbedType = (EmbedItemPlacementType)((int?)Node["EmbedType"] ?? (int)EmbedItemPlacementType.RowBased);
        if (EmbedType == EmbedItemPlacementType.FreelyBased)
        {
            Width = (int?)Node["Width"];
            Height = (int?)Node["Height"];
        }
        TitleColor = (string)Node["TitleColor"] ?? "eeeeee";
        FooterColor = (string)Node["FooterColor"] ?? "eeeeee";
        Rows = new();
        Items = new();

        // now we need to convert each embeditem into the proper types
        if (Node["Items"] is not null)
        {
            foreach (JsonNode node in Node["Items"].AsArray())
            {
                Items.Add(Embed.ConvertNodeToEmbedItem(node, embed));
            }
        }

        if (Node["Rows"] is not null && Node["Items"] is null)
        {
            foreach (var rownode in Node["Rows"].AsArray())
            {
                EmbedRow rowobject = new();
                if (rownode["Align"] is not null)
                    rowobject.Align = (EmbedAlignType)(int)rownode["Align"];
                int i = 0;
                foreach (JsonNode node in rownode["Items"].AsArray())
                {
                    rowobject.Items.Add(Embed.ConvertNodeToEmbedItem(node, embed));
                }
                Rows.Add(rowobject);
            }
        }
    }
    public string GetStyle(Embed embed)
    {
        string style = "";
        if (EmbedType == EmbedItemPlacementType.FreelyBased)
        {
            int? height = Height;
            int? width = Width;
            if (!embed.HideChangePageArrows && embed.Pages.Count > 1)
                height += 32;
            if (Title is not null)
                height += 36;
            style += $"height: calc(2rem + {height}px);width: calc(2rem + {width}px);";
        }
        return style;
    }
}

public class Embed
{
    /// <summary>
    /// The pages within this embed.
    /// </summary>
    public List<EmbedPage> Pages { get; set; }

    /// <summary>
    /// The name of this embed. Must be set if the embed has forms.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The id of this embed. Must be set if the embed has forms.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// The page that the embed starts on when it's loaded
    /// </summary>
    public int StartPage { get; set; }

    public int currentPage = 0;

    /// <summary>
    /// If true, hide the change page arrows at the bottom of the embed
    /// </summary>
    public bool HideChangePageArrows { get; set; }

    public bool KeepPageOnUpdate { get; set; }

    /// <summary>
    /// The Version of the embed system
    /// </summary>
    public string EmbedVersion
    {
        get
        {
            return "1.1.0";
        }
    }

    public EmbedItem GetLastItem(bool InsideofForms)
    {
        var page = Pages.Last();
        if (page.EmbedType == EmbedItemPlacementType.RowBased) {
            var item = page.Rows.Last().Items.Last();
            if (InsideofForms) {
                if (item.ItemType == EmbedItemType.Form) {
                    return ((EmbedFormItem)item).GetLastItem();
                }
            }
            return item;
        }
        else {
            var item = page.Items.Last();
            if (InsideofForms) {
                if (item.ItemType == EmbedItemType.Form) {
                    return ((EmbedFormItem)item).GetLastItem();
                }
            }
            return item;
        }
    }

    internal static EmbedItem ConvertNodeToEmbedItem(JsonNode node, Embed embed)
    {
        var type = (EmbedItemType)(int)node["ItemType"];
        EmbedItem item = type switch
        {
            EmbedItemType.Text => node.Deserialize<EmbedTextItem>(),
            EmbedItemType.Form => new EmbedFormItem(node, embed),
            EmbedItemType.Button => node.Deserialize<EmbedButtonItem>(),
            EmbedItemType.InputBox => node.Deserialize<EmbedInputBoxItem>(),
            EmbedItemType.DropDownItem => node.Deserialize<EmbedDropDownItem>(),
            EmbedItemType.DropDownMenu => node.Deserialize<EmbedDropDownMenuItem>()
        };
        item.Embed = embed;
        return item;
    }

    public string GetStyle()
    {
        return CurrentlyDisplayed.GetStyle(this);
    }

    public void BuildFromJson(JsonNode Node)
    {
        Id = (string)Node["Id"];
        Name = (string)Node["Name"];
        if (Node["KeepPageOnUpdate"] is not null)
            KeepPageOnUpdate = (bool)Node["KeepPageOnUpdate"];
        else
            KeepPageOnUpdate = false;
        if (Node["StartPage"] is not null)
            StartPage = (int)Node["StartPage"];
        else
            StartPage = 0;
		if (Node["HideChangePageArrows"] is not null)
			HideChangePageArrows = (bool)Node["HideChangePageArrows"];
		else
			HideChangePageArrows = false;
		Pages = new();
        foreach(var pagenode in Node["Pages"].AsArray())
        {
            var page = new EmbedPage();
            page.BuildFromJson(pagenode, this);
            Pages.Add(page);
        }
    }

    /// <summary>
    /// The currently displayed page
    /// </summary>
    [JsonIgnore]
    public EmbedPage CurrentlyDisplayed
    {
        get
        {
            return Pages[currentPage];
        }
    }

    public void NextPage()
    {
        currentPage += 1;

        if (currentPage >= Pages.Count)
        {
            currentPage = 0;
        }
    }
    public void PrevPage()
    {
        currentPage -= 1;

        if (currentPage < 0)
        {
            currentPage = Pages.Count - 1;
        }
    }

    public List<EmbedFormData> GetFormData()
    {

        // TODO: Convert this function to use the new form system
        // Embed.AddPage()
        //      .AddRow()
        //        .AddForm(Id: "User Info Form")
        //          .AddInput("Your Name", "name")
        //          .AddInput("Your Email", "email")
        //          .AddSubmitButton("Submit");

        List<EmbedFormData> data = new();

        /*foreach (EmbedItem item in CurrentlyDisplayed)
        {
            if (item.Type == EmbedItemType.InputBox)
            {
                EmbedFormData DataItem = new()
                {
                    Element_Id = item.Id,
                    Value = item.Value,
                    Type = item.Type
                };

                // do only if input is not null
                // limit the size of the input to 4096 char
                if (DataItem.Value != null)
                {
                    if (DataItem.Value.Length > 4096)
                    {
                        DataItem.Value = DataItem.Value.Substring(0, 4095);
                    }
                }
                data.Add(DataItem);
            }
        }*/
        return data;
    }
}

