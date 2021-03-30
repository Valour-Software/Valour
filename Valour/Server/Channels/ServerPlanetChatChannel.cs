using AutoMapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Users;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Shared.Oauth;
using Valour.Server.Roles;
using Valour.Server.Users;
using Valour.Shared.Channels;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using Valour.Shared.Roles;
using Valour.Shared.Users;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
using Valour.Shared;

namespace Valour.Server.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class exists to add server funtionality to the Planet Chat Channel
    /// class. It does not, and should not, have any extra fields or properties.
    /// Just helper methods.
    /// </summary>
    public class ServerPlanetChatChannel : PlanetChatChannel, IServerChannelListItem
    {

        [ForeignKey("Planet_Id")]
        [JsonIgnore]
        public virtual ServerPlanet Planet { get; set; }

        /// <summary>
        /// Returns the generic planet chat channel object
        /// </summary>
        [JsonIgnore]
        public ServerPlanetChatChannel PlanetChatChannel
        {
            get
            {
                return (ServerPlanetChatChannel)this;
            }
        }

        /// <summary>
        /// Returns a ServerPlanetChatChannel using a PlanetChatChannel as a base
        /// </summary>
        public static ServerPlanetChatChannel FromBase(PlanetChatChannel channel)
        {
            return MappingManager.Mapper.Map<ServerPlanetChatChannel>(channel);
        }

        /// <summary>
        /// Retrieves a ServerPlanetChatChannel for the given id
        /// </summary>
        public static async Task<ServerPlanetChatChannel> FindAsync(ulong id)
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                PlanetChatChannel channel = await db.PlanetChatChannels.FindAsync(id);
                return ServerPlanetChatChannel.FromBase(channel);
            }
        }

        public async Task<bool> HasPermission(ServerPlanetMember member, ChatChannelPermission permission)
        {
            var roles = await member.GetRolesAsync();

            // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
            // If we get an undecided, we continue to the next role down
            foreach (var role in roles)
            {
                var node = await ServerPlanetRole.FromBase(role).GetChannelNodeAsync(this);

                PermissionState state = node.GetPermissionState(permission);

                if (state == PermissionState.Undefined)
                {
                    continue;
                }
                else if (state == PermissionState.True)
                {
                    return true;
                }
                else
                {
                    return false;
                }

            }

            // No roles ever defined behavior: resort to false.
            return false;
        }

        public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

        /// <summary>
        /// Validates that a given name is allowable for a channel
        /// </summary>
        public static TaskResult ValidateName(string name)
        {
            if (name.Length > 32)
            {
                return new TaskResult(false, "Channel names must be 32 characters or less.");
            }

            if (!nameRegex.IsMatch(name))
            {
                return new TaskResult(false, "Channel names may only include letters, numbers, dashes, and underscores.");
            }

            return new TaskResult(true, "The given name is valid.");
        }

        public async Task SetNameAsync(string name, ValourDB db = null)
        {
            bool createdb = false;
            if (db == null) { db = new ValourDB(ValourDB.DBOptions); createdb = true; }

            this.Name = name;

            db.PlanetChatChannels.Update(this);
            await db.SaveChangesAsync();

            if (createdb) { await db.DisposeAsync(); }
        }

        public async Task SetDescriptionAsync(string desc, ValourDB db = null)
        {
            bool createdb = false;
            if (db == null) { db = new ValourDB(ValourDB.DBOptions); createdb = true; }

            this.Description = desc;

            db.PlanetChatChannels.Update(this);
            await db.SaveChangesAsync();

            if (createdb) { await db.DisposeAsync(); }
        }
    }
}
