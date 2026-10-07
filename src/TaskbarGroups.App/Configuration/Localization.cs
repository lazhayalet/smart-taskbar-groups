using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace TaskbarGroups.App.Configuration
{
    /// <summary>
    /// UI language for the application. The language choice is stored in
    /// <c>AppSettings.Language</c> and applied to every open form.
    /// </summary>
    /// <remarks>
    /// Rather than touching a designer-generated form string by string, the
    /// localiser walks the control tree and replaces known English strings
    /// with their Turkish equivalent. Strings that were written in code and
    /// cannot be reached this way go through <see cref="T(string)"/> directly.
    /// </remarks>
    public static class Localization
    {
        public const string English = "en";
        public const string Turkish = "tr";

        private static readonly Dictionary<string, string> EnglishToTurkish = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // frmClient
            ["Taskbar Groups"] = "Taskbar Grupları",
            ["Search groups and shortcuts"] = "Grupları ve kısayolları ara",
            ["Press \"Add Taskbar group\" to get started"] = "Başlamak için \"Görev çubuğu grubu ekle\"ye basın",
            ["No group matches the current search."] = "Geçerli aramayla eşleşen grup yok.",
            ["Click a group to add a taskbar shortcut"] = "Görev çubuğu kısayolu eklemek için bir gruba tıklayın",
            ["group"] = "grup",
            ["groups"] = "grup",
            ["shortcut"] = "kısayol",
            ["shortcuts"] = "kısayol",

            // Common buttons and labels
            ["Add Taskbar group"] = "Görev çubuğu grubu ekle",
            ["Discovery"] = "Keşif",
            ["Workspaces"] = "Çalışma alanları",
            ["Backup"] = "Yedekle",
            ["Settings"] = "Ayarlar",
            ["Diagnostics"] = "Tanılama",
            ["Save"] = "Kaydet",
            ["Cancel"] = "İptal",
            ["Delete"] = "Sil",
            ["Exit"] = "Çıkış",
            ["Close"] = "Kapat",
            ["OK"] = "Tamam",
            ["Yes"] = "Evet",
            ["No"] = "Hayır",
            ["Browse..."] = "Gözat...",
            ["Select"] = "Seç",

            // frmGroup
            ["New group"] = "Yeni grup",
            ["Edit group"] = "Grubu düzenle",
            ["Add group icon"] = "Grup simgesi ekle",
            ["Change group icon"] = "Grup simgesini değiştir",
            ["Name the new group..."] = "Yeni gruba ad verin...",
            ["Add shortcuts...   (click, or drop .exe / .lnk / .url / scripts / folders / Store apps)"] = "Kısayol ekle...   (tıkla veya sürükle .exe / .lnk / .url / betik / klasör / Store uygulaması)",
            ["Arguments"] = "Argümanlar",
            ["Working directory"] = "Çalışma dizini",
            ["Run as administrator (asks for permission each time)"] = "Yönetici olarak çalıştır (her seferinde izin sorar)",
            ["Allow Ctrl+Enter to open everything in this group"] = "Ctrl+Enter ile gruptaki tüm öğeleri açmaya izin ver",
            ["That file could not be read as an image."] = "Bu dosya bir görüntü olarak okunamadı.",
            ["The icon could not be applied: "] = "Simge uygulanamadı: ",
            ["Must select a name"] = "Bir ad seçmelisiniz",
            ["That name cannot be used"] = "Bu ad kullanılamaz",
            ["There is already a group with that name"] = "Bu ada sahip bir grup zaten var",
            ["Must select at least one shortcut"] = "En az bir kısayol seçmelisiniz",
            ["Max "] = "En fazla ",
            [" shortcuts in one group"] = " kısayol bir grupta",
            ["The maximum width is "] = "En fazla genişlik: ",
            ["The minimum width is "] = "En az genişlik: ",
            ["The maximum opacity is 100"] = "En fazla opaklık 100",
            ["The minimum opacity is 0"] = "En az opaklık 0",

            // frmSettings
            ["Start with Windows"] = "Windows ile başlat",
            ["Start minimized"] = "Simge durumunda başlat",
            ["Start in tray"] = "Tepsi simgesiyle başlat",
            ["Confirm before open all"] = "Tümünü açmadan önce onayla",
            ["Enable script launching"] = "Betik başlatmayı etkinleştir",
            ["Warn about script launching"] = "Betik başlatma hakkında uyar",
            ["Default run as administrator"] = "Varsayılan olarak yönetici çalıştır",
            ["Icon cache enabled"] = "Simge önbelleği etkin",
            ["Preferred icon size"] = "Tercih edilen simge boyutu",
            ["Allow remote favicons"] = "Uzak favicon'lara izin ver",
            ["Add to Explorer context menu"] = "Explorer içerik menüsüne ekle",
            ["Automatic backup"] = "Otomatik yedekleme",
            ["Backup interval (hours)"] = "Yedekleme aralığı (saat)",
            ["Backup rotation count"] = "Yedek saklama sayısı",
            ["Check for updates on startup"] = "Başlangıçta güncelleme denetle",
            ["Offline mode"] = "Çevrimdışı mod",
            ["Enable global hotkeys"] = "Genel kısayolları etkinleştir",
            ["Enable logging"] = "Günlüklemeyi etkinleştir",
            ["Verbose logging"] = "Ayrıntılı günlükleme",
            ["Log level"] = "Günlük düzeyi",
            ["Group popup offset"] = "Grup açılır penceresi kaydırma",
            ["Group popup gap"] = "Grup açılır pencere boşluğu",
            ["Language"] = "Dil",
            ["General"] = "Genel",
            ["Behavior"] = "Davranış",
            ["Appearance"] = "Görünüm",
            ["About"] = "Hakkında",

            // frmWorkspaces / frmBackup / frmDiagnostics / frmDiscovery
            ["No workspaces yet."] = "Henüz çalışma alanı yok.",
            ["Create"] = "Oluştur",
            ["Duplicate"] = "Kopyala",
            ["Launch"] = "Başlat",
            ["Status"] = "Durum",
            ["Name"] = "Ad",
            ["Items"] = "Öğeler",
            ["Monitor"] = "Monitör",
            ["Layout"] = "Yerleşim",
            ["Scanning..."] = "Taranıyor...",
            ["Cancelled."] = "İptal edildi.",
            ["The scan failed: "] = "Tarama başarısız: ",
            [" application(s) found."] = " uygulama bulundu.",
            [" shown."] = " gösteriliyor.",
            ["Smart group"] = "Akıllı grup",
            ["Group name"] = "Grup adı",
            ["Behaviour"] = "Davranış",
            ["Create group"] = "Grup oluştur",

            // Tray
            ["Open Taskbar Groups"] = "Taskbar Grupları'nı aç",
            ["No groups yet"] = "Henüz grup yok",
            ["No workspaces yet"] = "Henüz çalışma alanı yok",

            // Validation / messages
            ["A group needs a name."] = "Grubun bir adı olmalı.",
            ["That workspace no longer exists."] = "O çalışma alanı artık yok.",
            ["The file contained no groups or workspaces."] = "Dosyada grup veya çalışma alanı yoktu.",
            ["Import groups and workspaces"] = "Grupları ve çalışma alanlarını içe aktar",
            ["Export groups and workspaces"] = "Grupları ve çalışma alanlarını dışa aktar",
            ["Add shortcuts to the group"] = "Gruba kısayol ekle",
            ["Choose a group icon"] = "Grup simgesi seç",
        };

        private static readonly Dictionary<string, string> TurkishToEnglish = new Dictionary<string, string>(StringComparer.Ordinal);

        static Localization()
        {
            foreach (KeyValuePair<string, string> pair in EnglishToTurkish)
                TurkishToEnglish[pair.Value] = pair.Key;
        }

        /// <summary>The active language code ("en" or "tr").</summary>
        public static string Current { get; set; } = English;

        public static bool IsTurkish => string.Equals(Current, Turkish, StringComparison.OrdinalIgnoreCase);

        /// <summary>Translates an English source string into the active language.</summary>
        public static string T(string english)
        {
            if (english == null || !IsTurkish) return english ?? string.Empty;
            return EnglishToTurkish.TryGetValue(english, out string? turkish) ? turkish : english;
        }

        /// <summary>Reverse-maps a Turkish string back to English (for stored settings).</summary>
        public static string? ToEnglish(string text)
        {
            if (text == null) return null;
            return TurkishToEnglish.TryGetValue(text, out string? english) ? english : text;
        }

        /// <summary>Replaces every known string on a control recursively.</summary>
        public static void ApplyTo(Control root)
        {
            if (root == null) return;

            ApplyOne(root);

            foreach (Control child in root.Controls)
                ApplyTo(child);

            if (root is Form form && form.MainMenuStrip != null)
                ApplyTo(form.MainMenuStrip);

            if (root is ToolStrip toolStrip)
            {
                foreach (ToolStripItem item in toolStrip.Items)
                    ApplyOne(item);
            }

            if (root is TabControl tabs)
            {
                foreach (TabPage page in tabs.TabPages)
                {
                    ApplyOne(page);
                    ApplyTo(page);
                }
            }
        }

        private static void ApplyOne(object target)
        {
            switch (target)
            {
                case Control control:
                    if (!string.IsNullOrEmpty(control.Text))
                        control.Text = IsTurkish ? T(control.Text) : ToEnglish(control.Text) ?? control.Text;
                    if (control is TextBox textBox && !string.IsNullOrEmpty(textBox.PlaceholderText))
                        textBox.PlaceholderText = IsTurkish ? T(textBox.PlaceholderText) : ToEnglish(textBox.PlaceholderText) ?? textBox.PlaceholderText;
                    break;

                case ToolStripItem item:
                    if (!string.IsNullOrEmpty(item.Text))
                        item.Text = IsTurkish ? T(item.Text) : ToEnglish(item.Text) ?? item.Text;
                    break;
            }
        }
    }
}