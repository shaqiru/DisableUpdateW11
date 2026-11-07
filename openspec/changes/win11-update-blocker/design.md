# Desain Teknis: Windows 11 Update Blocker

## 1. Arsitektur Komponen
Aplikasi dirancang menggunakan arsitektur berlapis dalam satu proyek C# mandiri yang dikompilasi dengan `csc.exe` (.NET Framework 4.8):

- **`Program` & Manifest UAC**: Memeriksa hak Administrator (`WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)`). Jika belum menjadi Administrator, aplikasi melakukan *re-launch* dengan `Verb = "runas"`.
- **`PrivilegeManager` (Win32 P/Invoke)**: Mengambil kepemilikan dan mengatur ACL pada key Registry yang diproteksi `TrustedInstaller` (khususnya `HKLM\SYSTEM\CurrentControlSet\Services\WaaSMedicSvc`) menggunakan `advapi32.dll` (`OpenProcessToken`, `LookupPrivilegeValue`, `AdjustTokenPrivileges` untuk `SeTakeOwnershipPrivilege` dan `SeRestorePrivilege`).
- **`UpdateBlockerEngine`**:
  - **Manajemen Pause 2077**: Menulis konfigurasi waktu ISO-8601 (`2077-12-31T23:59:59Z`) ke `HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings`.
  - **Manajemen Group Policy**: Menulis kunci kebijakan ke `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` dan subkey `AU`.
  - **Manajemen Service & ACL**: Menghentikan service aktif via `ServiceController` dan mengubah `Start` value di `HKLM\SYSTEM\CurrentControlSet\Services\<ServiceName>`.
  - **Manajemen Scheduled Tasks**: Menonaktifkan tugas di `\Microsoft\Windows\WindowsUpdate\` dan `\Microsoft\Windows\UpdateOrchestrator\` via `schtasks.exe`.
  - **Pembersihan Cache**: Menghentikan sementara `wuauserv`/`BITS`, menghitung ukuran dan menghapus isi `C:\Windows\SoftwareDistribution\Download`.
- **`MainForm` (WinForms GUI)**: Antarmuka gelap/modern yang bersih, menampilkan indikator kartu status secara real-time, tombol aksi utama, opsi blokir driver, pembersih cache, dan log aktivitas langsung.

## 2. Peta Target Sistem Windows 11

### A. Registry Pause Update s/d 2077
Key: `HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings`
- `FlightSettingsMaxPauseDays` (`REG_DWORD`) = `20000`
- `PauseFeatureUpdatesStartTime` (`REG_SZ`) = Waktu UTC saat ini (`yyyy-MM-ddTHH:mm:ssZ`)
- `PauseFeatureUpdatesEndTime` (`REG_SZ`) = `2077-12-31T23:59:59Z`
- `PauseQualityUpdatesStartTime` (`REG_SZ`) = Waktu UTC saat ini (`yyyy-MM-ddTHH:mm:ssZ`)
- `PauseQualityUpdatesEndTime` (`REG_SZ`) = `2077-12-31T23:59:59Z`
- `PauseUpdatesStartTime` (`REG_SZ`) = Waktu UTC saat ini (`yyyy-MM-ddTHH:mm:ssZ`)
- `PauseUpdatesExpiryTime` (`REG_SZ`) = `2077-12-31T23:59:59Z`

### B. Registry Group Policy Windows Update
Key: `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`
- `TargetReleaseVersion` (`REG_DWORD`) = `1`
- `TargetReleaseVersionInfo` (`REG_SZ`) = `DisplayVersion` dari `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` (contoh: `23H2`, `24H2`)
- `ProductVersion` (`REG_SZ`) = `Windows 11`
- `ExcludeWUDriversInQualityUpdate` (`REG_DWORD`) = `1` (jika opsi blokir driver aktif)
- `SetDisableUXWUAccess` (`REG_DWORD`) = `1` pada Mode Total Lockdown, `0` pada Mode Smart Pause

Key: `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU`
- `NoAutoUpdate` (`REG_DWORD`) = `1`
- `AUOptions` (`REG_DWORD`) = `2` (Notify before download)

### C. Target Windows Services
| Nama Service | Deskripsi | Mode Smart Pause 2077 | Mode Total Lockdown | Mode Restore Default |
| :--- | :--- | :--- | :--- | :--- |
| `WaaSMedicSvc` | Windows Update Medic Service | Disabled (4) + Clear FailureActions | Disabled (4) + Clear FailureActions | Manual (3) |
| `UsoSvc` | Update Orchestrator Service | Disabled (4) | Disabled (4) | Automatic (2) |
| `wuauserv` | Windows Update Service | Manual (3) *(Store Aman)* | Disabled (4) + Stop | Manual (3) |
| `BITS` | Background Intelligent Transfer | Manual (3) *(Store Aman)* | Disabled (4) + Stop | Manual (3) |
| `DoSvc` | Delivery Optimization | Manual (3) *(Store Aman)* | Disabled (4) + Stop | Automatic (2) |

### D. Target Scheduled Tasks
- `\Microsoft\Windows\WindowsUpdate\Scheduled Start`
- `\Microsoft\Windows\UpdateOrchestrator\Schedule Scan`
- `\Microsoft\Windows\UpdateOrchestrator\Schedule Scan Static Task`
- `\Microsoft\Windows\UpdateOrchestrator\UpdateModelTask`
- `\Microsoft\Windows\UpdateOrchestrator\USO_UxBroker`
- `\Microsoft\Windows\WaaSMedic\PerformRemediation`
