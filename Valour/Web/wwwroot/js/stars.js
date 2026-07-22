const starsContainer = document.getElementById("stars");

// Function to create stars
function createStars(count) {
    for (let i = 0; i < count; i++) {
        const star = document.createElement("div");
        star.className = "star";

        // Random position and size
        const size = Math.random() * 4 + 2; // Star size between 2px and 6px
        star.style.width = `${size}px`;
        star.style.height = `${size}px`;
        star.style.top = `${Math.random() * 200}%`;
        star.style.left = `${Math.random() * 200}%`;
        star.style.animationDelay = `${Math.random() * 5}s`; // Different twinkling timings

        starsContainer.appendChild(star);
    }
}

// Create 150 stars
createStars(150);

let scrollMod = 0;

let mouseModX = 0;
let mouseModY = 0;

// Parallax effect for mouse movement
document.addEventListener("mousemove", (e) => {
    const { clientX, clientY } = e;
    const offsetX = (clientX / window.innerWidth) * 20; // Adjust multiplier for intensity
    const offsetY = (clientY / window.innerHeight) * 20;
    
    mouseModX = -offsetX;
    mouseModY = -offsetY;

    starsContainer.style.transform = `translate(${mouseModX}px, ${mouseModY + scrollMod}px)`;
});

// Parallax effect for scrolling
window.addEventListener("scroll", () => {
    const scrollY = window.scrollY; // How far the page has scrolled
    const parallaxAmount = scrollY * 0.3; // Adjust multiplier for intensity
    scrollMod = -parallaxAmount;
    
    starsContainer.style.transform = `translate(${mouseModX}px, ${mouseModY + scrollMod}px)`; // Move stars upward
});
