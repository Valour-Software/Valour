const disposers = new Set();

export function init(ref, id) {
    const sidebar = document.getElementById(id);
    if (!sidebar) return null;
    let open = false;
    let gesture = null;
    let disposed = false;

    const setSidebarOpen = (value) => {
        if (disposed) return;
        const wasOpen = open;
        open = value === true;
        sidebar.style.transition = '';
        sidebar.style.transform = open ? 'translateX(0px)' : '';
        sidebar.dataset.mobileOpen = String(open);
        document.querySelector('.sidebar-toggle')?.setAttribute('aria-expanded', String(open));
        if (open && !wasOpen) {
            ref.invokeMethodAsync('OnMobileSidebarOpened').catch(() => {});
        }
    };
    const toggleOpen = () => setSidebarOpen(!open);
    const cancelGesture = () => {
        gesture = null;
        setSidebarOpen(open);
    };
    const onTouchStart = (event) => {
        if (gesture) cancelGesture();
        if (event.touches.length !== 1 || event.target.closest?.('canvas, input, textarea, select, button, [role="button"]')) return;
        const touch = event.touches[0];
        const width = sidebar.getBoundingClientRect().width;
        if ((!open && touch.clientX > 24) || (open && touch.clientX < width - 80)) return;
        gesture = { id: touch.identifier, x: touch.clientX, y: touch.clientY, width, offset: open ? 0 : -width, dragging: false };
    };
    const onTouchMove = (event) => {
        if (!gesture) return;
        if (event.touches.length !== 1) return cancelGesture();
        const touch = [...event.touches].find(item => item.identifier === gesture.id);
        if (!touch) return cancelGesture();
        const dx = touch.clientX - gesture.x, dy = touch.clientY - gesture.y;
        if (!gesture.dragging) {
            if (Math.abs(dy) > Math.abs(dx)) return cancelGesture();
            if (Math.abs(dx) < 8) return;
            gesture.dragging = true;
            sidebar.style.transition = 'none';
        }
        if (event.cancelable) event.preventDefault();
        const offset = Math.max(-gesture.width, Math.min(0, gesture.offset + dx));
        sidebar.style.transform = `translateX(${offset}px)`;
    };
    const onTouchEnd = (event) => {
        if (!gesture) return;
        const touch = [...event.changedTouches].find(item => item.identifier === gesture.id);
        if (!touch) return;
        const dx = touch.clientX - gesture.x;
        const nextOpen = gesture.dragging && Math.abs(dx) >= Math.min(80, gesture.width * 0.2) ? dx > 0 : open;
        gesture = null;
        setSidebarOpen(nextOpen);
    };
    const clearInlineTransform = () => {
        if (disposed) return;
        gesture = null;
        sidebar.style.transform = '';
    };
    const dispose = () => {
        if (disposed) return;
        disposed = true;
        open = false;
        gesture = null;
        window.removeEventListener('touchstart', onTouchStart);
        window.removeEventListener('touchmove', onTouchMove);
        window.removeEventListener('touchend', onTouchEnd);
        window.removeEventListener('touchcancel', cancelGesture);
        window.removeEventListener('resize', cancelGesture);
        if (window.toggleSidebar === toggleOpen) delete window.toggleSidebar;
        if (window.setSidebarOpen === setSidebarOpen) delete window.setSidebarOpen;
        disposers.delete(dispose);
    };

    window.addEventListener('touchstart', onTouchStart, { passive: true });
    window.addEventListener('touchmove', onTouchMove, { passive: false });
    window.addEventListener('touchend', onTouchEnd);
    window.addEventListener('touchcancel', cancelGesture);
    window.addEventListener('resize', cancelGesture);
    window.toggleSidebar = toggleOpen;
    window.setSidebarOpen = setSidebarOpen;
    sidebar.dataset.mobileOpen = 'false';
    disposers.add(dispose);
    return { toggleOpen, clearInlineTransform, isOpen: () => open, setOpen: setSidebarOpen, dispose };
}

export function cleanup() {
    for (const dispose of [...disposers]) dispose();
}
