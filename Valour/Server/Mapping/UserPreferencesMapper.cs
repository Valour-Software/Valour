namespace Valour.Server.Mapping;

public static class UserPreferencesMapper
{
    public static UserPreferences ToModel(this Valour.Database.UserPreferences prefs)
    {
        if (prefs is null)
            return null;

        return new UserPreferences()
        {
            Id = prefs.Id,
            ErrorReportingState = prefs.ErrorReportingState
        };
    }

    public static Valour.Database.UserPreferences ToDatabase(this UserPreferences prefs)
    {
        if (prefs is null)
            return null;

        return new Valour.Database.UserPreferences()
        {
            Id = prefs.Id,
            ErrorReportingState = prefs.ErrorReportingState
        };
    }
}
