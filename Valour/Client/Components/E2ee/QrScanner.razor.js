let jsQrLoad = null;
// jsQR decodes frames on browsers without a native barcode detector, such as
// Safari and the iOS app. It is only loaded when needed.
const loadJsQr = (url) => {
    if (jsQrLoad) {
        return jsQrLoad;
    }
    jsQrLoad = new Promise((resolve, reject) => {
        const existing = window.jsQR;
        if (existing) {
            resolve(existing);
            return;
        }
        const script = document.createElement("script");
        script.src = url;
        script.async = true;
        script.onload = () => {
            const loaded = window.jsQR;
            if (loaded) {
                resolve(loaded);
            }
            else {
                reject(new Error("The QR decoder did not load."));
            }
        };
        script.onerror = () => {
            jsQrLoad = null;
            reject(new Error("The QR decoder could not be downloaded."));
        };
        document.head.appendChild(script);
    });
    return jsQrLoad;
};
const createDetector = () => {
    const ctor = window.BarcodeDetector;
    return ctor ? new ctor({ formats: ["qr_code"] }) : null;
};
// Errors reported to .NET carry only the browser's error name, such as
// "NotAllowedError", so the component can choose wording for its screen.
const errorName = (error) => error instanceof Error && error.name ? error.name : "Error";
const stopStream = (stream, video) => {
    if (stream) {
        for (const track of stream.getTracks()) {
            track.stop();
        }
    }
    video.srcObject = null;
};
export async function start(video, dotnet, jsQrUrl) {
    let stream = null;
    let detector = null;
    let decode = null;
    try {
        stream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: "environment" },
            audio: false
        });
        video.srcObject = stream;
        await video.play();
        detector = createDetector();
        decode = detector ? null : await loadJsQr(jsQrUrl);
    }
    catch (error) {
        // Never leave the camera on when starting fails part way.
        stopStream(stream, video);
        throw new Error(`qr-scanner:${errorName(error)}`);
    }
    const activeStream = stream;
    const canvas = document.createElement("canvas");
    const context = canvas.getContext("2d", { willReadFrequently: true });
    let running = true;
    let frame = 0;
    const scan = async () => {
        if (!running) {
            return;
        }
        try {
            let text = null;
            if (video.readyState >= video.HAVE_ENOUGH_DATA) {
                if (detector) {
                    const codes = await detector.detect(video);
                    // The scanner may have been stopped while detecting.
                    if (!running) {
                        return;
                    }
                    text = codes.length > 0 ? codes[0].rawValue : null;
                }
                else if (decode && context) {
                    canvas.width = video.videoWidth;
                    canvas.height = video.videoHeight;
                    context.drawImage(video, 0, 0, canvas.width, canvas.height);
                    const image = context.getImageData(0, 0, canvas.width, canvas.height);
                    text = decode(image.data, image.width, image.height)?.data ?? null;
                }
            }
            if (text) {
                running = false;
                await dotnet.invokeMethodAsync("OnCodeScanned", text);
                return;
            }
        }
        catch (error) {
            if (!running) {
                return;
            }
            running = false;
            await dotnet.invokeMethodAsync("OnScanError");
            return;
        }
        if (running) {
            frame = window.setTimeout(run, 150);
        }
    };
    // A closed component can no longer receive calls; that is not an error.
    const run = () => {
        scan().catch(() => undefined);
    };
    run();
    return {
        stop() {
            running = false;
            window.clearTimeout(frame);
            stopStream(activeStream, video);
        }
    };
}
//# sourceMappingURL=QrScanner.razor.js.map