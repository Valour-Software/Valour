declare var Cropper: any;

type CropService = {
    image: HTMLElement;
    cropper: any;
    getCroppedImage: (mimeType: string) => string | null;
    cleanup: () => void;
};

export function initCropper(id: string, aspectRatio: string): CropService {

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