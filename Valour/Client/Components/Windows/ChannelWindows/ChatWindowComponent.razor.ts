import DotnetObject = DotNet.DotnetObject;

type Channel = {
    dotnet: DotnetObject;
    messageHolderEl: HTMLElement;
    oldScrollHeight: number;
    oldScrollTop: number;
    lastTopLoadPos: number;
    stickToBottom: boolean;
    scrollUpTimer: number;
    
    hookEvents(): void;
    cleanup(): void;
    
    updateScrollPosition(): void;
    scaleScrollPosition(): void;
    isAtBottom(): boolean;
    checkBottomSticky(): void;
    scrollToBottom(force: boolean): void;
    scrollToBottomAnimated(): void;
    handleChatWindowScroll(e: MouseEvent): void;
};

export function init(dotnet: DotnetObject, messageWrapperEl: HTMLElement): Channel{
    const channel: Channel = {
        dotnet: dotnet,
        messageHolderEl: messageWrapperEl,
        oldScrollHeight: 0,
        oldScrollTop: 0,
        lastTopLoadPos: 0,
        stickToBottom: true,
        scrollUpTimer: Date.now(),
        
        updateScrollPosition(){
            this.oldScrollHeight = this.messageHolderEl.scrollHeight;
            this.oldScrollTop = this.messageHolderEl.scrollTop;
        },
        
        scaleScrollPosition(){
            this.messageHolderEl.scrollTop = this.oldScrollTop + (this.messageHolderEl.scrollHeight - this.oldScrollHeight);
        },
        
        isAtBottom(){
            return (this.messageHolderEl.scrollHeight - (this.messageHolderEl.scrollTop + this.messageHolderEl.getBoundingClientRect().height)) < 200;
        },
        
        checkBottomSticky(){
            this.stickToBottom = this.isAtBottom();
        },
        
        scrollToBottom(force){
            if (force || this.stickToBottom){
                this.messageHolderEl.scrollTop = this.messageHolderEl.scrollHeight;
                this.stickToBottom = true;
            }
        },
        
        scrollToBottomAnimated(){
            this.messageHolderEl.scrollTo({
                top: this.messageHolderEl.scrollHeight,
                behavior: 'smooth' // For smooth scrolling; use 'auto' for instant scroll
            });
        },
        
        handleChatWindowScroll(e: MouseEvent) {
            // Scrollbar is visible
            if (this.messageHolderEl.scrollHeight > this.messageHolderEl.clientHeight) {
                // User has reached top of scroll
                if (this.messageHolderEl.scrollTop < 2000 && this.scrollUpTimer < (Date.now() - 500)) {
                    this.scrollUpTimer = Date.now();
                    this.dotnet.invokeMethodAsync('OnScrollTopInvoke');
                }
            }

            this.checkBottomSticky();
        },
        
        hookEvents(){
            this.messageHolderEl.addEventListener('scroll', this.handleChatWindowScroll);
        },
        
        cleanup(){
            this.messageHolderEl.removeEventListener('scroll', this.handleChatWindowScroll);
        }
    };
    
    channel.hookEvents();
    
    return channel;
}