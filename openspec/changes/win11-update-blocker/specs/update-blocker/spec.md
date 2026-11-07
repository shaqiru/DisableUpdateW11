# Spesifikasi: Core Windows 11 Update Blocker

## Requirement 1: Mode Smart Pause 2077 (Store & Defender Aman)
Sistem HARUS dapat menghentikan pembaruan fitur dan kualitas Windows 11 hingga tahun 2077 tanpa mematikan fungsi unduhan Microsoft Store dan Windows Defender.

### Scenario: Mengaktifkan Mode Smart Pause 2077
- **GIVEN** aplikasi berjalan dengan hak akses Administrator
- **WHEN** pengguna menekan tombol "Mode 1: Smart Pause 2077 (Store & Defender Aman)"
- **THEN** sistem menulis tanggal jeda `2077-12-31T23:59:59Z` pada `HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings`
- **AND** sistem mengaktifkan Group Policy `NoAutoUpdate = 1` dan mengunci `TargetReleaseVersionInfo` ke versi Windows 11 saat ini
- **AND** sistem menonaktifkan `WaaSMedicSvc` dan `UsoSvc` serta Scheduled Tasks pembaruan
- **AND** sistem memastikan `wuauserv`, `BITS`, dan `DoSvc` disetel ke `Manual (3)` agar Microsoft Store dan Windows Defender tetap dapat bekerja.

## Requirement 2: Mode Total Lockdown (Matikan Semua Service)
Sistem HARUS menyediakan mode pemblokiran total yang mematikan seluruh layanan terkait Windows Update.

### Scenario: Mengaktifkan Mode Total Lockdown
- **GIVEN** aplikasi berjalan dengan hak akses Administrator
- **WHEN** pengguna menekan tombol "Mode 2: Total Lockdown (Matikan Semua Service)"
- **THEN** sistem menerapkan seluruh kunci Registry dan Scheduled Tasks dari Mode 1
- **AND** sistem menghentikan dan menyetel `Start = 4` (Disabled) pada `wuauserv`, `UsoSvc`, `WaaSMedicSvc`, `BITS`, dan `DoSvc`, serta menghapus `FailureActions` pada `WaaSMedicSvc`.

## Requirement 3: Pemulihan Default Windows 11 (Restore)
Sistem HARUS dapat mengembalikan konfigurasi Windows Update ke keadaan standar Windows 11 dalam 1 klik.

### Scenario: Mengembalikan Pengaturan Default
- **GIVEN** pemblokiran sedang aktif (Mode 1 atau Mode 2)
- **WHEN** pengguna menekan tombol "Restore Default Windows 11"
- **THEN** sistem menghapus kunci Pause 2077 dan kebijakan Group Policy Windows Update
- **AND** sistem mengembalikan `wuauserv`, `BITS`, `WaaSMedicSvc` ke `Manual (3)` serta `UsoSvc` dan `DoSvc` ke `Automatic (2)`
- **AND** sistem mengaktifkan kembali Scheduled Tasks pembaruan.

## Requirement 4: Pembersih Cache & Pemblokir Update Driver
Sistem HARUS menyediakan kontrol tambahan untuk memblokir pembaruan driver otomatis dan membersihkan cache unduhan Windows Update.

### Scenario: Membersihkan Cache SoftwareDistribution
- **GIVEN** aplikasi berjalan sebagai Administrator
- **WHEN** pengguna menekan tombol "Bersihkan Cache Update"
- **THEN** sistem menghentikan sementara `wuauserv` dan `BITS`, menghapus isi `C:\Windows\SoftwareDistribution\Download`, dan melaporkan jumlah ruang penyimpanan yang berhasil dibersihkan.
