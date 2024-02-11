using Valour.Sdk.Models.Economy;

namespace Valour.Sdk.Models;

public class EcoReceipt
{
    public long UserFromId { get; set; }
    public long UserToId { get; set; }
    public Currency Currency { get; set; }
    public decimal Amount { get; set; }
    public long AccountFromId { get; set; }
    public long AccountToId { get; set; }
    public string TransactionId { get; set; }
    public string AccountFromName { get; set; }
    public string AccountToName { get; set; }
    public DateTime TimeStamp { get; set; }
}
