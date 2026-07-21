const pickers = {};
let pickrLoadPromise = null;

function ensurePickrLoaded() {
    if (globalThis.Pickr?.create) return Promise.resolve();
    pickrLoadPromise ??= new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = 'https://cdn.jsdelivr.net/npm/@simonwep/pickr/dist/pickr.min.js';
        script.onload = resolve;
        script.onerror = () => reject(new Error('Failed to load Pickr'));
        document.head.appendChild(script);
    });
    return pickrLoadPromise;
}

export async function init(id, ref, startColor, button = false) {
    await ensurePickrLoaded();
    const pickr = Pickr.create({
        el: '#' + id,
        theme: 'nano', // or 'monolith', or 'nano'
        comparison: false,
        default: startColor,
        useAsButton: button,
        
        swatches: [
            "rgb(255, 105, 97)",   // Pastel Red
            "rgb(255, 179, 186)",  // Pastel Pink
            "rgb(255, 174, 201)",  // Light Pink
            "rgb(209, 159, 232)",  // Lavender
            "rgb(170, 152, 169)",  // Pastel Purple
            "rgb(157, 197, 255)",  // Baby Blue
            "rgb(159, 226, 191)",  // Mint Green
            "rgb(172, 225, 175)",  // Pistachio
            "rgb(178, 236, 93)",   // Light Lime
            "rgb(243, 253, 149)",  // Pastel Yellow
            "rgb(255, 248, 175)",  // Cream
            "rgb(255, 209, 148)",  // Peach
            "rgb(255, 158, 127)",  // Melon
            "rgb(255, 107, 107)"   // Salmon Pink
        ],

        components: {

            // Main components
            preview: true,
            opacity: false,
            hue: true,

            // Input / output Options
            interaction: {
                hex: true,
                rgba: false,
                hsla: false,
                hsva: false,
                cmyk: false,
                input: true,
                clear: false,
                save: false
            }
        }
    });
    
    pickers[id] = pickr;
    
    pickr.on('changestop', (source, instance) => {
        ref.invokeMethodAsync('ColorChange', instance.getColor().toHEXA().toString());
    });
    
    pickr.on('swatchselect', (color, instance) => {
        ref.invokeMethodAsync('ColorChange', color.toHEXA().toString());
    });
}

export function destroy(id) {
    pickers[id].destroyAndRemove();
}
