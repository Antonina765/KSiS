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
        public static bool IsRunning = true;
        public const int BUFFER = 8192;
        public static IPAddress ipAddress = IPAddress.Any;
        public static int Port = 8888;

        public static void Start()
        {
            TcpListener tcpListener = new TcpListener(ipAddress, Port);
            tcpListener.Start();
            
            while (IsRunning)
            {
                try
                {
                    Socket client = tcpListener.AcceptSocket();
                    Thread thread = new Thread(() => Listen(client));
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
                    byte[] httpRequest = Receive(clientStream);
                    if (httpRequest.Length > 0)
                    {
                        Response(clientStream, httpRequest);
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

        public static byte[] Receive(NetworkStream netStream)
        {
            byte[] bufData = new byte[BUFFER];
            byte[] data = new byte[BUFFER];
            int receivedBytes, dataBytes = 0;
            do
            {
                receivedBytes = netStream.Read(bufData, 0, bufData.Length);
                Array.Copy(bufData, 0, data, dataBytes, receivedBytes);
                dataBytes += receivedBytes;
            } while (netStream.DataAvailable && receivedBytes < BUFFER);

            return data;
        }

        public static void Response(NetworkStream clientStream, byte[] httpRequest)
        {
            try
            {
                string request = Encoding.UTF8.GetString(httpRequest);
                string host;
                IPEndPoint ipEnd = GetEndPoint(request, out host);
        
                using (Socket server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    server.Connect(ipEnd);
                    using (NetworkStream serverStream = new NetworkStream(server))
                    {
                        // Отправляем запрос на целевой сервер
                        serverStream.Write(httpRequest, 0, httpRequest.Length);
                
                        // Получаем ответ
                        byte[] responseBuffer = new byte[BUFFER];
                        int bytesRead;
                        while ((bytesRead = serverStream.Read(responseBuffer, 0, responseBuffer.Length)) > 0)
                        {
                            // Пересылаем клиенту
                            clientStream.Write(responseBuffer, 0, bytesRead);
                            OutputResponse(responseBuffer, host);
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
        {
            Regex regex = new Regex(@"Host: (((?<host>.+?):(?<port>\d+?))|(?<host>.+?))\s+", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            Match match = regex.Match(request);
            host = match.Groups["host"].Value;
            int port = 0;
            if (!int.TryParse(match.Groups["port"].Value, out port))
            {
                port = 80;
            }
            IPAddress ipHost = Dns.GetHostEntry(host).AddressList[0];
            IPEndPoint ipEnd = new IPEndPoint(ipHost, port);

            return ipEnd;
        }

        public static void OutputResponse(byte[] httpResponse, string host)
        {
            string response = Encoding.UTF8.GetString(httpResponse);
            string[] bufResponse = response.Split('\r', '\n');
            string code = bufResponse[0].Substring(bufResponse[0].IndexOf(" ") + 1);

            Console.WriteLine(DateTime.Now + " " + host + " " + code);
        }
    }
}