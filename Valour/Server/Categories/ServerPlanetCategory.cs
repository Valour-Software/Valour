using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Planets;
using Valour.Shared;
using Valour.Shared.Categories;

namespace Valour.Server.Categories
{
    public class ServerPlanetCategory : PlanetCategory, IServerChannelListItem
    {

        [JsonIgnore]
        [ForeignKey("Planet_Id")]
        public virtual ServerPlanet Planet { get; set; }

        [JsonIgnore]
        public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

        /// <summary>
        /// Validates that a given name is allowable for a server
        /// </summary>
        public static TaskResult ValidateName(string name)
        {
            if (name.Length > 32)
            {
                return new TaskResult(false, "Planet names must be 32 characters or less.");
            }

            if (!nameRegex.IsMatch(name))
            {
                return new TaskResult(false, "Planet names may only include letters, numbers, dashes, and underscores.");
            }

            return new TaskResult(true, "The given name is valid.");
        }

        public async Task SetNameAsync(string name, ValourDB db = null)
        {
            bool createdb = false;
            if (db == null) { db = new ValourDB(ValourDB.DBOptions); createdb = true; }

            this.Name = name;

            db.PlanetCategories.Update(this);
            await db.SaveChangesAsync();

            if (createdb) { await db.DisposeAsync(); }
        }

        public async Task SetDescriptionAsync(string desc, ValourDB db = null)
        {
            bool createdb = false;
            if (db == null) { db = new ValourDB(ValourDB.DBOptions); createdb = true; }

            this.Description = desc;

            db.PlanetCategories.Update(this);
            await db.SaveChangesAsync();

            if (createdb) { await db.DisposeAsync(); }
        }
    }
}
