// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Runtime.InteropServices;
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

        /// <summary>
        /// Одноразовые файлы прежних версий: сюда через cmd писал свой вывод pnputil.
        /// Больше сюда не пишет никто — папка осталась только затем, чтобы «очистить
        /// загруженное» убрало её у тех, у кого она уже есть.
        /// </summary>
        private static string RunFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MagicKeys\\run");
            }
        }

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
                Directory.CreateDirectory(CacheFolder);
                // В своей папке, а не в %TEMP%: там имя общеизвестно и подложить туда
                // чужой установщик может любая программа этого же пользователя.
                string msi = Path.Combine(CacheFolder, "7zip.msi");
                report(0, "скачиваю 7-Zip…");
                // Не держимся сайта: у пакета закреплена сумма, и она строже любого
                // требования к адресу.
                if (!Download(SevenZipMsi, msi, report, null, false, out error)) return null;

                // Перед msiexec сверяем сумму: административная установка исполняет
                // последовательность действий из самого пакета, то есть чужой MSI здесь
                // не данные, а программа.
                //
                // Суммой, а не подписью. ЗАМЕРЕНО: официальный 7z2501-x64.msi подписи
                // Authenticode не имеет вовсе — ни потока DigitalSignature, ни цепочки;
                // автор 7-Zip не подписывает свои сборки принципиально. Требование
                // подписи здесь означало отказ ВСЕГДА: добыча 7-Zip не могла сработать
                // ни разу, а человек читал «установщик 7-Zip не подписан — запускать
                // его нельзя» и оставался без драйвера. Контрольный опыт при замере
                // тоже сделан: тот же вызов на собственном пакете MagicKeys подпись
                // читает, так что дело не в средстве.
                //
                // Закреплённая сумма при этом не слабее: она называет ровно один файл,
                // а не «кого-то, кому доверяет эта машина». Меняется вместе со ссылкой
                // на установщик — и только вместе с ней.
                //
                // Держим файл открытым от проверки до самого msiexec: иначе между
                // «сумма сошлась» и «пакет запущен» лежит окно, в которое подставляется
                // другой файл. Папка своя, но писать в неё может любой процесс этого же
                // пользователя — а проверка, которую можно обойти подменой, не проверка.
                using (FileStream keep = Open(msi))
                {
                    if (keep == null)
                    {
                        error = "скачанный установщик 7-Zip занят другой программой";
                        return null;
                    }

                string now = HashOf(keep);
                if (!String.Equals(now, SevenZipMsiSha256, StringComparison.OrdinalIgnoreCase))
                {
                    Diag.Log("установщик 7-Zip не тот, что закреплён: " + now);
                    error = "скачанный установщик 7-Zip не совпал с закреплённой суммой — " +
                            "запускать его нельзя";
                    try { File.Delete(msi); } catch { }
                    try { File.Delete(StatePath(msi)); } catch { }
                    return null;
                }
                report(0.85, "сумма установщика сошлась");

                string target = Path.Combine(ToolsFolder, "7zip");
                report(0.9, "разворачиваю 7-Zip…");
                var psi = new ProcessStartInfo(
                    Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                    "/a \"" + msi + "\" TARGETDIR=\"" + target + "\" /qn");
                psi.WorkingDirectory = Environment.SystemDirectory;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(180000);
                    if (!p.HasExited) { error = "разворачивание 7-Zip не уложилось в срок"; return null; }
                    if (p.ExitCode != 0)
                    {
                        Diag.Log("7-Zip: msiexec вернул " + p.ExitCode);
                        error = "не удалось развернуть 7-Zip из установщика";
                        return null;
                    }
                }

                }   // отпускаем пакет: он уже развёрнут

                try { File.Delete(msi); } catch { }
                try { File.Delete(StatePath(msi)); } catch { }
                string found = SevenZip();
                if (found == null) { error = "7-Zip развернулся, но 7z.exe не нашёлся"; return null; }

                return found;
            }
            catch (Exception e) { error = Why("не удалось развернуть 7-Zip", e); return null; }
        }

        /// <summary>
        /// Назвать беду по-русски и своими словами, а подробность оставить в журнале.
        ///
        /// Текст исключения .NET приходит на языке системы библиотек, то есть
        /// по-английски, и говорит про свои понятия: человек читал «The remote server
        /// returned an error: (404) Not Found» там, где ему нужно «каталог Apple
        /// прочитать не вышло».
        /// </summary>
        private static string Why(string what, Exception e)
        {
            Diag.Log(what, e);
            return what + ".";
        }

        /// <summary>Открыть файл на чтение, запретив запись всем остальным.</summary>
        private static FileStream Open(string path)
        {
            try { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch { return null; }
        }

        /// <summary>Сколько места занято скачанным и распакованным.</summary>
        public static long CacheSize()
        {
            long total = 0;
            foreach (string dir in new string[] { CacheFolder, ToolsFolder })
                total += FolderSize(dir);
            return total;
        }

        private static long FolderSize(string dir)
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(dir)) return 0;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    try { total += new FileInfo(f).Length; } catch { }
            }
            catch { }
            return total;
        }

        /// <summary>
        /// Убрать скачанное и распакованное. Пакет весит около 700 МБ, а распакованное —
        /// ещё несколько гигабайт слоями; раньше всё это оставалось на диске навсегда.
        /// Удаляются только свои папки, на уже установленный драйвер это не влияет.
        /// </summary>
        public static bool ClearCache(out string error)
        {
            error = null;
            bool ok = true;
            foreach (string dir in new string[] { CacheFolder, ToolsFolder, RunFolder })
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch (Exception e) { ok = false; error = Why("не удалось убрать скачанное", e); }
            }
            return ok;
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

            // PATH здесь не спрашиваем. У обычного пользователя в нём есть каталоги,
            // доступные ему на запись, а именно 7z.exe решает, какое дерево окажется
            // распаковано и какой .inf уйдёт в pnputil с правами администратора. Это
            // единственный внешний исполняемый файл в цепочке, не проверяемый ничем,
            // — а своя копия и запись в реестре стоят выше, так что отказ от PATH
            // почти ничего не стоит.

            foreach (string t in tries)
                try { if (File.Exists(t) && Usable(t)) return t; } catch { }
            return null;
        }

        /// <summary>
        /// Годится ли эта копия — та же проверка, что и перед самим запуском.
        ///
        /// Спрашиваем уже здесь. Иначе копия, проверку не проходящая, занимала место
        /// ответа навсегда: 7-Zip, поставленный не в Program Files (скажем, на D:),
        /// попадал сюда через ветку HKLM, FetchSevenZip возвращал его, не скачивая
        /// свою копию, а запуск отказывал — и выхода не было ни одного. Совет «уберите
        /// скачанное и повторите» чистит свою папку, а мешала чужая.
        /// </summary>
        private static bool Usable(string path)
        {
            try
            {
                string dll = Path.Combine(Path.GetDirectoryName(path), "7z.dll");
                using (FileStream keep = Open(path))
                using (FileStream keepDll = Open(dll))
                {
                    if (keep == null) return false;
                    string error;
                    return TrustedSevenZip(path, keep, keepDll, out error);
                }
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        //  Каталог Apple
        // ------------------------------------------------------------------

        /// <summary>
        /// Включает TLS 1.2, ничего не выключая. Раньше протокол присваивался, а это
        /// настройка всего процесса: заодно отключался и более новый TLS 1.3.
        /// </summary>
        private static void EnsureTls()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }
            catch { }
        }

        /// <summary>Самый свежий пакет Boot Camp: ссылка и дата выпуска.</summary>
        public static bool FindNewestPackage(out string url, out string posted, out string error)
        {
            url = null; posted = null; error = null;
            try
            {
                EnsureTls();
                string xml;
                // Тем же путём, что и сам пакет: перенаправления идём своими руками
                // и каждое проверяем на https. Именно ответ каталога решает, что будет
                // скачано, распаковано и отдано pnputil с правами администратора, —
                // проверять его слабее, чем сам пакет, значит запирать вторую дверь
                // при открытой первой.
                string catalogError;
                using (HttpWebResponse resp = OpenHttps(Catalog, "GET", 0, true, out catalogError))
                {
                    if (resp == null) { error = catalogError; return false; }
                    using (var reader = new StreamReader(resp.GetResponseStream()))
                        xml = reader.ReadToEnd();
                }

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
                // И хост тоже Apple. Выражение выше принимает любой https-адрес, а качаем
                // мы по нему семьсот мегабайт, распаковываем и предлагаем поставить.
                if (!AppleHost(bestUrl))
                {
                    error = "каталог Apple указал на чужой сервер — качать оттуда не буду";
                    return false;
                }
                url = bestUrl; posted = bestDate;
                return true;
            }
            catch (Exception e) { error = Why("каталог Apple прочитать не вышло", e); return false; }
        }

        /// <summary>
        /// Тот же сайт, что и вначале: совпадают две последние части имени. Строже
        /// нельзя — каталог Apple живёт на swscan.apple.com, а пакеты раздаёт
        /// swcdn.apple.com, и это законный переход.
        /// </summary>
        private static bool SameSite(Uri a, Uri b)
        {
            return Site(a) == Site(b) && Site(a).Length > 0;
        }

        private static readonly char[] Dot = { (char)46 };

        private static string Site(Uri u)
        {
            try
            {
                string[] parts = u.Host.ToLowerInvariant().Split(Dot);
                if (parts.Length < 2) return u.Host.ToLowerInvariant();
                return parts[parts.Length - 2] + "." + parts[parts.Length - 1];
            }
            catch { return ""; }
        }

        /// <summary>
        /// Ведёт ли ссылка на серверы Apple. Список каталога подписью не защищён,
        /// и адрес в нём — такие же данные, как всё остальное.
        /// </summary>
        private static bool AppleHost(string url)
        {
            try
            {
                Uri u = new Uri(url);
                if (u.Scheme != Uri.UriSchemeHttps) return false;
                string host = u.Host.ToLowerInvariant();
                return host == "apple.com" || host.EndsWith(".apple.com");
            }
            catch { return false; }
        }

        public static long SizeOf(string url) { return SizeOf(url, true); }

        private static long SizeOf(string url, bool sameSite)
        {
            try
            {
                string error;
                using (HttpWebResponse resp = OpenHttps(url, "HEAD", 0, sameSite, out error))
                    return resp == null ? -1 : resp.ContentLength;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Открыть ответ, идя по перенаправлениям своими руками.
        ///
        /// Своими — потому что каждый переход надо проверить на https: HttpWebRequest
        /// молча уходит с https на http, а скачанное отсюда попадает потом в pnputil
        /// с правами администратора. То же требование уже выполнено в Updater.Grab —
        /// здесь оно ничем не слабее.
        /// </summary>
        private static HttpWebResponse OpenHttps(string url, string method, long from,
                                                 bool sameSite, out string error)
        {
            error = null;
            EnsureTls();
            Uri at;
            if (!Uri.TryCreate(url, UriKind.Absolute, out at)) { error = "ссылка не разбирается"; return null; }
            Uri start = at;
            for (int hop = 0; hop < 5; hop++)
            {
                if (at.Scheme != Uri.UriSchemeHttps)
                {
                    error = "загрузка увела с защищённого соединения";
                    return null;
                }
                // И с сайта не увела. Проверка хоста на первой ссылке ничего не стоит,
                // если переход уводит куда угодно, — а сюда попадает пакет Apple, который
                // потом уйдёт в pnputil с правами администратора и не проверяется больше
                // ничем, кроме подписи драйвера. Для установщика 7-Zip требование мягче:
                // у него закреплена сумма, и она называет ровно один файл — куда бы
                // переход ни увёл, чужой пакет её не пройдёт.
                if (sameSite && !SameSite(start, at))
                {
                    error = "загрузка увела на другой сайт: " + at.Host;
                    return null;
                }
                var req = (HttpWebRequest)WebRequest.Create(at);
                req.Method = method;
                req.AllowAutoRedirect = false;
                req.Timeout = 60000;
                req.ReadWriteTimeout = 120000;
                if (from > 0) req.AddRange(from);
                var resp = (HttpWebResponse)req.GetResponse();
                int code = (int)resp.StatusCode;
                if (code < 300 || code >= 400) return resp;
                string next = resp.Headers["Location"];
                resp.Close();
                if (String.IsNullOrEmpty(next) || !Uri.TryCreate(at, next, out at))
                { error = "сервер отправил в никуда"; return null; }
            }
            error = "сервер водит по кругу";
            return null;
        }

        // ------------------------------------------------------------------
        //  Скачивание с продолжением: чему верить в уже скачанном
        // ------------------------------------------------------------------
        //
        // Совпадения длины мало. Файл лежит там, куда пишет любая программа, запущенная
        // от этого же пользователя, а дальше уходит в msiexec и в установку драйвера
        // с правами администратора. Подпись драйвера Windows проверит сама — это
        // последний заслон, но единственным ему быть не следует. Поэтому рядом с файлом
        // лежит запись: откуда качали, сколько вышло и какова сумма SHA-256. Кэшу верим,
        // только если сумма сходится заново.

        private static string StatePath(string target) { return target + ".state"; }

        private static void WriteState(string target, string url, long length, string sha)
        {
            try
            {
                File.WriteAllText(StatePath(target),
                    url + "\n" + length + "\n" + (sha == null ? "" : sha), Encoding.UTF8);
            }
            catch { }
        }

        private static bool ReadState(string target, out string url, out long length, out string sha)
        {
            url = null; length = 0; sha = null;
            try
            {
                string p = StatePath(target);
                if (!File.Exists(p) || IsLink(p)) return false;
                string[] lines = File.ReadAllText(p, Encoding.UTF8).Split(new char[] { '\n' });
                if (lines.Length < 3) return false;
                url = lines[0].Trim();
                if (!Int64.TryParse(lines[1].Trim(), out length)) return false;
                sha = lines[2].Trim();
                return true;
            }
            catch { return false; }
        }

        private static string Sha256Of(string path, Action<double, string> report)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                byte[] buf = new byte[1 << 20];
                long done = 0, total = f.Length;
                int read, lastPercent = -1;
                while ((read = f.Read(buf, 0, buf.Length)) > 0)
                {
                    sha.TransformBlock(buf, 0, read, null, 0);
                    done += read;
                    int percent = total > 0 ? (int)(done * 100 / total) : 0;
                    if (report != null && percent != lastPercent)
                    {
                        lastPercent = percent;
                        report(total > 0 ? (double)done / total : 0, "проверяю уже скачанное…");
                    }
                }
                sha.TransformFinalBlock(buf, 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "");
            }
        }

        /// <summary>
        /// Скачать пакет Apple: не сходя с https и не сходя с сайта Apple. После этого
        /// файла проверять нечем — он уйдёт в pnputil с правами администратора, и
        /// последним заслоном останется подпись драйвера.
        /// </summary>
        public static bool DownloadFromApple(string url, string target, Action<double, string> report,
                                            ManualResetEvent cancel, out string error)
        {
            if (!AppleHost(url))
            {
                error = "ссылка на пакет ведёт не к Apple — качать оттуда не буду";
                return false;
            }
            return Download(url, target, report, cancel, true, out error);
        }

        /// <param name="sameSite">
        /// Держаться ли того же сайта на перенаправлениях. Для пакета Apple — да:
        /// после него проверять нечем. Для установщика 7-Zip — нет: у него закреплена
        /// сумма, и она строже любого требования к адресу.
        /// </param>
        /// <summary>Качает пакет, докачивая начатое. report(доля 0..1, поясняющая строка).</summary>
        private static bool Download(string url, string target, Action<double, string> report,
                                    ManualResetEvent cancel, bool sameSite, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));

                string sUrl, sSha; long sLen;
                bool state = ReadState(target, out sUrl, out sLen, out sSha);
                // Точку повторного разбора убираем, а не пишем сквозь неё: FileMode.Create
                // открыл бы и обрезал цель ссылки — чужой файл в чужом каталоге.
                if (File.Exists(target) && IsLink(target))
                {
                    Diag.Log("в кэше вместо файла точка повторного разбора — убираю");
                    try { File.Delete(target); } catch { }
                    // Не вышло — отказываемся. Открыть по тому же пути FileMode.Create
                    // значит обрезать цель ссылки, то есть чужой файл в чужом каталоге.
                    if (File.Exists(target) && IsLink(target))
                    {
                        error = "в кэше вместо файла ссылка, и убрать её не вышло";
                        return false;
                    }
                }
                long have = File.Exists(target) && !IsLink(target) ? new FileInfo(target).Length : 0;
                // Тем же правилом, что и сама закачка: иначе для 7-Zip длина не узнавалась
                // бы вовсе, а без неё молча отключаются и докачка, и проверка целостности.
                long total = SizeOf(url, sameSite);

                // Целое, от того же адреса и с сошедшейся суммой — только тогда кэш.
                if (have > 0 && state && sUrl == url && sLen == have
                    && !String.IsNullOrEmpty(sSha) && (total <= 0 || total == have))
                {
                    string now = null;
                    try { now = Sha256Of(target, report); } catch { }
                    if (now == sSha) { report(1, "пакет уже скачан"); return true; }
                    report(0, "скачанное не сходится с записью — качаю заново");
                    try { File.Delete(target); } catch { }
                    have = 0;
                }

                // Докачивать можно только начатое от того же адреса и той же длины. Иначе
                // к обрывку старой версии допишется новая: выйдет испорченный архив,
                // который не распакуется, а причину без ручной чистки кэша не найти.
                // И только НЕДОкачанное. Файл, скачанный целиком, но не успевший
                // получить сумму — выход из программы приходится ровно на её счёт,
                // а это секунды на семистах мегабайтах, — кэшем не признаётся, а без
                // этого условия и не удалялся: у сервера просили диапазон, начинающийся
                // на длине файла. Ответ 416, и человек читал «скачать не вышло» при
                // каждом повторе, пока не догадается очистить кэш руками.
                if (have > 0 && !(state && sUrl == url && total > 0 && sLen == total && have < total))
                {
                    try { File.Delete(target); } catch { }
                    have = 0;
                }
                if (total > 0) WriteState(target, url, total, null);

                HttpWebResponse opened = OpenHttps(url, "GET", have, sameSite, out error);
                if (opened == null) return false;

                using (HttpWebResponse resp = opened)
                using (Stream src = resp.GetResponseStream())
                {
                    // Докачивать можно, только если сервер на диапазон согласился. Он
                    // вправе прислать файл целиком (ответ 200), и тогда дописывание
                    // к обрывку дало бы мусор — который мы тут же заверили бы собственной
                    // суммой, то есть узаконили порчу, от которой сумма и заводилась.
                    bool append = have > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
                    if (have > 0 && !append)
                    {
                        report(0, "сервер прислал файл целиком — качаю заново");
                        have = 0;
                    }

                using (var dst = new FileStream(target, append ? FileMode.Append : FileMode.Create,
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
                }

                // Оборванная закачка — не удача. Без этой проверки обрыв на середине
                // объявлялся успехом, распаковка потом не находила драйвера, и человек
                // читал «7-Zip не смог распаковать пакет» — то есть вину переложили
                // на Apple. Хуже того, состоянию с чужой длиной верили, и докачка
                // отключалась: следующий раз качал семьсот мегабайт заново.
                long got = new FileInfo(target).Length;
                if (total > 0 && got != total)
                {
                    error = "связь оборвалась: скачано " + Mb(got) + " из " + Mb(total);
                    return false;
                }

                // Сумму записываем только теперь, когда файл целиком наш.
                report(1, "проверяю скачанное…");
                try { WriteState(target, url, got, Sha256Of(target, null)); }
                catch { }
                return true;
            }
            catch (Exception e) { error = Why("скачать пакет не вышло", e); return false; }
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
                        if (!Run7z(sevenZip, "x -y -o\"" + outDir + "\" -- \"" + archive + "\"", out error)) continue;

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
            catch (Exception e) { error = Why("распаковать пакет не вышло", e); return null; }
        }

        private static bool Run7z(string sevenZip, string args, out string error)
        {
            error = null;

            // Проверяем перед КАЖДЫМ запуском — суммой. Замерено: подписи Authenticode
            // нет ни у 7z.exe, ни у самого установщика: автор 7-Zip не подписывает свои
            // сборки принципиально. Требовать её значило бы отказывать всегда и убить
            // добычу драйвера целиком.
            //
            // Своя копия лежит в папке пользователя, писать в неё может любой его процесс,
            // и подложенный туда файл обходил проверку скачанного: FetchSevenZip до неё
            // просто не доходил. Поэтому у своей копии запомнена сумма — та, что была
            // при распаковке из установщика с проверенной подписью. Копию из Program Files
            // или из ветки HKLM так не проверяем: туда без прав администратора не записать,
            // а если они есть, защищать уже нечего.
            // Держим открытыми оба: и фасад, и библиотеку. Пока они открыты без права
            // записи, подменить их между проверкой и запуском нельзя.
            string dll = Path.Combine(Path.GetDirectoryName(sevenZip), "7z.dll");
            using (FileStream keep = Open(sevenZip))
            using (FileStream keepDll = Open(dll))
            {
                if (keep == null)
                {
                    error = "7-Zip занят другой программой";
                    return false;
                }
                // «Занята» и «её нет» — разные беды и разные советы. Прежде оба случая
                // сливались в «рядом с 7z.exe нет 7z.dll», и человека отправляли искать
                // библиотеку, которая лежит на месте.
                if (keepDll == null && File.Exists(dll))
                {
                    error = "библиотека 7z.dll занята другой программой";
                    return false;
                }
                if (!TrustedSevenZip(sevenZip, keep, keepDll, out error)) return false;
                return Run7zHeld(sevenZip, args, out error);
            }
        }

        /// <summary>
        /// Сумма самого 7z2501-x64.msi — того, что скачивает FetchSevenZip.
        ///
        /// Замерено на файле, скачанном этой же программой; из него развернулись
        /// ровно те 7z.exe и 7z.dll, чьи суммы закреплены ниже, — то есть цепочка
        /// «пакет → двоичные файлы» сходится сама с собой.
        ///
        /// Меняется вместе со ссылкой на установщик — и только вместе с ней.
        /// </summary>
        private const string SevenZipMsiSha256 =
            "e7eb0b7ed5efa4e087b7b17f191797f7af5b7f442d1290c66f3a21777005ef57";

        /// <summary>
        /// Сумма 7z.exe из официального 7z2501-x64.msi — того самого, что скачивает
        /// FetchSevenZip, и того же разряда.
        ///
        /// Закреплена здесь, а не в файле рядом со своей копией. Рядом она ничего
        /// не стоила: противник, о котором идёт речь, — процесс этого же пользователя,
        /// и папку он правит целиком, вместе с записью о ней. А программа лежит
        /// в Program Files, куда без прав администратора не записать; если они есть,
        /// защищать уже нечего.
        ///
        /// Меняется вместе со ссылкой на установщик — и только вместе с ней.
        /// </summary>
        private const string SevenZipSha256 =
            "4cd7d776c686427226a151789d2d61f0b2ed2c392148cc4e69c0238362fafecf";

        /// <summary>
        /// И сумма 7z.dll — из того же установщика. Сам 7z.exe весит 575 КБ и без этой
        /// библиотеки не распаковывает ничего: все обработчики форматов в ней. Закрепить
        /// один фасад значило закрыть проверку наполовину — подменивший библиотеку
        /// получал исполнение внутри процесса, который выбирает .inf для установки
        /// драйвера с правами администратора.
        /// </summary>
        private const string SevenZipDllSha256 =
            "5bd20fb38499d95c39594f41d4781b6181b3304b7f1f4d06b0182f514e7eaa74";

        /// <summary>
        /// Можно ли верить этому 7z.exe. Файл уже открыт без права записи, и сумма
        /// считается по открытому — подменить его между проверкой и запуском нельзя.
        /// </summary>
        private static bool TrustedSevenZip(string path, FileStream open, FileStream dll, out string error)
        {
            error = null;

            // Библиотека нужна любой копии: 7z.exe весит полмегабайта, а все обработчики
            // форматов лежат в 7z.dll. Спрашиваем до послабления для Program Files —
            // иначе человек читал «7-Zip не смог распаковать пакет», то есть вину
            // перекладывали на Apple, вместо готового и верного объяснения.
            if (dll == null)
            {
                error = "рядом с 7z.exe нет 7z.dll — распаковывать нечем";
                return false;
            }

            if (WrittenOnlyByAdmin(path)) return true;

            string now = HashOf(open);
            if (!String.Equals(now, SevenZipSha256, StringComparison.OrdinalIgnoreCase))
            {
                Diag.Log("7z.exe не тот, что кладёт официальный установщик: " + now);
                error = "7-Zip не тот, что кладёт официальный установщик — уберите скачанное и повторите";
                return false;
            }
            string nowDll = HashOf(dll);
            if (!String.Equals(nowDll, SevenZipDllSha256, StringComparison.OrdinalIgnoreCase))
            {
                Diag.Log("7z.dll не та, что кладёт официальный установщик: " + nowDll);
                error = "7z.dll не та, что кладёт официальный установщик — уберите скачанное и повторите";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Лежит ли файл там, куда без прав администратора не записать.
        ///
        /// Судим по месту, а не по происхождению пути. Прежнее правило считало доверенным
        /// всё, что не в своей папке, — в том числе путь из ветки HKLM, а его записал
        /// установщик 7-Zip, и указывать он может куда угодно, включая папку профиля,
        /// открытую на запись любому процессу этого пользователя.
        /// </summary>
        private static bool WrittenOnlyByAdmin(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                // Только Program Files. Внутри C:\Windows есть каталоги, куда обычный
                // пользователь пишет штатно (Temp, Tracing), — то есть послабление там
                // шире правды. 7-Zip в C:\Windows не ставится никогда, и эта папка
                // в списке была лишней.
                foreach (Environment.SpecialFolder f in new[]
                {
                    Environment.SpecialFolder.ProgramFiles,
                    Environment.SpecialFolder.ProgramFilesX86
                })
                {
                    string root = Environment.GetFolderPath(f);
                    if (String.IsNullOrEmpty(root)) continue;
                    if (!root.EndsWith("\\")) root += "\\";
                    if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch (Exception e) { Diag.Log("не удалось выяснить, где лежит 7-Zip", e); }
            return false;
        }

        /// <summary>Сумма уже открытого файла — без второго открытия и без окна для подмены.</summary>
        private static string HashOf(FileStream open)
        {
            open.Position = 0;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(open);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static bool Run7zHeld(string sevenZip, string args, out string error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo(sevenZip, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(sevenZip);
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    // Один поток читаем по событию, второй — целиком. Читать оба подряд
                    // нельзя: пока мы стоим на выводе, 7-Zip заполняет буфер ошибок
                    // (несколько килобайт) и встаёт — а он сыплет по строке на файл,
                    // когда архив чужой или битый. Распаковка висла навсегда, страница
                    // оставалась на «Распаковываю…», и повторить было нельзя до
                    // перезапуска программы.
                    var errText = new StringBuilder();
                    p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) lock (errText) errText.AppendLine(e.Data);
                    };
                    p.BeginErrorReadLine();
                    string so = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(600000))
                    {
                        try { p.Kill(); } catch { }
                        error = "распаковка не уложилась в срок";
                        return false;
                    }
                    if (p.ExitCode > 1)
                    {
                        string se;
                        lock (errText) se = errText.ToString();
                        Diag.Log("7-Zip вернул " + p.ExitCode + ": " + se + so);
                        error = "7-Zip не смог распаковать пакет";
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception e) { error = Why("7-Zip запустить не вышло", e); return false; }
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

        /// <summary>
        /// Ставит драйвер через pnputil. Требует прав администратора — их спросит Windows.
        ///
        /// Файл .inf держим открытым без права записи на всё время установки: он лежит
        /// там, куда 7-Zip распаковал архив Apple, а это папка текущего пользователя,
        /// и писать в неё может любой его процесс — пока человек смотрит на запрос прав.
        /// Соседние файлы драйвера так не удержать, и последним заслоном остаётся проверка
        /// подписи самой Windows: неподписанный .sys в ядро не встанет. Заслон настоящий,
        /// но единственным ему быть не следует, а этот — наш — стоит одной строки.
        /// </summary>
        public static bool Install(string inf, out string output)
        {
            using (FileStream keep = Open(inf))
            {
                if (keep == null)
                {
                    // Без удержания заслон исчезал молча — а он здесь единственный наш.
                    output = "Файл драйвера занят другой программой.";
                    return false;
                }
                // Держим мы разрешённый файл, а pnputil получает строку и разбирает путь
                // заново — уже с правами администратора. Между этими двумя разборами любой
                // процесс этого же пользователя может подставить соединение (junction прав
                // администратора не требует) и увести установку на чужой подписанный пакет.
                // Точек повторного разбора на пути быть не должно.
                if (PathHasLink(inf))
                {
                    output = "На пути к файлу драйвера есть точка повторного разбора — установку не начинаю.";
                    return false;
                }
                return Pnputil("/add-driver \"" + inf + "\" /install", false, out output);
            }
        }

        /// <summary>
        /// Убирает драйвер из хранилища Windows.
        ///
        /// Имя приходится искать: pnputil ждёт то, под которым драйвер опубликован
        /// (oem12.inf и подобные), а не исходное имя из пакета. С исходным он отвечает
        /// «такого драйвера нет» — и человек читал это при установленном драйвере.
        /// </summary>
        public static bool Uninstall(string infName, out string output)
        {
            // Имя приходит одно, а поставить программа могла любое из четырёх: какое
            // нашлось в пакете, то и поставили. Ищем по всем — иначе человек с алюминиевой
            // клавиатурой читал «не нашлось, под каким именем драйвер лежит в хранилище»
            // при драйвере, который программа сама и поставила.
            string published = PublishedInf(infName);
            if (published == null)
                foreach (string other in WantedInf)
                {
                    if (String.Equals(other, infName, StringComparison.OrdinalIgnoreCase)) continue;
                    published = PublishedInf(other);
                    if (published != null) break;
                }
            if (published == null)
            {
                output = "Не нашлось, под каким именем драйвер лежит в хранилище Windows. "
                       + "Его можно убрать через «Диспетчер устройств».";
                return false;
            }
            return Pnputil("/delete-driver \"" + published + "\" /uninstall /force", true, out output);
        }

        /// <summary>
        /// Под каким именем драйвер опубликован. Перебором по %WINDIR%\INF\oem*.inf:
        /// в опубликованной копии остаётся имя из исходного файла, по нему и узнаём.
        /// Читать эти файлы прав администратора не требует.
        /// </summary>
        public static string PublishedInf(string originalInfName)
        {
            string mark = Path.GetFileNameWithoutExtension(originalInfName);
            if (String.IsNullOrEmpty(mark)) return null;
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
                foreach (string f in Directory.GetFiles(dir, "oem*.inf"))
                {
                    try
                    {
                        // Кодировка у этих файлов разная: часть в UTF-16 с меткой,
                        // часть в однобайтовой. StreamReader распознаёт метку сам.
                        string text;
                        using (var sr = new StreamReader(f, Encoding.Default, true))
                            text = sr.ReadToEnd();
                        // С точкой: голое «keymagic» совпадает и внутри «KeyMagic2.cat»,
                        // и перебор по WantedInf сносил не тот драйвер семейства.
                        if (text.IndexOf(mark + ".", StringComparison.OrdinalIgnoreCase) >= 0)
                            return Path.GetFileName(f);
                    }
                    catch { }
                }
            }
            catch (Exception e) { Diag.Log("не удалось найти опубликованное имя драйвера", e); }
            return null;
        }

        /// <summary>
        /// Запустить pnputil с правами администратора. Напрямую, без cmd.
        ///
        /// Раньше между нами и pnputil стоял cmd — только затем, чтобы перенаправлением
        /// «>» забрать вывод в файл. Обходилось это дорого. Cmd раскрывает %ПЕРЕМЕННЫЕ%
        /// в командной строке, и внутри кавычек тоже, — а путь к .inf выбирает человек
        /// или распаковывает архив Apple в папку, доступную на запись кому угодно того же
        /// пользователя. Папки «скидка 50%» хватало, чтобы установка молча пошла не туда
        /// и человек прочитал «запрос прав администратора отклонён». А тот, кто заведёт
        /// себе переменную окружения нужного содержания и подложит каталог с её именем,
        /// получал исполнение своей команды с правами администратора — то есть ровно тот
        /// переход, ради которого всё это и закрывали. CreateProcess не раскрывает ничего.
        ///
        /// Вывод забрать теперь нечем: перенаправление требует cmd, а поток напрямую —
        /// UseShellExecute, без которого нет запроса прав. Взамен отвечаем сами, по коду
        /// возврата. Человеку от этого только лучше: pnputil говорит с ним кодами вида
        /// 0xE0000247, да ещё и в кодировке консоли, которую мы читали как системную —
        /// то есть кириллица доходила мусором.
        /// </summary>
        /// <param name="removing">
        /// Убираем драйвер, а не ставим. Ответы у pnputil общие на оба случая, а слова
        /// к ним нужны разные: после удачного удаления «до этого драйвер не заработает»
        /// читалось как прямая противоположность правде, а «удалять нечего» приходило
        /// на путь установки, где удалять никто не просил.
        /// </param>
        private static bool Pnputil(string args, bool removing, out string output)
        {
            output = null;
            try
            {
                string sys = Environment.SystemDirectory;
                var psi = new ProcessStartInfo(Path.Combine(sys, "pnputil.exe"), args);
                psi.WorkingDirectory = sys;
                psi.UseShellExecute = true;      // нужно для запроса прав
                psi.Verb = "runas";
                // Окно прячет только это: CreateNoWindow при UseShellExecute не значит
                // ничего — так описано в .NET, и так оно и есть.
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process started = Process.Start(psi);
                if (started == null)
                {
                    output = "Запрос прав администратора отклонён.";
                    return false;
                }
                using (Process p = started)
                {
                    if (!p.WaitForExit(300000))
                    {
                        // Не убиваем: установщик драйверов на медленной машине бывает
                        // долгим, а прерванная установка хуже долгой. Но и успехом
                        // не называем — говорим то, что есть.
                        output = (removing ? "Удаление идёт" : "Установка идёт")
                               + " дольше обычного. Оно продолжается само; "
                               + "загляните сюда позже.";
                        return false;
                    }
                    return Verdict(p.ExitCode, removing, out output);
                }
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                // Отказ от повышения прав — это 1223, и только он. Раньше сюда сводили
                // всё подряд, и человек читал «запрос прав отклонён» при любой поломке,
                // уверенный, что в системе ничего не изменилось.
                Diag.Log("pnputil не запустился, код " + e.NativeErrorCode, e);
                output = e.NativeErrorCode == 1223
                    ? "Запрос прав администратора отклонён."
                    : "Не удалось запустить установщик драйверов Windows (код " + e.NativeErrorCode + ").";
                return false;
            }
            catch (Exception e)
            {
                Diag.Log("pnputil не запустился", e);
                output = "Не удалось запустить установщик драйверов Windows.";
                return false;
            }
        }

        /// <summary>Код возврата pnputil человеческими словами.</summary>
        private static bool Verdict(int code, bool removing, out string output)
        {
            switch (code)
            {
                case 0:
                    output = null;
                    return true;
                case 3010:   // ERROR_SUCCESS_REBOOT_REQUIRED
                    // Успех, а не отказ. Считая его отказом, программа говорила
                    // «установить не удалось» и тут же, на том же экране, показывала
                    // драйвер установленным.
                    // Про само действие здесь не говорим: об этом уже сказал тот, кто нас
                    // позвал. А вот чего ждать после перезагрузки — говорим, и это разное:
                    // общая на оба случая строка про «драйвер не заработает» после удаления
                    // обещала прямо обратное тому, что произошло.
                    output = removing
                        ? "Windows просит перезагрузить компьютер — до этого драйвер останется в работе."
                        : "Windows просит перезагрузить компьютер — до этого драйвер не заработает.";
                    return true;
                case 5:
                    output = removing
                        ? "Windows не дала прав на удаление драйвера."
                        : "Windows не дала прав на установку драйвера.";
                    return false;
                case 1223:
                    output = "Запрос прав администратора отклонён.";
                    return false;
                case 259:
                    // ERROR_NO_MORE_ITEMS. При удалении это «такого в хранилище нет»,
                    // при установке — «пакет не подошёл ни одному устройству».
                    output = removing
                        ? "Такого драйвера в системе нет — удалять нечего."
                        : "Windows не нашла в этом пакете драйвера для вашей клавиатуры.";
                    return false;
                case 87:
                    output = removing
                        ? "Windows не приняла имя, под которым драйвер лежит в хранилище."
                        : "Windows не приняла путь к файлу драйвера.";
                    return false;
                default:
                    output = "Установщик драйверов Windows ответил кодом " + code + ".";
                    return false;
            }
        }

        /// <summary>
        /// Точка повторного разбора: символьная ссылка или соединение. Жёсткие ссылки
        /// сюда не попадают — у них такого признака нет и отличить их от обычного файла
        /// этим способом нельзя. Для нашей задачи довольно: перенаправить запись в чужой
        /// каталог можно как раз точкой повторного разбора.
        /// </summary>
        private static bool IsLink(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return true; }   // не смогли выяснить — считаем подозрительным
        }

        /// <summary>Есть ли точка повторного разбора на самом файле или на любой папке пути.</summary>
        private static bool PathHasLink(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (IsLink(full)) return true;
                DirectoryInfo d = new DirectoryInfo(Path.GetDirectoryName(full));
                while (d != null)
                {
                    if ((d.Attributes & FileAttributes.ReparsePoint) != 0) return true;
                    d = d.Parent;
                }
                return false;
            }
            catch { return true; }
        }
    }
}
