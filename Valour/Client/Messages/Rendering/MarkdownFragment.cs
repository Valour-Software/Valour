using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Client.Messages.Rendering
{
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
}
