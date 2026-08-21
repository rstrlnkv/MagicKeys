// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Runtime.InteropServices;

namespace MagicKeys
{
    /// <summary>Всё, что уходит в Win32. Собрано в одном месте, чтобы остальной код читался.</summary>
    internal static class Native
    {
        // ---------- окно и оформление ----------

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        public const int DWMSBT_MAINWINDOW = 2;       // Mica — для окон

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // ---------- акрил и скругление углов, как у системных плашек ----------

        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;

        // Цвет рамки; DWMWA_COLOR_NONE убирает её совсем — у системных плашек её нет.
        public const int DWMWA_BORDER_COLOR = 34;
        public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint value, int size);

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS { public int Left, Right, Top, Bottom; }

        /// <summary>
        /// Расширяет рамку DWM на всю клиентскую область. Без этого прозрачный фон
        /// окна остаётся чёрным, и размытие, нарисованное под окном, не видно.
        /// </summary>
        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;   // порядок байтов ABGR, а не ARGB
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWCOMPOSITIONATTRIBDATA
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

        /// <summary>
        /// Включает акриловое размытие за окном. Цвет задаётся в порядке ABGR —
        /// это не опечатка, так устроен GradientColor.
        /// </summary>
        public static int EnableAcrylic(IntPtr hwnd, byte alpha, byte r, byte g, byte b)
        {
            var policy = new ACCENT_POLICY
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = (uint)((alpha << 24) | (b << 16) | (g << 8) | r),
                AnimationId = 0
            };

            int size = Marshal.SizeOf(typeof(ACCENT_POLICY));
            IntPtr mem = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, mem, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = mem,
                    SizeOfData = size
                };
                return SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        // ---------- где сейчас указатель и на каком мониторе ----------

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;   // весь монитор
            public RECT rcWork;      // без панели задач
            public uint dwFlags;
        }

        public const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT p, uint flags);

        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

        // CharSet обязателен: без него ByValTStr считается однобайтовым, cbSize выходит
        // 72 вместо 104, и GetMonitorInfo молча отказывает.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfoExW(IntPtr monitor, ref MONITORINFOEX info);

        // Размер мелкого значка — он же размер значка в трее; растёт вместе с масштабом экрана.
        public const int SM_CXSMICON = 49;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        // ---------- низкоуровневый хук клавиатуры ----------

        public const int WH_KEYBOARD_LL = 13;
        public const int HC_ACTION = 0;

        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;

        public const uint LLKHF_EXTENDED = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        // ---------- синтез ввода ----------

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_SCANCODE = 0x0008;

        /// <summary>Метка «это мы сами послали» — чтобы хук не съел собственный ввод.</summary>
        public static readonly IntPtr InjectedTag = new IntPtr(0x4D4B4559); // 'MKEY'

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        // Объединение обязано быть выровнено как MOUSEINPUT, иначе на x64 SendInput
        // молча ничего не делает: sizeof(INPUT) там 40 байт, а не 32.
        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

        public const uint MAPVK_VK_TO_VSC = 0;

        // ---------- сырой ввод: опознание клавиатур ----------

        public const int RIM_TYPEKEYBOARD = 1;
        public const int RIM_TYPEHID = 2;
        public const uint RIDI_DEVICENAME = 0x20000007;
        public const uint RIDI_DEVICEINFO = 0x2000000B;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RID_DEVICE_INFO_HID
        {
            public uint dwVendorId, dwProductId, dwVersionNumber;
            public ushort usUsagePage, usUsage;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RID_DEVICE_INFO_KEYBOARD
        {
            public uint dwType, dwSubType, dwKeyboardMode;
            public uint dwNumberOfFunctionKeys, dwNumberOfIndicators, dwNumberOfKeysTotal;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct RID_DEVICE_INFO
        {
            [FieldOffset(0)] public uint cbSize;
            [FieldOffset(4)] public uint dwType;
            [FieldOffset(8)] public RID_DEVICE_INFO_HID hid;
            [FieldOffset(8)] public RID_DEVICE_INFO_KEYBOARD keyboard;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceList([In, Out] RAWINPUTDEVICELIST[] list, ref uint num, uint size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint cmd, IntPtr data, ref uint size);

        // ---------- сырой ввод: какие клавиши на устройстве вообще есть ----------

        public const int WM_INPUT = 0x00FF;
        public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
        public const int GIDC_ARRIVAL = 1, GIDC_REMOVAL = 2;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RIDEV_DEVNOTIFY = 0x00002000;
        public const uint RID_INPUT = 0x10000003;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage, usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER
        {
            public uint dwType, dwSize;
            public IntPtr hDevice, wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWKEYBOARD
        {
            public ushort MakeCode, Flags, Reserved, VKey;
            public uint Message, ExtraInformation;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint num, uint size);

        [DllImport("user32.dll")]
        public static extern uint GetRawInputData(IntPtr hRawInput, uint cmd, IntPtr data, ref uint size, uint headerSize);

        // ---------- строки HID: модель прямо из устройства ----------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa,
                                                uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr handle);

        public const uint OPEN_EXISTING = 3;
        public const uint FILE_SHARE_READWRITE = 3;
        public const uint GENERIC_READ = 0x80000000;

        // У всех HidD_* возврат — BOOLEAN, то есть один байт. Без пометки маршалер
        // читает четыре, а старшие три по соглашению о вызове не определены: «работает»
        // ровно до того дня, когда компилятор перестанет обнулять регистр.
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_GetInputReport(IntPtr h, [In, Out] byte[] buffer, int size);


        // ---------- разбор отчётов HID: медиастраница и Eject ----------

        public const uint RIDI_PREPARSEDDATA = 0x20000005;
        public const int HIDP_INPUT = 0;
        // Успех у разбора отчётов HID — не ноль. Код собирается как
        // (важность &lt;&lt; 28) | (0x11 &lt;&lt; 16) | номер, и код средства 0x11 подмешан
        // даже в удачу. Сверка с нулём отбрасывала каждый разобранный отчёт:
        // медиакоды не замечались никогда, а с ними — ни ⏏, ни коды яркости,
        // ни строка «замечено кодов» в окне.
        public const int HIDP_STATUS_SUCCESS = unchecked((int)0x00110000);
        public const ushort UsagePageConsumer = 0x0C;
        public const ushort UsageEject = 0xB8;

        // Яркость экрана на медиастранице. Windows применяет эти коды только
        // к встроенной панели ноутбука, поэтому на обычном ПК они пропадают зря —
        // а MagicKeys умеет по ним управлять внешними мониторами через DDC/CI.
        public const ushort UsageBrightnessUp = 0x6F;
        public const ushort UsageBrightnessDown = 0x70;

        /// <summary>
        /// Названия кодов медиастраницы HID. Драйвер Apple выдаёт их через свою
        /// коллекцию (у Magic Keyboard это COL04, отчёт 0x53 длиной два байта:
        /// идентификатор отчёта и сам код), поэтому по коду сразу видно,
        /// какую клавишу драйвер перевёл и во что.
        /// </summary>
        public static string UsageName(int usage)
        {
            switch (usage)
            {
                case 0x30: return "питание";
                case 0x6F: return "ярче (экран)";
                case 0x70: return "темнее (экран)";
                case 0xB0: return "воспроизведение";
                case 0xB1: return "пауза";
                case 0xB3: return "перемотка вперёд";
                case 0xB4: return "перемотка назад";
                case 0xB5: return "следующий трек";
                case 0xB6: return "предыдущий трек";
                case 0xB7: return "стоп";
                case 0xB8: return "извлечь (Eject)";
                case 0xCD: return "пуск/пауза";
                case 0xE2: return "без звука";
                case 0xE9: return "громче";
                case 0xEA: return "тише";
                // По документу HID Usage Tables подсветка клавиатуры — 0x79/0x7A;
                // 0x6A и 0x6B не назначены ничему.
                case 0x79: return "подсветка клавиш ярче";
                case 0x7A: return "подсветка клавиш темнее";
                case 0x192: return "калькулятор";
                case 0x221: return "поиск";
                case 0x223: return "домашняя страница";
                case 0x224: return "назад";
                case 0x225: return "вперёд";
                case 0x22A: return "избранное";
                default: return "код " + usage.ToString("X2");
            }
        }

        [DllImport("hid.dll")]
        public static extern int HidP_MaxUsageListLength(int reportType, ushort usagePage, IntPtr preparsed);

        [DllImport("hid.dll")]
        public static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
            [In, Out] ushort[] usageList, ref uint usageLength, IntPtr preparsed, IntPtr report, uint reportLength);

        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_GetProductString(IntPtr h, [Out] byte[] buffer, int size);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_GetManufacturerString(IntPtr h, [Out] byte[] buffer, int size);

        // ---------- скрытое окно для сырого ввода ----------

        public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize, style;
            public WndProc lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string lpszMenuName, lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEX c);

        // SetLastError обязателен: код этого вызова уходит в журнал, а без пометки
        // туда попадал остаток от какого-то другого вызова — то есть журнал врал числом.
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(uint exStyle, string cls, string name, uint style,
            int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProcW(IntPtr h, uint msg, IntPtr w, IntPtr l);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr h);

        public const uint WS_POPUP = 0x80000000;

        // ---------- яркость внешних мониторов по DDC/CI ----------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szDescription;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr data);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint count);

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint size, [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll")]
        public static extern bool DestroyPhysicalMonitors(uint size, [In] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll")]
        public static extern bool GetMonitorBrightness(IntPtr hMonitor, ref uint min, ref uint cur, ref uint max);

        [DllImport("dxva2.dll")]
        public static extern bool SetMonitorBrightness(IntPtr hMonitor, uint value);

        // ---------- раскладки ----------

        public const uint KEYEVENTF_UNICODE = 0x0004;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll")]
        public static extern uint GetKeyboardLayoutList(int count, [Out] IntPtr[] list);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int vKey);

        // ---------- прочее ----------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string name);

        /// <summary>Значок из ресурсов самой программы — отдельный файл для этого не нужен.</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadImageW(IntPtr instance, string name, uint type,
                                               int cx, int cy, uint flags);
        public const uint IMAGE_ICON = 1;

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public int x, y;
        }

        [DllImport("user32.dll")]
        public static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);

        [DllImport("kernel32.dll")]
        public static extern uint GetTickCount();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        public const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplayDevicesW(string device, uint num, ref DISPLAY_DEVICE info, uint flags);

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);
        private const int LOGPIXELSX = 88;

        /// <summary>
        /// Системный масштаб в точках на дюйм. Именно им живёт WPF в этой программе:
        /// манифеста с осведомлённостью о масштабе каждого монитора у неё нет, поэтому
        /// спрашивать масштаб отдельного монитора было бы неверно.
        /// </summary>
        public static uint SystemDpi()
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return 0;
            try { return (uint)GetDeviceCaps(dc, LOGPIXELSX); }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }

        // ---- подпись Authenticode ----
        public static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential)]
        public struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
        }

        public const uint WTD_UI_NONE = 2;
        public const uint WTD_REVOKE_NONE = 0;
        public const uint WTD_CHOICE_FILE = 1;
        public const uint WTD_STATEACTION_VERIFY = 1;
        public const uint WTD_STATEACTION_CLOSE = 2;
        public const uint WTD_SAFER_FLAG = 0x100;

        // Вердикты WinVerifyTrust, которые надо различать. Недоверенный корень и истёкший
        // срок — не отказ: сертификат самодельный, а подпись ставится с меткой времени.
        // А вот несовпадение содержимого с подписью — отказ, и говорить о нём надо прямо.
        public const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
        public const int CERT_E_CHAINING = unchecked((int)0x800B010A);
        public const int CERT_E_EXPIRED = unchecked((int)0x800B0101);
        public const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
        public const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        public static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, IntPtr data);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessageW(ref MSG msg);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr w, IntPtr l);

        public const uint WM_QUIT = 0x0012;

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong32(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

        public static void AddExStyle(IntPtr hWnd, int bits)
        {
            if (IntPtr.Size == 8)
            {
                long cur = GetWindowLongPtr64(hWnd, GWL_EXSTYLE).ToInt64();
                SetWindowLongPtr64(hWnd, GWL_EXSTYLE, new IntPtr(cur | (long)(uint)bits));
            }
            else
            {
                int cur = GetWindowLong32(hWnd, GWL_EXSTYLE);
                SetWindowLong32(hWnd, GWL_EXSTYLE, cur | bits);
            }
        }
    }
}
