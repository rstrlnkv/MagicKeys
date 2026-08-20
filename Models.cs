// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;

namespace MagicKeys
{
    /// <summary>Физическое исполнение клавиатуры. От него зависит набор клавиш, а не язык.</summary>
    public enum PhysLayout
    {
        Auto,   // определить по нажатиям
        Ansi,   // 78/109 клавиш, Enter в одну строку, нет клавиши слева от «Z»
        Iso,    // европейское: высокий Enter и лишняя клавиша слева от «Z»
        Jis     // японское: 英数/かな у пробела, ¥ и ろ
    }

    /// <summary>Поколение — им определяется, что напечатано на F1–F12.</summary>
    public enum AppleGen
    {
        Unknown,
        Alu,        // алюминиевые A1243/A1314: F5/F6 — подсветка, есть Eject
        Magic2015,  // A1644/A1843: F5/F6 пустые
        Magic2021,  // A2449/A2450/A2520: F4 — Spotlight, F5 — диктовка, F6 — не беспокоить
        Magic2024   // A3118/A3119/A3203, то же самое, разъём USB-C
    }

    internal sealed class AppleModel
    {
        public int Product;
        public string Name;
        public string Designation;   // номер модели Apple, например A1843
        public int Year;
        public AppleGen Gen;
        public bool Numpad;
        public bool TouchId;
        public bool Eject;           // клавиша ⏏ у алюминиевой серии
        public int FunctionKeys;     // 12 или 19: у полноразмерных есть ряд F13–F19
        public PhysLayout Phys;      // у старых моделей исполнение закодировано в PID
    }

    /// <summary>
    /// Каталог клавиатур Apple. Идентификаторы товаров совпадают у USB (VID 05AC)
    /// и Bluetooth (VID 004C), поэтому ключ — только PID.
    /// Список сверен с таблицей драйвера hid-apple в ядре Linux.
    /// </summary>
    internal static class Models
    {
        private static readonly Dictionary<int, AppleModel> ByProduct = new Dictionary<int, AppleModel>();

        private static void Add(int pid, string name, string designation, int year,
                                AppleGen gen, bool numpad, bool touchId, PhysLayout phys)
        {
            AppleModel m = new AppleModel();
            m.Product = pid; m.Name = name; m.Designation = designation; m.Year = year;
            m.Gen = gen; m.Numpad = numpad; m.TouchId = touchId; m.Phys = phys;
            // ⏏ есть у всей алюминиевой серии и у полноразмерной Magic Keyboard;
            // у моделей с Touch ID её место занял датчик.
            m.Eject = gen == AppleGen.Alu || (gen == AppleGen.Magic2015 && numpad);
            // Ряд F13–F19 есть только у полноразмерных: он идёт над навигационным
            // блоком и цифровой клавиатурой.
            m.FunctionKeys = numpad ? 19 : 12;
            ByProduct[pid] = m;
        }

        private static void AddTriplet(int basePid, string name, string designation, int year, AppleGen gen, bool numpad)
        {
            Add(basePid + 0, name, designation, year, gen, numpad, false, PhysLayout.Ansi);
            Add(basePid + 1, name, designation, year, gen, numpad, false, PhysLayout.Iso);
            Add(basePid + 2, name, designation, year, gen, numpad, false, PhysLayout.Jis);
        }

        static Models()
        {
            // ---- алюминиевые проводные и беспроводные ----
            AddTriplet(0x021D, "Apple Keyboard (компактная)", "A1242", 2007, AppleGen.Alu, false);
            AddTriplet(0x0220, "Apple Keyboard с цифровым блоком", "A1243", 2007, AppleGen.Alu, true);
            AddTriplet(0x024F, "Apple Keyboard с цифровым блоком (rev. B)", "A1243", 2009, AppleGen.Alu, true);
            AddTriplet(0x022C, "Apple Wireless Keyboard", "A1255", 2007, AppleGen.Alu, false);
            AddTriplet(0x0239, "Apple Wireless Keyboard", "A1314", 2009, AppleGen.Alu, false);
            AddTriplet(0x0255, "Apple Wireless Keyboard", "A1314", 2011, AppleGen.Alu, false);

            // ---- Magic Keyboard ----
            Add(0x0267, "Magic Keyboard", "A1644", 2015, AppleGen.Magic2015, false, false, PhysLayout.Auto);
            Add(0x026C, "Magic Keyboard с цифровым блоком", "A1843", 2017, AppleGen.Magic2015, true, false, PhysLayout.Auto);

            Add(0x029C, "Magic Keyboard (2-е поколение)", "A2450", 2021, AppleGen.Magic2021, false, false, PhysLayout.Auto);
            Add(0x029A, "Magic Keyboard с Touch ID", "A2449", 2021, AppleGen.Magic2021, false, true, PhysLayout.Auto);
            Add(0x029F, "Magic Keyboard с Touch ID и цифровым блоком", "A2520", 2021, AppleGen.Magic2021, true, true, PhysLayout.Auto);

            Add(0x0320, "Magic Keyboard (USB-C)", "A3203", 2024, AppleGen.Magic2024, false, false, PhysLayout.Auto);
            Add(0x0321, "Magic Keyboard с Touch ID (USB-C)", "A3118", 2024, AppleGen.Magic2024, false, true, PhysLayout.Auto);
            Add(0x0322, "Magic Keyboard с Touch ID и цифровым блоком (USB-C)", "A3119", 2024, AppleGen.Magic2024, true, true, PhysLayout.Auto);
        }

        public static AppleModel Find(int product)
        {
            AppleModel m;
            return ByProduct.TryGetValue(product, out m) ? m : null;
        }

        /// <summary>
        /// Запасной путь для незнакомого идентификатора: клавиатура сама сообщает своё
        /// название строкой HID («Magic Keyboard with Numeric Keypad»), и по нему видно
        /// и поколение, и наличие цифрового блока.
        /// </summary>
        public static AppleModel FromProduct(string product, int pid)
        {
            if (String.IsNullOrEmpty(product)) return null;
            string p = product.ToLowerInvariant();
            if (p.IndexOf("keyboard", StringComparison.Ordinal) < 0) return null;

            AppleModel m = new AppleModel();
            m.Product = pid;
            m.Name = product;
            m.Designation = "";
            m.Year = 0;
            m.Numpad = p.IndexOf("numeric", StringComparison.Ordinal) >= 0
                    || p.IndexOf("keypad", StringComparison.Ordinal) >= 0;
            m.TouchId = p.IndexOf("touch id", StringComparison.Ordinal) >= 0;
            m.Eject = m.Numpad && !m.TouchId;
            m.FunctionKeys = m.Numpad ? 19 : 12;
            m.Phys = PhysLayout.Auto;
            // «Magic Keyboard» без Touch ID — это 2015-й ряд подписей; с Touch ID — 2021-й.
            if (p.IndexOf("magic", StringComparison.Ordinal) >= 0)
                m.Gen = m.TouchId ? AppleGen.Magic2021 : AppleGen.Magic2015;
            else
                m.Gen = AppleGen.Alu;
            return m;
        }

        /// <summary>Что напечатано на F1–F12 у этого поколения — и, значит, что назначать по умолчанию.</summary>
        public static string[] DefaultFKeys(AppleGen gen)
        {
            switch (gen)
            {
                case AppleGen.Magic2021:
                case AppleGen.Magic2024:
                    return new string[]
                    {
                        "brightness.down", "brightness.up", "sys.taskview", "sys.search",
                        "sys.dictation", "sys.quicksettings",
                        "media.prev", "media.play", "media.next",
                        "volume.mute", "volume.down", "volume.up",
                        "none", "none", "none", "none", "none", "none",
                        "none", "none", "none", "none", "none", "none"
                    };
                case AppleGen.Alu:
                    return new string[]
                    {
                        "brightness.down", "brightness.up", "sys.taskview", "sys.search",
                        "none", "none",
                        "media.prev", "media.play", "media.next",
                        "volume.mute", "volume.down", "volume.up",
                        "none", "none", "none", "none", "none", "none",
                        "none", "none", "none", "none", "none", "none"
                    };
                default: // Magic2015 и всё незнакомое
                    return Settings.DefaultFKeys();
            }
        }

        /// <summary>Подписи клавиш F1–F12 у этого поколения — их видно в интерфейсе.</summary>
        public static string[] Legend(AppleGen gen)
        {
            switch (gen)
            {
                case AppleGen.Magic2021:
                case AppleGen.Magic2024:
                    return new string[]
                    {
                        "яркость −", "яркость +", "Mission Control", "Spotlight",
                        "диктовка", "не беспокоить", "предыдущий", "пуск / пауза", "следующий",
                        "без звука", "тише", "громче",
                        "", "", "", "", "", "", "", "", "", "", "", ""
                    };
                case AppleGen.Alu:
                    return new string[]
                    {
                        "яркость −", "яркость +", "Exposé", "Dashboard",
                        "подсветка −", "подсветка +", "перемотка", "пуск / пауза", "вперёд",
                        "без звука", "тише", "громче",
                        "", "", "", "", "", "", "", "", "", "", "", ""
                    };
                default:
                    return new string[]
                    {
                        "яркость −", "яркость +", "Mission Control", "Launchpad",
                        "—", "—", "предыдущий", "пуск / пауза", "следующий",
                        "без звука", "тише", "громче",
                        "", "", "", "", "", "", "", "", "", "", "", ""
                    };
            }
        }

        /// <summary>
        /// Сколько функциональных клавиш показывать: что обещает модель, что уже
        /// видели своими глазами и что запомнено с прошлого раза — берётся наибольшее.
        /// </summary>
        public static int FunctionKeyCount(Settings s)
        {
            int n = 12;
            AppleModel m = Devices.AppleModel;
            if (m != null && m.FunctionKeys > n) n = m.FunctionKeys;
            if (s != null && s.ObservedFunctionKeys > n) n = s.ObservedFunctionKeys;
            if (KeyWatch.MaxFunctionKey > n) n = KeyWatch.MaxFunctionKey;
            return n > Settings.MaxFKeys ? Settings.MaxFKeys : n;
        }

        public static string GenName(AppleGen gen)
        {
            switch (gen)
            {
                case AppleGen.Alu: return "алюминиевая серия";
                case AppleGen.Magic2015: return "Magic Keyboard, 2015–2017";
                case AppleGen.Magic2021: return "Magic Keyboard, 2021";
                case AppleGen.Magic2024: return "Magic Keyboard, 2024 (USB-C)";
                default: return "поколение не определено";
            }
        }

        public static string PhysName(PhysLayout p)
        {
            switch (p)
            {
                case PhysLayout.Ansi: return "ANSI (американское)";
                case PhysLayout.Iso: return "ISO (европейское)";
                case PhysLayout.Jis: return "JIS (японское)";
                default: return "определяется автоматически";
            }
        }
    }
}
