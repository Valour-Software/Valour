// Opens and closes the navigation menu on small screens.
document.addEventListener('DOMContentLoaded', function () {
    const toggle = document.querySelector('[data-menu-toggle]');
    const header = document.querySelector('.site-header');
    if (!toggle || !header) return;

    function setOpen(open) {
        header.classList.toggle('menu-open', open);
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        const icon = toggle.querySelector('.bi');
        if (icon) {
            icon.classList.toggle('bi-list', !open);
            icon.classList.toggle('bi-x-lg', open);
        }
    }

    toggle.addEventListener('click', function () {
        setOpen(!header.classList.contains('menu-open'));
    });

    header.querySelectorAll('.site-nav a').forEach(function (link) {
        link.addEventListener('click', function () {
            setOpen(false);
        });
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') setOpen(false);
    });
});
