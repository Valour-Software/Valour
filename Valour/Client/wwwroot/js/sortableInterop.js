window.initializeSortable = (container, blazorComponent, items, options) => {
    blazorComponent.items = items;

    const common = {
        onUpdate: function (evt) {
            blazorComponent.invokeMethodAsync('UpdateItemOrder', evt.oldIndex, evt.newIndex);
            const movedItem = items.splice(evt.oldIndex, 1)[0];
            blazorComponent.items.splice(evt.newIndex, 0, movedItem);
        },
        onAdd: function (evt) {
            const oldIndex = evt.oldIndex;
            const newIndex = evt.newIndex;
            const fromComponent = evt.from.__blazor_component;
            const toComponent = evt.to.__blazor_component;
            if (options.group && options.group.pull === 'clone') {
                const movedItem = fromComponent.items[oldIndex];
                toComponent.items.splice(newIndex, 0, movedItem);
                toComponent.invokeMethodAsync('AddItem', newIndex, movedItem);
            }
            else {
                const movedItem = fromComponent.items.splice(oldIndex, 1)[0];
                fromComponent.invokeMethodAsync('RemoveItem', oldIndex);
                toComponent.items.splice(newIndex, 0, movedItem);
                toComponent.invokeMethodAsync('AddItem', newIndex, movedItem);
            }
        }
    };

    const init = Object.assign({}, options, common);
    Sortable.create(container, init);
    //new Sortable(container, {
    //    animation: 150,
    //    ghostClass: "blue-background-class",
    //    chosenClass: "sortable-chosen",
    //    dragClass: "sortable-drag",

    //});
    container.__blazor_component = blazorComponent;
};

window.destroySortable = (element) => {
    // Causes bug but probably not needed accounding to issues on github
    //try {
    //    console.log(element);
    //    var sortable = Sortable.get(element);
    //    if (sortable) {
    //        sortable.destroy();
    //        sortable = null;
    //    }
    //} catch (e) {
    //    console.error(e);
    //}
};