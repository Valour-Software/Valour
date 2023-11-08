using Markdig.Blazor;
using Valour.Client.Components.Messages;

namespace Valour.Client.Markdig;

public class StockRenderer : BlazorObjectRenderer<StockInline>
{
    protected override void Write(BlazorRenderer renderer, StockInline obj)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        
        if (obj.Symbol is null)
            return;

        renderer.OpenComponent<StockMentionComponent>()
            .AddComponentParam("Symbol", obj.Symbol)
            .CloseComponent();
    }
}