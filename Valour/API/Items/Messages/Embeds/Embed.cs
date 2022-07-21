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
        if (items is not null) {
            Items = items;
        }
        else {
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
    /// Takes in JsonNode and builds the EmbedPage from it
    /// </summary>
    /// <param name="Node">JsonNode of embedpage</param>
    public void BuildFromJson(JsonNode Node)
    {
        Title = (string)Node["Title"];
        Footer = (string)Node["Footer"];
        TitleColor = (string)Node["TitleColor"] ?? "eeeeee";
        FooterColor = (string)Node["FooterColor"] ?? "eeeeee";
        Rows = new();
        Items = new();

        // now we need to convert each embeditem into the proper types
        if (Node["Items"] is not null)
        {
            foreach (JsonNode node in Node["Items"].AsArray())
            {
                Items.Add(Embed.ConvertNodeToEmbedItem(node));
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
                    rowobject.Items.Add(Embed.ConvertNodeToEmbedItem(node));
                }
                Rows.Add(rowobject);
            }
        }
    }
}

public class Embed
{
    /// <summary>
    /// The pages within this embed.
    /// </summary>
    public List<EmbedPage> Pages { get; set; }

    public EmbedItemPlacementType EmbedType { get; set; }

    /// <summary>
    /// The name of this embed. Must be set if the embed has forms.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The id of this embed. Must be set if the embed has forms.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Only works in FreelyBased embed/form type
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Only works in FreelyBased embed/form type
    /// </summary>
    public int? Height { get; set; }

    public int currentPage = 0;

    internal static EmbedItem ConvertNodeToEmbedItem(JsonNode node)
    {
        var type = (EmbedItemType)(int)node["ItemType"];
        EmbedItem item = type switch
        {
            EmbedItemType.Text => node.Deserialize<EmbedTextItem>(),
            EmbedItemType.Form => new EmbedFormItem(node),
            EmbedItemType.Button => node.Deserialize<EmbedButtonItem>(),
            EmbedItemType.InputBox => node.Deserialize<EmbedInputBoxItem>()
        };
        return item;
    }

    public string GetStyle()
    {
        string style = "";
        if (EmbedType == EmbedItemPlacementType.FreelyBased)
        {
            style += $"height: {Height}px;width: {Width}px;padding: unset;";
        }
        return style;
    }

    public void BuildFromJson(JsonNode Node)
    {
        Id = (string)Node["Id"];
        Name = (string)Node["Name"];
        EmbedType = (EmbedItemPlacementType)(int)Node["EmbedType"];
        if (EmbedType == EmbedItemPlacementType.FreelyBased)
        {
            Width = (int?)Node["Width"];
            Height = (int?)Node["Height"];
        }
        Pages = new();
        foreach(var pagenode in Node["Pages"].AsArray())
        {
            var page = new EmbedPage();
            page.BuildFromJson(pagenode);
            Pages.Add(page);
        }
    }

    /// <summary>
    /// The currently displayed page
    /// </summary>
    [JsonIgnore]
    public EmbedPage Currently_Displayed
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

        /*foreach (EmbedItem item in Currently_Displayed)
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

