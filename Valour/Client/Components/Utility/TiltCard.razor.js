/* Huge thanks to https://codepen.io/markmiro/pen/wbqMPa */

export function init(id, margin= '0'){
    const card = document.getElementById(id);
    let bounds;
    
    // Used for performance
    let i = 0;
    
    const rotateHandler = function(e) {
        
        i++;
        
        // Only do update every 5 events. It's animated and will be smoothed anyways.
        // This is a huge performance boost.
        if (i % 5 !== 0) return;
        
        const mouseX = e.clientX;
        const mouseY = e.clientY;
        const leftX = mouseX - bounds.x;
        const topY = mouseY - bounds.y;
        const center = {
            x: leftX - bounds.width / 2,
            y: topY - bounds.height / 2
        }
        const distance = Math.sqrt(center.x**2 + center.y**2);

        card.style.transform = `
            scale3d(1.07, 1.07, 1.07)
            rotate3d(
              ${center.y / 100},
              ${-center.x / 100},
              0,
              ${Math.log(distance)* 2}deg
            )
          `;
        card.querySelector('.shine').style.backgroundImage = `
            radial-gradient(
              circle at
              ${center.x * 2 + bounds.width/3}px
              ${center.y * 2 + bounds.height/3}px,
              #ffffff22,
              #0000000f
            )
          `;
    }

    bounds = card.getBoundingClientRect();
    card.style.margin = margin;
    card.addEventListener('mousemove', rotateHandler);
    
    card.addEventListener('mouseenter', () => {
        bounds = card.getBoundingClientRect();
        card.style.margin = margin;
        document.addEventListener('mousemove', rotateHandler);
    });

    card.addEventListener('mouseleave', () => {
        document.removeEventListener('mousemove', rotateHandler);
        card.style.margin = '';
        card.style.transform = '';
        card.style.background = '';
    });
}