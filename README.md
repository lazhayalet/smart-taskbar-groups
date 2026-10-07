# Taskbar Groups 2.0

<p align="center">
  <img src="assets/Icon.ico" width="96" alt="Taskbar Groups icon" />
</p>

| 🇬🇧 English | 🇹🇷 Türkçe |
|---|---|
| A smart taskbar launcher & Windows workspace manager. | Akıllı görev çubuğu başlatıcısı ve Windows çalışma alanı yöneticisi. |

> Fork of [tjackenpacken/taskbar-groups](https://github.com/tjackenpacken/taskbar-groups), modernised into a .NET 8 WinForms app with SQLite. Originally by **tjackenpacken**.

---

## 🇬🇧 English

### What it does
- **Groups** — collect shortcuts into a named group with its own icon, width, colour and opacity; pin a single taskbar entry that opens a popup.
- **Workspaces** — open a set of applications and arrange their windows on the right monitors and screen halves in one click.
- **Discovery** — scan installed applications, browsers, Steam libraries and Microsoft Store packages into groups.
- **Diagnostics** — self-checks that say why something is broken and what to do.
- **Backup & restore** — one ZIP per backup, validated before it can be restored, pre-restore copy kept.
- **Legacy migration** — imports a 1.x `config/` tree automatically, after archiving it, without touching the originals.
- **Turkish UI** — Settings → General → Language.

### Requirements
- Windows 10 (build 17763) or Windows 11, x64.
- Self-contained publish: no separate .NET runtime install required.

### Download & install
| Edition | File | Where data lives |
|---|---|---|
| Installed | `TaskbarGroups-2.0.0-win-x64.zip` | `%LOCALAPPDATA%\TaskbarGroups` |
| Portable | `TaskbarGroups-2.0.0-portable-win-x64.zip` | `Data/` beside the exe |

1. Extract the ZIP.
2. Run `TaskbarGroups.exe`.
3. Create a group, add shortcuts, save.
4. In `Shortcuts/`, right-click the generated `.lnk` and choose **Pin to taskbar**.
5. Number keys 1–0 open items; **Ctrl+Enter** opens all when the group allows it.

### Build from source
```powershell
dotnet build TaskbarGroups.sln -c Release
dotnet test TaskbarGroups.sln -c Release
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
powershell -ExecutionPolicy Bypass -File scripts\pack-portable.ps1
powershell -ExecutionPolicy Bypass -File scripts\pack-install.ps1
```
Requires the .NET 8 SDK.

### Known limitations
- Windows 11 has no public taskbar-pin automation API → pinning stays one manual click on the generated `.lnk`.
- Microsoft Store shortcut window placement in workspaces is best-effort.
- `Shortcuts/` paths are baked into pinned taskbar entries; moving a portable folder requires re-pinning.

### License
MIT — see [LICENSE](LICENSE). Original copyright tjackenpacken; this fork is a derivative work under the same terms.

---

## 🇹🇷 Türkçe

### Ne yapar
- **Gruplar** — kısayolları adlandırılmış bir grupta topla; kendi ikonu, genişliği, rengi ve opaklığı olur; tek bir görev çubuğu girdisine sabitleyip açılır menüyü kullanırsın.
- **Çalışma alanları** — bir grup uygulamayı tek tıkla açıp pencerelerini doğru monitöre ve ekran bölümüne yerleştirir.
- **Keşif** — kurulu uygulamaları, tarayıcıları, Steam kütüphanelerini ve Microsoft Store paketlerini gruplara dönüştürür.
- **Tanılama** — bir şeyin neden bozuk olduğunu ve ne yapman gerektiğini söyleyen öz kontroller.
- **Yedekleme & geri yükleme** — tek ZIP yedek; geri yüklenmeden önce doğrulanır, önceki veri için kopya alınır.
- **Eski sürümden geçiş** — 1.x `config/` ağacını otomatik içe aktarır; önce arşivler, asıllara dokunmaz.
- **Türkçe arayüz** — Ayarlar → Genel → Dil.

### Gereksinimler
- Windows 10 (build 17763) veya Windows 11, x64.
- Bağımsız yayın: ayrıca .NET runtime kurmanıza gerek yok.

### İndir & kurulum
| Sürüm | Dosya | Verinin yeri |
|---|---|---|
| Kurulum | `TaskbarGroups-2.0.0-win-x64.zip` | `%LOCALAPPDATA%\TaskbarGroups` |
| Taşınabilir | `TaskbarGroups-2.0.0-portable-win-x64.zip` | exe'nin yanında `Data/` |

1. ZIP'i çıkar.
2. `TaskbarGroups.exe`'yi çalıştır.
3. Grup oluştur, kısayolları ekle, kaydet.
4. `Shortcuts/` klasöründe oluşan `.lnk` dosyasına sağ tıkla → **Görev çubuğuna sabitle**.
5. 1–0 tuşları öğeleri açar; grup izin veriyorsa **Ctrl+Enter** hepsini açar.

### Kaynaktan derle
```powershell
dotnet build TaskbarGroups.sln -c Release
dotnet test TaskbarGroups.sln -c Release
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
powershell -ExecutionPolicy Bypass -File scripts\pack-portable.ps1
powershell -ExecutionPolicy Bypass -File scripts\pack-install.ps1
```
.NET 8 SDK gerekir.

### Bilinen sınırlamalar
- Windows 11'in public görev çubuğu sabitleme API'si yoktur → sabitleme, oluşan `.lnk` dosyasına tek tıkla manuel kalır.
- Microsoft Store kısayollarının çalışma alanlarında pencere yerleşimi "elinden gelenin en iyisi" düzeydedir.
- `Shortcuts/` yolları sabitlenen görev çubuğu girdilerine gömülüdür; taşınabilir klasörü taşımak için yeniden sabitlemek gerekir.

### Lisans
MIT — bkz. [LICENSE](LICENSE). Orijinal telif tjackenpacken; bu fork aynı şartlarla türetilmiş bir çalışmadır.
