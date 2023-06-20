using Valour.Shared;
using Valour.Shared.Categories;
using Valour.Api.Client;
using Valour.Api.Items;
using Valour.Api.Models;

namespace Valour.Client.Components.ChannelList
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
        /// Run when an item is dropped on a planet
        /// </summary>
        /// <param name="target">The planet component that the item was dropped onto</param>
        public async Task OnItemDropOnPlanet(PlanetListComponent target)
        {
            OnDragEnterItem(0);
            
            // Insert item into the next slot in the category
            if (target == null)
                return;

            if (_currentDragItem == null)
                return;

            // Only categories can be put under a planet
            if (_currentDragItem is not PlanetCategory)
                return;

            // Already parent
            if (target.Planet.Id == _currentDragItem.ParentId)
                return;

            // Add current item to target category

            _currentDragItem.Position =  -1;
            _currentDragItem.ParentId = null;
            var response = await Item.UpdateAsync(_currentDragItem);

            Console.WriteLine($"Inserting category {_currentDragItem.Id} into planet {target.Planet.Id}");

            Console.WriteLine(response.Message);
        }

        /// <summary>
        /// Run when an item is dropped on a category
        /// </summary>
        /// <param name="target">The category component that the item was dropped onto</param>
        public async Task OnItemDropOnCategory(CategoryListComponent target)
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
            var response = await target.Category.InsertChild(_currentDragItem.Id);
            Console.WriteLine($"Inserting category {_currentDragItem.Id} into {target.Category.Id}");
            Console.WriteLine(response.Message);
        }

        // TODO: Merge this and below into one function using some inheritance on the components
        public async Task OnItemDropOnVoiceChannel(VoiceChannelListComponent target)
        {
            OnDragEnterItem(0);
            
            if (target == null)
                return;

            var oldIndex = 0;

            if (_currentDragParentCategory != null)
            {
                oldIndex = _currentDragParentCategory.GetIndex(_currentDragItem);
            }
            var newIndex = target.ParentCategory.GetIndex(target.Channel);

            // Remove from old list
            if (_currentDragParentCategory != null)
            {
                _currentDragParentCategory.ItemList.RemoveAt(oldIndex);
            }
            // Insert into new list at correct position
            target.ParentCategory.ItemList.Insert(newIndex, _currentDragItem);
            _currentDragItem.ParentId = target.ParentCategory.Category.Id;

            TaskResult response;
            var orderData = new List<long>();

            ushort pos = 0;

            foreach (var item in target.ParentCategory.ItemList)
            {
                Console.WriteLine($"{item.Id} at {pos}");

                orderData.Add(
                    item.Id
                );

                pos++;
            }

            response = await target.ParentCategory.Category.SetChildOrderAsync(orderData);

            Console.WriteLine(response.Message);
            Console.WriteLine($"Dropped {_currentDragItem.Id} onto {target.Channel.Id} at {newIndex}");
        }

        public async Task OnItemDropOnChatChannel(ChatChannelListComponent target)
        {
            OnDragEnterItem(0);
            target.Refresh();
            
            if (target == null)
                return;

            if (_currentDragItem.Id == target.Channel.Id)
                return;

            int newIndex = -1;
            
            // Moving within the same category
            if (target.Channel.ParentId == _currentDragItem.ParentId)
            {
                var childrenOrder = target.ParentCategory.ItemList.Select(x => x.Id).ToList();
                childrenOrder.Remove(_currentDragItem.Id);
                newIndex = target.ParentCategory.GetIndex(target.Channel);
                    
                childrenOrder.Insert(newIndex, _currentDragItem.Id);

                var response = await target.ParentCategory.Category.SetChildOrderAsync(childrenOrder);
                if (!response.Success)
                    Console.WriteLine("Error setting order:\n" + response.Message);
            }
            // Inserting into new category
            else
            {
                newIndex = target.ParentCategory.GetIndex(target.Channel);
                var response = await target.ParentCategory.Category.InsertChild(_currentDragItem.Id, newIndex);
                if (!response.Success)
                    Console.WriteLine("Error setting order:\n" + response.Message);
            }
            
            Console.WriteLine($"Dropped {_currentDragItem.Id} onto {target.Channel.Id} at {newIndex}");
        }

        public long DragOverId = 0;
        
        public void OnDragEnterItem(long id)
        {
            DragOverId = id;
        }

        public void OnDragLeave()
        {
            DragOverId = 0;
        }
    }
}
