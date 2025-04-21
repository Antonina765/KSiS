using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileStorageApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace FileStorageApi.Controllers
{
    // Используем префикс "storage" и catch-all параметр для произвольной вложенности
    [Route("storage")]
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

        [HttpGet("all")]
        public IActionResult GetAllFiles()
        {
            var files = Directory.GetFiles(_storageRoot, "*.*", SearchOption.AllDirectories)
                .Select(file => new 
                {
                    Name = Path.GetFileName(file),
                    Path = file.Substring(_storageRoot.Length + 1)
                });
            return Json(files);
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
        [HttpGet("{*filePath}")]
        public IActionResult Get(string filePath)
        {
            string fullPath = GetSafePath(filePath);
            
            // Если запрошен конкретный файл, возвращаем тоже имя и содержимое
            if (System.IO.File.Exists(fullPath))
            {
                try
                {
                    string content = System.IO.File.ReadAllText(fullPath);
                    var fileResult = new
                    {
                        Name = Path.GetFileName(fullPath),
                        Content = content
                    };
                    return Json(fileResult);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Ошибка при чтении файла: {ex.Message}");
                }
            }
            // Если запрошен каталог, возвращаем список директорий и список файлов с содержимым
            else if (Directory.Exists(fullPath))
            {
                var directories = Directory.GetDirectories(fullPath)
                                           .Select(dir => Path.GetFileName(dir));
                var files = Directory.GetFiles(fullPath)
                                     .Select(file =>
                                     {
                                         string content = string.Empty;
                                         try
                                         {
                                             // Чтение содержимого файла
                                             content = System.IO.File.ReadAllText(file);
                                         }
                                         catch (Exception ex)
                                         {
                                             content = $"Ошибка при чтении: {ex.Message}";
                                         }
                                         return new
                                         {
                                             Name = Path.GetFileName(file),
                                             Content = content
                                         };
                                     });
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
        [HttpHead("{*filePath}")]
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
        [HttpPut("{*filePath}")]
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
        [HttpDelete("{*filePath}")]
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
        
        [HttpPatch("rename")]
        public IActionResult RenameFile([FromQuery] string filePath, [FromBody] RenameRequest request)
        {
            if (string.IsNullOrWhiteSpace(filePath) || request == null || string.IsNullOrWhiteSpace(request.NewName))
            {
                return BadRequest("Путь или новое имя не указаны.");
            }

            string fullPath = GetSafePath(filePath);
            if (System.IO.File.Exists(fullPath))
            {
                string newPath = Path.Combine(Path.GetDirectoryName(fullPath), request.NewName);
                try
                {
                    System.IO.File.Move(fullPath, newPath);
                    return Ok("Файл успешно переименован.");
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Ошибка при переименовании: {ex.Message}");
                }
            }
            return NotFound("Файл не найден.");
        }

        [HttpPost("edit")]
        public async Task<IActionResult> EditFile([FromQuery] string filePath)
        {
            string fullPath = GetSafePath(filePath);

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound("Файл не найден.");
            }

            using (var fs = new FileStream(fullPath, FileMode.Truncate, FileAccess.Write))
            {
                await Request.Body.CopyToAsync(fs);
            }

            return Ok("Содержимое файла успешно изменено.");
        }
    }
}
