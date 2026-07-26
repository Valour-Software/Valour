// Abstract "planet of planets" for the staff dashboard.
// Every dot is a Valour community placed deterministically from its id, so the
// sphere is stable across sessions without encoding any real-world geography.

const TAU = Math.PI * 2;
const CONTINENT_COUNT = 6;
const CONTINENT_SEED = 0x56414c; // fixed so the world never reshuffles
const ISLAND_RATIO = 0.16;       // share of communities scattered as islands
const PULSE_MS = 1900;
const ROTATE_SPEED = 0.045;      // radians / second of idle spin

// --- deterministic randomness -------------------------------------------------

function mulberry32(seed) {
    let a = seed >>> 0;
    return function () {
        a |= 0; a = (a + 0x6D2B79F5) | 0;
        let t = Math.imul(a ^ (a >>> 15), 1 | a);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

// FNV-1a over the id string; ids arrive as strings because snowflakes
// overflow JS number precision.
function hashId(id) {
    let h = 0x811c9dc5;
    for (let i = 0; i < id.length; i++) {
        h ^= id.charCodeAt(i);
        h = Math.imul(h, 0x01000193);
    }
    return h >>> 0;
}

function randomUnitVector(rand) {
    const z = rand() * 2 - 1;
    const a = rand() * TAU;
    const r = Math.sqrt(Math.max(0, 1 - z * z));
    return [r * Math.cos(a), r * Math.sin(a), z];
}

function normalize(v) {
    const l = Math.hypot(v[0], v[1], v[2]) || 1;
    return [v[0] / l, v[1] / l, v[2] / l];
}

// Continent centers derived once from the fixed seed.
function buildContinents() {
    const rand = mulberry32(CONTINENT_SEED);
    const centers = [];
    for (let i = 0; i < CONTINENT_COUNT; i++)
        centers.push(randomUnitVector(rand));
    return centers;
}

// Place a community: most cluster around a continent center with a
// gaussian-ish spread, the rest scatter as islands.
function placeDot(id, continents) {
    const h = hashId(id);
    const rand = mulberry32(h);

    if (rand() < ISLAND_RATIO)
        return randomUnitVector(rand);

    const center = continents[h % CONTINENT_COUNT];

    // Box-Muller for a soft cluster falloff
    const u1 = Math.max(rand(), 1e-6);
    const u2 = rand();
    const dist = Math.abs(Math.sqrt(-2 * Math.log(u1)) * Math.cos(TAU * u2)) * 0.34;

    // Random tangent direction away from the center
    const ref = Math.abs(center[2]) < 0.9 ? [0, 0, 1] : [1, 0, 0];
    let t1 = normalize([
        center[1] * ref[2] - center[2] * ref[1],
        center[2] * ref[0] - center[0] * ref[2],
        center[0] * ref[1] - center[1] * ref[0],
    ]);
    const t2 = [
        center[1] * t1[2] - center[2] * t1[1],
        center[2] * t1[0] - center[0] * t1[2],
        center[0] * t1[1] - center[1] * t1[0],
    ];
    const ang = rand() * TAU;
    const dx = Math.cos(ang), dy = Math.sin(ang);
    const s = Math.sin(dist), c = Math.cos(dist);

    return normalize([
        center[0] * c + (t1[0] * dx + t2[0] * dy) * s,
        center[1] * c + (t1[1] * dx + t2[1] * dy) * s,
        center[2] * c + (t1[2] * dx + t2[2] * dy) * s,
    ]);
}

function formatMembers(m) {
    if (m >= 1000000) return (m / 1000000).toFixed(1) + "M members";
    if (m >= 10000) return Math.round(m / 1000) + "K members";
    if (m >= 1000) return (m / 1000).toFixed(1) + "K members";
    return m + (m === 1 ? " member" : " members");
}

class Globe {
    constructor(container, canvas) {
        this.container = container;
        this.canvas = canvas;
        this.ctx = canvas.getContext("2d");
        this.tooltip = container.querySelector(".globe-tooltip");
        this.continents = buildContinents();

        this.dots = [];
        this.pulses = [];
        this.rotY = 0;
        this.tilt = -0.42;
        this.dragging = false;
        this.hover = null;
        this.lastFrame = performance.now();
        this.disposed = false;

        this.resize = this.resize.bind(this);
        this.frame = this.frame.bind(this);
        this.onPointerDown = this.onPointerDown.bind(this);
        this.onPointerMove = this.onPointerMove.bind(this);
        this.onPointerUp = this.onPointerUp.bind(this);

        this.observer = new ResizeObserver(this.resize);
        this.observer.observe(container);
        this.resize();

        canvas.addEventListener("pointerdown", this.onPointerDown);
        canvas.addEventListener("pointermove", this.onPointerMove);
        canvas.addEventListener("pointerup", this.onPointerUp);
        canvas.addEventListener("pointerleave", this.onPointerUp);

        this.raf = requestAnimationFrame(this.frame);
    }

    resize() {
        const rect = this.container.getBoundingClientRect();
        const dpr = window.devicePixelRatio || 1;
        this.width = Math.max(1, rect.width);
        this.height = Math.max(1, rect.height);
        this.canvas.width = Math.round(this.width * dpr);
        this.canvas.height = Math.round(this.height * dpr);
        this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    setPlanets(planets) {
        const maxMembers = planets.reduce((m, p) => Math.max(m, p.members), 1);
        this.dots = planets.map(p => ({
            id: p.id,
            name: p.name || "Community",
            members: p.members,
            online: p.online || 0,
            pos: placeDot(p.id, this.continents),
            r: 1.4 + Math.sqrt(Math.max(p.members, 1) / maxMembers) * 5.6,
        }));
    }

    pulse(planetId, online) {
        let pos = null;
        if (planetId) {
            const dot = this.dots.find(d => d.id === planetId);
            if (dot) pos = dot.pos;
        }
        if (!pos) {
            // No community to anchor to: land anywhere on the sphere
            pos = randomUnitVector(mulberry32((Math.random() * 0xffffffff) >>> 0));
        }
        this.pulses.push({ pos, online: !!online, start: performance.now() });
        if (this.pulses.length > 80)
            this.pulses.splice(0, this.pulses.length - 80);
    }

    // Rotate a unit vector by the current view and project to screen space.
    project(v, cx, cy, radius) {
        const cy1 = Math.cos(this.rotY), sy1 = Math.sin(this.rotY);
        const cx1 = Math.cos(this.tilt), sx1 = Math.sin(this.tilt);

        const x1 = v[0] * cy1 + v[2] * sy1;
        const z1 = -v[0] * sy1 + v[2] * cy1;
        const y2 = v[1] * cx1 - z1 * sx1;
        const z2 = v[1] * sx1 + z1 * cx1;

        return {
            x: cx + x1 * radius,
            y: cy - y2 * radius,
            z: z2, // depth toward the viewer
        };
    }

    onPointerDown(e) {
        this.dragging = true;
        this.dragX = e.clientX;
        this.dragY = e.clientY;
        this.canvas.setPointerCapture(e.pointerId);
        this.hideTooltip();
    }

    onPointerMove(e) {
        if (this.dragging) {
            this.rotY += (e.clientX - this.dragX) * 0.005;
            this.tilt += (e.clientY - this.dragY) * 0.005;
            this.tilt = Math.max(-1.25, Math.min(1.25, this.tilt));
            this.dragX = e.clientX;
            this.dragY = e.clientY;
            return;
        }

        const rect = this.canvas.getBoundingClientRect();
        this.pointerX = e.clientX - rect.left;
        this.pointerY = e.clientY - rect.top;
    }

    onPointerUp(e) {
        this.dragging = false;
        if (e.type === "pointerleave") {
            this.pointerX = undefined;
            this.hideTooltip();
        }
    }

    hideTooltip() {
        if (this.tooltip) this.tooltip.style.display = "none";
        this.hover = null;
    }

    showTooltip(dot, x, y) {
        if (!this.tooltip) return;

        // Community names are user content: textContent only, never innerHTML.
        // The line elements are Blazor-rendered so scoped CSS reaches them.
        const name = this.tooltip.querySelector(".tt-name");
        const members = this.tooltip.querySelector(".tt-members");
        const online = this.tooltip.querySelector(".tt-online");
        if (name) name.textContent = dot.name;
        if (members) members.textContent = formatMembers(dot.members);
        if (online) {
            online.textContent = dot.online + " online";
            online.classList.toggle("zero", dot.online === 0);
        }

        this.tooltip.style.display = "flex";
        const flip = x > this.width - 230;
        this.tooltip.style.left = flip ? "" : (x + 14) + "px";
        this.tooltip.style.right = flip ? (this.width - x + 14) + "px" : "";
        this.tooltip.style.top = Math.max(4, y - 30) + "px";
    }

    frame(now) {
        if (this.disposed) return;

        const dt = Math.min(0.1, (now - this.lastFrame) / 1000);
        this.lastFrame = now;
        if (!this.dragging)
            this.rotY += ROTATE_SPEED * dt;

        const ctx = this.ctx;
        const w = this.width, h = this.height;
        ctx.clearRect(0, 0, w, h);

        const cx = w / 2;
        const cy = h / 2;
        const radius = Math.max(40, Math.min(w, h) / 2 - 24);

        // Sphere body: a soft top-lit gradient plus a faint cool rim
        const body = ctx.createRadialGradient(
            cx - radius * 0.35, cy - radius * 0.45, radius * 0.1,
            cx, cy, radius);
        body.addColorStop(0, "rgba(23, 51, 74, 0.95)");
        body.addColorStop(0.65, "rgba(12, 30, 46, 0.95)");
        body.addColorStop(1, "rgba(6, 16, 26, 0.98)");
        ctx.beginPath();
        ctx.arc(cx, cy, radius, 0, TAU);
        ctx.fillStyle = body;
        ctx.fill();

        // Graticule: a few sampled latitude/longitude polylines, barely there
        ctx.lineWidth = 1;
        ctx.strokeStyle = "rgba(120, 190, 235, 0.07)";
        for (let lat = -60; lat <= 60; lat += 30)
            this.drawCircle(ctx, cx, cy, radius, lat, null);
        for (let lon = 0; lon < 180; lon += 45)
            this.drawCircle(ctx, cx, cy, radius, null, lon);

        // Dots: back hemisphere first as faint hints, then the front
        const projected = [];
        for (const dot of this.dots) {
            const p = this.project(dot.pos, cx, cy, radius);
            projected.push({ dot, p });
            if (p.z <= 0) {
                ctx.beginPath();
                ctx.arc(p.x, p.y, Math.max(0.8, dot.r * 0.7), 0, TAU);
                ctx.fillStyle = "rgba(90, 140, 185, 0.10)";
                ctx.fill();
            }
        }

        let nearest = null;
        for (const item of projected) {
            const { dot, p } = item;
            if (p.z <= 0) continue;

            const glow = 0.45 + p.z * 0.55;
            ctx.beginPath();
            ctx.arc(p.x, p.y, dot.r, 0, TAU);
            ctx.fillStyle = `rgba(115, 180, 240, ${(0.35 + 0.5 * glow).toFixed(3)})`;
            ctx.fill();

            // A brighter core keeps large hubs from reading as flat blobs
            if (dot.r > 3) {
                ctx.beginPath();
                ctx.arc(p.x, p.y, dot.r * 0.45, 0, TAU);
                ctx.fillStyle = `rgba(190, 226, 255, ${(0.5 * glow + 0.25).toFixed(3)})`;
                ctx.fill();
            }

            if (this.pointerX !== undefined && !this.dragging) {
                const dx = p.x - this.pointerX;
                const dy = p.y - this.pointerY;
                const dist = Math.hypot(dx, dy);
                if (dist < Math.max(14, dot.r + 6) && (!nearest || dist < nearest.dist))
                    nearest = { dot, p, dist };
            }
        }

        // Hover ring + tooltip
        if (nearest) {
            ctx.beginPath();
            ctx.arc(nearest.p.x, nearest.p.y, nearest.dot.r + 3, 0, TAU);
            ctx.lineWidth = 1.5;
            ctx.strokeStyle = "rgba(220, 240, 255, 0.9)";
            ctx.stroke();
            if (this.hover !== nearest.dot) {
                this.hover = nearest.dot;
            }
            this.showTooltip(nearest.dot, nearest.p.x, nearest.p.y);
        } else if (this.hover) {
            this.hideTooltip();
        }

        // Presence pulses: expanding rings anchored to community dots
        const alive = [];
        for (const pulse of this.pulses) {
            const t = (now - pulse.start) / PULSE_MS;
            if (t >= 1) continue;
            alive.push(pulse);

            const p = this.project(pulse.pos, cx, cy, radius);
            if (p.z <= -0.15) continue; // fully behind the sphere

            const eased = 1 - Math.pow(1 - t, 2);
            const ringR = 3 + eased * 26;
            const alpha = (1 - t) * (p.z > 0 ? 0.85 : 0.25);
            ctx.beginPath();
            ctx.arc(p.x, p.y, ringR, 0, TAU);
            ctx.lineWidth = 2;
            ctx.strokeStyle = pulse.online
                ? `rgba(88, 196, 139, ${alpha.toFixed(3)})`
                : `rgba(205, 94, 94, ${alpha.toFixed(3)})`;
            ctx.stroke();

            if (t < 0.35 && p.z > 0) {
                ctx.beginPath();
                ctx.arc(p.x, p.y, 3, 0, TAU);
                ctx.fillStyle = pulse.online
                    ? "rgba(150, 230, 180, 0.95)"
                    : "rgba(235, 150, 150, 0.95)";
                ctx.fill();
            }
        }
        this.pulses = alive;

        // Rim light on top of everything so dots feel inside an atmosphere
        ctx.beginPath();
        ctx.arc(cx, cy, radius, 0, TAU);
        ctx.lineWidth = 1.5;
        ctx.strokeStyle = "rgba(120, 200, 255, 0.16)";
        ctx.stroke();

        this.raf = requestAnimationFrame(this.frame);
    }

    // Draws one graticule circle (fixed latitude when lat given, otherwise a
    // meridian at the given longitude), sampled in 3D so it rotates correctly.
    drawCircle(ctx, cx, cy, radius, lat, lon) {
        ctx.beginPath();
        let started = false;
        for (let i = 0; i <= 72; i++) {
            const a = (i / 72) * TAU;
            let v;
            if (lat !== null) {
                const phi = (lat * Math.PI) / 180;
                v = [Math.cos(phi) * Math.cos(a), Math.sin(phi), Math.cos(phi) * Math.sin(a)];
            } else {
                const theta = (lon * Math.PI) / 180;
                v = [Math.cos(a) * Math.cos(theta), Math.sin(a), Math.cos(a) * Math.sin(theta)];
            }
            const p = this.project(v, cx, cy, radius);
            if (p.z <= 0) { started = false; continue; }
            if (!started) { ctx.moveTo(p.x, p.y); started = true; }
            else ctx.lineTo(p.x, p.y);
        }
        ctx.stroke();
    }

    dispose() {
        this.disposed = true;
        cancelAnimationFrame(this.raf);
        this.observer.disconnect();
        this.canvas.removeEventListener("pointerdown", this.onPointerDown);
        this.canvas.removeEventListener("pointermove", this.onPointerMove);
        this.canvas.removeEventListener("pointerup", this.onPointerUp);
        this.canvas.removeEventListener("pointerleave", this.onPointerUp);
    }
}

export function init(container, canvas) {
    return new Globe(container, canvas);
}
