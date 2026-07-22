// Wait for DOM to load
document.addEventListener('DOMContentLoaded', function() {
    // Add loaded class to body for animations
    setTimeout(function() {
        document.body.classList.add('loaded');
    }, 100);

    // Smooth scroll for anchor links
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function(e) {
            e.preventDefault();

            const targetId = this.getAttribute('href');
            if (targetId === '#') return;

            const targetElement = document.querySelector(targetId);
            if (targetElement) {
                window.scrollTo({
                    top: targetElement.offsetTop - 100,
                    behavior: 'smooth'
                });
            }
        });
    });

    // Scroll-based animations
    const observerOptions = {
        threshold: 0.1,
        rootMargin: '0px 0px -100px 0px'
    };

    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('in-view');
                observer.unobserve(entry.target);
            }
        });
    }, observerOptions);

    // Observe all elements with data-aos attribute
    document.querySelectorAll('[data-aos]').forEach(element => {
        observer.observe(element);
    });

    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        document.querySelectorAll('video[autoplay]').forEach(video => {
            video.removeAttribute('autoplay');
            video.pause();
        });
    }

    // Parallax effect for hero section
    window.addEventListener('scroll', function() {
        const scrollPosition = window.scrollY;
        const heroSection = document.querySelector('.hero-section');

        if (heroSection && scrollPosition < window.innerHeight) {
            const parallaxElements = heroSection.querySelectorAll('.hero-content, .hero-image');
            parallaxElements.forEach(element => {
                const speed = element.classList.contains('hero-content') ? 0.15 : 0.1;
                element.style.transform = `translateY(${scrollPosition * speed}px)`;
            });
        }
    });

    // Mobile menu toggle
    const menuToggle = document.querySelector('.navbar-toggler');
    if (menuToggle) {
        menuToggle.addEventListener('click', function() {
            document.body.classList.toggle('menu-open');
        });
    }
});
