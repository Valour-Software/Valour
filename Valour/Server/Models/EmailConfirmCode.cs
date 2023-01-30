using System.ComponentModel.DataAnnotations;

namespace Valour.Server.Models;

public class EmailConfirmCode
{
    public string Code { get; set; }
    public long UserId { get; set; }
}
