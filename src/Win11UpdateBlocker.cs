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

    #region Token Desain Minimalis-Slate (Tema Putih Bersih)
    public static class UITheme
    {
        // Latar Belakang Utama Putih & Lapisan Kaca Slate Terang
        public static readonly Color BgWindow = Color.FromArgb(255, 255, 255);      // Putih Murni (#FFFFFF)
        public static readonly Color BgSurface = Color.FromArgb(248, 250, 252);     // Slate 50 (#F8FAFC)
        public static readonly Color BgCard = Color.FromArgb(255, 255, 255);        // Kartu Putih (#FFFFFF)
        public static readonly Color BgTableHeader = Color.FromArgb(241, 245, 249); // Slate 100 (#F1F5F9)
        public static readonly Color BgRowAlt = Color.FromArgb(248, 250, 252);      // Baris Zebra (#F8FAFC)

        // Garis Batas (Borders)
        public static readonly Color BorderCard = Color.FromArgb(226, 232, 240);    // Slate 200 (#E2E8F0)
        public static readonly Color BorderStrong = Color.FromArgb(203, 213, 225);  // Slate 300 (#CBD5E1)
        public static readonly Color BorderFocus = Color.FromArgb(15, 23, 42);      // Cincin Fokus 3px (#0F172A)

        // Tipografi (Kontras WCAG AAA/AA >= 4.5:1 di atas putih)
        public static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);      // Slate 900 (#0F172A - 17.4:1)
        public static readonly Color TextSecondary = Color.FromArgb(51, 65, 85);    // Slate 700 (#334155 - 9.3:1)
        public static readonly Color TextMuted = Color.FromArgb(71, 85, 105);       // Slate 600 (#475569 - 7.0:1)

        // Status Emerald (Aman / Aktif)
        public static readonly Color EmeraldBadgeBg = Color.FromArgb(236, 253, 245);    // #ECFDF5
        public static readonly Color EmeraldBadgeBorder = Color.FromArgb(16, 185, 129); // #10B981
        public static readonly Color EmeraldBadgeText = Color.FromArgb(6, 95, 70);      // #065F46 (7.2:1)
        public static readonly Color EmeraldBtnBg = Color.FromArgb(5, 150, 105);        // #059669
        public static readonly Color EmeraldBtnHover = Color.FromArgb(4, 120, 87);      // #047857
        public static readonly Color EmeraldBtnPress = Color.FromArgb(6, 95, 70);       // #065F46

        // Status Rose (Total Lockdown / Nonaktif)
        public static readonly Color RoseBadgeBg = Color.FromArgb(255, 241, 242);       // #FFF1F2
        public static readonly Color RoseBadgeBorder = Color.FromArgb(244, 63, 94);     // #F43F5E
        public static readonly Color RoseBadgeText = Color.FromArgb(159, 18, 57);       // #9F1239 (7.5:1)
        public static readonly Color RoseBtnBg = Color.FromArgb(225, 29, 72);           // #E11D48
        public static readonly Color RoseBtnHover = Color.FromArgb(190, 18, 60);        // #BE123C
        public static readonly Color RoseBtnPress = Color.FromArgb(159, 18, 57);        // #9F1239

        // Status Amber (Standar / Tidak Terkunci)
        public static readonly Color AmberBadgeBg = Color.FromArgb(255, 251, 235);      // #FFFBEB
        public static readonly Color AmberBadgeBorder = Color.FromArgb(217, 119, 6);    // #D97706
        public static readonly Color AmberBadgeText = Color.FromArgb(146, 64, 14);      // #92400E (6.8:1)

        // Status Sky (Otomatis / Info)
        public static readonly Color SkyBadgeBg = Color.FromArgb(240, 249, 255);        // #F0F9FF
        public static readonly Color SkyBadgeBorder = Color.FromArgb(14, 165, 233);     // #0EA5E9
        public static readonly Color SkyBadgeText = Color.FromArgb(3, 105, 161);        // #0369A1 (6.3:1)

        // Tombol Netral Slate
        public static readonly Color SlateBtnBg = Color.FromArgb(241, 245, 249);        // #F1F5F9
        public static readonly Color SlateBtnHover = Color.FromArgb(226, 232, 240);     // #E2E8F0
        public static readonly Color SlateBtnPress = Color.FromArgb(203, 213, 225);     // #CBD5E1

        public static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0) return path;

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
    }

    /// <summary>
    /// Header Banner yang mengukur teks secara dinamis di OnPaint agar tidak pernah tumpang tindih pada DPI berapa pun.
    /// </summary>
    public class HeaderBannerControl : Control
    {
        public string TitleText { get; set; }
        public string SubtitleText { get; set; }
        public string BadgeText { get; set; }
        public Color BadgeBgColor { get; set; }
        public Color BadgeBorderColor { get; set; }
        public Color BadgeTextColor { get; set; }

        public HeaderBannerControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TitleText = "Windows 11 Update Blocker";
            SubtitleText = "Kontrol pembaruan otomatis tanpa mengganggu Microsoft Store & Windows Defender";
            BadgeText = "MEMERIKSA STATUS...";
            BadgeBgColor = UITheme.AmberBadgeBg;
            BadgeBorderColor = UITheme.AmberBadgeBorder;
            BadgeTextColor = UITheme.AmberBadgeText;
            BackColor = UITheme.BgWindow;
            Height = 82;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Latar putih bersih dengan efek kaca slate halus di bagian atas
            using (SolidBrush bgBrush = new SolidBrush(UITheme.BgWindow))
            {
                g.FillRectangle(bgBrush, ClientRectangle);
            }

            // Garis pemisah bawah
            using (Pen borderPen = new Pen(UITheme.BorderCard, 1f))
            {
                g.DrawLine(borderPen, 0, Height - 1, Width, Height - 1);
            }

            // 1. Gambar Ikon Perisai Vektor di kiri (tanpa bergantung pada ukuran font emoji)
            int iconSize = 38;
            int iconX = 24;
            int iconY = (Height - iconSize) / 2;
            Rectangle iconBox = new Rectangle(iconX, iconY, iconSize, iconSize);

            using (GraphicsPath iconBgPath = UITheme.CreateRoundedPath(iconBox, 10))
            {
                using (SolidBrush iconBgBrush = new SolidBrush(UITheme.BgTableHeader))
                {
                    g.FillPath(iconBgBrush, iconBgPath);
                }
                using (Pen iconBorderPen = new Pen(UITheme.BorderStrong, 1f))
                {
                    g.DrawPath(iconBorderPen, iconBgPath);
                }
            }

            // Gambar bentuk perisai di dalam kotak ikon
            PointF[] shieldPts = new PointF[]
            {
                new PointF(iconX + iconSize * 0.50f, iconY + 7),
                new PointF(iconX + iconSize * 0.80f, iconY + 12),
                new PointF(iconX + iconSize * 0.80f, iconY + 21),
                new PointF(iconX + iconSize * 0.50f, iconY + 31),
                new PointF(iconX + iconSize * 0.20f, iconY + 21),
                new PointF(iconX + iconSize * 0.20f, iconY + 12)
            };
            using (GraphicsPath shieldPath = new GraphicsPath())
            {
                shieldPath.AddPolygon(shieldPts);
                using (SolidBrush shieldFill = new SolidBrush(BadgeBorderColor))
                {
                    g.FillPath(shieldFill, shieldPath);
                }
            }

            // 2. Ukur dan Gambar Badge Status di sebelah kanan
            using (Font badgeFont = new Font("Segoe UI Semibold", 9f, FontStyle.Bold))
            {
                Size badgeTextSize = TextRenderer.MeasureText(g, BadgeText, badgeFont);
                int badgeW = Math.Max(badgeTextSize.Width + 28, 190);
                int badgeH = Math.Max(badgeTextSize.Height + 14, 34);
                int badgeX = Width - badgeW - 24;
                int badgeY = (Height - badgeH) / 2;
                Rectangle badgeRect = new Rectangle(badgeX, badgeY, badgeW, badgeH);

                using (GraphicsPath badgePath = UITheme.CreateRoundedPath(badgeRect, 8))
                {
                    using (SolidBrush bBrush = new SolidBrush(BadgeBgColor))
                    {
                        g.FillPath(bBrush, badgePath);
                    }
                    using (Pen bPen = new Pen(BadgeBorderColor, 1.2f))
                    {
                        g.DrawPath(bPen, badgePath);
                    }
                }

                TextFormatFlags badgeFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                TextRenderer.DrawText(g, BadgeText, badgeFont, badgeRect, BadgeTextColor, badgeFlags);

                // 3. Gambar Judul & Subjudul di area tengah dengan perhitungan tinggi aktual (Anti Tumpang Tindih)
                int textLeft = iconBox.Right + 16;
                int availWidth = Math.Max(100, badgeRect.Left - textLeft - 20);

                using (Font titleFont = new Font("Segoe UI Semibold", 13.5f, FontStyle.Bold))
                using (Font subFont = new Font("Segoe UI", 9f, FontStyle.Regular))
                {
                    Size titleSize = TextRenderer.MeasureText(g, TitleText, titleFont);
                    Size subSize = TextRenderer.MeasureText(g, SubtitleText, subFont);

                    int totalTextH = titleSize.Height + 4 + subSize.Height;
                    int startY = Math.Max(8, (Height - totalTextH) / 2);

                    Rectangle titleRect = new Rectangle(textLeft, startY, availWidth, titleSize.Height + 2);
                    Rectangle subRect = new Rectangle(textLeft, startY + titleSize.Height + 3, availWidth, subSize.Height + 4);

                    TextFormatFlags textFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                    TextRenderer.DrawText(g, TitleText, titleFont, titleRect, UITheme.TextPrimary, textFlags);
                    TextRenderer.DrawText(g, SubtitleText, subFont, subRect, UITheme.TextMuted, textFlags);
                }
            }
        }
    }

    /// <summary>
    /// Kartu Metrik Status yang menggambar 3 baris teks secara berurutan berdasarkan tinggi font aktual.
    /// </summary>
    public class MetricCardControl : Control
    {
        public string HeadingText { get; set; }
        public string ValueText { get; set; }
        public string DetailText { get; set; }
        public Color AccentColor { get; set; }
        public Color ValueColor { get; set; }

        public MetricCardControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            HeadingText = "STATUS";
            ValueText = "Memuat...";
            DetailText = "-";
            AccentColor = UITheme.BorderStrong;
            ValueColor = UITheme.TextPrimary;
            Height = 96;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle cardRect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (cardRect.Width <= 4 || cardRect.Height <= 4) return;

            using (GraphicsPath path = UITheme.CreateRoundedPath(cardRect, 10))
            {
                using (SolidBrush bgBrush = new SolidBrush(UITheme.BgSurface))
                {
                    g.FillPath(bgBrush, path);
                }
                using (Pen borderPen = new Pen(UITheme.BorderCard, 1f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            // Garis aksen vertikal di sisi kiri dalam kartu
            Rectangle accentRect = new Rectangle(12, 16, 4, Height - 32);
            if (accentRect.Height > 6)
            {
                using (GraphicsPath accentPath = UITheme.CreateRoundedPath(accentRect, 2))
                using (SolidBrush accentBrush = new SolidBrush(AccentColor))
                {
                    g.FillPath(accentBrush, accentPath);
                }
            }

            int textX = 26;
            int availW = Math.Max(40, Width - textX - 12);

            using (Font fHead = new Font("Segoe UI Semibold", 8f, FontStyle.Bold))
            using (Font fVal = new Font("Segoe UI Semibold", 11f, FontStyle.Bold))
            using (Font fDet = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            {
                Size sHead = TextRenderer.MeasureText(g, HeadingText, fHead);
                Size sVal = TextRenderer.MeasureText(g, ValueText, fVal);
                Size sDet = TextRenderer.MeasureText(g, DetailText, fDet);

                int totalH = sHead.Height + 4 + sVal.Height + 4 + sDet.Height;
                int curY = Math.Max(8, (Height - totalH) / 2);

                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

                Rectangle rHead = new Rectangle(textX, curY, availW, sHead.Height + 2);
                TextRenderer.DrawText(g, HeadingText, fHead, rHead, UITheme.TextMuted, flags);
                curY += sHead.Height + 4;

                Rectangle rVal = new Rectangle(textX, curY, availW, sVal.Height + 2);
                TextRenderer.DrawText(g, ValueText, fVal, rVal, ValueColor, flags);
                curY += sVal.Height + 4;

                Rectangle rDet = new Rectangle(textX, curY, availW, sDet.Height + 2);
                TextRenderer.DrawText(g, DetailText, fDet, rDet, UITheme.TextSecondary, flags);
            }
        }
    }

    /// <summary>
    /// Tabel Layanan Kustom yang merender 5 baris layanan dengan presisi tinggi tanpa potong teks atau scrollbar sempit.
    /// </summary>
    public class ServicesTableCard : Control
    {
        private List<ServiceInfoItem> items = new List<ServiceInfoItem>();
        public string SectionTitle { get; set; }

        public ServicesTableCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SectionTitle = "Status Layanan Inti Windows Update (5 Services)";
            Height = 228;
        }

        public void SetItems(List<ServiceInfoItem> newItems)
        {
            items = newItems ?? new List<ServiceInfoItem>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle cardRect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (cardRect.Width <= 10 || cardRect.Height <= 10) return;

            using (GraphicsPath path = UITheme.CreateRoundedPath(cardRect, 10))
            {
                using (SolidBrush bgBrush = new SolidBrush(UITheme.BgCard))
                {
                    g.FillPath(bgBrush, path);
                }
                using (Pen borderPen = new Pen(UITheme.BorderCard, 1f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            int padX = 16;
            int curY = 12;

            // 1. Judul Seksi
            using (Font titleFont = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold))
            {
                Size titleSize = TextRenderer.MeasureText(g, SectionTitle, titleFont);
                Rectangle titleRect = new Rectangle(padX, curY, Width - padX * 2, titleSize.Height + 2);
                TextRenderer.DrawText(g, SectionTitle, titleFont, titleRect, UITheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                curY += titleSize.Height + 10;
            }

            // 2. Area Tabel
            int tableW = Width - padX * 2;
            int col0W = (int)(tableW * 0.42);
            int col1W = (int)(tableW * 0.18);
            int col2W = (int)(tableW * 0.18);
            int col3W = tableW - (col0W + col1W + col2W);

            int col0X = padX;
            int col1X = col0X + col0W;
            int col2X = col1X + col1W;
            int col3X = col2X + col2W;

            int headerH = 28;
            Rectangle headerRect = new Rectangle(padX, curY, tableW, headerH);
            using (SolidBrush hBrush = new SolidBrush(UITheme.BgTableHeader))
            {
                g.FillRectangle(hBrush, headerRect);
            }
            using (Pen hPen = new Pen(UITheme.BorderCard, 1f))
            {
                g.DrawRectangle(hPen, headerRect);
            }

            TextFormatFlags cellFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            using (Font hFont = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "Nama Layanan (Service)", hFont, new Rectangle(col0X + 10, curY, col0W - 14, headerH), UITheme.TextSecondary, cellFlags);
                TextRenderer.DrawText(g, "Tipe Startup", hFont, new Rectangle(col1X + 8, curY, col1W - 12, headerH), UITheme.TextSecondary, cellFlags);
                TextRenderer.DrawText(g, "Status Proses", hFont, new Rectangle(col2X + 8, curY, col2W - 12, headerH), UITheme.TextSecondary, cellFlags);
                TextRenderer.DrawText(g, "Indikator Proteksi", hFont, new Rectangle(col3X + 8, curY, col3W - 12, headerH), UITheme.TextSecondary, cellFlags);
            }
            curY += headerH;

            // 3. Baris Layanan
            int rowCount = Math.Max(1, items.Count);
            int availRowsH = Math.Max(100, Height - curY - 12);
            int rowH = availRowsH / rowCount;

            using (Font rowFont = new Font("Segoe UI", 9f, FontStyle.Regular))
            using (Font boldFont = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold))
            using (Pen rowBorderPen = new Pen(UITheme.BorderCard, 1f))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    ServiceInfoItem svc = items[i];
                    Rectangle rowRect = new Rectangle(padX, curY, tableW, rowH);

                    Color rowBg = (i % 2 == 1) ? UITheme.BgRowAlt : UITheme.BgCard;
                    using (SolidBrush rBrush = new SolidBrush(rowBg))
                    {
                        g.FillRectangle(rBrush, rowRect);
                    }
                    g.DrawRectangle(rowBorderPen, rowRect);

                    // Kolom 0: Nama Layanan
                    TextRenderer.DrawText(g, svc.DisplayName, rowFont, new Rectangle(col0X + 10, curY, col0W - 14, rowH), UITheme.TextPrimary, cellFlags);

                    // Kolom 1: Tipe Startup
                    Color startCol = UITheme.TextSecondary;
                    if (svc.StartMode == "Disabled") startCol = UITheme.RoseBadgeText;
                    else if (svc.StartMode == "Manual") startCol = UITheme.EmeraldBadgeText;
                    else if (svc.StartMode == "Automatic") startCol = UITheme.SkyBadgeText;

                    TextRenderer.DrawText(g, svc.StartMode, boldFont, new Rectangle(col1X + 8, curY, col1W - 12, rowH), startCol, cellFlags);

                    // Kolom 2: Status Proses
                    Color statCol = (svc.Status == "Running") ? UITheme.EmeraldBadgeText : UITheme.TextMuted;
                    TextRenderer.DrawText(g, svc.Status, rowFont, new Rectangle(col2X + 8, curY, col2W - 12, rowH), statCol, cellFlags);

                    // Kolom 3: Badge Pill
                    string badgeLabel = "Standar";
                    Color pBg = UITheme.BgTableHeader;
                    Color pBorder = UITheme.BorderStrong;
                    Color pText = UITheme.TextSecondary;

                    if (svc.StartMode == "Disabled")
                    {
                        badgeLabel = "Terkunci Mati";
                        pBg = UITheme.RoseBadgeBg;
                        pBorder = UITheme.RoseBadgeBorder;
                        pText = UITheme.RoseBadgeText;
                    }
                    else if (svc.StartMode == "Manual")
                    {
                        badgeLabel = "Siap On-Demand";
                        pBg = UITheme.EmeraldBadgeBg;
                        pBorder = UITheme.EmeraldBadgeBorder;
                        pText = UITheme.EmeraldBadgeText;
                    }
                    else if (svc.StartMode == "Automatic")
                    {
                        badgeLabel = "Otomatis Windows";
                        pBg = UITheme.SkyBadgeBg;
                        pBorder = UITheme.SkyBadgeBorder;
                        pText = UITheme.SkyBadgeText;
                    }

                    int pillH = Math.Min(22, rowH - 4);
                    int pillW = Math.Min(138, col3W - 16);
                    Rectangle pillRect = new Rectangle(col3X + 8, curY + (rowH - pillH) / 2, pillW, pillH);
                    if (pillRect.Width > 20 && pillRect.Height > 10)
                    {
                        using (GraphicsPath pillPath = UITheme.CreateRoundedPath(pillRect, 6))
                        {
                            using (SolidBrush pb = new SolidBrush(pBg))
                            {
                                g.FillPath(pb, pillPath);
                            }
                            using (Pen pp = new Pen(pBorder, 1f))
                            {
                                g.DrawPath(pp, pillPath);
                            }
                        }
                        TextFormatFlags pillFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                        TextRenderer.DrawText(g, badgeLabel, boldFont, pillRect, pText, pillFlags);
                    }

                    curY += rowH;
                }
            }
        }
    }

    public class GlassPanel : Panel
    {
        public Color CardBgColor { get; set; }
        public Color CardBorderColor { get; set; }
        public int CornerRadius { get; set; }

        public GlassPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            CardBgColor = UITheme.BgSurface;
            CardBorderColor = UITheme.BorderCard;
            CornerRadius = 10;
            BackColor = UITheme.BgWindow;
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
                using (Pen pen = new Pen(CardBorderColor, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            base.OnPaint(e);
        }
    }

    /// <summary>
    /// Tombol Minimalis-Slate dengan Cincin Fokus Keyboard 3px (Impeccable Skill) & dua baris teks terukur.
    /// </summary>
    public class GlassButton : Button
    {
        private bool isHovered = false;
        private bool isPressed = false;

        public Color BaseColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color BorderLineColor { get; set; }
        public string SubText { get; set; }
        public int CornerRadius { get; set; }

        public GlassButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            BaseColor = UITheme.SlateBtnBg;
            HoverColor = UITheme.SlateBtnHover;
            PressedColor = UITheme.SlateBtnPress;
            BorderLineColor = UITheme.BorderStrong;
            CornerRadius = 8;
            ForeColor = UITheme.TextPrimary;
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            UseMnemonic = false;
            SubText = string.Empty;
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

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Bersihkan latar sudut dengan warna induk
            Color parentBg = (Parent != null) ? Parent.BackColor : UITheme.BgWindow;
            using (SolidBrush clearBrush = new SolidBrush(parentBg))
            {
                g.FillRectangle(clearBrush, ClientRectangle);
            }

            Rectangle rect = new Rectangle(2, 2, Width - 5, Height - 5);
            if (rect.Width <= 4 || rect.Height <= 4) return;

            Color fill = !Enabled ? UITheme.BgTableHeader : (isPressed ? PressedColor : (isHovered ? HoverColor : BaseColor));
            Color border = !Enabled ? UITheme.BorderCard : BorderLineColor;
            Color textCol = !Enabled ? UITheme.TextMuted : ForeColor;

            using (GraphicsPath path = UITheme.CreateRoundedPath(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                // Cincin fokus keyboard 3px sesuai pedoman Impeccable
                if (Focused && Enabled)
                {
                    using (Pen focusPen = new Pen(UITheme.BorderFocus, 2.5f))
                    {
                        g.DrawPath(focusPen, path);
                    }
                }
                else
                {
                    using (Pen pen = new Pen(border, 1.2f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            if (string.IsNullOrEmpty(SubText))
            {
                Rectangle textRect = new Rectangle(8, 4, Width - 16, Height - 8);
                TextRenderer.DrawText(g, Text, Font, textRect, textCol, flags);
            }
            else
            {
                using (Font subFont = new Font("Segoe UI", 8.25f, FontStyle.Regular))
                {
                    Size mainSize = TextRenderer.MeasureText(g, Text, Font);
                    Size subSize = TextRenderer.MeasureText(g, SubText, subFont);
                    int totalH = mainSize.Height + 2 + subSize.Height;
                    int startY = Math.Max(4, (Height - totalH) / 2);

                    Rectangle rMain = new Rectangle(8, startY, Width - 16, mainSize.Height + 2);
                    Rectangle rSub = new Rectangle(8, startY + mainSize.Height + 1, Width - 16, subSize.Height + 2);

                    TextRenderer.DrawText(g, Text, Font, rMain, textCol, flags);
                    TextRenderer.DrawText(g, SubText, subFont, rSub, textCol, flags);
                }
            }
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

    #region Engine Pemblokiran (Registry 64-Bit Native)
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

        private static RegistryKey OpenHKLM64()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        public static string GetWindowsVersionDisplay()
        {
            try
            {
                using (RegistryKey hklm = OpenHKLM64())
                using (RegistryKey key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
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

            try
            {
                using (RegistryKey hklm = OpenHKLM64())
                using (RegistryKey key = hklm.OpenSubKey(REG_UX_SETTINGS, false))
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

            try
            {
                using (RegistryKey hklm = OpenHKLM64())
                {
                    using (RegistryKey keyAU = hklm.OpenSubKey(REG_POLICY_AU, false))
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

                    using (RegistryKey keyWU = hklm.OpenSubKey(REG_POLICY_WU, false))
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
            }
            catch
            {
            }

            int disabledCount = 0;
            foreach (string svcName in TARGET_SERVICES)
            {
                ServiceInfoItem item = new ServiceInfoItem();
                item.ServiceName = svcName;
                item.DisplayName = GetServiceFriendlyName(svcName);

                try
                {
                    using (RegistryKey hklm = OpenHKLM64())
                    using (RegistryKey key = hklm.OpenSubKey(REG_SERVICES_BASE + @"\" + svcName, false))
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
                    item.StartMode = "Terkunci";
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
            log("[MODE 1] Menerapkan Smart Pause 2077 (Store & Defender tetap aman)...");

            try
            {
                using (RegistryKey hklm = OpenHKLM64())
                using (RegistryKey key = hklm.CreateSubKey(REG_UX_SETTINGS))
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
                using (RegistryKey hklm = OpenHKLM64())
                {
                    using (RegistryKey keyWU = hklm.CreateSubKey(REG_POLICY_WU))
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

                    using (RegistryKey keyAU = hklm.CreateSubKey(REG_POLICY_AU))
                    {
                        if (keyAU != null)
                        {
                            keyAU.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                            keyAU.SetValue("AUOptions", 2, RegistryValueKind.DWord);
                            log("[OK] Group Policy NoAutoUpdate: 1 (Dinonaktifkan)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log("[GALAT] Group Policy: " + ex.Message);
            }

            log("[INFO] Mengatur layanan: Mematikan WaaSMedicSvc & UsoSvc...");
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
                using (RegistryKey hklm = OpenHKLM64())
                {
                    using (RegistryKey key = hklm.CreateSubKey(REG_UX_SETTINGS))
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
                    using (RegistryKey keyWU = hklm.CreateSubKey(REG_POLICY_WU))
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

                    using (RegistryKey keyAU = hklm.CreateSubKey(REG_POLICY_AU))
                    {
                        if (keyAU != null)
                        {
                            keyAU.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                            keyAU.SetValue("AUOptions", 2, RegistryValueKind.DWord);
                        }
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
                using (RegistryKey hklm = OpenHKLM64())
                using (RegistryKey key = hklm.OpenSubKey(REG_UX_SETTINGS, true))
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
                using (RegistryKey hklm = OpenHKLM64())
                {
                    using (RegistryKey keyWU = hklm.OpenSubKey(REG_POLICY_WU, true))
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

                    using (RegistryKey keyAU = hklm.OpenSubKey(REG_POLICY_AU, true))
                    {
                        if (keyAU != null)
                        {
                            keyAU.DeleteValue("NoAutoUpdate", false);
                            keyAU.DeleteValue("AUOptions", false);
                            log("[OK] Group Policy AU dikembalikan.");
                        }
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
                using (RegistryKey hklm = OpenHKLM64())
                using (RegistryKey keyWU = hklm.CreateSubKey(REG_POLICY_WU))
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
                using (RegistryKey hklm = OpenHKLM64())
                using (RegistryKey key = hklm.OpenSubKey(svcPath, true))
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

    #region Antarmuka Pengguna (Grid TableLayoutPanel Anti Tumpang Tindih)
    public class MainForm : Form
    {
        private bool isUpdatingUI = false;

        private HeaderBannerControl headerBanner;
        private TableLayoutPanel mainGrid;

        private MetricCardControl cardPause;
        private MetricCardControl cardPolicy;
        private MetricCardControl cardCache;

        private ServicesTableCard servicesTable;

        private GlassPanel actionsCard;
        private GlassButton btnSmartPause;
        private GlassButton btnTotalLock;
        private GlassButton btnRestore;
        private CheckBox chkBlockDrivers;
        private GlassButton btnCleanCache;
        private GlassButton btnRefresh;

        private GlassPanel logCard;
        private TextBox txtLog;

        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusAdmin;
        private ToolStripStatusLabel statusOsVersion;
        private ToolStripStatusLabel statusClock;
        private System.Windows.Forms.Timer clockTimer;

        public MainForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            InitializeComponent();
            RefreshSystemStatus();
            LogMessage("Sistem siap. Pilih salah satu mode proteksi di atas.");
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.Text = "Windows 11 Update Blocker v1.1 (Kontrol Permanen & Aman)";
            this.Size = new Size(940, 810);
            this.MinimumSize = new Size(880, 740);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = UITheme.BgWindow;
            this.ForeColor = UITheme.TextPrimary;
            this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            this.Icon = SystemIcons.Shield;

            // 1. Header Banner (Self-Measuring)
            headerBanner = new HeaderBannerControl();
            headerBanner.Dock = DockStyle.Top;
            headerBanner.Height = 82;

            // 2. Main Grid (TableLayoutPanel 4 Baris: Kartu Metrik, Tabel Layanan, Panel Operasi, Log)
            mainGrid = new TableLayoutPanel();
            mainGrid.Dock = DockStyle.Fill;
            mainGrid.BackColor = UITheme.BgWindow;
            mainGrid.Padding = new Padding(20, 14, 20, 14);
            mainGrid.ColumnCount = 1;
            mainGrid.RowCount = 4;
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f)); // Baris 0: 3 Kartu Metrik
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 232f)); // Baris 1: Tabel 5 Layanan
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 162f)); // Baris 2: Tombol Aksi & Opsi
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Baris 3: Log Riwayat

            // --- BARIS 0: 3 KARTU METRIK ---
            TableLayoutPanel metricsGrid = new TableLayoutPanel();
            metricsGrid.Dock = DockStyle.Fill;
            metricsGrid.Margin = new Padding(0, 0, 0, 10);
            metricsGrid.ColumnCount = 3;
            metricsGrid.RowCount = 1;
            metricsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            metricsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            metricsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            metricsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            cardPause = new MetricCardControl();
            cardPause.Dock = DockStyle.Fill;
            cardPause.Margin = new Padding(0, 0, 8, 0);
            cardPause.HeadingText = "JEDA PEMBARUAN";

            cardPolicy = new MetricCardControl();
            cardPolicy.Dock = DockStyle.Fill;
            cardPolicy.Margin = new Padding(4, 0, 4, 0);
            cardPolicy.HeadingText = "KEBIJAKAN GROUP POLICY";

            cardCache = new MetricCardControl();
            cardCache.Dock = DockStyle.Fill;
            cardCache.Margin = new Padding(8, 0, 0, 0);
            cardCache.HeadingText = "CACHE SOFTWARE DISTRIBUTION";

            metricsGrid.Controls.Add(cardPause, 0, 0);
            metricsGrid.Controls.Add(cardPolicy, 1, 0);
            metricsGrid.Controls.Add(cardCache, 2, 0);

            // --- BARIS 1: TABEL 5 LAYANAN ---
            servicesTable = new ServicesTableCard();
            servicesTable.Dock = DockStyle.Fill;
            servicesTable.Margin = new Padding(0, 0, 0, 10);

            // --- BARIS 2: PANEL OPERASI & TOMBOL KONTROL ---
            actionsCard = new GlassPanel();
            actionsCard.Dock = DockStyle.Fill;
            actionsCard.Margin = new Padding(0, 0, 0, 10);
            actionsCard.Padding = new Padding(14, 10, 14, 10);

            TableLayoutPanel actionsInnerGrid = new TableLayoutPanel();
            actionsInnerGrid.Dock = DockStyle.Fill;
            actionsInnerGrid.BackColor = UITheme.BgSurface;
            actionsInnerGrid.ColumnCount = 1;
            actionsInnerGrid.RowCount = 3;
            actionsInnerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            actionsInnerGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f)); // Judul
            actionsInnerGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f)); // 3 Tombol Mode
            actionsInnerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Checkbox + Utilitas

            Label lblActionsTitle = new Label();
            lblActionsTitle.Text = "Operasi & Mode Proteksi";
            lblActionsTitle.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            lblActionsTitle.ForeColor = UITheme.TextPrimary;
            lblActionsTitle.BackColor = UITheme.BgSurface;
            lblActionsTitle.Dock = DockStyle.Fill;
            lblActionsTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblActionsTitle.UseMnemonic = false;

            TableLayoutPanel buttonsRow = new TableLayoutPanel();
            buttonsRow.Dock = DockStyle.Fill;
            buttonsRow.BackColor = UITheme.BgSurface;
            buttonsRow.Margin = new Padding(0, 2, 0, 2);
            buttonsRow.ColumnCount = 3;
            buttonsRow.RowCount = 1;
            buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            buttonsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            btnSmartPause = new GlassButton();
            btnSmartPause.Dock = DockStyle.Fill;
            btnSmartPause.Margin = new Padding(0, 0, 6, 0);
            btnSmartPause.Text = "Mode 1: Smart Pause 2077";
            btnSmartPause.SubText = "Store & Defender Tetap Aman";
            btnSmartPause.BaseColor = UITheme.EmeraldBtnBg;
            btnSmartPause.HoverColor = UITheme.EmeraldBtnHover;
            btnSmartPause.PressedColor = UITheme.EmeraldBtnPress;
            btnSmartPause.BorderLineColor = UITheme.EmeraldBadgeBorder;
            btnSmartPause.ForeColor = Color.White;
            btnSmartPause.Click += new EventHandler(BtnSmartPause_Click);

            btnTotalLock = new GlassButton();
            btnTotalLock.Dock = DockStyle.Fill;
            btnTotalLock.Margin = new Padding(6, 0, 6, 0);
            btnTotalLock.Text = "Mode 2: Total Lockdown";
            btnTotalLock.SubText = "Matikan Seluruh 5 Layanan";
            btnTotalLock.BaseColor = UITheme.RoseBtnBg;
            btnTotalLock.HoverColor = UITheme.RoseBtnHover;
            btnTotalLock.PressedColor = UITheme.RoseBtnPress;
            btnTotalLock.BorderLineColor = UITheme.RoseBadgeBorder;
            btnTotalLock.ForeColor = Color.White;
            btnTotalLock.Click += new EventHandler(BtnTotalLock_Click);

            btnRestore = new GlassButton();
            btnRestore.Dock = DockStyle.Fill;
            btnRestore.Margin = new Padding(6, 0, 0, 0);
            btnRestore.Text = "Pulihkan Default Windows 11";
            btnRestore.SubText = "Kembalikan Pengaturan Standar";
            btnRestore.BaseColor = UITheme.SlateBtnBg;
            btnRestore.HoverColor = UITheme.SlateBtnHover;
            btnRestore.PressedColor = UITheme.SlateBtnPress;
            btnRestore.BorderLineColor = UITheme.BorderStrong;
            btnRestore.ForeColor = UITheme.TextPrimary;
            btnRestore.Click += new EventHandler(BtnRestore_Click);

            buttonsRow.Controls.Add(btnSmartPause, 0, 0);
            buttonsRow.Controls.Add(btnTotalLock, 1, 0);
            buttonsRow.Controls.Add(btnRestore, 2, 0);

            TableLayoutPanel utilityRow = new TableLayoutPanel();
            utilityRow.Dock = DockStyle.Fill;
            utilityRow.BackColor = UITheme.BgSurface;
            utilityRow.Margin = new Padding(0, 6, 0, 0);
            utilityRow.ColumnCount = 3;
            utilityRow.RowCount = 1;
            utilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            utilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152f));
            utilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152f));
            utilityRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            chkBlockDrivers = new CheckBox();
            chkBlockDrivers.Text = "Kunci pembaruan driver otomatis (ExcludeWUDriversInQualityUpdate)";
            chkBlockDrivers.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            chkBlockDrivers.ForeColor = UITheme.TextSecondary;
            chkBlockDrivers.BackColor = UITheme.BgSurface;
            chkBlockDrivers.Dock = DockStyle.Fill;
            chkBlockDrivers.Checked = true;
            chkBlockDrivers.UseMnemonic = false;
            chkBlockDrivers.CheckedChanged += new EventHandler(ChkBlockDrivers_CheckedChanged);

            btnCleanCache = new GlassButton();
            btnCleanCache.Dock = DockStyle.Fill;
            btnCleanCache.Margin = new Padding(6, 0, 4, 0);
            btnCleanCache.Text = "Bersihkan Cache";
            btnCleanCache.Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold);
            btnCleanCache.BaseColor = UITheme.BgCard;
            btnCleanCache.HoverColor = UITheme.SlateBtnBg;
            btnCleanCache.PressedColor = UITheme.SlateBtnHover;
            btnCleanCache.BorderLineColor = UITheme.BorderStrong;
            btnCleanCache.ForeColor = UITheme.SkyBadgeText;
            btnCleanCache.Click += new EventHandler(BtnCleanCache_Click);

            btnRefresh = new GlassButton();
            btnRefresh.Dock = DockStyle.Fill;
            btnRefresh.Margin = new Padding(4, 0, 0, 0);
            btnRefresh.Text = "Segarkan Status";
            btnRefresh.Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold);
            btnRefresh.BaseColor = UITheme.BgCard;
            btnRefresh.HoverColor = UITheme.SlateBtnBg;
            btnRefresh.PressedColor = UITheme.SlateBtnHover;
            btnRefresh.BorderLineColor = UITheme.BorderStrong;
            btnRefresh.ForeColor = UITheme.TextPrimary;
            btnRefresh.Click += new EventHandler(BtnRefresh_Click);

            utilityRow.Controls.Add(chkBlockDrivers, 0, 0);
            utilityRow.Controls.Add(btnCleanCache, 1, 0);
            utilityRow.Controls.Add(btnRefresh, 2, 0);

            actionsInnerGrid.Controls.Add(lblActionsTitle, 0, 0);
            actionsInnerGrid.Controls.Add(buttonsRow, 0, 1);
            actionsInnerGrid.Controls.Add(utilityRow, 0, 2);
            actionsCard.Controls.Add(actionsInnerGrid);

            // --- BARIS 3: LOG RIWAYAT SISTEM ---
            logCard = new GlassPanel();
            logCard.Dock = DockStyle.Fill;
            logCard.Margin = new Padding(0);
            logCard.Padding = new Padding(14, 10, 14, 12);

            TableLayoutPanel logInnerGrid = new TableLayoutPanel();
            logInnerGrid.Dock = DockStyle.Fill;
            logInnerGrid.BackColor = UITheme.BgSurface;
            logInnerGrid.ColumnCount = 1;
            logInnerGrid.RowCount = 2;
            logInnerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            logInnerGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            logInnerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Label lblLogTitle = new Label();
            lblLogTitle.Text = "Log Riwayat Sistem";
            lblLogTitle.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            lblLogTitle.ForeColor = UITheme.TextPrimary;
            lblLogTitle.BackColor = UITheme.BgSurface;
            lblLogTitle.Dock = DockStyle.Fill;
            lblLogTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblLogTitle.UseMnemonic = false;

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = UITheme.BgCard;
            txtLog.ForeColor = UITheme.TextPrimary;
            txtLog.Font = new Font("Consolas", 9f, FontStyle.Regular);
            txtLog.BorderStyle = BorderStyle.FixedSingle;
            txtLog.Dock = DockStyle.Fill;

            logInnerGrid.Controls.Add(lblLogTitle, 0, 0);
            logInnerGrid.Controls.Add(txtLog, 0, 1);
            logCard.Controls.Add(logInnerGrid);

            // Masukkan 4 baris ke Main Grid
            mainGrid.Controls.Add(metricsGrid, 0, 0);
            mainGrid.Controls.Add(servicesTable, 0, 1);
            mainGrid.Controls.Add(actionsCard, 0, 2);
            mainGrid.Controls.Add(logCard, 0, 3);

            // 3. Status Bar
            statusStrip = new StatusStrip();
            statusStrip.BackColor = UITheme.BgTableHeader;
            statusStrip.SizingGrip = false;
            statusStrip.Padding = new Padding(14, 3, 14, 3);

            statusAdmin = new ToolStripStatusLabel();
            statusAdmin.Text = "Hak Akses: Administrator";
            statusAdmin.ForeColor = UITheme.EmeraldBadgeText;
            statusAdmin.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
            statusAdmin.BorderSides = ToolStripStatusLabelBorderSides.Right;

            statusOsVersion = new ToolStripStatusLabel();
            statusOsVersion.Text = "Windows 11 Build: " + UpdateBlockerEngine.GetWindowsVersionDisplay();
            statusOsVersion.ForeColor = UITheme.TextSecondary;
            statusOsVersion.BorderSides = ToolStripStatusLabelBorderSides.Right;

            statusClock = new ToolStripStatusLabel();
            statusClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            statusClock.ForeColor = UITheme.TextMuted;

            statusStrip.Items.AddRange(new ToolStripItem[] { statusAdmin, statusOsVersion, statusClock });

            clockTimer = new System.Windows.Forms.Timer();
            clockTimer.Interval = 1000;
            clockTimer.Tick += new EventHandler(ClockTimer_Tick);
            clockTimer.Start();

            this.Controls.Add(mainGrid);
            this.Controls.Add(headerBanner);
            this.Controls.Add(statusStrip);

            this.ResumeLayout(false);
            this.PerformLayout();
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
            isUpdatingUI = true;
            try
            {
                SystemStatusSnapshot snapshot = UpdateBlockerEngine.ScanCurrentStatus();

                // 1. Perbarui Header Banner
                if (snapshot.IsTotalLockdown)
                {
                    headerBanner.BadgeText = "TOTAL LOCKDOWN AKTIF";
                    headerBanner.BadgeBgColor = UITheme.RoseBadgeBg;
                    headerBanner.BadgeBorderColor = UITheme.RoseBadgeBorder;
                    headerBanner.BadgeTextColor = UITheme.RoseBadgeText;
                }
                else if (snapshot.IsPausedTo2077)
                {
                    headerBanner.BadgeText = "SMART PAUSE 2077 AKTIF";
                    headerBanner.BadgeBgColor = UITheme.EmeraldBadgeBg;
                    headerBanner.BadgeBorderColor = UITheme.EmeraldBadgeBorder;
                    headerBanner.BadgeTextColor = UITheme.EmeraldBadgeText;
                }
                else
                {
                    headerBanner.BadgeText = "STANDAR (TIDAK TERKUNCI)";
                    headerBanner.BadgeBgColor = UITheme.AmberBadgeBg;
                    headerBanner.BadgeBorderColor = UITheme.AmberBadgeBorder;
                    headerBanner.BadgeTextColor = UITheme.AmberBadgeText;
                }
                headerBanner.Invalidate();

                // 2. Perbarui 3 Kartu Metrik
                if (snapshot.IsPausedTo2077)
                {
                    cardPause.ValueText = "Hingga 31 Des 2077";
                    cardPause.ValueColor = UITheme.EmeraldBadgeText;
                    cardPause.AccentColor = UITheme.EmeraldBadgeBorder;
                    cardPause.DetailText = "Pembaruan OS dijeda permanen";
                }
                else
                {
                    cardPause.ValueText = snapshot.PauseExpiryString;
                    cardPause.ValueColor = UITheme.AmberBadgeText;
                    cardPause.AccentColor = UITheme.AmberBadgeBorder;
                    cardPause.DetailText = "Windows Update dapat berjalan";
                }
                cardPause.Invalidate();

                if (snapshot.IsGroupPolicyLocked)
                {
                    cardPolicy.ValueText = "Terkunci (NoAutoUpdate)";
                    cardPolicy.ValueColor = UITheme.EmeraldBadgeText;
                    cardPolicy.AccentColor = UITheme.EmeraldBadgeBorder;
                    cardPolicy.DetailText = "Target: Windows 11 " + (string.IsNullOrEmpty(snapshot.TargetReleaseVersion) ? "Aktif" : snapshot.TargetReleaseVersion);
                }
                else
                {
                    cardPolicy.ValueText = "Standar Windows";
                    cardPolicy.ValueColor = UITheme.TextPrimary;
                    cardPolicy.AccentColor = UITheme.BorderStrong;
                    cardPolicy.DetailText = "Pembaruan otomatis diizinkan";
                }
                cardPolicy.Invalidate();

                cardCache.ValueText = UpdateBlockerEngine.FormatBytes(snapshot.CacheSizeBytes);
                cardCache.ValueColor = UITheme.SkyBadgeText;
                cardCache.AccentColor = UITheme.SkyBadgeBorder;
                cardCache.DetailText = snapshot.IsDriverUpdateBlocked ? "Driver Lock: Aktif" : "Driver Lock: Nonaktif";
                cardCache.Invalidate();

                chkBlockDrivers.Checked = snapshot.IsDriverUpdateBlocked;

                // 3. Perbarui Tabel Layanan
                servicesTable.SetItems(snapshot.Services);
            }
            finally
            {
                isUpdatingUI = false;
            }
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
                    MessageBox.Show("Mode 1 (Smart Pause 2077) berhasil diterapkan.\nWindows Update OS dijeda hingga tahun 2077.", "Sukses", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    MessageBox.Show("Mode 2 (Total Lockdown) berhasil diterapkan.\nSeluruh layanan Windows Update telah dimatikan dan dikunci.", "Sukses", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            if (isUpdatingUI || !chkBlockDrivers.Enabled) return;
            UpdateBlockerEngine.SetDriverBlockPolicy(chkBlockDrivers.Checked, LogMessage);
            RefreshSystemStatus();
        }
    }
    #endregion

    #region Titik Masuk Aplikasi
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
