using Microsoft.AspNetCore.Mvc;
using MyChatApp.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace MyChatApp.Controllers
{
    public class ChatController : Controller
    {
        private readonly SocketService _socketService;

        public ChatController(SocketService socketService)
        {
            _socketService = socketService;
        }

        // Страница для ввода IP и порта (настройка сервера)
        public IActionResult Setup()
        {
            return View();
        }

        // Принимаем POST-запрос для запуска сервера
        [HttpPost]
        public async Task<IActionResult> StartServer(string ipAddress, int port)
        {
            try
            {
                if (!IPAddress.TryParse(ipAddress, out IPAddress ip))
                {
                    ModelState.AddModelError("", "Неверный IP адрес.");
                    return View("Setup");
                }
                await _socketService.StartServerAsync(ip, port);
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Ошибка запуска сервера: {ex.Message}");
                return View("Setup");
            }
        }

        // Страница самого чата
        public IActionResult Index()
        {
            return View();
        }

        // API: Получение накопленных сообщений (для AJAX polling)
        [HttpGet]
        public IActionResult GetMessages()
        {
            var messages = new List<string>();
            while (_socketService.ChatMessages.TryDequeue(out string message))
            {
                messages.Add(message);
            }
            return Json(messages);
        }

        // API: Отправка сообщения из веб-интерфейса
        [HttpPost]
        public async Task<IActionResult> SendMessage(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                await _socketService.SendMessageAsync(message);
            }
            return Ok();
        }
    }
}
