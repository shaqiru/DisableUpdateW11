using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
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
    #region Win32 API & Privilege Management
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

                // Step 1: Ambil kepemilikan
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

                // Step 2: Berikan hak Full Control ke Builtin Administrators
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
                // Fallback menggunakan perintah subinacl/reg/takeown jika diperlukan
            }
        }
    }
    #endregion

    #region Model Status
    public class ServiceInfoItem
    {
        public string ServiceName { get; set; }
        public string DisplayName { get; set; }
        public string StartMode { get; set; } // Automatic, Manual, Disabled, Unknown
        public string Status { get; set; }    // Running, Stopped, NotInstalled
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
                        if (val != null)
                        {
                            return val.ToString();
                        }
                        val = key.GetValue("ReleaseId");
                        if (val != null)
                        {
                            return val.ToString();
                        }
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

            // 1. Periksa Pause 2077
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

            // 2. Periksa Group Policy
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

            // 3. Periksa Status Services
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
                            item.StartMode = "Not Installed";
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
                    item.Status = "Stopped / Not Found";
                }

                snapshot.Services.Add(item);
            }

            if (disabledCount >= 4 && snapshot.IsGroupPolicyLocked)
            {
                snapshot.IsTotalLockdown = true;
            }

            // 4. Hitung Cache Download
            snapshot.CacheSizeBytes = CalculateCacheSize();

            return snapshot;
        }

        private static string GetServiceFriendlyName(string name)
        {
            if (name == "wuauserv") return "Windows Update (wuauserv)";
            if (name == "UsoSvc") return "Update Orchestrator Service (UsoSvc)";
            if (name == "WaaSMedicSvc") return "Windows Update Medic Service (WaaSMedicSvc)";
            if (name == "BITS") return "Background Intelligent Transfer (BITS)";
            if (name == "DoSvc") return "Delivery Optimization (DoSvc)";
            return name;
        }

        public static void ApplySmartPause2077(bool excludeDrivers, Action<string> log)
        {
            log("=== MEMULAI MODE 1: SMART PAUSE 2077 (STORE & DEFENDER AMAN) ===");

            // 1. Tulis jeda update s/d 2077
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

                        log("[OK] Tanggal Pause Update dikunci ke: 2077-12-31T23:59:59Z");
                    }
                }
            }
            catch (Exception ex)
            {
                log("[GALAT] Gagal menulis registry Pause 2077: " + ex.Message);
            }

            // 2. Kunci Group Policy (Target Release Version + NoAutoUpdate)
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
                        keyWU.SetValue("SetDisableUXWUAccess", 0, RegistryValueKind.DWord); // Izinkan UI Store

                        if (excludeDrivers)
                        {
                            keyWU.SetValue("ExcludeWUDriversInQualityUpdate", 1, RegistryValueKind.DWord);
                            log("[OK] Blokir Driver Update Otomatis: Diaktifkan");
                        }
                        else
                        {
                            keyWU.DeleteValue("ExcludeWUDriversInQualityUpdate", false);
                        }

                        log("[OK] Group Policy Target Release dikunci ke: Windows 11 " + winVer);
                    }
                }

                using (RegistryKey keyAU = Registry.LocalMachine.CreateSubKey(REG_POLICY_AU))
                {
                    if (keyAU != null)
                    {
                        keyAU.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                        keyAU.SetValue("AUOptions", 2, RegistryValueKind.DWord); // Pemberitahuan unduh
                        log("[OK] Group Policy NoAutoUpdate disetel ke: 1 (Nonaktifkan Otomatis)");
                    }
                }
            }
            catch (Exception ex)
            {
                log("[GALAT] Gagal mengatur Group Policy: " + ex.Message);
            }

            // 3. Konfigurasi Services:
            // Mode 1: Matikan WaaSMedicSvc & UsoSvc (remediasi & orkestrator)
            // Biarkan wuauserv, BITS, DoSvc tetap MANUAL agar Microsoft Store & Defender jalan normal!
            log("[INFO] Mengatur Service: Mematikan WaaSMedicSvc & UsoSvc, mengizinkan Store...");
            SetServiceStartMode("WaaSMedicSvc", 4, true, log);
            SetServiceStartMode("UsoSvc", 4, true, log);

            SetServiceStartMode("wuauserv", 3, false, log); // Manual
            SetServiceStartMode("BITS", 3, false, log);     // Manual
            SetServiceStartMode("DoSvc", 3, false, log);    // Manual

            // 4. Matikan Scheduled Tasks
            DisableScheduledTasks(log);

            log("=== SELESAI: MODE 1 BERHASIL DITERAPKAN DENGAN SUKSES ===");
        }

        public static void ApplyTotalLockdown(bool excludeDrivers, Action<string> log)
        {
            log("=== MEMULAI MODE 2: TOTAL LOCKDOWN (100% MATIKAN SEMUA SERVICE) ===");

            // 1. Tulis jeda update s/d 2077 & Group Policy
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
                        keyWU.SetValue("SetDisableUXWUAccess", 1, RegistryValueKind.DWord); // Kunci akses menu WU

                        if (excludeDrivers)
                        {
                            keyWU.SetValue("ExcludeWUDriversInQualityUpdate", 1, RegistryValueKind.DWord);
                        }

                        log("[OK] Group Policy Kunci Total diterapkan (TargetRelease + SetDisableUXWUAccess = 1)");
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
                log("[GALAT] Gagal menulis konfigurasi policy lockdown: " + ex.Message);
            }

            // 2. Matikan SEMUA 5 Service
            log("[INFO] Mematikan dan menonaktifkan seluruh 5 Windows Update Services...");
            foreach (string svcName in TARGET_SERVICES)
            {
                SetServiceStartMode(svcName, 4, true, log);
            }

            // 3. Matikan Scheduled Tasks
            DisableScheduledTasks(log);

            log("=== SELESAI: MODE 2 TOTAL LOCKDOWN AKTIF (Windows Update Mati Total) ===");
        }

        public static void RestoreDefault(Action<string> log)
        {
            log("=== MEMULAI RESTORE DEFAULT WINDOWS 11 ===");

            // 1. Hapus jeda update di UX Settings
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
                        log("[OK] Pengaturan Pause Update UX Settings dikembalikan ke default");
                    }
                }
            }
            catch (Exception ex)
            {
                log("[PERINGATAN] Gagal menghapus registry UX Settings: " + ex.Message);
            }

            // 2. Hapus Group Policy
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
                        log("[OK] Group Policy WindowsUpdate dikembalikan ke default");
                    }
                }

                using (RegistryKey keyAU = Registry.LocalMachine.OpenSubKey(REG_POLICY_AU, true))
                {
                    if (keyAU != null)
                    {
                        keyAU.DeleteValue("NoAutoUpdate", false);
                        keyAU.DeleteValue("AUOptions", false);
                        log("[OK] Group Policy AU dikembalikan ke default");
                    }
                }
            }
            catch (Exception ex)
            {
                log("[PERINGATAN] Gagal menghapus Group Policy: " + ex.Message);
            }

            // 3. Kembalikan Startup Service ke default Windows 11
            log("[INFO] Mengembalikan Startup Mode Services Windows 11...");
            SetServiceStartMode("wuauserv", 3, false, log);     // Manual
            SetServiceStartMode("BITS", 3, false, log);         // Manual
            SetServiceStartMode("DoSvc", 2, false, log);        // Automatic
            SetServiceStartMode("UsoSvc", 2, false, log);       // Automatic
            SetServiceStartMode("WaaSMedicSvc", 3, false, log); // Manual

            // 4. Aktifkan kembali Scheduled Tasks
            EnableScheduledTasks(log);

            log("=== SELESAI: PENGATURAN DEFAULT WINDOWS 11 BERHASIL DIPULIHKAN ===");
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
                log("[GALAT] Gagal mengubah opsi driver update: " + ex.Message);
            }
        }

        private static void SetServiceStartMode(string serviceName, int startMode, bool stopIfRunning, Action<string> log)
        {
            string svcPath = REG_SERVICES_BASE + @"\" + serviceName;

            // Jika WaaSMedicSvc, buka kepemilikan terlebih dahulu
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

                            // Jika di-disable, hapus FailureActions agar service tidak bangkit sendiri
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
                            log(string.Format("[OK] Service {0} disetel ke {1}", serviceName, modeDesc));
                        }
                        else
                        {
                            log(string.Format("[PERINGATAN] Service key {0} tidak ditemukan di Registry.", serviceName));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log(string.Format("[GALAT] Gagal menulis Start mode untuk {0}: {1}", serviceName, ex.Message));
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
                        log(string.Format("[OK] Service {0} berhasil dihentikan.", serviceName));
                    }
                }
            }
            catch
            {
                // Bila ServiceController timeout atau butuh paksa, panggil sc stop
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
            log("[INFO] Menonaktifkan Scheduled Tasks terkait pembaruan otomatis...");
            foreach (string task in TARGET_TASKS)
            {
                RunSchtasks(task, false, log);
            }
        }

        private static void EnableScheduledTasks(Action<string> log)
        {
            log("[INFO] Mengaktifkan kembali Scheduled Tasks...");
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
                        p.WaitForExit(4000);
                        if (p.ExitCode == 0)
                        {
                            log(string.Format("[OK] Task: {0} -> {1}", taskPath, enable ? "Aktif" : "Nonaktif"));
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

                if (!Directory.Exists(downloadPath))
                {
                    return 0;
                }

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
            log("=== MEMULAI PEMBERSIHAN CACHE SOFTWARE DISTRIBUTION ===");

            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string downloadPath = Path.Combine(windir, @"SoftwareDistribution\Download");

            if (!Directory.Exists(downloadPath))
            {
                log("[INFO] Direktori cache download tidak ditemukan.");
                return;
            }

            long beforeBytes = CalculateCacheSize();
            log(string.Format("[INFO] Ukuran cache saat ini: {0}", FormatBytes(beforeBytes)));

            // Hentikan wuauserv & BITS sementara agar berkas tidak terkunci
            log("[INFO] Menghentikan sementara wuauserv & BITS agar berkas tidak terkunci...");
            StopServiceSafely("wuauserv", log);
            StopServiceSafely("BITS", log);

            int deletedFiles = 0;
            int failedFiles = 0;

            try
            {
                DirectoryInfo di = new DirectoryInfo(downloadPath);
                FileInfo[] files = di.GetFiles();
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        files[i].Delete();
                        deletedFiles++;
                    }
                    catch
                    {
                        failedFiles++;
                    }
                }

                DirectoryInfo[] dirs = di.GetDirectories();
                for (int i = 0; i < dirs.Length; i++)
                {
                    try
                    {
                        dirs[i].Delete(true);
                    }
                    catch
                    {
                        failedFiles++;
                    }
                }
            }
            catch (Exception ex)
            {
                log("[GALAT] Gagal membersihkan direktori cache: " + ex.Message);
            }

            long afterBytes = CalculateCacheSize();
            long freedBytes = beforeBytes - afterBytes;
            if (freedBytes < 0) freedBytes = 0;

            log(string.Format("[OK] Pembersihan selesai! {0} berkas dihapus. Ruang disk dibebaskan: {1}",
                deletedFiles, FormatBytes(freedBytes)));
            log("=== SELESAI PEMBERSIHAN CACHE ===");
        }
    }
    #endregion

    #region Antarmuka Pengguna (WinForms GUI)
    public class MainForm : Form
    {
        private Panel panelHeader;
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblOverallBadge;

        private GroupBox grpStatus;
        private ListView lstServices;
        private Label lblPauseStatus;
        private Label lblPolicyStatus;
        private Label lblCacheStatus;

        private GroupBox grpActions;
        private Button btnSmartPause;
        private Button btnTotalLock;
        private Button btnRestore;
        private Button btnCleanCache;
        private Button btnRefresh;
        private CheckBox chkBlockDrivers;

        private GroupBox grpLog;
        private TextBox txtLog;

        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusAdmin;
        private ToolStripStatusLabel statusOsVersion;
        private ToolStripStatusLabel statusClock;
        private System.Windows.Forms.Timer clockTimer;

        public MainForm()
        {
            InitializeComponent();
            RefreshSystemStatus();
            LogMessage("Aplikasi siap. Silakan pilih salah satu opsi di atas.");
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.Text = "Windows 11 Update Blocker v1.0 — Kontrol Permanen & Aman";
            this.Size = new Size(860, 720);
            this.MinimumSize = new Size(800, 650);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(244, 246, 249);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // 1. Header Banner
            panelHeader = new Panel();
            panelHeader.Dock = DockStyle.Top;
            panelHeader.Height = 85;
            panelHeader.BackColor = Color.FromArgb(28, 35, 49);
            panelHeader.Padding = new Padding(18, 12, 18, 12);

            lblTitle = new Label();
            lblTitle.Text = "🛡️ Windows 11 Update Blocker";
            lblTitle.Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(16, 12);

            lblSubtitle = new Label();
            lblSubtitle.Text = "Blokir permanen pembaruan Windows 11 tanpa merusak Microsoft Store & Defender";
            lblSubtitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblSubtitle.ForeColor = Color.FromArgb(170, 185, 205);
            lblSubtitle.AutoSize = true;
            lblSubtitle.Location = new Point(20, 46);

            lblOverallBadge = new Label();
            lblOverallBadge.Text = "MEMERIKSA STATUS...";
            lblOverallBadge.Font = new Font("Segoe UI Bold", 10.5f, FontStyle.Bold);
            lblOverallBadge.ForeColor = Color.White;
            lblOverallBadge.BackColor = Color.FromArgb(70, 80, 95);
            lblOverallBadge.TextAlign = ContentAlignment.MiddleCenter;
            lblOverallBadge.Size = new Size(230, 36);
            lblOverallBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblOverallBadge.Location = new Point(panelHeader.Width - 255, 24);

            panelHeader.Controls.Add(lblTitle);
            panelHeader.Controls.Add(lblSubtitle);
            panelHeader.Controls.Add(lblOverallBadge);

            // 2. Status GroupBox
            grpStatus = new GroupBox();
            grpStatus.Text = "📊 Status Proteksi & Layanan Sistem";
            grpStatus.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
            grpStatus.ForeColor = Color.FromArgb(30, 41, 59);
            grpStatus.Location = new Point(18, 95);
            grpStatus.Size = new Size(808, 190);
            grpStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            lblPauseStatus = new Label();
            lblPauseStatus.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblPauseStatus.Location = new Point(16, 26);
            lblPauseStatus.Size = new Size(380, 22);
            lblPauseStatus.Text = "Status Pause: Memuat...";

            lblPolicyStatus = new Label();
            lblPolicyStatus.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblPolicyStatus.Location = new Point(410, 26);
            lblPolicyStatus.Size = new Size(380, 22);
            lblPolicyStatus.Text = "Group Policy: Memuat...";

            lblCacheStatus = new Label();
            lblCacheStatus.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblCacheStatus.Location = new Point(16, 50);
            lblCacheStatus.Size = new Size(760, 22);
            lblCacheStatus.Text = "Cache Unduhan: Memuat...";

            lstServices = new ListView();
            lstServices.View = View.Details;
            lstServices.FullRowSelect = true;
            lstServices.GridLines = true;
            lstServices.Font = new Font("Segoe UI", 9f);
            lstServices.Location = new Point(16, 76);
            lstServices.Size = new Size(776, 102);
            lstServices.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            lstServices.Columns.Add("Nama Layanan (Service)", 320);
            lstServices.Columns.Add("Tipe Startup", 160);
            lstServices.Columns.Add("Kondisi Berjalan", 150);
            lstServices.Columns.Add("Indikator", 120);

            grpStatus.Controls.Add(lblPauseStatus);
            grpStatus.Controls.Add(lblPolicyStatus);
            grpStatus.Controls.Add(lblCacheStatus);
            grpStatus.Controls.Add(lstServices);

            // 3. Action GroupBox
            grpActions = new GroupBox();
            grpActions.Text = "⚡ Operasi & Mode Kontrol";
            grpActions.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
            grpActions.ForeColor = Color.FromArgb(30, 41, 59);
            grpActions.Location = new Point(18, 292);
            grpActions.Size = new Size(808, 140);
            grpActions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            btnSmartPause = new Button();
            btnSmartPause.Text = "Mode 1: Smart Pause 2077\r\n(Store & Defender Aman)";
            btnSmartPause.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            btnSmartPause.BackColor = Color.FromArgb(16, 124, 65); // Modern Green
            btnSmartPause.ForeColor = Color.White;
            btnSmartPause.FlatStyle = FlatStyle.Flat;
            btnSmartPause.FlatAppearance.BorderSize = 0;
            btnSmartPause.Location = new Point(16, 30);
            btnSmartPause.Size = new Size(245, 50);
            btnSmartPause.Cursor = Cursors.Hand;
            btnSmartPause.Click += new EventHandler(BtnSmartPause_Click);

            btnTotalLock = new Button();
            btnTotalLock.Text = "Mode 2: Total Lockdown\r\n(100% Matikan Semua Service)";
            btnTotalLock.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            btnTotalLock.BackColor = Color.FromArgb(196, 43, 28); // Modern Red
            btnTotalLock.ForeColor = Color.White;
            btnTotalLock.FlatStyle = FlatStyle.Flat;
            btnTotalLock.FlatAppearance.BorderSize = 0;
            btnTotalLock.Location = new Point(275, 30);
            btnTotalLock.Size = new Size(245, 50);
            btnTotalLock.Cursor = Cursors.Hand;
            btnTotalLock.Click += new EventHandler(BtnTotalLock_Click);

            btnRestore = new Button();
            btnRestore.Text = "Restore Default\r\nWindows 11";
            btnRestore.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            btnRestore.BackColor = Color.FromArgb(90, 102, 120); // Slate Grey
            btnRestore.ForeColor = Color.White;
            btnRestore.FlatStyle = FlatStyle.Flat;
            btnRestore.FlatAppearance.BorderSize = 0;
            btnRestore.Location = new Point(535, 30);
            btnRestore.Size = new Size(255, 50);
            btnRestore.Cursor = Cursors.Hand;
            btnRestore.Click += new EventHandler(BtnRestore_Click);

            chkBlockDrivers = new CheckBox();
            chkBlockDrivers.Text = "Sertakan Blokir Update Driver Otomatis (ExcludeWUDriversInQualityUpdate)";
            chkBlockDrivers.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            chkBlockDrivers.Location = new Point(18, 92);
            chkBlockDrivers.Size = new Size(490, 26);
            chkBlockDrivers.Checked = true;
            chkBlockDrivers.CheckedChanged += new EventHandler(ChkBlockDrivers_CheckedChanged);

            btnCleanCache = new Button();
            btnCleanCache.Text = "🧹 Bersihkan Cache";
            btnCleanCache.Font = new Font("Segoe UI", 9f);
            btnCleanCache.BackColor = Color.FromArgb(230, 236, 245);
            btnCleanCache.FlatStyle = FlatStyle.Flat;
            btnCleanCache.FlatAppearance.BorderColor = Color.FromArgb(200, 210, 225);
            btnCleanCache.Location = new Point(535, 90);
            btnCleanCache.Size = new Size(130, 32);
            btnCleanCache.Cursor = Cursors.Hand;
            btnCleanCache.Click += new EventHandler(BtnCleanCache_Click);

            btnRefresh = new Button();
            btnRefresh.Text = "🔄 Segarkan";
            btnRefresh.Font = new Font("Segoe UI", 9f);
            btnRefresh.BackColor = Color.FromArgb(230, 236, 245);
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(200, 210, 225);
            btnRefresh.Location = new Point(675, 90);
            btnRefresh.Size = new Size(115, 32);
            btnRefresh.Cursor = Cursors.Hand;
            btnRefresh.Click += new EventHandler(BtnRefresh_Click);

            grpActions.Controls.Add(btnSmartPause);
            grpActions.Controls.Add(btnTotalLock);
            grpActions.Controls.Add(btnRestore);
            grpActions.Controls.Add(chkBlockDrivers);
            grpActions.Controls.Add(btnCleanCache);
            grpActions.Controls.Add(btnRefresh);

            // 4. Log GroupBox
            grpLog = new GroupBox();
            grpLog.Text = "📝 Log Aktivitas";
            grpLog.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
            grpLog.ForeColor = Color.FromArgb(30, 41, 59);
            grpLog.Location = new Point(18, 438);
            grpLog.Size = new Size(808, 185);
            grpLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.FromArgb(18, 24, 38);
            txtLog.ForeColor = Color.FromArgb(100, 255, 180);
            txtLog.Font = new Font("Consolas", 9f, FontStyle.Regular);
            txtLog.Dock = DockStyle.Fill;

            grpLog.Controls.Add(txtLog);

            // 5. StatusStrip
            statusStrip = new StatusStrip();
            statusStrip.BackColor = Color.FromArgb(230, 235, 242);

            statusAdmin = new ToolStripStatusLabel();
            statusAdmin.Text = "Hak Akses: Administrator";
            statusAdmin.ForeColor = Color.FromArgb(20, 100, 30);
            statusAdmin.BorderSides = ToolStripStatusLabelBorderSides.Right;

            statusOsVersion = new ToolStripStatusLabel();
            statusOsVersion.Text = "Windows 11 Version: " + UpdateBlockerEngine.GetWindowsVersionDisplay();
            statusOsVersion.BorderSides = ToolStripStatusLabelBorderSides.Right;

            statusClock = new ToolStripStatusLabel();
            statusClock.Text = DateTime.Now.ToString("HH:mm:ss");

            statusStrip.Items.Add(statusAdmin);
            statusStrip.Items.Add(statusOsVersion);
            statusStrip.Items.Add(statusClock);

            clockTimer = new System.Windows.Forms.Timer();
            clockTimer.Interval = 1000;
            clockTimer.Tick += new EventHandler(ClockTimer_Tick);
            clockTimer.Start();

            // Pasang semua kontrol ke Form
            this.Controls.Add(grpLog);
            this.Controls.Add(grpActions);
            this.Controls.Add(grpStatus);
            this.Controls.Add(panelHeader);
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
            SystemStatusSnapshot snapshot = UpdateBlockerEngine.ScanCurrentStatus();

            // Perbarui Badge Header
            if (snapshot.IsTotalLockdown)
            {
                lblOverallBadge.Text = "🔒 TOTAL LOCKDOWN";
                lblOverallBadge.BackColor = Color.FromArgb(196, 43, 28); // Red
            }
            else if (snapshot.IsPausedTo2077)
            {
                lblOverallBadge.Text = "🛡️ SMART PAUSE 2077";
                lblOverallBadge.BackColor = Color.FromArgb(16, 124, 65); // Green
            }
            else
            {
                lblOverallBadge.Text = "⚠️ TIDAK TERKUNCI";
                lblOverallBadge.BackColor = Color.FromArgb(180, 110, 15); // Amber/Orange
            }

            // Status Text
            lblPauseStatus.Text = "Status Pause: " + (snapshot.IsPausedTo2077 ? "Aktif s/d " + snapshot.PauseExpiryString : snapshot.PauseExpiryString);
            lblPolicyStatus.Text = "Group Policy: " + (snapshot.IsGroupPolicyLocked ? "Terkunci (NoAutoUpdate=1)" : "Standar / Tidak Terkunci");
            lblCacheStatus.Text = string.Format("Ukuran Cache Unduhan: {0} | Driver Lock: {1}",
                UpdateBlockerEngine.FormatBytes(snapshot.CacheSizeBytes),
                snapshot.IsDriverUpdateBlocked ? "Aktif" : "Nonaktif");

            chkBlockDrivers.Checked = snapshot.IsDriverUpdateBlocked;

            // Perbarui ListView Services
            lstServices.Items.Clear();
            foreach (ServiceInfoItem svc in snapshot.Services)
            {
                ListViewItem lvi = new ListViewItem(svc.DisplayName);
                lvi.SubItems.Add(svc.StartMode);
                lvi.SubItems.Add(svc.Status);

                string indicator = "Normal";
                if (svc.StartMode == "Disabled")
                {
                    indicator = "⛔ Terkunci Mati";
                    lvi.ForeColor = Color.DarkRed;
                }
                else if (svc.StartMode == "Manual")
                {
                    indicator = "✔️ Siap On-Demand";
                    lvi.ForeColor = Color.DarkGreen;
                }
                else
                {
                    indicator = "⚡ Otomatis";
                    lvi.ForeColor = Color.DarkBlue;
                }
                lvi.SubItems.Add(indicator);
                lstServices.Items.Add(lvi);
            }
        }

        private void BtnSmartPause_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show(
                "Mode 1 akan mengunci Windows Update hingga 31 Desember 2077 dan mematikan pengaktif otomatis WaaSMedicSvc.\n\n" +
                "Microsoft Store dan Windows Defender akan TETAP BERFUNGSI NORMAL.\n\nLanjutkan penerapan?",
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
                    MessageBox.Show("Mode 1 (Smart Pause 2077) berhasil diterapkan!\nWindows Update OS dijeda hingga tahun 2077.", "Sukses", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            });
        }

        private void BtnTotalLock_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show(
                "Mode 2 akan MEMATIKAN SELURUH 5 Windows Update Services termasuk wuauserv, BITS, dan DoSvc.\n\n" +
                "CATATAN: Microsoft Store tidak akan dapat mengunduh aplikasi selama mode ini aktif.\n\nApakah Anda yakin ingin menerapkan Total Lockdown?",
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
                "Apakah Anda ingin mengembalikan semua pengaturan, layanan, dan tugas Windows Update ke kondisi standar Windows 11?",
                "Konfirmasi Restore Default",
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
                    MessageBox.Show("Pengaturan Windows Update berhasil dipulihkan ke default Windows 11.", "Sukses", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    MessageBox.Show("Cache unduhan Windows Update berhasil dibersihkan!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    #region Program Entry Point
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
