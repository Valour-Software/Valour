declare var Cropper: any;

type CropService = {
    image: HTMLElement;
    cropper: any;
    getCroppedImage: (mimeType: string) => string | null;
    cleanup: () => void;
};

let cropperLoadPromise: Promise<void> | null = null;

function ensureCropperLoaded(): Promise<void> {
    if (typeof Cropper !== 'undefined') return Promise.resolve();

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

export async function initCropper(id: string, aspectRatio: string): Promise<CropService> {
    await ensureCropperLoaded();

    const image = document.getElementById(id);
    const cropper = new Cropper(image, {
        aspectRatio: aspectRatio,
        viewMode: 1,
        autoCropArea: 1
    });
    
    const service : CropService = {
        image,
        cropper,
        getCroppedImage: (mimeType: string) => {
            return cropper.getCroppedCanvas().toDataURL(mimeType);
        },
        cleanup: () => {
            cropper.destroy();
        }
    }
    
    return service;
}
