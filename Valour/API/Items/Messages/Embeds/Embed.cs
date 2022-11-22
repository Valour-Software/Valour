using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Items;

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

    public EmbedRow()
    {
    }

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
    public List<EmbedItem> Items { get; set; }

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

    public EmbedPage()
    {

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
    public string Name { get; set; }

    /// <summary>
    /// The id of this embed. Must be set if the embed has forms.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The page that the embed starts on when it's loaded
    /// </summary>
    public int StartPage { get; set; }

    [JsonIgnore]
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

    public Embed()
    {
    }

    public EmbedItem GetLastItem(bool InsideofForms, int? pagenum = null)
    {
        EmbedPage page = null;
        if (pagenum is null)
            page = Pages.Last();
        else 
            page = Pages[(int)pagenum];
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

    public string GetStyle()
    {
        return CurrentlyDisplayed.GetStyle(this);
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

