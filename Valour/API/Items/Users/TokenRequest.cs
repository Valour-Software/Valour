using System.Text.Json.Serialization;
using Valour.Shared.Items.Users;

namespace Valour.Api.Items.Users;

public class TokenRequest : TokenRequestBase
{
    public TokenRequest(string email, string password)
    {
        Email = email;
        Password = password;
    }
}
