/*
 * Created by SharpDevelop.
 * User: andsos
 * Date: 7/18/2011
 * Time: 5:35 PM
 */
using System;
using System.Net.Sockets;

namespace SerialSpeedConverter
{
    public class TcpLibException : Exception {
        public TcpLibException(string s) : base(s) {}
    }
    
    /// <summary>
    /// Buffers the socket connection and TcpServer instance.
    /// </summary>
    public class ConnectionState
    {
        protected Socket connection;
        protected TcpServer server;

        /// <summary>
        /// Gets the TcpServer instance. Throws an exception if the connection
        /// has been closed.
        /// </summary>
        public TcpServer Server
        {
            get
            {
                if (server == null)
                {
                    throw new TcpLibException("Connection is closed.");
                }

                return server;
            }
        }

        /// <summary>
        /// Gets the socket connection. Throws an exception if the connection
        /// has been closed.
        /// </summary>
        public Socket Connection
        {
            get
            {
                if (server == null)
                {
                    throw new TcpLibException("Connection is closed.");
                }

                return connection;
            }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="connection">The socket connection.</param>
        /// <param name="server">The TcpServer instance.</param>
        public ConnectionState(Socket connection, TcpServer server)
        {
            this.connection = connection;
            this.server = server;
        }

        /// <summary>
        /// This is the prefered manner for closing a socket connection, as it
        /// nulls the internal fields so that subsequently referencing a closed
        /// connection throws an exception. This method also throws an exception
        /// if the connection has already been shut down.
        /// </summary>
        public void Close()
        {
            if (server == null)
            {
                throw new TcpLibException("Connection already is closed.");
            }

            connection.Shutdown(SocketShutdown.Both);
            connection.Close();
            connection = null;
            server = null;
        }
    }
}
