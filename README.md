# Windows 11 Update Blocker

Aplikasi desktop GUI native Windows (C# .NET Framework 4.8) mandiri berukuran super ringan (~38 KB) untuk mengontrol dan memblokir pembaruan otomatis Windows 11 secara permanen, aman, dan tanpa merusak fungsi Microsoft Store maupun Windows Defender.

---

## 🚀 Fitur Utama

### 1. Mode 1: Smart Pause 2077 (Store & Defender Aman) — *Rekomendasi*
- Mengunci tanggal jeda pembaruan (*Pause Updates*) hingga **31 Desember 2077** di `HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings`.
- Menerapkan **Group Policy Lock** (`NoAutoUpdate = 1` dan `TargetReleaseVersion` dikunci ke versi Windows 11 saat ini, misal `23H2` atau `24H2`).
- Mematikan **`WaaSMedicSvc`** (Windows Update Medic Service) dan menghapus `FailureActions` agar sistem tidak meremediasi/menghidupkan kembali pembaruan secara diam-diam.
- Mematikan **`UsoSvc`** (Update Orchestrator Service) dan *Scheduled Tasks* pemindaian otomatis (`UpdateOrchestrator\Schedule Scan`, `WaaSMedic\PerformRemediation`, dll).
- **Keunggulan Utama**: Layanan `wuauserv`, `BITS`, dan `DoSvc` tetap dibiarkan dalam kondisi `Manual` (bukan Disabled). Dengan begitu, **Microsoft Store dan pembaruan definisi virus Windows Defender tetap bekerja 100% normal**.

### 2. Mode 2: Total Lockdown (100% Mati Total)
- Menerapkan semua proteksi Mode 1.
- Mengunci akses UI menu Windows Update (`SetDisableUXWUAccess = 1`).
- Mematikan total (`Disabled / Start = 4`) dan menghentikan seluruh 5 layanan:
  - `wuauserv` (Windows Update)
  - `UsoSvc` (Update Orchestrator)
  - `WaaSMedicSvc` (Windows Update Medic Service)
  - `BITS` (Background Intelligent Transfer Service)
  - `DoSvc` (Delivery Optimization)
- *Catatan*: Pada mode ini, Microsoft Store tidak dapat mengunduh aplikasi baru karena ketergantungan pada `wuauserv`.

### 3. Restore Default Windows 11 (1 Klik Pemulihan)
- Menghapus kunci registry jeda waktu 2077.
- Menghapus aturan Group Policy `TargetReleaseVersion`, `NoAutoUpdate`, dan `SetDisableUXWUAccess`.
- Mengembalikan tipe startup layanan ke setelan bawaan pabrik Windows 11:
  - `wuauserv`: Manual (3)
  - `BITS`: Manual (3)
  - `DoSvc`: Automatic (2)
  - `UsoSvc`: Automatic (2)
  - `WaaSMedicSvc`: Manual (3)
- Mengaktifkan kembali seluruh *Scheduled Tasks* pembaruan.

### 4. Blokir Pembaruan Driver Otomatis
- Checkbox langsung untuk menyetel kebijakan `ExcludeWUDriversInQualityUpdate = 1`, mencegah Windows Update menimpa driver kartu grafis (GPU) atau periferal Anda secara acak.

### 5. Pembersih Cache Unduhan (*SoftwareDistribution Cleaner*)
- Menghitung ukuran berkas unduhan update yang tertunda di `C:\Windows\SoftwareDistribution\Download`.
- Menghentikan sementara layanan pengunci berkas dan menghapus sisa file sementara untuk membebaskan ruang penyimpanan SSD/HDD Anda.

---

## 📁 Struktur Berkas

```
Blok Windows update/
│
├── Win11UpdateBlocker.exe      <-- Berkas aplikasi langsung pakai (Stand-alone executable)
├── build.ps1                   <-- Skrip kompilasi otomatis (.NET 4.8 / csc.exe)
├── README.md                   <-- Panduan penggunaan & dokumentasi teknis
│
├── src/
│   ├── Win11UpdateBlocker.cs   <-- Kode sumber lengkap (GUI WinForms, Engine, P/Invoke)
│   └── app.manifest            <-- Manifest UAC (requireAdministrator & PerMonitorV2 DPI)
│
└── openspec/
    └── changes/win11-update-blocker/
        ├── proposal.md         <-- Proposal arsitektur OpenSpec
        ├── design.md           <-- Dokumen desain teknis mendalam
        ├── tasks.md            <-- Status daftar tugas
        └── specs/update-blocker/spec.md
```

---

## 🛠️ Cara Penggunaan

1. **Jalankan Aplikasi**:
   Cukup klik dua kali `Win11UpdateBlocker.exe`.
   *(Aplikasi secara otomatis akan meminta konfirmasi hak akses UAC Administrator).*
2. **Pilih Mode**:
   - Klik tombol hijau **Mode 1: Smart Pause 2077** jika Anda ingin Windows Update OS berhenti selamanya tetapi Microsoft Store dan Defender tetap bisa dipakai.
   - Klik tombol merah **Mode 2: Total Lockdown** jika Anda ingin Windows Update benar-benar mati total tanpa sisa.
   - Klik **Restore Default Windows 11** kapan pun Anda ingin mengembalikan sistem ke kondisi semula.
3. **Pembersihan Disk**:
   Klik tombol **🧹 Bersihkan Cache** untuk membebaskan ruang disk dari sisa unduhan update yang belum terpasang.

---

## ⚙️ Cara Kompilasi Ulang (Opsional)

Jika Anda ingin memodifikasi kode sumber di `src/Win11UpdateBlocker.cs`, Anda dapat mengompilasi ulang dengan sangat mudah tanpa perlu menginstal Visual Studio atau SDK tambahan (cukup menggunakan compiler bawaan Windows 11):

Buka PowerShell di direktori ini, lalu jalankan:
```powershell
.\build.ps1
```
Hasil `Win11UpdateBlocker.exe` baru akan langsung dibuat dalam hitungan detik.
