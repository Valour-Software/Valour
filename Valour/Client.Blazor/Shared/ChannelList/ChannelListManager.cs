using Valour.Shared;
using Valour.Shared.Categories;
using Valour.Api.Client;
using Valour.Api.Items.Planets.Channels;

namespace Valour.Client.Blazor.Shared.ChannelList
{
    public class ChannelListManager
    {
        public static ChannelListManager Instance;

        public ChannelListManager()
        {
            Instance = this;
        }

        public int currentDragIndex;
        public PlanetChannel currentDragItem;

        // Only of of these should be non-null at a time
        public ChannelListCategoryComponent currentDragParentCategory;
        public ChannelListPlanetComponent currentDragParentPlanet;

        /// <summary>
        /// Run when an item is clicked within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemClickInCategory(PlanetChannel item, 
                                          ChannelListCategoryComponent parent)
        {
            SetTargetInCategory(item, parent);
            Console.WriteLine($"Click for {item.GetHumanReadableName()} {item.Name} at position {currentDragIndex}");
        }

        /// <summary>
        /// Run when an item is dragged within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemStartDragInCategory(PlanetChannel item,
                                              ChannelListCategoryComponent parent)
        {
            SetTargetInCategory(item, parent);
            Console.WriteLine($"Starting drag for {item.GetHumanReadableName()} {item.Name} at position {currentDragIndex}");
        }

        /// <summary>
        /// Prepares drag system by setting initial drag object values
        /// </summary>
        /// <param name="item">The item</param>
        /// <param name="parent">The parent category</param>
        public void SetTargetInCategory(PlanetChannel item,
                                        ChannelListCategoryComponent parent)
        {
            currentDragIndex = 0;

            if (parent != null)
            {
                currentDragIndex = parent.GetIndex(item);
            }

            currentDragParentPlanet = null;
            currentDragParentCategory = parent;
            currentDragItem = item;
        }

        /// <summary>
        /// Run when an item is dropped on a planet
        /// </summary>
        /// <param name="target">The planet component that the item was dropped onto</param>
        public async Task OnItemDropOnPlanet(ChannelListPlanetComponent target)
        {
            // Insert item into the next slot in the category
            if (target == null)
                return;

            if (currentDragItem == null)
                return;

            // Only categories can be put under a planet
            if (currentDragItem is not PlanetCategoryChannel)
                return;

            // Already parent
            if (target.Planet.Id == currentDragItem.ParentId)
                return;

            ushort position = (ushort)target.TopCategories.Count;

            // Add current item to target category

            var response = await ValourClient.PutAsync($"api/category/{currentDragItem.Id}/parentId?position={position}", "none");

            Console.WriteLine($"Inserting category {currentDragItem.Id} into planet {target.Planet.Id} at position {position}");

            Console.WriteLine(response.Message);
        }

        /// <summary>
        /// Run when an item is dropped on a category
        /// </summary>
        /// <param name="target">The category component that the item was dropped onto</param>
        public async Task OnItemDropOnCategory(ChannelListCategoryComponent target)
        {
            // Insert item into the next slot in the category
            if (target == null)
                return;

            // Already parent
            if (target.Category.Id == currentDragItem.ParentId)
                return;

            // Same item
            if (target.Category.Id == currentDragItem.Id)
                return;

            ushort position = (ushort)target.ItemList.Count();

            currentDragItem.ParentId = target.Category.Id;
            currentDragItem.Position = position;

            // Add current item to target category

            if (currentDragItem is PlanetCategoryChannel)
            {
                var response = await PlanetCategoryChannel.UpdateAsync((PlanetCategoryChannel)currentDragItem);
                Console.WriteLine($"Inserting category {currentDragItem.Id} into {target.Category.Id} at position {position}");
                Console.WriteLine(response.Message);
            }
            else if (currentDragItem is PlanetChatChannel)
            {
                var response = await PlanetChatChannel.UpdateAsync((PlanetChatChannel)currentDragItem);
                Console.WriteLine($"Inserting chat channel {currentDragItem.Id} into {target.Category.Id} at position {position}");
                Console.WriteLine(response.Message);
            }
        }

        public async Task OnItemDropOnChatChannel(ChannelListChatChannelComponent target)
        {
            if (target == null)
                return;

            var oldIndex = 0;

            if (currentDragParentCategory != null)
            {
                oldIndex = currentDragParentCategory.GetIndex(currentDragItem);
            }
            var newIndex = target.ParentCategory.GetIndex(target.Channel);

            // Remove from old list
            if (currentDragParentCategory != null)
            {
                currentDragParentCategory.ItemList.RemoveAt(oldIndex);
            }
            // Insert into new list at correct position
            target.ParentCategory.ItemList.Insert(newIndex, currentDragItem);
            currentDragItem.ParentId = target.ParentCategory.Category.Id;

            TaskResult response;
            List<long> orderData = null;

            // Categories are not the same
            //if (currentDragParentCategory.Category.Id !=
            //    target.ParentCategory.Category.Id)
            //{
                // Update the target's category

                // Create order data
                orderData = new List<long>();

                ushort pos = 0;

                foreach (var item in target.ParentCategory.ItemList)
                {
                    Console.WriteLine($"{item.Id} at {pos}");

                    orderData.Add(
                        item.Id
                    );

                    pos++;
                }

                response = await ValourClient.PostAsync($"{target.ParentCategory.Category.IdRoute}/children/order", orderData);

                Console.WriteLine(response.Message);

                //target.ParentCategory.Refresh();
            //}

            // Update the source category
            //currentDragParentCategory.Refresh();
            
            Console.WriteLine($"Dropped {currentDragItem.Id} onto {target.Channel.Id} at {newIndex}");
        }
    }
}
