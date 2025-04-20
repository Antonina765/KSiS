using Microsoft.AspNetCore.Mvc;

namespace FileStorageApi.Controllers
{
    public class HomeController : Controller
    {
        // Главная страница с интерфейсом для управления файловым хранилищем
        public IActionResult Index()
        {
            return View();
        }
    }
}