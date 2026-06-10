export function init(dotnet, messageWrapperEl) {
    const channel = {
        dotnet: dotnet,
        messageWrapperEl: messageWrapperEl,
        oldScrollHeight: 0,
        oldScrollTop: 0,
        lastTopLoadPos: 0,
        stickToBottom: true,
        scrollUpTimer: Date.now(),
        scrollDownTimer: Date.now(),
        scrollTimer: Date.now(),
        lastReportedBottomState: true,
        suppressPagingUntil: 0,
        updateScrollPosition() {
            this.oldScrollHeight = this.messageWrapperEl.scrollHeight;
            this.oldScrollTop = this.messageWrapperEl.scrollTop;
        },
        scaleScrollPosition() {
            const suppressUntil = Date.now() + 250;
            this.suppressPagingUntil = Math.max(this.suppressPagingUntil, suppressUntil);
            this.messageWrapperEl.scrollTop = this.oldScrollTop + (this.messageWrapperEl.scrollHeight - this.oldScrollHeight);
            window.setTimeout(() => {
                if (this.suppressPagingUntil <= suppressUntil) {
                    this.suppressPagingUntil = 0;
                }
                this.checkBottomSticky();
            }, 250);
        },
        shiftScrollPosition(amount) {
            this.updateScrollPosition();
            this.messageWrapperEl.scrollTop = this.oldScrollTop + amount;
        },
        isAtBottom() {
            return (this.messageWrapperEl.scrollHeight - (this.messageWrapperEl.scrollTop + this.messageWrapperEl.getBoundingClientRect().height)) < 200;
        },
        checkBottomSticky() {
            this.stickToBottom = this.isAtBottom();
            if (this.stickToBottom !== this.lastReportedBottomState) {
                this.lastReportedBottomState = this.stickToBottom;
                void this.dotnet.invokeMethodAsync('OnBottomStickinessChanged', this.stickToBottom);
            }
        },
        scrollToBottom(force) {
            if (force || this.stickToBottom) {
                this.messageWrapperEl.scrollTop = this.messageWrapperEl.scrollHeight;
                this.stickToBottom = true;
                this.checkBottomSticky();
            }
        },
        scrollToBottomDeferred(force) {
            // Two frames: lets Blazor commit pending DOM changes (e.g. the
            // reply preview growing the input area) before measuring.
            requestAnimationFrame(() => requestAnimationFrame(() => {
                this.scrollToBottom(force);
            }));
        },
        scrollToBottomAnimated() {
            this.messageWrapperEl.scrollTo({
                top: this.messageWrapperEl.scrollHeight,
                behavior: 'smooth' // For smooth scrolling; use 'auto' for instant scroll
            });
        },
        scrollFromTopToBottomAnimated() {
            this.suppressPagingUntil = Date.now() + 900;
            this.messageWrapperEl.scrollTop = 0;
            requestAnimationFrame(() => {
                this.messageWrapperEl.scrollTo({
                    top: this.messageWrapperEl.scrollHeight,
                    behavior: 'smooth'
                });
            });
            window.setTimeout(() => {
                this.suppressPagingUntil = 0;
                this.checkBottomSticky();
            }, 900);
        },
        async handleChatWindowScroll(e) {
            // NOTE: 'this' is the message wrapper
            const channel = this['context'];
            // Scrollbar is visible
            if (this.scrollHeight > this.clientHeight) {
                const pagingSuppressed = channel.suppressPagingUntil > Date.now();
                // User has reached top of scroll
                if (!pagingSuppressed && this.scrollTop < 2000 && channel.scrollUpTimer < (Date.now() - 500)) {
                    channel.scrollUpTimer = Date.now();
                    await channel.dotnet.invokeMethodAsync('OnScrollTopInvoke');
                }
                // User has reached bottom of scroll
                const distFromBottom = this.scrollHeight - (this.scrollTop + this.clientHeight);
                if (!pagingSuppressed && distFromBottom < 2000 && channel.scrollDownTimer < (Date.now() - 500)) {
                    channel.scrollDownTimer = Date.now();
                    await channel.dotnet.invokeMethodAsync('OnScrollBottomInvoke');
                }
                // Normal scroll event
                if (channel.scrollTimer < (Date.now() - 500)) {
                    await channel.dotnet.invokeMethodAsync('OnDebouncedScroll');
                }
            }
            channel.checkBottomSticky();
        },
        scrollToMessage(elementId, highlight) {
            // The target element may not exist yet (Blazor commits renders
            // asynchronously and messages build async), so retry across
            // frames for up to ~2s before giving up.
            const deadline = Date.now() + 2000;
            const attempt = () => {
                const el = document.getElementById(elementId);
                if (!el) {
                    if (Date.now() < deadline)
                        requestAnimationFrame(attempt);
                    return;
                }
                el.scrollIntoView({ block: 'center', behavior: 'instant' });
                if (highlight) {
                    // Force animation restart by removing and re-adding the class
                    el.classList.remove('highlighted');
                    void el.offsetWidth; // trigger reflow
                    el.classList.add('highlighted');
                    setTimeout(() => el.classList.remove('highlighted'), 3000);
                }
            };
            requestAnimationFrame(attempt);
        },
        hookEvents() {
            this.messageWrapperEl.addEventListener('scroll', this.handleChatWindowScroll);
        },
        cleanup() {
            this.messageWrapperEl.removeEventListener('scroll', this.handleChatWindowScroll);
        }
    };
    messageWrapperEl['context'] = channel;
    channel.hookEvents();
    return channel;
}
//# sourceMappingURL=ChatWindowComponent.razor.js.map