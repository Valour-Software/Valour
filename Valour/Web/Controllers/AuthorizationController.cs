using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Valour.Server.Database;

namespace Valour.Web.Controllers;

public class AuthorizationController : Controller
{
    private readonly ValourDB _valourDB;

    public AuthorizationController(ValourDB valourDB)
    {
        this._valourDB = valourDB;
    }

    public static void RegisterMinimalRoutes(WebApplication app)
    {
        
    }

    /// <summary>
    /// The Authorize route is sent the oauth response type requested, the client id and permission scope,
    /// and displays the page to log in to authenticate the request.
    /// </summary>

    [HttpGet("authorize")]
    public async Task<IActionResult> AuthorizeAsync(HttpContext context, string responseType, ulong clientId, string redirectUrl, ulong scope)
    {
        // Check if already logged in
        context.Request.Cookies.TryGetValue("Authorization", out var cookie);
        if (cookie is not null)
        {
            
        }


        return View();
    }
}
