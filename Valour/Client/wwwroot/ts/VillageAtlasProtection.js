const imageUrls = new Map();
const magic = [86, 76, 84, 69, 88, 48, 48, 49];
const pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
export function isProtectedVillageAtlas(url) {
    return String(url).split(/[?#]/, 1)[0].endsWith(".vtex.bin");
}
export function decodeVillageAtlas(encoded) {
    if (encoded.length < 44 || !magic.every((value, index) => encoded[index] === value))
        throw new Error("Unsupported Village atlas package.");
    const header = new DataView(encoded.buffer, encoded.byteOffset, 20);
    const length = header.getUint32(8, true);
    if (length < 24 || length > 16 * 1024 * 1024 || length !== encoded.length - 20)
        throw new Error("Invalid Village atlas size.");
    let seed = header.getUint32(16, true);
    let checksum = 2166136261;
    const png = new Uint8Array(length);
    for (let i = 0; i < length; i++) {
        seed ^= seed << 13;
        seed ^= seed >>> 17;
        seed ^= seed << 5;
        png[i] = encoded[i + 20] ^ (seed & 255);
        checksum = Math.imul(checksum ^ png[i], 16777619) >>> 0;
    }
    if (checksum !== header.getUint32(12, true) || !pngSignature.every((value, index) => png[index] === value))
        throw new Error("The Village atlas package is damaged.");
    return png;
}
export function resolveVillageImageUrl(url) {
    if (!isProtectedVillageAtlas(url))
        return Promise.resolve(url);
    const existing = imageUrls.get(url);
    if (existing)
        return existing;
    // CSS previews and canvas textures share this URL for the document lifetime.
    // This is reversible obfuscation, not encryption or an authorization boundary.
    const pending = (async () => {
        const response = await fetch(url);
        if (!response.ok)
            throw new Error(`Unable to load Village atlas (${response.status}).`);
        const png = decodeVillageAtlas(new Uint8Array(await response.arrayBuffer()));
        return URL.createObjectURL(new Blob([png], { type: "image/png" }));
    })().catch(error => {
        imageUrls.delete(url);
        throw error;
    });
    imageUrls.set(url, pending);
    return pending;
}
export async function resolveVillagePreviewImages(urls) {
    return Object.fromEntries(await Promise.all([...new Set(urls.filter(Boolean))].map(async (url) => [url, await resolveVillageImageUrl(url)])));
}
//# sourceMappingURL=VillageAtlasProtection.js.map