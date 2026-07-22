// Helpers for the in-app docs windows

// Scrolls the nth h2/h3 heading inside the given container into view.
// The TOC is computed from markdown source, so index order matches the
// rendered heading order (h1s are excluded from both).
window.wikiScrollToHeading = function (containerId, index) {
    const container = document.getElementById(containerId);
    if (!container) return;
    const headings = container.querySelectorAll('h2,h3');
    if (index >= 0 && index < headings.length)
        headings[index].scrollIntoView({ behavior: 'smooth', block: 'start' });
};

let highlightScriptPromise = null;

function ensureHighlightScript() {
    if (window.hljs) return Promise.resolve();
    if (highlightScriptPromise) return highlightScriptPromise;

    highlightScriptPromise = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = 'https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.2.0/highlight.min.js';
        script.async = true;
        script.onload = resolve;
        script.onerror = () => reject(new Error('Failed to load syntax highlighting'));
        document.head.appendChild(script);
    });

    return highlightScriptPromise;
}

// Load highlight.js only when a rendered wiki page actually contains code.
window.wikiHighlightAll = async function (containerId) {
    const container = document.getElementById(containerId);
    if (!container) return;
    if (!container.querySelector('pre code')) return;

    try {
        await ensureHighlightScript();
    } catch (error) {
        // Syntax highlighting is an optional enhancement. A CDN or network
        // failure must not prevent the wiki page itself from rendering.
        console.warn('Wiki syntax highlighting is unavailable.', error);
        return;
    }
    container.querySelectorAll('pre code').forEach(function (el) {
        try { hljs.highlightElement(el); } catch { /* already highlighted */ }
    });
};

// Injects a floating copy button into each code block. Idempotent, so it is
// safe to run after every render. Used by both the in-app docs window and
// the public docs pages (styles live in globals.css -> bundled.min.css).
window.wikiEnhanceCodeBlocks = function (containerId) {
    const container = containerId ? document.getElementById(containerId) : document;
    if (!container) return;

    container.querySelectorAll('pre').forEach(function (pre) {
        if (pre.querySelector('.docs-code-copy')) return;

        const code = pre.querySelector('code');
        if (!code) return;

        pre.classList.add('docs-code-host');

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'docs-code-copy';
        btn.title = 'Copy code';
        btn.setAttribute('aria-label', 'Copy code');
        btn.innerHTML = '<i class="bi bi-clipboard"></i>';

        btn.addEventListener('click', function () {
            const text = code.innerText.replace(/\n$/, '');

            const done = function () {
                btn.classList.add('copied');
                btn.innerHTML = '<i class="bi bi-check-lg"></i>';
                setTimeout(function () {
                    btn.classList.remove('copied');
                    btn.innerHTML = '<i class="bi bi-clipboard"></i>';
                }, 1600);
            };

            // Async clipboard first; embedded webviews (MAUI, previews) can
            // reject on permissions, so fall back to the legacy path
            const legacy = function () {
                const ta = document.createElement('textarea');
                ta.value = text;
                ta.style.position = 'fixed';
                ta.style.opacity = '0';
                document.body.appendChild(ta);
                ta.select();
                try { document.execCommand('copy'); done(); } catch { /* no clipboard */ }
                document.body.removeChild(ta);
            };

            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text).then(done).catch(legacy);
            } else {
                legacy();
            }
        });

        pre.appendChild(btn);
    });
};

// Clicks a hidden element (used to open the editor's file picker)
window.wikiClickElement = function (elementId) {
    document.getElementById(elementId)?.click();
};

// Inserts text at the caret of a textarea and returns the new value
window.wikiInsertAtCursor = function (textarea, text) {
    if (!textarea) return '';
    const start = textarea.selectionStart ?? textarea.value.length;
    const end = textarea.selectionEnd ?? start;
    textarea.value = textarea.value.slice(0, start) + text + textarea.value.slice(end);
    const pos = start + text.length;
    textarea.selectionStart = textarea.selectionEnd = pos;
    textarea.focus();
    return textarea.value;
};
