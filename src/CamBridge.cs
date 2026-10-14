using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using CamBridge.Core;

namespace CamBridge
{
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _settingsItem;
        private readonly ToolStripMenuItem _restartItem;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly ToolStripMenuItem _logsItem;
        private readonly ToolStripMenuItem _exitItem;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly string _configPath;

        private BridgeConfig _cfg;
        private BridgeRunner _runner;
        private bool _errorShown;

        public TrayContext()
        {
            _configPath = Path.Combine(BridgeConfig.Default().ConfigDir, "bridge.config");
            Log.Init(Path.Combine(BridgeConfig.Default().ConfigDir, "bridge.log"));
            Log.Write("CamBridge " + typeof(TrayContext).Assembly.GetName().Version
                + " starting, language " + S.Lang);

            _cfg = BridgeConfig.Load(_configPath);
            S.SetLanguage(_cfg.Lang);
            Log.Write("UI language: " + S.Lang);

            _statusItem = new ToolStripMenuItem();
            _statusItem.Enabled = false;
            _settingsItem = new ToolStripMenuItem();
            _settingsItem.Click += delegate { OpenSettings(); };
            _restartItem = new ToolStripMenuItem();
            _restartItem.Click += delegate { Restart(); };
            _autoStartItem = new ToolStripMenuItem();
            _autoStartItem.Click += delegate { ToggleAutoStart(); };
            _logsItem = new ToolStripMenuItem();
            _logsItem.Click += delegate { OpenLogs(); };
            _exitItem = new ToolStripMenuItem();
            _exitItem.Click += delegate { Shutdown(); };

            _menu = new ContextMenuStrip();
            if (S.IsRtl) _menu.RightToLeft = RightToLeft.Yes;
            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_settingsItem);
            _menu.Items.Add(_restartItem);
            _menu.Items.Add(_autoStartItem);
            _menu.Items.Add(_logsItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_exitItem);
            ApplyMenuText();

            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = S.T("app");
            _tray.ContextMenuStrip = _menu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { OpenSettings(); };

            RefreshAutoStartItem();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { UpdateStatus(); };
            _timer.Start();

            StartBridge();
        }

        private void ApplyMenuText()
        {
            _settingsItem.Text = S.T("menu_settings");
            _restartItem.Text = S.T("menu_restart");
            _autoStartItem.Text = S.T("menu_autostart");
            _logsItem.Text = S.T("menu_logs");
            _exitItem.Text = S.T("menu_exit");
        }

        private void StartBridge()
        {
            if (string.IsNullOrEmpty(_cfg.Device))
            {
                Log.Write("no device configured - start the installer first");
                Balloon(S.T("balloon_not_configured"), S.T("balloon_not_configured_hint"), true);
                UpdateStatus();
                return;
            }
            try
            {
                _runner = new BridgeRunner(_cfg);
                _runner.Start();
                _errorShown = false;
            }
            catch (Exception ex)
            {
                Log.Write("start failed: " + ex);
                Balloon(S.T("app"), S.T("balloon_start_failed") + " " + ex.Message, true);
            }
        }

        private void StopBridge()
        {
            if (_runner != null)
            {
                _runner.Dispose();
                _runner = null;
            }
        }

        private void Restart()
        {
            Log.Write("restart requested");
            StopBridge();
            StartBridge();
        }

        private void UpdateStatus()
        {
            if (_runner == null || !_runner.IsRunning)
            {
                _statusItem.Text = S.T("tray_status_stopped");
                return;
            }
            if (!_runner.CameraOk)
            {
                _statusItem.Text = S.T("tray_status_starting");
                string err = _runner.LastError;
                if (!string.IsNullOrEmpty(err) && !_errorShown)
                {
                    _errorShown = true;
                    Log.Write("reported error: " + err);
                    Balloon(S.T("balloon_error"), FirstLine(err), true);
                }
                return;
            }
            _statusItem.Text = S.F("tray_status_active", _runner.EffectiveFps);
        }

        private static string FirstLine(string s)
        {
            if (s == null) return "";
            int i = s.IndexOfAny(new char[] { '\r', '\n' });
            return i > 0 ? s.Substring(0, i) : s;
        }

        private void RefreshAutoStartItem()
        {
            _autoStartItem.Checked = AutoStart.IsEnabled();
        }

        private void ToggleAutoStart()
        {
            string exe = Application.ExecutablePath;
            bool want = !AutoStart.IsEnabled();
            if (AutoStart.SetEnabled(want, exe))
            {
                _cfg.AutoStart = want;
                _cfg.Save(_configPath);
                RefreshAutoStartItem();
                Balloon(S.T("app"),
                    want ? S.T("autostart_on") : S.T("autostart_off"), false);
            }
            else
            {
                MessageBox.Show(S.T("autostart_failed"), S.T("app"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenSettings()
        {
            bool restartApp;
            try
            {
                using (SettingsForm form = new SettingsForm(_configPath))
                {
                    form.ShowDialog();
                    restartApp = form.RestartRequested;
                }
                if (restartApp)
                {
                    Relaunch();
                    return;
                }
                _cfg = BridgeConfig.Load(_configPath);
                RefreshAutoStartItem();
                Restart();
            }
            catch (Exception ex)
            {
                Log.Write("settings failed: " + ex);
            }
        }

        private static void Relaunch()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = false
                });
            }
            catch { }
            Application.Exit();
        }

        private void OpenLogs()
        {
            try
            {
                string dir = BridgeConfig.Default().ConfigDir;
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            }
            catch { }
        }

        private void Balloon(string title, string text, bool error)
        {
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.BalloonTipIcon = error ? ToolTipIcon.Error : ToolTipIcon.Info;
                _tray.ShowBalloonTip(6000);
            }
            catch { }
        }

        private void Shutdown()
        {
            Log.Write("exiting from tray");
            _timer.Stop();
            StopBridge();
            _tray.Visible = false;
            _tray.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _runner != null) _runner.Dispose();
            base.Dispose(disposing);
        }

        private static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (SolidBrush body = new SolidBrush(Color.FromArgb(45, 95, 170)))
                        g.FillRectangle(body, 3, 12, 19, 14);
                    using (SolidBrush bump = new SolidBrush(Color.FromArgb(45, 95, 170)))
                        g.FillRectangle(bump, 8, 8, 8, 5);
                    using (SolidBrush lens = new SolidBrush(Color.White))
                        g.FillEllipse(lens, 19, 13, 11, 11);
                    using (SolidBrush inner = new SolidBrush(Color.FromArgb(45, 95, 170)))
                        g.FillEllipse(inner, 22, 16, 5, 5);
                }
                IntPtr h = bmp.GetHicon();
                using (Icon tmp = Icon.FromHandle(h))
                {
                    return (Icon)tmp.Clone();
                }
            }
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly string _configPath;
        private BridgeConfig _cfg;
        private string _startLang;

        private readonly ComboBox _device = new ComboBox();
        private readonly ComboBox _resolution = new ComboBox();
        private readonly ComboBox _language = new ComboBox();
        private readonly NumericUpDown _fps = new NumericUpDown();
        private readonly TextBox _cameraName = new TextBox();
        private readonly TextBox _ffmpegPath = new TextBox();
        private readonly CheckBox _autoStart = new CheckBox();
        private readonly Button _refresh = new Button();
        private readonly Button _browse = new Button();
        private readonly Button _apply = new Button();
        private readonly Button _close = new Button();

        private Label _lDevice, _lResolution, _lFps, _lCameraName, _lFfmpeg, _lAutoStart, _lLanguage;

        public bool RestartRequested { get; private set; }

        public SettingsForm(string configPath)
        {
            _configPath = configPath;
            _cfg = BridgeConfig.Load(configPath);
            _startLang = S.Lang;
            Build();
            Populate();
        }

        private void Build()
        {
            Text = S.T("settings_title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 330);
            if (S.IsRtl) RightToLeft = RightToLeft.Yes;

            _lDevice = Lbl(15, 15, "lbl_device");
            _device.Left = 180; _device.Top = 12; _device.Width = 250;
            _device.DropDownStyle = ComboBoxStyle.DropDownList;

            _refresh.Text = S.T("btn_refresh");
            _refresh.Left = 438; _refresh.Top = 11; _refresh.Width = 70;
            _refresh.Click += delegate { Populate(); };

            _lResolution = Lbl(15, 50, "lbl_resolution");
            _resolution.Left = 180; _resolution.Top = 47; _resolution.Width = 120;
            _resolution.DropDownStyle = ComboBoxStyle.DropDownList;
            _resolution.SelectedIndexChanged += delegate { ApplyResolutionToName(); };

            _lFps = Lbl(15, 85, "lbl_fps");
            _fps.Left = 180; _fps.Top = 82; _fps.Width = 80;
            _fps.Minimum = 1; _fps.Maximum = 30;

            _lCameraName = Lbl(15, 120, "lbl_camera_name");
            _cameraName.Left = 180; _cameraName.Top = 117; _cameraName.Width = 330;

            _lFfmpeg = Lbl(15, 155, "lbl_ffmpeg");
            _ffmpegPath.Left = 180; _ffmpegPath.Top = 152; _ffmpegPath.Width = 220;

            _browse.Text = S.T("btn_browse");
            _browse.Left = 402; _browse.Top = 151; _browse.Width = 108;
            _browse.Click += delegate
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Filter = "ffmpeg.exe|ffmpeg.exe|*.*|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _ffmpegPath.Text = dlg.FileName;
            };

            _lAutoStart = Lbl(15, 190, "lbl_autostart");
            _autoStart.Left = 180; _autoStart.Top = 187; _autoStart.Width = 330;
            _autoStart.CheckedChanged += delegate
            {
                AutoStart.SetEnabled(_autoStart.Checked, Application.ExecutablePath);
            };

            _lLanguage = Lbl(15, 222, "lbl_language");
            _language.Left = 180; _language.Top = 219; _language.Width = 200;
            _language.DropDownStyle = ComboBoxStyle.DropDownList;

            _apply.Text = S.T("btn_save");
            _apply.Left = 180; _apply.Top = 266; _apply.Width = 110;
            _apply.Click += delegate { Save(); Close(); };

            _close.Text = S.T("btn_cancel");
            _close.Left = 300; _close.Top = 266; _close.Width = 110;
            _close.Click += delegate { Close(); };

            Controls.AddRange(new Control[]
            {
                _lDevice, _device, _refresh, _lResolution, _resolution, _lFps, _fps,
                _lCameraName, _cameraName, _lFfmpeg, _ffmpegPath, _browse,
                _lAutoStart, _autoStart, _lLanguage, _language, _apply, _close
            });
        }

        private Label Lbl(int x, int y, string key)
        {
            return new Label { Text = S.T(key), Left = x, Top = y, Width = 165 };
        }

        private void Populate()
        {
            string ffmpeg = FfmpegLocator.Find(_cfg.FfmpegPath, BridgeConfig.AppBaseDir);

            _device.Items.Clear();
            _device.SelectedIndexChanged -= delegate { };
            List<string> cams = DShow.ListCameras(ffmpeg);
            foreach (string c in cams) _device.Items.Add(c);
            if (cams.Count == 0) _device.Items.Add(S.T("no_cameras"));

            string want = !string.IsNullOrEmpty(_cfg.Device) ? _cfg.Device : _cfg.DeviceHint;
            int idx = want != null ? _device.Items.IndexOf(want) : -1;
            if (idx < 0 && !string.IsNullOrEmpty(want))
            {
                _device.Items.Add(want);
                idx = _device.Items.Count - 1;
            }
            _device.SelectedIndex = idx >= 0 ? idx : (_device.Items.Count > 0 ? 0 : -1);
            PopulateResolutions();

            _fps.Value = Math.Min(Math.Max(_cfg.Fps, 1), 30);
            _cameraName.Text = _cfg.CameraName;
            _ffmpegPath.Text = _cfg.FfmpegPath;
            _autoStart.Checked = AutoStart.IsEnabled();

            _language.Items.Clear();
            for (int i = 0; i < S.Codes.Length; i++) _language.Items.Add(S.Names[i]);
            _language.SelectedIndex = Math.Max(0, Array.IndexOf(S.Codes, S.Lang));
        }

        private void PopulateResolutions()
        {
            _resolution.Items.Clear();
            string ffmpeg = FfmpegLocator.Find(_cfg.FfmpegPath, BridgeConfig.AppBaseDir);
            string dev = _device.SelectedItem as string;
            List<string> res = DShow.ListResolutions(ffmpeg, dev);
            foreach (string r in res) _resolution.Items.Add(r);
            string want = _cfg.Width + "x" + _cfg.Height;
            int i = _resolution.Items.IndexOf(want);
            _resolution.SelectedIndex = i >= 0 ? i : (_resolution.Items.Count > 0 ? 0 : -1);
            ApplyResolutionToName();
        }

        private void ApplyResolutionToName()
        {
            string res = _resolution.SelectedItem as string;
            if (string.IsNullOrEmpty(res)) return;
            string[] p = res.Split('x');
            int w = 0, h = 0;
            int.TryParse(p[0], out w);
            if (p.Length > 1) int.TryParse(p[1], out h);
            string dev = _device.SelectedItem as string;
            string baseName = string.IsNullOrEmpty(dev) ? S.T("default_camera_name") : dev;
            _cameraName.Text = baseName + " (CamBridge)";
            _cfg.Width = w; _cfg.Height = h;
        }

        private void Save()
        {
            _cfg.Device = _device.SelectedItem as string ?? "";
            _cfg.DeviceHint = _cfg.Device;
            string res = _resolution.SelectedItem as string;
            if (!string.IsNullOrEmpty(res))
            {
                string[] p = res.Split('x');
                int w = 0, h = 0;
                if (int.TryParse(p[0], out w)) _cfg.Width = w;
                if (p.Length > 1 && int.TryParse(p[1], out h)) _cfg.Height = h;
            }
            _cfg.Fps = (int)_fps.Value;
            _cfg.CameraName = _cameraName.Text.Trim();
            _cfg.FfmpegPath = _ffmpegPath.Text.Trim();
            _cfg.AutoStart = _autoStart.Checked;

            int li = _language.SelectedIndex;
            _cfg.Lang = (li >= 0 && li < S.Codes.Length) ? S.Codes[li] : "";

            _cfg.Save(_configPath);
            Log.Write("settings saved: " + _cfg.Device + " " + _cfg.Width + "x" + _cfg.Height
                + " lang=" + _cfg.Lang);

            // a language change needs new strings everywhere, so restart the process
            S.SetLanguage(_cfg.Lang);
            if (S.Lang != _startLang) RestartRequested = true;
        }
    }

    internal static class Program
    {
        private const string MutexName = "Local\\CamBridge.SingleInstance";

        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex m = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    Log.Init(Path.Combine(BridgeConfig.Default().ConfigDir, "bridge.log"));
                    MessageBox.Show(S.T("already_running"), S.T("app"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
                {
                    Log.Write("UI exception: " + e.Exception);
                };

                using (TrayContext ctx = new TrayContext())
                {
                    Application.Run(ctx);
                }
            }
        }
    }
}
