using Microsoft.AspNetCore.Components.Rendering;
using Valour.Client.Components.Messages;
using Valour.Shared.Models;

namespace Valour.Client.Messages;

public class StockMentionFragment : MessageFragment
{
    public Mention Mention { get; set; }

    public override void BuildRenderTree(RenderTreeBuilder builder, ref int stage)
    {
        builder.OpenComponent<StockMentionComponent>(stage);
        stage++;
        builder.AddAttribute(stage, "Mention", Mention);
        stage++;
        builder.CloseComponent();
    }
}