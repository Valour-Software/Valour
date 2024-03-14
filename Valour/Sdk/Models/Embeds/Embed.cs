using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Valour.Sdk.Models.Messages.Embeds.Items;
using Valour.Sdk.Models.Messages.Embeds.Styles;

namespace Valour.Sdk.Models.Messages.Embeds;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class EmbedRow : EmbedItem
{
	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.EmbedRow;
	public override EmbedItem GetLastItem(bool InsideofForms)
	{
        if (Children is null || Children.Count == 0)
            return this;
        else
        {
            return Children.Last().GetLastItem(InsideofForms);
        }
	}
}

public class EmbedPage : EmbedItem, IParentItem
{
    public new List<EmbedItem> Children { get; set; }

    public string Title { get; set; }

    public string Footer { get; set; }
	public List<StyleBase> TitleStyles { get; set; }

    public List<StyleBase> FooterStyles { get; set; }

    [JsonIgnore]
    public new EmbedItemType ItemType => EmbedItemType.EmbedPage;

    [JsonIgnore]
	public new IParentItem Parent { get; set; }

    public override List<EmbedItem> GetAllItems()
	{
        if (Children is null)
            return new();
        List<EmbedItem> items = new();
        foreach(var _item in Children) 
        {
            items.Add(_item);
            items.AddRange(_item.GetAllItems());
        }
        return items;
	}

	public EmbedPage()
    {

    }
	public string GetTitleStyle(Embed embed)
	{
		string style = "";
		if (TitleStyles is not null)
		{
			foreach (var _style in TitleStyles)
			{
				style += _style;
			}
		}
		return style;
	}
	public string GetFooterStyle(Embed embed)
	{
		string style = "";
		if (FooterStyles is not null)
		{
			foreach (var _style in FooterStyles)
			{
				style += _style;
			}
		}
		return style;
	}

	public string GetStyle(Embed embed)
    {
        string style = "";
		if (Styles is not null)
		{
			foreach (var _style in Styles)
			{
				style += _style;
			}
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
            return "1.3";
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
        var item = page.GetLastItem(InsideofForms);
        //if (InsideofForms) {
        //    if (item.ItemType == EmbedItemType.Form) {
        //        return ((EmbedFormItem)item).GetLastItem(InsideofForms);
        //    }
       // }
        return item;
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

