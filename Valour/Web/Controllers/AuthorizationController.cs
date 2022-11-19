using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;
using Valour.Web.Models;

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
    /// Returns the auth token from the given HttpContext
    /// </summary>
    public async Task<AuthToken> GetAuthToken(HttpContext context)
    {
        // Check if already logged in
        context.Request.Cookies.TryGetValue("token", out var cookieToken);

        if (string.IsNullOrWhiteSpace(cookieToken))
            return null;

        return await _valourDB.AuthTokens.FindAsync(cookieToken);        
    } 

    
    [HttpGet("login")]
    public async Task<IActionResult> Login(string redirectUrl = "/")
    {
        var token = await GetAuthToken(HttpContext);

        // If we're already logged in, redirect to the target
        if (token is not null)
            return RedirectPermanent(redirectUrl);

        LoginViewModel model = new();

        // Otherwise, show login page
        return View(model);
    }


    /// <summary>
    /// The Authorize route is sent the oauth response type requested, the client id and permission scope,
    /// and displays the page to log in to authenticate the request.
    /// </summary>

    [HttpGet("authorize")]
    public async Task<IActionResult> AuthorizeAsync(string responseType, ulong clientId, string redirectUrl, ulong scope)
    {
        // If the token is not there or is invalid, send to login page with a redirect back to here
        var token = await GetAuthToken(HttpContext);
        if (token is null)
            return RedirectToAction("login", new { redirectUrl = HttpContext.Request.GetEncodedUrl() });

        // Show authorize dialog

        return View();
    }
}
