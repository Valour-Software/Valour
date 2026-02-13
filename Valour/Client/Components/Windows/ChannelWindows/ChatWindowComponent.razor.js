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
        updateScrollPosition() {
            this.oldScrollHeight = this.messageWrapperEl.scrollHeight;
            this.oldScrollTop = this.messageWrapperEl.scrollTop;
        },
        scaleScrollPosition() {
            this.messageWrapperEl.scrollTop = this.oldScrollTop + (this.messageWrapperEl.scrollHeight - this.oldScrollHeight);
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
        },
        scrollToBottom(force) {
            if (force || this.stickToBottom) {
                this.messageWrapperEl.scrollTop = this.messageWrapperEl.scrollHeight;
                this.stickToBottom = true;
            }
        },
        scrollToBottomAnimated() {
            this.messageWrapperEl.scrollTo({
                top: this.messageWrapperEl.scrollHeight,
                behavior: 'smooth' // For smooth scrolling; use 'auto' for instant scroll
            });
        },
        async handleChatWindowScroll(e) {
            // NOTE: 'this' is the message wrapper
            const channel = this['context'];
            // Scrollbar is visible
            if (this.scrollHeight > this.clientHeight) {
                // User has reached top of scroll
                if (this.scrollTop < 2000 && channel.scrollUpTimer < (Date.now() - 500)) {
                    channel.scrollUpTimer = Date.now();
                    await channel.dotnet.invokeMethodAsync('OnScrollTopInvoke');
                }
                // User has reached bottom of scroll
                const distFromBottom = this.scrollHeight - (this.scrollTop + this.clientHeight);
                if (distFromBottom < 2000 && channel.scrollDownTimer < (Date.now() - 500)) {
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
            // Wait for layout, then instant-scroll, then trigger highlight animation.
            requestAnimationFrame(() => {
                const el = document.getElementById(elementId);
                if (!el)
                    return;
                el.scrollIntoView({ block: 'center', behavior: 'instant' });
                if (highlight) {
                    // Force animation restart by removing and re-adding the class
                    el.classList.remove('highlighted');
                    void el.offsetWidth; // trigger reflow
                    el.classList.add('highlighted');
                    setTimeout(() => el.classList.remove('highlighted'), 3000);
                }
            });
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