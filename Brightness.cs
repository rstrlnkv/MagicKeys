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
        private static bool _wmiOnly;

        /// <summary>Сообщает новый уровень в процентах — для экранного индикатора.</summary>
        public static event Action<int> Changed;

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
                // Сначала пробуем средства Windows: так управляется встроенная панель
                // ноутбука, и она как раз по DDC/CI не отвечает. Без этой попытки
                // достаточно было подключить внешний монитор, чтобы яркость экрана
                // ноутбука пропала: до общей ветки WMI ниже дело просто не доходило.
                int wmi = ApplyWmi(delta);
                if (wmi >= 0)
                {
                    Diag.Log("яркость: итог " + wmi + "% на встроенной панели");
                    Action<IntPtr, string, int> hw = ChangedOn;
                    if (hw != null) hw(cursorScreen, null, wmi);
                    return;
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

                if (want != target0.Cur && Native.SetMonitorBrightness(target0.Handle, (uint)want))
                    target0.Cur = (uint)want;

                // Показываем и когда упёрлись в край: иначе кажется, что клавиша не сработала.
                shown = Percent(target0);
                shownOn = target0.Screen;
                shownName = target0.Name;
            }

            if (shown < 0) shown = ApplyWmi(delta);
            Diag.Log("яркость: итог " + shown + "% на " + ScreenName(shownOn) + (shownName == null ? "" : " (" + shownName + ")"));
            if (shown >= 0)
            {
                Action<int> h = Changed;
                if (h != null) h(shown);
                Action<IntPtr, string, int> h2 = ChangedOn;
                if (h2 != null) h2(shownOn, shownName, shown);
            }
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
            if (screen == IntPtr.Zero) return panels[0];
            foreach (Panel p in panels)
                if (p.Screen == screen) return p;
            return null;
        }

        private static void Rescan()
        {
            lock (Sync)
            {
                if (_panels != null && (DateTime.UtcNow - _scanned) < TimeSpan.FromSeconds(20)) return;
                Release();
                _scanned = DateTime.UtcNow;
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

            lock (Sync)
            {
                _panels = found;
                _owned = owned.ToArray();
                if (found.Count == 0) _wmiOnly = ProbeWmi();
            }
        }

        private static void Release()
        {
            if (_owned != null && _owned.Length > 0)
            {
                try { Native.DestroyPhysicalMonitors((uint)_owned.Length, _owned); }
                catch { }
            }
            _owned = null;
            _panels = null;
        }

        // ---------- встроенная панель ----------

        private static bool ProbeWmi()
        {
            try { return ReadWmiLevel() >= 0; }
            catch { return false; }
        }

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

                using (var searcher = new System.Management.ManagementObjectSearcher(
                           "root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                using (var all = searcher.Get())
                {
                    foreach (System.Management.ManagementObject o in all)
                    {
                        using (o) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)target });
                    }
                }
                return target;
            }
            catch { return -1; }
        }
    }
}
