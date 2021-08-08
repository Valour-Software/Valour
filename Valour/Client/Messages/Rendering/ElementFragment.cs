using Microsoft.AspNetCore.Components.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Client.Messages.Rendering
{
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
}
