using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FileStorage.Models;

namespace FileStorage.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}