using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Valour.Web.Models;

namespace Valour.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }
    
    [HttpGet("/userCount")]
    public IActionResult UserCount()
    {
        return View();
    }
    
    [HttpGet("/faq")]
    public IActionResult Faq()
    {
        return View();
    }

    [HttpGet("/privacy")]
    public IActionResult Privacy()
    {
        return View();
    }

    [HttpGet("/terms")]
    public IActionResult Terms()
    {
        return View();
    }

    [HttpGet("/rules")]
    public IActionResult Rules()
    {
        return View();
    }

    [HttpGet("/rules/economy")]
    public IActionResult EconomyRules()
    {
        return View();
    }

    [HttpGet("/delete-account")]
    public IActionResult DeleteAccount()
    {
        return View();
    }

    [HttpGet("/texas")]
    public IActionResult Texas()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
