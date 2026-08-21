// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;

namespace MagicKeys
{
    /// <summary>
    /// Заряд клавиатуры. Windows его не публикует (свойство DEVPKEY_Bluetooth_Battery
    /// у Magic Keyboard пустое), и сама клавиатура отчёт о заряде не присылает — но его
    /// можно запросить.
    ///
    /// В вендорной коллекции Apple «Device Management» (страница FF00, usage 0014) есть
    /// входной отчёт 0x90 со страницей Battery System. Клавиатура отдаёт его по требованию
    /// через HidD_GetInputReport: три байта, где последний — проценты.
    /// Замерено на подключённой клавиатуре: 90 03 64, то есть 0x64 = 100 %.
    /// </summary>
    internal static class KeyboardBattery
    {
        private const int ReportId = 0x90;
        private const int ReportSize = 3;

        private static readonly object Sync = new object();
        // Состояний три, и путать их нельзя: человеку показывают разные слова.
        // −1 — спросили, клавиатура не ответила (бывает по Bluetooth у спящей).
        // −2 — ещё не спрашивали: по Bluetooth первый ответ приходит секундами позже,
        //      и говорить «не ответила» в эту секунду значит утверждать непроверенное.
        // −3 — спрашивать некого: вендорной коллекции, через которую спрашивают заряд,
        //      у этой клавиатуры нет вовсе. Ждать нечего, и обещать «сейчас придёт»
        //      нельзя — это штатное состояние для алюминиевых моделей, а не сбой.
        private static int _percent = Unknown;

        /// <summary>Ещё не спрашивали.</summary>
        public const int Unknown = -2;

        /// <summary>Спрашивать некого: клавиатура заряд не сообщает.</summary>
        public const int NoSource = -3;
        private static DateTime _stamp = DateTime.MinValue;

        /// <summary>
        /// Проценты заряда или одно из трёх «не число»: −1, Unknown, NoSource.
        ///
        /// Чтение НЕ здесь: оно открывает устройство HID и ждёт отчёта, а по Bluetooth
        /// у спящей клавиатуры это заметная заминка — и приходилась она на поток окна,
        /// прямо при построении страницы. Свойство теперь отдаёт то, что известно, и
        /// заводит обновление в пуле потоков; когда ответ придёт, поднимется Updated,
        /// и страница перестроится сама.
        /// </summary>
        public static int Percent
        {
            get { Wake(); lock (Sync) return _percent; }
        }

        /// <summary>Заряд перечитан. Приходит с потока пула — маршалит подписчик.</summary>
        public static event Action Updated;

        private static int _asking;

        /// <summary>Попросить обновление, если сведения устарели, и сразу вернуться.</summary>
        private static void Wake()
        {
            lock (Sync)
            {
                if ((DateTime.UtcNow - _stamp) < TimeSpan.FromSeconds(60)) return;
            }
            // Один спрашивающий за раз: страница строится не по одному разу, а ответа
            // ждать секунды. Interlocked, а не замок, — Wake зовут из геттера.
            if (System.Threading.Interlocked.CompareExchange(ref _asking, 1, 0) != 0) return;

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    int before;
                    lock (Sync) before = _percent;
                    Refresh();
                    int after;
                    lock (Sync) after = _percent;
                    if (after != before)
                    {
                        Action h = Updated;
                        if (h != null) h();
                    }
                }
                catch (Exception e) { Diag.Log("заряд: не удалось обновить", e); }
                finally { System.Threading.Interlocked.Exchange(ref _asking, 0); }
            });
        }

        /// <summary>Сколько раз забывали. По нему видно, не устарел ли идущий запрос.</summary>
        private static int _age;
        /// <summary>Сколько опросов было к тому мигу, когда ответ забыли.</summary>
        private static int _scanMark;

        /// <summary>
        /// Забыть ответ. Не только срок: число тоже, иначе заряд прежней клавиатуры
        /// показывается как заряд новой — «промах не стирает известного» относится
        /// к промаху той же клавиатуры, а не к смене устройства.
        /// </summary>
        public static void Invalidate()
        {
            lock (Sync)
            {
                _stamp = DateTime.MinValue;
                _percent = Unknown;
                _age++;
                // И отметку опроса: «спрашивать некого» можно говорить только про набор
                // устройств, который уже прочитали целиком. Прежде тут стоял признак
                // «опрос вообще когда-то был», а он защёлкивается навсегда — и после
                // каждого пробуждения человек читал «ждать нечего» на клавиатуре,
                // которая заряд отдаёт прекрасно: по Bluetooth вендорная коллекция
                // поднимается на секунду позже клавиатурной.
                _scanMark = Devices.ScanCount;
            }
        }

        /// <summary>
        /// Спросить клавиатуру о заряде и потратить на это минутный срок. Срок держат
        /// оба: Wake решает, пора ли спрашивать, а здесь он отсчитывается заново —
        /// но только когда было кого спрашивать.
        /// </summary>
        private static void Refresh()
        {
            // Путь узнаём до срока: список устройств заполняется в своём потоке, а по
            // Bluetooth это секунды. Спросив раньше него, мы записывали «клавиатура
            // не ответила» и держали этот ответ минуту — при исправной клавиатуре.
            // Возраст берём ПЕРВЫМ. Взяв его после пути, мы ловили Invalidate, случившийся
            // между двумя строками: возраст уже новый, значит ответ признаётся своим, —
            // а срок при этом ставится по новому пути и переживает забвение. «Спрашиваю
            // у клавиатуры…» держалось минуту без единого запроса в полёте.
            // И счёт опросов — тоже первым, тем же снимком. Взяв его после пути, мы
            // ловили опрос, завершившийся между двумя строками: путь снят до него,
            // а счёт уже вырос — и пустой путь объявлялся как «спрашивать некого».
            // Отпечаток набора при этом не меняется: он считает клавиатуры, а вендорная
            // коллекция — устройство класса HID, так что забвение не приходит вовсе.
            int age, scans;
            lock (Sync) age = _age;
            scans = Devices.ScanCount;
            string path = Devices.AppleStatusPath;

            // Срок тратим, только если было кого спрашивать. И только если за то время,
            // пока мы узнавали путь, набор клавиатур не сменился — спрашиваем об этом
            // ровно тем же способом, каким ниже признаём ответ своим. Прежде здесь
            // сверяли путь, а ниже возраст: между двумя разными вопросами оставалась
            // щель, в которую проваливался Invalidate. Срок при этом уже потрачен,
            // ответ уже выброшен — и «Спрашиваю у клавиатуры…» держалось минуту
            // без единого запроса в полёте.
            if (!String.IsNullOrEmpty(path))
                lock (Sync) { if (age == _age) _stamp = DateTime.UtcNow; }

            int percent = -1, flags = -1;
            if (!String.IsNullOrEmpty(path)) Ask(path, out percent, out flags);

            // Промах не стирает известного. По Bluetooth клавиатура отвечает не всегда,
            // и от одного неудачного вопроса заряд пропадал из подсказки значка на минуту,
            // хотя минуту назад был известен. Пустой ответ признаём, только если самой
            // клавиатуры больше нет.
            lock (Sync)
            {
                // Пока мы спрашивали, набор клавиатур сменился — ответ уже не о том
                // устройстве. По Bluetooth вопрос идёт секундами, и за это время
                // «ещё спрашиваю» успевало превратиться в «клавиатура не ответила».
                if (age != _age) return;

                // Клавиатуры нет — не «не ответила», а «спрашивать некого». Но только
                // если список устройств уже прочитан: на запуске первый опрос уходит
                // в свой поток и по Bluetooth идёт секунды, а мы к тому времени успеваем
                // спросить и получить пустой путь. Сказать тогда «эта клавиатура заряд
                // не сообщает, ждать нечего» — значит утверждать непроверенное, ради чего
                // три состояния и заведены.
                if (String.IsNullOrEmpty(path))
                    _percent = scans > _scanMark ? NoSource : Unknown;
                else if (percent >= 0) _percent = percent;
                else if (_percent < 0) _percent = -1;   // ждали ответа — не дождались
                // Иначе не трогаем: известное число промах не стирает.
            }
            if (percent >= 0) Diag.Log("заряд клавиатуры: " + percent + " % (состояние " + flags + ")");
        }

        private static void Ask(string path, out int percent, out int flags)
        {
            percent = -1; flags = -1;
            IntPtr h = Open(path);
            if (h == IntPtr.Zero) return;
            try
            {
                byte[] buf = new byte[ReportSize];
                buf[0] = ReportId;
                if (!Native.HidD_GetInputReport(h, buf, buf.Length)) return;
                if (buf[0] != ReportId) return;
                flags = buf[1];
                percent = buf[2];
                if (percent > 100) percent = -1;   // байт, меньше нуля не бывает
            }
            catch (Exception e) { Diag.Log("заряд: не удалось спросить", e); }
            finally { Native.CloseHandle(h); }
        }

        private static IntPtr Open(string path)
        {
            IntPtr h = Native.CreateFileW(path, Native.GENERIC_READ, Native.FILE_SHARE_READWRITE,
                                          IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.ToInt64() != -1) return h;
            h = Native.CreateFileW(path, 0, Native.FILE_SHARE_READWRITE,
                                   IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            return h.ToInt64() == -1 ? IntPtr.Zero : h;
        }
    }
}
