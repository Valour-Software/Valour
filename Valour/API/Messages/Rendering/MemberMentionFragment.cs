using Microsoft.AspNetCore.Components.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Planets;
using Valour.Client.Shared.Messages;
using Valour.Shared.Messages;

namespace Valour.Api.Messages;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class MemberMentionFragment : MessageFragment
{
    public Mention Mention { get; set; }

    public override void BuildRenderTree(RenderTreeBuilder builder, ref int stage)
    {
        builder.OpenComponent<MemberMentionComponent>(stage);
        stage++;
        builder.AddAttribute(stage, "Mention", Mention);
        stage++;
        builder.CloseComponent();
    }
}

