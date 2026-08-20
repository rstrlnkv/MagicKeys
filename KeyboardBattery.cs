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
        private static int _percent = -1;
        private static int _flags = -1;
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
                    Refresh(true);
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

        /// <summary>Второй байт отчёта — состояние. Показываем его только в журнале.</summary>
        public static int Flags
        {
            get { lock (Sync) return _flags; }
        }

        public static void Invalidate()
        {
            lock (Sync) _stamp = DateTime.MinValue;
        }

        /// <summary>Перечитать, если прошло больше минуты (или если попросили немедленно).</summary>
        public static void Refresh(bool force)
        {
            lock (Sync)
            {
                if (!force && (DateTime.UtcNow - _stamp) < TimeSpan.FromSeconds(60)) return;
                _stamp = DateTime.UtcNow;
            }

            int percent = -1, flags = -1;
            string path = Devices.AppleStatusPath;
            if (!String.IsNullOrEmpty(path)) Ask(path, out percent, out flags);

            lock (Sync) { _percent = percent; _flags = flags; }
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
