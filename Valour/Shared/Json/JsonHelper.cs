using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Json
{
    /// <summary>
    /// Used for some optimizations
    /// </summary>
    public static class JsonHelper
    {
        public static JsonSerializer OptimizedSerializer;

        static JsonHelper()
        {
            OptimizedSerializer = new JsonSerializer
            {
                // This heavily increases bandwidth performance by not bothering to write empty
                // fields into objects
                NullValueHandling = NullValueHandling.Ignore,

                // Save bandwidth by not bothering with newlines and extra formatting data
                Formatting = Formatting.None
            };
        }
    }
}
