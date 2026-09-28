// ValourOS terminal: renders output from the .NET shell and implements a
// readline-style line editor. The shell decides what commands do; this file
// owns keys, history, animation, and drawing.
const HISTORY_KEY = "valouros-history";
const THEME_KEY = "valouros-theme";
const FONT_KEY = "valouros-font-size";
const WINDOWED_KEY = "valouros-windowed";
const MAX_LINES = 3000;
const MAX_HISTORY = 500;
const TYPING_INTERVAL = 3000;
const THEMES = ["deep-field", "phosphor", "amber", "aubergine", "dracula", "paper"];
const COLOR_PATTERN = /^#[0-9a-fA-F]{3,8}$/;
const IS_MAC = /Mac|iPhone|iPad/.test(navigator.platform) || navigator.userAgent.includes("Mac OS");
// The boot sequence plays once per page load. Later opens resume quietly.
let bootedThisPage = false;
function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className)
        node.className = className;
    if (text !== undefined)
        node.textContent = text;
    return node;
}
function styleClasses(style) {
    if (!style)
        return [];
    return style.split(" ").filter(Boolean).map(x => "vos-" + x);
}
function renderSeg(seg) {
    const isLink = !!seg.h && /^https?:\/\//i.test(seg.h);
    const node = isLink ? el("a") : el("span");
    node.textContent = seg.t;
    node.classList.add(...styleClasses(seg.c));
    if (seg.s && COLOR_PATTERN.test(seg.s))
        node.style.color = seg.s;
    if (isLink) {
        const a = node;
        a.href = seg.h;
        a.target = "_blank";
        a.rel = "noopener noreferrer";
    }
    return node;
}
function renderLine(line) {
    const row = el("div", "vos-line");
    if (line.id)
        row.dataset.id = line.id;
    for (const c of styleClasses(line.lc))
        row.classList.add(c);
    if (line.k === "msg") {
        row.classList.add("vos-msg");
        row.append(el("span", "vos-msg-time", line.time ?? ""));
        const nick = el("span", "vos-msg-nick");
        for (const seg of line.nick ?? [])
            nick.append(renderSeg(seg));
        nick.title = nick.textContent ?? "";
        row.append(nick);
        row.append(el("span", "vos-msg-bar", "│"));
        const body = el("span", "vos-msg-body");
        for (const seg of line.segs)
            body.append(renderSeg(seg));
        row.append(body);
        return row;
    }
    if (line.segs.length === 0)
        row.append(el("span", undefined, "​"));
    for (const seg of line.segs)
        row.append(renderSeg(seg));
    return row;
}
function delay(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}
function isWordChar(ch) {
    return /[\p{L}\p{N}_]/u.test(ch);
}
class ValourTerminal {
    constructor(root, dotnet) {
        this.isOpen = false;
        this.animating = false;
        this.booting = false;
        this.skipBoot = false;
        this.busy = false;
        this.mode = "shell";
        this.prompt = [];
        this.buffer = "";
        this.cursor = 0;
        this.killRing = "";
        this.lastKey = "";
        this.typeahead = "";
        this.typeaheadSubmit = false;
        this.shellHistory = [];
        this.chatHistory = [];
        this.historyIndex = -1;
        this.historyDraft = "";
        this.searching = false;
        this.searchQuery = "";
        this.searchIndex = -1;
        this.searchFailed = false;
        this.lines = new Map();
        this.queue = Promise.resolve();
        this.lastTypingSent = 0;
        this.clockTimer = 0;
        this.restoreFocus = null;
        this.savedBodyBackground = "";
        this.appAnimation = null;
        this.fontSize = 15;
        this.onGlobalKey = (e) => this.handleGlobalKey(e);
        this.root = root;
        this.dotnet = dotnet;
        this.build();
        this.loadPreferences();
        document.addEventListener("keydown", this.onGlobalKey, true);
    }
    //////////////
    // Building //
    //////////////
    build() {
        this.root.hidden = true;
        this.root.setAttribute("role", "application");
        this.root.setAttribute("aria-label", "ValourOS terminal");
        this.win = el("div", "vos-window");
        const titlebar = el("div", "vos-titlebar");
        const icon = el("span", "vos-title-icon", "▲");
        this.titleEl = el("span", "vos-title", "valouros");
        const controls = el("div", "vos-controls");
        const minimize = el("button", "vos-control vos-min");
        minimize.title = "Return to desktop";
        minimize.setAttribute("aria-label", "Return to the Valour desktop");
        const maximize = el("button", "vos-control vos-max");
        maximize.title = "Toggle window";
        maximize.setAttribute("aria-label", "Toggle windowed mode");
        const close = el("button", "vos-control vos-close");
        close.title = "Close terminal";
        close.setAttribute("aria-label", "Close terminal");
        for (const b of [minimize, maximize, close])
            b.type = "button";
        minimize.addEventListener("click", () => this.close());
        close.addEventListener("click", () => this.close());
        maximize.addEventListener("click", () => this.toggleWindowed());
        titlebar.addEventListener("dblclick", () => this.toggleWindowed());
        controls.append(minimize, maximize, close);
        titlebar.append(icon, this.titleEl, controls);
        this.screen = el("div", "vos-screen");
        this.output = el("div", "vos-output");
        this.output.setAttribute("aria-live", "polite");
        this.inputRow = el("div", "vos-line vos-input");
        this.promptEl = el("span", "vos-prompt");
        this.beforeEl = el("span", "vos-typed");
        this.cursorEl = el("span", "vos-cursor", " ");
        this.afterEl = el("span", "vos-typed");
        this.inputRow.append(this.promptEl, this.beforeEl, this.cursorEl, this.afterEl);
        this.inputRow.hidden = true;
        this.screen.append(this.output, this.inputRow);
        const status = el("div", "vos-status");
        this.statusLeft = el("div", "vos-status-left");
        this.statusRight = el("div", "vos-status-right");
        this.clockEl = el("span", "vos-clock");
        status.append(this.statusLeft, this.statusRight, this.clockEl);
        this.capture = el("textarea", "vos-capture");
        this.capture.setAttribute("autocapitalize", "off");
        this.capture.setAttribute("autocomplete", "off");
        this.capture.setAttribute("autocorrect", "off");
        this.capture.setAttribute("spellcheck", "false");
        this.capture.setAttribute("aria-label", "Terminal input");
        this.capture.addEventListener("keydown", e => this.handleKey(e));
        this.capture.addEventListener("input", () => this.handleInput());
        this.capture.addEventListener("compositionend", () => this.handleInput());
        this.capture.addEventListener("blur", () => this.win.classList.add("vos-unfocused"));
        this.capture.addEventListener("focus", () => this.win.classList.remove("vos-unfocused"));
        this.measure = el("span", "vos-measure", "MMMMMMMMMM");
        this.screen.addEventListener("mouseup", () => {
            const selection = window.getSelection();
            if (!selection || selection.isCollapsed)
                this.focus();
        });
        this.win.append(titlebar, this.screen, status, this.capture, this.measure);
        const crt = el("div", "vos-crt");
        crt.setAttribute("aria-hidden", "true");
        const stars = el("div", "vos-desktop");
        stars.setAttribute("aria-hidden", "true");
        this.beam = el("div", "vos-beam");
        this.beam.setAttribute("aria-hidden", "true");
        this.root.append(stars, this.win, crt, this.beam);
    }
    loadPreferences() {
        try {
            const history = JSON.parse(localStorage.getItem(HISTORY_KEY) ?? "[]");
            if (Array.isArray(history))
                this.shellHistory = history.filter(x => typeof x === "string").slice(-MAX_HISTORY);
        }
        catch {
            this.shellHistory = [];
        }
        const theme = localStorage.getItem(THEME_KEY);
        this.root.dataset.theme = theme && THEMES.includes(theme) ? theme : "deep-field";
        const size = parseInt(localStorage.getItem(FONT_KEY) ?? "", 10);
        this.setFontSize(Number.isFinite(size) ? size : 15);
        this.root.classList.toggle("vos-windowed", localStorage.getItem(WINDOWED_KEY) === "1");
    }
    //////////////////////
    // Open, close, boot //
    //////////////////////
    handleGlobalKey(e) {
        const toggle = (e.metaKey || e.ctrlKey) && !e.altKey && (e.code === "Slash" || e.key === "/");
        if (toggle) {
            e.preventDefault();
            e.stopPropagation();
            if (!e.repeat)
                this.toggle();
            return;
        }
        // Keys aimed elsewhere, for example after clicking the output, go to
        // the terminal. Copy is left alone so selected output can be copied.
        if (this.isOpen && !this.animating && e.target !== this.capture) {
            if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "c")
                return;
            e.stopPropagation();
            this.focus();
            if (!e.ctrlKey && !e.metaKey && !e.altKey && e.key.length === 1) {
                e.preventDefault();
                this.typeText(e.key);
            }
            else {
                this.handleKey(e);
            }
        }
    }
    toggle() {
        if (this.animating)
            return;
        if (this.isOpen)
            void this.close();
        else
            void this.open();
    }
    get reducedMotion() {
        return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    }
    get app() {
        return document.querySelector(".mobile-holder");
    }
    async open() {
        if (this.isOpen || this.animating)
            return;
        this.animating = true;
        this.isOpen = true;
        this.restoreFocus = document.activeElement;
        const app = this.app;
        const reduce = this.reducedMotion;
        // The app powers off like an old monitor: it squashes to a bright
        // line, the line shrinks to a point, and the terminal powers on.
        this.savedBodyBackground = document.body.style.background;
        document.body.style.background = "#000";
        if (app && !reduce) {
            // The overlay shows only the beam while the app collapses behind it.
            this.root.classList.add("vos-powering");
            this.root.hidden = false;
            this.appAnimation = app.animate([
                { transform: "scale(1, 1)", filter: "brightness(1)", opacity: 1 },
                { transform: "scale(1, 0.004)", filter: "brightness(3)", opacity: 1, offset: 0.55 },
                { transform: "scale(0, 0.004)", filter: "brightness(6)", opacity: 0 },
            ], { duration: 380, easing: "cubic-bezier(0.7, 0, 0.3, 1)", fill: "forwards" });
            this.sweepBeam(false, 380);
            await this.appAnimation.finished.catch(() => undefined);
            await delay(90);
        }
        if (app)
            app.inert = true;
        this.root.classList.remove("vos-powering");
        this.root.hidden = false;
        this.root.classList.add("vos-open");
        if (!reduce)
            this.sweepBeam(true, 300);
        if (!reduce) {
            await this.win.animate([
                { transform: "scale(0, 0.004)", filter: "brightness(6)", opacity: 0 },
                { transform: "scale(1, 0.004)", filter: "brightness(3)", opacity: 1, offset: 0.4 },
                { transform: "scale(1, 1)", filter: "brightness(1)", opacity: 1 },
            ], { duration: 440, easing: "cubic-bezier(0.2, 0.8, 0.2, 1)" }).finished.catch(() => undefined);
        }
        else {
            await this.win.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 150 }).finished.catch(() => undefined);
        }
        this.animating = false;
        this.startClock();
        this.focus();
        let boot;
        try {
            boot = await this.dotnet.invokeMethodAsync("OpenAsync", this.root.dataset.theme);
        }
        catch (err) {
            this.print([{ k: "text", segs: [{ t: "vsh: failed to start the shell: " + String(err), c: "err" }] }]);
            this.inputRow.hidden = false;
            return;
        }
        if (!this.isOpen)
            return;
        if (!bootedThisPage) {
            bootedThisPage = true;
            await this.playBoot(boot.boot);
            this.clear();
            await this.playLines(boot.welcome, 28);
        }
        this.inputRow.hidden = false;
        if (this.typeahead) {
            this.insert(this.typeahead);
            this.typeahead = "";
        }
        this.renderInput();
        this.scrollToBottom();
        if (this.typeaheadSubmit) {
            this.typeaheadSubmit = false;
            void this.submit();
        }
    }
    async close() {
        if (!this.isOpen || this.animating)
            return;
        this.animating = true;
        this.isOpen = false;
        this.searching = false;
        this.stopClock();
        void this.dotnet.invokeMethodAsync("SuspendAsync").catch(() => undefined);
        const app = this.app;
        const reduce = this.reducedMotion;
        if (!reduce) {
            await this.win.animate([
                { transform: "scale(1, 1)", filter: "brightness(1)", opacity: 1 },
                { transform: "scale(1, 0.004)", filter: "brightness(3)", opacity: 1, offset: 0.5 },
                { transform: "scale(0, 0.004)", filter: "brightness(6)", opacity: 0 },
            ], { duration: 340, easing: "cubic-bezier(0.7, 0, 0.3, 1)", fill: "forwards" }).finished.catch(() => undefined);
        }
        this.win.getAnimations().forEach(a => a.cancel());
        this.root.classList.remove("vos-open");
        if (reduce)
            this.root.hidden = true;
        else
            this.root.classList.add("vos-powering");
        if (app) {
            app.inert = false;
            this.appAnimation?.cancel();
            this.appAnimation = null;
            if (!reduce) {
                this.sweepBeam(true, 380);
                await app.animate([
                    { transform: "scale(0, 0.004)", filter: "brightness(6)", opacity: 0 },
                    { transform: "scale(1, 0.004)", filter: "brightness(3)", opacity: 1, offset: 0.45 },
                    { transform: "scale(1, 1)", filter: "brightness(1)", opacity: 1 },
                ], { duration: 420, easing: "cubic-bezier(0.2, 0.8, 0.2, 1)" }).finished.catch(() => undefined);
            }
        }
        this.root.classList.remove("vos-powering");
        this.root.hidden = true;
        document.body.style.background = this.savedBodyBackground;
        this.animating = false;
        if (this.restoreFocus instanceof HTMLElement && document.contains(this.restoreFocus))
            this.restoreFocus.focus();
    }
    /** Flashes the bright scan line, collapsing to a point or growing from one. */
    sweepBeam(grow, duration) {
        const frames = grow
            ? [
                { transform: "scaleX(0)", opacity: 1 },
                { transform: "scaleX(1)", opacity: 1, offset: 0.55 },
                { transform: "scaleX(1)", opacity: 0 },
            ]
            : [
                { transform: "scaleX(1)", opacity: 0 },
                { transform: "scaleX(1)", opacity: 1, offset: 0.5 },
                { transform: "scaleX(0.002)", opacity: 1, offset: 0.9 },
                { transform: "scaleX(0)", opacity: 0 },
            ];
        this.beam.animate(frames, { duration, easing: "cubic-bezier(0.6, 0, 0.3, 1)" });
    }
    async playBoot(lines) {
        this.booting = true;
        this.skipBoot = false;
        for (const line of lines) {
            if (!this.isOpen)
                break;
            this.print([line]);
            if (!this.skipBoot) {
                const text = line.segs.map(s => s.t).join("");
                const pause = text.startsWith("[  ") ? 35 + Math.random() * 70 : text.length === 0 ? 120 : 60 + Math.random() * 90;
                await delay(pause);
            }
        }
        if (!this.skipBoot)
            await delay(450);
        this.booting = false;
    }
    async playLines(lines, stagger) {
        this.booting = true;
        this.skipBoot = false;
        for (const line of lines) {
            this.print([line]);
            if (!this.skipBoot && this.isOpen)
                await delay(stagger);
        }
        this.booting = false;
    }
    toggleWindowed() {
        const windowed = !this.root.classList.contains("vos-windowed");
        this.root.classList.toggle("vos-windowed", windowed);
        localStorage.setItem(WINDOWED_KEY, windowed ? "1" : "0");
        this.focus();
    }
    ////////////////////////
    // Calls from the shell //
    ////////////////////////
    print(lines) {
        const stick = this.isNearBottom();
        const fragment = document.createDocumentFragment();
        for (const line of lines) {
            const node = renderLine(line);
            if (line.id)
                this.lines.set(line.id, node);
            fragment.append(node);
        }
        this.output.append(fragment);
        this.trim();
        if (stick)
            this.scrollToBottom();
    }
    replace(id, line) {
        const existing = this.lines.get(id);
        if (!existing || !existing.isConnected)
            return;
        const stick = this.isNearBottom();
        const node = renderLine(line);
        existing.replaceWith(node);
        this.lines.delete(id);
        if (line.id)
            this.lines.set(line.id, node);
        if (stick)
            this.scrollToBottom();
    }
    setPrompt(prompt, mode) {
        if (mode !== this.mode) {
            this.historyIndex = -1;
            this.historyDraft = "";
        }
        this.mode = mode;
        this.prompt = prompt ?? [];
        this.root.dataset.mode = mode;
        this.titleEl.textContent = mode === "chat"
            ? (this.prompt[1]?.t ?? "chat") + " - vsh"
            : this.prompt.slice(0, -1).map(s => s.t).join("");
        this.renderInput();
    }
    setStatus(status) {
        this.statusLeft.replaceChildren(...(status.left ?? []).map(renderSeg));
        this.statusRight.replaceChildren(...(status.right ?? []).map(renderSeg));
    }
    setTheme(theme) {
        if (!THEMES.includes(theme))
            return;
        this.root.dataset.theme = theme;
        localStorage.setItem(THEME_KEY, theme);
    }
    clear() {
        this.output.replaceChildren();
        this.lines.clear();
    }
    dispose() {
        document.removeEventListener("keydown", this.onGlobalKey, true);
        this.stopClock();
        if (this.isOpen) {
            const app = this.app;
            if (app)
                app.inert = false;
            this.appAnimation?.cancel();
            document.body.style.background = this.savedBodyBackground;
        }
        this.root.replaceChildren();
    }
    /////////////
    // Drawing //
    /////////////
    renderInput() {
        if (this.searching) {
            const match = this.searchIndex >= 0 ? this.currentHistory()[this.searchIndex] : "";
            const label = this.searchFailed ? "(failed reverse-i-search)`" : "(reverse-i-search)`";
            this.promptEl.replaceChildren(renderSeg({ t: label + this.searchQuery + "': ", c: "dim" }));
            const at = match ? Math.max(0, match.toLowerCase().indexOf(this.searchQuery.toLowerCase())) : 0;
            this.beforeEl.textContent = match.slice(0, at);
            this.cursorEl.textContent = match.charAt(at) || " ";
            this.afterEl.textContent = match.slice(at + 1);
        }
        else {
            this.promptEl.replaceChildren(...this.prompt.map(renderSeg));
            this.beforeEl.textContent = this.buffer.slice(0, this.cursor);
            const under = this.buffer.charAt(this.cursor);
            this.cursorEl.textContent = !under || under === "\n" ? " " : under;
            this.afterEl.textContent = under === "\n" ? this.buffer.slice(this.cursor) : this.buffer.slice(this.cursor + 1);
        }
        // Restart the blink so the cursor stays solid while typing.
        this.cursorEl.classList.remove("vos-blink");
        void this.cursorEl.offsetWidth;
        this.cursorEl.classList.add("vos-blink");
        this.positionCapture();
    }
    positionCapture() {
        // Keep the hidden textarea at the cursor so input method windows
        // appear in the right place.
        const cursor = this.cursorEl.getBoundingClientRect();
        const win = this.win.getBoundingClientRect();
        this.capture.style.left = `${Math.max(0, cursor.left - win.left)}px`;
        this.capture.style.top = `${Math.max(0, cursor.top - win.top)}px`;
    }
    isNearBottom() {
        const s = this.screen;
        return s.scrollHeight - s.scrollTop - s.clientHeight < 48;
    }
    scrollToBottom() {
        this.screen.scrollTop = this.screen.scrollHeight;
    }
    trim() {
        const extra = this.output.childElementCount - MAX_LINES;
        for (let i = 0; i < extra; i++) {
            const first = this.output.firstElementChild;
            if (!first)
                break;
            if (first.dataset.id)
                this.lines.delete(first.dataset.id);
            first.remove();
        }
    }
    columns() {
        const charWidth = this.measure.getBoundingClientRect().width / 10 || 9;
        return Math.max(20, Math.floor((this.screen.clientWidth - 32) / charWidth));
    }
    bell() {
        this.win.classList.remove("vos-bell");
        void this.win.offsetWidth;
        this.win.classList.add("vos-bell");
    }
    startClock() {
        const tick = () => {
            const now = new Date();
            this.clockEl.textContent = " " + now.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) + " ";
        };
        tick();
        this.stopClock();
        this.clockTimer = window.setInterval(tick, 10_000);
    }
    stopClock() {
        if (this.clockTimer)
            window.clearInterval(this.clockTimer);
        this.clockTimer = 0;
    }
    focus() {
        this.capture.focus({ preventScroll: true });
    }
    setFontSize(size) {
        this.fontSize = Math.min(28, Math.max(10, size));
        this.root.style.setProperty("--vos-font-size", `${this.fontSize}px`);
        localStorage.setItem(FONT_KEY, String(this.fontSize));
    }
    /////////////////
    // Line editor //
    /////////////////
    currentHistory() {
        return this.mode === "chat" ? this.chatHistory : this.shellHistory;
    }
    setBuffer(text, cursor = text.length) {
        this.buffer = text;
        this.cursor = Math.max(0, Math.min(cursor, text.length));
        this.renderInput();
        this.scrollToBottom();
    }
    insert(text) {
        if (!text)
            return;
        // Shell commands are one line; chat messages may span several.
        if (this.mode === "shell")
            text = text.replace(/\r?\n/g, " ");
        text = text.replace(/\r/g, "");
        this.setBuffer(this.buffer.slice(0, this.cursor) + text + this.buffer.slice(this.cursor), this.cursor + text.length);
        this.notifyTyping();
    }
    kill(from, to) {
        if (from === to)
            return;
        const [a, b] = from < to ? [from, to] : [to, from];
        this.killRing = this.buffer.slice(a, b);
        this.setBuffer(this.buffer.slice(0, a) + this.buffer.slice(b), a);
    }
    wordLeft(pos) {
        let i = pos;
        while (i > 0 && !isWordChar(this.buffer[i - 1]))
            i--;
        while (i > 0 && isWordChar(this.buffer[i - 1]))
            i--;
        return i;
    }
    wordRight(pos) {
        let i = pos;
        while (i < this.buffer.length && !isWordChar(this.buffer[i]))
            i++;
        while (i < this.buffer.length && isWordChar(this.buffer[i]))
            i++;
        return i;
    }
    shellWordLeft(pos) {
        // Ctrl+W deletes back to whitespace, like bash's unix-word-rubout.
        let i = pos;
        while (i > 0 && /\s/.test(this.buffer[i - 1]))
            i--;
        while (i > 0 && !/\s/.test(this.buffer[i - 1]))
            i--;
        return i;
    }
    historyMove(delta) {
        const history = this.currentHistory();
        if (history.length === 0)
            return this.bell();
        if (this.historyIndex === -1) {
            if (delta > 0)
                return this.bell();
            this.historyDraft = this.buffer;
            this.historyIndex = history.length;
        }
        const next = this.historyIndex + delta;
        if (next < 0)
            return this.bell();
        if (next >= history.length) {
            this.historyIndex = -1;
            this.setBuffer(this.historyDraft);
            return;
        }
        this.historyIndex = next;
        this.setBuffer(history[next]);
    }
    pushHistory(line) {
        if (!line.trim())
            return;
        const history = this.currentHistory();
        // A leading space keeps a command out of history, as in bash.
        if (this.mode === "shell" && line.startsWith(" "))
            return;
        if (history[history.length - 1] !== line)
            history.push(line);
        if (history.length > MAX_HISTORY)
            history.splice(0, history.length - MAX_HISTORY);
        if (this.mode === "shell") {
            // Chat history holds message text, so only commands are saved.
            try {
                localStorage.setItem(HISTORY_KEY, JSON.stringify(this.shellHistory));
            }
            catch {
                // Storage can be full or unavailable; history still works in memory.
            }
        }
    }
    notifyTyping() {
        if (this.mode !== "chat" || !this.buffer || this.buffer.startsWith("/"))
            return;
        const now = Date.now();
        if (now - this.lastTypingSent < TYPING_INTERVAL)
            return;
        this.lastTypingSent = now;
        void this.dotnet.invokeMethodAsync("TypingAsync").catch(() => undefined);
    }
    handleInput() {
        const value = this.capture.value;
        if (!value)
            return;
        // Wait for an input method to finish composing.
        if (this.capture.isComposing)
            return;
        this.capture.value = "";
        this.typeText(value);
    }
    typeText(value) {
        if (this.booting) {
            // Typing skips the animation and keeps what was typed, like
            // typeahead on a real console.
            this.skipBoot = true;
            this.typeahead += value;
            return;
        }
        if (this.busy)
            return;
        if (this.searching) {
            this.searchQuery += value.replace(/\s+/g, " ");
            this.runSearch(false);
            return;
        }
        this.lastKey = "";
        this.insert(value);
    }
    handleKey(e) {
        if (e.isComposing || e.keyCode === 229)
            return;
        // The app's own shortcuts must not see keys typed into the terminal.
        e.stopPropagation();
        const ctrl = e.ctrlKey && !e.metaKey;
        const meta = e.metaKey && !e.ctrlKey;
        const alt = e.altKey;
        const key = e.key;
        const code = e.code;
        if (this.booting) {
            if (!e.metaKey && !e.ctrlKey && key.length !== 1)
                e.preventDefault();
            if (key === "Enter" && this.typeahead)
                this.typeaheadSubmit = true;
            this.skipBoot = true;
            return;
        }
        // Copying selected output.
        const selected = window.getSelection()?.toString() ?? "";
        const copyCombo = (meta && key === "c") || (ctrl && e.shiftKey && code === "KeyC") || (ctrl && !IS_MAC && key === "c" && selected);
        if (copyCombo) {
            e.preventDefault();
            if (selected)
                void navigator.clipboard?.writeText(selected);
            return;
        }
        // Pasting falls through to the textarea, which raises an input event.
        if ((meta && key === "v") || (ctrl && (key === "v" || (e.shiftKey && code === "KeyV")))) {
            return;
        }
        // Text size.
        if ((ctrl || meta) && (key === "=" || key === "+")) {
            e.preventDefault();
            this.setFontSize(this.fontSize + 1);
            return;
        }
        if ((ctrl || meta) && key === "-") {
            e.preventDefault();
            this.setFontSize(this.fontSize - 1);
            return;
        }
        if ((ctrl || meta) && key === "0") {
            e.preventDefault();
            this.setFontSize(15);
            return;
        }
        // Scrolling.
        if (key === "PageUp" || key === "PageDown") {
            e.preventDefault();
            const page = this.screen.clientHeight * 0.9;
            this.screen.scrollBy({ top: key === "PageUp" ? -page : page });
            return;
        }
        if (e.shiftKey && (key === "Home" || key === "End")) {
            e.preventDefault();
            this.screen.scrollTop = key === "Home" ? 0 : this.screen.scrollHeight;
            return;
        }
        if (this.busy) {
            if (ctrl && key === "c") {
                // Like a shell, Ctrl+C gives the prompt back right away. The
                // shell stops the command where it can; anything still running
                // finishes in the background.
                e.preventDefault();
                void this.dotnet.invokeMethodAsync("Interrupt").catch(() => undefined);
                this.print([{ k: "text", segs: [{ t: "^C", c: "dim" }] }]);
                this.queue = Promise.resolve();
                this.releasePrompt();
            }
            else if (key.length === 1 || key === "Enter" || key === "Tab" || key === "Backspace") {
                e.preventDefault();
            }
            return;
        }
        if (this.searching) {
            this.handleSearchKey(e);
            return;
        }
        const wasTab = this.lastKey === "Tab";
        this.lastKey = key === "Tab" ? "Tab" : "";
        // Printable characters arrive through the input event.
        if (!e.ctrlKey && !e.metaKey && !alt && key.length === 1)
            return;
        const handled = this.editKey(e, ctrl, meta, alt, key, code, wasTab);
        if (handled)
            e.preventDefault();
    }
    editKey(e, ctrl, meta, alt, key, code, wasTab) {
        const b = this.buffer;
        const c = this.cursor;
        if (key === "Enter") {
            if (this.mode === "chat" && e.shiftKey) {
                this.insert("\n");
            }
            else {
                void this.submit();
            }
            return true;
        }
        if (key === "Tab") {
            void this.complete(wasTab);
            return true;
        }
        if (key === "Escape") {
            return true;
        }
        if (key === "Backspace") {
            if (meta)
                this.kill(0, c);
            else if (alt || ctrl)
                this.kill(this.wordLeft(c), c);
            else if (c > 0)
                this.setBuffer(b.slice(0, c - 1) + b.slice(c), c - 1);
            else
                this.bell();
            return true;
        }
        if (key === "Delete") {
            if (alt)
                this.kill(c, this.wordRight(c));
            else if (c < b.length)
                this.setBuffer(b.slice(0, c) + b.slice(c + 1), c);
            return true;
        }
        if (key === "ArrowLeft") {
            this.setBuffer(b, meta ? 0 : alt || ctrl ? this.wordLeft(c) : c - 1);
            return true;
        }
        if (key === "ArrowRight") {
            this.setBuffer(b, meta ? b.length : alt || ctrl ? this.wordRight(c) : c + 1);
            return true;
        }
        if (key === "ArrowUp") {
            this.historyMove(-1);
            return true;
        }
        if (key === "ArrowDown") {
            this.historyMove(1);
            return true;
        }
        if (key === "Home") {
            this.setBuffer(b, 0);
            return true;
        }
        if (key === "End") {
            this.setBuffer(b, b.length);
            return true;
        }
        if (alt && !e.ctrlKey && !e.metaKey) {
            switch (code) {
                case "KeyB":
                    this.setBuffer(b, this.wordLeft(c));
                    return true;
                case "KeyF":
                    this.setBuffer(b, this.wordRight(c));
                    return true;
                case "KeyD":
                    this.kill(c, this.wordRight(c));
                    return true;
                case "Period": {
                    // Alt+. inserts the last word of the previous command.
                    const last = this.currentHistory()[this.currentHistory().length - 1] ?? "";
                    this.insert(last.trim().split(/\s+/).pop() ?? "");
                    return true;
                }
            }
            return false;
        }
        if (meta && key === "k") {
            this.clear();
            return true;
        }
        if (meta && key === "a") {
            const range = document.createRange();
            range.selectNodeContents(this.screen);
            const selection = window.getSelection();
            selection?.removeAllRanges();
            selection?.addRange(range);
            return true;
        }
        if (!ctrl)
            return false;
        switch (key.toLowerCase()) {
            case "a":
                this.setBuffer(b, 0);
                return true;
            case "e":
                this.setBuffer(b, b.length);
                return true;
            case "b":
                this.setBuffer(b, c - 1);
                return true;
            case "f":
                this.setBuffer(b, c + 1);
                return true;
            case "h":
                if (c > 0)
                    this.setBuffer(b.slice(0, c - 1) + b.slice(c), c - 1);
                return true;
            case "d":
                if (b.length === 0) {
                    void this.run(() => this.dotnet.invokeMethodAsync("EndOfInputAsync"), true);
                }
                else if (c < b.length) {
                    this.setBuffer(b.slice(0, c) + b.slice(c + 1), c);
                }
                return true;
            case "u":
                this.kill(0, c);
                return true;
            case "k":
                this.kill(c, b.length);
                return true;
            case "w":
                this.kill(this.shellWordLeft(c), c);
                return true;
            case "y":
                this.insert(this.killRing);
                return true;
            case "t":
                if (c > 0 && b.length > 1) {
                    const at = c === b.length ? c - 1 : c;
                    const chars = b.split("");
                    [chars[at - 1], chars[at]] = [chars[at], chars[at - 1]];
                    this.setBuffer(chars.join(""), Math.min(b.length, at + 1));
                }
                return true;
            case "p":
                this.historyMove(-1);
                return true;
            case "n":
                this.historyMove(1);
                return true;
            case "l":
                this.clear();
                this.renderInput();
                return true;
            case "c":
                this.echoInput("^C");
                this.historyIndex = -1;
                this.setBuffer("");
                return true;
            case "r":
                this.searching = true;
                this.searchQuery = "";
                this.searchIndex = -1;
                this.searchFailed = false;
                this.renderInput();
                return true;
            case "g":
                this.bell();
                return true;
        }
        return false;
    }
    handleSearchKey(e) {
        const ctrl = e.ctrlKey && !e.metaKey;
        const key = e.key;
        if (!e.ctrlKey && !e.metaKey && !e.altKey && key.length === 1)
            return;
        e.preventDefault();
        if (ctrl && key === "r") {
            this.runSearch(true);
            return;
        }
        if (key === "Backspace") {
            this.searchQuery = this.searchQuery.slice(0, -1);
            this.runSearch(false);
            return;
        }
        if ((ctrl && (key === "g" || key === "c")) || key === "Escape") {
            this.searching = false;
            this.renderInput();
            return;
        }
        // Anything else accepts the match and, for Enter, runs it.
        const match = this.searchIndex >= 0 ? this.currentHistory()[this.searchIndex] : this.buffer;
        this.searching = false;
        this.setBuffer(match);
        if (key === "Enter")
            void this.submit();
    }
    runSearch(older) {
        const history = this.currentHistory();
        const query = this.searchQuery.toLowerCase();
        const from = this.searchIndex === -1 ? history.length - 1 : older ? this.searchIndex - 1 : this.searchIndex;
        if (!query) {
            this.searchIndex = -1;
            this.searchFailed = false;
            this.renderInput();
            return;
        }
        for (let i = from; i >= 0; i--) {
            if (history[i].toLowerCase().includes(query)) {
                this.searchIndex = i;
                this.searchFailed = false;
                this.renderInput();
                return;
            }
        }
        this.searchFailed = true;
        this.bell();
        this.renderInput();
    }
    echoInput(suffix = "") {
        const segs = [...this.prompt, { t: this.buffer }];
        if (suffix)
            segs.push({ t: suffix, c: "dim" });
        this.print([{ k: "text", segs }]);
    }
    expandHistory(line) {
        const history = this.shellHistory;
        if (!line.includes("!"))
            return line;
        let expanded = line.replace(/!!/g, () => history[history.length - 1] ?? "!!");
        const bang = /^!(-?\d+|[^\s!=]+)/.exec(expanded);
        if (bang) {
            const ref = bang[1];
            let found;
            if (/^-?\d+$/.test(ref)) {
                const n = parseInt(ref, 10);
                found = n < 0 ? history[history.length + n] : history[n - 1];
            }
            else {
                found = [...history].reverse().find(x => x.startsWith(ref));
            }
            if (found === undefined)
                return null;
            expanded = found + expanded.slice(bang[0].length);
        }
        return expanded;
    }
    async submit() {
        let line = this.buffer;
        this.historyIndex = -1;
        this.historyDraft = "";
        if (this.mode === "chat") {
            this.setBuffer("");
            this.pushHistory(line);
            if (!line.trim())
                return;
            // Messages are sent in order without blocking the next one.
            // Slash commands block input like a shell command does.
            await this.run(() => this.dotnet.invokeMethodAsync("ExecuteAsync", line), line.startsWith("/") && !line.startsWith("//"));
            return;
        }
        this.echoInput();
        this.setBuffer("");
        const expanded = this.expandHistory(line);
        if (expanded === null) {
            this.print([{ k: "text", segs: [{ t: `vsh: ${line.trim()}: event not found`, c: "err" }] }]);
            return;
        }
        if (expanded !== line) {
            this.print([{ k: "text", segs: [{ t: expanded }] }]);
            line = expanded;
        }
        this.pushHistory(line);
        const trimmed = line.trim();
        if (!trimmed)
            return;
        // History and clear are handled here, since the history lives here.
        const [word, ...rest] = trimmed.split(/\s+/);
        if (word === "clear" || word === "reset") {
            this.clear();
            return;
        }
        if (word === "history") {
            if (rest[0] === "-c") {
                this.shellHistory = [];
                localStorage.removeItem(HISTORY_KEY);
                return;
            }
            const count = parseInt(rest[0] ?? "", 10);
            const start = Number.isFinite(count) ? Math.max(0, this.shellHistory.length - count) : 0;
            this.print(this.shellHistory.slice(start).map((cmd, i) => ({
                k: "text",
                segs: [{ t: String(start + i + 1).padStart(5) + "  ", c: "dim" }, { t: cmd }],
            })));
            return;
        }
        await this.run(() => this.dotnet.invokeMethodAsync("ExecuteAsync", line), true);
    }
    run(action, blocking) {
        if (blocking) {
            this.busy = true;
            this.inputRow.hidden = true;
        }
        const task = this.queue.then(action).catch(err => {
            this.print([{ k: "text", segs: [{ t: "vsh: " + String(err?.message ?? err), c: "err" }] }]);
        });
        this.queue = task;
        if (!blocking)
            return task;
        return task.finally(() => {
            // An interrupted command may finish after a newer one started.
            if (this.queue === task)
                this.releasePrompt();
        });
    }
    releasePrompt() {
        this.busy = false;
        if (this.isOpen) {
            this.inputRow.hidden = false;
            this.renderInput();
            this.scrollToBottom();
            this.focus();
        }
    }
    async complete(showAll) {
        const line = this.buffer;
        const cursor = this.cursor;
        let result;
        try {
            result = await this.dotnet.invokeMethodAsync("CompleteAsync", line, cursor);
        }
        catch {
            return;
        }
        // Ignore stale answers if the line changed while waiting.
        if (this.buffer !== line || this.cursor !== cursor || !result)
            return;
        const items = result.items ?? [];
        const typed = line.slice(result.start, result.end);
        if (items.length === 0) {
            this.bell();
            return;
        }
        if (items.length === 1) {
            this.replaceRange(result.start, result.end, items[0].insert);
            return;
        }
        const common = this.commonPrefix(items.map(x => x.insert));
        if (common.length > typed.length) {
            this.replaceRange(result.start, result.end, common);
            this.lastKey = "";
            return;
        }
        if (!showAll) {
            this.bell();
            return;
        }
        // Second Tab lists the options in columns, like bash.
        this.echoInput();
        const names = items.map(x => x.display);
        const width = Math.min(48, Math.max(...names.map(x => x.length)) + 2);
        const perRow = Math.max(1, Math.floor(this.columns() / width));
        const rows = Math.ceil(names.length / perRow);
        const out = [];
        for (let r = 0; r < rows; r++) {
            const segs = [];
            for (let col = 0; col < perRow; col++) {
                const name = names[col * rows + r];
                if (name === undefined)
                    break;
                segs.push({ t: name.padEnd(width), c: name.endsWith("/") ? "blue bold" : name.startsWith("#") ? "cyan" : undefined });
            }
            out.push({ k: "text", segs });
        }
        this.print(out);
        this.renderInput();
    }
    replaceRange(start, end, text) {
        const next = this.buffer.slice(0, start) + text + this.buffer.slice(end);
        this.setBuffer(next, start + text.length);
    }
    commonPrefix(values) {
        if (values.length === 0)
            return "";
        let prefix = values[0];
        for (const value of values) {
            let i = 0;
            while (i < prefix.length && i < value.length && prefix[i].toLowerCase() === value[i].toLowerCase())
                i++;
            prefix = prefix.slice(0, i);
        }
        return prefix;
    }
}
export function create(root, dotnet) {
    const terminal = new ValourTerminal(root, dotnet);
    return {
        print: (lines) => terminal.print(lines),
        replace: (id, line) => terminal.replace(id, line),
        setPrompt: (prompt, mode) => terminal.setPrompt(prompt, mode),
        setStatus: (status) => terminal.setStatus(status),
        setTheme: (theme) => terminal.setTheme(theme),
        clear: () => terminal.clear(),
        close: () => terminal.close(),
        open: () => terminal.open(),
        dispose: () => terminal.dispose(),
    };
}
//# sourceMappingURL=ValourTerminal.razor.js.map