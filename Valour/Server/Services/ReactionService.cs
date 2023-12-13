namespace Valour.Server.Services;

public class ReactionService
{
    private readonly ValourDB _db;
    private readonly ILogger<ReactionService> _logger;
    
    public ReactionService(ValourDB db, ILogger<ReactionService> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    /// <summary>
    /// Returns the reaction with the given id
    /// </summary>
    /// <param name="id">The id of the reaction</param>
    public async Task<Reaction> GetReaction(long id) => 
        (await _db.Reactions.FindAsync(id)).ToModel();
    
    /// <summary>
    /// Returns all of the reactions for the given message id
    /// </summary>
    /// <param name="messageId">The id of the message</param>
    /// <returns>A list of reactions</returns>
    public async Task<List<Reaction>> GetReactionsForMessage(long messageId) =>
        await _db.Reactions.Where(x => x.MessageId == messageId)
            .Select(x => x.ToModel()).ToListAsync();
    
}