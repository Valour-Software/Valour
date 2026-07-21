using Valour.Client.Components.Utility;
using Valour.Client.Toast;
using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Client.Components.Sidebar.Directory
{
    public class ChannelDragManager
    {

        public static ChannelDragManager Instance;

        public static bool Dragging;
        
        public static HybridEvent<Channel?> ChannelDragChanged;

        public ChannelDragManager()
        {
            Instance = this;
            // BrowserUtils.Blurred += OnCancelDrag;
            // BrowserUtils.Focused += OnCancelDrag;
        }
        
        public static ChannelDirectoryItem CurrentDragItem;

        /// <summary>
        /// A channel being dragged from outside the channel directory (e.g. the recent
        /// chats dock). Lets non-directory sources reuse the window drop targets.
        /// </summary>
        public static Channel CurrentDragChannel;

        /// <summary>
        /// The channel currently being dragged, regardless of its source.
        /// </summary>
        public static Channel EffectiveDragChannel => CurrentDragItem?.Channel ?? CurrentDragChannel;

        /// <summary>
        /// Begins a drag for a channel that does not originate from the channel directory.
        /// </summary>
        public void OnStartExternalDrag(Channel channel)
        {
            Dragging = true;
            CurrentDragItem = null;
            CurrentDragChannel = channel;

            ChannelDragChanged?.Invoke(channel);
        }

        public void OnCancelDrag()
        {
            Dragging = false;
            
            DragOverId = 0;
            var dragItem = CurrentDragItem;
            CurrentDragItem = null;
            CurrentDragChannel = null;
            
            dragItem?.PlanetComponent.ChannelsNeedRefresh?.Invoke();
            
            ChannelDragChanged?.Invoke(null);
        }

        /// <summary>
        /// Run when an item is clicked within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemClickInCategory(ChannelDirectoryItem item)
        {
            SetTarget(item);
            Console.WriteLine($"Click for {item.Channel.GetHumanReadableName()} {item.Channel.Name}");
        }

        /// <summary>
        /// Run when an item is dragged within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemStartDragInCategory(ChannelDirectoryItem item)
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
        public void SetTarget(ChannelDirectoryItem item)
        {
            CurrentDragItem = item;
        }
        
        
        public async Task OnItemDropOn(ChannelDirectoryItem droppedOn)
        {
            Dragging = false;
            
            if (droppedOn is null)
                return;
            
            // Get current top/bottom value
            var top = DragIsTop;
            OnDragEnterItem(0, DragIsTop);
            droppedOn.Refresh();

            var draggedChannel = CurrentDragItem?.Channel;
            var droppedOnChannel = droppedOn.Channel;

            await MoveChannelAsync(draggedChannel, droppedOnChannel, top, DragIntoCategory);
        }

        /// <summary>
        /// Moves a channel from either the HTML drag path or the pointer/touch
        /// fallback used by native WebViews and mobile browsers.
        /// </summary>
        public async Task<TaskResult> MoveChannelAsync(
            Channel draggedChannel,
            Channel droppedOnChannel,
            bool placeBefore,
            bool insideCategory)
        {
            if (draggedChannel is null || droppedOnChannel is null)
                return TaskResult.FromFailure("A drag source or destination was unavailable.");

            if (draggedChannel.Id == droppedOnChannel.Id)
                return TaskResult.FromFailure("A channel cannot be moved onto itself.");

            var loop = ChannelPosition.ContainsPosition(draggedChannel.RawPosition, droppedOnChannel.RawPosition);
            
            // Ensure we aren't creating a loop
            if (loop)
            {
                ToastContainer.Instance?.AddToast(new ToastData()
                {
                    Type = ToastProgressState.Failure,
                    Title = "Failed to move channel",
                    Message = "Move resulted in a loop",
                });
                
                return TaskResult.FromFailure("Move resulted in a loop");
            }

            var task = draggedChannel.Client.ChannelService.MoveChannelAsync(draggedChannel, droppedOnChannel,
                placeBefore, insideCategory);

            TaskResult result;
            if (ToastContainer.Instance is not null)
            {
                result = await ToastContainer.Instance.WaitToastWithTaskResult(new ProgressToastData<TaskResult>(
                    "Moving channel",
                    "Waiting for server...",
                    task,
                    "Channel moved successfully"
                ));
            }
            else
            {
                result = await task;
            }
            
            Console.WriteLine($"Dropped {draggedChannel.Id} onto {droppedOnChannel.Id}");
            
            ChannelDragChanged?.Invoke(null);
            return result;
        }

        public long DragOverId = 0;
        public bool DragIsTop = true;
        public bool DragIntoCategory = false;
        public ChannelDirectoryItem HighlightInner = null;

        public void OnDragEnterItem(long id, bool top = false, bool insideCategory = false)
        {
            DragOverId = id;
            DragIsTop = top;
            DragIntoCategory = insideCategory;

            var oldHighlight = HighlightInner;

            HighlightInner = null;

            if (oldHighlight is not null)
                oldHighlight.Refresh();
        }
    }
}
