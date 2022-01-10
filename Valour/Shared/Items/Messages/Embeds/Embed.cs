namespace Valour.Shared.Items.Messages.Embeds;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


public class Embed
{
    /// <summary>
    /// The pages within this embed. Sub-arrays are the items within the page.
    /// </summary>
    public EmbedItem[][] Pages { get; set; }

    public int currentPage = 0;

    /// <summary>
    /// The currently displayed embed items
    /// </summary>
    public EmbedItem[] Currently_Displayed
    {
        get
        {
            return Pages[currentPage];
        }
    }

    public void NextPage()
    {
        currentPage += 1;

        if (currentPage >= Pages.Length)
        {
            currentPage = 0;
        }
    }
    public void PrevPage()
    {
        currentPage -= 1;

        if (currentPage < 0)
        {
            currentPage = Pages.Length - 1;
        }
    }

    public List<EmbedFormData> GetFormData()
    {
        List<EmbedFormData> data = new();

        foreach (EmbedItem item in Currently_Displayed)
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
                // limit the size of the input to 32 char
                if (DataItem.Value != null)
                {
                    if (DataItem.Value.Length > 32)
                    {
                        DataItem.Value = DataItem.Value.Substring(0, 31);
                    }
                }
                data.Add(DataItem);
            }
        }
        return data;
    }
}

