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
        // −2 — ещё не спрашивали, −1 — спросили и не ответила. Разница видна человеку:
        // при −2 показывать «клавиатура не ответила» значит утверждать то, чего никто
        // не проверял, — а по Bluetooth первый ответ приходит секундами позже.
        private static int _percent = Unknown;

        /// <summary>Ещё не спрашивали.</summary>
        public const int Unknown = -2;
        private static DateTime _stamp = DateTime.MinValue;

        /// <summary>
        /// Проценты заряда или −1, если пока неизвестно.
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

        public static void Invalidate()
        {
            lock (Sync) _stamp = DateTime.MinValue;
        }

        /// <summary>Спросить клавиатуру о заряде. Срок держит Wake, здесь его нет.</summary>
        private static void Refresh()
        {
            // Путь узнаём до срока: список устройств заполняется в своём потоке, а по
            // Bluetooth это секунды. Спросив раньше него, мы записывали «клавиатура
            // не ответила» и держали этот ответ минуту — при исправной клавиатуре.
            string path = Devices.AppleStatusPath;

            // Срок тратим, только если было кого спрашивать.
            if (!String.IsNullOrEmpty(path)) { lock (Sync) _stamp = DateTime.UtcNow; }

            int percent = -1, flags = -1;
            if (!String.IsNullOrEmpty(path)) Ask(path, out percent, out flags);

            // Промах не стирает известного. По Bluetooth клавиатура отвечает не всегда,
            // и от одного неудачного вопроса заряд пропадал из подсказки значка на минуту,
            // хотя минуту назад был известен. Пустой ответ признаём, только если самой
            // клавиатуры больше нет.
            lock (Sync)
            {
                // Клавиатуры нет — не «не ответила», а «спрашивать было некого».
                if (String.IsNullOrEmpty(path)) _percent = Unknown;
                else if (percent >= 0) _percent = percent;
                else if (_percent == Unknown) _percent = -1;
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
                if (percent < 0 || percent > 100) percent = -1;
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
