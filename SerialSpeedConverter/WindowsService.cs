﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.IO.Ports;
using System.Configuration;
using System.Net.Sockets;
using System.IO;
using System.Net;

namespace WindowsService
{
    class SerialSpeedControllerWindowsService : ServiceBase
    {

        protected bool EnableRemote { get { return bool.Parse(ConfigurationManager.AppSettings["EnableRemote"] ?? "true"); } }

        protected Thread _mainLoopThread;
        private bool _continue = true;
        private SerialPort _serialPort;
        private TcpClient _tcpclient;
        private Timer _timer;

        protected bool SerialEndianSwap;

        /// <summary>
        /// Public Constructor for WindowsService.
        /// - Put all of your Initialization code here.
        /// </summary>
        public SerialSpeedControllerWindowsService()
        {
            this.ServiceName = "Serial wheel speed reporting service";
            this.EventLog.Log = "SerialSpeedConverter";

            SerialEndianSwap = bool.Parse(ConfigurationManager.AppSettings["SerialEndianSwap"] ?? "true");

            // These Flags set whether or not to handle that specific
            //  type of event. Set to true if you need it, false otherwise.

            this.CanHandlePowerEvent = false;
            this.CanHandleSessionChangeEvent = false;
            this.CanPauseAndContinue = false;
            this.CanShutdown = true;
            this.CanStop = true;

            _mainLoopThread = new Thread(MainLoop); 
        }

        /// <summary>
        /// The Main Thread: This is where your Service is Run.
        /// </summary>

        static void Main(string[] args)
        {
            var service = new SerialSpeedControllerWindowsService();
            if (Environment.UserInteractive)
            {
                service.OnStart(args);
                Console.WriteLine("Enter any key to stop the program");
                Console.Read();
                service.OnStop();
            }
            else
            {
                ServiceBase.Run(service);
            }
        }

        /// <summary>
        /// Dispose of objects that need it here.
        /// </summary>
        /// <param name="disposing">Whether
        ///    or not disposing is going on.</param>

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        /// <summary>
        /// OnStart(): Put startup code here
        ///  - Start threads, get inital data, etc.
        /// </summary>
        /// <param name="args"></param>

        protected override void OnStart(string[] args)
        {
            base.OnStart(args);
            // First, open serial connection, if fails, abort
            
            if (EnableRemote)
            {
                OpenRemote();
            }

            OpenSerial();
            _mainLoopThread.Start();
        }

        /// <summary>
        /// OnStop(): Put your stop code here
        /// - Stop threads, set final data, etc.
        /// </summary>

        protected override void OnStop()
        {
            _continue = false;
            _mainLoopThread.Join();
            CloseSerial();
            CloseRemote();
            base.OnStop();
        }

        /// <summary>
        /// OnPause: Put your pause code here
        /// - Pause working threads, etc.
        /// </summary>

        protected override void OnPause()
        {
            base.OnPause();
        }

        /// <summary>
        /// OnContinue(): Put your continue code here
        /// - Un-pause working threads, etc.
        /// </summary>

        protected override void OnContinue()
        {
            base.OnContinue();
        }

        /// <summary>
        /// OnShutdown(): Called when the System is shutting down
        /// - Put code here when you need special handling
        ///   of code that deals with a system shutdown, such
        ///   as saving special data before shutdown.
        /// </summary>

        protected override void OnShutdown()
        {
            base.OnShutdown();
        }

        /// <summary>
        /// OnCustomCommand(): If you need to send a command to your
        ///   service without the need for Remoting or Sockets, use
        ///   this method to do custom methods.
        /// </summary>
        /// <param name="command">Arbitrary Integer between 128 & 256</param>

        protected override void OnCustomCommand(int command)
        {
            //  A custom command can be sent to a service by using this method:
            //#  int command = 128; //Some Arbitrary number between 128 & 256
            //#  ServiceController sc = new ServiceController("NameOfService");
            //#  sc.ExecuteCommand(command);

            base.OnCustomCommand(command);
        }

        /// <summary>
        /// OnPowerEvent(): Useful for detecting power status changes,
        ///   such as going into Suspend mode or Low Battery for laptops.
        /// </summary>
        /// <param name="powerStatus">The Power Broadcast Status
        /// (BatteryLow, Suspend, etc.)</param>

        protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
        {
            return base.OnPowerEvent(powerStatus);
        }

        /// <summary>
        /// OnSessionChange(): To handle a change event
        ///   from a Terminal Server session.
        ///   Useful if you need to determine
        ///   when a user logs in remotely or logs off,
        ///   or when someone logs into the console.
        /// </summary>
        /// <param name="changeDescription">The Session Change
        /// Event that occured.</param>

        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            base.OnSessionChange(changeDescription);
        }

        /// <summary>
        /// MainLoop(): main loop to read values from 
        /// the serial connection and writing them to the 
        /// TCP connection.
        /// </summary>
        /// <param name="obj"></param>
        public void MainLoop()
        {
            byte[] buf = new byte[2];
            while (_continue)
            {
                try
                {
                    int interval = ReadSerial(buf);
                    int converted = 30000000 / 1024 * interval;

                    if (Environment.UserInteractive)
                    {
                        Console.WriteLine("Interval ms: {0:X4} / Voltage: {1} / Converted to RPM: {2}", 
                            interval, interval / 1024.0 * 5.0, converted/1000.0);
                    }

                    if (EnableRemote)
                    {
                        SendRemote((Int16)converted); // Truncate to 16 bits
                    }
                }
                catch (System.TimeoutException) 
                { }
            }
        }

        delegate int CombineType(byte f, byte s);

        private int ReadSerial(byte[] buf)
        {
            while (_serialPort.BytesToRead < 2)
            {
                Thread.Sleep(100);
            }

            _serialPort.Read(buf, 0, buf.Length);
            CombineType combine = (byte first, byte second) => (((int)first << 8) | second);
            return SerialEndianSwap ? combine(buf[1], buf[0]) : combine(buf[0], buf[1]);
        }
        
        private void SendRemote(short value)
        {
            short converted = value;
            
            if (BitConverter.IsLittleEndian)
            {
                converted = IPAddress.HostToNetworkOrder(value);
            }

            if (_tcpclient == null)
            {
                ScheduleReconnect();
                return;
            }

            var buf = BitConverter.GetBytes(converted);
            var stream = _tcpclient.GetStream();

            try
            {
                stream.Write(buf, 0, 2);
            }
            catch (IOException)
            {
                ScheduleReconnect();
            }
        }

        private void ScheduleReconnect()
        {
            _timer = new Timer(OpenRemote, null, 1000, System.Threading.Timeout.Infinite);
        }

        private void OpenSerial()
        {
            CloseSerial(); // just to be safe

            _serialPort = new SerialPort();

            // Allow the user to set the appropriate properties.
            _serialPort.PortName = SetPortName(_serialPort.PortName);
            _serialPort.BaudRate = SetPortBaudRate(_serialPort.BaudRate);
            _serialPort.Parity = SetPortParity(_serialPort.Parity);
            _serialPort.DataBits = SetPortDataBits(_serialPort.DataBits);
            _serialPort.StopBits = SetPortStopBits(_serialPort.StopBits);
            _serialPort.Handshake = SetPortHandshake(_serialPort.Handshake);

            // Set the read/write timeouts (set to 10x normal update time)
            _serialPort.ReadTimeout = 5000;
            _serialPort.WriteTimeout = 1000;
            _serialPort.Open();
        }

        private void CloseSerial()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }

        private void OpenRemote(object o)
        {
            OpenRemote();
        }

        private void OpenRemote()
        {
            CloseRemote();
            string RemoteConnectionString = ConfigurationManager.AppSettings["RemoteTCPServer"];
            var remote = RemoteConnectionString.Split(new char[] { ':' });
            var hostname = remote[0];
            var port = remote[1];
            try
            {
                _tcpclient = new TcpClient(hostname, int.Parse(port));
            }
            catch (SocketException e)
            {
                if (Environment.UserInteractive)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.StackTrace);
                }

                throw;
            }
        }

        private void CloseRemote()
        {
            if (_tcpclient != null && _tcpclient.Connected)
            {
                _tcpclient.Close();
            }
        }

        public string SetPortName(string defaultPortName)
        {
            string portName = ConfigurationManager.AppSettings["PortName"];

            if (string.IsNullOrEmpty(portName))
            {
                portName = defaultPortName;
            }
            return portName;
        }

        public static int SetPortBaudRate(int defaultPortBaudRate)
        {
            string baudRate = ConfigurationManager.AppSettings["BaudRate"];

            if (string.IsNullOrEmpty(baudRate))
            {
                baudRate = defaultPortBaudRate.ToString();
            }

            return int.Parse(baudRate);
        }

        public static Parity SetPortParity(Parity defaultPortParity)
        {
            string parity = ConfigurationManager.AppSettings["Parity"];

            if (string.IsNullOrEmpty(parity))
            {
                parity = defaultPortParity.ToString();
            }

            return (Parity)Enum.Parse(typeof(Parity), parity);
        }

        public static int SetPortDataBits(int defaultPortDataBits)
        {
            string dataBits = ConfigurationManager.AppSettings["DataBits"];

            if (string.IsNullOrEmpty(dataBits))
            {
                dataBits = defaultPortDataBits.ToString();
            }

            return int.Parse(dataBits);
        }

        public static StopBits SetPortStopBits(StopBits defaultPortStopBits)
        {
            string stopBits = ConfigurationManager.AppSettings["StopBits"];

            if (string.IsNullOrEmpty(stopBits))
            {
                stopBits = defaultPortStopBits.ToString();
            }

            return (StopBits)Enum.Parse(typeof(StopBits), stopBits);
        }

        public static Handshake SetPortHandshake(Handshake defaultPortHandshake)
        {
            string handshake = ConfigurationManager.AppSettings["Handshake"];

            if (string.IsNullOrEmpty(handshake))
            {
                handshake = defaultPortHandshake.ToString();
            }

            return (Handshake)Enum.Parse(typeof(Handshake), handshake);
        }
    }
}