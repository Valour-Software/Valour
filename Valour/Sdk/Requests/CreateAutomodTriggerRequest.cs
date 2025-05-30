namespace Valour.Sdk.Requests;

using System.Collections.Generic;
using Valour.Sdk.Models;

public class CreateAutomodTriggerRequest
{
    public AutomodTrigger Trigger { get; set; }
    public List<AutomodAction> Actions { get; set; } = new();
}
