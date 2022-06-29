using System.Text.Json.Serialization;
using Valour.Shared.Items;

namespace Valour.Shared.Categories
{
    /// <summary>
    /// This class allows for information about item contents and ordering within a category
    /// to be easily sent to the server
    /// </summary>
    public class CategoryContentData
    {
        public ulong Id { get; set; }
        public ushort Position { get; set; }
    }
}
