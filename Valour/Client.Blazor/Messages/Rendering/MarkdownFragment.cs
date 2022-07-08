using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Client.Blazor.Messages;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class MarkdownFragment : MessageFragment
{
    public string Content;

    public override void BuildRenderTree(RenderTreeBuilder builder, ref int stage)
    {
        // Just write markdown. Pretty simple.
        builder.AddMarkupContent(stage, Content);
        stage++;
    }
}

