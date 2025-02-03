using Valour.Sdk.Models;
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
        private Channel _currentDragItem;

        // Only of of these should be non-null at a time
        private ChannelListItem _currentDragParentCategory;
        public OldPlanetListComponent currentDragParentPlanet;

        /// <summary>
        /// Run when an item is clicked within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemClickInCategory(Channel item, 
                                          ChannelListItem parent)
        {
            SetTargetInCategory(item, parent);
            Console.WriteLine($"Click for {item.GetHumanReadableName()} {item.Name} at position {_currentDragIndex}");
        }

        /// <summary>
        /// Run when an item is dragged within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemStartDragInCategory(Channel item,
                                              ChannelListItem parent)
        {
            SetTargetInCategory(item, parent);
            Console.WriteLine($"Starting drag for {item.GetHumanReadableName()} {item.Name} at position {_currentDragIndex}");
        }

        /// <summary>
        /// Prepares drag system by setting initial drag object values
        /// </summary>
        /// <param name="item">The item</param>
        /// <param name="parent">The parent category</param>
        public void SetTargetInCategory(Channel item,
                                        ChannelListItem parent)
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
        public async Task OnItemDropIntoCategory(ChannelListItem target)
        {
            if (target == null)
                return;
            
            OnDragEnterItem(0);
            target.Refresh();
            
            // Insert item into the next slot in the category

            // Already parent
            if (target.Channel.Id == _currentDragItem.ParentId)
                return;

            // Same item
            if (target.Channel.Id == _currentDragItem.Id)
                return;
            

            // Add current item to target category
            InsertChannelChildModel model = new()
            {
                ParentId = target.Channel.Id,
                InsertId = _currentDragItem.Id,
                PlanetId = _currentDragItem.PlanetId!.Value,
            };
            var response = await target.Planet.InsertChild(model);
            Console.WriteLine($"Inserting category {_currentDragItem.Id} into {target.Channel.Id}");
            Console.WriteLine(response.Message);
        }
        
        public async Task OnItemDropOnItem(ChannelListItem target)
        {
            if (target == null)
                return;
            
            // Get current top/bottom value
            var top = DragIsTop;
            OnDragEnterItem(0, DragIsTop);
            target.Refresh();

            var targetChannel = target.Channel;

            if (_currentDragItem.Id == targetChannel.Id)
                return;

            int newIndex = -1;

            // Moving within the same category
            if (targetChannel.ParentId == _currentDragItem.ParentId)
            {
                List<long> childrenOrder = null;
                
                // Moving within top level (planet)
                if (targetChannel.ParentId == null)
                {
                    childrenOrder = target.PlanetComponent.TopChannels
                        .Select(x => x.Id)
                        .ToList();
                }
                else
                {
                    childrenOrder = target.ParentComponent.GetChildren()
                        .Select(x => x.Id)
                        .ToList();
                }
                
                childrenOrder.Remove(_currentDragItem.Id);
                
                newIndex = childrenOrder.IndexOf(targetChannel.Id);
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
                    PlanetId = _currentDragItem.PlanetId!.Value,
                    CategoryId = target.Channel.ParentId,
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
                
                if (target.ParentComponent is null)
                {
                    childrenOrder = target.PlanetComponent.TopChannels
                        .Select(x => x.Id)
                        .ToList();
                }
                else
                {
                    childrenOrder = target.ParentComponent.GetChildren()
                        .Select(x => x.Id)
                        .ToList();
                }
                
                
                newIndex = childrenOrder.IndexOf(targetChannel.Id);
                if (!top)
                    newIndex += 1;

                var model = new InsertChannelChildModel()
                {
                    InsertId = _currentDragItem.Id,
                    ParentId = target.Channel.ParentId,
                    PlanetId = _currentDragItem.PlanetId!.Value,
                    Position = newIndex
                };

                var response = await target.PlanetComponent.Planet.InsertChild(model);
                if (!response.Success)
                    Console.WriteLine("Error setting order:\n" + response.Message);
            }

            Console.WriteLine($"Dropped {_currentDragItem.Id} onto {targetChannel.Id} at {newIndex}");
        }

        public long DragOverId = 0;
        public bool DragIsTop = true;
        public ChannelListItem HighlightInner = null;

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
