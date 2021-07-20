using Microsoft.AspNetCore.Components.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Client.Messages.Rendering
{
    public abstract class MessageFragment
    {
        public ushort Position;

        public ushort Length;

        public abstract void BuildRenderTree(RenderTreeBuilder builder, ref int stage);
    }
}
