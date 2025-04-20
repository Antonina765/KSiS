using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace FileStorageApi.Controllers
{
    // Используем префикс "storage" и catch-all параметр для произвольной вложенности
    [Route("storage/{*filePath}")]
    public class FileStorageController : Controller
    {
        // Корневой каталог файлового хранилища (папка FileStorage в корне приложения)
        private readonly string _storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "FileStorage");

        public FileStorageController()
        {
            // Если хранилище не создано, создаём его
            if (!Directory.Exists(_storageRoot))
            {
                Directory.CreateDirectory(_storageRoot);
            }
        }

        /// <summary>
        /// Возвращает безопасный абсолютный путь для заданного относительного пути.
        /// Позволяет избежать доступа за пределы каталога хранилища.
        /// </summary>
        private string GetSafePath(string relativePath)
        {
            relativePath = relativePath ?? string.Empty;
            string fullPath = Path.GetFullPath(Path.Combine(_storageRoot, relativePath));
            if (!fullPath.StartsWith(_storageRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Недопустимый путь.");
            }
            return fullPath;
        }

        /// <summary>
        /// GET:
        /// - Если переданный путь указывает на файл, возвращает содержимое файла.
        /// - Если путь — каталог, возвращает JSON со списком файлов и подкаталогов.
        /// </summary>
        [HttpGet]
        public IActionResult Get(string filePath)
        {
            string fullPath = GetSafePath(filePath);
            if (System.IO.File.Exists(fullPath))
            {
                return PhysicalFile(fullPath, "application/octet-stream", Path.GetFileName(fullPath));
            }
            else if (Directory.Exists(fullPath))
            {
                var directories = Directory.GetDirectories(fullPath).Select(dir => Path.GetFileName(dir));
                var files = Directory.GetFiles(fullPath).Select(file => Path.GetFileName(file));
                var result = new { Directories = directories, Files = files };

                return Json(result);
            }
            else
            {
                return NotFound("Файл или каталог не найден.");
            }
        }

        /// <summary>
        /// HEAD:
        /// Отправляет информацию о файле через HTTP-заголовки: его размер и дату последнего изменения.
        /// Заголовки: X-File-Size и X-Last-Modified.
        /// </summary>
        [HttpHead]
        public IActionResult Head(string filePath)
        {
            string fullPath = GetSafePath(filePath);
            if (System.IO.File.Exists(fullPath))
            {
                FileInfo fi = new FileInfo(fullPath);
                Response.Headers["X-File-Size"] = fi.Length.ToString();
                Response.Headers["X-Last-Modified"] = fi.LastWriteTimeUtc.ToString("R"); // формат RFC1123
                return Ok();
            }
            return NotFound("Файл не найден.");
        }

        /// <summary>
        /// PUT:
        /// Загрузить (с перезаписью) файл по указанному пути.
        /// Тело запроса должно содержать данные файла.
        /// </summary>
        [HttpPut]
        public async Task<IActionResult> Put(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return BadRequest("Не указан путь файла.");
            }

            string fullPath = GetSafePath(filePath);
            // Обеспечиваем наличие каталога для файла
            string directoryPath = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // Записываем контент запроса в файл (перезапись, если файл уже существует)
            using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                await Request.Body.CopyToAsync(fs);
            }
            return Ok("Файл успешно загружен.");
        }

        /// <summary>
        /// DELETE:
        /// Удаляет файл или каталог (с рекурсивным удалением, если это каталог) из хранилища.
        /// В случае успеха возвращается статус 204 No Content.
        /// </summary>
        [HttpDelete]
        public IActionResult Delete(string filePath)
        {
            string fullPath = GetSafePath(filePath);
            try
            {
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                    return NoContent();
                }
                else if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, recursive: true);
                    return NoContent();
                }
                else
                {
                    return NotFound("Файл или каталог не найдены.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка при удалении: {ex.Message}");
            }
        }
    }
}
