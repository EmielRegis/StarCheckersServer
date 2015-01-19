using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace StarCheckersServer
{
    // State object for reading client data asynchronously
    public class UserConnector
    {
        // Size of receive buffer.
        public const int BufferSize = 1024;

        // Client  socket.
        public Socket WorkSocket;

        // Receive buffer.
        private byte[] _buffer = new byte[BufferSize];
        // Received data string.
        private readonly StringBuilder _stringBuilder = new StringBuilder();

        public UserConnector Pair;
        public bool IsPaired { get; private set; }

        public Action EndEvent;


        public UserConnector()
        {
            IsPaired = false;
        }

        public void WaitForMessage()
        {
            WorkSocket.BeginReceive(_buffer, 0, BufferSize, 0,
                ReadCallback, this);
        }

        public void SetConnection(UserConnector connection)
        {
            Pair = connection;
            IsPaired = true;
            Send(WorkSocket, WorkSocket.RemoteEndPoint.ToString().Select(c => (int)c).Aggregate((a, b) => a + b) >
                             connection.WorkSocket.RemoteEndPoint.ToString()
                                 .Select(c => (int)c)
                                 .Aggregate((a, b) => a + b)
                ? "white"
                : "black");
        }

        private void ReadCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the state object and the handler socket
                // from the asynchronous state object.
                UserConnector state = (UserConnector)ar.AsyncState;
                Socket handler = state.WorkSocket;

                // Read data from the client socket.
                if (handler != null && handler.Connected)
                {
                    int bytesRead = 0;
                    try
                    {
                        bytesRead = handler.EndReceive(ar);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        return;
                    }

                    if (bytesRead > 0)
                    {
                        state._stringBuilder.Clear();
                        // There  might be more data, so store the data received so far.
                        state._stringBuilder.Append(Encoding.ASCII.GetString(
                            state._buffer, 0, bytesRead));

                        // Check for end-of-file tag. If it is not there, read 
                        // more data.
                        var content = state._stringBuilder.ToString();

                        // Echo the data back to the client.
                        Console.WriteLine("Read {0} bytes from socket. \n Data : {1}",
                            content.Length, content);

                        if (IsPaired)
                        {
                            if (content == "end")
                            {
                                Pair.Send(Pair.WorkSocket, content);
                                Send(WorkSocket, content);

                                //                            if (EndEvent != null)
                                //                                EndEvent();
                            }
                            else
                                Pair.Send(Pair.WorkSocket, content);
                        }
                        else
                            Send(handler, "any");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private void Send(Socket handler, String data)
        {
            // Convert the string data to byte data using ASCII encoding.
            byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            handler.BeginSend(byteData, 0, byteData.Length, 0,
                (data != "end" && data != "black" && data != "user_disconnected") ? SendCallback : new AsyncCallback(SendCallbackNoWait), handler);
        }

        private void SendCallbackNoWait(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                if (handler != null && handler.Connected)
                {
                    try
                    {
                        int bytesSent = handler.EndSend(ar);
                        Console.WriteLine("Sent {0} bytes to client: {1}", bytesSent, handler.RemoteEndPoint);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                if (handler != null && handler.Connected)
                {
                    try
                    {
                        // Complete sending the data to the remote device.
                        int bytesSent = handler.EndSend(ar);
                        Console.WriteLine("Sent {0} bytes to client: {1}", bytesSent, handler.RemoteEndPoint);

                        WaitForMessage();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        public void Dispose()
        {
            try
            {
                WorkSocket.Shutdown(SocketShutdown.Both);
                WorkSocket.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    public class AsyncServer
    {
        Dictionary<string, UserConnector> connections = new Dictionary<string, UserConnector>();
        // Thread signal.
        public ManualResetEvent AllDone = new ManualResetEvent(false);


        public void StartListening()
        {
            // Establish the local endpoint for the socket.
            // The DNS name of the computer
            // running the listener is "host.contoso.com".
            IPHostEntry ipHostInfo = Dns.Resolve(Dns.GetHostName());
            IPAddress ipAddress = ipHostInfo.AddressList.FirstOrDefault(adr => adr.ToString().StartsWith("19")) ??
                                  ipHostInfo.AddressList.FirstOrDefault();
            if (ipAddress == null)
            {
                Console.WriteLine("Cannot find network...");
                return;
            }

            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 8888);

            // Create a TCP/IP socket.
            Socket listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);

            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    // Set the event to nonsignaled state.
                    AllDone.Reset();

                    // Start an asynchronous socket to listen for connections.
                    Console.WriteLine("Waiting for a connection...");
                    listener.BeginAccept(
                        AcceptCallback,
                        listener);

                    // Wait until a connection is made before continuing.
                    AllDone.WaitOne();
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            Console.WriteLine("\nPress ENTER to continue...");
            Console.Read();
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.
            AllDone.Set();

            // Get the socket that handles the client request.
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);

            // Create the state object.
            UserConnector state = new UserConnector { WorkSocket = handler };

            var key = state.WorkSocket.RemoteEndPoint.ToString();
            connections.Add(key, state);

            if (connections.Any(v => v.Key != key && !v.Value.IsPaired))
            {
                var con = connections.First(v => v.Key != key && !v.Value.IsPaired).Value;
                state.SetConnection(con);
                con.SetConnection(state);

                Action ac = () =>
                {
                    connections.Remove(state.WorkSocket.RemoteEndPoint.ToString());
                    connections.Remove(con.WorkSocket.RemoteEndPoint.ToString());
                    state.Dispose();
                    con.Dispose();
                };

                state.EndEvent += ac;
                con.EndEvent += ac;
            }
        }
    }
}