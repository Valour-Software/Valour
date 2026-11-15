using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using Valour.Client.Messages;

namespace Valour.Client.Markdig;

/// <summary>
/// A code block that shows its code as plain text until highlight.js returns
/// the highlighted markup, then renders that markup in the same element.
/// Results are cached by language and code, so messages that are rebuilt, such
/// as when a hidden chat is shown again, do not call into JavaScript again.
/// </summary>
public sealed class HighlightedCodeBlock : ComponentBase
{
    private static readonly BoundedLruCache<string, string> HighlightCache = new(256);

    [Inject]
    private IJSRuntime JsRuntime { get; set; }

    [Parameter]
    public string Code { get; set; }

    [Parameter]
    public string Language { get; set; }

    private string _key;
    private string _html;
    private string _requestedKey;

    protected override void OnParametersSet()
    {
        _key = $"{Language}\n{Code}";
        _html = HighlightCache.TryGet(_key, out var html) ? html : null;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var languageClass = string.IsNullOrWhiteSpace(Language) ? null : $" language-{Language}";

        builder.OpenElement(0, "pre");
        builder.OpenElement(1, "code");
        builder.AddAttribute(2, "class", $"hljs{languageClass}");
        if (_html is null)
            builder.AddContent(3, Code);
        else
            builder.AddMarkupContent(4, _html);
        builder.CloseElement();
        builder.CloseElement();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_html is not null || _requestedKey == _key || string.IsNullOrEmpty(Code))
            return;

        var key = _key;
        _requestedKey = key;

        string html;
        try
        {
            // highlight.js escapes the code text in the markup it returns
            html = await JsRuntime.InvokeAsync<string>("hljsHighlightHtml", Code, Language);
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or TaskCanceledException)
        {
            return;
        }

        if (html is null)
            return;

        HighlightCache.Set(key, html);
        if (key == _key)
        {
            _html = html;
            StateHasChanged();
        }
    }
}
