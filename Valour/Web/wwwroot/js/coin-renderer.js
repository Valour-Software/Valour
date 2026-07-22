const container = document.getElementById('coin-container');

// Set up the renderer with transparency and high resolution
const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
renderer.setPixelRatio(window.devicePixelRatio);
const size = Math.min(window.innerWidth * 0.7, 400);
renderer.setSize(size, size);
container.appendChild(renderer.domElement);

// Create the scene
const scene = new THREE.Scene();

// Set up an Orthographic Camera for a fixed-size view
const width = 200;
const height = 200;
const camera = new THREE.OrthographicCamera(
    width / -2,    // left
    width / 2,     // right
    height / 2,    // top
    height / -2,   // bottom
    -1000,         // near
    1000           // far
);
camera.position.z = 100; // Position the camera in front of the coin

// Create the coin geometry with more segments for smoother edges
const geometry = new THREE.CylinderGeometry(80, 80, 10, 128);
const loader = new THREE.TextureLoader();

// Load textures and normal maps
const frontTexture = loader.load('/media/valour-credit.png');
const backTexture = loader.load('/media/valour-credit.png'); // Using the same texture for back
const normalMap = loader.load('/media/valour-credit-norm.png');

// Create materials with normal maps
const materialFront = new THREE.MeshStandardMaterial({
    map: frontTexture,
    normalMap: normalMap,
    roughness: 0.7,
    metalness: 0.7,
});
const materialBack = new THREE.MeshStandardMaterial({
    map: backTexture,
    normalMap: normalMap,
    roughness: 0.7,
    metalness: 0.7,
});
const materialEdge = new THREE.MeshStandardMaterial({
    color: 0xb5ae4e,
    roughness: 0.7,
    metalness: 0.7,
});

const materials = [materialEdge, materialFront, materialBack];
const coin = new THREE.Mesh(geometry, materials);

// Adjust initial rotation of the coin
coin.rotation.x = Math.PI / 2; // Rotate to face camera
// Remove coin.rotation.y = Math.PI / 2;

scene.add(coin);

// Increase the lighting intensity
const ambientLight = new THREE.AmbientLight(0xffffff, 0.5); // Increased from 0.3
scene.add(ambientLight);

const pointLight = new THREE.PointLight(0xffffff, 1.2); // Increased from 0.5
pointLight.position.set(0, 0, 200);
scene.add(pointLight);

// Variables to store target rotation
let targetRotationX = Math.PI / 2;
let targetRotationZ = 0; // Initialize Z rotation

// Variables for spinning
let isSpinning = false;
let spinStartTime = 0;
let spinDuration = 2000; // Spin duration in milliseconds
let spinStartRotationY = 0;
let spinTargetRotationY = 0;

// Define raycaster and mouse
const raycaster = new THREE.Raycaster();
const mouse = new THREE.Vector2();

// Mouse interactivity
const animateCoin = (event) => {
    if (isSpinning) return; // Ignore mouse movement during spin

    const x = (event.clientX / window.innerWidth - 0.5) * -2; // Map to range [-1, 1]
    const y = (event.clientY / window.innerHeight - 0.5) * 2; // Map to range [-1, 1]

    targetRotationX = y * 0.7 + Math.PI / 2;
    targetRotationZ = x * 0.7;
};

// Add mousemove listener to the whole window
window.addEventListener('mousemove', animateCoin);

// Click event listener
container.addEventListener('mousedown', onDocumentMouseDown, false);

function onDocumentMouseDown(event) {
    event.preventDefault();

    const rect = renderer.domElement.getBoundingClientRect();

    mouse.x = ((event.clientX - rect.left) / rect.width) * 2 -1;
    mouse.y = -((event.clientY - rect.top) / rect.height) * 2 +1;

    raycaster.setFromCamera(mouse, camera);

    const intersects = raycaster.intersectObject(coin);

    if (intersects.length > 0) {
        // Coin was clicked
        startSpin();
    }
}

function startSpin() {
    if (isSpinning) return; // Prevent multiple spins at the same time

    isSpinning = true;
    spinStartTime = performance.now();
    spinDuration = 2000; // 2 seconds spin duration
    spinStartRotationY = coin.rotation.y;
    const fullRotations = 3; // Number of full spins
    spinTargetRotationY = spinStartRotationY + fullRotations * 2 * Math.PI;
}

// Animate the scene
function animateCore() {
    requestAnimationFrame(animateCore);

    const now = performance.now();

    if (isSpinning) {
        const elapsed = now - spinStartTime;
        const progress = Math.min(elapsed / spinDuration, 1); // Clamp between 0 and 1

        // Easing function (easeOutQuad)
        const easedProgress = 1 - (1 - progress) * (1 - progress);

        coin.rotation.z = spinStartRotationY + (spinTargetRotationY - spinStartRotationY) * easedProgress;

        if (progress >= 1) {
            isSpinning = false;
        }
    } else {
        // Smoothly interpolate rotation
        coin.rotation.x += (targetRotationX - coin.rotation.x) * 0.1;
        coin.rotation.z += (targetRotationZ - coin.rotation.z) * 0.1;
    }

    renderer.render(scene, camera);
}
animateCore();

// Ensure the canvas resizes if needed
window.addEventListener('resize', () => {
    // Width and height are either 400 or 70% of the window size, whichever is smaller
    const size = Math.min(window.innerWidth * 0.7, 400);

    renderer.setSize(size, size);
    renderer.setPixelRatio(window.devicePixelRatio);
});