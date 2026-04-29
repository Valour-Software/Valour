namespace Valour.Shared.Models;

public enum MediaSafetyHashMatchState
{
    NotChecked = 0,
    NoMatch = 1,
    Matched = 2,
    Error = 3,
    Skipped = 4
}
