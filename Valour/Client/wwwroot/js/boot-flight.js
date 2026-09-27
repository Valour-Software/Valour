// Star flight for the loading scene. Stars stream out of a vanishing point and
// slow down as loading completes. Motion is based on elapsed time, so a delayed
// frame catches up instead of stalling. The same code runs in a worker when the
// browser can hand the canvas to one, and on the main thread otherwise.

export function createFlight(canvas, reducedMotion) {
    const ctx = canvas.getContext('2d');
    let width = 0, height = 0, ratio = 1;
    let stars = [];
    let progress = 0;
    let shown = 0;

    function spawn(depth) {
        const angle = Math.random() * Math.PI * 2;
        const spread = 0.02 + Math.random() * 0.98;
        const warm = Math.random();
        return {
            x: Math.cos(angle) * spread,
            y: Math.sin(angle) * spread,
            z: depth,
            size: 0.5 + Math.random() * 1.1,
            color: warm < 0.08 ? '255, 214, 170' : warm < 0.4 ? '200, 222, 255' : '240, 244, 255',
        };
    }

    function resize(w, h, r) {
        width = w;
        height = h;
        ratio = r;
        canvas.width = Math.round(w * r);
        canvas.height = Math.round(h * r);
        const count = Math.round(Math.min(340, Math.max(140, w * h / 5000)));
        stars = Array.from({ length: count }, () => spawn(Math.random()));
    }

    function setProgress(value) {
        progress = Math.max(progress, value);
    }

    function step(seconds) {
        const dt = Math.min(seconds, 0.1);
        shown += (progress - shown) * (1 - Math.exp(-dt * 2.5));
        const speed = reducedMotion ? 0 : (0.07 + 0.35 * Math.pow(1 - shown, 1.6)) * dt;

        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        ctx.clearRect(0, 0, width, height);
        const cx = width * 0.5, cy = height * 0.42;
        const reach = Math.max(width, height) * 0.155;
        const trail = Math.min(0.2, (0.07 + 0.35 * Math.pow(1 - shown, 1.6)) * 0.06);
        ctx.lineCap = 'round';

        for (const star of stars) {
            star.z -= speed * (0.35 + star.size * 0.2);
            if (star.z <= 0.02) {
                Object.assign(star, spawn(1));
                continue;
            }

            const sx = cx + star.x * reach / star.z;
            const sy = cy + star.y * reach / star.z;
            if (sx < -20 || sx > width + 20 || sy < -20 || sy > height + 20) {
                Object.assign(star, spawn(1));
                continue;
            }

            const tailZ = Math.min(1, star.z + trail);
            const tx = cx + star.x * reach / tailZ;
            const ty = cy + star.y * reach / tailZ;
            const near = 1 - star.z;
            const alpha = Math.min(1, near * 1.3) * 0.75;
            ctx.strokeStyle = `rgba(${star.color}, ${alpha.toFixed(3)})`;
            ctx.lineWidth = star.size * (0.5 + near * 1.1);
            ctx.beginPath();
            ctx.moveTo(tx, ty);
            ctx.lineTo(sx + 0.01, sy + 0.01);
            ctx.stroke();
        }
    }

    return { resize, setProgress, step };
}

// Drives a flight with animation frames, or timers where workers lack them.
export function runFlight(flight) {
    const frame = typeof requestAnimationFrame === 'function'
        ? requestAnimationFrame
        : callback => setTimeout(() => callback(performance.now()), 16);
    let last = performance.now();
    let running = true;

    function loop(now) {
        if (!running) return;
        flight.step((now - last) / 1000);
        last = now;
        frame(loop);
    }

    frame(loop);
    return () => { running = false; };
}
