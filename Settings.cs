// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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

        /// <summary>
        /// Для какого поколения подобран набор в FKeys. Нужно, чтобы подставить
        /// заводские назначения самой — но только пока человек их не трогал.
        /// </summary>
        public AppleGen FKeysGen = AppleGen.Unknown;

        public const string ChannelStable = "stable";
        public const string ChannelDev = "dev";

        /// <summary>Какие выпуски предлагать: только готовые или ранние тоже.</summary>
        public string UpdateChannel = ChannelStable;

        /// <summary>Когда в последний раз спрашивали GitHub. Пусто — ни разу.</summary>
        public string UpdateChecked = "";

        /// <summary>Клавиша ⌧ на цифровом блоке: у Apple это «clear», Windows видит Num Lock.</summary>
        public string NumpadClear = "key.delete";

        /// <summary>Клавиша «=» на цифровом блоке: Windows её не понимает и по умолчанию молчит.</summary>


        /// <summary>Клавиша ⏏ — приходит не как клавиша, а отчётом медиастраницы.</summary>
        public string EjectKey = "none";

        /// <summary>Сколько функциональных клавиш замечено у клавиатуры Apple на самом деле.</summary>


        public ModKey MapLCtrl = ModKey.LCtrl;
        public ModKey MapLWin = ModKey.LWin;
        public ModKey MapLAlt = ModKey.LAlt;
        public ModKey MapRAlt = ModKey.RAlt;
        public ModKey MapRWin = ModKey.RWin;
        public ModKey MapCapsLock = ModKey.CapsLock;

        /// <summary>Поменять местами клавишу слева от «1» и клавишу слева от «Z».</summary>


        /// <summary>Исполнение клавиатуры. «Auto» — определять по нажатиям.</summary>
        public PhysLayout Physical = PhysLayout.Auto;

        /// <summary>Воспроизводить раскладки macOS: то, что напечатано на клавишах Apple.</summary>
        public bool AppleLayoutEnabled = false;

        /// <summary>Каким ⌥ набирается третий уровень (символы, напечатанные на клавише третьими).</summary>
        public OptLevel OptLevel = OptLevel.RightOption;

        /// <summary>Подменять все клавиши раскладки, а не только отличающиеся от раскладки Microsoft.</summary>


        /// <summary>Какая раскладка Apple применяется к какому языку ввода Windows.</summary>
        public LayoutBinding[] LayoutBindings = new LayoutBinding[0];

        /// <summary>Навигация как в macOS: Fn+стрелки, Fn+Backspace, Fn+Enter.</summary>
        public bool FnNavigation = true;

        /// <summary>⌘+Tab переключает окна так же, как Alt+Tab.</summary>
        // Включено: это первое, что человек с мака жмёт не глядя, а выключенным оно
        // давало молчание. Техническая причина держать его выключенным отпала, когда
        // перед отпусканием Win стали подсовывать незанятый код — иначе выскакивал «Пуск».
        public bool CmdTabSwitchesWindows = true;

        /// <summary>Переводить сочетания macOS в виндовые: ⌘C, ⌘←, ⌘Q и прочие.</summary>
        // Включены сразу: ради них программа и переделывалась. Выключенными по умолчанию
        // они означали, что пришедший с мака при первом запуске не получает ничего.
        public bool MacShortcuts = true;

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






        public bool StartMinimized = false;

        /// <summary>F1…F24 — больше в Windows не бывает.</summary>
        /// <summary>
        /// Шаг яркости. Постоянная, а не настройка: три значения одного вкуса, и кому
        /// десять процентов много — нажмёт один раз вместо двух.
        /// </summary>
        public const int BrightnessStep = 10;

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

        /// <summary>Какая раскладка Apple назначена этому языку ввода Windows.</summary>
        /// <summary>
        /// Какая раскладка Apple отвечает за этот язык ввода Windows.
        ///
        /// Сначала то, что человек выбрал руками. Если не выбирал — подбираем сами
        /// по коду языка: файлы названы теми же кодами, что и культуры Windows
        /// (ru.xml, de.xml, en-GB.xml), и совпадение здесь не догадка, а тождество.
        /// Прежде это была таблица из тридцати с лишним строк, которую человек сводил
        /// вручную, — и, включив раскладки Apple, он не получал ничего и не понимал
        /// почему: по умолчанию не было привязано ни одного языка.
        /// </summary>
        public string LayoutFor(int langId)
        {
            string hex = langId.ToString("X4");
            if (LayoutBindings != null)
                foreach (LayoutBinding b in LayoutBindings)
                    if (b != null && String.Equals(b.Lang, hex, StringComparison.OrdinalIgnoreCase))
                        return String.IsNullOrEmpty(b.Layout) ? null : b.Layout;
            return GuessLayout(langId);
        }

        /// <summary>Раскладка по коду языка: сперва точная («en-GB»), потом общая («en»).</summary>
        public static string GuessLayout(int langId)
        {
            try
            {
                CultureInfo c = CultureInfo.GetCultureInfo(langId & 0xFFFF);
                if (Layouts.ById(c.Name) != null) return c.Name;
                string two = c.TwoLetterISOLanguageName;
                if (Layouts.ById(two) != null) return two;
            }
            catch { /* язык, которого нет в списке культур, — просто не подбираем */ }
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

        /// <summary>
        /// Снимок для тех, кто читает настройки с чужого потока.
        ///
        /// Живой объект настроек правит окно — прямо в полях, по одному на каждое
        /// движение галочки. А поток перехвата читает их из обработчика на каждое
        /// нажатие, без всякой синхронизации. Пока каждое поле умещается в машинное
        /// слово, это сходит с рук: чтение отдаёт либо старое значение, либо новое,
        /// и оба осмысленны. Но правило это нигде не записано, а держится оно на
        /// свойстве, которого никто не обещал. Первое же составное значение — список
        /// или пара полей, которые обязаны меняться вместе, — сломается молча и редко,
        /// то есть самым дорогим способом.
        ///
        /// Поэтому потокам, кроме оконного, достаётся не сам объект, а его копия,
        /// и меняется она только целиком: Engine.Apply кладёт новый снимок вместо
        /// старого. Внутри снимка ничего не правят — он существует, чтобы его читали.
        /// </summary>
        public Settings Snapshot()
        {
            Settings s = (Settings)MemberwiseClone();

            // Массивы копируем перебором полей, а не поимённо. Поимённый список — ловушка
            // ровно того рода, ради которой снимок и заводился: следующее добавленное поле
            // разделилось бы между окном и потоком перехвата молча, компилятор бы смолчал,
            // а разошлось бы редко и на живой машине.
            foreach (FieldInfo f in Fields())
            {
                var array = f.GetValue(this) as Array;
                if (array == null) continue;

                var copy = (Array)array.Clone();
                // Массив ссылок копируем вглубь: иначе поделили бы сами элементы.
                if (!f.FieldType.GetElementType().IsValueType && f.FieldType.GetElementType() != typeof(string))
                    for (int i = 0; i < copy.Length; i++)
                    {
                        var b = copy.GetValue(i) as LayoutBinding;
                        if (b != null) copy.SetValue(new LayoutBinding { Lang = b.Lang, Layout = b.Layout }, i);
                    }
                f.SetValue(s, copy);
            }
            return s;
        }

        private static FieldInfo[] _fields;

        /// <summary>
        /// Поля настроек — один раз и навсегда. Заодно проверка: ссылочное поле, которое
        /// не строка, не массив и не перечисление, снимок не умеет копировать, и о таком
        /// надо узнать сразу, а не через месяц по странному поведению.
        /// </summary>
        private static FieldInfo[] Fields()
        {
            if (_fields != null) return _fields;
            FieldInfo[] all = typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (FieldInfo f in all)
            {
                Type t = f.FieldType;
                if (t.IsValueType || t == typeof(string) || t.IsArray) continue;
                Diag.Log("снимок настроек не умеет копировать поле " + f.Name + " типа " + t.Name +
                         " — оно будет общим с окном");
            }
            _fields = all;
            return all;
        }

        // ---------- обновления ----------
        //
        // Канал и отметка живут в тех же настройках, но правит их окно «О программе»
        // напрямую, а не через снимок: к перехвату они отношения не имеют, и читает
        // их только тот же поток окна.

        public static string Channel
        {
            get { Settings s = Current; return s == null ? ChannelStable : s.UpdateChannel; }
            set
            {
                Settings s = Current;
                if (s == null) return;
                s.UpdateChannel = value;
                s.Save();
            }
        }

        public static DateTime? LastUpdateCheck
        {
            get
            {
                Settings s = Current;
                if (s == null || String.IsNullOrEmpty(s.UpdateChecked)) return null;
                DateTime t;
                return DateTime.TryParse(s.UpdateChecked, CultureInfo.InvariantCulture,
                                         DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)
                    ? (DateTime?)t : null;
            }
            set
            {
                Settings s = Current;
                if (s == null) return;
                s.UpdateChecked = value == null ? "" : value.Value.ToString("o", CultureInfo.InvariantCulture);
                s.Save();
            }
        }

        /// <summary>
        /// Живой объект настроек. Заводится при загрузке; нужен обновлению, которое
        /// работает на потоке окна и правит свои два поля напрямую.
        /// </summary>
        public static Settings Current;

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

        /// <summary>Запомнить объект как живой — его правит окно.</summary>
        public void MakeCurrent() { Current = this; }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string tmp = FilePath + ".tmp";
                using (FileStream fs = File.Create(tmp))
                {
                    new XmlSerializer(typeof(Settings)).Serialize(fs, this);
                    // Сбрасываем на диск до подмены: иначе от пропажи питания Replace
                    // не спасает — имя уже новое, а содержимого ещё нет.
                    fs.Flush(true);
                }

                // Replace вместо «удалить и переименовать»: между теми двумя строками
                // настроек не существовало ни под одним именем, и падение ровно там
                // стирало их целиком — человек получал умолчания.
                if (!File.Exists(FilePath)) File.Move(tmp, FilePath);
                else
                {
                    try { File.Replace(tmp, FilePath, null); }
                    catch (Exception e)
                    {
                        // ReplaceFile поддерживают не все файловые системы, а %AppData%
                        // политикой могут увести на сетевой ресурс. Отступаем на старый
                        // способ: он хуже, но молча перестать сохранять настройки —
                        // хуже во много раз.
                        Diag.Log("настройки: подмена файла не удалась, сохраняю по-старому", e);
                        File.Delete(FilePath);
                        File.Move(tmp, FilePath);
                    }
                }
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
            if (LayoutBindings == null) LayoutBindings = new LayoutBinding[0];
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
