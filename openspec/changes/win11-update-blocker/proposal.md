# Proposal: Aplikasi Pemblokir Permanen Windows Update (Windows 11)

## Ringkasan Masalah
Windows 11 secara agresif mengaktifkan kembali layanan pembaruan otomatis melalui `WaaSMedicSvc` (Windows Update Medic Service), `UsoSvc` (Update Orchestrator), dan tugas terjadwal (*Scheduled Tasks*), meskipun pengguna telah mematikan layanan `wuauserv`. Di sisi lain, mematikan `wuauserv` dan `DoSvc` secara paksa membuat Microsoft Store dan pembaruan definisi Windows Defender berhenti berfungsi.

## Solusi yang Diusulkan
Membangun aplikasi desktop ringan berbasis **C# (.NET Framework 4.8 WinForms)** yang dikompilasi menjadi **satu berkas `.exe` mandiri** tanpa ketergantungan eksternal, dengan dua mode proteksi utama:
1. **Mode 1: Smart Pause 2077 (Store & Defender Aman)**
   Mengunci jeda pembaruan (*Pause Updates*) hingga `2077-12-31`, menerapkan kunci Group Policy (`NoAutoUpdate`, `TargetReleaseVersion` ke versi Windows saat ini), memblokir pembaruan driver otomatis (`ExcludeWUDriversInQualityUpdate`), serta mematikan `WaaSMedicSvc` dan Scheduled Tasks pemulihan, sembari membiarkan `wuauserv`, `BITS`, dan `DoSvc` tetap tersedia untuk Microsoft Store & Windows Defender.
2. **Mode 2: Total Lockdown (Blokir Total 100%)**
   Menjalankan seluruh proteksi Mode 1 ditambah mematikan dan mengunci layanan `wuauserv`, `UsoSvc`, `WaaSMedicSvc`, `BITS`, dan `DoSvc` di tingkat Registry dan Service Control Manager.

## Cakupan Fitur
- **Panel Status Real-Time**: Menampilkan status langsung setiap service (`wuauserv`, `UsoSvc`, `WaaSMedicSvc`, `BITS`, `DoSvc`), status Group Policy, status Driver Update Lock, dan tanggal kedaluwarsa Pause Update.
- **2 Mode Pemblokiran + 1 Klik Restore Default**:
  - *Terapkan Smart Pause 2077 (Store Aman)*
  - *Terapkan Total Lockdown (Matikan Semua Service)*
  - *Pulihkan Default Windows 11 (Restore)*
- **Toggle Blokir Update Driver Otomatis**: Mengatur `ExcludeWUDriversInQualityUpdate` agar Windows Update tidak menimpa driver GPU/perangkat keras.
- **Pembersih Cache Windows Update**: Menghapus berkas unduhan tertunda di `C:\Windows\SoftwareDistribution\Download` dan menampilkan jumlah ruang disk yang berhasil dibebaskan.
- **Auto-Elevation (UAC Administrator)**: Memastikan aplikasi selalu berjalan dengan hak akses Administrator dan menggunakan hak `SeTakeOwnershipPrivilege` / `SeRestorePrivilege` untuk memodifikasi `WaaSMedicSvc`.
