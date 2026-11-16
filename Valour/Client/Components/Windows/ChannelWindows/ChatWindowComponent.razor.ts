import DotnetObject = DotNet.DotnetObject;

type Channel = {
    dotnet: DotnetObject;
    messageWrapperEl: HTMLElement;
    oldScrollHeight: number;
    oldScrollTop: number;
    anchorId: string | null;
    anchorOffset: number;
    lastTopLoadPos: number;
    stickToBottom: boolean;
    scrollUpTimer: number;
    scrollDownTimer: number;
    lastReportedBottomState: boolean;
    suppressPagingUntil: number;
    resizeObserver: ResizeObserver | null;
    contentObserver: ResizeObserver | null;
    childObserver: MutationObserver | null;
    savedAnchorId: string | null;
    savedAnchorOffset: number;
    saveAnchorTimer: number;
    frozen: { atBottom: boolean; anchorId: string | null; anchorOffset: number } | null;
    restorePending: boolean;
    
    hookEvents(): void;
    cleanup(): void;
    
    updateScrollPosition(): void;
    scaleScrollPosition(): void;
    shiftScrollPosition(amount: number): void;
    isAtBottom(): boolean;
    checkBottomSticky(): void;
    scrollToBottom(force: boolean): void;
    scrollToBottomDeferred(force: boolean): void;
    scrollToBottomAnimated(): void;
    scrollFromTopToBottomAnimated(): void;
    handleChatWindowScroll(e: MouseEvent): void;
    scrollToMessage(elementId: string, highlight: boolean): void;
    findTopMessage(): { id: string; offset: number } | null;
    scheduleSaveAnchor(): void;
    freezePosition(): void;
    restorePosition(): void;
};

export function init(dotnet: DotnetObject, messageWrapperEl: HTMLElement): Channel{
    
    const channel: Channel = {
        dotnet: dotnet,
        messageWrapperEl: messageWrapperEl,
        oldScrollHeight: 0,
        oldScrollTop: 0,
        anchorId: null,
        anchorOffset: 0,
        lastTopLoadPos: 0,
        stickToBottom: true,
        scrollUpTimer: Date.now(),
        scrollDownTimer: Date.now(),
        lastReportedBottomState: true,
        suppressPagingUntil: 0,
        resizeObserver: null,
        contentObserver: null,
        childObserver: null,
        savedAnchorId: null,
        savedAnchorOffset: 0,
        saveAnchorTimer: 0,
        frozen: null,
        restorePending: false,
        
        updateScrollPosition(){
            const holder = this.messageWrapperEl;
            this.oldScrollHeight = holder.scrollHeight;
            this.oldScrollTop = holder.scrollTop;

            // Remember the first message visible at the top and where it sits,
            // so the view can be put back on it after messages are added above
            // or removed. Heights of skipped (content-visibility) messages are
            // only estimates, which makes a plain height difference unreliable.
            this.anchorId = null;
            const holderTop = holder.getBoundingClientRect().top;
            for (let i = 0; i < holder.children.length; i++) {
                const child = holder.children[i] as HTMLElement;
                if (!child.id || !child.classList.contains('message') || child.classList.contains('ghost'))
                    continue;

                const rect = child.getBoundingClientRect();
                if (rect.bottom > holderTop + 1) {
                    this.anchorId = child.id;
                    this.anchorOffset = rect.top - holderTop;
                    break;
                }
            }
        },
        
        scaleScrollPosition(){
            const holder = this.messageWrapperEl;
            const suppressUntil = Date.now() + 250;
            this.suppressPagingUntil = Math.max(this.suppressPagingUntil, suppressUntil);

            const anchor = this.anchorId ? document.getElementById(this.anchorId) : null;
            if (anchor) {
                const shift = (anchor.getBoundingClientRect().top - holder.getBoundingClientRect().top) - this.anchorOffset;
                holder.scrollTop += shift;
            } else {
                holder.scrollTop = this.oldScrollTop + (holder.scrollHeight - this.oldScrollHeight);
            }
            this.anchorId = null;
            window.setTimeout(() => {
                if (this.suppressPagingUntil <= suppressUntil) {
                    this.suppressPagingUntil = 0;
                }
                this.checkBottomSticky();
            }, 250);
        },
        
        shiftScrollPosition(amount: number){
            this.messageWrapperEl.scrollTop += amount;
        },
        
        // The first message whose bottom is below the top of the view. Children
        // are in document order, so their bottoms only grow and a binary
        // search finds it without measuring every message.
        findTopMessage() {
            const holder = this.messageWrapperEl;
            const children = holder.children;
            const holderTop = holder.getBoundingClientRect().top;
            let low = 0;
            let high = children.length;
            while (low < high) {
                const mid = (low + high) >> 1;
                if (children[mid].getBoundingClientRect().bottom > holderTop + 1)
                    high = mid;
                else
                    low = mid + 1;
            }

            for (let i = low; i < children.length; i++) {
                const child = children[i] as HTMLElement;
                if (child.id && child.classList.contains('message') && !child.classList.contains('ghost'))
                    return { id: child.id, offset: child.getBoundingClientRect().top - holderTop };
            }

            return null;
        },

        // Positions cannot be measured once the window is hidden, so the place
        // to return to is saved whenever scrolling settles.
        scheduleSaveAnchor() {
            window.clearTimeout(this.saveAnchorTimer);
            this.saveAnchorTimer = window.setTimeout(() => {
                if (this.messageWrapperEl.clientHeight === 0)
                    return;

                const top = this.findTopMessage();
                this.savedAnchorId = top?.id ?? null;
                this.savedAnchorOffset = top?.offset ?? 0;
            }, 150);
        },

        freezePosition() {
            window.clearTimeout(this.saveAnchorTimer);
            this.frozen = {
                atBottom: this.stickToBottom,
                anchorId: this.savedAnchorId,
                anchorOffset: this.savedAnchorOffset
            };
        },

        restorePosition() {
            const holder = this.messageWrapperEl;
            if (!this.frozen)
                return;

            // Still hidden: the resize observer finishes once it has a size
            if (holder.clientHeight === 0) {
                this.restorePending = true;
                return;
            }

            const frozen = this.frozen;
            this.frozen = null;
            this.restorePending = false;
            this.suppressPagingUntil = Date.now() + 250;

            const anchor = frozen.anchorId ? document.getElementById(frozen.anchorId) : null;
            if (frozen.atBottom || !anchor) {
                holder.scrollTop = holder.scrollHeight;
                this.stickToBottom = true;
            } else {
                holder.scrollTop += (anchor.getBoundingClientRect().top - holder.getBoundingClientRect().top) - frozen.anchorOffset;
                this.stickToBottom = false;
            }

            this.checkBottomSticky();
        },

        isAtBottom(){
            return (this.messageWrapperEl.scrollHeight - (this.messageWrapperEl.scrollTop + this.messageWrapperEl.getBoundingClientRect().height)) < 200;
        },
        
        checkBottomSticky(){
            this.stickToBottom = this.isAtBottom();
            if (this.stickToBottom !== this.lastReportedBottomState) {
                this.lastReportedBottomState = this.stickToBottom;
                void this.dotnet.invokeMethodAsync('OnBottomStickinessChanged', this.stickToBottom);
            }
        },
        
        scrollToBottom(force){
            if (force || this.stickToBottom){
                this.messageWrapperEl.scrollTop = this.messageWrapperEl.scrollHeight;
                this.stickToBottom = true;
                this.checkBottomSticky();
            }
        },

        scrollToBottomDeferred(force){
            // Two frames: lets Blazor commit pending DOM changes (e.g. the
            // reply preview growing the input area) before measuring.
            requestAnimationFrame(() => requestAnimationFrame(() => {
                this.scrollToBottom(force);
            }));
        },
        
        scrollToBottomAnimated(){
            this.messageWrapperEl.scrollTo({
                top: this.messageWrapperEl.scrollHeight,
                behavior: 'smooth' // For smooth scrolling; use 'auto' for instant scroll
            });
        },

        scrollFromTopToBottomAnimated(){
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
        
        async handleChatWindowScroll(e: MouseEvent) {
            // NOTE: 'this' is the message wrapper
            
            const channel: Channel = this['context'];
            
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
            }

            channel.checkBottomSticky();
            channel.scheduleSaveAnchor();
        },
        
        scrollToMessage(elementId: string, highlight: boolean){
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

        hookEvents(){
            this.messageWrapperEl.addEventListener('scroll', this.handleChatWindowScroll);

            // Keep the chat pinned to the bottom when the viewport itself
            // resizes (emoji picker opening/closing, soft keyboard, reply
            // preview, etc). Fires every frame during animated resizes, so
            // the chat follows the animation smoothly.
            this.resizeObserver = new ResizeObserver(() => {
                if (this.restorePending)
                    this.restorePosition();
                else if (this.stickToBottom)
                    this.messageWrapperEl.scrollTop = this.messageWrapperEl.scrollHeight;
            });
            this.resizeObserver.observe(this.messageWrapperEl);

            // Keep the chat pinned to the bottom when its content changes
            // height after it was laid out: an image finishing loading, a
            // message being decrypted, an embed expanding. Browser scroll
            // anchoring differs between engines, so this does not rely on it.
            const holder = this.messageWrapperEl;
            this.contentObserver = new ResizeObserver(() => {
                if (this.stickToBottom && !this.frozen && holder.clientHeight > 0)
                    holder.scrollTop = holder.scrollHeight;
            });
            for (let i = 0; i < holder.children.length; i++)
                this.contentObserver.observe(holder.children[i]);

            this.childObserver = new MutationObserver(records => {
                for (const record of records) {
                    record.addedNodes.forEach(node => {
                        if (node instanceof Element)
                            this.contentObserver?.observe(node);
                    });
                    record.removedNodes.forEach(node => {
                        if (node instanceof Element)
                            this.contentObserver?.unobserve(node);
                    });
                }
            });
            this.childObserver.observe(holder, { childList: true });
        },
        
        cleanup(){
            window.clearTimeout(this.saveAnchorTimer);
            this.messageWrapperEl.removeEventListener('scroll', this.handleChatWindowScroll);
            this.resizeObserver?.disconnect();
            this.resizeObserver = null;
            this.contentObserver?.disconnect();
            this.contentObserver = null;
            this.childObserver?.disconnect();
            this.childObserver = null;
        }
    };

    messageWrapperEl['context'] = channel;

    channel.hookEvents();
    
    return channel;
}
