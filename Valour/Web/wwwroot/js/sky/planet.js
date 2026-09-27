// Procedural planets. Every community's planet is derived from its id: the
// genome decides the kind of world, its colors, surface, rings and moons, so the
// same planet looks the same everywhere and no two planets are alike. Only the
// night-side lights are a runtime input, because they show how many people are
// around.

import { PALETTES, paletteFor, hashSeed } from './nebula.js';

const TAU = Math.PI * 2;

export const PLANET_KINDS = ['terra', 'ocean', 'desert', 'gas', 'ice', 'rock', 'lava'];
const KIND_WEIGHTS = { terra: 24, ocean: 10, desert: 10, gas: 24, ice: 12, rock: 12, lava: 8 };
const RING_CHANCE = { terra: 0.08, ocean: 0.08, desert: 0.12, gas: 0.55, ice: 0.25, rock: 0.12, lava: 0.05 };
const ATMOSPHERE = { terra: 0.9, ocean: 1, desert: 0.55, gas: 0.7, ice: 0.45, rock: 0.12, lava: 0.35 };

export const DEFAULT_LIGHT = [-0.88, -0.32, 0.12];

function rng(seed) {
    let s = seed >>> 0 || 1;
    return () => {
        s = Math.imul(s ^ (s >>> 15), 1 | s);
        s ^= s + Math.imul(s ^ (s >>> 7), 61 | s);
        return ((s ^ (s >>> 14)) >>> 0) / 4294967296;
    };
}

const wrapHue = h => ((h % 360) + 360) % 360;

// Moves a hue out of the range between yellow-green and green, where low-chroma
// colors turn muddy.
function avoidMud(h) {
    h = wrapHue(h);
    if (h > 95 && h < 150) return h < 122 ? 95 - (122 - h) * 0.3 : 150 + (h - 122) * 0.3;
    return h;
}

export function planetGenome(seed) {
    const id = String(seed ?? '');
    const seedNumber = hashSeed(id);
    const random = rng(seedNumber ^ 0x9e3779b9);

    let roll = random() * Object.values(KIND_WEIGHTS).reduce((a, b) => a + b, 0), kind = 'terra';
    for (const [name, weight] of Object.entries(KIND_WEIGHTS)) {
        if ((roll -= weight) < 0) { kind = name; break; }
    }

    const palette = paletteFor(id);
    const offset = (random() - 0.5) * 90;
    let hues = PALETTES[palette].map(h => avoidMud(h + offset));
    if (kind === 'lava') hues = [avoidMud(10 + random() * 40), 20 + random() * 25, 55 + random() * 20];
    if (kind === 'desert') hues = [avoidMud(25 + random() * 60), avoidMud(40 + random() * 50), hues[2]];
    if (kind === 'ice') hues = [185 + random() * 90, hues[1], hues[2]];

    const rings = random() < RING_CHANCE[kind];
    const moonCount = random() < 0.38 ? (random() < 0.3 ? 2 : 1) : 0;
    const moons = [];
    for (let i = 0; i < moonCount; i++) {
        moons.push({
            angle: -Math.PI * (0.15 + random() * 0.7) + (i ? Math.PI : 0),
            distance: 1.5 + random() * 0.55,
            size: 0.1 + random() * 0.12,
            hue: wrapHue(hues[1] + (random() - 0.5) * 60),
            seed: (seedNumber + i * 7919) >>> 0
        });
    }

    return {
        seed: seedNumber,
        kind,
        palette,
        hues,
        size: 0.82 + random() * 0.18,
        spin: random() * TAU,
        axialTilt: (random() - 0.5) * 0.7,
        sea: kind === 'ocean' ? 0.2 + random() * 0.12 : -0.06 + random() * 0.2,
        clouds: kind === 'terra' || kind === 'ocean' ? 0.05 + random() * 0.3 : kind === 'desert' ? random() * 0.15 : 0,
        caps: 0.76 + random() * 0.16,
        bands: 5 + random() * 7,
        turbulence: 0.1 + random() * 0.22,
        storms: 0.28 + random() * 0.2,
        craters: 0.5 + random() * 0.8,
        atmosphere: ATMOSPHERE[kind] * (0.75 + random() * 0.35),
        rings: rings ? {
            inner: 1.3 + random() * 0.2,
            outer: 1.7 + random() * 0.45,
            tilt: (random() - 0.5) * 0.9,
            squash: 0.2 + random() * 0.14,
            gap: 0.4 + random() * 0.35,
            hue: wrapHue(hues[2] + (random() - 0.5) * 40)
        } : null,
        moons
    };
}

// How far the planet's drawing reaches from its center, in planet radii.
export function planetExtent(genome) {
    let extent = 1.25;
    if (genome.rings) extent = Math.max(extent, genome.rings.outer + 0.05);
    for (const moon of genome.moons) extent = Math.max(extent, moon.distance + moon.size + 0.05);
    return extent;
}

function hash3(seed) {
    return (x, y, z) => {
        let h = seed ^ Math.imul(x, 374761393) ^ Math.imul(y, 668265263) ^ Math.imul(z, 2147483647);
        h = Math.imul(h ^ (h >>> 13), 1274126177);
        return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
    };
}

function valueNoise3(seed) {
    const h = hash3(seed), f = t => t * t * (3 - 2 * t), lerp = (a, b, t) => a + (b - a) * t;
    return (x, y, z) => {
        const xi = Math.floor(x), yi = Math.floor(y), zi = Math.floor(z);
        const xf = f(x - xi), yf = f(y - yi), zf = f(z - zi);
        return lerp(
            lerp(lerp(h(xi, yi, zi), h(xi + 1, yi, zi), xf), lerp(h(xi, yi + 1, zi), h(xi + 1, yi + 1, zi), xf), yf),
            lerp(lerp(h(xi, yi, zi + 1), h(xi + 1, yi, zi + 1), xf), lerp(h(xi, yi + 1, zi + 1), h(xi + 1, yi + 1, zi + 1), xf), yf),
            zf) * 2 - 1;
    };
}

function fbm3(noise, x, y, z, octaves, gain = 0.5) {
    let amplitude = 0.5, sum = 0, frequency = 1, norm = 0;
    for (let i = 0; i < octaves; i++) {
        sum += amplitude * noise(x * frequency, y * frequency, z * frequency);
        norm += amplitude;
        frequency *= 2.02;
        amplitude *= gain;
    }
    return sum / norm;
}

function oklchToRgb(L, C, h) {
    const hr = h * Math.PI / 180, a = C * Math.cos(hr), b = C * Math.sin(hr);
    const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3;
    const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3;
    const s = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3;
    return [
        4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s
    ].map(c => {
        c = Math.max(0, Math.min(1, c));
        return 255 * (c <= 0.0031308 ? 12.92 * c : 1.055 * Math.pow(c, 1 / 2.4) - 0.055);
    });
}

const mix = (a, b, t) => a + (b - a) * t;
const clamp = v => Math.max(0, Math.min(1, v));
const smooth = (a, b, v) => { const t = clamp((v - a) / (b - a)); return t * t * (3 - 2 * t); };
const mixRgb = (a, b, t) => [mix(a[0], b[0], t), mix(a[1], b[1], t), mix(a[2], b[2], t)];

// Surface color and material for a point on the unit sphere, in the planet's own
// rotated frame. Returns [rgb, specular, cityMask, emissive].
function surface(g, n, sx, sy, sz) {
    const { kind, hues } = g;
    if (kind === 'gas') {
        const turbulence = fbm3(n.b, sx * 2.2, sy * 2.2, sz * 2.2, 5, 0.55);
        const band = fbm3(n.a, (sy + turbulence * g.turbulence) * g.bands, 0.5, 0.5, 4);
        const t = clamp(0.5 + band * 1.1);
        let rgb = oklchToRgb(0.5 + 0.26 * t, 0.045 + 0.03 * t, hues[1] + (t - 0.5) * 50);
        const storm = fbm3(n.c, sx * 3, sy * 6, sz * 3, 3);
        rgb = mixRgb(rgb, oklchToRgb(0.78, 0.06, hues[2]), smooth(g.storms, g.storms + 0.22, storm) * 0.55);
        return [rgb, 0, 0, 0];
    }
    if (kind === 'rock' || kind === 'lava') {
        const h = fbm3(n.a, sx * 3, sy * 3, sz * 3, 6, 0.55);
        let crater = 0;
        for (let q = 0; q < 3; q++) {
            const f = 5 + q * 6, c = fbm3(n.b, sx * f + q * 9, sy * f, sz * f, 2);
            crater += Math.max(0, 0.32 - Math.abs(c - 0.18) * 3) * (1 - q * 0.25);
        }
        crater *= g.craters;
        if (kind === 'rock')
            return [oklchToRgb(0.4 + h * 0.1 - crater * 0.25 + (crater > 0.12 ? 0.04 : 0), 0.024, hues[1]), 0, 0, 0];
        const vein = Math.abs(fbm3(n.c, sx * 4, sy * 4, sz * 4, 5, 0.55));
        const glow = smooth(0.07, 0.0, vein) * (0.6 + 0.4 * smooth(-0.2, 0.3, h));
        return [oklchToRgb(0.34 + h * 0.1 - crater * 0.1, 0.035, hues[0]), 0, 0, glow];
    }
    if (kind === 'ice') {
        const h = fbm3(n.a, sx * 2.5, sy * 2.5, sz * 2.5, 6, 0.55), cracks = Math.abs(fbm3(n.b, sx * 6, sy * 6, sz * 6, 4));
        return [oklchToRgb(0.8 + h * 0.08 - (cracks < 0.04 ? 0.12 : 0), 0.035, hues[0]), 0.25, 0, 0];
    }
    if (kind === 'desert') {
        const h = fbm3(n.a, sx * 2, sy * 2, sz * 2, 6, 0.55);
        const dunes = Math.sin((sy + fbm3(n.b, sx * 3, sy * 3, sz * 3, 3) * 0.3) * 40) * 0.5 + 0.5;
        let rgb = oklchToRgb(0.58 + h * 0.18 + dunes * 0.04, 0.07, mix(hues[0], hues[1], clamp(0.5 + h)));
        const polar = Math.abs(sy) + fbm3(n.b, sx * 4, sy * 4, sz * 4, 3) * 0.15 > g.caps + 0.06;
        if (polar) rgb = oklchToRgb(0.88, 0.02, hues[0]);
        const haze = smooth(0.2, 0.5, fbm3(n.c, sx * 3 + 4, sy * 5, sz * 3, 5, 0.55)) * g.clouds;
        return [mixRgb(rgb, [236, 226, 210], haze), 0, h > 0.1 ? 1 : 0, 0];
    }

    const h = fbm3(n.a, sx * 1.8, sy * 1.8, sz * 1.8, 7, 0.55) - g.sea + 0.08;
    const land = h > 0.02;
    const polar = Math.abs(sy) + fbm3(n.b, sx * 4, sy * 4, sz * 4, 3) * 0.15 > g.caps;
    let rgb, spec = 0, city = 0;
    if (polar) rgb = oklchToRgb(0.9, 0.02, hues[0]);
    else if (land) {
        rgb = oklchToRgb(0.5 + h * 0.5, 0.06, avoidMud(hues[2] + h * 60));
        city = h < 0.25 ? 1 : 0;
    } else {
        rgb = oklchToRgb(0.42 + h * 0.35, 0.08, hues[0]);
        spec = 0.6;
    }
    const cloud = smooth(0.6 - g.clouds * 0.5, 0.82 - g.clouds * 0.5, fbm3(n.c, sx * 3 + 4, sy * 5, sz * 3, 5, 0.55) * 0.5 + 0.5);
    return [mixRgb(rgb, [240, 242, 246], cloud * 0.85), spec, city * (1 - cloud), 0];
}

const toLinear = c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
const toSrgb = c => 255 * (c <= 0.0031308 ? 12.92 * c : 1.055 * Math.pow(c, 1 / 2.4) - 0.055);
const luminance = rgb => 0.2126 * toLinear(rgb[0]) + 0.7152 * toLinear(rgb[1]) + 0.0722 * toLinear(rgb[2]);

// Dark worlds disappear against a dark sky. The surface's average luminance is
// sampled over the whole sphere, and worlds below the target are brightened to
// meet it. Brighter worlds keep their own albedo.
export const TARGET_ALBEDO = 0.2;
const EXPOSURE_SAMPLES = 256;

export function planetExposure(genome) {
    const n = { a: valueNoise3(genome.seed), b: valueNoise3(genome.seed * 3 + 7), c: valueNoise3(genome.seed * 11 + 1) };
    const golden = Math.PI * (3 - Math.sqrt(5));
    let total = 0;
    for (let i = 0; i < EXPOSURE_SAMPLES; i++) {
        const y = 1 - (i + 0.5) / EXPOSURE_SAMPLES * 2, r = Math.sqrt(1 - y * y), theta = golden * i;
        total += luminance(surface(genome, n, Math.cos(theta) * r, y, Math.sin(theta) * r)[0]);
    }
    const mean = total / EXPOSURE_SAMPLES;
    return mean >= TARGET_ALBEDO ? 1 : Math.min(5, TARGET_ALBEDO / Math.max(mean, 0.01));
}

function expose(rgb, gain) {
    if (gain === 1) return rgb;
    return rgb.map(c => toSrgb(Math.min(1, toLinear(Math.max(0, c)) * gain)));
}

// Renders the planet into a square RGBA buffer with a transparent background,
// centered, for a planet of the given radius in pixels. `lights` (0 to 1 or a
// little above) sets how bright the night side is. `light` is the direction the
// sunlight comes from.
export function renderPlanet(genome, radius, lights = 0.6, light = DEFAULT_LIGHT) {
    const g = genome, R = radius * g.size;
    const size = Math.max(4, Math.ceil(2 * radius * planetExtent(g)));
    const pixels = new Uint8ClampedArray(size * size * 4);
    const c0 = size / 2;
    const ll = Math.hypot(light[0], light[1], light[2]);
    const lx = light[0] / ll, ly = light[1] / ll, lz = light[2] / ll;
    const n = { a: valueNoise3(g.seed), b: valueNoise3(g.seed * 3 + 7), c: valueNoise3(g.seed * 11 + 1) };
    const atmo = oklchToRgb(0.78, 0.12, g.kind === 'terra' || g.kind === 'ocean' ? g.hues[0] : g.hues[1]);
    const cosSpin = Math.cos(g.spin), sinSpin = Math.sin(g.spin);
    const cosTilt = Math.cos(g.axialTilt), sinTilt = Math.sin(g.axialTilt);
    const ring = g.rings, ringRgb = ring ? oklchToRgb(0.8, 0.05, ring.hue) : null;
    const rcos = ring ? Math.cos(ring.tilt) : 1, rsin = ring ? Math.sin(ring.tilt) : 0;
    const lightsFrequency = 18 + R * 0.09;
    const gain = planetExposure(g);
    const atmosphere = Math.max(0.3, g.atmosphere);
    const moons = g.moons.map(m => ({
        ...m,
        x: Math.cos(m.angle) * m.distance * R,
        y: Math.sin(m.angle) * m.distance * R * 0.55,
        r: m.size * R,
        noise: valueNoise3(m.seed),
        rgb: oklchToRgb(0.62, 0.025, m.hue)
    }));

    for (let j = 0; j < size; j++) {
        for (let i = 0; i < size; i++) {
            const X = i + 0.5 - c0, Y = j + 0.5 - c0, dx = X / R, dy = Y / R, d2 = dx * dx + dy * dy;
            let r = 0, gg = 0, b = 0, a = 0;
            const over = (rgb, alpha) => {
                r = rgb[0] * alpha + r * (1 - alpha);
                gg = rgb[1] * alpha + gg * (1 - alpha);
                b = rgb[2] * alpha + b * (1 - alpha);
                a = alpha + a * (1 - alpha);
            };

            if (d2 > 1) {
                const d = Math.sqrt(d2), facing = -(dx * lx + dy * ly) / d;
                const glow = Math.exp(-(d - 1) * 14) * 0.55 * atmosphere * (0.2 + 0.8 * smooth(0.6, -0.6, -facing));
                if (glow > 0.002) over(atmo, glow);
            }

            let ringHit = null;
            if (ring) {
                const u = dx * rcos + dy * rsin, v = (-dx * rsin + dy * rcos) / ring.squash, d = Math.hypot(u, v);
                if (d > ring.inner && d < ring.outer) {
                    const t = (d - ring.inner) / (ring.outer - ring.inner), band = 0.5 + 0.5 * n.c(t * 40, 1.3, 2.1);
                    const alpha = smooth(0, 0.12, t) * (1 - smooth(0.8, 1, t)) * (0.15 + 0.55 * band) * (Math.abs(t - ring.gap) < 0.02 ? 0.15 : 1);
                    ringHit = { alpha, front: v > 0, rgb: ringRgb.map(c => c * (0.55 + 0.45 * band)) };
                    if (!ringHit.front) over(ringHit.rgb, ringHit.alpha);
                }
            }

            if (d2 <= 1) {
                const nz = Math.sqrt(1 - d2), nx = dx, ny = dy;
                const tx = nx * cosTilt - ny * sinTilt, ty = nx * sinTilt + ny * cosTilt;
                const sx = tx * cosSpin + nz * sinSpin, sz = -tx * sinSpin + nz * cosSpin, sy = ty;
                const [raw, spec, city, emissive] = surface(g, n, sx, sy, sz);
                const base = expose(raw, gain);
                const ndl = nx * lx + ny * ly + nz * lz, lit = smooth(-0.12, 0.35, ndl), shade = 0.04 + lit * 0.96;
                let rgb = [base[0] * shade, base[1] * shade, base[2] * shade];
                if (spec) {
                    const hx = lx, hy = ly, hz = lz + 1, hl = Math.hypot(hx, hy, hz);
                    const s = Math.pow(Math.max(0, (nx * hx + ny * hy + nz * hz) / hl), 40) * spec * lit * 255;
                    rgb = [rgb[0] + s, rgb[1] + s, rgb[2] + s];
                }
                if (city && lights > 0 && ndl < 0.05) {
                    const region = fbm3(n.b, sx * 5, sy * 5, sz * 5, 3), c = fbm3(n.c, sx * lightsFrequency, sy * lightsFrequency, sz * lightsFrequency, 2);
                    if (region > -0.05 && c > 0.3) {
                        const glow = Math.min(1, (c - 0.3) * 4.5) * smooth(-0.05, 0.3, region) * lights * 0.8 * smooth(0.05, -0.2, ndl);
                        rgb = [rgb[0] + 255 * glow, rgb[1] + 196 * glow, rgb[2] + 118 * glow];
                    }
                }
                if (emissive) {
                    const e = emissive * (0.35 + 0.65 * (1 - lit));
                    rgb = [rgb[0] + 255 * e, rgb[1] + 120 * e, rgb[2] + 40 * e];
                }
                const fresnel = Math.pow(1 - nz, 2.6) * atmosphere, rim = fresnel * (0.15 + 0.85 * smooth(-0.35, 0.4, ndl));
                rgb = mixRgb(rgb, atmo, rim);
                over(rgb, smooth(1, 0.985, Math.sqrt(d2)));
            }

            if (ringHit && ringHit.front) over(ringHit.rgb, ringHit.alpha);

            for (const m of moons) {
                const mx = (X - m.x) / m.r, my = (Y - m.y) / m.r, md2 = mx * mx + my * my;
                if (md2 > 1) continue;
                const mz = Math.sqrt(1 - md2), ndl = mx * lx + my * ly + mz * lz;
                const h = fbm3(m.noise, mx * 3, my * 3, mz * 3, 4);
                const shade = 0.04 + smooth(-0.1, 0.35, ndl) * 0.96 * (0.85 + h * 0.3);
                over(m.rgb.map(c => c * shade), smooth(1, 0.94, Math.sqrt(md2)));
            }

            const k = (j * size + i) * 4;
            if (a > 0) {
                pixels[k] = r / a;
                pixels[k + 1] = gg / a;
                pixels[k + 2] = b / a;
                pixels[k + 3] = a * 255;
            }
        }
    }
    return { pixels, size };
}
