// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MagicKeys
{
    /// <summary>Виртуальные коды, которые встречаются в этой программе.</summary>
    internal static class Vk
    {
        public const int Back = 0x08;
        public const int Tab = 0x09;
        public const int Return = 0x0D;
        public const int Shift = 0x10, Control = 0x11, Menu = 0x12;
        public const int Pause = 0x13;
        public const int Capital = 0x14;
        public const int Kana = 0x15, ImeOn = 0x16, ImeOff = 0x1A;
        public const int Convert = 0x1C, NonConvert = 0x1D;
        public const int Escape = 0x1B;
        public const int Space = 0x20;
        public const int Prior = 0x21, Next = 0x22, End = 0x23, Home = 0x24;
        public const int Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28;
        public const int Snapshot = 0x2C;
        public const int Insert = 0x2D, Delete = 0x2E;
        public const int A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45, F = 0x46, G = 0x47;
        public const int H = 0x48, I = 0x49, J = 0x4A, K = 0x4B, L = 0x4C, M = 0x4D, N = 0x4E;
        public const int O = 0x4F, P = 0x50, Q = 0x51, R = 0x52, S = 0x53, T = 0x54, U = 0x55;
        public const int V = 0x56, W = 0x57, X = 0x58, Y = 0x59, Z = 0x5A;
        public const int LWin = 0x5B, RWin = 0x5C;
        public const int F1 = 0x70, F3 = 0x72, F4 = 0x73, F12 = 0x7B, F13 = 0x7C, F19 = 0x82, F24 = 0x87;
        public const int OemComma = 0xBC;
        public const int D0 = 0x30, D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34;
        public const int D5 = 0x35, D6 = 0x36, D7 = 0x37, D8 = 0x38, D9 = 0x39;
        public const int OemPlus = 0xBB, OemMinus = 0xBD;
        public const int NumLock = 0x90;
        public const int Clear = 0x0C;
        public const int Scroll = 0x91;
        public const int LShift = 0xA0, RShift = 0xA1;
        public const int LControl = 0xA2, RControl = 0xA3;
        public const int LMenu = 0xA4, RMenu = 0xA5;
        public const int MediaNext = 0xB0, MediaPrev = 0xB1, MediaStop = 0xB2, MediaPlay = 0xB3;
        public const int VolumeMute = 0xAD, VolumeDown = 0xAE, VolumeUp = 0xAF;
        public const int Oem3 = 0xC0;   // клавиша слева от «1» на клавиатуре Windows
        public const int Oem102 = 0xE2; // дополнительная клавиша ISO, слева от «Z»
        public const int OemPeriod = 0xBE;

        /// <summary>
        /// Человеческое название клавиши — для проверки клавиш и журнала.
        /// Буквы и цифры называет система, остальное перечислено здесь: у Windows
        /// собственные имена для них либо кривые, либо на английском.
        /// </summary>
        public static string Name(int vk)
        {
            switch (vk)
            {
                case Back: return "Backspace";
                case Tab: return "Tab";
                case Return: return "Enter";
                case Escape: return "Esc";
                case Space: return "Пробел";
                case Capital: return "Caps Lock";
                case Clear: return "Clear (= на блоке)";
                case NumLock: return "Num Lock (Clear на блоке)";
                case Scroll: return "Scroll Lock";
                case Pause: return "Pause";
                case Snapshot: return "Print Screen";
                case Insert: return "Insert";
                case Delete: return "Delete";
                case Home: return "Home";
                case End: return "End";
                case Prior: return "Page Up";
                case Next: return "Page Down";
                case Left: return "←";
                case Up: return "↑";
                case Right: return "→";
                case Down: return "↓";
                case LWin: return "левая ⌘";
                case RWin: return "правая ⌘";
                case LMenu: return "левая ⌥";
                case RMenu: return "правая ⌥ (AltGr)";
                case LShift: return "левая ⇧";
                case RShift: return "правая ⇧";
                case LControl: return "левая control";
                case RControl: return "правая control";
                case MediaNext: return "следующий трек";
                case MediaPrev: return "предыдущий трек";
                case MediaStop: return "стоп";
                case MediaPlay: return "пуск/пауза";
                case VolumeMute: return "без звука";
                case VolumeDown: return "тише";
                case VolumeUp: return "громче";
                case Kana: return "かな";
                case Convert: return "変換";
                case NonConvert: return "無変換";
            }
            if (vk >= F1 && vk <= F24) return "F" + (vk - F1 + 1);
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "цифра " + (vk - 0x60) + " на блоке";
            return "код " + vk.ToString("X2");
        }

        /// <summary>Клавиши, которые в PS/2-мире идут с префиксом E0.</summary>
        public static bool IsExtended(int vk)
        {
            switch (vk)
            {
                case LWin: case RWin: case RControl: case RMenu:
                case Insert: case Delete: case Home: case End:
                case Prior: case Next: case Left: case Up: case Right: case Down:
                case Snapshot:
                case MediaNext: case MediaPrev: case MediaStop: case MediaPlay:
                case VolumeMute: case VolumeDown: case VolumeUp:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Отправка нажатий. Всё помечается меткой, чтобы собственный хук их пропускал.</summary>
    internal static class Input
    {
        private static readonly object Sync = new object();

        public static void Key(int vk, bool down)
        {
            Native.INPUT[] one = new Native.INPUT[1];
            one[0] = MakeVk(vk, down);
            Send(one);
        }

        public static void Tap(int vk)
        {
            Native.INPUT[] two = new Native.INPUT[2];
            two[0] = MakeVk(vk, true);
            two[1] = MakeVk(vk, false);
            Send(two);
        }

        /// <summary>Аккорд: модификаторы зажимаются, клавиша нажимается, всё отпускается в обратном порядке.</summary>
        public static void Chord(int[] modifiers, int vk)
        {
            List<Native.INPUT> seq = new List<Native.INPUT>();
            for (int i = 0; i < modifiers.Length; i++) seq.Add(MakeVk(modifiers[i], true));
            if (vk != 0)
            {
                seq.Add(MakeVk(vk, true));
                seq.Add(MakeVk(vk, false));
            }
            for (int i = modifiers.Length - 1; i >= 0; i--) seq.Add(MakeVk(modifiers[i], false));
            Send(seq.ToArray());
        }

        /// <summary>Нажатие по скан-коду: символ выберет текущая раскладка, а не мы.</summary>
        public static void Scan(ushort scanCode, bool extended, bool down)
        {
            Native.INPUT[] one = new Native.INPUT[1];
            one[0] = MakeScan(scanCode, extended, down);
            Send(one);
        }

        /// <summary>Ввод готового текста: символ уходит как есть, минуя раскладку Windows.</summary>
        public static void Text(string s)
        {
            if (String.IsNullOrEmpty(s)) return;
            var seq = new List<Native.INPUT>(s.Length * 2);
            for (int i = 0; i < s.Length; i++)
            {
                seq.Add(MakeUnicode(s[i], true));
                seq.Add(MakeUnicode(s[i], false));
            }
            Send(seq.ToArray());
        }

        private static Native.INPUT MakeUnicode(char c, bool down)
        {
            Native.INPUT i = new Native.INPUT();
            i.type = Native.INPUT_KEYBOARD;
            i.u.ki.wVk = 0;
            i.u.ki.wScan = c;
            i.u.ki.dwFlags = Native.KEYEVENTF_UNICODE;
            if (!down) i.u.ki.dwFlags |= Native.KEYEVENTF_KEYUP;
            i.u.ki.dwExtraInfo = Native.InjectedTag;
            return i;
        }

        private static Native.INPUT MakeVk(int vk, bool down)
        {
            Native.INPUT i = new Native.INPUT();
            i.type = Native.INPUT_KEYBOARD;
            i.u.ki.wVk = (ushort)vk;
            i.u.ki.wScan = (ushort)Native.MapVirtualKeyW((uint)vk, Native.MAPVK_VK_TO_VSC);
            i.u.ki.dwFlags = 0;
            if (Vk.IsExtended(vk)) i.u.ki.dwFlags |= Native.KEYEVENTF_EXTENDEDKEY;
            if (!down) i.u.ki.dwFlags |= Native.KEYEVENTF_KEYUP;
            i.u.ki.dwExtraInfo = Native.InjectedTag;
            return i;
        }

        private static Native.INPUT MakeScan(ushort scan, bool extended, bool down)
        {
            Native.INPUT i = new Native.INPUT();
            i.type = Native.INPUT_KEYBOARD;
            i.u.ki.wVk = 0;
            i.u.ki.wScan = scan;
            i.u.ki.dwFlags = Native.KEYEVENTF_SCANCODE;
            if (extended) i.u.ki.dwFlags |= Native.KEYEVENTF_EXTENDEDKEY;
            if (!down) i.u.ki.dwFlags |= Native.KEYEVENTF_KEYUP;
            i.u.ki.dwExtraInfo = Native.InjectedTag;
            return i;
        }

        /// <summary>
        /// Куда уходит собранный ввод. Обычно — в Windows. Стенд подменяет его записью:
        /// иначе решения перехвата не проверить, не рассылая нажатия по чужим окнам,
        /// а проверять их надо — на глаз в этой части программы не видно ничего.
        /// В самой программе не подменяется никогда.
        /// </summary>
        internal static Action<Native.INPUT[]> Sink { get; set; }

        private static void Send(Native.INPUT[] items)
        {
            if (items.Length == 0) return;
            Action<Native.INPUT[]> sink = Sink;
            if (sink != null) { sink(items); return; }
            lock (Sync) Native.SendInput((uint)items.Length, items, Marshal.SizeOf(typeof(Native.INPUT)));
        }
    }
}
