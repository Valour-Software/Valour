let cropperLoadPromise = null;
function ensureCropperLoaded() {
    if (typeof Cropper !== 'undefined')
        return Promise.resolve();
    cropperLoadPromise ??= new Promise((resolve, reject) => {
        if (!document.querySelector('link[data-valour-cropper]')) {
            const css = document.createElement('link');
            css.rel = 'stylesheet';
            css.href = 'https://cdnjs.cloudflare.com/ajax/libs/cropperjs/1.5.13/cropper.min.css';
            css.dataset.valourCropper = 'true';
            document.head.appendChild(css);
        }
        const script = document.createElement('script');
        script.src = 'https://cdnjs.cloudflare.com/ajax/libs/cropperjs/1.5.13/cropper.min.js';
        script.onload = () => resolve();
        script.onerror = () => reject(new Error('Failed to load Cropper'));
        document.head.appendChild(script);
    });
    return cropperLoadPromise;
}
export async function initCropper(id, aspectRatio) {
    await ensureCropperLoaded();
    const image = document.getElementById(id);
    const cropper = new Cropper(image, {
        aspectRatio: aspectRatio,
        viewMode: 1,
        autoCropArea: 1
    });
    const service = {
        image,
        cropper,
        getCroppedImage: (mimeType) => {
            return cropper.getCroppedCanvas().toDataURL(mimeType);
        },
        cleanup: () => {
            cropper.destroy();
        }
    };
    return service;
}
//# sourceMappingURL=ImageCropperModal.razor.js.map