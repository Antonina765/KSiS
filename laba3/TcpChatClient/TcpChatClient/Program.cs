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

                // Запускаем задачу для получения сообщений от сервера
                _ = Task.Run(() => ReceiveMessagesAsync(client));

                // Цикл ввода и отправки сообщений
                while (true)
                {
                    string message = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(message))
                        break;

                    byte[] data = Encoding.UTF8.GetBytes(message);
                    await client.GetStream().WriteAsync(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка подключения: " + ex.Message);
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
                        break; // сервер разорвал соединение

                    string message = Encoding.UTF8.GetString(buffer, 0, byteCount);
                    Console.WriteLine("Получено: " + message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка получения сообщения: " + ex.Message);
            }
        }
    }
}
