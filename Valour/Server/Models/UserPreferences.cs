using Valour.Shared.Models;

namespace Valour.Server.Models;

public class UserPreferences : ISharedUserPreferences
{
    public long Id { get; set; }
    public ErrorReportingState ErrorReportingState { get; set; }
}
