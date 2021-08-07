using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Messages;

namespace Valour.Client.Messages.Rendering
{
    public class MessageContentComponent : ComponentBase
    {
        [Parameter]
        public ClientPlanetMessage Message { get; set; }

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
}
