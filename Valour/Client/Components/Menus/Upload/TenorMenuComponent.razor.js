/* Credit to Diederik van Leeuwen, https://codepen.io/didumos/pen/xNPKRJ */

const delay = ms => new Promise(res => setTimeout(res, ms));

export function focusAndScroll(rootId){
    const rootElement = document.getElementById(rootId);
    const scrollElement = rootElement.parentElement;
    scrollElement.scrollTop = 0;
    
    // Focus on input
    setTimeout(function(){
        scrollElement.parentElement.querySelector('input').focus();
    }, 10);
}

export function sizeItems(rootElement){
    // Get width of root element
    const rootWidth = rootElement.offsetWidth;

    // Width of images should be half of offset width minus margin
    const imageWidth = (rootWidth / 2) - 15;

    // Scale images to screen size
    const items = rootElement.querySelectorAll('.item');
    items.forEach((item) => {
        const natWidth = item.dataset.natwidth;
        const natHeight = item.dataset.natheight;
        let scalar = imageWidth / natWidth;

        item.width = imageWidth;
        item.height = natHeight * scalar;
    });
}

export function buildMasonry(rootId) {
    const rootElement = document.getElementById(rootId);
    if (!rootElement)
        return;
    
    sizeItems(rootElement);
    
    let cellElements = rootElement.getElementsByClassName('masonry-cell');
    
    if (!cellElements || cellElements.length === 0)
        return;

    let root = {
        element: rootElement,
        noOfColumns: 2,
        cells: Array.prototype.map.call(cellElements, function (cellElement) {
            const style = getComputedStyle(cellElement);
            return {
                'element': cellElement,
                'outerHeight': parseInt(style.marginTop) + cellElement.offsetHeight + parseInt(style.marginBottom)
            }
        })
    }

    // initialize
    const columns = Array.from(new Array(root.noOfColumns)).map(function(column) {
        return {
            'cells': [],
            'outerHeight': 0
        };
    });

    // divide...
    for (let cell of root.cells) {
        const minOuterHeight = Math.min(...columns.map(function (column) {
            return column.outerHeight;
        }));
        const column = columns.find(function (column) {
            return column.outerHeight === minOuterHeight;
        });
        column.cells.push(cell);
        column.outerHeight += cell.outerHeight;
    }

    // calculate masonry height
    const masonryHeight = Math.max(...columns.map( function(column) {
        return column.outerHeight;
    }));

    // ...and conquer
    let order = 0;
    for (let column of columns) {
        for (let cell of column.cells) {
            cell.element.style.order = order.toString();
            order++;
            
            // set the cell's flex-basis to 0
            cell.element.style.flexBasis = '0';
        }
        
        let lastCell = column.cells[column.cells.length - 1];

        if (lastCell) {
            let lastCellEl = lastCell.element;

            // set flex-basis of the last cell to fill the
            // leftover space at the bottom of the column
            // to prevent the first cell of the next column
            // to be rendered at the bottom of this column
            lastCellEl.style.flexBasis =
                lastCellEl.offsetHeight + masonryHeight - column.outerHeight - 1 + 'px';
        }
    }

    // set the masonry height to trigger
    // re-rendering of all cells over columns
    // one pixel more than the tallest column
    root.element.style.maxHeight = masonryHeight + 17 + 'px';
}

export function setupHide(elementId, dotnetRef) {
    document.addEventListener('click', (e) => {
        // Allow tenor button
        if (e.target.classList.contains('tenor')) {
            return;
        }

        let targetEl = e.target;

        // Checks if the clicked element is a child or equal to
        // the given element. If target is outside of the given
        // element, it will continue. Otherwise, it will halt.
        do {
            if (targetEl.id == elementId)
                return;
            targetEl = targetEl.parentElement;
        } while (targetEl);

        // Close the menu if the target was not us or the upload
        // button
        dotnetRef.invokeMethodAsync('Hide');
    });
}