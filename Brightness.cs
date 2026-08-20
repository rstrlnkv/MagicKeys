// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Threading;

namespace MagicKeys
{
    /// <summary>
    /// Яркость экрана по клавишам F1/F2. Внешние мониторы управляются по DDC/CI,
    /// встроенная панель ноутбука — через WMI. Работа идёт в отдельном потоке:
    /// DDC/CI отвечает десятки миллисекунд, а хук клавиатуры столько ждать не может.
    /// </summary>
    internal static class Brightness
    {
        private sealed class Panel
        {
            public IntPtr Handle;   // физический монитор для DDC/CI
            public IntPtr Screen;   // HMONITOR — по нему находим тот, где указатель
            public string Name;
            public uint Min, Cur, Max;
        }

        /// <summary>Сообщает, на каком мониторе изменилась яркость, — чтобы там же показать индикатор.</summary>
        public static event Action<IntPtr, string, int> ChangedOn;

        private static readonly object Sync = new object();
        private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private static List<Panel> _panels;
        private static Native.PHYSICAL_MONITOR[] _owned;
        private static DateTime _scanned = DateTime.MinValue;
        private static int _pending;
        private static Thread _worker;


        /// <summary>Сообщает новый уровень в процентах — для экранного индикатора.</summary>

        // Зовётся из обработчика хука, поэтому здесь только атомарная прибавка и побудка
        // рабочего потока. Запись в журнал открывает и закрывает файл на каждой строке —
        // в хуке этого хватает, чтобы Windows сняла перехват по таймауту.
        public static void Nudge(int deltaPercent)
        {
            Interlocked.Add(ref _pending, deltaPercent);
            EnsureWorker();
            Wake.Set();
        }

        /// <summary>Сбросить кэш мониторов — например, после смены конфигурации экранов.</summary>
        public static void Invalidate()
        {
            lock (Sync) _scanned = DateTime.MinValue;
        }

        private static void EnsureWorker()
        {
            if (_worker != null) return;
            lock (Sync)
            {
                if (_worker != null) return;
                _worker = new Thread(Loop);
                _worker.IsBackground = true;
                _worker.Name = "MagicKeys brightness";
                _worker.Start();
            }
        }

        private static void Loop()
        {
            while (true)
            {
                Wake.WaitOne();
                int delta = Interlocked.Exchange(ref _pending, 0);
                if (delta == 0) continue;
                Diag.Log("яркость: запрос " + delta + "%");
                try { Apply(delta); }
                catch (Exception e) { Diag.Log("яркость: сбой", e); Invalidate(); }
            }
        }

        private static void Apply(int delta)
        {
            Rescan();

            List<Panel> panels;
            lock (Sync) panels = _panels;

            Diag.Log("яркость: панелей по DDC/CI — " + (panels == null ? 0 : panels.Count));
            int shown = -1;
            IntPtr shownOn = IntPtr.Zero;
            string shownName = null;

            // Меняем только тот монитор, на котором сейчас указатель. Это единственный
            // способ понять, какой экран человек имеет в виду: клавиша одна, мониторов
            // несколько, а менять все сразу — не то, чего от неё ждут.
            IntPtr cursorScreen = CursorScreen();
            Panel target0 = UnderCursor(panels, cursorScreen);

            if (target0 == null && panels != null && panels.Count > 0)
            {
                // Монитор под указателем есть, но яркостью не управляется — например,
                // телевизор. Молча менять соседний было бы хуже, чем не делать ничего.
                // Средствами Windows управляется только встроенная панель, поэтому
                // пробуем их, лишь когда под указателем именно она. Без этой попытки
                // достаточно было подключить внешний монитор, чтобы яркость экрана
                // ноутбука пропала совсем. А без проверки, что панель встроенная,
                // выходило обратное: увёл указатель на телевизор — приглушилась панель
                // ноутбука, и плашка с её процентом показалась на телевизоре.
                if (IsInternal(cursorScreen))
                {
                    int wmi = ApplyWmi(delta);
                    if (wmi >= 0)
                    {
                        Diag.Log("яркость: итог " + wmi + "% на встроенной панели");
                        Action<IntPtr, string, int> hw = ChangedOn;
                        if (hw != null) hw(cursorScreen, null, wmi);
                        return;
                    }
                }

                Diag.Log("яркость: монитор под указателем (" + ScreenName(cursorScreen) +
                         ") не отвечает ни на DDC/CI, ни средствами Windows");
                Action<IntPtr, string, int> hu = ChangedOn;
                if (hu != null) hu(cursorScreen, null, -1);
                return;
            }

            if (target0 != null)
            {
                uint range = target0.Max > target0.Min ? target0.Max - target0.Min : 100;
                long step = (long)Math.Round(range * (delta / 100.0));
                if (step == 0) step = delta > 0 ? 1 : -1;
                long want = (long)target0.Cur + step;
                if (want < target0.Min) want = target0.Min;
                if (want > target0.Max) want = target0.Max;

                bool ok = want == target0.Cur;      // упёрлись в край — тоже удача
                if (!ok && Native.SetMonitorBrightness(target0.Handle, (uint)want))
                {
                    target0.Cur = (uint)want;
                    ok = true;
                }

                // Показываем и когда упёрлись в край: иначе кажется, что клавиша
                // не сработала. Но если монитор отказал — молчим: раньше плашка
                // рисовала прежний процент как подтверждение того, чего не было.
                // Так выглядит монитор, выключенный своей кнопкой: событие о смене
                // экранов не приходит, кэш ещё жив, а яркость уже никуда не идёт.
                if (ok)
                {
                    shown = Percent(target0);
                    shownOn = target0.Screen;
                    shownName = target0.Name;
                }
                else Invalidate();   // кэш врёт — пересобрать при следующем нажатии
            }

            // И здесь тоже только для встроенной панели. Эта ветка достаётся случаю,
            // когда по DDC/CI не отвечает ни один монитор: на ноутбуке с телевизором
            // без неё приглушалась бы панель ноутбука, где бы ни стоял указатель.
            if (shown < 0 && IsInternal(cursorScreen))
            {
                shown = ApplyWmi(delta);
                if (shown < 0) Diag.Log("яркость: встроенная панель не отозвалась");
                // Плашке нужен экран, иначе она уедет на главный, а не на тот,
                // где указатель, — соседняя ветка передаёт его именно так.
                if (shown >= 0) shownOn = cursorScreen;
            }
            Diag.Log("яркость: итог " + shown + "% на " + ScreenName(shownOn) + (shownName == null ? "" : " (" + shownName + ")"));

            // Плашку показываем всегда — и с процентом, и без него. Молчание в ответ
            // на нажатие клавиши читается как «программа сломалась»: на настольной
            // машине с одним монитором без DDC/CI не происходило ровно ничего,
            // включая плашку «этот монитор яркость не меняет».
            Action<IntPtr, string, int> h = ChangedOn;
            if (h != null) h(shown >= 0 ? shownOn : cursorScreen, shownName, shown);
        }

        private static int Percent(Panel p)
        {
            uint range = p.Max > p.Min ? p.Max - p.Min : 1;
            return (int)Math.Round((p.Cur - p.Min) * 100.0 / range);
        }

        /// <summary>
        /// Имя экрана вида \\.\DISPLAY2 — мониторы часто зовутся одинаково
        /// («Generic PnP Monitor»), и по названию их в журнале не различить.
        /// </summary>
        // Значения VideoOutputTechnology, означающие «панель внутри корпуса»:
        // LVDS, встроенный DisplayPort, встроенный UDI и собственно INTERNAL.
        private static readonly int[] InternalTech =
            { 6, 11, 13, unchecked((int)0x80000000) };

        /// <summary>
        /// Встроенная ли это панель. Отличить нужно от внешнего монитора, который тоже
        /// не отвечает на DDC/CI, — телевизора или дешёвой панели: средства Windows
        /// управляют только встроенной, где бы ни стоял указатель.
        ///
        /// Windows сама такого вопроса не отвечает, поэтому сводим два источника: WMI
        /// говорит, каким проводом подключён каждый монитор, а EnumDisplayDevices даёт
        /// у экрана путь устройства. Пути записаны по-разному — в одном разделители
        /// «\», в другом «#», — поэтому сравниваем приведённые к одному виду.
        /// </summary>
        private static bool IsInternal(IntPtr screen)
        {
            try
            {
                string device = ScreenName(screen);            // \\.\DISPLAY1
                if (String.IsNullOrEmpty(device) || device == "?") return false;

                var dd = new Native.DISPLAY_DEVICE();
                dd.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
                if (!Native.EnumDisplayDevicesW(device, 0, ref dd, Native.EDD_GET_DEVICE_INTERFACE_NAME))
                    return false;
                string id = dd.DeviceID;                       // \\?\DISPLAY#LGD05C0#4&…#{guid}
                if (String.IsNullOrEmpty(id)) return false;

                foreach (string instance in InternalInstances())
                {
                    string key = instance.Replace('\\', '#');   // DISPLAY\LGD05C0\4&…_0
                    int tail = key.LastIndexOf("_", StringComparison.Ordinal);
                    if (tail > 0) key = key.Substring(0, tail);
                    if (key.Length > 0 && id.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch (Exception e) { Diag.Log("яркость: не удалось выяснить, встроенный ли экран", e); }
            return false;
        }

        private static List<string> InternalInstances()
        {
            var found = new List<string>();
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                           "root\\WMI", "SELECT * FROM WmiMonitorConnectionParams"))
                using (var all = searcher.Get())
                    foreach (System.Management.ManagementObject o in all)
                        using (o)
                        {
                            object tech = o["VideoOutputTechnology"];
                            object name = o["InstanceName"];
                            if (tech == null || name == null) continue;
                            int t = unchecked((int)Convert.ToInt64(tech));
                            for (int i = 0; i < InternalTech.Length; i++)
                                if (t == InternalTech[i]) { found.Add(Convert.ToString(name)); break; }
                        }
            }
            catch (Exception e) { Diag.Log("яркость: не удалось перечислить подключения мониторов", e); }
            return found;
        }

        private static string ScreenName(IntPtr screen)
        {
            if (screen == IntPtr.Zero) return "?";
            try
            {
                var info = new Native.MONITORINFOEX();
                info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (Native.GetMonitorInfoExW(screen, ref info)) return info.szDevice;
            }
            catch { }
            return "?";
        }

        /// <summary>Экран, на котором сейчас указатель.</summary>
        private static IntPtr CursorScreen()
        {
            try
            {
                Native.POINT pt;
                if (Native.GetCursorPos(out pt))
                    return Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
            }
            catch (Exception e) { Diag.Log("яркость: не удалось найти монитор под указателем", e); }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Панель под указателем — или ничего, если этот монитор яркостью не управляется.
        /// Подставлять вместо него соседний нельзя: человек смотрит на один экран,
        /// а потемнел бы другой. Лучше честно ничего не сделать и сказать об этом.
        /// </summary>
        private static Panel UnderCursor(List<Panel> panels, IntPtr screen)
        {
            if (panels == null || panels.Count == 0) return null;
            // Экран под указателем неизвестен — не подсовываем первый попавшийся:
            // «менять соседний» здесь так же неверно, как и везде.
            if (screen == IntPtr.Zero) return null;
            foreach (Panel p in panels)
                if (p.Screen == screen) return p;
            return null;
        }

        private static void Rescan()
        {
            Native.PHYSICAL_MONITOR[] letGo;
            lock (Sync)
            {
                if (_panels != null && (DateTime.UtcNow - _scanned) < TimeSpan.FromSeconds(20)) return;
                letGo = _owned;
                _owned = null;
                _panels = null;
                _scanned = DateTime.UtcNow;
            }

            // Освобождаем СНАРУЖИ замка: DestroyPhysicalMonitors говорит с монитором
            // по DDC/CI, а спящий или отключённый отвечает сотнями миллисекунд. На этом
            // же замке стоит поток системных уведомлений — и стоит он ровно тогда, когда
            // мониторы переключают, то есть когда ответ хуже всего.
            if (letGo != null && letGo.Length > 0)
            {
                try { Native.DestroyPhysicalMonitors((uint)letGo.Length, letGo); }
                catch { }
            }

            List<IntPtr> screens = new List<IntPtr>();
            Native.MonitorEnumProc cb = delegate(IntPtr h, IntPtr hdc, IntPtr rc, IntPtr d)
            {
                screens.Add(h);
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);

            List<Panel> found = new List<Panel>();
            List<Native.PHYSICAL_MONITOR> owned = new List<Native.PHYSICAL_MONITOR>();

            // Полученные дескрипторы кладём в поле сразу, а не в конце: сбой посреди
            // опроса (монитор отключили, dxva2 не отозвалась) терял всё, что уже взяли,
            // и освободить их потом было некому — они утекали порциями, на каждое нажатие.
            try
            {
            foreach (IntPtr screen in screens)
            {
                uint count = 0;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(screen, ref count) || count == 0) continue;
                Native.PHYSICAL_MONITOR[] mons = new Native.PHYSICAL_MONITOR[count];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(screen, count, mons)) continue;
                foreach (Native.PHYSICAL_MONITOR m in mons)
                {
                    owned.Add(m);
                    uint min = 0, cur = 0, max = 0;
                    if (!Native.GetMonitorBrightness(m.hPhysicalMonitor, ref min, ref cur, ref max)) continue;
                    if (max <= min) continue;
                    Panel p = new Panel();
                    p.Handle = m.hPhysicalMonitor;
                    p.Screen = screen;
                    p.Name = m.szDescription;
                    p.Min = min; p.Cur = cur; p.Max = max;
                    found.Add(p);
                }
            }

            }
            finally
            {
                lock (Sync)
                {
                    _panels = found;
                    _owned = owned.ToArray();
                }
            }
        }

        // ---------- встроенная панель ----------

        private static int ReadWmiLevel()
        {
            using (var searcher = new System.Management.ManagementObjectSearcher(
                       "root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
            using (var all = searcher.Get())
            {
                foreach (System.Management.ManagementObject o in all)
                {
                    using (o) return Convert.ToInt32(o["CurrentBrightness"]);
                }
            }
            return -1;
        }

        private static int ApplyWmi(int delta)
        {
            try
            {
                int cur = ReadWmiLevel();
                if (cur < 0) return -1;
                int target = Math.Max(0, Math.Min(100, cur + delta));
                if (target == cur) return cur;
                bool applied = false;

                using (var searcher = new System.Management.ManagementObjectSearcher(
                           "root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                using (var all = searcher.Get())
                {
                    foreach (System.Management.ManagementObject o in all)
                    {
                        using (o) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)target });
                        applied = true;
                    }
                }
                // Пустое перечисление — это «панели, которая слушается, нет», а не успех.
                // Возвращая target без этой проверки, мы рисовали плашку с процентом,
                // которого никто не выставлял, — ровно то, что уже исправили для DDC/CI.
                return applied ? target : -1;
            }
            catch { return -1; }
        }
    }
}
