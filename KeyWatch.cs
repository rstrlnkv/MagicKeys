// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MagicKeys
{
    /// <summary>
    /// Перепись клавиш: какие клавиши на самом деле есть на подключённой клавиатуре Apple.
    ///
    /// Низкоуровневый хук не знает устройства, зато сырой ввод знает. Поэтому здесь заведено
    /// отдельное скрытое окно, которое слушает WM_INPUT и запоминает, какие клавиши приходили
    /// именно с клавиатуры Apple. Каталог моделей говорит, чего ждать; перепись это уточняет —
    /// и незнакомая модель тоже перестаёт быть загадкой.
    ///
    /// Окно должно быть обычным, пусть и не показанным: окно только для сообщений
    /// (HWND_MESSAGE) сообщений WM_INPUT не получает — проверено.
    /// </summary>
    internal static class KeyWatch
    {
        private const int AppleUsb = 0x05AC, AppleBluetooth = 0x004C;

        private static readonly object Sync = new object();
        private static readonly HashSet<int> SeenKeys = new HashSet<int>();
        private static readonly Dictionary<IntPtr, bool> IsApple = new Dictionary<IntPtr, bool>();
        private static readonly Dictionary<IntPtr, IntPtr> Preparsedes = new Dictionary<IntPtr, IntPtr>();
        private static Thread _thread;
        private static Native.WndProc _proc;
        private static IntPtr _window;
        private static volatile int _maxFunctionKey;
        private static volatile bool _running;

        /// <summary>Самая старшая замеченная функциональная клавиша: 12, 19, 24… или 0.</summary>
        public static int MaxFunctionKey { get { return _maxFunctionKey; } }

        public static bool Running { get { return _running; } }

        private static volatile bool _ejectSeen;

        /// <summary>Приходила ли когда-нибудь клавиша ⏏ с клавиатуры Apple.</summary>
        public static bool EjectSeen { get { return _ejectSeen; } }

        /// <summary>Замечена клавиша, которой раньше не было.</summary>
        public static event Action Discovered;

        /// <summary>Нажата клавиша ⏏ — её нет в потоке клавиатуры, только в медиаотчёте.</summary>
        public static event Action EjectPressed;

        /// <summary>
        /// Каждое событие с клавиатуры Apple, как оно пришло. Нужно для самопроверки:
        /// человек нажимает клавишу и сразу видит, что именно до Windows дошло —
        /// клавиша, медиакод или ничего.
        /// </summary>
        public static event Action<KeyEvent> Activity;

        /// <summary>Одно замеченное событие: либо клавиша, либо код медиастраницы.</summary>
        internal sealed class KeyEvent
        {
            public bool Media;       // true — код медиастраницы, false — обычная клавиша
            public int Code;         // виртуальный код клавиши либо usage медиастраницы
            public int ScanCode;     // скан-код; у медиакодов нулевой
            public bool Fresh;       // такого события раньше не было
        }

        private static readonly HashSet<int> SeenUsages = new HashSet<int>();
        private static volatile bool _mediaSeen;

        /// <summary>
        /// Приходил ли с клавиатуры Apple хоть один медиакод. Это единственное
        /// надёжное свидетельство, что родной драйвер действительно преобразует
        /// функциональный ряд: значение в реестре говорит лишь о том, как драйвер
        /// настроен, а не о том, дошёл ли он до этой клавиатуры. По Bluetooth,
        /// например, он может не подключиться вовсе.
        /// </summary>
        public static bool MediaSeen { get { return _mediaSeen; } }

        /// <summary>Приходил ли когда-нибудь такой код медиастраницы.</summary>
        public static bool SeenUsage(int usage)
        {
            lock (Sync) return SeenUsages.Contains(usage);
        }

        public static int[] AllUsages()
        {
            lock (Sync)
            {
                int[] a = new int[SeenUsages.Count];
                SeenUsages.CopyTo(a);
                Array.Sort(a);
                return a;
            }
        }

        public static bool Seen(int vk)
        {
            lock (Sync) return SeenKeys.Contains(vk);
        }

        public static int[] All()
        {
            lock (Sync)
            {
                int[] a = new int[SeenKeys.Count];
                SeenKeys.CopyTo(a);
                Array.Sort(a);
                return a;
            }
        }

        public static void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "MagicKeys key watch";
            _thread.Start();
        }

        private static void Run()
        {
            try
            {
                _proc = WindowProc;
                var wc = new Native.WNDCLASSEX();
                wc.cbSize = (uint)Marshal.SizeOf(typeof(Native.WNDCLASSEX));
                wc.lpfnWndProc = _proc;
                wc.hInstance = Native.GetModuleHandleW(null);
                wc.lpszClassName = "MagicKeysKeyWatch";
                Native.RegisterClassExW(ref wc);

                _window = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW, "MagicKeysKeyWatch", "MagicKeys",
                    Native.WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
                if (_window == IntPtr.Zero)
                {
                    Diag.Log("перепись клавиш: окно не создано, ошибка " + Marshal.GetLastWin32Error());
                    return;
                }

                var rid = new Native.RAWINPUTDEVICE[2];
                rid[0].usUsagePage = 0x01;   // Generic Desktop
                rid[0].usUsage = 0x06;       // Keyboard
                rid[0].dwFlags = Native.RIDEV_INPUTSINK | Native.RIDEV_DEVNOTIFY;
                rid[0].hwndTarget = _window;
                rid[1].usUsagePage = 0x0C;   // Consumer Control — там живёт Eject
                rid[1].usUsage = 0x01;
                rid[1].dwFlags = Native.RIDEV_INPUTSINK | Native.RIDEV_DEVNOTIFY;
                rid[1].hwndTarget = _window;
                if (!Native.RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE))))
                {
                    Diag.Log("перепись клавиш: сырой ввод не подписан, ошибка " + Marshal.GetLastWin32Error());
                    return;
                }

                _running = true;
                Native.MSG msg;
                while (Native.GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessageW(ref msg);
                }
            }
            catch (Exception e) { Diag.Log("перепись клавиш: сбой", e); }
            finally
            {
                _running = false;
                if (_window != IntPtr.Zero) { Native.DestroyWindow(_window); _window = IntPtr.Zero; }
            }
        }

        private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            if (msg == Native.WM_INPUT)
            {
                try { Handle(l); }
                catch { }
            }
            else if (msg == Native.WM_INPUT_DEVICE_CHANGE)
            {
                // Дескриптор устройства живёт до отключения, а потом Windows выдаёт его
                // заново — возможно, уже другому устройству. Кэш, привязанный к нему,
                // надо снимать сразу: иначе он растёт при каждом переподключении (Magic
                // Keyboard по Bluetooth переподключается на каждом пробуждении) и однажды
                // отдаёт чужое описание отчётов не тому устройству.
                if (w.ToInt64() == Native.GIDC_REMOVAL) { try { Drop(l); } catch { } }
            }
            return Native.DefWindowProcW(hwnd, msg, w, l);
        }

        private static void Handle(IntPtr rawHandle)
        {
            int headerSize = Marshal.SizeOf(typeof(Native.RAWINPUTHEADER));
            uint size = 0;
            Native.GetRawInputData(rawHandle, Native.RID_INPUT, IntPtr.Zero, ref size, (uint)headerSize);
            if (size == 0 || size > 4096) return;

            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Native.GetRawInputData(rawHandle, Native.RID_INPUT, buf, ref size, (uint)headerSize) != size) return;
                var header = (Native.RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(Native.RAWINPUTHEADER));
                if (!FromApple(header.hDevice)) return;

                if (header.dwType == Native.RIM_TYPEKEYBOARD)
                {
                    var kb = (Native.RAWKEYBOARD)Marshal.PtrToStructure(
                        new IntPtr(buf.ToInt64() + headerSize), typeof(Native.RAWKEYBOARD));
                    if (kb.Message != Native.WM_KEYDOWN && kb.Message != Native.WM_SYSKEYDOWN) return;
                    Remember(kb.VKey, kb.MakeCode);
                }
                else if (header.dwType == Native.RIM_TYPEHID)
                {
                    HandleHid(header.hDevice, buf, headerSize);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>
        /// Отчёт медиастраницы. Здесь ищем ровно одно: usage 0xB8 — клавишу ⏏.
        /// У Magic Keyboard такой коллекции нет вовсе, так что для неё сюда не попадают;
        /// у алюминиевых моделей с USB — должны.
        /// </summary>
        private static void Drop(IntPtr device)
        {
            lock (Sync)
            {
                IntPtr p;
                if (Preparsedes.TryGetValue(device, out p))
                {
                    if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
                    Preparsedes.Remove(device);
                }
                IsApple.Remove(device);
            }
        }

        // Под замком целиком. Описание отчётов освобождается из другого потока — при
        // отключении устройства и по кнопке «Начать заново», — а разбор без замка ушёл бы
        // по уже освобождённой памяти внутри чужого кода HID: падение без объяснений.
        private static void HandleHid(IntPtr device, IntPtr buf, int headerSize)
        {
            lock (Sync) HandleHidLocked(device, buf, headerSize);
        }

        private static void HandleHidLocked(IntPtr device, IntPtr buf, int headerSize)
        {
            IntPtr preparsed = Preparsed(device);
            if (preparsed == IntPtr.Zero) return;

            int sizeHid = Marshal.ReadInt32(buf, headerSize);
            int count = Marshal.ReadInt32(buf, headerSize + 4);
            if (sizeHid <= 0 || count <= 0) return;
            IntPtr data = new IntPtr(buf.ToInt64() + headerSize + 8);

            int max = Native.HidP_MaxUsageListLength(Native.HIDP_INPUT, Native.UsagePageConsumer, preparsed);
            if (max <= 0 || max > 256) return;
            ushort[] usages = new ushort[max];

            for (int i = 0; i < count; i++)
            {
                uint len = (uint)max;
                IntPtr report = new IntPtr(data.ToInt64() + (long)i * sizeHid);
                if (Native.HidP_GetUsages(Native.HIDP_INPUT, Native.UsagePageConsumer, 0,
                                          usages, ref len, preparsed, report, (uint)sizeHid) != 0) continue;
                for (int u = 0; u < len; u++)
                {
                    int usage = usages[u];
                    if (usage == 0) continue;   // отпускание: пустой список кодов

                    bool fresh;
                    lock (Sync) fresh = SeenUsages.Add(usage);
                    if (fresh) Diag.Log("замечен медиакод " + usage.ToString("X2"));
                    _mediaSeen = true;

                    Report(new KeyEvent { Media = true, Code = usage, Fresh = fresh });

                    if (usage != Native.UsageEject) continue;
                    if (!_ejectSeen) { _ejectSeen = true; Diag.Log("замечена клавиша ⏏"); }
                    Action h = EjectPressed;
                    if (h != null) h();
                }
            }
        }

        private static IntPtr Preparsed(IntPtr device)
        {
            IntPtr cached;
            lock (Sync) if (Preparsedes.TryGetValue(device, out cached)) return cached;

            IntPtr result = IntPtr.Zero;
            uint size = 0;
            Native.GetRawInputDeviceInfoW(device, Native.RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
            if (size > 0 && size < 64 * 1024)
            {
                IntPtr b = Marshal.AllocHGlobal((int)size);
                if (Native.GetRawInputDeviceInfoW(device, Native.RIDI_PREPARSEDDATA, b, ref size) != uint.MaxValue)
                    result = b;
                else
                    Marshal.FreeHGlobal(b);
            }
            lock (Sync) Preparsedes[device] = result;
            return result;
        }

        private static void Remember(int vk, int scanCode)
        {
            if (vk == 0 || vk == 0xFF) return;
            bool fresh;
            lock (Sync) fresh = SeenKeys.Add(vk);

            Report(new KeyEvent { Media = false, Code = vk, ScanCode = scanCode, Fresh = fresh });
            if (!fresh) return;

            if (vk >= Vk.F1 && vk <= Vk.F24)
            {
                int n = vk - Vk.F1 + 1;
                if (n > _maxFunctionKey) _maxFunctionKey = n;
            }

            Action h = Discovered;
            if (h != null) h();
        }

        private static void Report(KeyEvent e)
        {
            Action<KeyEvent> h = Activity;
            if (h == null) return;
            try { h(e); }
            catch (Exception ex) { Diag.Log("перепись клавиш: подписчик упал", ex); }
        }

        private static bool FromApple(IntPtr device)
        {
            bool known;
            lock (Sync) if (IsApple.TryGetValue(device, out known)) return known;

            bool apple = false;
            uint size = 0;
            Native.GetRawInputDeviceInfoW(device, Native.RIDI_DEVICENAME, IntPtr.Zero, ref size);
            if (size > 0 && size < 4096)
            {
                IntPtr b = Marshal.AllocHGlobal((int)size * 2);
                try
                {
                    if (Native.GetRawInputDeviceInfoW(device, Native.RIDI_DEVICENAME, b, ref size) != uint.MaxValue)
                    {
                        string path = Marshal.PtrToStringUni(b);
                        if (path != null)
                            apple = path.IndexOf("VID_05AC", StringComparison.OrdinalIgnoreCase) >= 0
                                 || path.IndexOf("VID&0001004C", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
                finally { Marshal.FreeHGlobal(b); }
            }

            lock (Sync) IsApple[device] = apple;
            return apple;
        }

        /// <summary>Забыть накопленное — например, когда подключили другую клавиатуру.</summary>
        public static void Forget()
        {
            lock (Sync)
            {
                SeenKeys.Clear();
                SeenUsages.Clear();
                IsApple.Clear();
                foreach (IntPtr p in Preparsedes.Values) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
                Preparsedes.Clear();
            }
            _mediaSeen = false;
            _maxFunctionKey = 0;
            _ejectSeen = false;
        }
    }
}
