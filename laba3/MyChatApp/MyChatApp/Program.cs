using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyChatApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Добавляем сервисы MVC.
builder.Services.AddControllersWithViews();

// Регистрируем SocketService как синглтон, чтобы он был доступен на протяжении работы приложения.
builder.Services.AddSingleton<SocketService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

// Настройка маршрутизации: по умолчанию переходим на Setup
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Chat}/{action=Setup}/{id?}");

app.Run();