export function init(dotNetReference) {
    document.addEventListener('keydown', event => {
        dotNetReference.invokeMethodAsync('OnKeyDownInteropAsync', {
            code: event.code,
            key: event.key,
            location: event.location,
            repeat: event.repeat,
            shiftKey: event.shiftKey,
            ctrlKey: event.ctrlKey,
            altKey: event.altKey,
            metaKey: event.metaKey,
        });
    });
}