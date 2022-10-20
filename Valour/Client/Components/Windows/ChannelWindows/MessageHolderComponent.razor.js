// Dict for message holders id -> obj
export const messageHolders = {};

// Initial setup of messageholder object
export function setup(id, ref){

    // Build object
    let holder = {
        id: id,
        element: document.getElementById('innerwindow-' + id),
        dotnet: ref,
        oldScrollHeight: 0,
        oldScrollTop: 0,
        lastTopLoadPos: 0,
        stickToBottom: true,
        scrollUpTimer: Date.now(),

        updateScrollPosition(){
            this.oldScrollHeight = this.element.scrollHeight;
            this.oldScrollTop = this.element.scrollTop;
        },

        scaleScrollPosition(){
            this.element.scrollTop = this.oldScrollTop + (this.element.scrollHeight - this.oldScrollHeight);
        },

        isAtBottom(){
            return (this.element.scrollHeight - (this.element.scrollTop + this.element.getBoundingClientRect().height)) < 75;
        },

        checkBottomSticky(){
            this.stickToBottom = this.isAtBottom();
        },

        scrollToBottom(force){
            if (force || this.stickToBottom){
                this.element.scrollTop = this.element.scrollHeight;
                this.stickToBottom = true;
            }
        },

        scrollToBottomAnimated(){
            $(this.element).animate({ scrollTop: this.element.scrollHeight }, "fast");
        },

        handleChatWindowScroll() {
            // Scrollbar is visible
            if (this.element.scrollHeight > this.element.clientHeight) {
                // User has reached top of scroll
                if (this.element.scrollTop < 2000 && this.scrollUpTimer < (Date.now() - 500)) {
                    this.scrollUpTimer = Date.now();
                    this.dotnet.invokeMethodAsync('OnScrollTopInvoke');
                }
            }
        }
    };

    // Set ref in dictionary
    messageHolders[id] = holder;

    // Attach scroll event to element
    holder.element.addEventListener('scroll', () => {
        holder.handleChatWindowScroll();
        holder.checkBottomSticky();
    });
}

// Updates positions in holder
export function updateScrollPosition(id){ 
    this.messageHolders[id].updateScrollPosition(); 
}

export function scaleScrollPosition(id){
    this.messageHolders[id].scaleScrollPosition();
}

export function isAtBottom(id){
    return this.messageHolders[id].isAtBottom();
}

export function scrollToBottom(id, force){
    this.messageHolders[id].scrollToBottom(force);
}

export function scrollToBottomAnimated(id){
    this.messageHolders[id].scrollToBottomAnimated();
}


