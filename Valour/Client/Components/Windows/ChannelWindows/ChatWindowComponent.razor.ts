import DotnetObject = DotNet.DotnetObject;

type Channel = {
    dotnet: DotnetObject;
    messageWrapperEl: HTMLElement;
    oldScrollHeight: number;
    oldScrollTop: number;
    lastTopLoadPos: number;
    stickToBottom: boolean;
    scrollUpTimer: number;
    scrollDownTimer: number;
    scrollTimer: number;
    lastReportedBottomState: boolean;
    suppressPagingUntil: number;
    
    hookEvents(): void;
    cleanup(): void;
    
    updateScrollPosition(): void;
    scaleScrollPosition(): void;
    shiftScrollPosition(amount: number): void;
    isAtBottom(): boolean;
    checkBottomSticky(): void;
    scrollToBottom(force: boolean): void;
    scrollToBottomAnimated(): void;
    scrollFromTopToBottomAnimated(): void;
    handleChatWindowScroll(e: MouseEvent): void;
    scrollToMessage(elementId: string, highlight: boolean): void;
};

export function init(dotnet: DotnetObject, messageWrapperEl: HTMLElement): Channel{
    
    const channel: Channel = {
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
        
        updateScrollPosition(){
            this.oldScrollHeight = this.messageWrapperEl.scrollHeight;
            this.oldScrollTop = this.messageWrapperEl.scrollTop;
        },
        
        scaleScrollPosition(){
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
        
        shiftScrollPosition(amount: number){
            this.updateScrollPosition();
            this.messageWrapperEl.scrollTop = this.oldScrollTop + amount;
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

                // Normal scroll event
                if (channel.scrollTimer < (Date.now() - 500)) {
                    await channel.dotnet.invokeMethodAsync('OnDebouncedScroll');
                }
            }

            channel.checkBottomSticky();
        },
        
        scrollToMessage(elementId: string, highlight: boolean){
            // Wait for layout, then instant-scroll, then trigger highlight animation.
            requestAnimationFrame(() => {
                const el = document.getElementById(elementId);
                if (!el) return;
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

        hookEvents(){
            this.messageWrapperEl.addEventListener('scroll', this.handleChatWindowScroll);
        },
        
        cleanup(){
            this.messageWrapperEl.removeEventListener('scroll', this.handleChatWindowScroll);
        }
    };

    messageWrapperEl['context'] = channel;

    channel.hookEvents();
    
    return channel;
}
