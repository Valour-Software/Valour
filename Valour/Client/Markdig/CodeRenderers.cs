using System.Text;
using Markdig.Blazor;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Valour.Client.Markdig;

/// <summary>
/// Renders inline code as a single element. Inline code is not syntax
/// highlighted, so it needs no component or script call.
/// </summary>
public class ValourCodeInlineRenderer : BlazorObjectRenderer<CodeInline>
{
    protected override void Write(BlazorRenderer renderer, CodeInline obj)
    {
        renderer.OpenElement("code", 0);
        renderer.AddAttribute("class", "hljs", 1);
        renderer.WriteText(obj.Content, 2);
        renderer.CloseElement();
    }
}

/// <summary>
/// Renders code blocks through <see cref="HighlightedCodeBlock"/>, which
/// highlights the code once and writes the result into its own element.
/// </summary>
public class ValourCodeBlockRenderer : BlazorObjectRenderer<CodeBlock>
{
    protected override void Write(BlazorRenderer renderer, CodeBlock obj)
    {
        renderer.OpenComponent<HighlightedCodeBlock>()
            .AddComponentParam(nameof(HighlightedCodeBlock.Code), GetCode(obj))
            .AddComponentParam(nameof(HighlightedCodeBlock.Language), (obj as FencedCodeBlock)?.Info)
            .CloseComponent();
    }

    private static string GetCode(CodeBlock block)
    {
        var lines = block.Lines.Lines;
        if (lines is null)
            return string.Empty;

        var code = new StringBuilder();
        for (var i = 0; i < block.Lines.Count; i++)
        {
            var slice = lines[i].Slice;
            if (slice.Text is null)
                break;

            code.Append(slice.AsSpan().TrimEnd()).Append('\n');
        }

        return code.ToString();
    }
}
