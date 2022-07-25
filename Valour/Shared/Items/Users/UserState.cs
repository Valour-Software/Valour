using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Users;

public class UserState
{
    public string Name { get; set; }

    public byte Value { get; set; }

    public string Description { get; set; }

    public string CssClassName { get; set; }

    public static UserState Automatic = new UserState()
    {
        Name = "Auto",
        Description = "Valour will determine the user state automatically",
        Value = 0,
        CssClassName = ""
    };

    public static UserState Offline = new UserState()
    {
        Name = "Offline",
        Description = "This user is not online",
        Value = 1,
        CssClassName = "offline"
    };

    public static UserState Away = new UserState()
    {
        Name = "Away",
        Description = "This user is online but not active",
        Value = 2,
        CssClassName = "away"
    };

    public static UserState DoNotDisturb = new UserState()
    {
        Name = "Do Not Disturb",
        Description = "This user has notifications disabled",
        Value = 3,
        CssClassName = "do-not-disturb"
    };

    public static UserState Online = new UserState()
    {
        Name = "Online",
        Description = "This user is online",
        Value = 4,
        CssClassName = "online"
    };

    public static UserState[] States = new UserState[]
    {
        Automatic,
        Offline,
        Away,
        DoNotDisturb,
        Online
    };

}

