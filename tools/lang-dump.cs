using System;
using System.IO;
using System.Text;
using CamBridge.Core;

internal static class LangDump
{
    private static void Main()
    {
        string[] keys =
        {
            "menu_settings", "menu_restart", "menu_autostart", "menu_logs", "menu_exit",
            "tray_status_active", "tray_status_stopped", "balloon_not_configured",
            "settings_title", "lbl_device", "lbl_resolution", "lbl_fps", "lbl_camera_name",
            "lbl_autostart", "lbl_language", "btn_refresh", "btn_save", "btn_cancel",
            "btn_browse", "btn_test", "no_cameras", "test_ok",
            "setup_title", "setup_page1_title", "setup_page1_body", "setup_page2_title",
            "setup_page3_title", "setup_step", "btn_next", "btn_back", "btn_finish",
            "inst_mf", "inst_done", "err_msi", "err_vc_download",
            "uninst_title", "uninst_question", "uninst_body", "uninst_error", "arp_name"
        };

        StringBuilder sb = new StringBuilder();
        foreach (string code in S.Codes)
        {
            S.SetLanguage(code);
            sb.AppendLine("=== " + code + "  RTL=" + S.IsRtl + " ===");
            foreach (string k in keys)
            {
                string v = S.T(k);
                if (v == k) sb.AppendLine("  !! MISSING " + k);
                else sb.AppendLine("  " + k.PadRight(24) + " " + Trunc(v));
            }
            sb.AppendLine();
        }

        using (StreamWriter w = new StreamWriter(
            @"J:\cam-bridge\lang-dump.txt", false, new UTF8Encoding(false)))
        {
            w.Write(sb.ToString());
        }
    }

    private static string Trunc(string s)
    {
        string one = s.Replace("\r\n", " ").Replace("\n", " ");
        return one.Length <= 70 ? one : one.Substring(0, 70) + "...";
    }
}
