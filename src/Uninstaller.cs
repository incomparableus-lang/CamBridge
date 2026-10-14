using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using CamBridge.Core;

namespace CamBridgeUninstall
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool silent = false;
            foreach (string a in args)
                if (a.Equals("/silent", StringComparison.OrdinalIgnoreCase)) silent = true;

            Log.Init(Path.Combine(BridgeConfig.Default().ConfigDir, "setup.log"));
            S.SetLanguage(BridgeConfig.Load(
                Path.Combine(BridgeConfig.Default().ConfigDir, "bridge.config")).Lang);

            if (!IsAdmin())
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(
                        Application.ExecutablePath, "\"" + string.Join(" ", args) + "\"");
                    psi.Verb = "runas";
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch { }
                return;
            }

            if (!silent)
            {
                DialogResult r = MessageBox.Show(
                    S.T("uninst_question") + "\n\n" + S.T("uninst_body"),
                    S.T("uninst_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }

            try
            {
                DoUninstall();
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show(S.T("uninst_error") + "\n" + ex.Message,
                        S.T("app"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void DoUninstall()
        {
            string programDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CamBridge");

            Log.Write("uninstall started");

            // 1. stop the bridge and its ffmpeg child
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
            Thread.Sleep(2000);

            // 2. remove the autostart task
            AutoStart.SetEnabled(false, "");

            // 3. remove the Media Foundation media source
            string msi = Path.Combine(programDir, "VirtualCamera.msi");
            if (File.Exists(msi))
            {
                Log.Write("removing virtual camera component");
                Run("msiexec.exe", "/x \"" + msi + "\" /qn /norestart");
            }
            else
            {
                Log.Write("VirtualCamera.msi missing, trying product code uninstall");
                Run("msiexec.exe", "/x {19B4C9C0-4A1C-4E4C-9F3B-2E1D9C6B7A11} /qn /norestart");
            }

            // 4. flush the frame server so the virtual camera disappears immediately
            RestartService("FrameServer");
            RestartService("FrameServerMonitor");

            // 5. drop the Add/Remove Programs entry
            try
            {
                Microsoft.Win32.Registry.LocalMachine
                    .DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CamBridge",
                        false);
            }
            catch (Exception ex) { Log.Write("uninstall entry: " + ex.Message); }

            // 6. remove our files. Uninstall.exe is still running from that folder, so the
            //    final wipe is handed to a detached process that waits for us to exit.
            try
            {
                if (Directory.Exists(programDir))
                {
                    StartDetachedDelete(programDir);
                    Log.Write("scheduled removal of " + programDir);
                }
            }
            catch (Exception ex)
            {
                Log.Write("could not remove " + programDir + ": " + ex.Message);
            }

            // 7. user config + logs
            string cfgDir = BridgeConfig.Default().ConfigDir;
            try
            {
                if (Directory.Exists(cfgDir)) Directory.Delete(cfgDir, true);
            }
            catch (Exception ex) { Log.Write("could not remove " + cfgDir + ": " + ex.Message); }

            Log.Write("uninstall finished");
        }

        private static void StartDetachedDelete(string dir)
        {
            string bat = Path.Combine(Path.GetTempPath(),
                "cambridge-cleanup-" + Process.GetCurrentProcess().Id + ".cmd");
            File.WriteAllText(bat,
                "@echo off" + Environment.NewLine +
                "ping -n 6 127.0.0.1 >nul" + Environment.NewLine +
                "rmdir /s /q \"" + dir + "\"" + Environment.NewLine +
                "del /q \"" + bat + "\"" + Environment.NewLine);

            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(psi);
        }

        private static void RestartService(string name)
        {
            try
            {
                Run("net.exe", "stop " + name + " /y");
                Thread.Sleep(1500);
                Run("net.exe", "start " + name);
            }
            catch (Exception ex) { Log.Write("service " + name + ": " + ex.Message); }
        }

        private static void Run(string exe, string args)
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
                    p.WaitForExit(120000);
                }
            }
            catch (Exception ex)
            {
                Log.Write("run " + exe + ": " + ex.Message);
            }
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
