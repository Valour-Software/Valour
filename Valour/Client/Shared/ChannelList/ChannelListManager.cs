using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Valour.Client.Categories;
using Valour.Client.Channels;
using Valour.Client.Planets;
using Valour.Shared;
using Valour.Shared.Categories;

namespace Valour.Client.Shared.ChannelList
{
    public class ChannelListManager
    {
        public static ChannelListManager Instance;

        public ChannelListManager()
        {
            Instance = this;
        }

        public int currentDragIndex;
        public IClientPlanetListItem currentDragItem;

        // Only of of these should be non-null at a time
        public ChannelListCategoryComponent currentDragParentCategory;
        public ChannelListPlanetComponent currentDragParentPlanet;

        /// <summary>
        /// Run when an item is clicked within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemClickInCategory(IClientPlanetListItem item, 
                                          ChannelListCategoryComponent parent)
        {
            SetTargetInCategory(item, parent);
            Console.WriteLine($"Click for {item.GetItemTypeName()} at position {currentDragIndex} in {parent.Category.Name}");
        }

        /// <summary>
        /// Run when an item is dragged within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemStartDragInCategory(IClientPlanetListItem item,
                                              ChannelListCategoryComponent parent)
        {
            SetTargetInCategory(item, parent);
            Console.WriteLine($"Starting drag for {item.GetItemTypeName()} at position {currentDragIndex} in {parent.Category.Name}");
        }

        /// <summary>
        /// Prepares drag system by setting initial drag object values
        /// </summary>
        /// <param name="item">The item</param>
        /// <param name="parent">The parent category</param>
        public void SetTargetInCategory(IClientPlanetListItem item,
                                        ChannelListCategoryComponent parent)
        {
            currentDragIndex = parent.GetIndex(item);
            currentDragParentPlanet = null;
            currentDragParentCategory = parent;
            currentDragItem = item;
        }

        /// <summary>
        /// Run when an item is dropped on a category
        /// </summary>
        /// <param name="target">The category component that the item was dropped onto</param>
        public void OnItemDropOnCategory(ChannelListCategoryComponent target)
        {
            
        }

        public async Task OnItemDropOnChatChannel(ChannelListChatChannelComponent target)
        {
            if (target == null)
                return;

            var oldIndex = currentDragParentCategory.GetIndex(currentDragItem);
            var newIndex = target.ParentCategory.GetIndex(target.Channel);

            // Remove from old list
            currentDragParentCategory.ItemList.RemoveAt(oldIndex);
            // Insert into new list at correct position
            target.ParentCategory.ItemList.Insert(newIndex, currentDragItem);
            currentDragItem.Parent_Id = target.ParentCategory.Category.Id;

            HttpResponseMessage response = null;
            List<CategoryContentData> orderData = null;

            // Categories are not the same
            //if (currentDragParentCategory.Category.Id !=
            //    target.ParentCategory.Category.Id)
            //{
                // Update the target's category

                // Create order data
                orderData = new List<CategoryContentData>();

                ushort pos = 0;

                foreach (var item in target.ParentCategory.ItemList)
                {
                    orderData.Add(
                        new CategoryContentData()
                        {
                            Id = item.Id,
                            Position = pos,
                            ItemType = item.ItemType
                        }
                    );

                    pos++;
                }

                response = await ClientUserManager.Http.PostAsJsonAsync($"Category/SetContents?category_id={target.ParentCategory.Category.Id}&auth={ClientUserManager.UserSecretToken}", orderData);

                TaskResult result = JsonConvert.DeserializeObject<TaskResult>(await response.Content.ReadAsStringAsync());

                Console.WriteLine(result);

                //target.ParentCategory.Refresh();
            //}

            // Update the source category
            //currentDragParentCategory.Refresh();
            
            Console.WriteLine($"Dropped onto {target.Channel.Name} at {newIndex}");
        }
    }
}
