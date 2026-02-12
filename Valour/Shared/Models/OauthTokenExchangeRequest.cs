namespace Valour.Shared.Models;

public class OauthTokenExchangeRequest
{
    public long ClientId { get; set; }
    public string ClientSecret { get; set; }
    public string GrantType { get; set; }
    public string Code { get; set; }
    public string RedirectUri { get; set; }
    public string? State { get; set; }
}
