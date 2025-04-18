namespace FileStorage.DTO;

public class UploadFileDTO
{
    // Обратите внимание, что свойство "file" должно иметь тип IFormFile
    public IFormFile file { get; set; }
    // folderPath или path для указания в каком каталоге будет сохранён файл
    public string path { get; set; }
}
