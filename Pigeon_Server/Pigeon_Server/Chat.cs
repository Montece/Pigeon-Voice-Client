using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Pigeon_Server
{
    public class Chat : IDisposable
    {
        public const int SERVER_PORT = 52535;
        public const int CLIENT_PORT = 52534;
        public const int CLIENT_FILE_PORT = 52533;

        public const int FILE_PACKAGE_SIZE = 1024;

        ServerWindow window;
        Thread recieverThread;
        public static UdpClient udp;
        static IPEndPoint clientEnd;

        public Chat(ServerWindow sw)
        {
            Database.Init();
            window = sw;
            int a = Database.GetUsersCount();
            window.AddLog("SERVER", "Чат загружен.");
            string text1 = Database.GetUsersCount() > 0 ? $"База данных пользователей загружена ({Database.GetUsersCount()})": $"База данных пользователей не была загружена, создана новая.";
            window.AddLog("SERVER", text1);
            string text2 = Database.GetServersCount() > 0 ? $"База данных серверов загружена ({Database.GetServersCount()})" : $"База данных серверов не была загружена, создана новая.";
            window.AddLog("SERVER", text2);
            window.ShowUsers_async();
            window.ShowServers_async();
            recieverThread = new Thread(Reciever) { Name = "Reciever" };
            recieverThread.Start();
        }

        void Reciever()
        {
            udp = new UdpClient(SERVER_PORT);

            List<byte> request = new List<byte>();
            int RequestType;
            ClientCommand JSONcommand;

            while (true)
            {
                request.Clear();
                RequestType = 0;
                JSONcommand = null;

                try
                {
                    request = udp.Receive(ref clientEnd).ToList();
                }
                catch 
                {
                    continue;
                }

                RequestType = request[0];
                if (RequestType == 0)
                {
                    request.RemoveAt(0);
                    try
                    {
                        JSONcommand = JsonConvert.DeserializeObject<ClientCommand>(Encoding.UTF8.GetString(request.ToArray()));
                    }
                    catch
                    {
                        continue;
                    }                 
                }
                else
                if (RequestType == 1)
                {
                    request.RemoveAt(0);
                    int length = request[0];
                    request.RemoveAt(0);
                    StringReader reader = new StringReader(Encoding.UTF8.GetString(request.GetRange(0, length).ToArray()));
                    VoiceCommand command = (VoiceCommand)Database.VoiceXML.Deserialize(reader);
                    request.RemoveRange(0, length);
                    
                    Database.SendVoice(command, request.ToArray());
                    continue;
                }           

                if (JSONcommand == null) continue;
                if (JSONcommand.Sender == null) AddLog_async(clientEnd.Address.ToString(), JSONcommand.CommandID.ToString());
                else AddLog_async(JSONcommand.Sender, JSONcommand.CommandID.ToString());

                switch (JSONcommand.CommandID)
                {
                    case ClientCommands.GetServerVersion: SendCommand(ServerCommands.SendServerVersion, null, ServerWindow.Version); break;
                    case ClientCommands.LogIn:
                        bool LogInResult = (Database.HasUser(JSONcommand.Parameters[0], JSONcommand.Parameters[1]));
                        if (LogInResult)
                        {
                            SendCommand(ServerCommands.SendLogInResult, null, true.ToString(), Database.GetUser(JSONcommand.Parameters[0]).Info.Nickname);
                            Database.SetUserIP(JSONcommand.Parameters[0], clientEnd);
                        }
                        else SendCommand(ServerCommands.SendLogInResult, null, false.ToString(), "");
                        break;
                    case ClientCommands.Register:
                        bool RegisterResult = false;
                        if (!Database.HasUser(JSONcommand.Parameters[0], JSONcommand.Parameters[1]))
                        {
                            RegisterResult = Database.CreateUser(JSONcommand.Parameters[0], JSONcommand.Parameters[1], JSONcommand.Parameters[2], JSONcommand.Parameters[3]);
                        }
                        SendCommand(ServerCommands.SendRegisterResult, null, RegisterResult.ToString());
                        break;
                    case ClientCommands.GetAllServers:
                        string AllServers = Database.GetServers();
                        SendCommand(ServerCommands.SendAllServers, null, AllServers);
                        break;
                    case ClientCommands.ConnectToServer:
                        bool Connected = Database.ConnectToServer(JSONcommand.Parameters[0], JSONcommand.Parameters[1], clientEnd);
                        SendCommand(ServerCommands.ConnectToServerAnswer, null, Connected.ToString());
                        Database.OnConnect(JSONcommand.Parameters[0], JSONcommand.Parameters[1], clientEnd);
                        break;
                    case ClientCommands.LoadHistory:
                        string history = Database.GetServerHistory(JSONcommand.Parameters[0]);
                        SendCommand(ServerCommands.ServerHistory, null, history);
                        break;
                    case ClientCommands.GetServerUsers:
                        string AllUsers = Database.GetUsers(JSONcommand.Parameters[0]);
                        SendCommand(ServerCommands.SendServerUsers, null, AllUsers);
                        break;
                    case ClientCommands.Disconnect:
                        Database.DisconnectFromServer(JSONcommand.Parameters[0], JSONcommand.Parameters[1], clientEnd);
                        Database.OnDisconnect(JSONcommand.Parameters[0], JSONcommand.Parameters[1]);
                        break;
                    case ClientCommands.TextMessage:
                        Database.SendTextMessage(JSONcommand.Parameters[0], JSONcommand.Parameters[1], JSONcommand.Parameters[2]);
                        break;
                    case ClientCommands.GetUpdate:
                        new Thread(UpdateClient).Start();
                        break;
                    case ClientCommands.HasFile:
                        SendHasFile(JSONcommand.Parameters[0]);
                        break;
                    case ClientCommands.DownloadFile:
                        string fp = JSONcommand.Parameters[0];
                        new Thread(() => SendFile(clientEnd, fp)).Start();
                        break;
                    default:
                        AddLog_async("SERVER", "Неизвестная команда " + JSONcommand.CommandID);
                        break;
                }
                window.ShowUsers_async();
                window.ShowServers_async();
            }
        }

        public void SendHasFile(string filename)
        {
            string[] files = Directory.GetFiles(Database.FilesPath, filename);
            bool status = files.Length == 1;
            SendCommand(ServerCommands.SendFileStatus, null, status.ToString(), filename);
        }

        void UpdateClient()
        {
            SendFile(clientEnd, Database.ClientFilePath);
        }

        void SendFile(IPEndPoint end, string path)
        {
            using (TcpClient tcp = new TcpClient())
            {
                try
                {
                    tcp.ReceiveBufferSize = FILE_PACKAGE_SIZE;
                    tcp.SendBufferSize = FILE_PACKAGE_SIZE;
                    tcp.Connect(new IPEndPoint(end.Address.Address, CLIENT_FILE_PORT));
                    
                }
                catch (Exception ex)
                {
                    AddLog_async("SERVER", ex.Message);
                    return;
                }

                using (NetworkStream netStream = tcp.GetStream())
                {
                    List<byte> file_data = File.ReadAllBytes($"Files/{path}").ToList();
                    int packages_count = (int)Math.Ceiling((double)file_data.Count / FILE_PACKAGE_SIZE);
                    byte[] packages_count_data = BitConverter.GetBytes(packages_count);
                    netStream.Write(packages_count_data, 0, packages_count_data.Length);

                    int to_send_length = file_data.Count;

                    for (int i = 0; i < packages_count; i++)
                    {
                        byte[] to_send = file_data.GetRange(FILE_PACKAGE_SIZE * i, to_send_length > FILE_PACKAGE_SIZE ? FILE_PACKAGE_SIZE : to_send_length).ToArray();
                        to_send_length -= FILE_PACKAGE_SIZE;
                        netStream.Write(to_send, 0, to_send.Length);
                    }

                    /*byte[] file_data = File.ReadAllBytes(path);
                    byte[] length = BitConverter.GetBytes(file_data.Length);
                    byte[] package = new byte[4 + file_data.Length];
                    length.CopyTo(package, 0);
                    file_data.CopyTo(package, 4);

                    int bytesSent = 0;
                    int bytesLeft = package.Length;

                    while (bytesLeft > 0)
                    {
                        int nextPacketSize = (bytesLeft > FILE_PACKAGE_SIZE) ? FILE_PACKAGE_SIZE : bytesLeft;
                        netStream.Write(package, bytesSent, nextPacketSize);
                        bytesSent += nextPacketSize;
                        bytesLeft -= nextPacketSize;
                    }*/

                    netStream.Close(); 
                }

                tcp.Close();
            }
        }

        public static void SendCommand(ServerCommands id, IPEndPoint otherEnd, params string[] parameters)
        {
            ServerCommand command = new ServerCommand() { CommandID = id, Parameters = parameters.ToList() };
            List<byte> request = new List<byte>() { 0 };
            byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(command, Formatting.Indented));
            request.AddRange(json);
            byte[] array = request.ToArray();
            if (otherEnd != null) udp.Send(array, array.Length, new IPEndPoint(otherEnd.Address.Address, 52534));
            else udp.Send(array, array.Length, new IPEndPoint(clientEnd.Address.Address, 52534));
        }

        public static void Kick(IPEndPoint currentEnd)
        {
            SendCommand(ServerCommands.Kick, null);
        }

        public string GetRecieverInfo()
        {
            string name = "Имя потока: " + recieverThread.Name;
            string priority = "Приоритет потока: " + recieverThread.Priority;
            string status = "Статус потока: " + recieverThread.ThreadState;
            return name + Environment.NewLine + priority + Environment.NewLine + status;
        }

        void AddLog(string sender, string text)
        {
            window.AddLog(sender, text);
        }

        void AddLog_async(string sender, string text)
        {
            window.AddLog_delegate(sender, text);
        }

        public void Dispose()
        {
            if (recieverThread != null) recieverThread.Abort();
            Database.Stop();
        }
    }
}
