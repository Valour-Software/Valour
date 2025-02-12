namespace Valour.Shared.Models;

public class RoleMembershipHashChange
{
    public long PlanetId { get; set; }
    
    // For bulk replacements, where everyone with the old hash should
    // be replaced with the new hash. Generally used for role deletion.
    public long[] ReplacedOldHashes { get; set; }
    public long[] ReplacedNewHashes { get; set; }
    
    // Any new combinations of roles that have been added,
    // with the roles they contain
    public Dictionary<long, long[]> AddedHashes { get; set; }
}