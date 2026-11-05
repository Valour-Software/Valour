// Syntax highlighting for the theme editor's custom CSS field. Shares the
// highlight.js loader used by the docs pages (ensureHighlightScript in wiki.js).

function cssEditorEscapeHtml(text) {
    return text
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

window.cssEditorHighlight = async function (code) {
    try {
        await ensureHighlightScript();
    } catch (error) {
        // The field stays usable as plain text if the CDN is unreachable.
        console.warn('CSS syntax highlighting is unavailable.', error);
        return cssEditorEscapeHtml(code) + '\n';
    }

    // A trailing newline keeps the highlight layer as tall as the textarea
    // when the caret is on an empty final line.
    return hljs.highlight(code, { language: 'css' }).value + '\n';
};
