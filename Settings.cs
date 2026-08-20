// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace MagicKeys
{
    /// <summary>Модификаторы, которые умеет различать хук. Порядок важен только для списков в интерфейсе.</summary>
    public enum ModKey
    {
        None,       // клавиша выключается совсем
        LCtrl,
        RCtrl,
        LWin,
        RWin,
        LAlt,
        RAlt,
        LShift,
        RShift,
        CapsLock,
        Escape
    }

    /// <summary>Откуда берётся третий уровень раскладки.</summary>
    public enum OptLevel
    {
        Off,            // не трогать: ⌥ остаётся обычным Alt
        RightOption,    // правый ⌥ — как AltGr; левый по-прежнему открывает меню
        AnyOption       // любой ⌥, как в macOS; меню по Alt перестанут открываться
    }

    /// <summary>Связка «язык ввода Windows → раскладка Apple».</summary>
    public sealed class LayoutBinding
    {
        public string Lang;    // LANGID четырьмя шестнадцатеричными цифрами, например 0419
        public string Layout;  // идентификатор файла раскладки, например ru
    }

    public sealed class Settings
    {
        public bool Enabled = true;

        /// <summary>Приостанавливать переназначения, пока Magic Keyboard не подключена.</summary>
        public bool PauseWhenAppleAbsent = true;

        /// <summary>true — F1…F12 сразу дают медиафункции (как в macOS); false — остаются F-клавишами.</summary>
        public bool MediaFirst = true;

        /// <summary>Клавиша-заменитель Fn: с ней режим F-клавиш временно переворачивается.</summary>
        public ModKey FnSubstitute = ModKey.RAlt;

        /// <summary>Действия для F1…F24 — идентификаторы из <see cref="Actions"/>.</summary>
        public string[] FKeys = DefaultFKeys();

        /// <summary>Клавиша ⌧ на цифровом блоке: у Apple это «clear», Windows видит Num Lock.</summary>
        public string NumpadClear = "key.delete";

        /// <summary>Клавиша «=» на цифровом блоке: Windows её не понимает и по умолчанию молчит.</summary>
        public string NumpadEquals = "text.equals";

        /// <summary>Клавиша ⏏ — приходит не как клавиша, а отчётом медиастраницы.</summary>
        public string EjectKey = "none";

        /// <summary>Сколько функциональных клавиш замечено у клавиатуры Apple на самом деле.</summary>
        public int ObservedFunctionKeys = 0;

        public ModKey MapLCtrl = ModKey.LCtrl;
        public ModKey MapLWin = ModKey.LWin;
        public ModKey MapLAlt = ModKey.LAlt;
        public ModKey MapRAlt = ModKey.RAlt;
        public ModKey MapRWin = ModKey.RWin;
        public ModKey MapCapsLock = ModKey.CapsLock;

        /// <summary>Поменять местами клавишу слева от «1» и клавишу слева от «Z».</summary>
        public bool SwapIsoKeys = true;

        /// <summary>Исполнение клавиатуры. «Auto» — определять по нажатиям.</summary>
        public PhysLayout Physical = PhysLayout.Auto;

        /// <summary>Воспроизводить раскладки macOS: то, что напечатано на клавишах Apple.</summary>
        public bool AppleLayoutEnabled = false;

        /// <summary>Каким ⌥ набирается третий уровень (символы, напечатанные на клавише третьими).</summary>
        public OptLevel OptLevel = OptLevel.RightOption;

        /// <summary>Подменять все клавиши раскладки, а не только отличающиеся от раскладки Microsoft.</summary>
        public bool AppleLayoutAll = false;

        /// <summary>Какая раскладка Apple применяется к какому языку ввода Windows.</summary>
        public LayoutBinding[] LayoutBindings = new LayoutBinding[0];

        /// <summary>Навигация как в macOS: Fn+стрелки, Fn+Backspace, Fn+Enter.</summary>
        public bool FnNavigation = true;

        /// <summary>Уступать функциональный ряд родному драйверу Apple, если он установлен.</summary>
        public bool YieldToAppleDriver = true;

        /// <summary>Действия для клавиш японской раскладки: かな, 変換, 無変換, ろ, ¥.</summary>
        public string[] JisKeys = new string[] { "none", "none", "none", "none", "none" };

        /// <summary>Отключить AltGr: правый ⌥ станет обычным Alt.</summary>
        public bool DisableAltGr = false;

        /// <summary>⌘+Tab переключает окна так же, как Alt+Tab.</summary>
        public bool CmdTabSwitchesWindows = false;

        /// <summary>Переводить сочетания macOS в виндовые: ⌘C, ⌘←, ⌘Q и прочие.</summary>
        public bool MacShortcuts = false;

        /// <summary>Какие сочетания выключены по отдельности — список их ключей.</summary>
        public string[] MacShortcutsOff = new string[0];

        /// <summary>
        /// Что делает пробел с модификатором. На маке привычки разные: у одних
        /// ⌘Space открывает поиск, у других переключает язык, а поиск висит на
        /// ⌃Space. Поэтому обе клавиши настраиваются по отдельности.
        /// Значения: search, language, none.
        /// </summary>
        public string CmdSpace = "search";
        public string CtrlSpace = "none";

        /// <summary>
        /// Режим разработчика: показывает страницы и настройки, которые обычному
        /// человеку только мешают, — перепись клавиш, устройства, исполнение
        /// клавиатуры, японские клавиши, подмену всех клавиш и техническую часть
        /// страницы драйвера. Журнал сюда не относится — он включается ключом --log.
        /// </summary>
        public bool DeveloperMode = false;

        /// <summary>Включено ли конкретное сочетание.</summary>
        public bool MacEnabled(string id)
        {
            if (MacShortcutsOff == null) return true;
            for (int i = 0; i < MacShortcutsOff.Length; i++)
                if (MacShortcutsOff[i] == id) return false;
            return true;
        }

        public void MacSet(string id, bool on)
        {
            var list = new System.Collections.Generic.List<string>();
            if (MacShortcutsOff != null) list.AddRange(MacShortcutsOff);
            list.Remove(id);
            if (!on) list.Add(id);
            MacShortcutsOff = list.ToArray();
        }

        /// <summary>
        /// Ловить коды яркости с медиастраницы и применять их к внешним мониторам.
        /// Нужно, когда функциональным рядом занимается драйвер: он шлёт коды яркости,
        /// но Windows применяет их только к встроенной панели ноутбука, и на обычном ПК
        /// они пропадают зря.
        /// </summary>
        public bool BrightnessFromMediaKeys = true;

        public bool ShowBrightnessOsd = true;
        public int BrightnessStep = 10;

        public bool Autostart = false;
        public bool StartMinimized = false;

        /// <summary>F1…F24 — больше в Windows не бывает.</summary>
        public const int MaxFKeys = 24;

        public static string[] DefaultFKeys()
        {
            return new string[]
            {
                "brightness.down",  // F1
                "brightness.up",    // F2
                "sys.taskview",     // F3  — Mission Control
                "sys.search",       // F4  — Launchpad / Spotlight
                "none",             // F5  — подсветки клавиш у Magic Keyboard нет
                "none",             // F6
                "media.prev",       // F7
                "media.play",       // F8
                "media.next",       // F9
                "volume.mute",      // F10
                "volume.down",      // F11
                "volume.up",        // F12
                // F13–F24: на клавишах Apple ничего не напечатано, поэтому по умолчанию
                // они остаются собой.
                "none", "none", "none", "none", "none", "none",
                "none", "none", "none", "none", "none", "none"
            };
        }

        public string FKey(int index)
        {
            if (FKeys == null || index < 0 || index >= FKeys.Length) return "none";
            return FKeys[index] ?? "none";
        }

        public string JisKey(int index)
        {
            if (JisKeys == null || index < 0 || index >= JisKeys.Length) return "none";
            return JisKeys[index] ?? "none";
        }

        /// <summary>Какая раскладка Apple назначена этому языку ввода Windows.</summary>
        public string LayoutFor(int langId)
        {
            if (LayoutBindings == null) return null;
            string hex = langId.ToString("X4");
            foreach (LayoutBinding b in LayoutBindings)
                if (b != null && String.Equals(b.Lang, hex, StringComparison.OrdinalIgnoreCase))
                    return String.IsNullOrEmpty(b.Layout) ? null : b.Layout;
            return null;
        }

        public void SetLayoutFor(int langId, string layout)
        {
            string hex = langId.ToString("X4");
            var list = new List<LayoutBinding>();
            if (LayoutBindings != null)
                foreach (LayoutBinding b in LayoutBindings)
                    if (b != null && !String.Equals(b.Lang, hex, StringComparison.OrdinalIgnoreCase))
                        list.Add(b);
            if (!String.IsNullOrEmpty(layout))
            {
                LayoutBinding nb = new LayoutBinding();
                nb.Lang = hex; nb.Layout = layout;
                list.Add(nb);
            }
            LayoutBindings = list.ToArray();
        }

        // ---------- хранение ----------

        public static string Folder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MagicKeys");
            }
        }

        public static string FilePath { get { return Path.Combine(Folder, "settings.xml"); } }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using (FileStream fs = File.OpenRead(FilePath))
                    {
                        var s = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(fs);
                        s.Normalize();
                        return s;
                    }
                }
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string tmp = FilePath + ".tmp";
                using (FileStream fs = File.Create(tmp))
                    new XmlSerializer(typeof(Settings)).Serialize(fs, this);
                // Replace вместо «удалить и переименовать»: между теми двумя строками
                // настроек не существовало ни под одним именем, и падение или пропажа
                // питания ровно там стирали их целиком — человек получал умолчания.
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch { }
        }

        private void Normalize()
        {
            if (FKeys == null || FKeys.Length != MaxFKeys)
            {
                string[] def = DefaultFKeys();
                string[] fixedUp = new string[MaxFKeys];
                for (int i = 0; i < MaxFKeys; i++)
                    fixedUp[i] = (FKeys != null && i < FKeys.Length && FKeys[i] != null) ? FKeys[i] : def[i];
                FKeys = fixedUp;
            }
            if (JisKeys == null || JisKeys.Length != 5)
            {
                string[] j = new string[5];
                for (int i = 0; i < 5; i++)
                    j[i] = (JisKeys != null && i < JisKeys.Length && JisKeys[i] != null) ? JisKeys[i] : "none";
                JisKeys = j;
            }
            if (LayoutBindings == null) LayoutBindings = new LayoutBinding[0];
            if (BrightnessStep < 1) BrightnessStep = 1;
            if (BrightnessStep > 25) BrightnessStep = 25;
        }
    }

    /// <summary>Запуск вместе с Windows — обычная запись в разделе Run текущего пользователя.</summary>
    internal static class Autostart
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "MagicKeys";

        public static bool Enabled
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key, false))
                        return k != null && k.GetValue(Name) != null;
                }
                catch { return false; }
            }
        }

        public static void Set(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    if (k == null) return;
                    if (on)
                    {
                        string exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                        k.SetValue(Name, "\"" + exe + "\" --tray");
                    }
                    else if (k.GetValue(Name) != null)
                    {
                        k.DeleteValue(Name, false);
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>Человеческие названия модификаторов, как они подписаны на клавиатуре Apple.</summary>
    internal static class ModNames
    {
        private static readonly Dictionary<ModKey, string> Map = new Dictionary<ModKey, string>();

        static ModNames()
        {
            Map[ModKey.None] = "Выключить клавишу";
            Map[ModKey.LCtrl] = "Ctrl (левый)";
            Map[ModKey.RCtrl] = "Ctrl (правый)";
            Map[ModKey.LWin] = "Win (левая)";
            Map[ModKey.RWin] = "Win (правая)";
            Map[ModKey.LAlt] = "Alt (левый)";
            Map[ModKey.RAlt] = "Alt (правый)";
            Map[ModKey.LShift] = "Shift (левый)";
            Map[ModKey.RShift] = "Shift (правый)";
            Map[ModKey.CapsLock] = "Caps Lock";
            Map[ModKey.Escape] = "Escape";
        }

        public static string Of(ModKey k)
        {
            string s;
            return Map.TryGetValue(k, out s) ? s : k.ToString();
        }

        public static int VirtualKey(ModKey k)
        {
            switch (k)
            {
                case ModKey.LCtrl: return Vk.LControl;
                case ModKey.RCtrl: return Vk.RControl;
                case ModKey.LWin: return Vk.LWin;
                case ModKey.RWin: return Vk.RWin;
                case ModKey.LAlt: return Vk.LMenu;
                case ModKey.RAlt: return Vk.RMenu;
                case ModKey.LShift: return Vk.LShift;
                case ModKey.RShift: return Vk.RShift;
                case ModKey.CapsLock: return Vk.Capital;
                case ModKey.Escape: return Vk.Escape;
                default: return 0;
            }
        }
    }
}
