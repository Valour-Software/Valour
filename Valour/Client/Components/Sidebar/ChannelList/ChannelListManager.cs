using Valour.Client.Components.Utility;
using Valour.Client.Toast;
using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Client.Components.Sidebar.ChannelList
{
    public class ChannelListManager
    {

        public static ChannelListManager Instance;

        public static bool Dragging;
        
        public static HybridEvent<Channel?> ChannelDragChanged;

        public ChannelListManager()
        {
            Instance = this;
            // BrowserUtils.Blurred += OnCancelDrag;
            // BrowserUtils.Focused += OnCancelDrag;
        }
        
        public static ChannelListItem CurrentDragItem;

        public void OnCancelDrag()
        {
            Dragging = false;
            
            DragOverId = 0;
            var dragItem = CurrentDragItem;
            CurrentDragItem = null;
            
            dragItem.PlanetComponent.RefreshChannels();
            
            ChannelDragChanged?.Invoke(null);
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
            Dragging = true;
            
            var old = CurrentDragItem;
            SetTarget(item);
            Console.WriteLine($"Starting drag for {item.Channel.GetHumanReadableName()} {item.Channel.Name}");
            
            if (old?.Channel?.Id != item?.Channel?.Id)
                ChannelDragChanged?.Invoke(item.Channel);
        }

        /// <summary>
        /// Prepares drag system by setting initial drag object values
        /// </summary>
        /// <param name="item">The item</param>
        /// <param name="parent">The parent category</param>
        public void SetTarget(ChannelListItem item)
        {
            CurrentDragItem = item;
        }
        
        
        public async Task OnItemDropOn(ChannelListItem droppedOn)
        {
            Dragging = false;
            
            if (droppedOn is null)
                return;
            
            // Get current top/bottom value
            var top = DragIsTop;
            OnDragEnterItem(0, DragIsTop);
            droppedOn.Refresh();

            var draggedChannel = CurrentDragItem.Channel;
            var droppedOnChannel = droppedOn.Channel;

            if (draggedChannel.Id == droppedOnChannel.Id)
                return;

            var loop = ChannelPosition.ContainsPosition(draggedChannel.RawPosition, droppedOnChannel.RawPosition);
            
            // Ensure we aren't creating a loop
            if (loop)
            {
                ToastContainer.Instance.AddToast(new ToastData()
                {
                    Type = ToastProgressState.Failure,
                    Title = "Failed to move channel",
                    Message = "Move resulted in a loop",
                });
                
                return;
            }

            var task = draggedChannel.Client.ChannelService.MoveChannelAsync(draggedChannel, droppedOnChannel,
                    DragIsTop);

            await ToastContainer.Instance.WaitToastWithTaskResult(new ProgressToastData<TaskResult>(
                "Moving channel",
                "Waiting for server...",
                task,
                "Channel moved successfully"
            ));
            
            Console.WriteLine($"Dropped {draggedChannel.Id} onto {droppedOnChannel.Id}");
            
            ChannelDragChanged?.Invoke(null);
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
