// Set the target date and time
const targetDate = new Date("2024-12-13T23:00:00Z");

// Function to update the countdown
function updateCountdown() {
    const now = new Date();
    const diff = targetDate - now;

    if (diff <= 0) {
        // Countdown has ended; set all values to 00 and stop the timer
        document.querySelector(".days").textContent = "00";
        document.querySelector(".hours").textContent = "00";
        document.querySelector(".minutes").textContent = "00";
        document.querySelector(".seconds").textContent = "00";
        clearInterval(timer);
        return;
    }

    // Calculate time components
    const days = Math.floor(diff / (1000 * 60 * 60 * 24));
    const hours = Math.floor((diff % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
    const minutes = Math.floor((diff % (1000 * 60 * 60)) / (1000 * 60));
    const seconds = Math.floor((diff % (1000 * 60)) / 1000);

    // Update the HTML elements
    document.querySelector(".days").textContent = String(days).padStart(2, "0");
    document.querySelector(".hours").textContent = String(hours).padStart(2, "0");
    document.querySelector(".minutes").textContent = String(minutes).padStart(2, "0");
    document.querySelector(".seconds").textContent = String(seconds).padStart(2, "0");
}

// Start the countdown
const timer = setInterval(updateCountdown, 1000);

// Initial call to display the countdown immediately
updateCountdown();
