using Microsoft.AspNetCore.Components.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Planets;
using Valour.Client.Shared.Messages;
using Valour.Shared.Messages;

namespace Valour.Client.Messages.Rendering
{
    public class MemberMentionFragment : MessageFragment
    {
        public ulong Member_Id { get; set; }

        public MemberMention Mention { get; set; }

        public override void BuildRenderTree(RenderTreeBuilder builder, ref int stage)
        {
            builder.OpenComponent<MemberMentionComponent>(stage);
            stage++;
            builder.AddAttribute(stage, "Mention", Mention);
            stage++;
            builder.AddAttribute(stage, "Member_Id", Member_Id);
            stage++;
            builder.CloseComponent();
        }
    }
}
