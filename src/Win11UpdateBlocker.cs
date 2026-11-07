using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Win11UpdateBlocker
{
    #region Win32 API & Hak Istimewa Sistem
    internal static class NativeMethods
    {
        public const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
        public const int TOKEN_QUERY = 0x0008;
        public const string SE_TAKE_OWNERSHIP_NAME = "SeTakeOwnershipPrivilege";
        public const string SE_RESTORE_NAME = "SeRestorePrivilege";
        public const string SE_BACKUP_NAME = "SeBackupPrivilege";
        public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
    }

    public static class PrivilegeManager
    {
        public static bool EnablePrivilege(string privilegeName)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                IntPtr processHandle = Process.GetCurrentProcess().Handle;
                if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out tokenHandle))
                {
                    return false;
                }

                NativeMethods.LUID luid;
                if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out luid))
                {
                    return false;
                }

                NativeMethods.TOKEN_PRIVILEGES tp = new NativeMethods.TOKEN_PRIVILEGES();
                tp.PrivilegeCount = 1;
                tp.Privileges = new NativeMethods.LUID_AND_ATTRIBUTES();
                tp.Privileges.Luid = luid;
                tp.Privileges.Attributes = NativeMethods.SE_PRIVILEGE_ENABLED;

                return NativeMethods.AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(tokenHandle);
                }
            }
        }

        public static void GrantFullControlToRegistryKey(string subKeyPath)
        {
            try
            {
                EnablePrivilege(NativeMethods.SE_TAKE_OWNERSHIP_NAME);
                EnablePrivilege(NativeMethods.SE_RESTORE_NAME);
                EnablePrivilege(NativeMethods.SE_BACKUP_NAME);

                SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

                // 1. Ambil kepemilikan kunci registry
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (RegistryKey key = baseKey.OpenSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.TakeOwnership))
                    {
                        if (key != null)
                        {
                            RegistrySecurity sec = key.GetAccessControl();
                            sec.SetOwner(adminSid);
                            key.SetAccessControl(sec);
                        }
                    }
                }

                // 2. Berikan hak Full Control ke Builtin Administrators
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (RegistryKey key = baseKey.OpenSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions))
                    {
                        if (key != null)
                        {
                            RegistrySecurity sec = key.GetAccessControl();
                            sec.AddAccessRule(new RegistryAccessRule(
                                adminSid,
                                RegistryRights.FullControl,
                                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                                PropagationFlags.None,
                                AccessControlType.Allow));
                            key.SetAccessControl(sec);
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
    #endregion

    #region Tema Desain Minimalis-Slate & Komponen Glass
    public static class UITheme
    {
        // Palet Warna Minimalis Slate
        public static readonly Color BgApp = Color.FromArgb(15, 23, 42);          // Slate 900
        public static readonly Color BgHeader = Color.FromArgb(11, 15, 25);       // Slate 950
        public static readonly Color BgCard = Color.FromArgb(22, 31, 48);         // Translucent Slate Card
        public static readonly Color BgCardAlt = Color.FromArgb(17, 24, 39);      // Darker Slate Card
        public static readonly Color BgInput = Color.FromArgb(9, 13, 22);         // Input / Log Box Background

        public static readonly Color BorderCard = Color.FromArgb(41, 53, 72);     // Slate Border
        public static readonly Color BorderSubtle = Color.FromArgb(30, 41, 59);   // Divider
        public static readonly Color BorderHighlight = Color.FromArgb(59, 75, 99); // Active Highlight

        public static readonly Color TextPrimary = Color.FromArgb(248, 250, 252);  // Slate 50 (Kontras Tinggi)
        public static readonly Color TextSecondary = Color.FromArgb(148, 163, 184); // Slate 400
        public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);    // Slate 500

        // Status Aksen (Memenuhi Kontras WCAG AA >= 4.5:1)
        public static readonly Color EmeraldText = Color.FromArgb(110, 231, 183); // Emerald 300
        public static readonly Color EmeraldBg = Color.FromArgb(6, 78, 59);       // Emerald 900
        public static readonly Color EmeraldBorder = Color.FromArgb(16, 185, 129); // Emerald 500

        public static readonly Color RoseText = Color.FromArgb(254, 205, 211);    // Rose 200
        public static readonly Color RoseBg = Color.FromArgb(136, 19, 55);        // Rose 900
        public static readonly Color RoseBorder = Color.FromArgb(244, 63, 94);    // Rose 500

        public static readonly Color AmberText = Color.FromArgb(253, 230, 138);   // Amber 200
        public static readonly Color AmberBg = Color.FromArgb(120, 53, 15);       // Amber 900
        public static readonly Color AmberBorder = Color.FromArgb(217, 119, 6);   // Amber 600

        public static readonly Color SkyText = Color.FromArgb(186, 230, 253);     // Sky 200
        public static readonly Color SkyBorder = Color.FromArgb(56, 189, 248);    // Sky 400

        public static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath CreateTopRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddLine(rect.Right, rect.Bottom, rect.Left, rect.Bottom);
            path.CloseFigure();
            return path;
        }
    }

    public class GlassPanel : Panel
    {
        public Color CardBgColor { get; set; }
        public Color CardBorderColor { get; set; }
        public int CornerRadius { get; set; }
        public bool ShowTopHighlight { get; set; }

        public GlassPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            CardBgColor = UITheme.BgCard;
            CardBorderColor = UITheme.BorderCard;
            CornerRadius = 10;
            ShowTopHighlight = true;
            Padding = new Padding(14);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 2 || rect.Height <= 2) return;

            using (GraphicsPath path = UITheme.CreateRoundedPath(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(CardBgColor))
                {
                    g.FillPath(brush, path);
                }

                if (ShowTopHighlight && rect.Height > 16)
                {
                    Rectangle sheenRect = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, 18);
                    using (LinearGradientBrush sheenBrush = new LinearGradientBrush(
                        sheenRect,
                        Color.FromArgb(28, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255),
                        LinearGradientMode.Vertical))
                    {
                        using (GraphicsPath topPath = UITheme.CreateTopRoundedPath(sheenRect, CornerRadius))
                        {
                            g.FillPath(sheenBrush, topPath);
                        }
                    }
                }

                using (Pen pen = new Pen(CardBorderColor, 1.0f))
                {
                    g.DrawPath(pen, path);
                }
            }

            base.OnPaint(e);
        }
    }

    public class GlassButton : Button
    {
        private bool isHovered = false;
        private bool isPressed = false;

        public Color BaseColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color BorderLineColor { get; set; }
        public int CornerRadius { get; set; }

        public GlassButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BaseColor = Color.FromArgb(30, 41, 59);
            HoverColor = Color.FromArgb(51, 65, 85);
            PressedColor = Color.FromArgb(15, 23, 42);
            BorderLineColor = Color.FromArgb(71, 85, 105);
            CornerRadius = 8;
            ForeColor = UITheme.TextPrimary;
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            UseMnemonic = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            isPressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            isPressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(1, 1, Width - 3, Height - 3);
            if (rect.Width <= 2 || rect.Height <= 2) return;

            Color fill = !Enabled ? Color.FromArgb(20, 28, 42) : (isPressed ? PressedColor : (isHovered ? HoverColor : BaseColor));
            Color border = !Enabled ? Color.FromArgb(35, 45, 60) : BorderLineColor;
            Color textCol = !Enabled ? UITheme.TextMuted : ForeColor;

            using (GraphicsPath path = UITheme.CreateRoundedPath(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                if (Enabled && rect.Height > 14)
                {
                    Rectangle sheenRect = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height / 2);
                    using (LinearGradientBrush sheen = new LinearGradientBrush(
                        sheenRect,
                        Color.FromArgb(26, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255),
                        LinearGradientMode.Vertical))
                    {
                        using (GraphicsPath sheenPath = UITheme.CreateTopRoundedPath(sheenRect, CornerRadius))
                        {
                            g.FillPath(sheen, sheenPath);
                        }
                    }
                }

                using (Pen pen = new Pen(border, 1.2f))
                {
                    g.DrawPath(pen, path);
                }
            }

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
            Rectangle textRect = new Rectangle(6, 4, Width - 12, Height - 8);
            TextRenderer.DrawText(g, Text, Font, textRect, textCol, flags);
        }
    }
    #endregion

    #region Model Status
    public class ServiceInfoItem
    {
        public string ServiceName { get; set; }
        public string DisplayName { get; set; }
        public string StartMode { get; set; }
        public string Status { get; set; }
    }

    public class SystemStatusSnapshot
    {
        public bool IsPausedTo2077 { get; set; }
        public string PauseExpiryString { get; set; }
        public bool IsGroupPolicyLocked { get; set; }
        public string TargetReleaseVersion { get; set; }
        public bool IsDriverUpdateBlocked { get; set; }
        public bool IsTotalLockdown { get; set; }
        public List<ServiceInfoItem> Services { get; set; }
        public long CacheSizeBytes { get; set; }

        public SystemStatusSnapshot()
        {
            Services = new List<ServiceInfoItem>();
        }
    }
    #endregion

    #region Engine Pemblokiran
    public static class UpdateBlockerEngine
    {
        private const string REG_UX_SETTINGS = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        private const string REG_POLICY_WU = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        private const string REG_POLICY_AU = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
        private const string REG_SERVICES_BASE = @"SYSTEM\CurrentControlSet\Services";

        private static readonly string[] TARGET_SERVICES = new string[]
        {
            "wuauserv",
            "UsoSvc",
            "WaaSMedicSvc",
            "BITS",
            "DoSvc"
        };

        private static readonly string[] TARGET_TASKS = new string[]
        {
            @"\Microsoft\Windows\WindowsUpdate\Scheduled Start",
            @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan",
            @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan Static Task",
            @"\Microsoft\Windows\UpdateOrchestrator\UpdateModelTask",
            @"\Microsoft\Windows\UpdateOrchestrator\USO_UxBroker",
            @"\Microsoft\Windows\WaaSMedic\PerformRemediation"
        };

        public static string GetWindowsVersionDisplay()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("DisplayVersion");
                        if (val != null) return val.ToString();
                        val = key.GetValue("ReleaseId");
                        if (val != null) return val.ToString();
                    }
                }
            }
            catch
            {
            }
            return "24H2";
        }

        public static SystemStatusSnapshot ScanCurrentStatus()
        {
            SystemStatusSnapshot snapshot = new SystemStatusSnapshot();

            // 1. Status Pause 2077
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(REG_UX_SETTINGS, false))
                {
                    if (key != null)
                    {
                        object expiry = key.GetValue("PauseUpdatesExpiryTime");
                        if (expiry != null)
                        {
                            string s = expiry.ToString();
                            snapshot.PauseExpiryString = s;
                            if (s.IndexOf("2077") >= 0)
                            {
                                snapshot.IsPausedTo2077 = true;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(snapshot.PauseExpiryString))
            {
                snapshot.PauseExpiryString = "Tidak Dijeda";
            }

            // 2. Status Group Policy
            try
            {
                using (RegistryKey keyAU = Registry.LocalMachine.OpenSubKey(REG_POLICY_AU, false))
                {
                    if (keyAU != null)
                    {
                        object noAuto = keyAU.GetValue("NoAutoUpdate");
                        if (noAuto != null && Convert.ToInt32(noAuto) == 1)
                        {
                            snapshot.IsGroupPolicyLocked = true;
                        }
                    }
                }

                using (RegistryKey keyWU = Registry.LocalMachine.OpenSubKey(REG_POLICY_WU, false))
                {
                    if (keyWU != null)
                    {
                        object trv = keyWU.GetValue("TargetReleaseVersionInfo");
                        if (trv != null)
                        {
                            snapshot.TargetReleaseVersion = trv.ToString();
                        }

                        object exDrivers = keyWU.GetValue("ExcludeWUDriversInQualityUpdate");
                        if (exDrivers != null && Convert.ToInt32(exDrivers) == 1)
                        {
                            snapshot.IsDriverUpdateBlocked = true;
                        }

                        object disUX = keyWU.GetValue("SetDisableUXWUAccess");
                        if (disUX != null && Convert.ToInt32(disUX) == 1)
                        {
                            snapshot.IsTotalLockdown = true;
                        }
                    }
                }
            }
            catch
            {
            }

            // 3. Status Layanan Windows
            int disabledCount = 0;
            foreach (string svcName in TARGET_SERVICES)
            {
                ServiceInfoItem item = new ServiceInfoItem();
                item.ServiceName = svcName;
                item.DisplayName = GetServiceFriendlyName(svcName);

                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(REG_SERVICES_BASE + @"\" + svcName, false))
                    {
                        if (key != null)
                        {
                            object startVal = key.GetValue("Start");
                            if (startVal != null)
                            {
                                int s = Convert.ToInt32(startVal);
                                if (s == 4)
                                {
                                    item.StartMode = "Disabled";
                                    disabledCount++;
                                }
                                else if (s == 3)
                                {
                                    item.StartMode = "Manual";
                                }
                                else if (s == 2)
                                {
                                    item.StartMode = "Automatic";
                                }
                                else
                                {
                                    item.StartMode = "Mode (" + s + ")";
                                }
                            }
                            else
                            {
                                item.StartMode = "Unknown";
                            }
                        }
                        else
                        {
                            item.StartMode = "Tidak Terpasang";
                        }
                    }
                }
                catch
                {
                    item.StartMode = "Akses Ditolak";
                }

                try
                {
                    using (ServiceController sc = new ServiceController(svcName))
                    {
                        item.Status = sc.Status.ToString();
                    }
                }
                catch
                {
                    item.Status = "Stopped";
                }

                snapshot.Services.Add(item);
            }

            if (disabledCount >= 4 && snapshot.IsGroupPolicyLocked)
            {
                snapshot.IsTotalLockdown = true;
            }

            snapshot.CacheSizeBytes = CalculateCacheSize();
            return snapshot;
        }

        private static string GetServiceFriendlyName(string name)
        {
            if (name == "wuauserv") return "Windows Update (wuauserv)";
            if (name == "UsoSvc") return "Update Orchestrator (UsoSvc)";
            if (name == "WaaSMedicSvc") return "Update Medic Service (WaaSMedicSvc)";
            if (name == "BITS") return "Background Intelligent Transfer (BITS)";
            if (name == "DoSvc") return "Delivery Optimization (DoSvc)";
            return name;
        }

        public static void ApplySmartPause2077(bool excludeDrivers, Action<string> log)
        {
            log("[MODE 1] Menerapkan Smart Pause 2077: Proteksi aktif, Store dan Defender tetap aman...");

            try
            {
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(REG_UX_SETTINGS))
                {
                    if (key != null)
                    {
                        string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                        string expiry2077 = "2077-12-31T23:59:59Z";

                        key.SetValue("FlightSettingsMaxPauseDays", 20000, RegistryValueKind.DWord);
                        key.SetValue("PauseFeatureUpdatesStartTime", nowUtc, RegistryValueKind.String);
                        key.SetValue("PauseFeatureUpdatesEndTime", expiry2077, RegistryValueKind.String);
                        key.SetValue("PauseQualityUpdatesStartTime", nowUtc, RegistryValueKind.String);
                        key.SetValue("PauseQualityUpdatesEndTime", expiry2077, RegistryValueKind.String);
                        key.SetValue("PauseUpdatesStartTime", nowUtc, RegistryValueKind.String);
                        key.SetValue("PauseUpdatesExpiryTime", expiry2077, RegistryValueKind.String);

                        log("[OK] Tanggal Pause Update dikunci ke: 2077-12-31");
                    }
                }
            }
            catch (Exception ex)
            {
                log("[GALAT] Registry Pause 2077: " + ex.Message);
            }

            try
            {
                string winVer = GetWindowsVersionDisplay();
                using (RegistryKey keyWU = Registry.LocalMachine.CreateSubKey(REG_POLICY_WU))
                {
                    if (keyWU != null)
                    {
                        keyWU.SetValue("TargetReleaseVersion", 1, RegistryValueKind.DWord);
                        keyWU.SetValue("TargetReleaseVersionInfo", winVer, RegistryValueKind.String);
                        keyWU.SetValue("ProductVersion", "Windows 11", RegistryValueKind.String);
                        keyWU.SetValue("SetDisableUXWUAccess", 0, RegistryValueKind.DWord);

                        if (excludeDrivers)
                        {
                            keyWU.SetValue("ExcludeWUDriversInQualityUpdate", 1, RegistryValueKind.DWord);
                            log("[OK] Blokir Driver Otomatis diaktifkan.");
                        }
                        else
                        {
                            keyWU.DeleteValue("ExcludeWUDriversInQualityUpdate", false);
                        }

                        log("[OK] Group Policy Target Release: Windows 11 " + winVer);
                    }
                }

                using (RegistryKey keyAU = Registry.LocalMachine.CreateSubKey(REG_POLICY_AU))
                {
                    if (keyAU != null)
                    {
                        keyAU.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                        keyAU.SetValue("AUOptions", 2, RegistryValueKind.DWord);
                        log("[OK] Group Policy NoAutoUpdate: 1 (Dinonaktifkan)");
                    }
                }
            }
            catch (Exception ex)
            {
                log("[GALAT] Group Policy: " + ex.Message);
            }

            // Mode 1: Matikan WaaSMedicSvc dan UsoSvc
            // Biarkan wuauserv, BITS, DoSvc pada posisi MANUAL agar Store dan Defender normal
            log("[INFO] Mengatur layanan: Mematikan WaaSMedicSvc dan UsoSvc...");
            SetServiceStartMode("WaaSMedicSvc", 4, true, log);
            SetServiceStartMode("UsoSvc", 4, true, log);

            SetServiceStartMode("wuauserv", 3, false, log);
            SetServiceStartMode("BITS", 3, false, log);
            SetServiceStartMode("DoSvc", 3, false, log);

            DisableScheduledTasks(log);
            log("[SELESAI] Mode 1 aktif: Windows Update OS dijeda hingga 2077.");
        }

        public static void ApplyTotalLockdown(bool excludeDrivers, Action<string> log)
        {
            log("[MODE 2] Menerapkan Total Lockdown: Mematikan seluruh 5 layanan Windows Update...");

            try
            {
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(REG_UX_SETTINGS))
                {
                    if (key != null)
                    {
                        string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                        string expiry2077 = "2077-12-31T23:59:59Z";

                        key.SetValue("FlightSettingsMaxPauseDays", 20000, RegistryValueKind.DWord);
                        key.SetValue("PauseFeatureUpdatesStartTime", nowUtc, RegistryValueKind.String);
                        key.SetValue("PauseFeatureUpdatesEndTime", expiry2077, RegistryValueKind.String);
                        key.SetValue("PauseQualityUpdatesStartTime", nowUtc, RegistryValueKind.String);
                        key.SetValue("PauseQualityUpdatesEndTime", expiry2077, RegistryValueKind.String);
                        key.SetValue("PauseUpdatesStartTime", nowUtc, RegistryValueKind.String);
                        key.SetValue("PauseUpdatesExpiryTime", expiry2077, RegistryValueKind.String);
                    }
                }

                string winVer = GetWindowsVersionDisplay();
                using (RegistryKey keyWU = Registry.LocalMachine.CreateSubKey(REG_POLICY_WU))
                {
                    if (keyWU != null)
                    {
                        keyWU.SetValue("TargetReleaseVersion", 1, RegistryValueKind.DWord);
                        keyWU.SetValue("TargetReleaseVersionInfo", winVer, RegistryValueKind.String);
                        keyWU.SetValue("ProductVersion", "Windows 11", RegistryValueKind.String);
                        keyWU.SetValue("SetDisableUXWUAccess", 1, RegistryValueKind.DWord);

                        if (excludeDrivers)
                        {
                            keyWU.SetValue("ExcludeWUDriversInQualityUpdate", 1, RegistryValueKind.DWord);
                        }

                        log("[OK] Akses UI Windows Update dikunci total (SetDisableUXWUAccess = 1).");
                    }
                }

                using (RegistryKey keyAU = Registry.LocalMachine.CreateSubKey(REG_POLICY_AU))
                {
                    if (keyAU != null)
                    {
                        keyAU.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                        keyAU.SetValue("AUOptions", 2, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex)
            {
                log("[GALAT] Konfigurasi Lockdown: " + ex.Message);
            }

            foreach (string svcName in TARGET_SERVICES)
            {
                SetServiceStartMode(svcName, 4, true, log);
            }

            DisableScheduledTasks(log);
            log("[SELESAI] Mode 2 Total Lockdown aktif: Semua layanan Windows Update mati total.");
        }

        public static void RestoreDefault(Action<string> log)
        {
            log("[RESTORE] Memulihkan seluruh konfigurasi ke standar Windows 11...");

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(REG_UX_SETTINGS, true))
                {
                    if (key != null)
                    {
                        key.DeleteValue("FlightSettingsMaxPauseDays", false);
                        key.DeleteValue("PauseFeatureUpdatesStartTime", false);
                        key.DeleteValue("PauseFeatureUpdatesEndTime", false);
                        key.DeleteValue("PauseQualityUpdatesStartTime", false);
                        key.DeleteValue("PauseQualityUpdatesEndTime", false);
                        key.DeleteValue("PauseUpdatesStartTime", false);
                        key.DeleteValue("PauseUpdatesExpiryTime", false);
                        log("[OK] Pengaturan jeda waktu UX Settings dikembalikan.");
                    }
                }
            }
            catch (Exception ex)
            {
                log("[PERINGATAN] Registry UX Settings: " + ex.Message);
            }

            try
            {
                using (RegistryKey keyWU = Registry.LocalMachine.OpenSubKey(REG_POLICY_WU, true))
                {
                    if (keyWU != null)
                    {
                        keyWU.DeleteValue("TargetReleaseVersion", false);
                        keyWU.DeleteValue("TargetReleaseVersionInfo", false);
                        keyWU.DeleteValue("ProductVersion", false);
                        keyWU.DeleteValue("SetDisableUXWUAccess", false);
                        keyWU.DeleteValue("ExcludeWUDriversInQualityUpdate", false);
                        log("[OK] Group Policy WindowsUpdate dikembalikan.");
                    }
                }

                using (RegistryKey keyAU = Registry.LocalMachine.OpenSubKey(REG_POLICY_AU, true))
                {
                    if (keyAU != null)
                    {
                        keyAU.DeleteValue("NoAutoUpdate", false);
                        keyAU.DeleteValue("AUOptions", false);
                        log("[OK] Group Policy AU dikembalikan.");
                    }
                }
            }
            catch (Exception ex)
            {
                log("[PERINGATAN] Group Policy: " + ex.Message);
            }

            log("[INFO] Mengembalikan status layanan ke pengaturan bawaan Windows 11...");
            SetServiceStartMode("wuauserv", 3, false, log);
            SetServiceStartMode("BITS", 3, false, log);
            SetServiceStartMode("DoSvc", 2, false, log);
            SetServiceStartMode("UsoSvc", 2, false, log);
            SetServiceStartMode("WaaSMedicSvc", 3, false, log);

            EnableScheduledTasks(log);
            log("[SELESAI] Konfigurasi standar Windows 11 berhasil dipulihkan.");
        }

        public static void SetDriverBlockPolicy(bool block, Action<string> log)
        {
            try
            {
                using (RegistryKey keyWU = Registry.LocalMachine.CreateSubKey(REG_POLICY_WU))
                {
                    if (keyWU != null)
                    {
                        if (block)
                        {
                            keyWU.SetValue("ExcludeWUDriversInQualityUpdate", 1, RegistryValueKind.DWord);
                            log("[OK] Blokir Driver Update Otomatis diaktifkan.");
                        }
                        else
                        {
                            keyWU.DeleteValue("ExcludeWUDriversInQualityUpdate", false);
                            log("[OK] Blokir Driver Update Otomatis dinonaktifkan.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log("[GALAT] Opsi Driver Update: " + ex.Message);
            }
        }

        private static void SetServiceStartMode(string serviceName, int startMode, bool stopIfRunning, Action<string> log)
        {
            string svcPath = REG_SERVICES_BASE + @"\" + serviceName;

            if (serviceName.Equals("WaaSMedicSvc", StringComparison.OrdinalIgnoreCase))
            {
                PrivilegeManager.GrantFullControlToRegistryKey(svcPath);
            }

            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (RegistryKey key = baseKey.OpenSubKey(svcPath, true))
                    {
                        if (key != null)
                        {
                            key.SetValue("Start", startMode, RegistryValueKind.DWord);

                            if (startMode == 4)
                            {
                                try
                                {
                                    key.DeleteValue("FailureActions", false);
                                }
                                catch
                                {
                                }
                            }

                            string modeDesc = (startMode == 4) ? "Disabled (4)" : (startMode == 3) ? "Manual (3)" : "Automatic (2)";
                            log(string.Format("[OK] Layanan {0} disetel ke {1}", serviceName, modeDesc));
                        }
                        else
                        {
                            log(string.Format("[PERINGATAN] Layanan {0} tidak ditemukan di registry.", serviceName));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log(string.Format("[GALAT] Pengaturan {0}: {1}", serviceName, ex.Message));
            }

            if (stopIfRunning)
            {
                StopServiceSafely(serviceName, log);
            }
        }

        private static void StopServiceSafely(string serviceName, Action<string> log)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(4));
                        log(string.Format("[OK] Layanan {0} berhasil dihentikan.", serviceName));
                    }
                }
            }
            catch
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("sc.exe", "stop " + serviceName);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    using (Process p = Process.Start(psi))
                    {
                        if (p != null) p.WaitForExit(3000);
                    }
                }
                catch
                {
                }
            }
        }

        private static void DisableScheduledTasks(Action<string> log)
        {
            log("[INFO] Menonaktifkan tugas terjadwal pembaruan...");
            foreach (string task in TARGET_TASKS)
            {
                RunSchtasks(task, false, log);
            }
        }

        private static void EnableScheduledTasks(Action<string> log)
        {
            log("[INFO] Mengaktifkan kembali tugas terjadwal pembaruan...");
            foreach (string task in TARGET_TASKS)
            {
                RunSchtasks(task, true, log);
            }
        }

        private static void RunSchtasks(string taskPath, bool enable, Action<string> log)
        {
            try
            {
                string actionArg = enable ? "/ENABLE" : "/DISABLE";
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", string.Format("/Change /TN \"{0}\" {1}", taskPath, actionArg));
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        p.WaitForExit(3500);
                        if (p.ExitCode == 0)
                        {
                            log(string.Format("[OK] Task {0} -> {1}", taskPath, enable ? "Aktif" : "Nonaktif"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log(string.Format("[PERINGATAN] Task {0}: {1}", taskPath, ex.Message));
            }
        }

        public static long CalculateCacheSize()
        {
            try
            {
                string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string downloadPath = Path.Combine(windir, @"SoftwareDistribution\Download");

                if (!Directory.Exists(downloadPath)) return 0;

                long totalBytes = 0;
                DirectoryInfo di = new DirectoryInfo(downloadPath);
                FileInfo[] files = di.GetFiles("*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    totalBytes += files[i].Length;
                }
                return totalBytes;
            }
            catch
            {
                return 0;
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            double mb = bytes / (1024.0 * 1024.0);
            if (mb > 1024.0)
            {
                double gb = mb / 1024.0;
                return gb.ToString("0.00") + " GB";
            }
            return mb.ToString("0.00") + " MB";
        }

        public static void ClearCache(Action<string> log)
        {
            log("[CACHE] Membersihkan direktori SoftwareDistribution\\Download...");

            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string downloadPath = Path.Combine(windir, @"SoftwareDistribution\Download");

            if (!Directory.Exists(downloadPath))
            {
                log("[INFO] Direktori cache download tidak ditemukan.");
                return;
            }

            long beforeBytes = CalculateCacheSize();
            log(string.Format("[INFO] Ukuran cache saat ini: {0}", FormatBytes(beforeBytes)));

            StopServiceSafely("wuauserv", log);
            StopServiceSafely("BITS", log);

            int deletedFiles = 0;
            try
            {
                DirectoryInfo di = new DirectoryInfo(downloadPath);
                FileInfo[] files = di.GetFiles();
                for (int i = 0; i < files.Length; i++)
                {
                    try { files[i].Delete(); deletedFiles++; } catch { }
                }

                DirectoryInfo[] dirs = di.GetDirectories();
                for (int i = 0; i < dirs.Length; i++)
                {
                    try { dirs[i].Delete(true); } catch { }
                }
            }
            catch (Exception ex)
            {
                log("[GALAT] Pembersihan direktori: " + ex.Message);
            }

            long afterBytes = CalculateCacheSize();
            long freedBytes = beforeBytes - afterBytes;
            if (freedBytes < 0) freedBytes = 0;

            log(string.Format("[OK] Pembersihan selesai. Berkas terhapus: {0}. Ruang dibebaskan: {1}",
                deletedFiles, FormatBytes(freedBytes)));
        }
    }
    #endregion

    #region Antarmuka Pengguna Modern Minimalis-Slate
    public class MainForm : Form
    {
        // Header
        private Panel panelHeader;
        private Label lblLogoIcon;
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblOverallBadge;

        // Container scrollable agar tidak tumpang tindih pada resolusi/DPI apa pun
        private Panel panelBody;

        // Kartu Ringkasan Status
        private GlassPanel cardPause;
        private Label lblPauseHeading;
        private Label lblPauseValue;
        private Label lblPauseDetail;

        private GlassPanel cardPolicy;
        private Label lblPolicyHeading;
        private Label lblPolicyValue;
        private Label lblPolicyDetail;

        private GlassPanel cardCache;
        private Label lblCacheHeading;
        private Label lblCacheValue;
        private Label lblCacheDetail;

        // Tabel Layanan Glass
        private GlassPanel panelServicesCard;
        private Label lblServicesTitle;
        private ListView lstServices;
        private ColumnHeader colName;
        private ColumnHeader colStartup;
        private ColumnHeader colStatus;
        private ColumnHeader colProtection;

        // Kontrol Aksi
        private GlassPanel panelActionsCard;
        private Label lblActionsTitle;
        private GlassButton btnSmartPause;
        private GlassButton btnTotalLock;
        private GlassButton btnRestore;
        private CheckBox chkBlockDrivers;
        private GlassButton btnCleanCache;
        private GlassButton btnRefresh;

        // Log Aktivitas
        private GlassPanel panelLogCard;
        private Label lblLogTitle;
        private TextBox txtLog;

        // Status Bar
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusAdmin;
        private ToolStripStatusLabel statusOsVersion;
        private ToolStripStatusLabel statusClock;
        private System.Windows.Forms.Timer clockTimer;

        public MainForm()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            InitializeComponent();
            RefreshSystemStatus();
            LogMessage("Sistem siap. Pilih salah satu mode kontrol di bawah.");
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Form Properties
            this.Text = "Windows 11 Update Blocker v1.0: Kontrol Permanen & Aman";
            this.Size = new Size(930, 800);
            this.MinimumSize = new Size(880, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = UITheme.BgApp;
            this.ForeColor = UITheme.TextPrimary;
            this.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
            this.Icon = SystemIcons.Shield;

            // 1. Header Banner
            panelHeader = new Panel();
            panelHeader.Dock = DockStyle.Top;
            panelHeader.Height = 84;
            panelHeader.BackColor = UITheme.BgHeader;
            panelHeader.Padding = new Padding(20, 12, 20, 12);
            panelHeader.Paint += new PaintEventHandler(PanelHeader_Paint);

            lblLogoIcon = new Label();
            lblLogoIcon.Text = "🛡️";
            lblLogoIcon.Font = new Font("Segoe UI Emoji", 20f, FontStyle.Regular);
            lblLogoIcon.ForeColor = UITheme.EmeraldText;
            lblLogoIcon.AutoSize = true;
            lblLogoIcon.Location = new Point(18, 18);

            lblTitle = new Label();
            lblTitle.Text = "Windows 11 Update Blocker";
            lblTitle.Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold);
            lblTitle.ForeColor = UITheme.TextPrimary;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(62, 14);

            lblSubtitle = new Label();
            lblSubtitle.Text = "Kontrol pembaruan otomatis tanpa mengganggu Microsoft Store & Windows Defender";
            lblSubtitle.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            lblSubtitle.ForeColor = UITheme.TextSecondary;
            lblSubtitle.AutoSize = true;
            lblSubtitle.Location = new Point(65, 46);

            lblOverallBadge = new Label();
            lblOverallBadge.Text = "MEMERIKSA STATUS...";
            lblOverallBadge.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            lblOverallBadge.ForeColor = UITheme.AmberText;
            lblOverallBadge.BackColor = UITheme.AmberBg;
            lblOverallBadge.TextAlign = ContentAlignment.MiddleCenter;
            lblOverallBadge.Size = new Size(230, 36);
            lblOverallBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblOverallBadge.Location = new Point(panelHeader.Width - 250, 24);
            lblOverallBadge.Paint += new PaintEventHandler(Badge_Paint);

            panelHeader.Controls.Add(lblLogoIcon);
            panelHeader.Controls.Add(lblTitle);
            panelHeader.Controls.Add(lblSubtitle);
            panelHeader.Controls.Add(lblOverallBadge);

            // 2. Body Panel (Scrollable, Padding rapi)
            panelBody = new Panel();
            panelBody.Dock = DockStyle.Fill;
            panelBody.AutoScroll = true;
            panelBody.Padding = new Padding(20, 16, 20, 16);
            panelBody.BackColor = UITheme.BgApp;

            // --- BARIS 1: 3 KARTU STATUS (Pause, Policy, Cache) ---
            int cardW = 278;
            int cardH = 92;

            // Card 1: Jeda Pembaruan
            cardPause = new GlassPanel();
            cardPause.Location = new Point(20, 14);
            cardPause.Size = new Size(cardW, cardH);
            cardPause.Padding = new Padding(12, 10, 12, 10);

            lblPauseHeading = new Label();
            lblPauseHeading.Text = "JEDA PEMBARUAN";
            lblPauseHeading.Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold);
            lblPauseHeading.ForeColor = UITheme.TextSecondary;
            lblPauseHeading.AutoSize = true;
            lblPauseHeading.Location = new Point(12, 10);

            lblPauseValue = new Label();
            lblPauseValue.Text = "Memeriksa...";
            lblPauseValue.Font = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold);
            lblPauseValue.ForeColor = UITheme.TextPrimary;
            lblPauseValue.AutoSize = true;
            lblPauseValue.Location = new Point(12, 32);

            lblPauseDetail = new Label();
            lblPauseDetail.Text = "Target UX Settings";
            lblPauseDetail.Font = new Font("Segoe UI", 8f);
            lblPauseDetail.ForeColor = UITheme.TextMuted;
            lblPauseDetail.AutoSize = true;
            lblPauseDetail.Location = new Point(12, 62);

            cardPause.Controls.Add(lblPauseHeading);
            cardPause.Controls.Add(lblPauseValue);
            cardPause.Controls.Add(lblPauseDetail);

            // Card 2: Group Policy
            cardPolicy = new GlassPanel();
            cardPolicy.Location = new Point(310, 14);
            cardPolicy.Size = new Size(cardW, cardH);
            cardPolicy.Padding = new Padding(12, 10, 12, 10);

            lblPolicyHeading = new Label();
            lblPolicyHeading.Text = "GROUP POLICY & TARGET";
            lblPolicyHeading.Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold);
            lblPolicyHeading.ForeColor = UITheme.TextSecondary;
            lblPolicyHeading.AutoSize = true;
            lblPolicyHeading.Location = new Point(12, 10);

            lblPolicyValue = new Label();
            lblPolicyValue.Text = "Memeriksa...";
            lblPolicyValue.Font = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold);
            lblPolicyValue.ForeColor = UITheme.TextPrimary;
            lblPolicyValue.AutoSize = true;
            lblPolicyValue.Location = new Point(12, 32);

            lblPolicyDetail = new Label();
            lblPolicyDetail.Text = "Target Release: Standar";
            lblPolicyDetail.Font = new Font("Segoe UI", 8f);
            lblPolicyDetail.ForeColor = UITheme.TextMuted;
            lblPolicyDetail.AutoSize = true;
            lblPolicyDetail.Location = new Point(12, 62);

            cardPolicy.Controls.Add(lblPolicyHeading);
            cardPolicy.Controls.Add(lblPolicyValue);
            cardPolicy.Controls.Add(lblPolicyDetail);

            // Card 3: Cache Unduhan
            cardCache = new GlassPanel();
            cardCache.Location = new Point(600, 14);
            cardCache.Size = new Size(cardW, cardH);
            cardCache.Padding = new Padding(12, 10, 12, 10);

            lblCacheHeading = new Label();
            lblCacheHeading.Text = "CACHE SOFTWARE DISTRIBUTION";
            lblCacheHeading.Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold);
            lblCacheHeading.ForeColor = UITheme.TextSecondary;
            lblCacheHeading.AutoSize = true;
            lblCacheHeading.Location = new Point(12, 10);

            lblCacheValue = new Label();
            lblCacheValue.Text = "0 MB";
            lblCacheValue.Font = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold);
            lblCacheValue.ForeColor = UITheme.SkyText;
            lblCacheValue.AutoSize = true;
            lblCacheValue.Location = new Point(12, 32);

            lblCacheDetail = new Label();
            lblCacheDetail.Text = "Ruang berkas sementara";
            lblCacheDetail.Font = new Font("Segoe UI", 8f);
            lblCacheDetail.ForeColor = UITheme.TextMuted;
            lblCacheDetail.AutoSize = true;
            lblCacheDetail.Location = new Point(12, 62);

            cardCache.Controls.Add(lblCacheHeading);
            cardCache.Controls.Add(lblCacheValue);
            cardCache.Controls.Add(lblCacheDetail);

            // --- BARIS 2: TABEL LAYANAN WINDOWS ---
            panelServicesCard = new GlassPanel();
            panelServicesCard.Location = new Point(20, 118);
            panelServicesCard.Size = new Size(858, 174);
            panelServicesCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            lblServicesTitle = new Label();
            lblServicesTitle.Text = "Status Layanan Inti (5 Services)";
            lblServicesTitle.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
            lblServicesTitle.ForeColor = UITheme.TextPrimary;
            lblServicesTitle.Location = new Point(16, 10);
            lblServicesTitle.AutoSize = true;

            lstServices = new ListView();
            lstServices.View = View.Details;
            lstServices.FullRowSelect = true;
            lstServices.GridLines = false;
            lstServices.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lstServices.Location = new Point(14, 38);
            lstServices.Size = new Size(828, 122);
            lstServices.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lstServices.BackColor = UITheme.BgInput;
            lstServices.ForeColor = UITheme.TextPrimary;
            lstServices.BorderStyle = BorderStyle.None;
            lstServices.Font = new Font("Segoe UI", 9f);
            lstServices.OwnerDraw = true;
            lstServices.DrawColumnHeader += new DrawListViewColumnHeaderEventHandler(LstServices_DrawColumnHeader);
            lstServices.DrawItem += new DrawListViewItemEventHandler(LstServices_DrawItem);
            lstServices.DrawSubItem += new DrawListViewSubItemEventHandler(LstServices_DrawSubItem);
            lstServices.Resize += new EventHandler(LstServices_Resize);

            colName = new ColumnHeader();
            colName.Text = "Nama Layanan Windows";
            colName.Width = 320;

            colStartup = new ColumnHeader();
            colStartup.Text = "Tipe Startup";
            colStartup.Width = 140;

            colStatus = new ColumnHeader();
            colStatus.Text = "Kondisi Berjalan";
            colStatus.Width = 140;

            colProtection = new ColumnHeader();
            colProtection.Text = "Status Proteksi";
            colProtection.Width = 200;

            lstServices.Columns.AddRange(new ColumnHeader[] { colName, colStartup, colStatus, colProtection });

            panelServicesCard.Controls.Add(lblServicesTitle);
            panelServicesCard.Controls.Add(lstServices);

            // --- BARIS 3: OPERASI KONTROL & TOMBOL GLASS ---
            panelActionsCard = new GlassPanel();
            panelActionsCard.Location = new Point(20, 304);
            panelActionsCard.Size = new Size(858, 142);
            panelActionsCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            lblActionsTitle = new Label();
            lblActionsTitle.Text = "Operasi & Mode Proteksi";
            lblActionsTitle.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
            lblActionsTitle.ForeColor = UITheme.TextPrimary;
            lblActionsTitle.Location = new Point(16, 10);
            lblActionsTitle.AutoSize = true;

            // Tombol Mode 1 (Emerald Glass)
            btnSmartPause = new GlassButton();
            btnSmartPause.Text = "Mode 1: Smart Pause 2077\r\n(Store & Defender Aman)";
            btnSmartPause.BaseColor = UITheme.EmeraldBg;
            btnSmartPause.HoverColor = Color.FromArgb(4, 120, 87);
            btnSmartPause.PressedColor = Color.FromArgb(2, 44, 34);
            btnSmartPause.BorderLineColor = UITheme.EmeraldBorder;
            btnSmartPause.ForeColor = UITheme.EmeraldText;
            btnSmartPause.Location = new Point(16, 36);
            btnSmartPause.Size = new Size(260, 48);
            btnSmartPause.Click += new EventHandler(BtnSmartPause_Click);

            // Tombol Mode 2 (Rose Glass)
            btnTotalLock = new GlassButton();
            btnTotalLock.Text = "Mode 2: Total Lockdown\r\n(Matikan Seluruh Layanan)";
            btnTotalLock.BaseColor = UITheme.RoseBg;
            btnTotalLock.HoverColor = Color.FromArgb(159, 18, 57);
            btnTotalLock.PressedColor = Color.FromArgb(76, 5, 25);
            btnTotalLock.BorderLineColor = UITheme.RoseBorder;
            btnTotalLock.ForeColor = UITheme.RoseText;
            btnTotalLock.Location = new Point(290, 36);
            btnTotalLock.Size = new Size(260, 48);
            btnTotalLock.Click += new EventHandler(BtnTotalLock_Click);

            // Tombol Restore (Slate Glass)
            btnRestore = new GlassButton();
            btnRestore.Text = "Pulihkan Default\r\nWindows 11";
            btnRestore.BaseColor = Color.FromArgb(30, 41, 59);
            btnRestore.HoverColor = Color.FromArgb(51, 65, 85);
            btnRestore.PressedColor = Color.FromArgb(15, 23, 42);
            btnRestore.BorderLineColor = UITheme.BorderHighlight;
            btnRestore.ForeColor = UITheme.TextPrimary;
            btnRestore.Location = new Point(564, 36);
            btnRestore.Size = new Size(278, 48);
            btnRestore.Click += new EventHandler(BtnRestore_Click);

            // Checkbox Driver Lock
            chkBlockDrivers = new CheckBox();
            chkBlockDrivers.Text = "Kunci pembaruan driver otomatis (ExcludeWUDriversInQualityUpdate)";
            chkBlockDrivers.Font = new Font("Segoe UI", 9f);
            chkBlockDrivers.ForeColor = UITheme.TextSecondary;
            chkBlockDrivers.Location = new Point(18, 96);
            chkBlockDrivers.Size = new Size(520, 26);
            chkBlockDrivers.Checked = true;
            chkBlockDrivers.CheckedChanged += new EventHandler(ChkBlockDrivers_CheckedChanged);

            // Tombol Pembersih Cache
            btnCleanCache = new GlassButton();
            btnCleanCache.Text = "🧹 Bersihkan Cache";
            btnCleanCache.Font = new Font("Segoe UI", 8.75f);
            btnCleanCache.BaseColor = Color.FromArgb(22, 31, 48);
            btnCleanCache.HoverColor = Color.FromArgb(30, 41, 59);
            btnCleanCache.PressedColor = Color.FromArgb(15, 23, 42);
            btnCleanCache.BorderLineColor = UITheme.BorderCard;
            btnCleanCache.ForeColor = UITheme.SkyText;
            btnCleanCache.Location = new Point(564, 94);
            btnCleanCache.Size = new Size(136, 30);
            btnCleanCache.Click += new EventHandler(BtnCleanCache_Click);

            // Tombol Segarkan
            btnRefresh = new GlassButton();
            btnRefresh.Text = "🔄 Segarkan Data";
            btnRefresh.Font = new Font("Segoe UI", 8.75f);
            btnRefresh.BaseColor = Color.FromArgb(22, 31, 48);
            btnRefresh.HoverColor = Color.FromArgb(30, 41, 59);
            btnRefresh.PressedColor = Color.FromArgb(15, 23, 42);
            btnRefresh.BorderLineColor = UITheme.BorderCard;
            btnRefresh.ForeColor = UITheme.TextPrimary;
            btnRefresh.Location = new Point(708, 94);
            btnRefresh.Size = new Size(134, 30);
            btnRefresh.Click += new EventHandler(BtnRefresh_Click);

            panelActionsCard.Controls.Add(lblActionsTitle);
            panelActionsCard.Controls.Add(btnSmartPause);
            panelActionsCard.Controls.Add(btnTotalLock);
            panelActionsCard.Controls.Add(btnRestore);
            panelActionsCard.Controls.Add(chkBlockDrivers);
            panelActionsCard.Controls.Add(btnCleanCache);
            panelActionsCard.Controls.Add(btnRefresh);

            // --- BARIS 4: KOTAK LOG AKTIVITAS SLATE ---
            panelLogCard = new GlassPanel();
            panelLogCard.Location = new Point(20, 458);
            panelLogCard.Size = new Size(858, 172);
            panelLogCard.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            lblLogTitle = new Label();
            lblLogTitle.Text = "Log Riwayat Sistem";
            lblLogTitle.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
            lblLogTitle.ForeColor = UITheme.TextPrimary;
            lblLogTitle.Location = new Point(16, 10);
            lblLogTitle.AutoSize = true;

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = UITheme.BgInput;
            txtLog.ForeColor = UITheme.EmeraldText;
            txtLog.Font = new Font("Consolas", 9f, FontStyle.Regular);
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.Location = new Point(14, 34);
            txtLog.Size = new Size(828, 122);
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            panelLogCard.Controls.Add(lblLogTitle);
            panelLogCard.Controls.Add(txtLog);

            // Susun Kontrol di Body Panel
            panelBody.Controls.Add(cardPause);
            panelBody.Controls.Add(cardPolicy);
            panelBody.Controls.Add(cardCache);
            panelBody.Controls.Add(panelServicesCard);
            panelBody.Controls.Add(panelActionsCard);
            panelBody.Controls.Add(panelLogCard);

            // 3. Status Bar di Bawah
            statusStrip = new StatusStrip();
            statusStrip.BackColor = UITheme.BgHeader;
            statusStrip.SizingGrip = false;
            statusStrip.Padding = new Padding(12, 4, 12, 4);

            statusAdmin = new ToolStripStatusLabel();
            statusAdmin.Text = "Hak Akses: Administrator";
            statusAdmin.ForeColor = UITheme.EmeraldText;
            statusAdmin.BorderSides = ToolStripStatusLabelBorderSides.Right;

            statusOsVersion = new ToolStripStatusLabel();
            statusOsVersion.Text = "Windows 11: " + UpdateBlockerEngine.GetWindowsVersionDisplay();
            statusOsVersion.ForeColor = UITheme.TextSecondary;
            statusOsVersion.BorderSides = ToolStripStatusLabelBorderSides.Right;

            statusClock = new ToolStripStatusLabel();
            statusClock.Text = DateTime.Now.ToString("HH:mm:ss");
            statusClock.ForeColor = UITheme.TextMuted;

            statusStrip.Items.AddRange(new ToolStripItem[] { statusAdmin, statusOsVersion, statusClock });

            clockTimer = new System.Windows.Forms.Timer();
            clockTimer.Interval = 1000;
            clockTimer.Tick += new EventHandler(ClockTimer_Tick);
            clockTimer.Start();

            // Pasang Komponen Utama ke Form
            this.Controls.Add(panelBody);
            this.Controls.Add(panelHeader);
            this.Controls.Add(statusStrip);

            this.Resize += new EventHandler(MainForm_Resize);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            // Proporsi lebar 3 kartu status agar selalu seimbang dan tidak bertumpuk
            int availWidth = panelBody.ClientSize.Width - 40;
            if (availWidth > 400)
            {
                int cardWidth = (availWidth - 24) / 3;
                cardPause.Width = cardWidth;
                cardPolicy.Left = cardPause.Right + 12;
                cardPolicy.Width = cardWidth;
                cardCache.Left = cardPolicy.Right + 12;
                cardCache.Width = availWidth - (cardWidth * 2 + 24);
            }

            // Atur posisi badge di header agar selalu di kanan dengan jarak aman dari subtitle
            lblOverallBadge.Left = panelHeader.Width - lblOverallBadge.Width - 24;

            LstServices_Resize(this, EventArgs.Empty);
        }

        private void LstServices_Resize(object sender, EventArgs e)
        {
            if (lstServices.Columns.Count >= 4)
            {
                int totalW = lstServices.ClientSize.Width - 20;
                if (totalW > 400)
                {
                    colName.Width = (int)(totalW * 0.40);
                    colStartup.Width = (int)(totalW * 0.18);
                    colStatus.Width = (int)(totalW * 0.18);
                    colProtection.Width = totalW - (colName.Width + colStartup.Width + colStatus.Width);
                }
            }
        }

        private void PanelHeader_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(UITheme.BorderSubtle, 1.0f))
            {
                e.Graphics.DrawLine(pen, 0, panelHeader.Height - 1, panelHeader.Width, panelHeader.Height - 1);
            }
        }

        private void Badge_Paint(object sender, PaintEventArgs e)
        {
            Label badge = sender as Label;
            if (badge == null) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, badge.Width - 1, badge.Height - 1);

            using (GraphicsPath path = UITheme.CreateRoundedPath(rect, 8))
            {
                using (SolidBrush brush = new SolidBrush(badge.BackColor))
                {
                    e.Graphics.FillPath(brush, path);
                }

                Color borderCol = badge.ForeColor;
                using (Pen pen = new Pen(borderCol, 1.2f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, badge.Text, badge.Font, rect, badge.ForeColor, flags);
        }

        private void LstServices_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = e.Bounds;

            using (SolidBrush brush = new SolidBrush(UITheme.BgCardAlt))
            {
                e.Graphics.FillRectangle(brush, rect);
            }

            using (Pen pen = new Pen(UITheme.BorderSubtle, 1.0f))
            {
                e.Graphics.DrawLine(pen, rect.Left, rect.Bottom - 1, rect.Right, rect.Bottom - 1);
            }

            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
            Rectangle textRect = new Rectangle(rect.Left + 8, rect.Top, rect.Width - 12, rect.Height);
            using (Font headerFont = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(e.Graphics, e.Header.Text, headerFont, textRect, UITheme.TextSecondary, flags);
            }
        }

        private void LstServices_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            // Background baris selang-seling (Zebra)
            Color rowBg = (e.ItemIndex % 2 == 0) ? UITheme.BgInput : Color.FromArgb(13, 19, 32);
            using (SolidBrush brush = new SolidBrush(rowBg))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
        }

        private void LstServices_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = e.Bounds;

            string text = e.SubItem.Text;
            int colIndex = e.ColumnIndex;

            if (colIndex == 0)
            {
                // Kolom Nama Layanan
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
                Rectangle textRect = new Rectangle(rect.Left + 8, rect.Top, rect.Width - 12, rect.Height);
                TextRenderer.DrawText(g, text, lstServices.Font, textRect, UITheme.TextPrimary, flags);
            }
            else if (colIndex == 1)
            {
                // Kolom Startup Type
                Color col = UITheme.TextSecondary;
                if (text == "Disabled") col = UITheme.RoseText;
                else if (text == "Manual") col = UITheme.EmeraldText;
                else if (text == "Automatic") col = UITheme.SkyText;

                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
                Rectangle textRect = new Rectangle(rect.Left + 8, rect.Top, rect.Width - 12, rect.Height);
                TextRenderer.DrawText(g, text, lstServices.Font, textRect, col, flags);
            }
            else if (colIndex == 2)
            {
                // Kolom Status (Running / Stopped)
                Color col = (text == "Running") ? UITheme.EmeraldText : UITheme.TextMuted;
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
                Rectangle textRect = new Rectangle(rect.Left + 8, rect.Top, rect.Width - 12, rect.Height);
                TextRenderer.DrawText(g, text, lstServices.Font, textRect, col, flags);
            }
            else if (colIndex == 3)
            {
                // Kolom Badge Proteksi (Pill Renderer)
                Color pillBg = UITheme.EmeraldBg;
                Color pillBorder = UITheme.EmeraldBorder;
                Color pillText = UITheme.EmeraldText;

                if (text.IndexOf("Terkunci") >= 0)
                {
                    pillBg = UITheme.RoseBg;
                    pillBorder = UITheme.RoseBorder;
                    pillText = UITheme.RoseText;
                }
                else if (text.IndexOf("Otomatis") >= 0)
                {
                    pillBg = Color.FromArgb(12, 74, 110);
                    pillBorder = UITheme.SkyBorder;
                    pillText = UITheme.SkyText;
                }

                Rectangle pillRect = new Rectangle(rect.Left + 6, rect.Top + 4, Math.Min(rect.Width - 16, 150), rect.Height - 8);
                if (pillRect.Width > 20 && pillRect.Height > 10)
                {
                    using (GraphicsPath path = UITheme.CreateRoundedPath(pillRect, 6))
                    {
                        using (SolidBrush brush = new SolidBrush(pillBg))
                        {
                            g.FillPath(brush, path);
                        }
                        using (Pen pen = new Pen(pillBorder, 1.0f))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                    TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
                    using (Font pillFont = new Font("Segoe UI Semibold", 8f, FontStyle.Bold))
                    {
                        TextRenderer.DrawText(g, text, pillFont, pillRect, pillText, flags);
                    }
                }
            }
        }

        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            statusClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public void LogMessage(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<string>(LogMessage), message);
                return;
            }

            string line = string.Format("[{0}] {1}\r\n", DateTime.Now.ToString("HH:mm:ss"), message);
            txtLog.AppendText(line);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private void SetControlsEnabled(bool enabled)
        {
            btnSmartPause.Enabled = enabled;
            btnTotalLock.Enabled = enabled;
            btnRestore.Enabled = enabled;
            btnCleanCache.Enabled = enabled;
            btnRefresh.Enabled = enabled;
            chkBlockDrivers.Enabled = enabled;
        }

        private void RefreshSystemStatus()
        {
            SystemStatusSnapshot snapshot = UpdateBlockerEngine.ScanCurrentStatus();

            // 1. Perbarui Badge Header Utama
            if (snapshot.IsTotalLockdown)
            {
                lblOverallBadge.Text = "TOTAL LOCKDOWN AKTIF";
                lblOverallBadge.BackColor = UITheme.RoseBg;
                lblOverallBadge.ForeColor = UITheme.RoseText;
            }
            else if (snapshot.IsPausedTo2077)
            {
                lblOverallBadge.Text = "SMART PAUSE 2077 AKTIF";
                lblOverallBadge.BackColor = UITheme.EmeraldBg;
                lblOverallBadge.ForeColor = UITheme.EmeraldText;
            }
            else
            {
                lblOverallBadge.Text = "STANDAR (TIDAK TERKUNCI)";
                lblOverallBadge.BackColor = UITheme.AmberBg;
                lblOverallBadge.ForeColor = UITheme.AmberText;
            }
            lblOverallBadge.Invalidate();

            // 2. Perbarui Kartu Ringkasan
            if (snapshot.IsPausedTo2077)
            {
                lblPauseValue.Text = "Hingga 31 Des 2077";
                lblPauseValue.ForeColor = UITheme.EmeraldText;
                lblPauseDetail.Text = "Jeda sistem aktif s/d tahun 2077";
            }
            else
            {
                lblPauseValue.Text = snapshot.PauseExpiryString;
                lblPauseValue.ForeColor = UITheme.AmberText;
                lblPauseDetail.Text = "Windows Update dapat berjalan";
            }

            if (snapshot.IsGroupPolicyLocked)
            {
                lblPolicyValue.Text = "Terkunci (NoAutoUpdate)";
                lblPolicyValue.ForeColor = UITheme.EmeraldText;
                lblPolicyDetail.Text = "Target: Windows 11 " + (string.IsNullOrEmpty(snapshot.TargetReleaseVersion) ? "Aktif" : snapshot.TargetReleaseVersion);
            }
            else
            {
                lblPolicyValue.Text = "Standar / Tidak Terkunci";
                lblPolicyValue.ForeColor = UITheme.TextSecondary;
                lblPolicyDetail.Text = "Kebijakan otomatis aktif";
            }

            lblCacheValue.Text = UpdateBlockerEngine.FormatBytes(snapshot.CacheSizeBytes);
            lblCacheDetail.Text = snapshot.IsDriverUpdateBlocked ? "Driver Lock: Aktif" : "Driver Lock: Nonaktif";
            chkBlockDrivers.Checked = snapshot.IsDriverUpdateBlocked;

            // 3. Perbarui Daftar Layanan
            lstServices.BeginUpdate();
            lstServices.Items.Clear();
            foreach (ServiceInfoItem svc in snapshot.Services)
            {
                ListViewItem lvi = new ListViewItem(svc.DisplayName);
                lvi.SubItems.Add(svc.StartMode);
                lvi.SubItems.Add(svc.Status);

                string indicator = "Standar";
                if (svc.StartMode == "Disabled")
                {
                    indicator = "Terkunci Mati";
                }
                else if (svc.StartMode == "Manual")
                {
                    indicator = "Siap On-Demand";
                }
                else if (svc.StartMode == "Automatic")
                {
                    indicator = "Otomatis Windows";
                }

                lvi.SubItems.Add(indicator);
                lstServices.Items.Add(lvi);
            }
            lstServices.EndUpdate();
        }

        private void BtnSmartPause_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show(
                "Mode 1 akan mengunci Windows Update hingga 31 Desember 2077 dan mematikan perbaikan otomatis WaaSMedicSvc.\n\n" +
                "Microsoft Store dan pembaruan definisi Windows Defender akan TETAP BERFUNGSI NORMAL.\n\nTerapkan mode ini sekarang?",
                "Konfirmasi Mode 1: Smart Pause 2077",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (dr != DialogResult.Yes) return;

            SetControlsEnabled(false);
            bool blockDrivers = chkBlockDrivers.Checked;

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateBlockerEngine.ApplySmartPause2077(blockDrivers, LogMessage);
                this.Invoke(new Action(delegate
                {
                    RefreshSystemStatus();
                    SetControlsEnabled(true);
                    MessageBox.Show("Mode 1 (Smart Pause 2077) berhasil diterapkan!\nWindows Update OS berhasil dijeda hingga tahun 2077.", "Sukses", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            });
        }

        private void BtnTotalLock_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show(
                "Mode 2 akan MEMATIKAN SELURUH 5 layanan Windows Update (termasuk wuauserv, BITS, dan DoSvc).\n\n" +
                "Perhatian: Microsoft Store tidak dapat mengunduh aplikasi selama mode ini aktif.\n\nTerapkan Total Lockdown sekarang?",
                "Konfirmasi Mode 2: Total Lockdown",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (dr != DialogResult.Yes) return;

            SetControlsEnabled(false);
            bool blockDrivers = chkBlockDrivers.Checked;

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateBlockerEngine.ApplyTotalLockdown(blockDrivers, LogMessage);
                this.Invoke(new Action(delegate
                {
                    RefreshSystemStatus();
                    SetControlsEnabled(true);
                    MessageBox.Show("Mode 2 (Total Lockdown) berhasil diterapkan!\nSeluruh layanan Windows Update telah dimatikan dan dikunci.", "Sukses", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            });
        }

        private void BtnRestore_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show(
                "Apakah Anda ingin mengembalikan semua pengaturan, layanan, dan tugas Windows Update ke kondisi standar bawaan Windows 11?",
                "Konfirmasi Pemulihan Default",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (dr != DialogResult.Yes) return;

            SetControlsEnabled(false);

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateBlockerEngine.RestoreDefault(LogMessage);
                this.Invoke(new Action(delegate
                {
                    RefreshSystemStatus();
                    SetControlsEnabled(true);
                    MessageBox.Show("Pengaturan Windows Update berhasil dipulihkan ke setelan standar Windows 11.", "Sukses", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            });
        }

        private void BtnCleanCache_Click(object sender, EventArgs e)
        {
            SetControlsEnabled(false);

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateBlockerEngine.ClearCache(LogMessage);
                this.Invoke(new Action(delegate
                {
                    RefreshSystemStatus();
                    SetControlsEnabled(true);
                    MessageBox.Show("Cache unduhan Windows Update berhasil dibersihkan.", "Pembersihan Berhasil", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            });
        }

        private void BtnRefresh_Click(object sender, EventArgs e)
        {
            RefreshSystemStatus();
            LogMessage("Status sistem diperbarui.");
        }

        private void ChkBlockDrivers_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkBlockDrivers.Enabled) return;
            UpdateBlockerEngine.SetDriverBlockPolicy(chkBlockDrivers.Checked, LogMessage);
        }
    }
    #endregion

    #region Titik Masuk Aplikasi (Program Entry Point)
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!IsRunningAsAdministrator())
            {
                try
                {
                    ProcessStartInfo proc = new ProcessStartInfo();
                    proc.UseShellExecute = true;
                    proc.WorkingDirectory = Environment.CurrentDirectory;
                    proc.FileName = Application.ExecutablePath;
                    proc.Verb = "runas";
                    Process.Start(proc);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Aplikasi ini membutuhkan hak akses Administrator untuk mengelola Windows Update.\n\nGalat: " + ex.Message,
                        "Membutuhkan Hak Administrator",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return;
            }

            Application.Run(new MainForm());
        }

        private static bool IsRunningAsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
    #endregion
}
