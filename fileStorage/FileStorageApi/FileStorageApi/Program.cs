using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Регистрируем MVC с поддержкой представлений
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Подключаем работу со статическими файлами (например, для CSS, js, изображений)
app.UseStaticFiles();

// Настраиваем маршрутизацию
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.Run();