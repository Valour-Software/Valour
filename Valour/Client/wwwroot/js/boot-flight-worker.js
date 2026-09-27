// Runs the loading scene's star flight off the main thread, so it keeps moving
// while the app starts up.

import { createFlight, runFlight } from './boot-flight.js';

let flight = null;
let stop = null;

self.onmessage = event => {
    const message = event.data;
    if (message.type === 'init') {
        flight = createFlight(message.canvas, message.reducedMotion);
        flight.resize(message.width, message.height, message.ratio);
        stop = runFlight(flight);
    } else if (message.type === 'resize') {
        flight?.resize(message.width, message.height, message.ratio);
    } else if (message.type === 'progress') {
        flight?.setProgress(message.value);
    } else if (message.type === 'stop') {
        stop?.();
        self.close();
    }
};
