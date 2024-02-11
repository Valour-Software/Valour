export function beforeStart(options, extensions) {
    console.log("Injecting custom event scripts...");

    const element = document.createElement('script');
    element.src = "_content/Valour.Client/js/contextPressEvent.js";
    element.async = true;
    document.body.appendChild(element);
}

export function afterStarted(blazor) {
    console.log("Registering custom events...");

    blazor.registerCustomEventType('contextpress', {
        browserEventName: 'contextpress',
        createEventArgs: event => {
            return {
                bubbles: event.bubbles,
                cancelable: event.cancelable,
                screenX: event.detail.screenX,
                screenY: event.detail.screenY,
                clientX: event.detail.clientX,
                clientY: event.detail.clientY,
                offsetX: event.detail.offsetX,
                offsetY: event.detail.offsetY,
                pageX: event.detail.pageX,
                pageY: event.detail.pageY,
                sourceElement: event.srcElement.localName,
                targetElement: event.target.localName,
                timeStamp: event.timeStamp,
                type: event.type,
            };
        }
    });
}