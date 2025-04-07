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
                string request = Encoding.UTF8.GetString(httpRequest); //преобразование массива байт полученного от клиента в строку
                string host;
                IPEndPoint ipEnd = GetEndPoint(request, out host); // извлекает из запроса заголовок и ип адрес целевого сервера (DNS-разрешение хоста)
        
                using (Socket server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                { //установка соединенияя с целевым сервером (новый сокет с параметрами) и происходит подклбчение
                    server.Connect(ipEnd);
                    using (NetworkStream serverStream = new NetworkStream(server))
                    { //новое соединение с сервером
                        // исходный HTTP-запрос (как массив байтов) отправляется на целевой сервер
                        serverStream.Write(httpRequest, 0, httpRequest.Length);
                
                        // Получаем ответ
                        byte[] responseBuffer = new byte[BUFFER];
                        int bytesRead;
                        bool headerLogged = false;
                        while ((bytesRead = serverStream.Read(responseBuffer, 0, responseBuffer.Length)) > 0)
                        {
                            // каждая порция данных сразу же пересылаем клиенту
                            clientStream.Write(responseBuffer, 0, bytesRead);
                            //логируем только заголовок ответа (один раз)
                            if (!headerLogged)
                            {
                                try
                                {
                                    // Попытка декодировать только первую порцию данных как текст
                                    string responseText = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
                                    int headerEnd = responseText.IndexOf("\r\n\r\n");
                                    string headerSection = headerEnd > 0
                                        ? responseText.Substring(0, headerEnd)
                                        : responseText;

                                    // Берём первую строку заголовка (например: "HTTP/1.1 200 OK")
                                    string firstLine = headerSection.Split(new string[] { "\r\n" }, StringSplitOptions.None)[0];
                                    Console.WriteLine($"{DateTime.Now} {host} {firstLine}");
                                    headerLogged = true;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Ошибка при разборе заголовка: " + ex.Message);
                                }
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

        public static string GetRelativePath(string message)
        {
            Regex regex = new Regex(@"http:\/\/[a-z0-9а-я\.\:]*");
            Match match = regex.Match(message);
            string host = match.Value;
            message = message.Replace(host, "");
            
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