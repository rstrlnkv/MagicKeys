// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Web.Script.Serialization;

namespace MagicKeys
{
    /// <summary>
    /// Обновление через выпуски на GitHub: спросить, что вышло, забрать установщик,
    /// проверить подпись и запустить.
    ///
    /// Установщик запускается, а не распаковывается вручную: он умеет закрыть работающую
    /// копию, заменить файлы и обновить запись в списке программ, а самодельная замена
    /// файла на ходу упирается в то, что файл занят.
    ///
    /// Устройство повторяет соседнюю программу DIIISPL — нарочно: две программы одного
    /// автора, обновляемые по-разному, — это два разных набора ошибок вместо одного.
    /// </summary>
    internal static class Updater
    {
        public const string Owner = "rstrlnkv";
        public const string Repo = "MagicKey";

        /// <summary>
        /// Отпечаток сертификата, которым подписаны сборки. Скачанный установщик обязан
        /// быть подписан именно им — иначе он не запускается, даже если пришёл с GitHub.
        /// Сертификат самодельный, и цепочка доверия здесь ничего не доказала бы: на чужой
        /// машине его корень не установлен. Сверка с известным отпечатком строже — она
        /// проверяет не «кто-то подписал», а «подписал тот самый».
        ///
        /// Значение взято то же, что у DIIISPL, в предположении, что сборки подписываются
        /// одним сертификатом. Если у MagicKeys он будет свой — заменить здесь. Ошибиться
        /// в безопасную сторону: при неверном отпечатке обновление не поставится и скажет
        /// об этом, а не поставится молча.
        /// </summary>
        private const string SignerThumbprint = "103850469089DEECC19D0DD6CE1D4DD3307601FA";

        public static string ReleasesPage
        {
            get { return "https://github.com/" + Owner + "/" + Repo + "/releases"; }
        }

        public static string ProjectPage
        {
            get { return "https://github.com/" + Owner + "/" + Repo; }
        }

        /// <summary>Что нашлось на GitHub.</summary>
        internal sealed class Release
        {
            public Version Version;
            public string Tag;
            public string Notes;
            public string Page;
            public string FileUrl;
            public string FileName;
            public bool Early;
        }

        public static Version Current
        {
            get
            {
                Version parsed = ParseVersion(BuildInfo.Version);
                return parsed != null ? parsed : new Version(0, 0, 0, 0);
            }
        }

        // ------------------------------------------------------------------ проверка

        /// <summary>
        /// Спросить GitHub, что вышло. Возвращает более новый выпуск или null, если новее
        /// ничего нет. Ошибку отдаёт текстом: её показывают в окне, а не пишут в журнал.
        /// </summary>
        public static Release Check(string channel, out string error)
        {
            error = null;
            try
            {
                string json = Fetch("https://api.github.com/repos/" + Owner + "/" + Repo +
                                    "/releases?per_page=20");
                var releases = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(json);
                if (releases == null) { error = "GitHub ответил непонятным"; return null; }

                Release best = null;
                foreach (var item in releases)
                {
                    Release r = Read(item);
                    if (r == null) continue;
                    if (Truthy(item, "draft")) continue;
                    // На канале Stable ранние сборки не предлагаем.
                    if (r.Early && channel != Settings.ChannelDev) continue;
                    if (best == null || r.Version > best.Version) best = r;
                }

                // Отметку ставим и тогда, когда подходящего выпуска не нашлось: связь была,
                // ответ получен — проверка состоялась.
                Settings.LastUpdateCheck = DateTime.UtcNow;
                if (best == null) { error = "выпусков пока нет"; return null; }
                return best.Version > Current ? best : null;
            }
            catch (WebException ex)
            {
                // Пока не вышло ни одного выпуска, GitHub отвечает «нет такого» — это
                // не поломка связи, и говорить о ней так не надо.
                var http = ex.Response as HttpWebResponse;
                error = http != null && http.StatusCode == HttpStatusCode.NotFound
                    ? "выпусков пока нет"
                    : "не удалось связаться с GitHub";
                return null;
            }
            catch
            {
                error = "не удалось прочитать список выпусков";
                return null;
            }
        }

        private static Release Read(Dictionary<string, object> item)
        {
            string tag = Text(item, "tag_name");
            Version v = ParseVersion(tag);
            if (v == null) return null;

            var r = new Release();
            r.Tag = tag;
            r.Version = v;
            r.Notes = Text(item, "body");
            r.Page = Text(item, "html_url");
            r.Early = Truthy(item, "prerelease");

            object assets;
            if (item.TryGetValue("assets", out assets))
            {
                var list = assets as System.Collections.ArrayList;
                if (list != null)
                    foreach (object a in list)
                    {
                        var asset = a as Dictionary<string, object>;
                        if (asset == null) continue;
                        string name = Text(asset, "name");
                        if (name == null || !name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) continue;
                        r.FileName = name;
                        r.FileUrl = Text(asset, "browser_download_url");
                        break;
                    }
            }
            return r;
        }

        // ------------------------------------------------------------------ загрузка

        /// <summary>
        /// Забрать установщик во временную папку и убедиться, что он подписан нашим
        /// сертификатом. Возвращает путь к файлу или null с объяснением.
        /// </summary>
        public static string Download(Release release, out string error)
        {
            error = null;
            if (release == null || String.IsNullOrEmpty(release.FileUrl))
            {
                error = "к этому выпуску не приложен установщик";
                return null;
            }

            string path = null;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "MagicKeys-update");
                Directory.CreateDirectory(dir);

                // Прошлые пакеты убираем здесь, а не после установки: там файл ещё читает
                // msiexec. Иначе они копились бы за каждое обновление и оставались лежать
                // даже после удаления программы — папку эту создаём мы, и убрать её некому.
                try
                {
                    foreach (string old in Directory.GetFiles(dir, "*.msi"))
                        try { File.Delete(old); } catch { }
                }
                catch { }

                // Только имя файла и ничего больше. Имя приходит с сервера, а Path.Combine
                // при корневом втором аргументе выбрасывает первый целиком: «C:\…\что.msi»
                // или «..\..\что.msi» увели бы запись из временной папки куда угодно —
                // и запись идёт до проверки подписи.
                path = Path.Combine(dir, Path.GetFileName(release.FileName));

                // Адрес приходит из ответа GitHub, и схему в нём никто не обещал. Подпись
                // всё равно не пропустит чужое, но качать по file:// с сетевой шары или
                // открытым текстом по http — не то, чего мы просили.
                Uri url;
                if (!Uri.TryCreate(release.FileUrl, UriKind.Absolute, out url) ||
                    url.Scheme != Uri.UriSchemeHttps)
                {
                    error = "ссылка на установщик ведёт не туда, куда должна";
                    return null;
                }

                if (!Grab(url, path, out error)) { Forget(path); return null; }

                // Держим до самого запуска: с этой секунды подменить файл нельзя.
                if (!Hold(path))
                {
                    Forget(path);
                    error = "скачанный установщик занят другой программой";
                    return null;
                }

                string wrong = SignatureProblem(path);
                if (wrong != null) { Forget(path); error = wrong; return null; }
                return path;
            }
            catch (WebException)
            {
                Forget(path);
                error = "не удалось скачать установщик";
                return null;
            }
            catch
            {
                // Системное сообщение здесь не помогает: его читают в окне, и сделать
                // с ним нечего. Говорим о деле — что не получилось и куда смотреть.
                Forget(path);
                error = "не удалось сохранить установщик — проверьте место на диске";
                return null;
            }
        }

        /// <summary>
        /// Что не так с подписью файла. null — всё в порядке. Две разные беды названы
        /// по-разному: «не подписан» бывает у сборки, собранной без сертификата,
        /// а «подписан не тем» — повод насторожиться всерьёз.
        /// </summary>
        private static string SignatureProblem(string path)
        {
            // Сначала целостность: цела ли подпись для этого содержимого. Одного отпечатка
            // мало, и это не мелочь: сертификат читается прямо из файла, поэтому в файл,
            // к которому дописали чужое, он придёт всё тот же — проверка отвечала бы
            // «подписан тем самым» про пакет, собранный кем угодно.
            int verdict = Verify(path);

            // Самоподписанный корень на чужой машине не установлен, и WinVerifyTrust честно
            // об этом говорит. Для нас это не отказ: доверие мы берём не из цепочки,
            // а из отпечатка ниже. Истёкший срок тоже не отказ — подпись ставится с меткой
            // времени, и старый выпуск остаётся действительным. Подмену содержимого ловит
            // не срок, а отдельный вердикт.
            if (verdict != 0 &&
                verdict != Native.CERT_E_UNTRUSTEDROOT &&
                verdict != Native.CERT_E_CHAINING &&
                verdict != Native.CERT_E_EXPIRED)
            {
                if (verdict == Native.TRUST_E_BAD_DIGEST)
                    return "файл изменён после подписания — скачайте выпуск заново";
                if (verdict == Native.TRUST_E_NOSIGNATURE)
                    return "у установщика нет подписи — скачайте выпуск заново";
                return "подпись установщика не прошла проверку — скачайте выпуск заново";
            }

            // Потом личность: тот ли это ключ.
            X509Certificate2 cert;
            try { cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)); }
            catch { return "у установщика нет подписи — скачайте выпуск заново"; }

            if (!String.Equals(cert.Thumbprint, SignerThumbprint, StringComparison.OrdinalIgnoreCase))
                return "установщик подписан чужим ключом. Берите пакет только со страницы выпусков MagicKeys";

            return null;
        }

        /// <summary>Что скажет система о подписи файла. 0 — всё в порядке.</summary>
        private static int Verify(string path)
        {
            var file = new Native.WINTRUST_FILE_INFO();
            file.cbStruct = (uint)Marshal.SizeOf(typeof(Native.WINTRUST_FILE_INFO));
            file.pcwszFilePath = path;

            IntPtr filePtr = Marshal.AllocHGlobal((int)file.cbStruct);
            IntPtr dataPtr = IntPtr.Zero;
            bool fileWritten = false, dataWritten = false;
            try
            {
                Marshal.StructureToPtr(file, filePtr, false);
                fileWritten = true;

                var data = new Native.WINTRUST_DATA();
                data.cbStruct = (uint)Marshal.SizeOf(typeof(Native.WINTRUST_DATA));
                data.dwUIChoice = Native.WTD_UI_NONE;
                data.fdwRevocationChecks = Native.WTD_REVOKE_NONE;   // сеть уже могла отвалиться
                data.dwUnionChoice = Native.WTD_CHOICE_FILE;
                data.pFile = filePtr;
                data.dwStateAction = Native.WTD_STATEACTION_VERIFY;
                data.dwProvFlags = Native.WTD_SAFER_FLAG;

                dataPtr = Marshal.AllocHGlobal((int)data.cbStruct);
                Marshal.StructureToPtr(data, dataPtr, false);
                dataWritten = true;

                Guid action = Native.WINTRUST_ACTION_GENERIC_VERIFY_V2;
                int result = Native.WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);

                // Закрыть состояние обязательно, иначе течёт память провайдера.
                data = (Native.WINTRUST_DATA)Marshal.PtrToStructure(dataPtr, typeof(Native.WINTRUST_DATA));
                data.dwStateAction = Native.WTD_STATEACTION_CLOSE;
                Marshal.StructureToPtr(data, dataPtr, true);
                Native.WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);

                return result;
            }
            catch { return Native.TRUST_E_NOSIGNATURE; }   // не смогли проверить — считаем, что не прошло
            finally
            {
                // Освободить память под структурой мало: путь к файлу в ней — строка,
                // и маршалинг отводит под неё отдельный кусок, о котором FreeHGlobal
                // не знает. Утечка маленькая, но при каждой проверке обновлений.
                if (dataPtr != IntPtr.Zero)
                {
                    if (dataWritten) Marshal.DestroyStructure(dataPtr, typeof(Native.WINTRUST_DATA));
                    Marshal.FreeHGlobal(dataPtr);
                }
                if (fileWritten) Marshal.DestroyStructure(filePtr, typeof(Native.WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(filePtr);
            }
        }

        private static void Forget(string path)
        {
            Unhold();
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        /// <summary>
        /// Скачанный пакет, открытый на чтение без права записи, — и открытым он остаётся
        /// до самого запуска msiexec.
        ///
        /// Иначе между «подпись верна» и «установщик запущен» лежит окно, в которое всё
        /// разваливается: файл лежит во временной папке пользователя, писать в неё может
        /// любой процесс того же человека, а человек в это время смотрит на запрос UAC
        /// и вот-вот нажмёт «Да». Проверили бы мы один файл, а установили другой — и уже
        /// с правами системы. Отпечаток сертификата от этого не спасает: он про тот файл,
        /// которого к моменту запуска может уже не быть.
        /// </summary>
        private static FileStream _held;
        private static string _heldPath;

        private static bool Hold(string path)
        {
            Unhold();
            try
            {
                _held = new FileStream(path, FileMode.Open, FileAccess.Read,
                                       FileShare.Read);   // читать можно всем, писать — никому
                _heldPath = path;
                return true;
            }
            catch { _held = null; _heldPath = null; return false; }
        }

        private static void Unhold()
        {
            FileStream s = _held;
            _held = null; _heldPath = null;
            if (s != null) try { s.Dispose(); } catch { }
        }

        // ------------------------------------------------------------------ установка

        /// <summary>
        /// Запустить установщик и выйти. Дальше работает он: закрывает эту копию,
        /// заменяет файлы и запускает программу заново.
        /// </summary>
        public static bool Install(string msiPath, out string error)
        {
            error = null;
            try
            {
                // Полный путь обязателен. По голому имени ShellExecute ищет и в текущем
                // каталоге тоже, а он у программы бывает чужим — из «Загрузок», из консоли,
                // из автозапуска. Подставленный туда msiexec.exe получил бы уже скачанный
                // и проверенный пакет в аргументах и полное доверие человека.
                var start = new ProcessStartInfo(
                    Path.Combine(Environment.SystemDirectory, "msiexec.exe"));
                // MAGICKEYSRELAUNCH=1 просит установщик поднять программу заново: /qb —
                // установка без последнего окна, а запуск иначе висел бы на его кнопке.
                start.Arguments = "/i \"" + msiPath + "\" MAGICKEYSRELAUNCH=1 /qb /norestart";
                start.UseShellExecute = true;
                Process.Start(start);
                return true;
            }
            catch
            {
                error = "не удалось запустить установщик — откройте скачанный пакет вручную";
                return false;
            }
        }

        public static void OpenPage(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* нечем открыть — молча */ }
        }

        // ------------------------------------------------------------------ мелочи

        /// <summary>
        /// Забрать файл по ссылке. Своими руками, а не WebClient, ради трёх вещей:
        /// каждый переход проверяется на https (WebClient молча уходит с https на http),
        /// у ожидания есть срок (иначе «Скачиваю…» висит до пяти минут молча),
        /// и у размера есть потолок (сервер отвечает не тем, чем обещал, чаще, чем кажется).
        /// </summary>
        private const long MaxInstaller = 200L * 1024 * 1024;

        private static bool Grab(Uri url, string path, out string error)
        {
            error = null;
            Prepare();
            Uri at = url;
            for (int hop = 0; hop < 5; hop++)
            {
                if (at.Scheme != Uri.UriSchemeHttps)
                {
                    error = "загрузка увела с защищённого соединения";
                    return false;
                }
                var request = (HttpWebRequest)WebRequest.Create(at);
                request.UserAgent = Repo;
                request.AllowAutoRedirect = false;
                request.Timeout = 15000;
                request.ReadWriteTimeout = 60000;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code < 400)
                    {
                        string next = response.Headers["Location"];
                        if (String.IsNullOrEmpty(next) ||
                            !Uri.TryCreate(at, next, out at)) { error = "сервер отправил в никуда"; return false; }
                        continue;
                    }
                    if (response.ContentLength > MaxInstaller)
                    {
                        error = "установщик неправдоподобно велик";
                        return false;
                    }

                    long total = 0;
                    var buffer = new byte[64 * 1024];
                    using (Stream from = response.GetResponseStream())
                    using (var to = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        int read;
                        while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            total += read;
                            if (total > MaxInstaller) { error = "установщик неправдоподобно велик"; return false; }
                            to.Write(buffer, 0, read);
                        }
                    }
                    return true;
                }
            }
            error = "сервер водит по кругу";
            return false;
        }

        private static string Fetch(string url)
        {
            Prepare();
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = Repo;                    // GitHub отвечает 403 без него
            request.Accept = "application/vnd.github+json";
            request.Timeout = 15000;
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// .NET Framework по умолчанию предлагает протоколы, от которых GitHub отказался
        /// ещё в 2018-м. Без этой строки соединение рвётся до первого байта.
        /// </summary>
        private static void Prepare()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }   // TLS 1.2
            catch { }
        }

        /// <summary>«v1.0.1», «1.0.1», «1.0.1-beta.2» — берём числа, остальное отбрасываем.</summary>
        public static Version ParseVersion(string text)
        {
            if (String.IsNullOrEmpty(text)) return null;
            string s = text.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int cut = s.IndexOfAny(new char[] { '-', '+', ' ' });
            if (cut > 0) s = s.Substring(0, cut);

            var parts = new List<int>();
            foreach (string piece in s.Split('.'))
            {
                int n;
                if (!Int32.TryParse(piece, out n)) break;
                parts.Add(n);
                if (parts.Count == 4) break;
            }
            if (parts.Count == 0) return null;
            while (parts.Count < 4) parts.Add(0);
            return new Version(parts[0], parts[1], parts[2], parts[3]);
        }

        private static string Text(Dictionary<string, object> map, string key)
        {
            object v;
            return map != null && map.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : null;
        }

        private static bool Truthy(Dictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return false;
            try { return Convert.ToBoolean(v); }
            catch { return false; }
        }

        /// <summary>«две минуты назад» — для строки «когда проверяли».</summary>
        public static string Ago(DateTime? moment)
        {
            if (moment == null) return "Обновления ещё не проверялись";
            TimeSpan since = DateTime.UtcNow - moment.Value;
            if (since < TimeSpan.Zero) since = TimeSpan.Zero;

            if (since.TotalMinutes < 1) return "Проверено только что";
            if (since.TotalHours < 1)
            {
                int m = (int)since.TotalMinutes;
                return "Проверено " + m + " " + Plural(m, "минуту", "минуты", "минут") + " назад";
            }
            if (since.TotalDays < 1)
            {
                int h = (int)since.TotalHours;
                return "Проверено " + h + " " + Plural(h, "час", "часа", "часов") + " назад";
            }
            int d = (int)since.TotalDays;
            return "Проверено " + d + " " + Plural(d, "день", "дня", "дней") + " назад";
        }

        private static string Plural(int n, string one, string few, string many)
        {
            int tens = n % 100;
            if (tens >= 11 && tens <= 14) return many;
            switch (n % 10)
            {
                case 1: return one;
                case 2: case 3: case 4: return few;
                default: return many;
            }
        }
    }
}
