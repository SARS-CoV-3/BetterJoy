﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Threading;
using System.Runtime.InteropServices;
using System.Timers;

using System.Net.NetworkInformation;
using System.Diagnostics;

using static BetterJoyForCemu.HIDapi;
using Nefarius.ViGEm.Client;
using System.Net;
using System.Configuration;
using System.Net.Http;
using System.IO;
using System.Windows.Forms;

using System.ServiceProcess;

namespace BetterJoyForCemu {
	public class JoyconManager {
		public bool EnableIMU = true;
		public bool EnableLocalize = false;

		private const ushort vendor_id = 0x57e;
		private const ushort vendor_id_ = 0x057e;
		private const ushort product_l = 0x2006;
		private const ushort product_r = 0x2007;
		private const ushort product_pro = 0x2009;
		private const ushort product_snes = 0x2017;

		public List<Joycon> j; // Array of all connected Joy-Cons
		static JoyconManager instance;

		public MainForm form;

		System.Timers.Timer controllerCheck;

		public static JoyconManager Instance {
			get { return instance; }
		}

		public void Awake() {
			instance = this;
			j = new List<Joycon>();
			HIDapi.hid_init();
		}

		public void Start() {
			controllerCheck = new System.Timers.Timer(2000); // check for new controllers every 2 seconds
			controllerCheck.Elapsed += CheckForNewControllersTime;
			controllerCheck.Start();
		}

		bool ControllerAlreadyAdded(string path) {
			foreach (Joycon v in j)
				if (v.path == path)
					return true;
			return false;
		}

		void CleanUp() { // removes dropped controllers from list
			List<Joycon> rem = new List<Joycon>();
			for (int i = 0; i < j.Count; i++) {
				Joycon v = j[i];
				if (v.state == Joycon.state_.DROPPED) {
					if (v.other != null)
						v.other.other = null; // The other of the other is the joycon itself

					v.Detach(); rem.Add(v);

					foreach (Button b in form.con) {
						if (b.Enabled & b.Tag == v) {
							b.Invoke(new MethodInvoker(delegate {
								b.BackColor = System.Drawing.Color.FromArgb(0x00, System.Drawing.SystemColors.Control);
								b.Enabled = false;
								b.BackgroundImage = Properties.Resources.cross;
							}));
							break;
						}
					}

					form.AppendTextBox("Removed dropped controller to list. Can be reconnected.\r\n");
				}
			}

			foreach (Joycon v in rem)
				j.Remove(v);
		}

		void CheckForNewControllersTime(Object source, ElapsedEventArgs e) {
			if (Config.IntValue("ProgressiveScan") == 1) {
				CheckForNewControllers();
			}
		}

		public void CheckForNewControllers() {
			CleanUp();

			// move all code for initializing devices here and well as the initial code from Start()
			bool isLeft = false;
			IntPtr ptr = HIDapi.hid_enumerate(vendor_id, 0x0);
			IntPtr top_ptr = ptr;

			hid_device_info enumerate; // Add device to list
			bool foundNew = false;
			while (ptr != IntPtr.Zero) {
				enumerate = (hid_device_info)Marshal.PtrToStructure(ptr, typeof(hid_device_info));

				if (enumerate.serial_number == null) {
					ptr = enumerate.next; // can't believe it took me this long to figure out why USB connections used up so much CPU.
					// it was getting stuck in an inf loop here!
					continue;
				}

				if (form.nonOriginal) {
					enumerate.product_id = product_pro;
				}

				bool validController = (enumerate.product_id == product_l || enumerate.product_id == product_r ||
										enumerate.product_id == product_pro || enumerate.product_id == product_snes);
				if (validController && !ControllerAlreadyAdded(enumerate.path)) {
					switch (enumerate.product_id) {
						case product_l:
							isLeft = true;
							form.AppendTextBox("Left Joy-Con connected.\r\n"); break;
						case product_r:
							isLeft = false;
							form.AppendTextBox("Right Joy-Con connected.\r\n"); break;
						case product_pro:
							isLeft = true;
							form.AppendTextBox("Pro controller connected.\r\n"); break;
						case product_snes:
							isLeft = true;
							form.AppendTextBox("SNES controller connected.\r\n"); break;
						default:
							form.AppendTextBox("Non Joy-Con Nintendo input device skipped.\r\n"); break;
					}

					// Add controller to block-list for HidGuardian
					if (Program.useHIDG) {
						HttpWebRequest request = (HttpWebRequest)WebRequest.Create(@"http://localhost:26762/api/v1/hidguardian/affected/add/");
						string postData = @"hwids=HID\" + enumerate.path.Split('#')[1].ToUpper();
						var data = Encoding.UTF8.GetBytes(postData);

						request.Method = "POST";
						request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
						request.ContentLength = data.Length;

						using (var stream = request.GetRequestStream())
							stream.Write(data, 0, data.Length);

						try {
							var response = (HttpWebResponse)request.GetResponse();
							var responseString = new StreamReader(response.GetResponseStream()).ReadToEnd();
						} catch {
							form.AppendTextBox("Unable to add controller to block-list.\r\n");
						}
					}
					// -------------------- //

					IntPtr handle = HIDapi.hid_open_path(enumerate.path);
					try {
						HIDapi.hid_set_nonblocking(handle, 1);
					} catch {
						form.AppendTextBox("Unable to open path to device - are you using the correct (64 vs 32-bit) version for your PC?\r\n");
						break;
					}

					bool isPro = enumerate.product_id == product_pro;
					bool isSnes = enumerate.product_id == product_snes;
					j.Add(new Joycon(handle, EnableIMU, EnableLocalize & EnableIMU, 0.05f, isLeft, enumerate.path, enumerate.serial_number, j.Count, isPro, isSnes));
					
					foundNew = true;
					j.Last().form = form;

					if (j.Count < 5) {
						int ii = -1;
						foreach (Button v in form.con) {
							ii++;
							if (!v.Enabled) {
								System.Drawing.Bitmap temp;
								switch (enumerate.product_id) {
									case (product_l):
										temp = Properties.Resources.jc_left_s; break;
									case (product_r):
										temp = Properties.Resources.jc_right_s; break;
									case (product_pro):
										temp = Properties.Resources.pro; break;
									case (product_snes):
										temp = Properties.Resources.snes; break;
									default:
										temp = Properties.Resources.cross; break;
								}

								v.Invoke(new MethodInvoker(delegate {
									v.Tag = j.Last(); // assign controller to button
									v.Enabled = true;
									v.Click += new EventHandler(form.conBtnClick);
									v.BackgroundImage = temp;
								}));

								form.loc[ii].Invoke(new MethodInvoker(delegate {
									form.loc[ii].Tag = v;
									form.loc[ii].Click += new EventHandler(form.locBtnClick);
								}));

								break;
							}
						}
					}

					byte[] mac = new byte[6];
					for (int n = 0; n < 6; n++)
						mac[n] = byte.Parse(enumerate.serial_number.Substring(n * 2, 2), System.Globalization.NumberStyles.HexNumber);
					j[j.Count - 1].PadMacAddress = new PhysicalAddress(mac);
				}

				ptr = enumerate.next;
			}

			if (foundNew) { // attempt to auto join-up joycons on connection
				Joycon temp = null;
				foreach (Joycon v in j) {
					if (!v.isPro) {
						if (temp == null)
							temp = v;
						else if (temp.isLeft != v.isLeft && v.other == null) {
							temp.other = v;
							v.other = temp;

							//Set both Joycon LEDs to the one with the lowest ID
							byte led = temp.LED <= v.LED ? temp.LED : v.LED;
							temp.LED = led;
							v.LED = led;
							temp.SetPlayerLED(led);
							v.SetPlayerLED(led);

							if (temp.xin != null)
								temp.xin.Dispose();
							if (temp.ds4 != null)
								temp.ds4.Dispose();
							temp.xin = null;
							temp.ds4 = null;

							foreach (Button b in form.con)
								if (b.Tag == v || b.Tag == temp) {
									Joycon tt = (b.Tag == v) ? v : (b.Tag == temp) ? temp : v;
									b.BackgroundImage = tt.isLeft ? Properties.Resources.jc_left : Properties.Resources.jc_right;
								}

							temp = null;    // repeat
						}
					}
				}
			}

			HIDapi.hid_free_enumeration(top_ptr);

			foreach (Joycon jc in j) { // Connect device straight away
				if (jc.state == Joycon.state_.NOT_ATTACHED) {
					if (jc.xin != null)
						jc.xin.Connect();
					if (jc.ds4 != null)
						jc.ds4.Connect();

					jc.Attach(leds_: jc.LED);

					bool on = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).AppSettings.Settings["HomeLEDOn"].Value.ToLower() == "true";
					foreach (Joycon j in Program.mgr.j) {
						j.SetHomeLight(on);
					}

					jc.Begin();
					if (form.nonOriginal) {
						jc.getActiveData();
					}

				}
			}
		}

		public void Update() {
			for (int i = 0; i < j.Count; ++i)
				j[i].Update();
		}

		public void OnApplicationQuit() {
			foreach (Joycon v in j) {
				v.Detach();

				if (v.xin != null) {
					v.xin.Disconnect();
					v.xin.Dispose();
				}

				if (v.ds4 != null) {
					v.ds4.Disconnect();
					v.ds4.Dispose();
				}
			}

			controllerCheck.Stop();
			HIDapi.hid_exit();
		}
	}

	// Custom timer class because system timers have a limit of 15.6ms
	class HighResTimer {
		double interval = 0;
		double frequency = 0;

		Thread thread;

		public delegate void ActionDelegate();
		ActionDelegate func;

		bool run = false;

		public HighResTimer(double f, ActionDelegate a) {
			frequency = f;
			interval = 1.0 / f;

			func = a;
		}

		public void Start() {
			run = true;
			thread = new Thread(new ThreadStart(Run));
			thread.IsBackground = true;
			thread.Start();
		}

		void Run() {
			while (run) {
				func();
				int timeToSleep = (int)(interval * 1000);
				Thread.Sleep(timeToSleep);
			}
		}

		public void Stop() {
			run = false;
		}
	}

	class Program {
		public static PhysicalAddress btMAC = new PhysicalAddress(new byte[] { 0, 0, 0, 0, 0, 0 });
		public static UdpServer server;
		static double pollsPerSecond = 120.0;

		public static ViGEmClient emClient;

		private static readonly HttpClient client = new HttpClient();

		public static JoyconManager mgr;
		static HighResTimer timer;
		static string pid;

		static MainForm form;

		static public bool useHIDG = Boolean.Parse(ConfigurationManager.AppSettings["UseHIDG"]);

		private static WindowsInput.Events.Sources.IKeyboardEventSource keyboard;
		private static WindowsInput.Events.Sources.IMouseEventSource mouse;

		public static void Start() {
			pid = Process.GetCurrentProcess().Id.ToString(); // get current process id for HidCerberus.Srv

			if (useHIDG) {
				form.console.AppendText("HidGuardian is enabled.\r\n");
				try {
					var HidCerberusService = new ServiceController("HidCerberus Service");
					if (HidCerberusService.Status == ServiceControllerStatus.Stopped) {
						form.console.AppendText("HidGuardian was stopped. Starting...\r\n");

						try {
							HidCerberusService.Start();
						} catch (Exception e) {
							form.console.AppendText("Unable to start HidGuardian - everything should work fine without it, but if you need it, run the app again as an admin.\r\n");
							useHIDG = false;
						}
					}
				} catch (Exception e) {
					form.console.AppendText("Unable to start HidGuardian - everything should work fine without it, but if you need it, install it properly as admin.\r\n");
					useHIDG = false;
				}

				HttpWebResponse response;
				if (Boolean.Parse(ConfigurationManager.AppSettings["PurgeWhitelist"])) {
					try {
						response = (HttpWebResponse)WebRequest.Create(@"http://localhost:26762/api/v1/hidguardian/whitelist/purge/").GetResponse(); // remove all programs allowed to see controller
					} catch (Exception e) {
						form.console.AppendText("Unable to purge whitelist.\r\n");
						useHIDG = false;
					}
				}

				try {
					response = (HttpWebResponse)WebRequest.Create(@"http://localhost:26762/api/v1/hidguardian/whitelist/add/" + pid).GetResponse(); // add BetterJoyForCemu to allowed processes 
				} catch (Exception e) {
					form.console.AppendText("Unable to add program to whitelist.\r\n");
					useHIDG = false;
				}
			}

			if (Boolean.Parse(ConfigurationManager.AppSettings["ShowAsXInput"]) || Boolean.Parse(ConfigurationManager.AppSettings["ShowAsDS4"])) {
				try {
					emClient = new ViGEmClient(); // Manages emulated XInput
				} catch (Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException) {
					form.console.AppendText("Could not start VigemBus. Make sure drivers are installed correctly.\r\n");
				}
			}

			foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
				// Get local BT host MAC
				if (nic.NetworkInterfaceType != NetworkInterfaceType.FastEthernetFx && nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) {
					if (nic.Name.Split()[0] == "Bluetooth") {
						btMAC = nic.GetPhysicalAddress();
					}
				}
			}

			mgr = new JoyconManager();
			mgr.form = form;
			mgr.Awake();
			mgr.CheckForNewControllers();
			mgr.Start();

			server = new UdpServer(mgr.j);
			server.form = form;

			server.Start(IPAddress.Parse(ConfigurationManager.AppSettings["IP"]), Int32.Parse(ConfigurationManager.AppSettings["Port"]));
			timer = new HighResTimer(pollsPerSecond, new HighResTimer.ActionDelegate(mgr.Update));
			timer.Start();

			// Capture keyboard + mouse events for binding's sake
			keyboard = WindowsInput.Capture.Global.KeyboardAsync();
			keyboard.KeyEvent += Keyboard_KeyEvent;
			mouse = WindowsInput.Capture.Global.MouseAsync();
			mouse.MouseEvent += Mouse_MouseEvent;

			form.console.AppendText("All systems go\r\n");
		}

		private static void Mouse_MouseEvent(object sender, WindowsInput.Events.Sources.EventSourceEventArgs<WindowsInput.Events.Sources.MouseEvent> e) {
			if (e.Data.ButtonDown != null) {
				string res_val = Config.Value("reset_mouse");
				if (res_val.StartsWith("mse_"))
					if ((int)e.Data.ButtonDown.Button == Int32.Parse(res_val.Substring(4)))
						WindowsInput.Simulate.Events().MoveTo(Screen.PrimaryScreen.Bounds.Width / 2, Screen.PrimaryScreen.Bounds.Height / 2).Invoke();

				res_val = Config.Value("active_gyro");
				if (res_val.StartsWith("mse_"))
					if ((int)e.Data.ButtonDown.Button == Int32.Parse(res_val.Substring(4)))
						foreach (var i in mgr.j)
							i.active_gyro = true;
			}

			if (e.Data.ButtonUp != null) {
				string res_val = Config.Value("active_gyro");
				if (res_val.StartsWith("mse_"))
					if ((int)e.Data.ButtonUp.Button == Int32.Parse(res_val.Substring(4)))
						foreach (var i in mgr.j)
							i.active_gyro = false;
			}
		}

		private static void Keyboard_KeyEvent(object sender, WindowsInput.Events.Sources.EventSourceEventArgs<WindowsInput.Events.Sources.KeyboardEvent> e) {
			if (e.Data.KeyDown != null) {
				string res_val = Config.Value("reset_mouse");
				if (res_val.StartsWith("key_"))
					if ((int)e.Data.KeyDown.Key == Int32.Parse(res_val.Substring(4)))
						WindowsInput.Simulate.Events().MoveTo(Screen.PrimaryScreen.Bounds.Width / 2, Screen.PrimaryScreen.Bounds.Height / 2).Invoke();

				res_val = Config.Value("active_gyro");
				if (res_val.StartsWith("key_"))
					if ((int)e.Data.KeyDown.Key == Int32.Parse(res_val.Substring(4)))
						foreach (var i in mgr.j)
							i.active_gyro = true;
			}

			if (e.Data.KeyUp != null) {
				string res_val = Config.Value("active_gyro");
				if (res_val.StartsWith("key_"))
					if ((int)e.Data.KeyUp.Key == Int32.Parse(res_val.Substring(4)))
						foreach (var i in mgr.j)
							i.active_gyro = false;
			}
		}

		public static void Stop() {
			if (Program.useHIDG) {
				try {
					HttpWebResponse response = (HttpWebResponse)WebRequest.Create(@"http://localhost:26762/api/v1/hidguardian/whitelist/remove/" + pid).GetResponse();
				} catch (Exception e) {
					form.console.AppendText("Unable to remove program from whitelist.\r\n");
				}
			}

			if (Boolean.Parse(ConfigurationManager.AppSettings["PurgeAffectedDevices"]) && Program.useHIDG) {
				try {
					HttpWebResponse r1 = (HttpWebResponse)WebRequest.Create(@"http://localhost:26762/api/v1/hidguardian/affected/purge/").GetResponse();
				} catch { }
			}

			keyboard.Dispose(); mouse.Dispose();
			server.Stop();
			timer.Stop();
			mgr.OnApplicationQuit();
		}

		static void Main(string[] args) {
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			form = new MainForm();
			Application.Run(form);
		}
	}
}