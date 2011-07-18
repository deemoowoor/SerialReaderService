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
using SerialSpeedConverter;

[assembly: log4net.Config.XmlConfigurator(Watch=true)]
namespace WindowsService
{
    class SerialSpeedControllerWindowsService : ServiceBase
    {

        protected int DelayMultiplier 
        { 
        	get 
        	{ 
        		int v = 1;
        		int.TryParse(ConfigurationManager.AppSettings["DelayMultiplier"] ?? "1", out v); 
        		return v;
        	} 
        }

       	private readonly ILog log = LogManager.GetLogger(typeof(SerialSpeedControllerWindowsService));
        
        protected Thread _mainLoopThread;
        private bool _continue = true;
        private SerialPort _serialPort;
        private Timer _timer;

        private TcpServer _server;
       
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
            OpenSerial();
            CreateRemote();
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
                    
                    int converted = 29296875 / interval * DelayMultiplier; // 30000 / 1.024 * 1000 = 29296875
                    
                    log.DebugFormat("Interval: {0} ms (0x{0:X4}) / Converted to RPM: {1}",
                                                              interval, converted / 1000.0);
                    
                    SendRemote(converted.ToString());
                    
                }
                catch (InvalidOperationException)
		        {
                	try 
                	{
                        OpenSerial();
                	} 
                	catch {}
		    	}
		        catch (System.TimeoutException)
                { }
                catch (Exception e)
                {
                	log.Error(e);
            		Thread.Sleep(2000); // give the user a chance to read the error message
                    buf = new byte[2];
                }
            }
        }

        private int Combine(byte first, byte second)
        {
        	return ((int)first << 8) | second;
        }
        
        private int ReadSerial(byte[] buf)
        {
			int past = 0;
			while (_serialPort.BytesToRead < 2)
			{
				if (_serialPort.BytesToRead == 1 && past > 2)
				{
					_serialPort.ReadByte();
				}
				
			    Thread.Sleep(10);
			    past++;
			}
            
			_serialPort.Read(buf, 0, buf.Length);
        	
			return SerialEndianSwap ? 
            	Combine(buf[1], buf[0]) : 
            	Combine(buf[0], buf[1]);
        }
        
        private void SendRemote(string rpm)
        {
            if (!_server.Listener.Connected)
            {
                return;
            }
			
            var buf = Encoding.ASCII.GetBytes(rpm);
            var socket = _server.Listener;
            
            try
            {
                socket.Send(buf);
            }
            catch (IOException)
            {
                ScheduleReconnect();
            }
        }
        
        private void SendRemote(int rpm)
        {
            if (!_server.Listener.Connected)
            {
                return;
            }

            int converted = rpm;
            
            if (BitConverter.IsLittleEndian)
            {
                converted = IPAddress.HostToNetworkOrder(rpm);
            }

            var buf = BitConverter.GetBytes(converted);

            try
            {
                _server.Listener.Send(buf);
            }
            catch (IOException)
            {
                ScheduleReconnect();
            }
        }

        private void ScheduleReconnect()
        {
            _timer = new Timer(CreateRemote, null, 1000, System.Threading.Timeout.Infinite);
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

        private void CreateRemote(object o)
        {
            CreateRemote();
        }

        private void CreateRemote()
        {
            if (_server == null)
            {
                string RemoteConnectionString = ConfigurationManager.AppSettings["TCPServer"];
                var remote = RemoteConnectionString.Split(new char[] { ':' });
                var hostname = remote[0];
                var port = remote[1];
                
                _server = new TcpServer(hostname, int.Parse(port));
                //_server.Connected += OnRemoteConnected;
            }
            else 
            {
                _server.StopListening();
            }
            
            _server.StartListening();
        }

        private void CloseRemote()
        {
            if (_server != null)
            {
                _server.StopListening();
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