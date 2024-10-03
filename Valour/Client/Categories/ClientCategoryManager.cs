using System.Collections.Concurrent;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Client.Categories
{
    public class ClientCategoryManager
    {
        private readonly ConcurrentDictionary<long, bool> _categoryOpenStates = new ();

        public bool IsOpen(Channel channel)
        {
            if (channel.ChannelType != ChannelTypeEnum.PlanetCategory)
                return false;
            
            return IsOpen(channel.Id);
        }

        public bool IsOpen(long categoryId)
        {
            if (_categoryOpenStates.TryGetValue(categoryId, out var result))
            {
                return result;
            }

            return true;
        }

        public void SetOpen(Channel category, bool value)
        {
            SetOpen(category.Id, value);
        }

        public void SetOpen(long categoryId, bool value)
        {
            if (!_categoryOpenStates.ContainsKey(categoryId))
            {
                _categoryOpenStates.TryAdd(categoryId, value);

                //Console.WriteLine($"Set new state to {value}");

                return;
            }

            _categoryOpenStates[categoryId] = value;

            //Console.WriteLine($"Set state to {value}");
        }
    }
}
