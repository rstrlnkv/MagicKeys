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
                if (!Download(SevenZipMsi, msi, report, null, out error)) return null;

                // Перед msiexec проверяем подпись: административная установка исполняет
                // последовательность действий из самого пакета, то есть чужой MSI здесь
                // не данные, а программа.
                // Мало убедиться, что подпись действительна: доверенным считается и корень,
                // добавленный в хранилище текущего пользователя, а туда пишут без прав
                // администратора. То есть тот самый противник, ради которого проверка и
                // делается, подписал бы подделку сам. Поэтому сверяем и подписанта.
                // Держим файл открытым от проверки до самого msiexec: иначе между
                // «подпись верна» и «пакет запущен» лежит окно, в которое подставляется
                // другой файл. Папка своя, но писать в неё может любой процесс этого же
                // пользователя — а проверка, которую можно обойти подменой, не проверка.
                using (FileStream keep = Open(msi))
                {
                    if (keep == null)
                    {
                        error = "скачанный установщик 7-Zip занят другой программой";
                        return null;
                    }

                string signer;
                if (!SignatureValid(msi, out signer) || !SignedBy7Zip(signer))
                {
                    error = signer == null
                        ? "установщик 7-Zip не подписан — запускать его нельзя"
                        : "установщик 7-Zip подписан не тем, кем должен (" + signer + ") — запускать его нельзя";
                    try { File.Delete(msi); } catch { }
                    try { File.Delete(StatePath(msi)); } catch { }
                    return null;
                }
                report(0.85, "подпись установщика в порядке: " + signer);

                string target = Path.Combine(ToolsFolder, "7zip");
                report(0.9, "разворачиваю 7-Zip…");
                var psi = new ProcessStartInfo(
                    Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                    "/a \"" + msi + "\" TARGETDIR=\"" + target + "\" /qn");
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
                if (found == null) error = "7-Zip развернулся, но 7z.exe не нашёлся";
                return found;
            }
            catch (Exception e) { error = e.Message; return null; }
        }

        /// <summary>Открыть файл на чтение, запретив запись всем остальным.</summary>
        private static FileStream Open(string path)
        {
            try { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch { return null; }
        }

        /// <summary>
        /// Подписан ли файл действительной подписью Authenticode. Проверяется именно то,
        /// что нужно: не «лежит ли внутри сертификат», а сходится ли подпись с содержимым
        /// файла и доверяет ли Windows цепочке.
        ///
        /// Это единственная настоящая проверка того, что скачанное — то самое. Сумма
        /// SHA-256 рядом с файлом ловит порчу и чужие остатки, но не злой умысел: кто
        /// может подменить файл, тот перепишет и запись о нём.
        ///
        /// Оговорка: проверяется подпись внутри файла. У многих файлов самой Windows её
        /// нет — они подписаны каталогом, — и для них ответ будет «нет». Нам это подходит:
        /// мы проверяем скачанное, а такие файлы подписывают именно внутри.
        /// </summary>
        public static bool SignatureValid(string path, out string signer)
        {
            signer = null;
            IntPtr pFile = IntPtr.Zero, pData = IntPtr.Zero;
            try
            {
                var fi = new Native.WINTRUST_FILE_INFO();
                fi.cbStruct = (uint)Marshal.SizeOf(typeof(Native.WINTRUST_FILE_INFO));
                fi.pcwszFilePath = path;
                pFile = Marshal.AllocHGlobal((int)fi.cbStruct);
                Marshal.StructureToPtr(fi, pFile, false);

                var wd = new Native.WINTRUST_DATA();
                wd.cbStruct = (uint)Marshal.SizeOf(typeof(Native.WINTRUST_DATA));
                wd.dwUIChoice = Native.WTD_UI_NONE;
                wd.fdwRevocationChecks = Native.WTD_REVOKE_NONE;
                wd.dwUnionChoice = Native.WTD_CHOICE_FILE;
                wd.pFile = pFile;
                wd.dwStateAction = Native.WTD_STATEACTION_VERIFY;
                wd.dwProvFlags = Native.WTD_SAFER_FLAG;
                pData = Marshal.AllocHGlobal((int)wd.cbStruct);
                Marshal.StructureToPtr(wd, pData, false);

                Guid action = Native.WINTRUST_ACTION_GENERIC_VERIFY_V2;
                int rc = Native.WinVerifyTrust(IntPtr.Zero, ref action, pData);
                if (rc != 0) return false;

                try
                {
                    var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                        System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path));
                    signer = cert.GetNameInfo(
                        System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false);
                }
                catch { }
                return true;
            }
            catch (Exception e) { Diag.Log("проверка подписи: сбой", e); return false; }
            finally
            {
                // Состояние wintrust закрываем здесь, а не в общем ходу: исключение между
                // проверкой и закрытием оставило бы его висеть до конца работы программы.
                if (pData != IntPtr.Zero)
                {
                    try
                    {
                        var close = (Native.WINTRUST_DATA)Marshal.PtrToStructure(pData, typeof(Native.WINTRUST_DATA));
                        if (close.dwStateAction == Native.WTD_STATEACTION_VERIFY)
                        {
                            close.dwStateAction = Native.WTD_STATEACTION_CLOSE;
                            Marshal.StructureToPtr(close, pData, false);
                            Guid a = Native.WINTRUST_ACTION_GENERIC_VERIFY_V2;
                            Native.WinVerifyTrust(IntPtr.Zero, ref a, pData);
                        }
                    }
                    catch { }
                    Marshal.FreeHGlobal(pData);
                }
                if (pFile != IntPtr.Zero)
                {
                    // FreeHGlobal освобождает блок структуры, но не буфер, который
                    // маршалинг выделил под строку пути. Без DestroyStructure он течёт.
                    try { Marshal.DestroyStructure(pFile, typeof(Native.WINTRUST_FILE_INFO)); } catch { }
                    Marshal.FreeHGlobal(pFile);
                }
            }
        }

        // Кем подписан официальный установщик 7-Zip. Проверка не строгая по регистру
        // и по написанию организации: у Igor Pavlov сертификат менялся, менялась и
        // форма записи, а вот имя оставалось.
        private const string SevenZipSigner = "Igor Pavlov";

        private static bool SignedBy7Zip(string signer)
        {
            return !String.IsNullOrEmpty(signer)
                && signer.IndexOf(SevenZipSigner, StringComparison.OrdinalIgnoreCase) >= 0;
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
                catch (Exception e) { ok = false; error = e.Message; }
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
                EnsureTls();
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

        // ------------------------------------------------------------------
        //  Чему верить в уже скачанном
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

        /// <summary>Качает пакет, докачивая начатое. report(доля 0..1, поясняющая строка).</summary>
        public static bool Download(string url, string target, Action<double, string> report,
                                    ManualResetEvent cancel, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));

                string sUrl, sSha; long sLen;
                bool state = ReadState(target, out sUrl, out sLen, out sSha);
                long have = File.Exists(target) && !IsLink(target) ? new FileInfo(target).Length : 0;
                long total = SizeOf(url);

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
                if (have > 0 && !(state && sUrl == url && total > 0 && sLen == total))
                {
                    try { File.Delete(target); } catch { }
                    have = 0;
                }
                if (total > 0) WriteState(target, url, total, null);

                EnsureTls();
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 60000;
                req.ReadWriteTimeout = 120000;
                if (have > 0) req.AddRange(have);

                using (var resp = (HttpWebResponse)req.GetResponse())
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

                // Сумму записываем только теперь, когда файл целиком наш.
                report(1, "проверяю скачанное…");
                try { WriteState(target, url, new FileInfo(target).Length, Sha256Of(target, null)); }
                catch { }
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
                    if (p.ExitCode > 1)
                    {
                        Diag.Log("7-Zip вернул " + p.ExitCode + ": " + se + so);
                        error = "7-Zip не смог распаковать пакет";
                        return false;
                    }
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
        private static bool Pnputil(string args, out string output)
        {
            output = null;
            try
            {
                string sys = Environment.SystemDirectory;
                var psi = new ProcessStartInfo(Path.Combine(sys, "pnputil.exe"), args);
                psi.WorkingDirectory = sys;
                psi.UseShellExecute = true;      // нужно для запроса прав
                psi.Verb = "runas";
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(120000))
                    {
                        output = "Установка драйвера не уложилась в срок.";
                        return false;
                    }
                    return Verdict(p.ExitCode, out output);
                }
            }
            catch (Exception e)
            {
                Diag.Log("pnputil не запустился", e);
                output = "Запрос прав администратора отклонён.";
                return false;
            }
        }

        /// <summary>Код возврата pnputil человеческими словами.</summary>
        private static bool Verdict(int code, out string output)
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
                    output = "Драйвер установлен. Чтобы он заработал, перезагрузите компьютер.";
                    return true;
                case 5:
                    output = "Windows не дала прав на установку драйвера.";
                    return false;
                case 1223:
                    output = "Запрос прав администратора отклонён.";
                    return false;
                case 259:
                    output = "Такого драйвера в системе нет — удалять нечего.";
                    return false;
                case 87:
                    output = "Windows не приняла путь к файлу драйвера.";
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
    }
}
