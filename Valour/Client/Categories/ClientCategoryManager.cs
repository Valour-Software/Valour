using System.Collections.Concurrent;
using Valour.Api.Items.Planets.Channels;

namespace Valour.Client.Categories
{
    public class ClientCategoryManager
    {
        private ConcurrentDictionary<ulong, bool> CategoryOpenStates = new ConcurrentDictionary<ulong, bool>();

        public bool IsOpen(PlanetCategoryChannel category)
        {
            return IsOpen(category.Id);
        }

        public bool IsOpen(ulong categoryId)
        {
            if (!CategoryOpenStates.ContainsKey(categoryId))
            {
                return false;
            }

            return CategoryOpenStates[categoryId];
        }

        public void SetOpen(PlanetCategoryChannel category, bool value)
        {
            SetOpen(category.Id, value);
        }

        public void SetOpen(ulong categoryId, bool value)
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
