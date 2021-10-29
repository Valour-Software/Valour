using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Valour.Client.Categories;
using Valour.Client.Planets;
using Valour.Shared;
using Valour.Shared.Categories;
using Valour.Api.Planets;
using Valour.Api.Client;

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
        public IPlanetListItem currentDragItem;

        // Only of of these should be non-null at a time
        public ChannelListCategoryComponent currentDragParentCategory;
        public ChannelListPlanetComponent currentDragParentPlanet;

        /// <summary>
        /// Run when an item is clicked within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemClickInCategory(IPlanetListItem item, 
                                          ChannelListCategoryComponent parent)
        {
            SetTargetInCategory(item, parent);
            Console.WriteLine($"Click for {item.GetItemTypeName()} {item.Name} at position {currentDragIndex}");
        }

        /// <summary>
        /// Run when an item is dragged within a category
        /// </summary>
        /// <param name="item">The item that was clicked</param>
        /// <param name="parent">The parent category of the item that was clicked</param>
        public void OnItemStartDragInCategory(IPlanetListItem item,
                                              ChannelListCategoryComponent parent)
        {
            SetTargetInCategory(item, parent);
            Console.WriteLine($"Starting drag for {item.GetItemTypeName()} {item.Name} at position {currentDragIndex}");
        }

        /// <summary>
        /// Prepares drag system by setting initial drag object values
        /// </summary>
        /// <param name="item">The item</param>
        /// <param name="parent">The parent category</param>
        public void SetTargetInCategory(IPlanetListItem item,
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
            if (currentDragItem.ItemType != Valour.Shared.Items.ItemType.Category)
                return;

            // Already parent
            if (target.Planet.Id == currentDragItem.Parent_Id)
                return;

            ushort position = (ushort)target.TopCategories.Count;

            // Add current item to target category

            var response = await ValourClient.PutAsync($"api/category/{currentDragItem.Id}/parent_id?position={position}", "none");

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
            if (target.Category.Id == currentDragItem.Parent_Id)
                return;

            // Same item
            if (target.Category.Id == currentDragItem.Id)
                return;

            ushort position = (ushort)target.ItemList.Count();

            currentDragItem.Parent_Id = target.Category.Id;
            currentDragItem.Position = position;

            // Add current item to target category

            var response = await ValourClient.PostAsync($"/api/category/{target.Category.Id}/children?type={currentDragItem.ItemType}", currentDragItem);

            Console.WriteLine($"Inserting {currentDragItem.Id} into {target.Category.Id} at position {position}");

            Console.WriteLine(response.Message);
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
            currentDragItem.Parent_Id = target.ParentCategory.Category.Id;

            TaskResult response;
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
                    Console.WriteLine($"{item.Id} at {pos}");

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

                response = await ValourClient.PostAsync($"api/category/{target.ParentCategory.Category.Id}/children/order", orderData);

                Console.WriteLine(response.Message);

                //target.ParentCategory.Refresh();
            //}

            // Update the source category
            //currentDragParentCategory.Refresh();
            
            Console.WriteLine($"Dropped {currentDragItem.Id} onto {target.Channel.Id} at {newIndex}");
        }
    }
}
