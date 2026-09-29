// Procedural nebula skies. A sky is built in two passes: the gas field (the
// expensive per-pixel noise work, done at reduced resolution and safe to run in
// a worker) and composition (upscaling, stars and grain on a 2D canvas).
// Everything is deterministic for a given seed, palette and size.

// Hues in OKLCH degrees: [outer envelope, body, bright core]. Named after real
// nebulae whose emission colors they loosely follow.
export const PALETTES = {
    valour: [300, 265, 205],
    carina: [205, 280, 350],
    eagle: [230, 190, 85],
    rosette: [260, 330, 20],
    crab: [30, 60, 210],
    helix: [190, 230, 300],
    veil: [215, 255, 185],
    lagoon: [330, 10, 60],
    orion: [250, 320, 35],
    tarantula: [180, 290, 45]
};

// Palettes a planet can be assigned by default. The Valour palette is kept for
// brand surfaces.
export const PLANET_PALETTES = Object.keys(PALETTES).filter(name => name !== 'valour');

const DEFAULTS = {
    seed: 'valour',
    palette: null,
    intensity: 1,
    focusX: 0.62,
    focusY: 0.42,
    spread: 0.45,
    scale: 2.2,
    stars: 260,
    base: 0.1,
    baseHue: 255,
    grain: 0.06,
    haze: 0.55,
    warp: 1,
    detail: 0.7,
    rough: 0.6,
    resolution: 2,
    blur: 0.25
};

// FNV-1a over the seed string. Ids arrive as strings because snowflake ids
// overflow JS number precision.
export function hashSeed(seed) {
    const text = String(seed ?? '');
    let h = 0x811c9dc5;
    for (let i = 0; i < text.length; i++) {
        h ^= text.charCodeAt(i);
        h = Math.imul(h, 0x01000193);
    }
    return h >>> 0 || 1;
}

export function paletteFor(seed) {
    return PLANET_PALETTES[hashSeed(seed) % PLANET_PALETTES.length];
}

export function resolveOptions(options) {
    const o = { ...DEFAULTS };
    for (const [key, value] of Object.entries(options ?? {})) {
        if (value !== null && value !== undefined)
            o[key] = value;
    }
    const name = String(o.palette ?? '').toLowerCase();
    o.palette = PALETTES[name] ? name : paletteFor(o.seed);
    o.hues = PALETTES[o.palette];
    o.seedNumber = hashSeed(o.seed);
    return o;
}

export function cacheKey(o, width, height) {
    return [o.seed, o.palette, o.intensity, o.focusX, o.focusY, o.spread, o.scale, o.stars, o.base, o.grain, o.haze, o.detail, o.rough, o.resolution, width, height].join('|');
}

// The reduced-resolution size of the gas field. Large skies are capped so a
// single render stays within a predictable budget on slow devices.
export function gasSize(o, width, height, maxPixels = 320000) {
    let resolution = o.resolution;
    while ((width / resolution) * (height / resolution) > maxPixels)
        resolution += 0.5;
    return { gw: Math.max(1, Math.ceil(width / resolution)), gh: Math.max(1, Math.ceil(height / resolution)), resolution };
}

function rng(seed) {
    let s = seed >>> 0 || 1;
    return () => {
        s = Math.imul(s ^ (s >>> 15), 1 | s);
        s ^= s + Math.imul(s ^ (s >>> 7), 61 | s);
        return ((s ^ (s >>> 14)) >>> 0) / 4294967296;
    };
}

function gradientNoise(seed) {
    const random = rng(seed), p = new Uint8Array(512), gx = new Float32Array(256), gy = new Float32Array(256);
    for (let i = 0; i < 256; i++) {
        p[i] = i;
        const a = random() * Math.PI * 2;
        gx[i] = Math.cos(a);
        gy[i] = Math.sin(a);
    }
    for (let i = 255; i > 0; i--) {
        const j = (random() * (i + 1)) | 0;
        const t = p[i]; p[i] = p[j]; p[j] = t;
    }
    for (let i = 0; i < 256; i++) p[i + 256] = p[i];
    const fade = t => t * t * t * (t * (t * 6 - 15) + 10);
    return (x, y) => {
        const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi, X = xi & 255, Y = yi & 255;
        const h00 = p[p[X] + Y], h10 = p[p[X + 1] + Y], h01 = p[p[X] + Y + 1], h11 = p[p[X + 1] + Y + 1];
        const a = gx[h00] * xf + gy[h00] * yf;
        const b = gx[h10] * (xf - 1) + gy[h10] * yf;
        const c = gx[h01] * xf + gy[h01] * (yf - 1);
        const d = gx[h11] * (xf - 1) + gy[h11] * (yf - 1);
        const u = fade(xf), v = fade(yf);
        const top = a + u * (b - a);
        return top + v * ((c + u * (d - c)) - top);
    };
}

function fbm(noise, x, y, octaves, gain = 0.5) {
    let amplitude = 0.5, sum = 0, frequency = 1, norm = 0;
    for (let i = 0; i < octaves; i++) {
        sum += amplitude * noise(x * frequency, y * frequency);
        norm += amplitude;
        frequency *= 2.03;
        amplitude *= gain;
    }
    return sum * (0.9375 / norm);
}

function oklchToRgb(L, C, h) {
    const hr = h * Math.PI / 180, a = C * Math.cos(hr), b = C * Math.sin(hr);
    const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3;
    const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3;
    const s = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3;
    const linear = [
        4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s
    ];
    return linear.map(c => {
        c = Math.max(0, Math.min(1, c));
        return 255 * (c <= 0.0031308 ? 12.92 * c : 1.055 * Math.pow(c, 1 / 2.4) - 0.055);
    });
}

const mix = (a, b, t) => a + (b - a) * t;
const mixHue = (a, b, t) => {
    const d = ((b - a + 540) % 360) - 180;
    return (a + d * t + 360) % 360;
};

// Computes rows [rowStart, rowEnd) of the gas field into an RGBA buffer of size
// gw x gh. Width and height are the final canvas size, used for the aspect ratio.
export function computeGasRows(o, gw, gh, width, height, pixels, rowStart, rowEnd) {
    const seed = o.seedNumber;
    const n1 = gradientNoise(seed), n2 = gradientNoise(seed * 7 + 3), n3 = gradientNoise(seed * 13 + 11), n4 = gradientNoise(seed * 31 + 5);
    const aspect = width / height, hues = o.hues;
    const baseRgb = oklchToRgb(o.base, 0.012, o.baseHue);
    for (let j = rowStart; j < rowEnd; j++) {
        for (let i = 0; i < gw; i++) {
            const u = i / gw, v = j / gh, x = u * aspect * o.scale, y = v * o.scale;
            const wx = fbm(n2, x + 1.7, y + 9.2, 4), wy = fbm(n2, x + 8.3, y + 2.8, 4);
            const base = fbm(n1, x + o.warp * wx, y + o.warp * wy, 9, o.rough) * 0.5 + 0.5;

            let ridge = 0, ra = 0.5, rf = 1.3, rn = 0;
            for (let q = 0; q < 7; q++) {
                rn += ra;
                ridge += ra * (1 - Math.abs(n3(x * rf + o.warp * 1.2 * wx, y * rf + o.warp * 1.2 * wy)));
                rf *= 2.1;
                ra *= o.rough;
            }
            ridge *= 0.99 / rn;

            let d = base * 0.78 + Math.pow(ridge, 3) * 0.42;
            const dx = (u - o.focusX) * aspect, dy = v - o.focusY;
            const falloff = Math.exp(-(dx * dx + dy * dy) / (2 * o.spread * o.spread));
            const haze = Math.pow(falloff, 1.6) * Math.max(0, base - 0.3) * o.haze;
            d = Math.max(0, d * 1.9 - 0.97) * falloff + haze;

            const mottle = fbm(n2, x * 9 + 17, y * 9 + 4, 5, o.rough) * 0.5 + 0.5;
            d *= mix(1, 0.35 + 1.3 * mottle, o.detail);
            const dustRaw = fbm(n4, x * 1.4 + 0.5 * wx, y * 1.4 + 0.5 * wy, 7) * 0.5 + 0.5;
            const dt = Math.min(1, Math.max(0, (dustRaw - 0.5) / 0.16));
            d *= 1 - 0.85 * dt * dt * (3 - 2 * dt);
            d = Math.min(1, d * o.intensity * 1.2);

            const sm = Math.min(1, Math.max(0, (d - 0.12) / 0.6)), ss = sm * sm * (3 - 2 * sm);
            const jitter = fbm(n2, x * 0.8 + 5, y * 0.8 + 1, 3) * 40;
            let hue = d > 0.55
                ? mixHue(hues[1], hues[2], Math.min(1, (d - 0.55) / 0.35))
                : mixHue(hues[0], hues[1], ss);
            hue = (hue + jitter + 360) % 360;

            const L = mix(o.base, 0.7, Math.pow(d, 1.5));
            const C = mix(0.015, 0.105, Math.pow(d, 0.6)) * (1 - 0.7 * Math.pow(d, 4));
            const rgb = oklchToRgb(L, C, hue), alpha = Math.min(1, d * 2.2), k = (j * gw + i) * 4;
            pixels[k] = mix(baseRgb[0], rgb[0], alpha);
            pixels[k + 1] = mix(baseRgb[1], rgb[1], alpha);
            pixels[k + 2] = mix(baseRgb[2], rgb[2], alpha);
            pixels[k + 3] = 255;
        }
    }
}

export function computeGas(o, gw, gh, width, height) {
    const pixels = new Uint8ClampedArray(gw * gh * 4);
    computeGasRows(o, gw, gh, width, height, pixels, 0, gh);
    return pixels;
}

// Draws a computed gas field onto a canvas of the final size, then adds stars
// and grain at full resolution.
export function compose(canvas, o, gas, gw, gh, resolution) {
    const W = canvas.width, H = canvas.height, ctx = canvas.getContext('2d');
    // A canvas the browser could not allocate has no pixels to read back.
    if (!W || !H || !ctx) return;
    const source = document.createElement('canvas');
    source.width = gw;
    source.height = gh;
    source.getContext('2d').putImageData(new ImageData(gas, gw, gh), 0, 0);

    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';
    ctx.filter = `blur(${resolution * o.blur}px)`;
    ctx.drawImage(source, 0, 0, W, H);
    ctx.filter = 'none';

    const random = rng(o.seedNumber * 97 + 1), count = Math.round(o.stars * W * H / 1e6);
    const temperatures = [[0.97, 0.03, 250], [0.98, 0.01, 90], [0.95, 0.05, 75], [0.9, 0.07, 55]];
    for (let s = 0; s < count; s++) {
        const x = random() * W, y = random() * H, m = Math.pow(random(), 7);
        const t = temperatures[(random() * temperatures.length) | 0];
        const color = oklchToRgb(t[0], t[1], t[2]).map(Math.round).join(',');
        const size = 0.6 + m * 2.4, alpha = 0.18 + 0.75 * Math.pow(random(), 2.5) * (0.4 + m);
        if (m > 0.35) {
            const glow = ctx.createRadialGradient(x, y, 0, x, y, size * 5);
            glow.addColorStop(0, `rgba(${color},${alpha * 0.35})`);
            glow.addColorStop(1, 'rgba(0,0,0,0)');
            ctx.fillStyle = glow;
            ctx.beginPath();
            ctx.arc(x, y, size * 5, 0, Math.PI * 2);
            ctx.fill();
        }
        ctx.fillStyle = `rgba(${color},${Math.min(1, alpha)})`;
        ctx.beginPath();
        ctx.arc(x, y, size / 2, 0, Math.PI * 2);
        ctx.fill();
    }

    for (let s = 0, n = Math.round(W * H / 900); s < n; s++) {
        ctx.fillStyle = `rgba(225,232,245,${0.05 + random() * 0.12})`;
        ctx.fillRect(random() * W, random() * H, 1, 1);
    }

    const image = ctx.getImageData(0, 0, W, H), data = image.data, grain = rng(o.seedNumber + 5);
    for (let k = 0; k < data.length; k += 4) {
        const luminance = (data[k] + data[k + 1] + data[k + 2]) / 765;
        const n = (grain() - 0.5) * 255 * o.grain * (0.5 + luminance * 2.5);
        data[k] += n;
        data[k + 1] += n;
        data[k + 2] += n;
    }
    ctx.putImageData(image, 0, 0);
}
