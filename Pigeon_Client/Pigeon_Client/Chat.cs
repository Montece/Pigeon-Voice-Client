using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Xml.Serialization;

namespace Pigeon_Client
{
    public static class Chat
    {
        public const int SERVER_PORT = 52535;
        public const int CLIENT_PORT = 52534;
        public static IPEndPoint ServerEnd;

        public static ServerInfo CurrentServer { get; private set; }
        public static List<ServerInfo> Servers = new List<ServerInfo>();
        static string Login { get; set; } = "";
        static string Nickname { get; set; } = "";

        public static IPEndPoint end = new IPEndPoint(IPAddress.Any, SERVER_PORT);
        public static UdpClient udp = new UdpClient(CLIENT_PORT);

        static List<byte> request = new List<byte>();
        static int RequestType;
        static ClientCommand JSONcommand;
        static List<string> Params;
        static List<byte> Voice;

        public static void SetIP(string serverIP)
        {
            ServerEnd = new IPEndPoint(IPAddress.Parse(serverIP), SERVER_PORT);
        }

        public static string GetServerVersion()
        {
            Send(ClientCommands.GetServerVersion,/* null,*/ Library.Version);
            bool status = Recieve(out Params, out Voice);
            if (!status) return "ERROR";    
            return Params[0];
        }

        public static bool SetCurrentServer(int i)
        {
            if (i >= 0 && i < Servers.Count)
            {
                CurrentServer = Servers[i];
                return true;
            }
            return false;
        }

        public static bool LogIn(string login, string password)
        {
            Login = login;
            Send(ClientCommands.LogIn, /*null,*/ login, password);
            Recieve(out Params, out Voice);
            Nickname = Params[1];
            return bool.Parse(Params[0]);
        }

        public static bool Register(string login, string password, string nickname, string email)
        {
            Login = login;
            Nickname = nickname;
            Send(ClientCommands.Register,/* null,*/ nickname, login, password, email);
            Recieve(out Params, out Voice);
            return bool.Parse(Params[0]);
        }

        public static void GetServers()
        {
            Send(ClientCommands.GetAllServers, null);
            Recieve(out Params, out Voice);

            XmlSerializer ServersXML = new XmlSerializer(typeof(ServersSavingData));
            StringReader writer = new StringReader(Params[0]);
            ServersSavingData data = (ServersSavingData)ServersXML.Deserialize(writer);
            Servers = data.Servers;
        }                   

        public static bool ConnectToServer()
        {
            Send(ClientCommands.ConnectToServer,/* null,*/ Login, CurrentServer.SID);
            Recieve(out Params, out Voice);
            bool success = bool.Parse(Params[0]);
            return success;
        }

        public static string LoadHistory()
        {
            Send(ClientCommands.LoadHistory,/* null,*/ CurrentServer.SID);
            Recieve(out Params, out Voice);
            return Params[0];
        }

        public static List<ShortUserInfo> GetUsers()
        {
            Send(ClientCommands.GetServerUsers,/* null,*/ CurrentServer.SID);
            Recieve(out Params, out Voice);
            var a = Params[0];
            XmlSerializer UsersXML = new XmlSerializer(typeof(UsersSavingData2));
            StringReader writer = new StringReader(Params[0]);
            UsersSavingData2 data = (UsersSavingData2)UsersXML.Deserialize(writer);
            return data.Users;
        }

        public static void SendTextMessage(string text)
        {
            Send(ClientCommands.TextMessage,/* null,*/ CurrentServer.SID, text, Login);
        }

        static bool Recieve(out List<string> result, out List<byte> voice)
        {
            result = null;
            voice = null;
            request.Clear();
            RequestType = 0;
            JSONcommand = null;

            try
            {
                request = udp.Receive(ref end).ToList();
            }
            catch (SocketException x)
            {
                return false;
            }

            RequestType = request[0];
            if (RequestType == 0)
            {
                request.RemoveAt(0);
                try
                {
                    JSONcommand = JsonConvert.DeserializeObject<ClientCommand>(Encoding.UTF8.GetString(request.ToArray()));
                    result = JSONcommand.Parameters;                   
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            else
            if (RequestType == 1)
            {
                request.RemoveAt(0);
                voice = request;
                return true;
            }
            return false;
        }

        public static void Send(ClientCommands id, params string[] parameters)
        {
            ClientCommand command = new ClientCommand() { CommandID = id, Parameters = parameters?.ToList() };
            List<byte> request = new List<byte>() { 0 };
            byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(command, Formatting.Indented));
            request.AddRange(json);
            byte[] array = request.ToArray();
            udp.Send(array, array.Length, ServerEnd);
        }

        public static void SendVoice(byte[] voice)
        {
            XmlSerializer VoiceXML = new XmlSerializer(typeof(VoiceCommand));
            VoiceCommand command = new VoiceCommand
            {
                SID = CurrentServer.SID,
                Login = Login
            };
            List<byte> request = new List<byte> { 1 };

            StringWriter writer = new StringWriter();
            VoiceXML.Serialize(writer, command);
            byte[] commandBYTE = Encoding.UTF8.GetBytes(writer.ToString());
            request.Add((byte)commandBYTE.Length);
            request.AddRange(commandBYTE);
            request.AddRange(voice);
            byte[] array = request.ToArray();
            udp.Send(array, array.Length, ServerEnd);
        }

        public static void Stop()
        {
            Send(ClientCommands.Disconnect,/* null,*/ Login, CurrentServer.SID);
        }
    }

    [Serializable]
    public class ServerInfo
    {
        public string Title { get; set; }
        public string SID { get; set; }
        public int MaxUsersCount { get; set; }
        public string Password { get; set; }
    }

    [Serializable]
    public class ServersSavingData
    {
        public List<ServerInfo> Servers { get; set; }

        public ServersSavingData()
        {

        }
    }

    [Serializable]
    public class UsersSavingData2
    {
        public List<ShortUserInfo> Users { get; set; }

        public UsersSavingData2()
        {

        }
    }

    [Serializable]
    public class ShortUserInfo
    {
        public string Nickname { get; set; }
        public string Login { get; set; }

        public ShortUserInfo()
        {

        }
    }
}
