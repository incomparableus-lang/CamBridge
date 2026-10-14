using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CamBridge.Core;

namespace CamBridgeSetup
{
    #region payload extraction

    internal sealed class SubStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        public SubStream(Stream inner, long length)
        {
            _inner = inner;
            _remaining = length;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return _remaining; } }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int want = (int)Math.Min(count, _remaining);
            if (want <= 0) return 0;
            int n = _inner.Read(buffer, offset, want);
            _remaining -= n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); }
        public override void SetLength(long v) { throw new NotSupportedException(); }
        public override void Write(byte[] b, int o, int c) { throw new NotSupportedException(); }
    }

    internal static class Payload
    {
        public static string TempDir { get; private set; }

        /// <summary>
        /// The payload zip is appended directly after the exe stub. Its length is derived
        /// from the End Of Central Directory record found near the end of the file, so the
        /// zip needs no extra bookkeeping and the stub stays a valid PE.
        /// </summary>
        public static long GetStubLength(string exePath)
        {
            using (FileStream fs = File.Open(exePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int tailLen = (int)Math.Min(fs.Length, 66000);
                if (fs.Length < tailLen + 22) throw new InvalidDataException("installer is truncated");

                byte[] tail = new byte[tailLen];
                fs.Seek(fs.Length - tailLen, SeekOrigin.Begin);
                fs.Read(tail, 0, tailLen);

                for (int i = tailLen - 22; i >= 0; i--)
                {
                    if (tail[i] != 0x50 || tail[i + 1] != 0x4b ||
                        tail[i + 2] != 0x05 || tail[i + 3] != 0x06) continue;

                    ushort commentLen = BitConverter.ToUInt16(tail, i + 20);
                    if (i + 22 + commentLen != tailLen) continue;   // not the real EOCD

                    uint cdSize = BitConverter.ToUInt32(tail, i + 12);
                    uint cdOffset = BitConverter.ToUInt32(tail, i + 16);

                    // archive length = (EOCD + comment) + central directory, where the
                    // central directory offset is relative to the start of the archive
                    long archiveLen = (tailLen - i) + cdOffset + cdSize;
                    long stub = fs.Length - archiveLen;
                    if (stub > 0 && stub < fs.Length) return stub;
                }
            }
            throw new InvalidDataException("no appended payload found");
        }

        public static string Extract(string exePath, Action<int, string> progress)
        {
            long stub = GetStubLength(exePath);
            TempDir = Path.Combine(Path.GetTempPath(),
                "CamBridge-Setup-" + Process.GetCurrentProcess().Id);

            using (FileStream fs = File.Open(exePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stub <= 0 || stub >= fs.Length)
                    throw new InvalidDataException("payload not appended to the installer");
                fs.Seek(stub, SeekOrigin.Begin);
                using (SubStream sub = new SubStream(fs, fs.Length - stub))
                using (ZipArchive zip = new ZipArchive(sub, ZipArchiveMode.Read))
                {
                    List<ZipArchiveEntry> entries = new List<ZipArchiveEntry>(zip.Entries);
                    int total = entries.Count;
                    int done = 0;
                    foreach (ZipArchiveEntry e in entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;   // directory marker
                        string dest = Path.Combine(TempDir, e.FullName);
                        string dir = Path.GetDirectoryName(dest);
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        using (Stream src = e.Open())
                        using (FileStream dst = new FileStream(dest, FileMode.Create, FileAccess.Write))
                        {
                            src.CopyTo(dst);
                        }
                        done++;
                        if (progress != null) progress(done * 100 / total, e.FullName);
                    }
                }
            }
            return TempDir;
        }
    }

    #endregion

    #region wizard

    internal sealed class WizardForm : Form
    {
        private readonly string _payloadDir;

        private Label _title = new Label();
        private Label _status = new Label();
        private Label _detail = new Label();
        private ProgressBar _progress = new ProgressBar();
        private Button _back = new Button();
        private Button _next = new Button();
        private Button _cancel = new Button();

        private ComboBox _device = new ComboBox();
        private ComboBox _resolution = new ComboBox();
        private ComboBox _language = new ComboBox();
        private NumericUpDown _fps = new NumericUpDown();
        private Label _testResult = new Label();
        private Label _languageLabel = new Label();
        private Button _test = new Button();

        private int _page;
        private bool _installDone;
        private string _ffmpeg;

        public WizardForm(string payloadDir)
        {
            _payloadDir = payloadDir;
            Build();
            PopulateLanguages();
            Go(0);
        }

        private void PopulateLanguages()
        {
            _language.Items.Clear();
            for (int i = 0; i < S.Codes.Length; i++) _language.Items.Add(S.Names[i]);
            _language.SelectedIndex = Math.Max(0, Array.IndexOf(S.Codes, S.Lang));
        }

        public void Status(string text)
        {
            _status.Text = text;
            Application.DoEvents();
        }

        public void Progress(int percent, string detail)
        {
            int v = percent;
            if (v < 0) v = 0;
            if (v > 100) v = 100;
            _progress.Value = v;
            _detail.Text = detail ?? "";
            Application.DoEvents();
        }

        private void Build()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 340);

            _title.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            _title.AutoSize = false;
            _title.SetBounds(20, 18, 520, 60);

            _status.SetBounds(20, 84, 520, 22);
            _detail.SetBounds(20, 106, 520, 20);
            _detail.ForeColor = Color.DimGray;

            _progress.SetBounds(20, 134, 520, 18);

            _languageLabel.SetBounds(20, 160, 160, 22);
            _language.SetBounds(190, 157, 200, 22);
            _language.DropDownStyle = ComboBoxStyle.DropDownList;
            _language.SelectedIndexChanged += delegate { OnLanguageChanged(); };

            _device.SetBounds(20, 170, 350, 22);
            _device.DropDownStyle = ComboBoxStyle.DropDownList;
            _device.SelectedIndexChanged += delegate { PopulateResolutions(); };

            _resolution.SetBounds(390, 170, 150, 22);
            _resolution.DropDownStyle = ComboBoxStyle.DropDownList;

            _fps.SetBounds(20, 205, 80, 22);
            _fps.Minimum = 1; _fps.Maximum = 30;
            _fps.Value = 10;

            _test.SetBounds(120, 203, 130, 25);
            _test.Click += delegate { RunTest(); };

            _testResult.SetBounds(270, 205, 270, 22);

            _back.SetBounds(290, 296, 80, 27);
            _back.Click += delegate { if (_page > 0) Go(_page - 1); };

            _next.SetBounds(380, 296, 80, 27);
            _next.Click += delegate { Next(); };

            _cancel.SetBounds(470, 296, 80, 27);
            _cancel.Click += delegate { Close(); };

            Controls.AddRange(new Control[]
            {
                _title, _status, _detail, _progress, _languageLabel, _language,
                _device, _resolution, _fps, _test, _testResult,
                _back, _next, _cancel
            });
        }

        private void OnLanguageChanged()
        {
            int i = _language.SelectedIndex;
            if (i < 0 || i >= S.Codes.Length) return;
            S.SetLanguage(S.Codes[i]);
            ApplyTexts();
        }

        private void ApplyTexts()
        {
            Text = S.T("setup_title");
            if (S.IsRtl) RightToLeft = RightToLeft.Yes;

            _languageLabel.Text = S.T("lbl_language");
            _test.Text = S.T("btn_test");
            _back.Text = S.T("btn_back");
            _cancel.Text = S.T("btn_quit");
            _next.Text = _page == 2 ? S.T("btn_finish") : S.T("btn_next");

            switch (_page)
            {
                case 0:
                    _title.Text = S.T("setup_page1_title") + "\r\n\r\n" + S.T("setup_page1_body");
                    _status.Text = S.T("setup_preparing");
                    _languageLabel.Visible = true;
                    _language.Visible = true;
                    break;
                case 1:
                    _title.Text = S.T("setup_page2_title") + "\r\n\r\n" + S.T("setup_page2_body");
                    _status.Text = S.F("setup_step", 2);
                    _languageLabel.Visible = false;
                    _language.Visible = false;
                    break;
                default:
                    _title.Text = S.T("setup_page3_title") + "\r\n\r\n" + S.T("setup_page3_body");
                    _status.Text = S.F("setup_step", 3);
                    _languageLabel.Visible = false;
                    _language.Visible = false;
                    break;
            }
        }

        private void Go(int page)
        {
            _page = page;
            bool onDevice = (page == 1);
            _device.Visible = _resolution.Visible = _fps.Visible = onDevice;
            _test.Visible = _testResult.Visible = onDevice;
            _next.Visible = true;

            _back.Enabled = page > 0;
            _next.Enabled = !onDevice;
            _progress.Visible = (page == 0);

            ApplyTexts();
        }

        private void Next()
        {
            if (_page == 0)
            {
                if (!_installDone)
                {
                    _status.Text = S.T("setup_installing");
                    Application.DoEvents();
                    string error;
                    if (!Installer.Install(_payloadDir, this, out error))
                    {
                        MessageBox.Show(error, S.T("err_setup_failed_title"),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        _status.Text = S.T("setup_error");
                        return;
                    }
                    _installDone = true;
                }
                _ffmpeg = Installer.InstalledFfmpeg();
                PopulateDevices();
                Go(1);
            }
            else if (_page == 1)
            {
                Installer.WriteConfig(_device.SelectedItem as string,
                    _resolution.SelectedItem as string, (int)_fps.Value, ChosenLanguage());
                Installer.EnableAutoStart(true);
                Installer.StartBridge();
                Installer.RegisterUninstallEntry();
                Go(2);
            }
            else
            {
                Close();
            }
        }

        private string ChosenLanguage()
        {
            int i = _language.SelectedIndex;
            return (i >= 0 && i < S.Codes.Length) ? S.Codes[i] : "";
        }

        private void PopulateDevices()
        {
            _device.Items.Clear();
            List<string> cams = DShow.ListCameras(_ffmpeg);
            foreach (string c in cams) _device.Items.Add(c);
            if (cams.Count == 0) _device.Items.Add(S.T("no_cameras"));
            _device.SelectedIndex = _device.Items.Count > 0 ? 0 : -1;
            _fps.Value = 10;
        }

        private void PopulateResolutions()
        {
            _resolution.Items.Clear();
            string dev = _device.SelectedItem as string;
            List<string> res = DShow.ListResolutions(_ffmpeg, dev);
            foreach (string r in res) _resolution.Items.Add(r);
            int i = _resolution.Items.IndexOf("640x480");
            _resolution.SelectedIndex = i >= 0 ? i : (_resolution.Items.Count > 0 ? 0 : -1);
        }

        private void RunTest()
        {
            _testResult.ForeColor = Color.DimGray;
            _testResult.Text = S.T("testing");
            Application.DoEvents();
            Installer.WriteConfig(_device.SelectedItem as string,
                _resolution.SelectedItem as string, (int)_fps.Value, ChosenLanguage());
            string message;
            bool ok = Installer.SelfTest(12000, out message);
            _testResult.ForeColor = ok ? Color.DarkGreen : Color.DarkRed;
            _testResult.Text = ok ? S.T("test_ok") : FirstLine(message);
        }

        private static string FirstLine(string s)
        {
            if (s == null) return "";
            int i = s.IndexOfAny(new char[] { '\r', '\n' });
            return i > 0 ? s.Substring(0, i) : s;
        }
    }

    #endregion

    #region installer operations

    internal static class Installer
    {
        public static string ProgramDir
        {
            get { return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CamBridge"); }
        }

        private static string ConfigPath
        {
            get { return Path.Combine(BridgeConfig.Default().ConfigDir, "bridge.config"); }
        }

        public static string InstalledFfmpeg()
        {
            return Path.Combine(ProgramDir, "ffmpeg", "ffmpeg.exe");
        }

        public static bool Install(string payloadDir, WizardForm ui, out string error)
        {
            error = null;
            try
            {
                // an upgrade has to replace our own running files first
                KillBridge();

                Directory.CreateDirectory(ProgramDir);

                Say(ui, S.T("inst_copy"));
                Step(ui, 0);

                CopyResource(Path.Combine(payloadDir, "CamBridge.exe"),
                    Path.Combine(ProgramDir, "CamBridge.exe"));
                CopyResource(Path.Combine(payloadDir, "Uninstall.exe"),
                    Path.Combine(ProgramDir, "Uninstall.exe"));
                CopyResource(Path.Combine(payloadDir, "LICENSE-locoslab.rtf"),
                    Path.Combine(ProgramDir, "LICENSE-locoslab.rtf"));

                Say(ui, S.T("inst_ffmpeg"));
                Step(ui, 40);
                string ffmpegDir = Path.Combine(ProgramDir, "ffmpeg");
                Directory.CreateDirectory(ffmpegDir);
                CopyResource(Path.Combine(payloadDir, "ffmpeg", "ffmpeg.exe"),
                    Path.Combine(ffmpegDir, "ffmpeg.exe"));
                CopyResource(Path.Combine(payloadDir, "LICENSE-ffmpeg.txt"),
                    Path.Combine(ffmpegDir, "LICENSE.txt"));

                Say(ui, S.T("inst_vc_check"));
                Step(ui, 70);
                if (!HasVCRuntime())
                {
                    if (!InstallVCRuntime(ui, out error)) return false;
                }

                Say(ui, S.T("inst_mf"));
                Step(ui, 85);
                string msi = Path.Combine(payloadDir, "VirtualCamera.msi");
                File.Copy(msi, Path.Combine(ProgramDir, "VirtualCamera.msi"), true);
                int rc = RunMsi("/i", Path.Combine(ProgramDir, "VirtualCamera.msi"));
                if (rc != 0 && rc != 3010 && rc != 1641)
                {
                    error = S.F("err_msi", rc) + Environment.NewLine + S.T("err_msi_hint");
                    return false;
                }

                Step(ui, 100);
                Say(ui, S.T("inst_done"));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void Say(WizardForm ui, string text)
        {
            Log.Write("install: " + text);
            if (ui != null) ui.Status(text);
        }

        private static void Step(WizardForm ui, int percent)
        {
            if (ui != null) ui.Progress(percent, "");
        }

        private static void CopyResource(string src, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(src, dest, true);
        }

        public static bool HasVCRuntime()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
                {
                    if (k == null) return false;
                    object v = k.GetValue("Installed");
                    return v != null && Convert.ToInt32(v) == 1;
                }
            }
            catch { return false; }
        }

        private static bool InstallVCRuntime(WizardForm ui, out string error)
        {
            error = null;
            string url = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
            string local = Path.Combine(Path.GetTempPath(), "vc_redist.x64.exe");
            try
            {
                Say(ui, S.T("inst_vc_download"));
                Step(ui, 75);
                System.Net.WebClient wc = new System.Net.WebClient();
                wc.DownloadFile(url, local);
                Say(ui, S.T("inst_vc_install"));
                Step(ui, 78);
                int rc = Run(local, "/install /quiet /norestart", true);
                if (rc != 0 && rc != 3010 && rc != 1638)
                {
                    error = S.F("err_vc", rc);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = S.T("err_vc_download") + "\r\n" + ex.Message;
                return false;
            }
        }

        private static int RunMsi(string verb, string msi)
        {
            return Run("msiexec.exe", verb + " \"" + msi + "\" /qn /norestart", true);
        }

        private static int Run(string exe, string args, bool wait)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (wait) p.WaitForExit(600000);
                    return wait ? p.ExitCode : 0;
                }
            }
            catch (Exception ex)
            {
                Log.Write("run " + exe + " failed: " + ex.Message);
                return -1;
            }
        }

        public static void WriteConfig(string device, string resolution, int fps, string lang)
        {
            BridgeConfig cfg = BridgeConfig.Load(ConfigPath);
            cfg.Device = device ?? "";
            cfg.DeviceHint = cfg.Device;
            cfg.FfmpegPath = InstalledFfmpeg();

            if (!string.IsNullOrEmpty(resolution))
            {
                string[] p = resolution.Split('x');
                int w = 0, h = 0;
                if (int.TryParse(p[0], out w)) cfg.Width = w;
                if (p.Length > 1 && int.TryParse(p[1], out h)) cfg.Height = h;
            }
            if (fps >= 1) cfg.Fps = fps;
            if (!string.IsNullOrEmpty(lang)) cfg.Lang = lang;
            string baseName = string.IsNullOrEmpty(cfg.Device) ? S.T("default_camera_name") : cfg.Device;
            cfg.CameraName = baseName + " (CamBridge)";
            cfg.Save(ConfigPath);
        }

        public static bool SelfTest(int millis, out string message)
        {
            message = "";
            KillBridge();
            Process p = StartBridge();
            if (p == null)
            {
                message = S.T("balloon_start_failed");
                return false;
            }
            Thread.Sleep(millis);
            string logDir = BridgeConfig.Default().ConfigDir;
            string status = ReadLogTail(logDir);
            if (status.Contains("virtual camera registered"))
            {
                message = S.T("test_ok");
                KillBridge();
                return true;
            }
            int errPos = status.LastIndexOf("ERROR ");
            message = errPos >= 0 ? status.Substring(errPos + 6) : "no success report in the log";
            KillBridge();
            return false;
        }

        public static string ReadLogTail(string logDir)
        {
            try
            {
                string p = Path.Combine(logDir, "bridge.log");
                if (!File.Exists(p)) return "";
                string[] lines = File.ReadAllLines(p);
                int start = Math.Max(0, lines.Length - 60);
                StringBuilder sb = new StringBuilder();
                for (int i = start; i < lines.Length; i++) sb.AppendLine(lines[i]);
                return sb.ToString();
            }
            catch { return ""; }
        }

        public static Process StartBridge()
        {
            try
            {
                string exe = Path.Combine(ProgramDir, "CamBridge.exe");
                if (!File.Exists(exe)) return null;
                return Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                Log.Write("start bridge failed: " + ex.Message);
                return null;
            }
        }

        public static void KillBridge()
        {
            foreach (string name in new string[] { "CamBridge", "ffmpeg" })
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(name))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }
            }
            Thread.Sleep(1500);
        }

        public static void EnableAutoStart(bool enabled)
        {
            AutoStart.SetEnabled(enabled, Path.Combine(ProgramDir, "CamBridge.exe"));
        }

        public static void RegisterUninstallEntry()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine
                    .CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CamBridge"))
                {
                    k.SetValue("DisplayName", S.T("arp_name"));
                    k.SetValue("DisplayVersion",
                        typeof(Installer).Assembly.GetName().Version.ToString(3));
                    k.SetValue("Publisher", "CamBridge");
                    k.SetValue("DisplayIcon", Path.Combine(ProgramDir, "CamBridge.exe"));
                    k.SetValue("UninstallString",
                        "\"" + Path.Combine(ProgramDir, "Uninstall.exe") + "\"");
                    k.SetValue("QuietUninstallString",
                        "\"" + Path.Combine(ProgramDir, "Uninstall.exe") + "\" /silent");
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    k.SetValue("NoModify", 1);
                    k.SetValue("NoRepair", 1);
                    k.SetValue("EstimatedSize", 103000);
                }
            }
            catch (Exception ex)
            {
                Log.Write("register uninstall failed: " + ex.Message);
            }
        }
    }

    #endregion

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                RealMain(args);
            }
            catch (Exception ex)
            {
                // the user config directory may not be writable; keep a copy next to the exe
                string fallback = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath), "setup-crash.log");
                try
                {
                    File.WriteAllText(fallback, DateTime.Now + Environment.NewLine + ex.ToString());
                }
                catch { }
                try
                {
                    if (HasArg("/silent")) { Console.WriteLine("CRASH: " + ex); return; }
                    MessageBox.Show(S.T("err_crash") + "\n\n" + ex.Message
                        + "\n\n" + S.T("err_details") + " " + fallback,
                        S.T("setup_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }
        }

        private static void RealMain(string[] args)
        {
            bool silent = HasArg("/silent");

            if (silent)
            {
                // a windowed exe has no console of its own; borrow the caller's so that
                // scripted deployments and logs work
                if (!AttachConsole(-1)) AllocConsole();
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!IsAdmin() && !HasArg("/noelevate"))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(
                        Application.ExecutablePath, QuoteArgs(Environment.GetCommandLineArgs()));
                    psi.Verb = "runas";
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch { }
                return;
            }

            Log.Init(Path.Combine(BridgeConfig.Default().ConfigDir, "setup.log"));

            // an existing install keeps its language; a fresh install follows the system
            BridgeConfig existing = BridgeConfig.Load(BridgeConfig.Default().ConfigPath);
            S.SetLanguage(existing.Lang);

            string payloadDir;
            try
            {
                payloadDir = Payload.Extract(Application.ExecutablePath, null);
            }
            catch (Exception ex)
            {
                Log.Write("payload extraction failed: " + ex);
                if (silent) { Console.WriteLine("FAILED: " + ex.Message); return; }
                MessageBox.Show(S.T("err_extract") + "\n" + ex.Message,
                    S.T("setup_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (silent)
            {
                RunSilent(payloadDir);
                return;
            }

            using (WizardForm form = new WizardForm(payloadDir))
            {
                Application.Run(form);
            }

            Cleanup();
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        /// <summary>Unattended mode: install, pick the first DirectShow camera, start the bridge.</summary>
        private static void RunSilent(string payloadDir)
        {
            string error;
            Log.Write("silent install started");
            if (!Installer.Install(payloadDir, null, out error))
            {
                Log.Write("silent install FAILED: " + error);
                Console.WriteLine("FAILED: " + error);
                return;
            }

            string ffmpeg = Installer.InstalledFfmpeg();
            List<string> cams = DShow.ListCameras(ffmpeg);
            string device = cams.Count > 0 ? cams[0] : "";
            Console.WriteLine("cameras found: " + string.Join(" | ", cams.ToArray()));

            List<string> res = DShow.ListResolutions(ffmpeg, device);
            string chosen = res.Contains("640x480") ? "640x480"
                : (res.Count > 0 ? res[0] : "640x480");

            Installer.WriteConfig(device, chosen, 10, S.Lang);
            Installer.EnableAutoStart(true);
            Installer.RegisterUninstallEntry();
            Installer.StartBridge();

            Log.Write("silent install finished");
            Console.WriteLine("OK device='" + device + "' " + chosen + " @10");
            Cleanup();
        }

        private static void Cleanup()
        {
            try
            {
                if (Directory.Exists(Payload.TempDir)) Directory.Delete(Payload.TempDir, true);
            }
            catch { }
        }

        private static bool HasArg(string name)
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (a.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string QuoteArgs(string[] args)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                if (args[i].IndexOf(' ') >= 0) sb.Append('"').Append(args[i]).Append('"');
                else sb.Append(args[i]);
            }
            return sb.ToString();
        }

        private static bool IsAdmin()
        {
            System.Security.Principal.WindowsIdentity id =
                System.Security.Principal.WindowsIdentity.GetCurrent();
            System.Security.Principal.WindowsPrincipal pr =
                new System.Security.Principal.WindowsPrincipal(id);
            return pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }
}
