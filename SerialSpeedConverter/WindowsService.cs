using System;
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
using log4net;

[assembly: log4net.Config.XmlConfigurator(Watch=true)]
namespace WindowsService
{
    class SerialSpeedControllerWindowsService : ServiceBase
    {

        protected bool EnableRemote 
        { 
        	get 
        	{ 
        		return bool.Parse(ConfigurationManager.AppSettings["EnableRemote"] ?? "true"); 
        	} 
        }

       	private readonly ILog log = LogManager.GetLogger(typeof(SerialSpeedControllerWindowsService));
        
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
                Console.WriteLine("Serial Wheel Speed Converter, Version 0.4");
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
                    
                    if (interval == 0)
                    {
                        log.Debug("Interval is less than 1.024 ms! RPM > 29296.875");
                        continue;
                    }
                    
                    int converted = 29296875 / interval; // 30000 / 1.024 * 1000 = 29296875
                    
                    log.DebugFormat("Interval: {0} ms (0x{0:X4}) / Converted to RPM: {1}",
                                                              interval, converted / 1000.0);
                    
                    if (EnableRemote)
                    {
                        SendRemote((Int16)converted); // Truncate to 16 bits
                    }
                }
                catch (System.TimeoutException)
                { }
                catch (Exception e)
                {
            		log.Error(e.Message);
                    log.Error(e.StackTrace);
                    Thread.Sleep(5000); // give the user a chance to read the error message
                    buf = new byte[2];
                }
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
                log.Debug(e.Message);
                log.Debug(e.StackTrace);
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