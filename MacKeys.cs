// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;

namespace MagicKeys
{
    /// <summary>Какие модификаторы Apple должны быть зажаты.</summary>
    [Flags]
    internal enum MacMod
    {
        None = 0,
        Cmd = 1,     // ⌘ — приходит как клавиша Windows
        Opt = 2,     // ⌥ — приходит как Alt
        Shift = 4,   // ⇧
        Ctrl = 8     // control
    }

    /// <summary>Одно сочетание: что нажато на клавиатуре Apple и что послать в Windows.</summary>
    internal sealed class MacShortcut
    {
        public string Id;        // устойчивый ключ для настроек
        public string Group;     // раздел в окне
        public string Title;     // что оно делает по-человечески
        public string Mac;       // как это выглядит на маке
        public string Win;       // во что превращается
        public MacMod Mods;      // что должно быть зажато
        public int Vk;           // какая клавиша нажата
        public int Vk2;          // и её же второй код, если клавиша приходит по-разному
        public int[] SendMods;   // что послать зажатым
        public int SendVk;       // и какую клавишу
        public int[] Then;       // второе нажатие, если одного мало
        public int[] ThenMods;
        public bool Repeats;     // повторять ли при удержании
    }

    /// <summary>
    /// Сочетания macOS в Windows. Смысл в том, чтобы человек, пришедший с мака,
    /// не переучивал пальцы: ⌘C копирует, ⌘← уходит в начало строки, ⌥← прыгает
    /// через слово.
    ///
    /// Почему перевод каждого сочетания, а не простой обмен ⌘ и control: обменом
    /// выражается только половина. ⌘← и ⌘⌫ в Windows соответствуют вовсе не
    /// Ctrl+←, а Home и Shift+Home с последующим Backspace; ⌘Q — это Alt+F4.
    /// Такие вещи заменой клавиш не получить, поэтому здесь таблица.
    /// </summary>
    internal static class MacKeys
    {
        public const string GroupEdit = "Правка";
        public const string GroupText = "Текст";
        public const string GroupSystem = "Система";

        private static readonly List<MacShortcut> _all = new List<MacShortcut>();

        public static IList<MacShortcut> All { get { return _all; } }

        static MacKeys()
        {
            // ---------------- правка: ⌘ вместо Ctrl ----------------
            Edit("copy", "Копировать", "⌘C", Vk.C, Vk.C);
            Edit("paste", "Вставить", "⌘V", Vk.V, Vk.V);
            Edit("cut", "Вырезать", "⌘X", Vk.X, Vk.X);
            // Отмену и повтор держат, чтобы откатиться на несколько шагов, — им повтор нужен.
            Edit("undo", "Отменить", "⌘Z", Vk.Z, Vk.Z).Repeats = true;
            Add("redo", GroupEdit, "Повторить", "⇧⌘Z", "Ctrl+Y",
                MacMod.Cmd | MacMod.Shift, Vk.Z, new int[] { Vk.LControl }, Vk.Y).Repeats = true;
            Edit("selectall", "Выделить всё", "⌘A", Vk.A, Vk.A);
            Edit("save", "Сохранить", "⌘S", Vk.S, Vk.S);
            Edit("find", "Найти", "⌘F", Vk.F, Vk.F);
            Add("findnext", GroupEdit, "Найти далее", "⌘G", "F3",
                MacMod.Cmd, Vk.G, new int[0], Vk.F3);
            Add("findprev", GroupEdit, "Найти назад", "⇧⌘G", "Shift+F3",
                MacMod.Cmd | MacMod.Shift, Vk.G, new int[] { Vk.LShift }, Vk.F3);
            Edit("print", "Печать", "⌘P", Vk.P, Vk.P);
            Edit("new", "Создать", "⌘N", Vk.N, Vk.N);
            Edit("open", "Открыть", "⌘O", Vk.O, Vk.O);
            Edit("close", "Закрыть вкладку", "⌘W", Vk.W, Vk.W);
            Edit("newtab", "Новая вкладка", "⌘T", Vk.T, Vk.T);
            Add("reopentab", GroupEdit, "Вернуть вкладку", "⇧⌘T", "Ctrl+Shift+T",
                MacMod.Cmd | MacMod.Shift, Vk.T, new int[] { Vk.LControl, Vk.LShift }, Vk.T);
            Edit("reload", "Обновить", "⌘R", Vk.R, Vk.R);
            Edit("address", "Адресная строка", "⌘L", Vk.L, Vk.L);
            Edit("bold", "Полужирный", "⌘B", Vk.B, Vk.B);
            Edit("italic", "Курсив", "⌘I", Vk.I, Vk.I);

            // ---------------- текст: то, чего заменой клавиш не получить ----------------
            Nav("linestart", "В начало строки", "⌘←", MacMod.Cmd, Vk.Left, new int[0], Vk.Home);
            Nav("lineend", "В конец строки", "⌘→", MacMod.Cmd, Vk.Right, new int[0], Vk.End);
            Nav("docstart", "В начало документа", "⌘↑", MacMod.Cmd, Vk.Up, new int[] { Vk.LControl }, Vk.Home);
            Nav("docend", "В конец документа", "⌘↓", MacMod.Cmd, Vk.Down, new int[] { Vk.LControl }, Vk.End);

            Nav("sellinestart", "Выделить до начала строки", "⇧⌘←",
                MacMod.Cmd | MacMod.Shift, Vk.Left, new int[] { Vk.LShift }, Vk.Home);
            Nav("sellineend", "Выделить до конца строки", "⇧⌘→",
                MacMod.Cmd | MacMod.Shift, Vk.Right, new int[] { Vk.LShift }, Vk.End);
            Nav("seldocstart", "Выделить до начала документа", "⇧⌘↑",
                MacMod.Cmd | MacMod.Shift, Vk.Up, new int[] { Vk.LControl, Vk.LShift }, Vk.Home);
            Nav("seldocend", "Выделить до конца документа", "⇧⌘↓",
                MacMod.Cmd | MacMod.Shift, Vk.Down, new int[] { Vk.LControl, Vk.LShift }, Vk.End);

            Nav("wordleft", "На слово влево", "⌥←", MacMod.Opt, Vk.Left, new int[] { Vk.LControl }, Vk.Left);
            Nav("wordright", "На слово вправо", "⌥→", MacMod.Opt, Vk.Right, new int[] { Vk.LControl }, Vk.Right);
            Nav("selwordleft", "Выделить слово слева", "⇧⌥←",
                MacMod.Opt | MacMod.Shift, Vk.Left, new int[] { Vk.LControl, Vk.LShift }, Vk.Left);
            Nav("selwordright", "Выделить слово справа", "⇧⌥→",
                MacMod.Opt | MacMod.Shift, Vk.Right, new int[] { Vk.LControl, Vk.LShift }, Vk.Right);

            Nav("delword", "Удалить слово слева", "⌥Backspace", MacMod.Opt, Vk.Back, new int[] { Vk.LControl }, Vk.Back);
            Nav("delwordfwd", "Удалить слово справа", "⌥Delete",
                MacMod.Opt, Vk.Delete, new int[] { Vk.LControl }, Vk.Delete);

            // ⌘⌫ на маке стирает до начала строки. В Windows одного нажатия мало:
            // сначала выделяем до начала строки, потом стираем выделенное.
            MacShortcut delLine = Add("delline", GroupText, "Удалить до начала строки", "⌘Backspace",
                "Shift+Home, затем Backspace",
                MacMod.Cmd, Vk.Back, new int[] { Vk.LShift }, Vk.Home);
            delLine.ThenMods = new int[0];
            delLine.Then = new int[] { Vk.Back };

            // ---------------- система ----------------
            Add("quit", GroupSystem, "Закрыть программу", "⌘Q", "Alt+F4",
                MacMod.Cmd, Vk.Q, new int[] { Vk.LMenu }, Vk.F4);
            Add("minimize", GroupSystem, "Свернуть окно", "⌘M", "Win+↓",
                MacMod.Cmd, Vk.M, new int[] { Vk.LWin }, Vk.Down);
            Add("hide", GroupSystem, "Свернуть всё", "⌘H", "Win+D",
                MacMod.Cmd, Vk.H, new int[] { Vk.LWin }, Vk.D);
            // ⌘Space и ⌃Space — см. SpaceAction: там один вопрос на обе клавиши.
            Add("taskmgr", GroupSystem, "Диспетчер задач", "⌥⌘Esc", "Ctrl+Shift+Esc",
                MacMod.Cmd | MacMod.Opt, Vk.Escape, new int[] { Vk.LControl, Vk.LShift }, Vk.Escape);
            Add("lock", GroupSystem, "Заблокировать экран", "⌃⌘Q", "Win+L",
                MacMod.Cmd | MacMod.Ctrl, Vk.Q, new int[] { Vk.LWin }, Vk.L);
            Add("screenfull", GroupSystem, "Снимок экрана", "⇧⌘3", "Print Screen",
                MacMod.Cmd | MacMod.Shift, Vk.D3, new int[0], Vk.Snapshot);
            Add("screenarea", GroupSystem, "Снимок области", "⇧⌘4", "Win+Shift+S",
                MacMod.Cmd | MacMod.Shift, Vk.D4, new int[] { Vk.LWin, Vk.LShift }, Vk.S);
            // ⌘, на маке открывает настройки ТЕКУЩЕЙ программы, а не системы. Win+I
            // открывал «Параметры Windows» и заодно съедал родное сочетание там, где
            // оно есть, — в редакторах кода это Ctrl+, ровно с тем же смыслом.
            Add("settings", GroupSystem, "Настройки программы", "⌘,", "Ctrl+,",
                MacMod.Cmd, Vk.OemComma, new int[] { Vk.LControl }, Vk.OemComma);
            Add("screenrec", GroupSystem, "Запись экрана", "⇧⌘5", "Win+Alt+R",
                MacMod.Cmd | MacMod.Shift, Vk.D5, new int[] { Vk.LWin, Vk.LMenu }, Vk.R);
            // На маке ⌘` листает окна текущей программы; в Windows такого сочетания нет,
            // и Alt+Esc листает окна всех подряд. Называем тем, что получается, а не тем,
            // что было на маке. Клавиша заведена дважды: на ANSI обратный апостроф слева
            // от «1», на ISO — слева от «Z», и приходит другим кодом.
            // Одна строка, два кода. Двумя строками список показывал две неразличимые
            // на вид записи с одной подписью, и выключив «Следующее окно», человек
            // оставлял второе включённым — не понимая, почему сочетание всё ещё работает.
            Add("windownext", GroupSystem, "Следующее окно", "⌘`", "Alt+Esc",
                MacMod.Cmd, Vk.Oem3, new int[] { Vk.LMenu }, Vk.Escape).Vk2 = Vk.Oem102;

            // Листание вкладок. На маке это ⌘⌥←/→, в Windows — Ctrl+PgUp/PgDn.
            // ⌘W и ⌘T уже были, а перехода между вкладками не хватало заметнее всего:
            // в браузере это замечают в первые полчаса.
            Add("tabprev", GroupEdit, "Предыдущая вкладка", "⌘⌥←", "Ctrl+PgUp",
                MacMod.Cmd | MacMod.Opt, Vk.Left, new int[] { Vk.LControl }, Vk.Prior);
            Add("tabnext", GroupEdit, "Следующая вкладка", "⌘⌥→", "Ctrl+PgDn",
                MacMod.Cmd | MacMod.Opt, Vk.Right, new int[] { Vk.LControl }, Vk.Next);

            // Вкладка по номеру: ⌘1…⌘9. На маке это одно из самых пальцевых движений,
            // и в Windows оно есть ровно такое же — Ctrl с той же цифрой.
            int[] digits = { Vk.D1, Vk.D2, Vk.D3, Vk.D4, Vk.D5, Vk.D6, Vk.D7, Vk.D8, Vk.D9 };
            for (int i = 0; i < digits.Length; i++)
                Add("tab" + (i + 1), GroupEdit, "Вкладка " + (i + 1), "⌘" + (i + 1), "Ctrl+" + (i + 1),
                    MacMod.Cmd, digits[i], new int[] { Vk.LControl }, digits[i]);

            // Масштаб. В браузере замечают за первый час.
            // «+» на клавише набирается с Shift, поэтому настоящее ⇧⌘+ приходит
            // с двумя модификаторами. Подпись обещала ⌘+, а работало только ⌘=.
            Add("zoomin", GroupEdit, "Крупнее", "⌘=", "Ctrl+=",
                MacMod.Cmd, Vk.OemPlus, new int[] { Vk.LControl }, Vk.OemPlus).Repeats = true;
            Add("zoominshift", GroupEdit, "Крупнее", "⇧⌘+", "Ctrl+=",
                MacMod.Cmd | MacMod.Shift, Vk.OemPlus, new int[] { Vk.LControl }, Vk.OemPlus).Repeats = true;
            Add("zoomout", GroupEdit, "Мельче", "⌘−", "Ctrl+−",
                MacMod.Cmd, Vk.OemMinus, new int[] { Vk.LControl }, Vk.OemMinus).Repeats = true;
            Add("zoomreset", GroupEdit, "Обычный размер", "⌘0", "Ctrl+0",
                MacMod.Cmd, Vk.D0, new int[] { Vk.LControl }, Vk.D0);
        }

        private static MacShortcut Edit(string id, string title, string mac, int vk, int sendVk)
        {
            return Add(id, GroupEdit, title, mac, "Ctrl+" + Vk.Name(sendVk),
                MacMod.Cmd, vk, new int[] { Vk.LControl }, sendVk);
        }

        /// <summary>
        /// Движение по тексту. Такие сочетания повторяются при удержании — на маке
        /// зажатое ⌥← идёт по словам, и здесь должно идти так же. Всё остальное
        /// (закрыть, открыть, снять снимок) срабатывает один раз на нажатие.
        /// </summary>
        private static void Nav(string id, string title, string mac, MacMod mods, int vk,
                                int[] sendMods, int sendVk)
        {
            Add(id, GroupText, title, mac, Describe(sendMods, sendVk), mods, vk, sendMods, sendVk)
                .Repeats = true;
        }

        private static MacShortcut Add(string id, string group, string title, string mac, string win,
                                       MacMod mods, int vk, int[] sendMods, int sendVk)
        {
            var s = new MacShortcut
            {
                Id = id, Group = group, Title = title, Mac = mac, Win = win,
                Mods = mods, Vk = vk, SendMods = sendMods, SendVk = sendVk
            };
            _all.Add(s);
            return s;
        }

        private static string Describe(int[] mods, int vk)
        {
            string s = "";
            foreach (int m in mods)
            {
                if (m == Vk.LControl) s += "Ctrl+";
                else if (m == Vk.LShift) s += "Shift+";
                else if (m == Vk.LMenu) s += "Alt+";
                else if (m == Vk.LWin) s += "Win+";
            }
            return s + Vk.Name(vk);
        }

        /// <summary>
        /// Пробел с модификатором. На маке привычки расходятся: у одних ⌘Space
        /// открывает поиск, у других переключает язык, а поиск живёт на ⌃Space.
        /// Навязывать один вариант неправильно, поэтому человек выбирает, на какой
        /// из двух клавиш живёт поиск; второй достаётся язык. Здесь только перевод
        /// роли в нажатие.
        /// </summary>
        public static MacShortcut SpaceAction(string role)
        {
            if (role == "search")
                return new MacShortcut
                {
                    Id = "space.search", Group = GroupSystem, Title = "Поиск", Win = "Win+S",
                    SendMods = new int[] { Vk.LWin }, SendVk = Vk.S
                };
            if (role == "language")
                return new MacShortcut
                {
                    Id = "space.language", Group = GroupSystem, Title = "Переключить язык", Win = "Win+Space",
                    SendMods = new int[] { Vk.LWin }, SendVk = Vk.Space
                };
            return null;
        }

        /// <summary>
        /// ⌘ с обычной клавишей, которой в таблице нет: то же самое, но с control.
        ///
        /// Без этого правила ⌘ вне таблицы не делала ничего. Перехват такие нажатия
        /// глотает — и правильно делает: у пришедшего с мака ⌘L означает «строка адреса»,
        /// а Windows на Win+L запирает экран. Но глотать — не значит выбрасывать:
        /// ⌘K, ⌘D, ⌘E, ⌘J, ⌘U, ⌘Y и весь слой ⇧⌘ оказывались мёртвыми, хотя в Windows
        /// им отвечает ровно то же с control. Таблица остаётся для того, чего заменой
        /// клавиши не получить (⌘←, ⌘⌫, ⌘Q), а всё прочее переводится по правилу.
        ///
        /// ⌥ и ⌃ вместе с ⌘ сюда не попадают: на маке ⌥ чаще набирает символ, чем
        /// участвует в команде, и переводить его наугад — обещать то, чего не знаем.
        /// </summary>
        public static MacShortcut Generic(int vk, MacMod mods)
        {
            if ((mods & MacMod.Cmd) == 0) return null;
            if ((mods & (MacMod.Opt | MacMod.Ctrl)) != 0) return null;
            if (!Plain(vk)) return null;

            var send = new List<int>();
            send.Add(Vk.LControl);
            if ((mods & MacMod.Shift) != 0) send.Add(Vk.LShift);

            var s = new MacShortcut();
            s.Id = "generic";
            s.Group = GroupEdit;
            s.Title = "То же самое с control";
            s.Mods = mods;
            s.Vk = vk;
            s.SendMods = send.ToArray();
            s.SendVk = vk;
            // Не повторяется. Про клавишу вне таблицы мы не знаем, осмыслен ли для неё
            // повтор, а цена ошибки несимметрична: лишний ⇧⌘N — это два десятка новых
            // окон, а пропущенный повтор человек просто нажмёт ещё раз.
            s.Repeats = false;
            return s;
        }

        /// <summary>Обычная клавиша: буква, цифра или знак. Не модификатор, не F, не стрелка.</summary>
        private static bool Plain(int vk)
        {
            if (vk >= 0x30 && vk <= 0x39) return true;   // 0-9
            if (vk >= 0x41 && vk <= 0x5A) return true;   // A-Z
            if (vk >= 0xBA && vk <= 0xC0) return true;   // ; = , - . / `
            if (vk >= 0xDB && vk <= 0xDF) return true;   // [ \ ] ковычка
            return vk == 0xE2;                           // клавиша ISO между Shift и Z
        }

        /// <summary>Ищет сочетание по нажатой клавише и зажатым модификаторам Apple.</summary>
        public static MacShortcut Find(int vk, MacMod mods)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                MacShortcut s = _all[i];
                if ((s.Vk == vk || (s.Vk2 != 0 && s.Vk2 == vk)) && s.Mods == mods) return s;
            }
            return null;
        }

        /// <summary>Отправляет то, во что сочетание переводится.</summary>
        public static void Send(MacShortcut s)
        {
            Input.Chord(s.SendMods, s.SendVk);
            if (s.Then != null)
                foreach (int vk in s.Then) Input.Chord(s.ThenMods == null ? new int[0] : s.ThenMods, vk);
        }
    }
}
