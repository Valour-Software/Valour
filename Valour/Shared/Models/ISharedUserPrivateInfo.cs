namespace Valour.Shared.Models;

public enum Locality
{
    EuropeanUnion, // This is first so that the fallback is the strictest
    General,
}

public interface ISharedUserPrivateInfo
{
    string Email { get; set; }
    bool Verified { get; set; }
    long UserId { get; set; }
    DateTime BirthDate { get; set; }
    Locality Locality { get; set; }
}