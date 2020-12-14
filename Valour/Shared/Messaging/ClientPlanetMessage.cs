using Valour.Shared.Users;

namespace Valour.Shared.Messaging
{
    public class ClientPlanetMessage : ClientMessage
    {
        /// <summary>
        /// The author of this message
        /// </summary>
        public ClientPlanetUser Author { get; set; }
    }
}
