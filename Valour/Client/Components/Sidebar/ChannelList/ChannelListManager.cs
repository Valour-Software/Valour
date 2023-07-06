using Valour.Shared;
using Valour.Shared.Categories;
using Valour.Api.Client;
using Valour.Api.Items;
using Valour.Api.Models;
using Valour.Shared.Models;

namespace Valour.Client.Components.Sidebar.ChannelList
{
    public class ChannelListManager
    {

        public static ChannelListManager Instance;

        public ChannelListManager()
        {
            Instance = this;
        }

        private int _currentDragIndex;
        private PlanetChannel _currentDragItem;

        // Only of of these should be non-null at a time
        private CategoryListComponent _currentDragParentCategory;
        public PlanetListComponent currentDragParentPlanet;

        /// <summary>
        /// Run when an item is clicked within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemClickInCategory(PlanetChannel item, 
                                          CategoryListComponent parent)
        {
            SetTargetInCategory(item, parent);
            Console.WriteLine($"Click for {item.GetHumanReadableName()} {item.Name} at position {_currentDragIndex}");
        }

        /// <summary>
        /// Run when an item is dragged within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemStartDragInCategory(PlanetChannel item,
                                              CategoryListComponent parent)
        {
            SetTargetInCategory(item, parent);
            Console.WriteLine($"Starting drag for {item.GetHumanReadableName()} {item.Name} at position {_currentDragIndex}");
        }

        /// <summary>
        /// Prepares drag system by setting initial drag object values
        /// </summary>
        /// <param name="item">The item</param>
        /// <param name="parent">The parent category</param>
        public void SetTargetInCategory(PlanetChannel item,
                                        CategoryListComponent parent)
        {
            _currentDragIndex = 0;

            if (parent != null)
            {
                _currentDragIndex = parent.GetIndex(item);
            }

            currentDragParentPlanet = null;
            _currentDragParentCategory = parent;
            _currentDragItem = item;
        }

        /// <summary>
        /// Run when an item is dropped on a category
        /// </summary>
        /// <param name="target">The category component that the item was dropped onto</param>
        public async Task OnItemDropIntoCategory(CategoryListComponent target)
        {
            OnDragEnterItem(0);
            target.Refresh();
            
            // Insert item into the next slot in the category
            if (target == null)
                return;

            // Already parent
            if (target.Category.Id == _currentDragItem.ParentId)
                return;

            // Same item
            if (target.Category.Id == _currentDragItem.Id)
                return;
            

            // Add current item to target category
            InsertChannelChildModel model = new()
            {
                ParentId = target.Category.Id,
                InsertId = _currentDragItem.Id,
                PlanetId = _currentDragItem.PlanetId,
            };
            var response = await target.Planet.InsertChild(model);
            Console.WriteLine($"Inserting category {_currentDragItem.Id} into {target.Category.Id}");
            Console.WriteLine(response.Message);
        }
        
        public async Task OnItemDropOnItem(ChannelListItemComponent target)
        {
            // Get current top/bottom value
            var top = DragIsTop;
            OnDragEnterItem(0, DragIsTop);
            target.Refresh();
            
            if (target == null)
                return;

            var targetItem = target.GetItem();

            if (_currentDragItem.Id == targetItem.Id)
                return;

            int newIndex = -1;

            // Moving within the same category
            if (targetItem.ParentId == _currentDragItem.ParentId)
            {
                List<long> childrenOrder = null;
                
                // Moving within top level (planet)
                if (targetItem.ParentId == null)
                {
                    childrenOrder = target.PlanetComponent.TopItems
                        .Select(x => x.Id)
                        .ToList();
                }
                else
                {
                    childrenOrder = target.ParentCategory.ItemList.Select(x => x.Id).ToList();
                }
                
                childrenOrder.Remove(_currentDragItem.Id);
                
                newIndex = childrenOrder.IndexOf(targetItem.Id);
                if (!top)
                    newIndex += 1;

                if (newIndex >= childrenOrder.Count)
                {
                    childrenOrder.Add(_currentDragItem.Id);
                }
                else
                {
                    childrenOrder.Insert(newIndex, _currentDragItem.Id);
                }

                var model = new OrderChannelsModel()
                {
                    PlanetId = _currentDragItem.PlanetId,
                    CategoryId = target.GetItem().ParentId,
                    Order = childrenOrder,
                };
                
                var response = await target.PlanetComponent.Planet.SetChildOrderAsync(model);
                if (!response.Success)
                    Console.WriteLine("Error setting order:\n" + response.Message);
            }
            // Inserting
            else
            {
                List<long> childrenOrder = null;
                
                if (target.ParentCategory is null)
                {
                    childrenOrder = target.PlanetComponent.TopItems
                        .Select(x => x.Id)
                        .ToList();
                }
                else
                {
                    childrenOrder = target.ParentCategory.ItemList.Select(x => x.Id).ToList();
                }
                
                
                newIndex = childrenOrder.IndexOf(targetItem.Id);
                if (!top)
                    newIndex += 1;

                var model = new InsertChannelChildModel()
                {
                    InsertId = _currentDragItem.Id,
                    ParentId = target.GetItem().ParentId,
                    PlanetId = _currentDragItem.PlanetId,
                    Position = newIndex
                };

                var response = await target.PlanetComponent.Planet.InsertChild(model);
                if (!response.Success)
                    Console.WriteLine("Error setting order:\n" + response.Message);
            }

            Console.WriteLine($"Dropped {_currentDragItem.Id} onto {targetItem.Id} at {newIndex}");
        }

        public long DragOverId = 0;
        public bool DragIsTop = true;
        public ChannelListItemComponent HighlightInner = null;

        public void OnDragEnterItem(long id, bool top = false)
        {
            DragOverId = id;
            DragIsTop = top;
            
            var oldHighlight = HighlightInner;
            
            HighlightInner = null;
            
            if (oldHighlight is not null)
                oldHighlight.Refresh();
        }
    }
}
