using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Valour.Web.Models;

namespace Valour.Web.Controllers;

public class FAQController : Controller
{
    private readonly ILogger<FAQController> _logger;

    public FAQController(ILogger<FAQController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View("FAQ");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}