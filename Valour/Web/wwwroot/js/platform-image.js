console.log("Setting up motions...");

const { animate, scroll } = Motion;

// if mobile, return
if (document.documentElement.clientWidth > 1024) {
    
    const items = document.querySelectorAll("li");
    
    // Animate gallery horizontally during vertical scroll
    scroll(
        animate(
            "ul",
            {
                transform: ["none", `translateX(-${items.length - 1}00vw)`],
            },
            { ease: "linear" }
        ),
        { target: document.querySelector("section") }
    );
    
    // Progress bar representing gallery scroll
    scroll(animate(".progress", { scaleX: [0, 1] }, { ease: "linear" }), {
        target: document.querySelector("section"),
    });
    
    // Image title parallax
    const segmentLength = 1 / items.length;
    items.forEach((item, i) => {
        const header = item.querySelector("h3");
    
        scroll(animate(header, { x: [200, -200] }, { ease: "linear" }), {
            target: document.querySelector("section"),
            offset: [
                [i * segmentLength, 1],
                [(i + 1) * segmentLength, 0],
            ],
        });
    });

}