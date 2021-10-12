using Microsoft.AspNetCore.Components;
using Valour.Shared.Messages;

namespace Valour.Api.Messages;

public class Embed
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    public class ClientEmbedItem
    {

        /// <summary>
        /// The type of this embed item
        /// </summary>
        public EmbedItemType Type { get; set; }

        /// <summary>
        /// The text within the embed.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Name of the embed. Not required.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// If this component should be inlined
        /// </summary>
        public bool Inline { get; set; }

        /// <summary>
        /// The link this component leads to
        /// </summary>
        public string Link { get; set; }

        /// <summary>
        /// Must be in hex format, example: "ffffff"
        /// </summary>
        public string Color { get; set; }

        /// <summary>
        /// The color (hex) of this embed item's text
        /// </summary>
        public string TextColor { get; set; }

        /// <summary>
        /// True if this item should be centered
        /// </summary>
        public bool Center { get; set; }

        /// <summary>
        /// The size of this embed item
        /// </summary>
        public EmbedSize Size { get; set; }

        /// <summary>
        /// Used to identify this embed item for events and more
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The input value
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// The placeholder text for inputs
        /// </summary>
        public string Placeholder { get; set; }

        public string GetInlineStyle
        {
            get
            {
                if (Inline)
                {
                    return "display:inline-grid;margin-right: 8px;";
                }
                else
                {
                    return "margin-right: 8px;";
                }
            }
        }

        public string GetInputStyle
        {
            get
            {
                switch (Size)
                {
                    case EmbedSize.Short:
                        return "width:50%;";
                    case EmbedSize.VeryShort:
                        return "width:25%";
                    default:
                        return "";
                }
            }
        }

        /// <summary>
        /// Conversion for markdown
        /// </summary>
        public MarkupString DoMarkdown(string data)
        {
            return (MarkupString)MarkdownManager.GetHtml(data);
        }
    }

    /// <summary>
    /// This class exists to render embeds
    /// </summary>
    public class ClientEmbed
    {

        /// <summary>
        /// The pages within this embed. Sub-arrays are the items within the page.
        /// </summary>
        public ClientEmbedItem[][] Pages { get; set; }

        public int currentPage = 0;

        /// <summary>
        /// The currently displayed embed items
        /// </summary>
        public ClientEmbedItem[] Currently_Displayed
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

        public List<EmbedFormDataItem> GetFormData()
        {
            List<EmbedFormDataItem> data = new();

            foreach (ClientEmbedItem item in Currently_Displayed)
            {
                if (item.Type == EmbedItemType.InputBox)
                {
                    EmbedFormDataItem DataItem = new()
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
}

