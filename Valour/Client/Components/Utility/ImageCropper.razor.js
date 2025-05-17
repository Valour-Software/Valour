const croppers = {};

export function initCropper(id, aspectRatio) {
    const image = document.getElementById(id);
    if (!image) return;
    croppers[id] = new Cropper(image, {
        aspectRatio: aspectRatio,
        viewMode: 1,
        autoCropArea: 1
    });
}

export function getCroppedImage(id, mimeType) {
    const cropper = croppers[id];
    if (!cropper) return null;
    return cropper.getCroppedCanvas().toDataURL(mimeType || 'image/png');
}

export function destroyCropper(id) {
    const cropper = croppers[id];
    if (cropper) {
        cropper.destroy();
        delete croppers[id];
    }
}
