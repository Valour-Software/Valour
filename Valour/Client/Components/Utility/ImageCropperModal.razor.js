export function initCropper(id, aspectRatio) {
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