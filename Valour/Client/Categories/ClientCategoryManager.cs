using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Categories;

namespace Valour.Client.Categories
{
    public class ClientCategoryManager
    {
        private ConcurrentDictionary<ulong, bool> CategoryOpenStates = new ConcurrentDictionary<ulong, bool>();

        public bool IsOpen(PlanetCategory category)
        {
            return IsOpen(category.Id);
        }

        public bool IsOpen(ulong category_id)
        {
            if (!CategoryOpenStates.ContainsKey(category_id))
            {
                return false;
            }

            return CategoryOpenStates[category_id];
        }

        public void SetOpen(PlanetCategory category, bool value)
        {
            SetOpen(category.Id, value);
        }

        public void SetOpen(ulong category_id, bool value)
        {
            if (!CategoryOpenStates.ContainsKey(category_id))
            {
                CategoryOpenStates.TryAdd(category_id, value);

                //Console.WriteLine($"Set new state to {value}");

                return;
            }

            CategoryOpenStates[category_id] = value;

            //Console.WriteLine($"Set state to {value}");
        }
    }
}
