# Daftar Tugas Implementasi (Tasks)

- [x] 1. Buat struktur kode sumber C# (`Win11UpdateBlocker.cs`) dan manifest UAC Administrator (`app.manifest`).
- [x] 2. Implementasikan modul `PrivilegeManager` dengan Win32 API (`SeTakeOwnershipPrivilege`, `SeRestorePrivilege`, Registry ACL) untuk membuka kunci `WaaSMedicSvc`.
- [x] 3. Implementasikan `UpdateBlockerEngine`:
  - [x] 3.1 Fungsi Pause Update hingga `2077-12-31T23:59:59Z`.
  - [x] 3.2 Fungsi Group Policy Lock (`NoAutoUpdate`, `TargetReleaseVersion`, `ExcludeWUDriversInQualityUpdate`).
  - [x] 3.3 Fungsi penguncian dan pemulihan Services (`wuauserv`, `UsoSvc`, `WaaSMedicSvc`, `BITS`, `DoSvc`).
  - [x] 3.4 Fungsi penonaktifan dan pemulihan Scheduled Tasks (`UpdateOrchestrator`, `WindowsUpdate`, `WaaSMedic`).
  - [x] 3.5 Fungsi penghitung dan pembersih cache `SoftwareDistribution\Download`.
- [x] 4. Bangun antarmuka GUI WinForms (`MainForm`) dengan Panel Status Real-Time, kartu indikator tiap komponen, tombol 2 Mode + Restore, checkbox Driver Lock, tombol Bersihkan Cache, dan kotak log aktivitas.
- [x] 5. Buat skrip kompilasi (`build.ps1`) menggunakan `csc.exe` (.NET Framework 4.8) dan verifikasi hasil kompilasi `.exe`.
