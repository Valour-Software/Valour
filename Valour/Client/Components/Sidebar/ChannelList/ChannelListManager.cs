using Valour.Client.Components.Utility;
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
            // BrowserUtils.Blurred += OnCancelDrag;
            // BrowserUtils.Focused += OnCancelDrag;
        }
        
        private ChannelListItem _currentDragItem;

        public void OnCancelDrag()
        {
            DragOverId = 0;
            var dragItem = _currentDragItem;
            _currentDragItem = null;
            
            dragItem.PlanetComponent.RefreshChannels();
        }

        /// <summary>
        /// Run when an item is clicked within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemClickInCategory(ChannelListItem item)
        {
            SetTarget(item);
            Console.WriteLine($"Click for {item.Channel.GetHumanReadableName()} {item.Channel.Name}");
        }

        /// <summary>
        /// Run when an item is dragged within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemStartDragInCategory(ChannelListItem item)
        {
            SetTarget(item);
            Console.WriteLine($"Starting drag for {item.Channel.GetHumanReadableName()} {item.Channel.Name}");
        }

        /// <summary>
        /// Prepares drag system by setting initial drag object values
        /// </summary>
        /// <param name="item">The item</param>
        /// <param name="parent">The parent category</param>
        public void SetTarget(ChannelListItem item)
        {
            _currentDragItem = item;
        }
        
        
        public async Task OnItemDropOn(ChannelListItem droppedOn)
        {
            if (droppedOn is null)
                return;
            
            // Get current top/bottom value
            var top = DragIsTop;
            OnDragEnterItem(0, DragIsTop);
            droppedOn.Refresh();

            var draggedChannel = _currentDragItem.Channel;
            var droppedOnChannel = droppedOn.Channel;

            if (draggedChannel.Id == droppedOnChannel.Id)
                return;
            
            // Ensure we aren't creating a loop
            if (draggedChannel.Position.ContainsPosition(droppedOnChannel.Position))
                return;

            // When dropped on a category, it is appended to the end
            if (!DragIsTop)
            {
                // Target to insert is simple the dropped on channel
                
            }
            else
            {
                // Target to insert is whatever is *before* the dropped on channel
            }
            
            Console.WriteLine($"Dropped {draggedChannel.Id} onto {droppedOnChannel.Id}");
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
