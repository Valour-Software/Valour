using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Web.Models;

namespace Valour.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ValourDB _db;

    public HomeController(ILogger<HomeController> logger, ValourDB db)
    {
        _logger = logger;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        HomeModel model;
        try
        {
            model = new HomeModel((await _db.Users.CountAsync()).ToString(), 
                                  (await _db.Planets.CountAsync()).ToString());
        }
        catch (Exception ex)
        {
            // If we fail to reach the database we do not want the homepage to break. Instead use nice words
            // instead of user counts.
            model = new HomeModel("Awesome", "Extraordinary");
            _logger.LogError(ex, "Error in homepage");
        }

        return View(model);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

