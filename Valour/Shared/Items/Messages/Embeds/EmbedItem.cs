namespace Valour.Shared.Items.Messages.Embeds;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public enum EmbedItemSize
{
    Big,
    Normal,
    Small,
    VerySmall,
    Short,
    VeryShort
}

public enum EmbedItemType
{
    Text,
    Button,
    InputBox
}

public class EmbedItem
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
    /// The name of the event for when a user has interacted with this item.
    /// </summary>
    public string? Event { get; set; }

    /// <summary>
    /// Whether or not when this button is pressed, it should submit the form data if any
    /// </summary>

    public bool SubmitForm { get; set; }

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
    public EmbedItemSize Size { get; set; }

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
                case EmbedItemSize.Short:
                    return "width:50%;";
                case EmbedItemSize.VeryShort:
                    return "width:25%";
                default:
                    return "";
            }
        }
    }
}
