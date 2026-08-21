// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace MagicKeys
{
    /// <summary>
    /// Родной драйвер Apple для клавиатур — тот самый KeyMagic из Boot Camp.
    ///
    /// Он делает в ядре примерно то же, что MagicKeys делает в пользовательском режиме,
    /// но с одним важным преимуществом: драйвер привязан к конкретному устройству, а
    /// низкоуровневый перехват — нет. Поэтому если драйвер стоит, разумнее уступить ему
    /// функциональный ряд, иначе одно нажатие переназначится дважды.
    ///
    /// Сам драйвер сюда не входит и войти не может: это несвободные файлы Apple, а
    /// MagicKeys выпущен под GPL. Программа только замечает его и подстраивается.
    /// </summary>
    internal static class AppleDriver
    {
        // Драйвер Magic Keyboard зовётся KeyMagic2, драйвер прежних моделей — KeyMagic.
        private static readonly string[] ServiceKeys =
        {
            @"SYSTEM\CurrentControlSet\Services\KeyMagic2",
            @"SYSTEM\CurrentControlSet\Services\KeyMagic"
        };

        // Где лежит OSXFnBehavior — замерено, а не предположено. На этой машине оно
        // лежит в ключе службы: HKLM\SYSTEM\CurrentControlSet\Services\KeyMagic2,
        // значение 0. Под классом клавиатур его нет ни у одного из шести устройств;
        // прежний комментарий здесь уверял в обратном, и поиск шёл не туда.
        //
        // Само устройство драйвера тоже не под классом клавиатур: «Apple Keyboard»
        // с InfPath = oem100.inf (опубликованное имя keymagic2.inf) стоит под классом
        // HID. Поэтому смотрим в трёх местах и заводим значение там, где стоит
        // устройство, — а не наугад.
        private const string KeyboardClass =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96b-e325-11ce-bfc1-08002be10318}";
        private const string HidClass =
            @"SYSTEM\CurrentControlSet\Control\Class\{745a17a0-74d3-11d0-b6fe-00a0c90f57da}";

        private static readonly object Sync = new object();
        private static bool _installed;
        private static bool _enabled;
        private static int _fnBehavior = -1;
        private static string _fnBehaviorPath;
        private static DateTime _stamp = DateTime.MinValue;

        /// <summary>Драйвер установлен в системе.</summary>
        public static bool Installed { get { Refresh(false); lock (Sync) return _installed; } }

        /// <summary>Установлен и не отключён.</summary>
        public static bool Active { get { Refresh(false); lock (Sync) return _installed && _enabled; } }

        /// <summary>Значение OSXFnBehavior: 1 — как на маке, 0 — наоборот. −1, если не найдено.</summary>
        public static int FnBehavior { get { Refresh(false); lock (Sync) return _fnBehavior; } }

        /// <summary>Где именно нашлось OSXFnBehavior — чтобы человек мог поправить сам.</summary>
        public static string FnBehaviorPath { get { lock (Sync) return _fnBehaviorPath; } }

        /// <summary>
        /// Забирает ли драйвер функциональный ряд себе. Замерено на Magic Keyboard
        /// с драйвером KeyMagic2: при OSXFnBehavior = 1 нажатие F8 приходит уже готовым
        /// медиакодом (ПУСК/ПАУЗА) и помечено как подставленное, то есть до нашего
        /// перехвата F-клавиши не доходят вовсе. При 0 ряд приходит F-клавишами,
        /// и переназначать его должны мы. Значение −1 (не нашлось) считаем за 1:
        /// у Apple это поведение по умолчанию.
        /// </summary>
        // Читается из обработчика хука, поэтому здесь не должно быть ничего медленного.
        // Раньше геттер звал Refresh, а тот раз в 30 секунд перебирал подключи класса
        // клавиатур — десятки открытий ключа HKLM прямо в хуке. На холодном кэше или
        // под антивирусом это укладывается в сотни миллисекунд, а после 300 Windows
        // снимает перехват молча. Теперь значение обновляет сторожевой таймер Engine.
        private static volatile bool _takesRow;

        public static bool TakesFunctionRow { get { return _takesRow; } }

        /// <summary>
        /// Меняет OSXFnBehavior. Значение лежит в HKLM, поэтому нужны права
        /// администратора: запускается reg.exe с запросом повышения. Драйвер
        /// перечитывает его не сразу — нужно переподключить клавиатуру или
        /// перезагрузиться.
        /// </summary>
        public static bool SetFnBehavior(int value, out string error)
        {
            error = null;
            string path;
            lock (Sync) path = _fnBehaviorPath;

            // Нашли значение в отключённой службе — писать туда нельзя: её никто
            // не читает, а человек прочтёт «Записано». Живая служба важнее найденного
            // места: KeyMagic2 вполне может остаться от прошлой клавиатуры и быть
            // отключён при исправно работающем KeyMagic.
            if (!String.IsNullOrEmpty(path) && path.IndexOf(@"\Services\",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string alive = LiveServiceKey();
                // «Тот же ключ или лежит под ним»: сравнение целиком нужно потому, что
                // «KeyMagic» — приставка «KeyMagic2», и найденное в отключённой KeyMagic2
                // принималось за живую KeyMagic. А «под ним» — потому что значение может
                // лежать в подключе \Parameters живой службы, и переводить его оттуда
                // в родительский ключ значит писать не туда, откуда читаем.
                bool same = alive != null && (SameKey(path, alive) || Under(path, alive));
                if (alive == null)
                {
                    // Все службы отключены. Найденное место не выбрасываем: писать
                    // в него бессмысленно, но и подменять на ключ устройства нельзя —
                    // драйвер читает значение из службы. Говорим как есть.
                    error = "Служба драйвера Apple отключена — включать ей нечего. "
                          + "Включите её в «Диспетчере устройств» и повторите.";
                    return false;
                }
                if (!same) path = alive;
            }
            if (String.IsNullOrEmpty(path))
            {
                // Значения ещё нет — заводим его в ключе службы: замер на этой машине
                // показал OSXFnBehavior именно там (Services\KeyMagic2, значение 0),
                // и под классом клавиатур его нет ни у одного устройства.
                //
                // Место всё же гадательное: прочитал ли его драйвер, программе не узнать
                // ничем. Поэтому «Записано» здесь и значит только «записано». Забирает
                // ли драйвер ряд на деле, видно по приходящим медиакодам — это отдельный
                // и честный признак, и страница «Драйвер Apple» показывает его рядом.
                // Работающая служба, а не первая существующая: писать в отключённую
                // значит сказать «Записано» о записи, которую никто не прочтёт.
                path = LiveServiceKey();
                // Ключа устройства здесь нет нарочно. Обратно мы его читаем только при
                // существующей службе (см. Refresh), и запись туда без службы осталась бы
                // обещанием, которого сама программа никогда не подтвердит: человек жмёт,
                // соглашается на запрос прав, читает «Записано» — а страница по-прежнему
                // пишет, что значения нет. Найденное у устройства значение сюда не попадёт:
                // оно уже лежит в _fnBehaviorPath, и ветка выше его не тронет.
            }
            if (String.IsNullOrEmpty(path))
            {
                // Службы есть, но все отключены — это не «не найден». Карточка выше
                // в этот миг пишет «установлен · отключена», и назвать причиной
                // отсутствие значило отправить человека искать не то.
                error = Installed
                    ? "Служба драйвера Apple отключена — записывать некуда. "
                    + "Включите её в «Диспетчере устройств» и повторите."
                    : "Драйвер Apple не найден.";
                return false;
            }

            try
            {
                // Полный путь, а не имя. ShellExecute ищет имя без пути и в текущем
                // каталоге тоже, а он у переносимой программы — её собственная папка,
                // доступная на запись любому процессу того же пользователя. Подложенный
                // туда reg.exe исполнился бы с правами администратора: готовый переход
                // из пользователя в администраторы, ради одного нажатия кнопки.
                var psi = new System.Diagnostics.ProcessStartInfo(
                    System.IO.Path.Combine(Environment.SystemDirectory, "reg.exe"),
                    "add \"" + path + "\" /v OSXFnBehavior /t REG_DWORD /d " + value + " /f");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WorkingDirectory = Environment.SystemDirectory;
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                System.Diagnostics.Process started = System.Diagnostics.Process.Start(psi);
                if (started == null)
                {
                    // Пусто ShellExecute отдаёт, когда новый процесс не понадобился, —
                    // документ открыла уже запущенная программа. Для reg.exe такого
                    // не бывает, и отказ от повышения прав приходит не сюда, а
                    // исключением 1223 ниже. Ветка на всякий случай: молча вернуть
                    // «получилось», не запустив ничего, хуже.
                    error = "Не удалось запустить запись значения.";
                    return false;
                }
                using (System.Diagnostics.Process p = started)
                {
                    p.WaitForExit(30000);
                    if (!p.HasExited) { error = "Windows не ответила на запрос записи"; return false; }
                    if (p.ExitCode != 0) { error = "Windows отказалась записать значение (код " + p.ExitCode + ")"; return false; }
                }
                Refresh(true);
                return true;
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                // Отказ от повышения прав приходит сюда, и это 1223. Прежде человеку
                // показывали текст исключения .NET — на чужом языке и не о том.
                Diag.Log("драйвер Apple: не записать значение, код " + e.NativeErrorCode, e);
                error = e.NativeErrorCode == 1223
                    ? "Запрос прав администратора отклонён — ничего не изменилось."
                    : "Windows не дала записать значение (код " + e.NativeErrorCode + ").";
                return false;
            }
            catch (Exception e)
            {
                Diag.Log("драйвер Apple: не удалось записать OSXFnBehavior", e);
                error = "Не удалось записать значение драйвера.";
                return false;
            }
        }

        public static void Refresh(bool force)
        {
            lock (Sync)
            {
                if (!force && (DateTime.UtcNow - _stamp) < TimeSpan.FromSeconds(30)) return;
                _stamp = DateTime.UtcNow;
                // По просьбе «перечитать» забываем и запомненный ключ устройства:
                // ровно тогда драйвер и мог появиться или исчезнуть.
                if (force) { _devKeyKnown = false; _devKeyAge++; }
            }

            bool installed = false, enabled = false;
            int behavior = -1;
            string where = null;
            string live = null;   // первая работающая служба

            try
            {
                // Обе службы, а не первая найденная. Установщик Boot Camp кладёт обе:
                // KeyMagic2 для Magic Keyboard и KeyMagic для прежних моделей. Обрывая
                // перебор на первой, программа судила о работающем драйвере по чужой
                // службе: KeyMagic2, оставшийся от прошлой клавиатуры и отключённый,
                // давал «Состояние: отключена» при исправно работающем KeyMagic —
                // и ряд забирался себе у драйвера, который его тоже забирает.
                foreach (string path in ServiceKeys)
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(path, false))
                    {
                        if (k == null) continue;
                        installed = true;
                        object start = k.GetValue("Start");
                        // 4 — служба отключена; всё остальное считаем рабочим.
                        // Рабочей считаем, если рабочая хоть одна.
                        if (!(start is int) || ((int)start) != 4)
                        {
                            enabled = true;
                            if (live == null) live = path;
                        }
                    }
                }

                // Порядок обхода: сперва работающая служба, потом все прочие. Список
                // ServiceKeys начинается с KeyMagic2, а он вполне может остаться
                // от прошлой клавиатуры и быть отключён — при работающем KeyMagic.
                // Читая значение у мёртвой службы и записывая туда же, программа
                // сообщала «Записано» о записи, которую никто не прочтёт.
                var order = new List<string>();
                if (live != null) order.Add(live);
                foreach (string path in ServiceKeys) if (path != live) order.Add(path);

                // Ищем в четырёх местах, в порядке достоверности. Ключ живой службы —
                // именно там значение и лежит на этой машине, замерено. Его \Parameters.
                // Ключ устройства драйвера, найденный по опубликованному имени .inf.
                // И последним рубежом — перебор устройств обоих классов.
                foreach (string path in order)
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(path, false))
                    {
                        if (k == null) continue;
                        // Первый нашедший и побеждает. Прежде второй вызов молча
                        // переписывал ответ первого: значение из \Parameters вытесняло
                        // значение самой службы, а запись всё равно уходила в службу —
                        // человек читал «Записано», и режим не менялся никогда.
                        if (!TryBehavior(k, path, ref behavior, ref where))
                            using (RegistryKey p = k.OpenSubKey("Parameters", false))
                                if (p != null) TryBehavior(p, path + @"\Parameters", ref behavior, ref where);
                        if (behavior >= 0) break;
                    }

                // Только когда драйвер вообще установлен. Поиск ключа устройства идёт
                // через опубликованное имя .inf, а оно добывается перебором oem*.inf
                // в %WINDIR%\INF с чтением каждого файла: замерено 112 таких файлов,
                // 8 МБ, 80 мс на четыре имени. На машине без драйвера ответ
                // гарантированно пуст, и повторять эту работу каждые тридцать секунд —
                // молотить диск ради невозможного.
                if (behavior < 0 && installed)
                {
                    string dev = DeviceKey();
                    if (dev != null)
                        using (RegistryKey k = OpenLocal(dev))
                            if (k != null) TryBehavior(k, dev, ref behavior, ref where);
                }

                // Тоже только при установленном драйвере, и по той же причине, что
                // и поиск ключа устройства: без него перебирать десятки устройств двух
                // классов каждые тридцать секунд незачем — ответ всё равно не пригодится,
                // _takesRow домножается на installed.
                if (behavior < 0 && installed) FindBehaviorInClass(ref behavior, ref where);
            }
            catch (Exception e) { Diag.Log("драйвер Apple: не удалось посмотреть реестр", e); }

            lock (Sync)
            {
                _installed = installed;
                _enabled = enabled;
                _fnBehavior = behavior;
                _fnBehaviorPath = where;
            }
            _takesRow = installed && enabled && behavior != 0;
        }


        /// <summary>
        /// Ключ устройства, которым занимается драйвер Apple, — или null. Ищется под
        /// обоими классами: замер показал устройство под классом HID («Apple Keyboard»),
        /// а не под классом клавиатур. Узнаётся по InfPath: там записано имя, под которым
        /// .inf драйвера опубликован в хранилище Windows, а его добывает AppleDriverSetup.
        /// </summary>
        // Ответ помним: перебор %WINDIR%\INF стоит десятки миллисекунд, а меняется он
        // только вместе с установкой или удалением драйвера — то есть по Refresh(true),
        // который зовут обе кнопки.
        private static bool _devKeyKnown;
        private static string _devKey;
        // Поколение: между «сходили за ответом» и «запомнили» может целиком пройти
        // Refresh(true), и без него забвение отменялось ответом, посчитанным ДО
        // установки драйвера. Тот же приём, что у возраста в KeyboardBattery.
        private static int _devKeyAge;

        /// <summary>Лежит ли первый ключ внутри второго.</summary>
        private static bool Under(string inner, string outer)
        {
            if (inner == null || outer == null) return false;
            string root = outer.TrimEnd((char)92) + (char)92;
            return inner.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Один и тот же ключ реестра — сравнение целиком, а не приставкой.</summary>
        private static bool SameKey(string a, string b)
        {
            if (a == null || b == null) return false;
            return String.Equals(a.TrimEnd((char)92), b.TrimEnd((char)92),
                                 StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Первая работающая служба драйвера с приставкой «HKLM\», или null. Отключённая
        /// служба (Start = 4) не в счёт: запись в неё не прочтёт никто.
        /// </summary>
        private static string LiveServiceKey()
        {
            foreach (string p in ServiceKeys)
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(p, false))
                {
                    if (k == null) continue;
                    object start = k.GetValue("Start");
                    if (!(start is int) || ((int)start) != 4) return @"HKLM\" + p;
                }
            return null;
        }

        /// <summary>Ключ устройства драйвера, с памятью на один ответ.</summary>
        private static string DeviceKey()
        {
            int age;
            lock (Sync)
            {
                if (_devKeyKnown) return _devKey;
                age = _devKeyAge;
            }
            string found = DeviceKeyOfDriver();
            lock (Sync)
            {
                // Пока мы искали, кто-то попросил перечитать — ответ уже не о том.
                if (age != _devKeyAge) return found;
                _devKey = found;
                _devKeyKnown = true;
            }
            return found;
        }

        private static string DeviceKeyOfDriver()
        {
            try
            {
                var published = new List<string>();
                foreach (string inf in AppleDriverSetup.WantedInfNames)
                {
                    string oem = AppleDriverSetup.PublishedInf(inf);
                    if (!String.IsNullOrEmpty(oem)) published.Add(oem);
                }
                if (published.Count == 0) return null;

                // Оба класса: замер показал устройство драйвера под HID, а не под
                // классом клавиатур. Перебирать оба дешевле, чем гадать.
                foreach (string cls in new[] { HidClass, KeyboardClass })
                    using (RegistryKey root = Registry.LocalMachine.OpenSubKey(cls, false))
                    {
                        if (root == null) continue;
                        foreach (string sub in root.GetSubKeyNames())
                            using (RegistryKey k = root.OpenSubKey(sub, false))
                            {
                                if (k == null) continue;
                                string inf = Convert.ToString(k.GetValue("InfPath"));
                                if (String.IsNullOrEmpty(inf)) continue;
                                foreach (string oem in published)
                                    if (String.Equals(inf, oem, StringComparison.OrdinalIgnoreCase))
                                        return cls + @"\" + sub;
                            }
                    }
            }
            catch (Exception e) { Diag.Log("драйвер Apple: не найти ключ устройства", e); }
            return null;
        }

        /// <summary>Открыть ключ HKLM по пути с приставкой «HKLM\» или без неё.</summary>
        private static RegistryKey OpenLocal(string path)
        {
            if (String.IsNullOrEmpty(path)) return null;
            if (path.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)) path = path.Substring(5);
            try { return Registry.LocalMachine.OpenSubKey(path, false); }
            catch { return null; }
        }

        /// <summary>
        /// Последний рубеж: перебор устройств обоих классов. Достаётся ему случай, когда
        /// ключ устройства по имени .inf не опознан, — тогда искать больше негде.
        /// Классов два, как и в DeviceKeyOfDriver: устройство драйвера стоит под HID,
        /// и смотреть только под классом клавиатур значило не найти ничего никогда.
        /// </summary>
        private static void FindBehaviorInClass(ref int behavior, ref string where)
        {
            try
            {
                foreach (string cls in new[] { HidClass, KeyboardClass })
                    using (RegistryKey root = Registry.LocalMachine.OpenSubKey(cls, false))
                    {
                        if (root == null) continue;
                        foreach (string sub in root.GetSubKeyNames())
                            using (RegistryKey k = root.OpenSubKey(sub, false))
                            {
                                if (k == null) continue;
                                if (TryBehavior(k, cls + @"\" + sub, ref behavior, ref where)) return;
                            }
                    }
            }
            catch { }
        }

        private static bool TryBehavior(RegistryKey key, string path, ref int behavior, ref string where)
        {
            object v = key.GetValue("OSXFnBehavior");
            if (v == null) return false;

            // Значение, которое не удалось разобрать, — это «не нашли», а не «нашли».
            // Раньше такое обрывало перебор остальных ключей и оставляло behavior = -1,
            // а -1 считается за единицу: один мусорный OSXFnBehavior у любого устройства
            // класса клавиатур — и программа навсегда уступала верхний ряд драйверу.
            int found = -1;
            try
            {
                if (v is int) found = (int)v;
                else if (v is byte[]) { byte[] b = (byte[])v; if (b.Length > 0) found = b[0]; }
                else { int parsed; if (int.TryParse(Convert.ToString(v), out parsed)) found = parsed; }
            }
            catch { return false; }
            if (found < 0) return false;

            behavior = found;
            where = @"HKLM\" + path;
            return true;
        }
    }
}
