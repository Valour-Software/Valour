/* Credit to Diederik van Leeuwen, https://codepen.io/didumos/pen/xNPKRJ */

export function focusAndScroll(rootId) {
    const rootElement = document.getElementById(rootId);
    if (!rootElement) return;
    const scrollElement = rootElement.parentElement;
    scrollElement.scrollTop = 0;
    setTimeout(() => scrollElement.parentElement.querySelector('input')?.focus(), 10);
}

function sizeItems(rootElement) {
    const imageWidth = (rootElement.offsetWidth / 2) - 15;
    rootElement.querySelectorAll('.item').forEach(item => {
        const natWidth = Number(item.dataset.natwidth);
        const natHeight = Number(item.dataset.natheight);
        if (!natWidth || !natHeight) return;
        item.width = imageWidth;
        item.height = natHeight * (imageWidth / natWidth);
    });
}

export function buildMasonry(rootId) {
    const rootElement = document.getElementById(rootId);
    if (!rootElement) return;
    sizeItems(rootElement);

    const cells = Array.from(rootElement.getElementsByClassName('masonry-cell'));
    if (cells.length === 0) return;
    const columns = [{ cells: [], outerHeight: 0 }, { cells: [], outerHeight: 0 }];

    for (const element of cells) {
        const style = getComputedStyle(element);
        const outerHeight = parseInt(style.marginTop) + element.offsetHeight + parseInt(style.marginBottom);
        const column = columns[0].outerHeight <= columns[1].outerHeight ? columns[0] : columns[1];
        column.cells.push({ element, outerHeight });
        column.outerHeight += outerHeight;
    }

    const masonryHeight = Math.max(...columns.map(column => column.outerHeight));
    let order = 0;
    for (const column of columns) {
        for (const cell of column.cells) {
            cell.element.style.order = (order++).toString();
            cell.element.style.flexBasis = '0';
        }
        const lastCell = column.cells.at(-1)?.element;
        if (lastCell)
            lastCell.style.flexBasis = `${lastCell.offsetHeight + masonryHeight - column.outerHeight - 1}px`;
    }
    rootElement.style.maxHeight = `${masonryHeight + 17}px`;
}

export function setupHide(elementId, dotnetRef) {
    document.addEventListener('mousedown', function handler(e) {
        if (e.target.classList.contains('klipy')) return;
        let target = e.target;
        while (target) {
            if (target.id === elementId) return;
            target = target.parentElement;
        }
        dotnetRef.invokeMethodAsync('Hide').catch(() => document.removeEventListener('mousedown', handler));
    });
}
