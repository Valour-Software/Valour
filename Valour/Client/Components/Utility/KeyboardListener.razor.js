export function init() {
    window.blazorFuncs.addKeyboardListenerEvent = function (dotNetReference) {
            document.addEventListener('keydown', event => {
                dotNetReference.invokeMethodAsync('OnKeyDown', event.key);
            });
        }
}