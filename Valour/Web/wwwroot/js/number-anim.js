// Element to update
const numberElement = document.getElementById("member-count");

function DoCountCheck() {
    fetch('https://app.valour.gg/api/users/count')
        .then(response => response.json())
        .then(OnGetCount);
}

let init = false;

const observerCallback = (entries, observer) => {
    
    if (init) {
        return;
    }
    
    entries.forEach(entry => {
        if (entry.isIntersecting) {
            init = true;
            DoCountCheck();

            setInterval(() => {
                DoCountCheck();
            }, 10000);

            // Stop observing once animation starts
            observer.unobserve(entry.target);
        }
    });
}

// Create Intersection Observer
const observer = new IntersectionObserver(observerCallback, {
    threshold: 0.5, // Trigger when 50% of the element is visible
});

// Observe the target element
observer.observe(numberElement);

let lastValue = 0;

// Final value
let finalValue = 0;

// Duration of animation in milliseconds
const duration = 3000;

function OnGetCount(newCount) {
    finalValue = newCount;
    doAnimate();
    lastValue = finalValue;
}

// Custom easing function: starts fast, slows significantly at the end
const veryAggressiveEaseOut = "cubic-bezier(0.99, 0.01, 0.6, 1)";

function doAnimate() {
    // Animate the number
    animate(
        lastValue,               // Start value
        finalValue,              // End value
        {
            duration: duration / 1000,  // Duration in seconds
            onUpdate: (value) => {
                numberElement.textContent = Math.floor(value); // Update displayed number
            },
            easing: veryAggressiveEaseOut, // Optional easing
        }
    );
}