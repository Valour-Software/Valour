type DotNetReference = {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
};

type JsQr = (data: Uint8ClampedArray, width: number, height: number) => { data: string } | null;

type Detector = {
    detect(source: HTMLVideoElement): Promise<Array<{ rawValue: string }>>;
};

let jsQrLoad: Promise<JsQr> | null = null;

// jsQR decodes frames on browsers without a native barcode detector, such as
// Safari and the iOS app. It is only loaded when needed.
const loadJsQr = (url: string): Promise<JsQr> => {
    if (jsQrLoad) {
        return jsQrLoad;
    }

    jsQrLoad = new Promise<JsQr>((resolve, reject) => {
        const existing = (window as unknown as { jsQR?: JsQr }).jsQR;
        if (existing) {
            resolve(existing);
            return;
        }

        const script = document.createElement("script");
        script.src = url;
        script.async = true;
        script.onload = () => {
            const loaded = (window as unknown as { jsQR?: JsQr }).jsQR;
            if (loaded) {
                resolve(loaded);
            } else {
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

const createDetector = (): Detector | null => {
    const ctor = (window as unknown as {
        BarcodeDetector?: new (options: { formats: string[] }) => Detector;
    }).BarcodeDetector;
    return ctor ? new ctor({ formats: ["qr_code"] }) : null;
};

// Errors reported to .NET carry only the browser's error name, such as
// "NotAllowedError", so the component can choose wording for its screen.
const errorName = (error: unknown): string =>
    error instanceof Error && error.name ? error.name : "Error";

const stopStream = (stream: MediaStream | null, video: HTMLVideoElement): void => {
    if (stream) {
        for (const track of stream.getTracks()) {
            track.stop();
        }
    }
    video.srcObject = null;
};

export async function start(video: HTMLVideoElement, dotnet: DotNetReference, jsQrUrl: string) {
    let stream: MediaStream | null = null;
    let detector: Detector | null = null;
    let decode: JsQr | null = null;

    try {
        stream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: "environment" },
            audio: false
        });

        video.srcObject = stream;
        await video.play();

        detector = createDetector();
        decode = detector ? null : await loadJsQr(jsQrUrl);
    } catch (error) {
        // Never leave the camera on when starting fails part way.
        stopStream(stream, video);
        throw new Error(`qr-scanner:${errorName(error)}`);
    }

    const activeStream = stream;
    const canvas = document.createElement("canvas");
    const context = canvas.getContext("2d", { willReadFrequently: true });

    let running = true;
    let frame = 0;

    const scan = async (): Promise<void> => {
        if (!running) {
            return;
        }

        try {
            let text: string | null = null;
            if (video.readyState >= video.HAVE_ENOUGH_DATA) {
                if (detector) {
                    const codes = await detector.detect(video);
                    // The scanner may have been stopped while detecting.
                    if (!running) {
                        return;
                    }
                    text = codes.length > 0 ? codes[0].rawValue : null;
                } else if (decode && context) {
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
        } catch (error) {
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
    const run = (): void => {
        scan().catch(() => undefined);
    };

    run();

    return {
        stop(): void {
            running = false;
            window.clearTimeout(frame);
            stopStream(activeStream, video);
        }
    };
}
