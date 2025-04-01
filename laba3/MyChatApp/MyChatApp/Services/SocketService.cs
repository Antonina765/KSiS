using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MyChatApp.Services;

public class SocketService
{
    private TcpListener _listener;
    private readonly ConcurrentBag<TcpClient> _clients = new ConcurrentBag<TcpClient>();
        
    // Очередь сообщений, которую читаем из веб-интерфейса (для простоты – все накопленные сообщения)
    public ConcurrentQueue<string> ChatMessages { get; private set; } = new ConcurrentQueue<string>();
    public bool IsRunning { get; private set; } = false;

    // Запуск сервера с вводимыми IP-адресом и портом
    public async Task StartServerAsync(IPAddress ipAddress, int port) 
    { 
        try 
        { 
            _listener = new TcpListener(ipAddress, port); 
            _listener.Start(); 
            IsRunning = true; 
            Console.WriteLine($"Сервер запущен на {ipAddress}:{port}"); 
            _ = AcceptClientsAsync();
        }
        catch (SocketException ex) 
        { 
            Console.WriteLine("Ошибка при старте сервера: " + ex.Message); 
            throw; // Чтобы контроллер смог вывести сообщение об ошибке
        }
    }

    // Асинхронное принятие клиентов
    private async Task AcceptClientsAsync() 
    { 
        while (IsRunning) 
        { 
            try 
            { 
                TcpClient client = await _listener.AcceptTcpClientAsync(); 
                _clients.Add(client); 
                _ = HandleClientAsync(client);
            }
            catch (Exception ex) 
            { 
                Console.WriteLine("AcceptClientsAsync: " + ex.Message); 
                break;
            }
        }
    }

    // Обработка клиента: чтение данных и рассылка сообщений
    private async Task HandleClientAsync(TcpClient client)
    { 
        byte[] buffer = new byte[1024]; 
        NetworkStream stream = client.GetStream();
        
        while (IsRunning) 
        { 
            int byteCount = 0; 
            try 
            { 
                byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex) 
            { 
                Console.WriteLine("Ошибка чтения клиента: " + ex.Message); 
                break;
            }
            
            if (byteCount == 0) 
                break; // Клиент отключился

            string message = Encoding.UTF8.GetString(buffer, 0, byteCount); 
            string fullMessage = $"[Remote] {message}"; 
            ChatMessages.Enqueue(fullMessage);

            // Рассылаем сообщение всем клиентам, кроме отправителя
            await BroadcastMessageAsync(message, client);
        } 
        client.Close();
    }

    // Рассылка сообщения всем подключённым клиентам (опционально исключая отправителя)
    public async Task BroadcastMessageAsync(string message, TcpClient sender = null) 
    { 
        byte[] data = Encoding.UTF8.GetBytes(message); 
        foreach (var client in _clients) 
        { 
            if (client == sender) 
                continue;
            try 
            { 
                NetworkStream stream = client.GetStream(); 
                await stream.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex) 
            { 
                Console.WriteLine("Ошибка отправки клиенту: " + ex.Message);
            }
        }
    }
    
    // Метод, вызываемый из веб-интерфейса – отправка сообщения от "сервера"
    public async Task SendMessageAsync(string message) 
    { 
        string fullMessage = $"[Server] {message}"; 
        ChatMessages.Enqueue(fullMessage); 
        await BroadcastMessageAsync(message);
    }

    // Остановка сервера и закрытие подключений
    // Вместо существующего метода StopServer добавляем асинхронную версию
    public async Task StopServerAsync()
    {
        // Устанавливаем флаг завершения работы, чтобы все циклы обработки перестали чтение/приём данных
        IsRunning = false;
    
        // Формируем сообщение об остановке сервера.
        string shutdownMessage = "[Server] Сервер закрыт! Соединение будет завершено.";
    
        // Рассылаем клиентам сообщение об остановке
        await BroadcastMessageAsync(shutdownMessage);
    
        // Останавливаем прием новых подключений
        _listener.Stop();
    
        // Закрываем все соединения с клиентами
        foreach (var client in _clients)
        {
            try
            {
                client.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка закрытия клиента: " + ex.Message);
            }
        }
    }

}
