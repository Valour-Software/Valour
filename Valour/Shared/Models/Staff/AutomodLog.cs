namespace Valour.Shared.Models.Staff;

public interface ISharedAutomodLog : ISharedPlanetModel<Guid>
{
    Guid TriggerId { get; set; }
    long MemberId { get; set; }
    long? MessageId { get; set; }
    DateTime TimeTriggered { get; set; }
}

