using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace proxy
{
    public class Proxy
    {
        public static bool IsRunning = true; // определяет работает ли сервер, используется для ожидания подключения клиента
        public const int BUFFER = 8192; //задает размера буфера для чтения данных из потоков  
        public static IPAddress ipAddress = IPAddress.Any; // адрес на котором прослушивает входяшие соединения и принимает со всех интерфейсов
        public static int Port = 8888; // номер порта на котором сервер слушает входящие подключения

        public static void Start()
        {
            TcpListener tcpListener = new TcpListener(ipAddress, Port); //для прослушивания TCP-соединений по указаному ир и портк
            tcpListener.Start();
            
            while (IsRunning)
            {
                try
                {
                    Socket client = tcpListener.AcceptSocket(); //сервер ожидает пока клиент установит соединение
                    Thread thread = new Thread(() => Listen(client)); // поток обрабатывающий подключение и вызывает с конкретным сокетом
                    thread.IsBackground = true; 
                    thread.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка accept: {ex.Message}");
                }
            }
        }

        public static void Listen(Socket client)
        {
            try
            {
                using (NetworkStream clientStream = new NetworkStream(client))
                {
                    byte[] httpRequest = Receive(clientStream); //считывает данные полученные от клиента и возвращает массив байтов
                    if (httpRequest.Length > 0)
                    {
                        Response(clientStream, httpRequest); //пересылает запрос к целевому серверу и возращает ответ клиенту
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в соединении: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        public static byte[] Receive(NetworkStream netStream) //чтение потока данных от клиента
        {
            byte[] bufData = new byte[BUFFER]; //временный буфер фиксированного размера 
            byte[] data = new byte[BUFFER]; // массив в который копируются полученные данные 
            int receivedBytes, dataBytes = 0;
            do
            { //происходит чтение из потока который возращает количество реально прочитанных байт 
                receivedBytes = netStream.Read(bufData, 0, bufData.Length);
                Array.Copy(bufData, 0, data, dataBytes, receivedBytes);
                dataBytes += receivedBytes; //накапливает общее кол прочитанных байт 
            } while (netStream.DataAvailable && receivedBytes < BUFFER);

            return data;
        }

        public static void Response(NetworkStream clientStream, byte[] httpRequest)
        { //преобрахование запроса в строку
            try
            {
                // Считываем исходный запрос от клиента в строку
                string fullRequest = Encoding.UTF8.GetString(httpRequest);
                string host;
                IPEndPoint ipEnd = GetEndPoint(fullRequest, out host);

                // Если запрос содержит абсолютный URL (например, "GET http://example.com/ HTTP/1.1"),
                // преобразуем его в относительный ("GET / HTTP/1.1")
                string fixedRequest = fullRequest;
                if (fullRequest.StartsWith("GET http", StringComparison.OrdinalIgnoreCase) ||
                    fullRequest.StartsWith("POST http", StringComparison.OrdinalIgnoreCase))
                {
                    fixedRequest = GetRelativePath(fullRequest);
                }
                byte[] fixedRequestBytes = Encoding.UTF8.GetBytes(fixedRequest);

                // Соединяемся с сервером
                using (Socket server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    server.Connect(ipEnd);
                    using (NetworkStream serverStream = new NetworkStream(server))
                    {
                        // Отправляем исправленный HTTP-запрос удалённому серверу
                        serverStream.Write(fixedRequestBytes, 0, fixedRequestBytes.Length);
                        serverStream.Flush();
                        
                        // Получаем ответ
                        byte[] responseBuffer = new byte[BUFFER];
                        int bytesRead;
                        bool headerParsed = false;
                        string statusLine = "";
                        bool logEveryChunk = false;

                        // Аккумулятор для накопления байт заголовка (на случай, если заголовок придёт порциями)
                        List<byte> headerAccumulator = new List<byte>();

                        while ((bytesRead = serverStream.Read(responseBuffer, 0, responseBuffer.Length)) > 0)
                        {
                            // Пересылаем полученные данные клиенту сразу
                            clientStream.Write(responseBuffer, 0, bytesRead);
                            clientStream.Flush();

                            if (!headerParsed)
                            {
                                // Накопление байт заголовка
                                for (int i = 0; i < bytesRead; i++)
                                {
                                    headerAccumulator.Add(responseBuffer[i]);
                                }

                                // Пробуем декодировать накопленные байты как строку
                                string headerText = Encoding.UTF8.GetString(headerAccumulator.ToArray());
                                int headerEnd = headerText.IndexOf("\r\n\r\n");
                                if (headerEnd != -1)
                                {
                                    // Заголовок получен полностью
                                    string headerSection = headerText.Substring(0, headerEnd);
                                    string[] headerLines = headerSection.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                                    if (headerLines.Length > 0)
                                    {
                                        statusLine = headerLines[0];
                                    }
                                    headerParsed = true;

                                    // Если в заголовке указан тип контента с аудио, будем логировать каждый чанк
                                    if (headerText.ToLower().Contains("content-type:") &&
                                        headerText.ToLower().Contains("audio"))
                                    {
                                        logEveryChunk = true;
                                    }
                                    Console.WriteLine($"{DateTime.Now} {host} {statusLine}");
                                }
                                else
                                {
                                    // Если заголовок ещё не полностью получен — выводим промежуточную информацию
                                    Console.WriteLine($"{DateTime.Now} {host} — получаю заголовок, прочитано {headerAccumulator.Count} байт");
                                }
                            }
                            else
                            {
                                // Если заголовок уже разобран
                                if (logEveryChunk)
                                {
                                    // Для аудио­потока выводим статус на каждый чанк
                                    Console.WriteLine($"{DateTime.Now} {host} {statusLine}");
                                }
                                // Для остальных ответов дополнительное логирование не производится
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки запроса: {ex.Message}");
            }
        }

        // Метод для извлечения относительного пути из запроса с абсолютным URL
        public static string GetRelativePath(string message)
        {
            // Регулярка ищет подстроку вида "http://example.com"
            Regex regex = new Regex(@"http:\/\/[a-z0-9а-я\.\:]*", RegexOptions.IgnoreCase);
            Match match = regex.Match(message);
            if (match.Success)
            {
                string absoluteHost = match.Value;
                // Заменяем абсолютный URL на пустую строку,
                // оставляя только относительный путь в запросе
                message = message.Replace(absoluteHost, "");
            }
            return message;
        }

        public static IPEndPoint GetEndPoint(string request, out string host)
        { //анализирование HTTP-запрос и извлечение из него заголовка и порта 
            Regex regex = new Regex(@"Host:\s*(?<host>[^:\r\n]+)(:(?<port>\d+))?", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            Match match = regex.Match(request);
            host = match.Groups["host"].Value;
            int port = 0;
            if (!int.TryParse(match.Groups["port"].Value, out port))
            {
                port = 80;
            }
            IPAddress ipHost = Dns.GetHostEntry(host).AddressList[0]; //берем хост и возразаем его записи DNS и выбираем первый ип адрес
            IPEndPoint ipEnd = new IPEndPoint(ipHost, port);

            return ipEnd;
        }

        public static void OutputResponse(byte[] httpResponse, string host)
        { //логирование информации о полученном ответе от сервера
            string response = Encoding.UTF8.GetString(httpResponse); //преобразуем байтов ответ в строку
            string[] bufResponse = response.Split('\r', '\n'); //делем строку на фрагменты по символам
            string code = bufResponse[0].Substring(bufResponse[0].IndexOf(" ") + 1);

            Console.WriteLine(DateTime.Now + " " + host + " " + code);
        }
    }
}