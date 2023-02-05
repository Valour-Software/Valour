using System.Collections.Concurrent;
using Valour.Api.Models;

namespace Valour.Client.Categories
{
    public class ClientCategoryManager
    {
        private ConcurrentDictionary<long, bool> CategoryOpenStates = new ConcurrentDictionary<long, bool>();

        public bool IsOpen(PlanetCategory category)
        {
            return IsOpen(category.Id);
        }

        public bool IsOpen(long categoryId)
        {
            if (!CategoryOpenStates.ContainsKey(categoryId))
            {
                return true;
            }

            return CategoryOpenStates[categoryId];
        }

        public void SetOpen(PlanetCategory category, bool value)
        {
            SetOpen(category.Id, value);
        }

        public void SetOpen(long categoryId, bool value)
        {
            if (!CategoryOpenStates.ContainsKey(categoryId))
            {
                CategoryOpenStates.TryAdd(categoryId, value);

                //Console.WriteLine($"Set new state to {value}");

                return;
            }

            CategoryOpenStates[categoryId] = value;

            //Console.WriteLine($"Set state to {value}");
        }
    }
}
