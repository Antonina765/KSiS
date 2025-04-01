using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TcpChatClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Write("Введите имя пользователя: ");
            string username = Console.ReadLine();

            Console.Write("Введите IP-адрес сервера: ");
            string ip = Console.ReadLine();

            Console.Write("Введите порт сервера: ");
            int port = int.Parse(Console.ReadLine());

            using TcpClient client = new TcpClient();

            try
            {
                Console.WriteLine("Подключение к серверу...");
                await client.ConnectAsync(ip, port);
                Console.WriteLine("Подключено!");

                // Отправляем имя пользователя сразу после подключения
                byte[] usernameData = Encoding.UTF8.GetBytes(username + "\n");
                await client.GetStream().WriteAsync(usernameData, 0, usernameData.Length);

                // Запускаем задачу для получения сообщений от сервера
                _ = Task.Run(() => ReceiveMessagesAsync(client));

                // Цикл ввода и отправки сообщений
                while (true)
                {
                    string message = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(message))
                        break;

                    string formattedMessage = $"{username}: {message}";
                    byte[] data = Encoding.UTF8.GetBytes(formattedMessage);
                    await client.GetStream().WriteAsync(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка подключения, сервер отключен");
            }
        }
        
        static async Task ReceiveMessagesAsync(TcpClient client)
        {
            byte[] buffer = new byte[1024];
            try
            {
                while (true)
                {
                    int byteCount = await client.GetStream().ReadAsync(buffer, 0, buffer.Length);
                    if (byteCount == 0)
                        break; // сервер закрыл соединение

                    string message = Encoding.UTF8.GetString(buffer, 0, byteCount);
                    Console.WriteLine(message);

                    // Если получено уведомление о завершении работы сервера,
                    // выводим сообщение и завершаем программу.
                    if (message.Contains("Сервер закрыт"))
                    {
                        Console.WriteLine("Сервер остановлен. Программа будет завершена.");
                        // Необходимо дать время на вывод сообщения, если нужно.
                        await Task.Delay(500);
                        Environment.Exit(0);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка получения сообщения: " + ex.Message);
            }
        }
    }
}
