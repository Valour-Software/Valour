using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Valour.Client.Messages;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class MessageContentComponent : ComponentBase
{
    [Parameter]
    public Message Message { get; set; }

    public void ReRender()
    {
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        //Console.WriteLine("Rendering content");

        var fragments = Message.GetMessageFragments();
        int stage = 0;

        builder.OpenElement(0, "div");
        stage++;

        //Console.WriteLine("Fragments: " + fragments.Count);

        foreach (var frag in fragments)
        {
            stage++;
            frag.BuildRenderTree(builder, ref stage);
        }

        builder.CloseElement();
    }
}

