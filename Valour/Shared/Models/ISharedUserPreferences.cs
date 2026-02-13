namespace Valour.Shared.Models;

public interface ISharedUserPreferences : ISharedModel<long>
{
    ErrorReportingState ErrorReportingState { get; set; }
}
