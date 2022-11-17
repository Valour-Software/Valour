namespace Valour.Web.Models;

public class AuthorizeViewModel
{
    public string ResponseType { get; set; }
    public ulong ClientId { get; set; }
    public string RedirectUrl { get; set; }
    public ulong Scope {  get; set; }
}