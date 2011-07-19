/*
 * Created by SharpDevelop.
 * User: andsos
 * Date: 7/18/2011
 * Time: 5:19 PM
 */
using System;
using System.Net;
using System.Net.Sockets;

namespace SerialSpeedConverter
{
    public class TcpServerEventArgs {
        public ConnectionState ConnectionState { get ; set; }
        public TcpServerEventArgs(ConnectionState cs){
            ConnectionState = cs;
        }
    }
    public class TcpLibApplicationExceptionEventArgs {
        Exception Exception { get; set; }
        public TcpLibApplicationExceptionEventArgs(Exception ex) {
            Exception = ex;
        }
    }
    
    /// <summary>
    /// Description of TcpServer.
    /// </summary>
    public class TcpServer
    {

        public delegate void TcpServerEventDlgt(object sender, TcpServerEventArgs e);
        public delegate void ApplicationExceptionDlgt(object sender, TcpLibApplicationExceptionEventArgs e);

        /// <summary>
        /// Event fires when a connection is accepted. Being multicast, this
        /// allows you to attach not only your application's event handler, but
        /// also other handlers, such as diagnostics/monitoring, to the event.
        /// </summary>
        public event TcpServerEventDlgt Connected;

        protected IPEndPoint endPoint;
        protected Socket listener;
        protected int pendingConnectionQueueSize;

        /// <summary>
        /// Gets/sets pendingConnectionQueueSize. The default is 100.
        /// </summary>
        public int PendingConnectionQueueSize
        {
            get { return pendingConnectionQueueSize; }
            set
            {
                if (listener != null)
                {
                    throw new TcpLibException("Listener has already started. Changing the pending queue size is not allowed.");
                }

                pendingConnectionQueueSize = value;
            }
        }

        /// <summary>
        /// Gets listener socket.
        /// </summary>
        public Socket Listener
        {
            get { return listener; }
        }

        /// <summary>
        /// Gets/sets endPoint
        /// </summary>
        public IPEndPoint EndPoint
        {
            get { return endPoint; }
            set
            {
                if (listener != null)
                {
                    throw new TcpLibException("Listener has already started. Changing the endpoint is not allowed.");
                }

                endPoint = value;
            }
        }
        
        /// <summary>
        /// Default constructor.
        /// </summary>
        public TcpServer()
        {
            pendingConnectionQueueSize = 100;
        }

        /// <summary>
        /// Initializes the server with an endpoint.
        /// </summary>
        /// <param name="endpoint"></param>
        public TcpServer(IPEndPoint endpoint)
        {
            this.endPoint = endpoint;
            pendingConnectionQueueSize = 100;
        }

        /// <summary>
        /// Initializes the server with a port, the endpoint is initialized
        /// with IPAddress.Any.
        /// </summary>
        /// <param name="port"></param>
        public TcpServer(int port)
        {
            endPoint = new IPEndPoint(IPAddress.Any, port);
            pendingConnectionQueueSize = 100;
        }

        /// <summary>
        /// Initializes the server with a 4 digit IP address and port.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public TcpServer(string address, int port)
        {
            endPoint = new IPEndPoint(IPAddress.Parse(address), port);
            pendingConnectionQueueSize = 100;
        }

        /// <summary>
        /// Begins listening for incoming connections.
        /// This method returns immediately.
        /// Incoming connections are reported using the Connected event.
        /// </summary>
        public void StartListening()
        {
            if (endPoint == null)
            {
                throw new TcpLibException("EndPoint not initialized.");
            }

            if (listener != null)
            {
                throw new TcpLibException("Already listening.");
            }

            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(endPoint);
            listener.Listen(pendingConnectionQueueSize);
            listener.BeginAccept(AcceptConnection, null);
        }

        /// <summary>
        /// Shuts down the listener.
        /// </summary>
        public void StopListening()
        {
            // Make sure we're not accepting a connection.
            lock (this)
            {
                listener.Close();
                listener = null;
            }
        }

        /// <summary>
        /// Accepts the connection and invokes any Connected event handlers.
        /// </summary>
        /// <param name="res"></param>
        protected void AcceptConnection(IAsyncResult res)
        {
            Socket connection;

            // Make sure listener doesn't go null on us.
            lock (this)
            {
                connection = listener.EndAccept(res);
                listener.BeginAccept(AcceptConnection, null);
            }

            // Close the connection if there are no handlers to accept it!
            if (Connected == null)
            {
                connection.Close();
            }
            else
            {
                ConnectionState cs = new ConnectionState(connection, this);
                OnConnected(new TcpServerEventArgs(cs));
            }
        }

        /// <summary>
        /// Fire the Connected event if it exists.
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnConnected(TcpServerEventArgs e)
        {
            if (Connected != null)
            {
                try
                {
                    Connected(this, e);
                }
                catch (TcpLibException ex)
                {
                    // Close the connection if the application threw an exception that
                    // is caught here by the server.
                    e.ConnectionState.Close();
                    
                    TcpLibApplicationExceptionEventArgs appErr =
                        new TcpLibApplicationExceptionEventArgs(ex);

                }
            }
        }
        
    }
}
