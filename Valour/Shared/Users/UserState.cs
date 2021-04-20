using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Users
{
    public class UserState
    {
        public string Name { get; set; }
        public byte Value { get; set; }
        public string Description { get; set; }
        public string ClassName { get; set; }

        public static UserState Automatic = new UserState()
        {
            Name = "Auto",
            Description = "Valour will determine the use state automatically",
            Value = 0,
            ClassName = ""
        };

        public static UserState Offline = new UserState()
        {
            Name = "Offline",
            Description = "This user is not online",
            Value = 1,
            ClassName = "offline"
        };

        public static UserState Away = new UserState()
        {
            Name = "Away",
            Description = "This user is online but not active",
            Value = 2,
            ClassName = "away"
        };

        public static UserState DoNotDisturb = new UserState()
        {
            Name = "Do Not Disturb",
            Description = "This user has notifications disabled",
            Value = 3,
            ClassName = "do-not-disturb"
        };

        public static UserState Online = new UserState()
        {
            Name = "Online",
            Description = "This user is online",
            Value = 4,
            ClassName = "online"
        };

        public static UserState[] States = new UserState[]
        {
            Offline,
            Away,
            DoNotDisturb,
            Online
        };

    }
}
