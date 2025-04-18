using FileStorage.DTO;
using FileStorage.Services;
using Microsoft.AspNetCore.Mvc;

namespace FileStorage.Controllers;

[ApiController]
[Route("[controller]")]
public class FilesController : ControllerBase
{
    private readonly IFilesService _fileService;
    public FilesController(IFilesService fileService)
    {
        _fileService = fileService;
    }

    [HttpPut]
    public async Task<IActionResult> UploadWithRewrite([FromForm] UploadFileDTO file)
    {
        await _fileService.UploadWithRewrite(file);
        return Ok();
    }

    [HttpHead("{*path}")]
    public async Task<IActionResult> HeadInformation(string path)
    {
        var fileInfo = await _fileService.HeadInformation(path);

        if (!fileInfo.Exists)
        {
            return NotFound("File not found");
        }

        Response.Headers["Content-Name"] = fileInfo.Name;
        Response.Headers["Content-Length"] = fileInfo.Size.ToString();
        Response.Headers["Last-Modified"] = fileInfo.LastModified.ToString("R");

        return Ok();
    }

    [HttpDelete("{*path}")]
    public async Task<IActionResult> Delete(string path)
    {
        await _fileService.Delete(path);
        return Ok();
    }
}