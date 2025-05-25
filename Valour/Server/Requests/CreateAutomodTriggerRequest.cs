namespace Valour.Server.Requests;

using Valour.Server.Models;
using System.Collections.Generic;

public class CreateAutomodTriggerRequest
{
    public AutomodTrigger Trigger { get; set; }
    public List<AutomodAction> Actions { get; set; } = new();
}
