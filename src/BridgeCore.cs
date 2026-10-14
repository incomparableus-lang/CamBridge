using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using LocosLab.VirtualCamera;

namespace CamBridge.Core
{
    #region logging

    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static void Init(string path)
        {
            _path = path;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                Roll(path, 512 * 1024);
            }
            catch { }
        }

        public static void Write(string message)
        {
            if (_path == null) return;
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(_path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }

        private static void Roll(string path, long maxBytes)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < maxBytes) return;
            string old = path + ".1";
            if (File.Exists(old)) File.Delete(old);
            File.Move(path, old);
        }
    }

    #endregion

    #region configuration

    public sealed class BridgeConfig
    {
        public string Device;        // DirectShow device name, e.g. "Look 320S"
        public string CameraName;    // friendly name shown to apps
        public string FfmpegPath;    // full path to ffmpeg.exe (may be empty -> autodetect)
        public int Width;
        public int Height;
        public int Fps;
        public bool AutoStart;
        public string DeviceHint;    // remembered name, used to re-pick the same camera
        public string Lang;          // "ru", "be", "ko", "fa", "zh", "hi"; empty = follow the system

        public static BridgeConfig Default()
        {
            return new BridgeConfig
            {
                Device = "",
                CameraName = "",
                FfmpegPath = "",
                Width = 640,
                Height = 480,
                Fps = 10,
                AutoStart = true,
                DeviceHint = "",
                Lang = ""
            };
        }

        public string ConfigDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CamBridge");
            }
        }

        public string ConfigPath { get { return Path.Combine(ConfigDir, "bridge.config"); } }

        public string ResolveFfmpeg()
        {
            string found = FfmpegLocator.Find(FfmpegPath, AppBaseDir);
            return found;
        }

        public static string AppBaseDir
        {
            get { return Path.GetDirectoryName(new Uri(AssemblyPath()).LocalPath); }
        }

        private static string AssemblyPath()
        {
            return typeof(BridgeConfig).Assembly.Location;
        }

        public static BridgeConfig Load(string path)
        {
            BridgeConfig c = Default();
            if (!File.Exists(path)) return c;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "device": c.Device = v; break;
                    case "cameraName": c.CameraName = v; break;
                    case "ffmpeg": c.FfmpegPath = v; break;
                    case "width": c.Width = ParseInt(v, c.Width); break;
                    case "height": c.Height = ParseInt(v, c.Height); break;
                    case "fps": c.Fps = ParseInt(v, c.Fps); break;
                    case "autoStart": c.AutoStart = ParseBool(v, c.AutoStart); break;
                    case "deviceHint": c.DeviceHint = v; break;
                    case "lang": c.Lang = v; break;
                }
            }
            return c;
        }

        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                StringWriter w = new StringWriter();
                w.WriteLine("# CamBridge configuration - edited by the installer or the tray app");
                w.WriteLine("device=" + Device);
                w.WriteLine("deviceHint=" + DeviceHint);
                w.WriteLine("cameraName=" + CameraName);
                w.WriteLine("ffmpeg=" + FfmpegPath);
                w.WriteLine("width=" + Width.ToString(CultureInfo.InvariantCulture));
                w.WriteLine("height=" + Height.ToString(CultureInfo.InvariantCulture));
                w.WriteLine("fps=" + Fps.ToString(CultureInfo.InvariantCulture));
                w.WriteLine("autoStart=" + (AutoStart ? "1" : "0"));
                w.WriteLine("lang=" + Lang);
                File.WriteAllText(path, w.ToString());
            }
            catch (Exception ex)
            {
                Log.Write("config save failed: " + ex.Message);
            }
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static bool ParseBool(string s, bool fallback)
        {
            if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }
    }

    #endregion

    #region ffmpeg discovery

    public static class FfmpegLocator
    {
        public static string Find(string configured, string baseDir)
        {
            // 1. explicit configuration
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                return configured;

            // 2. bundled next to the app  <base>\ffmpeg\ffmpeg.exe
            if (baseDir != null)
            {
                string bundled = Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe");
                if (File.Exists(bundled)) return bundled;
                bundled = Path.Combine(baseDir, "ffmpeg.exe");
                if (File.Exists(bundled)) return bundled;
            }

            // 3. system PATH
            string onPath = Which("ffmpeg.exe");
            if (onPath != null) return onPath;

            // 4. common install locations
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            };
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                try
                {
                    string[] candidates = Directory.GetFiles(root, "ffmpeg.exe", SearchOption.AllDirectories);
                    if (candidates.Length > 0) return candidates[0];
                }
                catch { }
            }
            return null;
        }

        private static string Which(string exe)
        {
            string pathVar = Environment.GetEnvironmentVariable("PATH");
            if (pathVar == null) return null;
            foreach (string dir in pathVar.Split(Path.PathSeparator))
            {
                if (dir.Length == 0) continue;
                try
                {
                    string full = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return null;
        }
    }

    #endregion

    #region DirectShow discovery through ffmpeg

    public static class DShow
    {
        public static List<string> ListCameras(string ffmpegPath)
        {
            List<string> result = new List<string>();
            if (ffmpegPath == null) return result;
            string text = Run(ffmpegPath, "-hide_banner -list_devices true -f dshow -i dummy");
            if (text == null) return result;
            Regex rx = new Regex("\"([^\"]+)\"\\s*\\(video\\)");
            foreach (Match m in rx.Matches(text))
            {
                string name = m.Groups[1].Value;
                // our own virtual camera also shows up in DirectShow; feeding the bridge
                // with its own output would build a feedback loop
                if (IsOwnVirtualCamera(name)) continue;
                if (!result.Contains(name)) result.Add(name);
            }
            return result;
        }

        public static bool IsOwnVirtualCamera(string name)
        {
            return name != null
                && name.IndexOf("(CamBridge)", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static List<string> ListResolutions(string ffmpegPath, string device)
        {
            List<string> result = new List<string>();
            if (ffmpegPath == null || string.IsNullOrEmpty(device)) return result;
            string text = Run(ffmpegPath,
                "-hide_banner -list_options true -f dshow -i video=\"" + device + "\"");
            if (text == null) return result;
            Regex rx = new Regex("min s=(\\d+x\\d+)");
            foreach (Match m in rx.Matches(text))
            {
                string s = m.Groups[1].Value;
                if (!result.Contains(s)) result.Add(s);
            }
            // stable, most useful first
            result.Sort(delegate (string a, string b) { return CompareSize(b, a); });
            return result;
        }

        private static int CompareSize(string a, string b)
        {
            int[] av = Split(a);
            int[] bv = Split(b);
            int ap = av[0] * av[1];
            int bp = bv[0] * bv[1];
            return ap.CompareTo(bp);
        }

        private static int[] Split(string s)
        {
            string[] p = s.Split('x');
            int w = 0, h = 0;
            int.TryParse(p[0], out w);
            if (p.Length > 1) int.TryParse(p[1], out h);
            return new int[] { w, h };
        }

        private static string Run(string exe, string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, arguments);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                // ffmpeg logs in the console output code page, but with no console
                // attached - which is our case, we are a windowed exe - it falls back to
                // UTF-8. Reading it as the ANSI code page turned "Виртуальная" into mojibake.
                psi.StandardOutputEncoding = Utf8;
                psi.StandardErrorEncoding = Utf8;

                using (Process p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    p.WaitForExit(20000);
                    if (!p.HasExited) { try { p.Kill(); } catch { } }
                    return Repair(so) + "\n" + Repair(se);
                }
            }
            catch (Exception ex)
            {
                Log.Write("ffmpeg probe failed: " + ex.Message);
                return null;
            }
        }

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, false);

        /// <summary>
        /// If the text came back as UTF-8 read through the wrong code page, every Cyrillic
        /// character became a Latin-1 range letter and the byte stream no longer decodes;
        /// push it back through the legacy code page to recover the original text.
        /// </summary>
        private static string Repair(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (raw.IndexOf('\uFFFD') < 0) return raw;
            try
            {
                return Encoding.GetEncoding(1251).GetString(Encoding.UTF8.GetBytes(raw));
            }
            catch { return raw; }
        }
    }

    #endregion

    #region frame source

    internal sealed class FrameSource : IDisposable
    {
        private readonly string _ffmpeg;
        private readonly string _device;
        private readonly int _width;
        private readonly int _height;
        private readonly int _srcFps;
        private readonly int _frameSize;
        private readonly byte[][] _buffers;
        private readonly object _lock = new object();
        private readonly ManualResetEventSlim _firstFrame = new ManualResetEventSlim(false);
        private readonly StringWriter _stderr = new StringWriter();

        private Process _proc;
        private Thread _reader;
        private volatile bool _running;
        private int _latest = -1;
        private long _frames;

        public FrameSource(string ffmpeg, string device, int width, int height, int srcFps)
        {
            _ffmpeg = ffmpeg;
            _device = device;
            _width = width;
            _height = height;
            _srcFps = srcFps;
            _frameSize = width * height * 4;
            _buffers = new byte[3][];
            for (int i = 0; i < _buffers.Length; i++) _buffers[i] = new byte[_frameSize];
        }

        public long FramesReceived { get { return Interlocked.Read(ref _frames); } }
        public int Width { get { return _width; } }
        public int Height { get { return _height; } }
        public bool IsAlive { get { return _running && _proc != null && !_proc.HasExited; } }
        public string Errors { get { lock (_stderr) { return _stderr.ToString(); } } }

        public bool Start(out string error)
        {
            error = null;
            ProcessStartInfo psi = new ProcessStartInfo(_ffmpeg, BuildArguments());
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            try
            {
                _proc = new Process();
                _proc.StartInfo = psi;
                if (!_proc.Start())
                {
                    error = "ffmpeg did not start";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "cannot launch ffmpeg: " + ex.Message;
                return false;
            }

            _proc.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (_stderr) { _stderr.WriteLine(e.Data); }
            };
            _proc.BeginErrorReadLine();

            _running = true;
            _reader = new Thread(ReadLoop);
            _reader.IsBackground = true;
            _reader.Name = "ffmpeg-reader";
            _reader.Start();
            return true;
        }

        private string BuildArguments()
        {
            StringWriter a = new StringWriter();
            a.Write("-hide_banner -loglevel warning ");
            a.Write("-f dshow -framerate {0} -i video=\"{1}\" ", _srcFps, _device);
            a.Write("-vf scale={0}:{1}:force_original_aspect_ratio=decrease,pad={0}:{1}:(ow-iw)/2:(oh-ih)/2:black "
                + "-pix_fmt bgra -f rawvideo -", _width, _height);
            return a.ToString();
        }

        private void ReadLoop()
        {
            Stream s = _proc.StandardOutput.BaseStream;
            int next = 0;
            try
            {
                while (_running)
                {
                    byte[] buf = _buffers[next];
                    int read = 0;
                    while (read < _frameSize)
                    {
                        int n = s.Read(buf, read, _frameSize - read);
                        if (n <= 0)
                        {
                            lock (_stderr)
                            {
                                _stderr.WriteLine("[bridge] ffmpeg closed stdout after "
                                    + read + "/" + _frameSize + " bytes");
                            }
                            _running = false;
                            return;
                        }
                        read += n;
                    }
                    lock (_lock) { _latest = next; next = (next + 1) % _buffers.Length; }
                    Interlocked.Increment(ref _frames);
                    _firstFrame.Set();
                }
            }
            catch (Exception ex)
            {
                lock (_stderr) { _stderr.WriteLine("[bridge] reader: " + ex.Message); }
                _running = false;
            }
        }

        public bool WaitForFirstFrame(TimeSpan timeout) { return _firstFrame.Wait(timeout); }

        public byte[] LatestFrame()
        {
            lock (_lock) { return _latest < 0 ? null : _buffers[_latest]; }
        }

        public void Dispose()
        {
            _running = false;
            if (_proc != null && !_proc.HasExited) { try { _proc.Kill(); } catch { } }
            if (_reader != null && _reader.IsAlive) { try { _reader.Join(2000); } catch { } }
            if (_proc != null)
            {
                try { _proc.WaitForExit(2000); } catch { }
                try { _proc.Dispose(); } catch { }
                _proc = null;
            }
        }
    }

    internal sealed class PipeFrameGeneratorFactory : FrameGeneratorFactory
    {
        private readonly FrameSource _src;
        public PipeFrameGeneratorFactory(FrameSource src) { _src = src; }

        public FrameGenerator CreateFrameGenerator(ushort width, ushort height)
        {
            Log.Write(string.Format("[cam] FrameServer requested {0}x{1}", width, height));
            return new PipeFrameGenerator(_src, width, height);
        }
    }

    internal sealed class PipeFrameGenerator : FrameGeneratorBase
    {
        private readonly FrameSource _src;
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _out;
        private readonly byte[] _black;
        private readonly bool _needsScale;
        private Bitmap _scaled;
        private Graphics _gfx;

        public PipeFrameGenerator(FrameSource src, ushort width, ushort height)
        {
            _src = src;
            _width = width;
            _height = height;
            int size = width * height * 4;
            _out = new byte[size];
            _black = new byte[size];
            _needsScale = (width != src.Width || height != src.Height);
            if (_needsScale)
            {
                _scaled = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                _gfx = Graphics.FromImage(_scaled);
                _gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            }
        }

        public override void CreateFrame(long time, BinaryWriter writer, uint bytes)
        {
            ThrottleFrameRate(time);
            int want = (int)bytes;
            try
            {
                byte[] frame = _src.LatestFrame();
                if (frame == null) { Write(writer, _black, want); return; }
                if (!_needsScale) { Write(writer, frame, want); return; }
                Scale(frame);
                Write(writer, _out, want);
            }
            catch (Exception ex)
            {
                Log.Write("[warn] CreateFrame: " + ex.Message);
                Write(writer, _black, want);
            }
        }

        private void Scale(byte[] frame)
        {
            Rectangle srcRect = new Rectangle(0, 0, _src.Width, _src.Height);
            Rectangle dstRect = new Rectangle(0, 0, _width, _height);
            using (Bitmap bmp = new Bitmap(_src.Width, _src.Height, PixelFormat.Format32bppRgb))
            {
                BitmapData bd = bmp.LockBits(srcRect, ImageLockMode.WriteOnly, bmp.PixelFormat);
                try { Marshal.Copy(frame, 0, bd.Scan0, Math.Min(frame.Length, bd.Stride * bmp.Height)); }
                finally { bmp.UnlockBits(bd); }

                _gfx.DrawImage(bmp, dstRect);

                BitmapData sd = _scaled.LockBits(dstRect, ImageLockMode.ReadOnly, _scaled.PixelFormat);
                try { Marshal.Copy(sd.Scan0, _out, 0, Math.Min(_out.Length, sd.Stride * _scaled.Height)); }
                finally { _scaled.UnlockBits(sd); }
            }
        }

        private static void Write(BinaryWriter writer, byte[] data, int want)
        {
            if (data.Length == want) { writer.Write(data); return; }
            if (data.Length > want) { writer.Write(data, 0, want); return; }
            writer.Write(data);
            writer.Write(new byte[want - data.Length]);
        }
    }

    #endregion

    #region runner

    /// <summary>
    /// Owns the whole chain on a dedicated MTA thread: ffmpeg -> named pipe -> Media Foundation.
    /// </summary>
    public sealed class BridgeRunner : IDisposable
    {
        private readonly BridgeConfig _cfg;
        private readonly ManualResetEvent _quit = new ManualResetEvent(false);
        private readonly object _lock = new object();

        private Thread _thread;
        private volatile bool _running;
        private volatile bool _cameraOk;
        private volatile string _lastError;
        private long _lastFrameCount;
        private DateTime _lastFrameTime;
        private long _prevFrames;
        private DateTime _prevSample;
        private volatile int _effectiveFpsX10;   // fps * 10; volatile needs an integral type
        private int _ticks;

        public BridgeRunner(BridgeConfig cfg)
        {
            _cfg = cfg;
            Log.Write("bridge starting: device='" + cfg.Device + "' " + cfg.Width + "x" + cfg.Height
                + " name='" + cfg.CameraName + "'");
        }

        public bool IsRunning { get { return _running; } }
        public bool CameraOk { get { return _cameraOk; } }
        public string LastError { get { return _lastError; } }
        public long Frames { get { return _lastFrameCount; } }

        /// <summary>
        /// Frames actually delivered by the physical camera per second, measured as the
        /// delta between two consecutive samples. Never a cumulative count divided by an
        /// arbitrary delay - that produced nonsense numbers.
        /// </summary>
        public double EffectiveFps { get { return _effectiveFpsX10 / 10.0; } }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "bridge";
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        private void Run()
        {
            _running = true;
            string ffmpeg = _cfg.ResolveFfmpeg();
            if (ffmpeg == null)
            {
                Fail("ffmpeg.exe not found");
                return;
            }
            Log.Write("ffmpeg: " + ffmpeg);

            List<string> cams = DShow.ListCameras(ffmpeg);
            Log.Write("[live] DirectShow cameras: " + string.Join(" | ", cams.ToArray()));

            FrameSource src = new FrameSource(ffmpeg, _cfg.Device, _cfg.Width, _cfg.Height, _cfg.Fps);
            string error;
            if (!src.Start(out error))
            {
                Fail(error);
                src.Dispose();
                return;
            }

            VirtualCamera camera = null;
            try
            {
                if (!src.WaitForFirstFrame(TimeSpan.FromSeconds(25)))
                {
                    Fail("no frames from '" + _cfg.Device + "' within 25s"
                        + Environment.NewLine + src.Errors);
                    src.Dispose();
                    return;
                }
                Log.Write("first frame received");

                camera = new VirtualCamera(_cfg.CameraName, (ushort)_cfg.Width, (ushort)_cfg.Height,
                    new PipeFrameGeneratorFactory(src));

                uint hr = camera.Start();
                if (hr != 0)
                {
                    Fail(string.Format("camera.Start() failed, HRESULT 0x{0:x8}", hr));
                    src.Dispose();
                    return;
                }

                _cameraOk = true;
                _lastFrameTime = DateTime.Now;
                _lastFrameCount = src.FramesReceived;
                _prevFrames = _lastFrameCount;
                _prevSample = _lastFrameTime;
                _effectiveFpsX10 = 0;
                _ticks = 0;
                Log.Write("virtual camera registered: " + _cfg.CameraName);

                while (!_quit.WaitOne(1000))
                {
                    if (!src.IsAlive)
                    {
                        Fail("ffmpeg exited" + Environment.NewLine + src.Errors);
                        break;
                    }

                    DateTime now = DateTime.Now;
                    long total = src.FramesReceived;
                    double seconds = (now - _prevSample).TotalSeconds;

                    if (seconds > 0.05)
                    {
                        double fps = (total - _prevFrames) / seconds;
                        // smooth it so the tray text does not flicker between samples
                        double smoothed = _effectiveFpsX10 <= 0
                            ? fps
                            : (EffectiveFps * 0.6 + fps * 0.4);
                        _effectiveFpsX10 = (int)Math.Round(smoothed * 10);
                        _prevFrames = total;
                        _prevSample = now;
                    }

                    _lastFrameCount = total;
                    _lastFrameTime = now;

                    _ticks++;
                    if (_ticks % 30 == 0)
                    {
                        Log.Write(string.Format("[live] {0} frames total, {1} fps from '{2}'",
                            total, EffectiveFps, _cfg.Device));
                    }
                }

                _cameraOk = false;
                try
                {
                    if (camera.Stop() == 0) Log.Write("virtual camera removed");
                }
                catch (Exception ex) { Log.Write("camera.Stop(): " + ex.Message); }
            }
            catch (Exception ex)
            {
                Fail("unexpected: " + ex);
            }
            finally
            {
                _cameraOk = false;
                _running = false;
                src.Dispose();
            }
        }

        private void Fail(string message)
        {
            _lastError = message;
            Log.Write("ERROR " + message);
        }

        public void Stop()
        {
            _quit.Set();
            Thread t = _thread;
            if (t != null && t.IsAlive) { try { t.Join(6000); } catch { } }
            _thread = null;
        }

        public void Dispose()
        {
            lock (_lock) { Stop(); }
        }
    }

    #endregion

    #region autostart

    public static class AutoStart
    {
        public const string TaskName = "CamBridge";

        public static bool IsEnabled()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", "/Query /TN \"" + TaskName + "\"");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        public static bool SetEnabled(bool enabled, string exePath)
        {
            try
            {
                string args = enabled
                    ? "/Create /TN \"" + TaskName + "\" /TR \"'" + exePath + "'\" /SC ONLOGON /RL LIMITED /F"
                    : "/Delete /TN \"" + TaskName + "\" /F";
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    p.WaitForExit(20000);
                    if (p.ExitCode != 0) Log.Write("schtasks failed: " + so + se);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Log.Write("schtasks exception: " + ex.Message);
                return false;
            }
        }
    }

    #endregion
}
