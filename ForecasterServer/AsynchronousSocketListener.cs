﻿using Forecaster.Net;
using Forecaster.Net.Requests;
using Forecaster.Net.Responses;
using Forecaster.Server.Network;
using Forecaster.Server.Prediction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Forecaster.Server
{
    public class AsynchronousSocketListener
    {
        // Thread signal.  
        public static ManualResetEvent allDone = new ManualResetEvent(false);

        public AsynchronousSocketListener()
        {
        }

        public static void StartListening()
        {
            // Establish the local endpoint for the socket.  
            // The DNS name of the computer  
            // running the listener is "host.contoso.com".  
            IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 11000);

            // Create a TCP/IP socket.  
            Socket listener = new Socket(ipAddress.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp);

            // Bind the socket to the local endpoint and listen for incoming connections.  
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    // Set the event to nonsignaled state.  
                    allDone.Reset();

                    // Start an asynchronous socket to listen for connections.  
                    Console.WriteLine("Waiting for a connection...");
                    listener.BeginAccept(
                        new AsyncCallback(AcceptCallback),
                        listener);

                    // Wait until a connection is made before continuing.  
                    allDone.WaitOne();
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            Console.WriteLine("\nPress ENTER to continue...");
            Console.Read();

        }

        public static void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.  
            allDone.Set();

            // Get the socket that handles the client request.  
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);

            // Create the state object.  
            StateObject state = new StateObject();
            state.workSocket = handler;
            handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                new AsyncCallback(ReadCallback), state);
        }

        public static void ReadCallback(IAsyncResult ar)
        {
            string content = string.Empty;

            // Retrieve the state object and the handler socket  
            // from the asynchronous state object.  
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;

            int bytesRead;

            try
            {
                // Read data from the client socket.
                bytesRead = handler.EndReceive(ar);
            }
            catch(SocketException ex)
            {
                Console.WriteLine(ex.Message + " Error code: " + ex.ErrorCode);

                ShutdownSocket(handler);

                return;
            }

            if (bytesRead > 0)
            {
                // There  might be more data, so store the data received so far.  
                state.sb.Append(Encoding.ASCII.GetString(
                    state.buffer, 0, bytesRead));

                state.receivedData.Add(state.buffer.Take(bytesRead).ToArray());

                // Check for end-of-file tag. If it is not there, read
                // more data.

                if (state.totalBytesExpected == 0)
                {
                    try
                    {
                        state.totalBytesExpected = RequestHandler.ReadRequestLength(state.buffer) + sizeof(int);
                    }
                    catch (Exception ex)
                    {
                        if (ex is OverflowException || ex is ArgumentOutOfRangeException)
                        {
                            Console.WriteLine(ex.Message);

                            Response clientErrorResponse = new Response((int)ResponseCode.Declinded);

                            byte[] responseBytes = ResponseManager.CreateByteResponse(clientErrorResponse);

                            Send(handler, responseBytes);

                            return;
                        }

                        throw;
                    }
                }

                state.totalBytesRead += bytesRead;

                if (state.totalBytesRead >= state.totalBytesExpected)
                {
                    try
                    {
                        // All the data has been read from the
                        // client. Display it on the console.
                        byte[] data = state.receivedData.SelectMany(a => a).ToArray(),
                            response = Controller.GetResponse(data);

                        Console.WriteLine("Read {0} bytes from socket.", data.Length);

                        // Echo the data back to the client.  
                        Send(handler, response);
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine(ex.Message);

                        Response serverErrorResponse = new Response((int)ResponseCode.Error);

                        byte[] responseBytes = ResponseManager.CreateByteResponse(serverErrorResponse);

                        Send(handler, responseBytes);
                    }
                }
                else
                {
                    // Not all data received. Get more.  
                    handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReadCallback), state);
                }
            }
            else
            {
                Response clientErrorResponse = new Response((int)ResponseCode.Declinded);

                byte[] responseBytes = ResponseManager.CreateByteResponse(clientErrorResponse);

                Send(handler, responseBytes);
            }
        }

        private static void Send(Socket handler, byte[] responseBytes)
        {
            // Begin sending the data to the remote device.  
            handler.BeginSend(responseBytes, 0, responseBytes.Length, 0,
                new AsyncCallback(SendCallback), handler);
        }

        private static void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.  
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.  
                int bytesSent = handler.EndSend(ar);
                Console.WriteLine("Sent {0} bytes to client.", bytesSent);

                ShutdownSocket(handler);

                
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static void ShutdownSocket(Socket handler)
        {
            handler.Shutdown(SocketShutdown.Both);
            handler.Close();

            Console.WriteLine("Connection closed");
        }
    }
}
