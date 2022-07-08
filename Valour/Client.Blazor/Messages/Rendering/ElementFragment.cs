using Microsoft.AspNetCore.Components.Rendering;

namespace Valour.Client.Blazor.Messages;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class ElementFragment : MessageFragment
{
    public string Tag { get; set; }

    public Dictionary<string, string> Attributes { get; set; }

    public bool Closing { get; set; }

    public bool Self_Closing { get; set; }

    public override void BuildRenderTree(RenderTreeBuilder builder, ref int stage)
    {
        if (!Closing)
        {
            builder.OpenElement(stage, Tag);
            stage++;

            if (Self_Closing)
            {
                builder.CloseElement();
            }
        }
        else
        {
            builder.CloseElement();
        }
    }
}
