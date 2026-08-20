// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace MagicKeys
{
    /// <summary>
    /// Добыча и установка родного драйвера клавиатур Apple.
    ///
    /// Самих файлов Apple в программе нет и быть не может — они несвободные, а MagicKeys
    /// под GPL. Но забрать их с серверов Apple и поставить на этой машине программа вправе:
    /// ровно это делает открытая утилита brigadier, и никакого распространения чужих файлов
    /// здесь не происходит. Пользователь решает сам, ставить ли ПО Apple на не-Apple железо.
    ///
    /// Путь такой: каталог обновлений Apple → самый свежий BootCampESD.pkg → распаковка
    /// (пакет xar, внутри Payload, внутри образ WindowsSupport.dmg) → поиск Keymagic2.inf →
    /// установка через pnputil с запросом прав.
    ///
    /// Формат DMG встроенный в Windows bsdtar не читает, поэтому для распаковки нужен 7-Zip.
    /// Его программа тоже не вкладывает в себя: если в системе его нет, скачивает
    /// официальный MSI и разворачивает административной установкой в свою папку —
    /// без установки в систему и без прав администратора.
    /// </summary>
    internal static class AppleDriverSetup
    {
        private const string Catalog =
            "https://swscan.apple.com/content/catalogs/others/" +
            "index-14-13-12-10.16-10.15-10.14-10.13-10.12-10.11-10.10-10.9-" +
            "mountainlion-lion-snowleopard-leopard.merged-1.sucatalog";

        /// <summary>Имена inf, которые нас интересуют: от новых моделей к старым.</summary>
        private static readonly string[] WantedInf =
        {
            "keymagic2.inf", "keymagic64.inf", "keymagic.inf", "keymanager.inf"
        };

        public static string CacheFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MagicKeys", "bootcamp");
            }
        }

        // ------------------------------------------------------------------
        //  7-Zip
        // ------------------------------------------------------------------

        /// <summary>Куда кладётся своя копия 7-Zip, если в системе его нет.</summary>
        public static string ToolsFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MagicKeys", "tools");
            }
        }

        private const string SevenZipMsi = "https://www.7-zip.org/a/7z2501-x64.msi";

        /// <summary>
        /// Достаёт 7-Zip, не устанавливая его в систему: скачивает официальный MSI и
        /// разворачивает административной установкой в свою папку. Прав администратора
        /// это не требует, в систему ничего не прописывается.
        /// </summary>
        public static string FetchSevenZip(Action<double, string> report, out string error)
        {
            error = null;
            try
            {
                string have = SevenZip();
                if (have != null) return have;

                Directory.CreateDirectory(ToolsFolder);
                // msiexec не любит длинные и необычные пути, поэтому скачиваем во временную папку.
                string msi = Path.Combine(Path.GetTempPath(), "magickeys-7zip.msi");
                report(0, "скачиваю 7-Zip…");
                if (!Download(SevenZipMsi, msi, report, null, out error)) return null;

                string target = Path.Combine(ToolsFolder, "7zip");
                report(0.9, "разворачиваю 7-Zip…");
                var psi = new ProcessStartInfo("msiexec.exe",
                    "/a \"" + msi + "\" TARGETDIR=\"" + target + "\" /qn");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(180000);
                    if (p.ExitCode != 0) { error = "msiexec вернул " + p.ExitCode; return null; }
                }

                try { File.Delete(msi); } catch { }
                string found = SevenZip();
                if (found == null) error = "7-Zip развернулся, но 7z.exe не нашёлся";
                return found;
            }
            catch (Exception e) { error = e.Message; return null; }
        }

        /// <summary>Путь к 7z.exe или null. Ищется в системе и в своей папке.</summary>
        public static string SevenZip()
        {
            var tries = new List<string>();
            tries.Add(Path.Combine(ToolsFolder, @"7zip\Files\7-Zip\7z.exe"));
            tries.Add(Path.Combine(ToolsFolder, @"7zip\7z.exe"));
            try
            {
                foreach (string view in new string[] { @"SOFTWARE\7-Zip", @"SOFTWARE\WOW6432Node\7-Zip" })
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(view, false))
                        if (k != null)
                        {
                            object p = k.GetValue("Path");
                            if (p != null) tries.Add(Path.Combine(Convert.ToString(p), "7z.exe"));
                        }
            }
            catch { }

            tries.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"7-Zip\7z.exe"));
            tries.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"7-Zip\7z.exe"));

            string path = Environment.GetEnvironmentVariable("PATH");
            if (path != null)
                foreach (string dir in path.Split(';'))
                    if (dir.Length > 0) { try { tries.Add(Path.Combine(dir, "7z.exe")); } catch { } }

            foreach (string t in tries)
                try { if (File.Exists(t)) return t; } catch { }
            return null;
        }

        // ------------------------------------------------------------------
        //  Каталог Apple
        // ------------------------------------------------------------------

        /// <summary>Самый свежий пакет Boot Camp: ссылка и дата выпуска.</summary>
        public static bool FindNewestPackage(out string url, out string posted, out string error)
        {
            url = null; posted = null; error = null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                string xml;
                using (var wc = new WebClient()) xml = wc.DownloadString(Catalog);

                // В каталоге продукты идут как <key>002-34411</key><dict>…</dict>.
                // Нас интересуют те, внутри которых встречается BootCampESD.pkg.
                var product = new Regex(@"<key>(\d{3}-\d{5})</key>", RegexOptions.Compiled);
                var esd = new Regex(@"<string>(https://[^<]*BootCampESD\.pkg)</string>", RegexOptions.Compiled);
                var date = new Regex(@"<key>PostDate</key>\s*<date>([^<]+)</date>", RegexOptions.Compiled);

                MatchCollection products = product.Matches(xml);
                string bestUrl = null; DateTime best = DateTime.MinValue; string bestDate = null;

                for (int i = 0; i < products.Count; i++)
                {
                    int start = products[i].Index;
                    int end = i + 1 < products.Count ? products[i + 1].Index : xml.Length;
                    string chunk = xml.Substring(start, end - start);

                    Match m = esd.Match(chunk);
                    if (!m.Success) continue;
                    Match d = date.Match(chunk);
                    DateTime when;
                    if (!d.Success || !DateTime.TryParse(d.Groups[1].Value, null,
                            System.Globalization.DateTimeStyles.AdjustToUniversal, out when)) continue;
                    if (when <= best) continue;
                    best = when; bestUrl = m.Groups[1].Value; bestDate = when.ToString("yyyy-MM-dd");
                }

                if (bestUrl == null) { error = "в каталоге Apple не нашлось ни одного пакета Boot Camp"; return false; }
                url = bestUrl; posted = bestDate;
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        public static long SizeOf(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "HEAD";
                req.Timeout = 30000;
                using (var resp = (HttpWebResponse)req.GetResponse()) return resp.ContentLength;
            }
            catch { return -1; }
        }

        // ------------------------------------------------------------------
        //  Скачивание с продолжением
        // ------------------------------------------------------------------

        /// <summary>Качает пакет, докачивая начатое. report(доля 0..1, поясняющая строка).</summary>
        public static bool Download(string url, string target, Action<double, string> report,
                                    ManualResetEvent cancel, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                long have = File.Exists(target) ? new FileInfo(target).Length : 0;
                long total = SizeOf(url);
                if (total > 0 && have == total) { report(1, "пакет уже скачан"); return true; }

                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 60000;
                req.ReadWriteTimeout = 120000;
                if (have > 0) req.AddRange(have);

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (Stream src = resp.GetResponseStream())
                using (var dst = new FileStream(target, have > 0 ? FileMode.Append : FileMode.Create,
                                                FileAccess.Write, FileShare.None, 1 << 16))
                {
                    if (total <= 0) total = have + resp.ContentLength;
                    byte[] buf = new byte[1 << 16];
                    long done = have;
                    int lastPercent = -1;
                    int read;
                    while ((read = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (cancel != null && cancel.WaitOne(0)) { error = "отменено"; return false; }
                        dst.Write(buf, 0, read);
                        done += read;
                        int percent = total > 0 ? (int)(done * 100 / total) : 0;
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            report(total > 0 ? (double)done / total : 0,
                                   "скачано " + Mb(done) + " из " + Mb(total));
                        }
                    }
                }
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        private static string Mb(long bytes)
        {
            if (bytes <= 0) return "?";
            return (bytes / 1024.0 / 1024.0).ToString("F0") + " МБ";
        }

        // ------------------------------------------------------------------
        //  Распаковка
        // ------------------------------------------------------------------

        /// <summary>
        /// Разворачивает вложенные слои, пока не найдётся нужный inf. Слоёв немного:
        /// xar → Payload → cpio → образ dmg → папка BootCamp.
        /// </summary>
        public static string ExtractUntilInf(string sevenZip, string package, string workFolder,
                                             Action<double, string> report, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(workFolder);
                var queue = new List<string>();
                queue.Add(package);

                for (int level = 0; level < 6 && queue.Count > 0; level++)
                {
                    var next = new List<string>();
                    foreach (string archive in queue)
                    {
                        string outDir = Path.Combine(workFolder, "l" + level + "_" +
                            Path.GetFileNameWithoutExtension(archive));
                        report(0, "распаковка: " + Path.GetFileName(archive));
                        if (!Run7z(sevenZip, "x -y -o\"" + outDir + "\" \"" + archive + "\"", out error)) continue;

                        string inf = FindInf(outDir);
                        if (inf != null) return inf;

                        foreach (string f in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories))
                        {
                            string name = Path.GetFileName(f).ToLowerInvariant();
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            if (name == "payload" || name == "payload~" || ext == ".dmg" ||
                                ext == ".pkg" || ext == ".cpio" || ext == ".gz" || ext == ".hfs")
                                next.Add(f);
                        }
                    }
                    queue = next;
                }

                if (error == null) error = "внутри пакета не нашлось файла драйвера клавиатуры";
                return null;
            }
            catch (Exception e) { error = e.Message; return null; }
        }

        private static bool Run7z(string sevenZip, string args, out string error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo(sevenZip, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode > 1) { error = "7-Zip вернул " + p.ExitCode + ": " + se + so; return false; }
                    return true;
                }
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        /// <summary>Ищет файл драйвера клавиатуры в распакованном дереве.</summary>
        public static string FindInf(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return null;
                string[] all = Directory.GetFiles(root, "*.inf", SearchOption.AllDirectories);
                foreach (string wanted in WantedInf)
                    foreach (string f in all)
                        if (String.Equals(Path.GetFileName(f), wanted, StringComparison.OrdinalIgnoreCase))
                            return f;
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------------
        //  Установка
        // ------------------------------------------------------------------

        /// <summary>Ставит драйвер через pnputil. Требует прав администратора — их спросит Windows.</summary>
        public static bool Install(string inf, out string output)
        {
            return Pnputil("/add-driver \"" + inf + "\" /install", out output);
        }

        public static bool Uninstall(string infName, out string output)
        {
            return Pnputil("/delete-driver \"" + infName + "\" /uninstall /force", out output);
        }

        private static bool Pnputil(string args, out string output)
        {
            output = null;
            string log = Path.Combine(Path.GetTempPath(), "magickeys-pnputil.txt");
            try { if (File.Exists(log)) File.Delete(log); } catch { }

            try
            {
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c pnputil " + args + " > \"" + log + "\" 2>&1");
                psi.UseShellExecute = true;      // нужно для запроса прав
                psi.Verb = "runas";
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(120000);
                    try { if (File.Exists(log)) output = File.ReadAllText(log, Encoding.Default); } catch { }
                    return p.ExitCode == 0;
                }
            }
            catch (Exception e)
            {
                output = e.Message;   // отказ в правах приходит сюда же
                return false;
            }
        }
    }
}
