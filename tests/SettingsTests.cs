// Стенд настроек: каждую настройку включают, шлют в перехват нажатия и смотрят,
// что он решил отдать Windows. Ввод никуда не уходит — Input.Sink его записывает.
//
// Что здесь считается шумом. Когда зажата клавиша ⌘ (у пришедших с мака это Windows),
// перехват перед посылкой отпускает то, что держит сам, и жмёт незанятый код 0xE8 —
// иначе Windows примет одинокую Win за команду и откроет «Пуск». Это задумано, поэтому
// проверяем не побуквенное совпадение, а что нужные события пришли по порядку.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MagicKeys
{
    static class SettingsTests
    {
        const int VkNoop = 0xE8;

        static Engine _eng;
        static MethodInfo _handle, _release;
        static readonly List<string> _log = new List<string>();
        static int _pass, _fail;
        static readonly List<string> _fails = new List<string>();

        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            // Стенд собирается в папку программы: раскладки программа ищет рядом с собой,
            // и собранный на стороне он не нашёл бы ни одной.
            Input.Sink = Record;
            _eng = new Engine();
            _handle = typeof(Engine).GetMethod("Handle", BindingFlags.NonPublic | BindingFlags.Instance);
            _release = typeof(Engine).GetMethod("ReleaseEverything", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_handle == null || _release == null) { Console.WriteLine("нет Handle/ReleaseEverything"); return 2; }

            Enabled();
            PauseWhenAppleAbsent();
            MediaFirst();
            FnSubstituteAll();
            FKeysActions();
            NumpadClear();
            EjectKey();
            Modifiers();
            FnNavigation();
            CmdTab();
            MacShortcuts();
            SpaceRoles();
            AppleLayout();
            ModifierChangedMidPress();
            OneOwnerPerKey();
            SnapshotIndependence();
            LayoutGuess();
            OtherSettings();
            Tables();
            SnapshotByReflection();
            BrokenFile();

            Console.WriteLine();
            Console.WriteLine("прошло " + _pass + ", провалено " + _fail);
            foreach (string f in _fails) Console.WriteLine("  ПРОВАЛ: " + f);
            return _fail == 0 ? 0 : 1;
        }

        // ---------- стенд ----------

        static void Record(Native.INPUT[] items)
        {
            foreach (Native.INPUT i in items)
            {
                Native.KEYBDINPUT k = i.u.ki;
                bool up = (k.dwFlags & Native.KEYEVENTF_KEYUP) != 0;
                string what;
                if ((k.dwFlags & Native.KEYEVENTF_UNICODE) != 0) what = "'" + (char)k.wScan + "'";
                else if ((k.dwFlags & Native.KEYEVENTF_SCANCODE) != 0) what = "scan" + k.wScan.ToString("X2");
                else if (k.wVk == VkNoop) what = "щит";
                else what = Vk.Name(k.wVk);
                _log.Add(what + (up ? "^" : "v"));
            }
        }

        static Settings Fresh()
        {
            Settings s = new Settings();
            s.PauseWhenAppleAbsent = false;   // клавиатуры на стенде нет
            return s;
        }

        static void Use(Settings s)
        {
            _release.Invoke(_eng, null);
            _eng.Apply(s);
            _log.Clear();
        }

        static void Apple(bool on)
        {
            typeof(Devices).GetField("_appleConnected", BindingFlags.NonPublic | BindingFlags.Static)
                           .SetValue(null, on);
        }

        static bool Send(int vk, bool down, bool ext)
        {
            return Send(vk, (int)Native.MapVirtualKeyW((uint)vk, Native.MAPVK_VK_TO_VSC), down, ext);
        }

        /// <summary>То же, но со своим скан-кодом: по нему разбираются перестановка ISO и «=».</summary>
        static bool Send(int vk, int scan, bool down, bool ext)
        {
            Native.KBDLLHOOKSTRUCT k = new Native.KBDLLHOOKSTRUCT();
            k.vkCode = (uint)vk;
            k.scanCode = (uint)scan;
            k.flags = ext ? Native.LLKHF_EXTENDED : 0u;
            return (bool)_handle.Invoke(_eng, new object[] { down ? Native.WM_KEYDOWN : Native.WM_KEYUP, k });
        }

        static bool Down(int vk) { return Send(vk, true, false); }
        static bool Up(int vk) { return Send(vk, false, false); }
        static bool DownE(int vk) { return Send(vk, true, true); }
        static bool UpE(int vk) { return Send(vk, false, true); }

        static string N(int vk) { return Vk.Name(vk); }
        static string Sent() { return String.Join(" ", _log.ToArray()); }
        static void Clear() { _log.Clear(); }

        static void Check(string name, bool ok, string got)
        {
            if (ok) { _pass++; Console.WriteLine("  + " + name); }
            else { _fail++; _fails.Add(name + " — " + got); Console.WriteLine("  ! " + name + " — " + got); }
        }

        /// <summary>Нажатие и отпускание клавиши-действия: ровно вот это и ничего больше.</summary>
        static void Tapped(string name, bool swallowed, int vk)
        {
            string want = N(vk) + "v " + N(vk) + "^";
            Check(name, swallowed && Sent() == want, "ждали «" + want + "», пришло «" + Sent() + "»");
            Clear();
        }

        /// <summary>Ничего не отправлено и ничего не проглочено — нажатие ушло как есть.</summary>
        static void Untouched(string name, bool swallowed)
        {
            string rest = Sent().Replace("щитv", "").Replace("щит^", "").Trim();
            Check(name, !swallowed && rest == "", "проглочено=" + swallowed + ", пришло «" + Sent() + "»");
            Clear();
        }

        /// <summary>Эти события должны прийти в этом порядке (между ними может быть щит).</summary>
        static void Seq(string name, bool swallowed, params string[] want)
        {
            int at = 0;
            foreach (string ev in _log)
                if (at < want.Length && ev == want[at]) at++;
            Check(name, swallowed && at == want.Length,
                  "не нашли «" + String.Join(" ", want) + "», пришло «" + Sent() + "»");
            Clear();
        }

        /// <summary>Клавишу проглотили и выставили только щит: ⌘ не должна звать Windows.</summary>
        static void Shielded(string name, bool swallowed)
        {
            string rest = Sent().Replace("щитv", "").Replace("щит^", "").Trim();
            Check(name, swallowed && rest == "", "проглочено=" + swallowed + ", пришло «" + Sent() + "»");
            Clear();
        }

        static void Head(string t) { Console.WriteLine(); Console.WriteLine("== " + t + " =="); }

        // ---------- настройки ----------

        static void Enabled()
        {
            Head("Переназначения включены");
            Settings s = Fresh();
            s.MapCapsLock = ModKey.LCtrl;
            Use(s);
            bool sw = Down(Vk.Capital); Up(Vk.Capital);
            Tapped("включено: Caps Lock подменяется", sw, Vk.LControl);

            s = Fresh(); s.MapCapsLock = ModKey.LCtrl; s.Enabled = false;
            Use(s);
            sw = Down(Vk.Capital); Up(Vk.Capital);
            Untouched("выключено: не трогает ничего", sw);
        }

        static void PauseWhenAppleAbsent()
        {
            Head("Приостанавливать без Magic Keyboard");
            Settings s = new Settings();
            s.PauseWhenAppleAbsent = true;
            s.MapCapsLock = ModKey.LCtrl;

            Apple(false); Use(s);
            bool sw = Down(Vk.Capital); Up(Vk.Capital);
            Untouched("клавиатуры нет — молчит", sw);

            Apple(true); Use(s);
            sw = Down(Vk.Capital); Up(Vk.Capital);
            Tapped("клавиатура есть — работает", sw, Vk.LControl);
            Apple(false);
        }

        static void MediaFirst()
        {
            Head("Верхний ряд");
            Settings s = Fresh();
            s.MediaFirst = true;
            s.FKeys[6] = "media.prev";
            Use(s);
            bool sw = Down(Vk.F1 + 6); Up(Vk.F1 + 6);
            Tapped("медиа сразу: F7 даёт «предыдущий трек»", sw, Vk.MediaPrev);

            s = Fresh(); s.MediaFirst = false; s.FKeys[6] = "media.prev";
            Use(s);
            sw = Down(Vk.F1 + 6); Up(Vk.F1 + 6);
            Untouched("F-клавиши сразу: F7 уходит как F7", sw);
        }

        static void FnSubstituteAll()
        {
            Head("Заменитель Fn");
            ModKey[] subs = { ModKey.RAlt, ModKey.RWin, ModKey.LWin, ModKey.CapsLock };
            int[] vks = { Vk.RMenu, Vk.RWin, Vk.LWin, Vk.Capital };
            bool[] ext = { true, true, false, false };

            for (int i = 0; i < subs.Length; i++)
            {
                Settings s = Fresh();
                s.FnSubstitute = subs[i];
                s.MediaFirst = true;
                s.FKeys[6] = "media.prev";
                Use(s);

                Send(vks[i], true, ext[i]); Clear();
                bool sw = Down(Vk.F1 + 6); Up(Vk.F1 + 6);
                // Модификатор надо снять, иначе выйдет ⌥+F7, — и тогда F7 перехват
                // подставляет сам. Заменителю без модификатора снимать нечего, и настоящая
                // F7 просто проходит насквозь.
                if (subs[i] == ModKey.CapsLock)
                    Untouched(subs[i] + " держим — F7 проходит насквозь", sw);
                else
                    Seq(subs[i] + " держим — приходит настоящая F7", sw, "F7v", "F7^");
                Send(vks[i], false, ext[i]); Clear();

                sw = Down(Vk.F1 + 6); Up(Vk.F1 + 6);
                Tapped(subs[i] + " отпустили — снова медиа", sw, Vk.MediaPrev);
            }

            Settings n = Fresh();
            n.FnSubstitute = ModKey.None;
            n.MediaFirst = true; n.FKeys[6] = "media.prev";
            Use(n);
            DownE(Vk.RMenu); Clear();
            bool sw2 = Down(Vk.F1 + 6); Up(Vk.F1 + 6);
            Tapped("без заменителя правый ⌥ ряд не переключает", sw2, Vk.MediaPrev);
            UpE(Vk.RMenu); Clear();
        }

        static void FKeysActions()
        {
            Head("Действия верхнего ряда");
            Settings s = Fresh();
            s.MediaFirst = true;
            s.FKeys[0] = "media.play";
            s.FKeys[1] = "volume.mute";
            s.FKeys[2] = "none";
            s.FKeys[3] = "key.escape";
            s.FKeys[4] = "key.home";
            Use(s);

            bool sw = Down(Vk.F1); Up(Vk.F1);
            Tapped("F1 → плей/пауза", sw, Vk.MediaPlay);

            sw = Down(Vk.F1 + 1); Up(Vk.F1 + 1);
            Tapped("F2 → без звука", sw, Vk.VolumeMute);

            sw = Down(Vk.F1 + 2); Up(Vk.F1 + 2);
            Untouched("F3 → «оставить как есть»: не вмешиваемся", sw);

            sw = Down(Vk.F1 + 3); Up(Vk.F1 + 3);
            Tapped("F4 → Escape", sw, Vk.Escape);

            sw = Down(Vk.F1 + 4); Up(Vk.F1 + 4);
            Tapped("F5 → Home", sw, Vk.Home);

            // Автоповтор — только там, где он осмыслен: Delete повторяется, Escape нет.
            string esc = N(Vk.Escape);
            Down(Vk.F1 + 3); Down(Vk.F1 + 3); Down(Vk.F1 + 3); Up(Vk.F1 + 3);
            Check("удержание Escape не размножается",
                  Sent() == esc + "v " + esc + "^", Sent());
            Clear();

            Settings r = Fresh();
            r.MediaFirst = true; r.FKeys[0] = "key.delete";
            Use(r);
            string del = N(Vk.Delete);
            Down(Vk.F1); Down(Vk.F1); Down(Vk.F1); Up(Vk.F1);
            Check("удержание Delete повторяется",
                  Sent() == del + "v " + del + "v " + del + "v " + del + "^", Sent());
            Clear();
        }

        static void NumpadClear()
        {
            Head("Клавиша ⌧ на цифровом блоке");   // приходит как Num Lock
            Settings s = Fresh();
            s.NumpadClear = "key.delete";
            Use(s);
            bool sw = Down(Vk.NumLock); Up(Vk.NumLock);
            Tapped("⌧ → Delete", sw, Vk.Delete);

            s = Fresh(); s.NumpadClear = "key.numlock";
            Use(s);
            sw = Down(Vk.NumLock); Up(Vk.NumLock);
            Tapped("⌧ → Num Lock", sw, Vk.NumLock);

            s = Fresh(); s.NumpadClear = "none";
            Use(s);
            sw = Down(Vk.NumLock); Up(Vk.NumLock);
            Untouched("⌧ → оставить как есть", sw);

            s = Fresh(); s.NumpadClear = "swallow";
            Use(s);
            sw = Down(Vk.NumLock); Up(Vk.NumLock);
            Check("⌧ → отключить клавишу", sw && Sent() == "", Sent());
            Clear();
        }

        static void EjectKey()
        {
            Head("Клавиша ⏏");   // до перехвата не доходит: её ловит перепись клавиш
            Settings s = Fresh();
            s.EjectKey = "key.delete";
            _eng.Apply(s); Clear();

            KeyAction a = Actions.Get(_eng.Current.EjectKey);
            Actions.Begin(a, false, Settings.BrightnessStep);
            Actions.End(a);
            Tapped("⏏ → Delete", true, Vk.Delete);

            s = Fresh(); s.EjectKey = "none";
            _eng.Apply(s); Clear();
            a = Actions.Get(_eng.Current.EjectKey);
            Check("⏏ по умолчанию не вмешивается", a.Kind == ActionKind.PassThrough, "" + a.Kind);
            Clear();
        }

        static void Modifiers()
        {
            Head("Модификаторы");
            Settings s = Fresh();
            s.MapCapsLock = ModKey.LCtrl;
            s.MapLCtrl = ModKey.LWin;
            s.MapLWin = ModKey.LAlt;
            s.MapLAlt = ModKey.LWin;
            s.MapRAlt = ModKey.None;
            s.MapRWin = ModKey.Escape;
            Use(s);

            bool sw = Down(Vk.Capital); Up(Vk.Capital);
            Tapped("Caps Lock → control", sw, Vk.LControl);

            sw = Down(Vk.LControl); Up(Vk.LControl);
            Tapped("левый control → Windows", sw, Vk.LWin);

            sw = Down(Vk.LWin); Up(Vk.LWin);
            Tapped("левый Windows → Alt", sw, Vk.LMenu);

            sw = Down(Vk.LMenu); Up(Vk.LMenu);
            Tapped("левый Alt → Windows", sw, Vk.LWin);

            sw = DownE(Vk.RMenu); UpE(Vk.RMenu);
            Check("правый ⌥ отключён", sw && Sent() == "", Sent());
            Clear();

            sw = DownE(Vk.RWin); UpE(Vk.RWin);
            Tapped("правый ⌘ → Escape", sw, Vk.Escape);

            s = Fresh();
            Use(s);
            sw = Down(Vk.LControl); Up(Vk.LControl);
            Untouched("нетронутый control не подменяется", sw);
        }

        static void FnNavigation()
        {
            Head("Fn+стрелки");
            Settings s = Fresh();
            s.FnNavigation = true;
            s.FnSubstitute = ModKey.RAlt;
            Use(s);

            DownE(Vk.RMenu); Clear();
            bool sw = Down(Vk.Up); Up(Vk.Up);
            Seq("Fn+↑ → PageUp", sw, N(Vk.Prior) + "v", N(Vk.Prior) + "^");
            sw = Down(Vk.Down); Up(Vk.Down);
            Seq("Fn+↓ → PageDown", sw, N(Vk.Next) + "v", N(Vk.Next) + "^");
            sw = Down(Vk.Return); Up(Vk.Return);
            Seq("Fn+Enter → Insert", sw, N(Vk.Insert) + "v", N(Vk.Insert) + "^");
            // ←/→/Backspace на умолчаниях уходят сочетаниям macOS: заменитель Fn и ⌥ —
            // одна клавиша, и таблица побеждает. Так и написано в карточке.
            sw = Down(Vk.Left); Up(Vk.Left);
            Seq("Fn+← достаётся ⌥← (по словам влево)", sw,
                N(Vk.LControl) + "v", N(Vk.Left) + "v", N(Vk.LControl) + "^");
            sw = Down(Vk.Back); Up(Vk.Back);
            Seq("Fn+Backspace достаётся ⌥Backspace (слово назад)", sw,
                N(Vk.LControl) + "v", N(Vk.Back) + "v", N(Vk.LControl) + "^");

            // А без сочетаний macOS остаются ровно те Fn, что обещаны.
            Settings only = Fresh();
            only.FnNavigation = true; only.FnSubstitute = ModKey.RAlt; only.MacShortcuts = false;
            Use(only);
            DownE(Vk.RMenu); Clear();
            sw = Down(Vk.Left); Up(Vk.Left);
            Seq("без сочетаний macOS Fn+← → Home", sw, N(Vk.Home) + "v", N(Vk.Home) + "^");
            sw = Down(Vk.Right); Up(Vk.Right);
            Seq("без сочетаний macOS Fn+→ → End", sw, N(Vk.End) + "v", N(Vk.End) + "^");
            sw = Down(Vk.Back); Up(Vk.Back);
            Seq("без сочетаний macOS Fn+Backspace → Delete", sw, N(Vk.Delete) + "v", N(Vk.Delete) + "^");
            UpE(Vk.RMenu); Clear();
            Use(s);
            DownE(Vk.RMenu); Clear();
            UpE(Vk.RMenu); Clear();

            sw = Down(Vk.Up); Up(Vk.Up);
            Untouched("без Fn стрелка остаётся стрелкой", sw);

            s = Fresh(); s.FnNavigation = false; s.FnSubstitute = ModKey.RAlt;
            Use(s);
            DownE(Vk.RMenu); Clear();
            sw = Down(Vk.Up); Up(Vk.Up);
            Untouched("выключено: Fn+↑ остаётся стрелкой", sw);
            UpE(Vk.RMenu); Clear();

            // Заменитель на Caps Lock. Щит снимает с заменителя его обычное значение,
            // пока тот работает как Fn, — но Caps Lock это не модификатор, а переключатель:
            // снять и вернуть его значит зажечь лампочку.
            Settings caps = Fresh();
            caps.FnNavigation = true;
            caps.FnSubstitute = ModKey.CapsLock;   // и MapCapsLock остаётся Caps Lock
            Use(caps);
            Down(Vk.Capital); Clear();
            sw = Down(Vk.Up); Up(Vk.Up);
            string capsName = N(Vk.Capital);
            Check("Fn на Caps Lock не щёлкает Caps Lock",
                  sw && !Sent().Contains(capsName), Sent());
            Clear();
            Up(Vk.Capital); Clear();
        }

        static void CmdTab()
        {
            Head("⌘Tab");
            Settings s = Fresh();
            s.CmdTabSwitchesWindows = true;
            Use(s);

            Down(Vk.LWin); Clear();
            bool sw = Down(Vk.Tab);
            Seq("⌘Tab переключает окна", sw,
                N(Vk.LWin) + "^", N(Vk.LMenu) + "v", N(Vk.Tab) + "v", N(Vk.Tab) + "^");
            Up(Vk.Tab); Clear();
            sw = Down(Vk.Tab);
            Seq("второй Tab листает дальше", sw, N(Vk.Tab) + "v", N(Vk.Tab) + "^");
            Up(Vk.Tab); Up(Vk.LWin);
            Check("⌘ отпустили — Alt тоже отпущен", Sent().Contains(N(Vk.LMenu) + "^"), Sent());
            Clear();

            s = Fresh(); s.CmdTabSwitchesWindows = false;
            Use(s);
            Down(Vk.LWin); Clear();
            sw = Down(Vk.Tab); Up(Vk.Tab);
            Shielded("выключено: ⌘Tab не открывает «Представление задач»", sw);
            Up(Vk.LWin); Clear();
        }

        static void MacShortcuts()
        {
            Head("Сочетания macOS");
            Settings s = Fresh();
            s.MacShortcuts = true;
            Use(s);

            Down(Vk.LWin); Clear();
            bool sw = Down(0x43); // C
            Seq("⌘C → Ctrl+C", sw,
                N(Vk.LControl) + "v", "Cv", "C^", N(Vk.LControl) + "^");
            Up(0x43); Up(Vk.LWin); Clear();

            Down(Vk.LWin); Clear();
            sw = Down(Vk.Left);
            Seq("⌘← → Home", sw, N(Vk.Home) + "v", N(Vk.Home) + "^");
            Up(Vk.Left); Up(Vk.LWin); Clear();

            // Удержание двух аккордов разом: отпустив второй, автоповтор первого
            // не должен сработать заново.
            Down(Vk.LWin); Clear();
            Down(0x43);                 // ⌘C — сработало
            Down(0x56); Up(0x56);       // ⌘V нажали и отпустили
            Clear();
            Down(0x43);                 // автоповтор ⌘C
            Check("автоповтор ⌘C не оживает от чужого отпускания", Sent() == "", Sent());
            Clear();
            Up(0x43); Up(Vk.LWin); Clear();

            Down(Vk.LWin); Clear();
            sw = Down(0x51); Down(0x51); Down(0x51);   // ⌘Q трижды
            int n = Sent().Split(new string[] { N(Vk.F4) + "v" }, StringSplitOptions.None).Length - 1;
            Check("удержание ⌘Q закрывает окно один раз", sw && n == 1, "срабатываний " + n + ": " + Sent());
            Clear();
            Up(0x51); Up(Vk.LWin); Clear();

            s = Fresh();
            s.MacShortcuts = true;
            s.MacShortcutsOff = new string[] { "copy" };
            Use(s);
            Down(Vk.LWin); Clear();
            sw = Down(0x43);
            Shielded("выключенное поимённо не срабатывает и не зовёт Windows", sw);
            Up(0x43); Up(Vk.LWin); Clear();

            // ⌘ с клавишей, которой в таблице нет, — то же самое с control.
            s = Fresh();
            Use(s);
            Down(Vk.LWin); Clear();
            sw = Down(0x4B);            // ⌘K
            Seq("⌘K → Ctrl+K, хотя в таблице его нет", sw,
                N(Vk.LControl) + "v", "Kv", "K^", N(Vk.LControl) + "^");
            Up(0x4B); Up(Vk.LWin); Clear();

            Down(Vk.LWin); Down(Vk.LShift); Clear();
            sw = Down(0x4E);            // ⇧⌘N
            Seq("⇧⌘N → Ctrl+Shift+N", sw,
                N(Vk.LControl) + "v", N(Vk.LShift) + "v", "Nv", "N^");
            Up(0x4E); Up(Vk.LShift); Up(Vk.LWin); Clear();

            // А выключенное поимённо общее правило не воскрешает.
            s = Fresh();
            s.MacShortcutsOff = new string[] { "copy" };
            Use(s);
            Down(Vk.LWin); Clear();
            sw = Down(0x43);
            Shielded("выключенное ⌘C общим правилом не оживает", sw);
            Up(0x43); Up(Vk.LWin); Clear();

            // Правило не берёт то, у чего есть свой хозяин: верхний ряд разбирается
            // ниже и остаётся за ним, даже когда зажата ⌘.
            s = Fresh();
            s.MediaFirst = true;
            s.FKeys[4] = "key.home";
            Use(s);
            Down(Vk.LWin); Clear();
            sw = Down(Vk.F1 + 4); Up(Vk.F1 + 4);
            Tapped("⌘ с F-клавишей достаётся верхнему ряду", sw, Vk.Home);
            Up(Vk.LWin); Clear();

            s = Fresh(); s.MacShortcuts = false;
            Use(s);
            Down(Vk.LWin); Clear();
            sw = Down(0x43);
            Untouched("сочетания выключены целиком — ⌘ снова обычная Win", sw);
            Up(0x43); Up(Vk.LWin); Clear();
        }

        static void SpaceRoles()
        {
            Head("⌘Space и ⌃Space");
            Settings s = Fresh();
            s.SpaceSearch = Settings.SpaceCmd;
            Use(s);

            Down(Vk.LWin); Clear();
            bool sw = Down(Vk.Space);
            Seq("⌘Space → поиск", sw, N(Vk.LWin) + "v", "Sv", "S^", N(Vk.LWin) + "^");
            Up(Vk.Space); Up(Vk.LWin); Clear();

            // Второй клавише достаётся язык — так обещает карточка.
            Down(Vk.LControl); Clear();
            sw = Down(Vk.Space);
            Seq("⌃Space → смена языка", sw, N(Vk.LWin) + "v", N(Vk.Space) + "v");
            Up(Vk.Space); Up(Vk.LControl); Clear();

            // Поменяли местами — поменялись и роли.
            s = Fresh(); s.SpaceSearch = Settings.SpaceCtrl;
            Use(s);
            Down(Vk.LControl); Clear();
            sw = Down(Vk.Space);
            Seq("поиск переехал на ⌃Space", sw, N(Vk.LWin) + "v", "Sv", "S^", N(Vk.LWin) + "^");
            Up(Vk.Space); Up(Vk.LControl); Clear();

            Down(Vk.LWin); Clear();
            sw = Down(Vk.Space);
            Seq("а язык — на ⌘Space", sw, N(Vk.LWin) + "v", N(Vk.Space) + "v");
            Up(Vk.Space); Up(Vk.LWin); Clear();

            // «Не трогать пробел» — и ни на одной клавише ничего не появляется.
            s = Fresh(); s.SpaceSearch = Settings.SpaceNone;
            Use(s);
            Down(Vk.LWin); Clear();
            sw = Down(Vk.Space); Up(Vk.Space);
            Shielded("не трогать: ⌘Space ничего не делает и «Пуск» не открывает", sw);
            Up(Vk.LWin); Clear();

            Down(Vk.LControl); Clear();
            sw = Down(Vk.Space); Up(Vk.Space);
            Untouched("не трогать: ⌃Space уходит программе как есть", sw);
            Up(Vk.LControl); Clear();
        }

        /// <summary>
        /// Модификатор нажали или отпустили между нажатием и отпусканием клавиши.
        ///
        /// Это самая тихая поломка, какая здесь бывает: если отпускание проглотили,
        /// а нажатие ушло в систему, клавиша остаётся зажатой навсегда — снять её
        /// нечем, человек её уже отпустил. Правило одно: куда ушло нажатие, туда же
        /// обязано уйти и отпускание, что бы ни изменилось между ними.
        /// </summary>
        static void ModifierChangedMidPress()
        {
            Head("Модификатор сменился посреди нажатия");
            Settings s = Fresh();
            Use(s);

            // ⌥ нажали ПОСЛЕ стрелки: нажатие ушло в систему как обычная стрелка.
            bool swDown = Down(Vk.Left);
            Down(Vk.LMenu);
            Clear();
            bool swUp = Up(Vk.Left);
            Check("стрелка: ⌥ нажали после — отпускание уходит следом за нажатием",
                  !swDown && !swUp, "нажатие проглочено=" + swDown + ", отпускание=" + swUp);
            Clear();
            Up(Vk.LMenu); Clear();

            // ⌘ нажали после буквы: то же самое.
            swDown = Down(0x53);            // S
            Down(Vk.LWin);
            Clear();
            swUp = Up(0x53);
            Check("буква: ⌘ нажали после — отпускание уходит следом за нажатием",
                  !swDown && !swUp, "нажатие проглочено=" + swDown + ", отпускание=" + swUp);
            Clear();
            Up(Vk.LWin); Clear();

            // Tab нажали до ⌘: переключателя окон не начинали, отпускать нечего.
            swDown = Down(Vk.Tab);
            Down(Vk.LWin);
            Clear();
            swUp = Up(Vk.Tab);
            Check("Tab: ⌘ нажали после — отпускание уходит следом за нажатием",
                  !swDown && !swUp, "нажатие проглочено=" + swDown + ", отпускание=" + swUp);
            Clear();
            Up(Vk.LWin); Clear();

            // Пробел нажали до ⌘.
            swDown = Down(Vk.Space);
            Down(Vk.LWin);
            Clear();
            swUp = Up(Vk.Space);
            Check("пробел: ⌘ нажали после — отпускание уходит следом за нажатием",
                  !swDown && !swUp, "нажатие проглочено=" + swDown + ", отпускание=" + swUp);
            Clear();
            Up(Vk.LWin); Clear();

            // А обратный порядок обязан работать по-прежнему: сначала ⌘, потом клавиша.
            Down(Vk.LWin); Clear();
            swDown = Down(0x43);            // ⌘C
            Seq("⌘ держали заранее — сочетание работает", swDown,
                N(Vk.LControl) + "v", "Cv", "C^", N(Vk.LControl) + "^");
            swUp = Up(0x43);
            Check("и его отпускание проглочено", swUp, "отпускание=" + swUp);
            Clear();
            Up(Vk.LWin); Clear();

            // ⌥ отпустили раньше клавиши: нажатие ушло аккордом, отпускание — тоже наше.
            Down(Vk.LMenu); Clear();
            swDown = Down(Vk.Left);         // ⌥← = по словам влево
            Clear();
            Up(Vk.LMenu); Clear();
            swUp = Up(Vk.Left);
            Check("⌥ отпустили раньше стрелки — отпускание всё равно наше",
                  swDown && swUp, "нажатие=" + swDown + ", отпускание=" + swUp);
            Clear();
        }

        /// <summary>
        /// У нажатой клавиши один хозяин.
        ///
        /// Слоёв, умеющих взять нажатие себе, шесть: навигация, аккорды, раскладка,
        /// перестановка ISO, одиночные клавиши и верхний ряд. Каждый помнит взятое
        /// по-своему, и если нажатие возьмёт один, а повтор — другой, то на отпускании
        /// сработает первый попавшийся, а запись второго останется навсегда. Следующее
        /// нажатие той же клавиши уйдёт в приложение, а отпускание съедим мы — и клавиша
        /// останется зажатой. Проверяем, что второй слой чужого не берёт.
        /// </summary>
        static void OneOwnerPerKey()
        {
            Head("У клавиши один хозяин");

            // Аккорд взял стрелку, а потом появился заменитель Fn: навигация её не отнимает.
            Settings s = Fresh();
            Use(s);
            Down(Vk.LWin);
            Down(Vk.Up);            // ⌘↑ — в начало документа, взял слой аккордов
            Up(Vk.LWin);            // ⌘ отпустили, стрелку держим
            DownE(Vk.RMenu);        // нажали заменитель Fn
            Down(Vk.Up);            // автоповтор стрелки
            Up(Vk.Up);              // отпустили
            UpE(Vk.RMenu);
            Clear();
            bool swDown = Down(Vk.Up);
            bool swUp = Up(Vk.Up);
            Check("после ⌘↑ и Fn стрелка не остаётся за нами",
                  !swDown && !swUp, "нажатие=" + swDown + ", отпускание=" + swUp);
            Clear();

            // Перестановка ISO взяла клавишу, а потом нажали ⌘: аккорд её не отнимает.
            s = Fresh();
            s.Physical = PhysLayout.Iso;
            Use(s);
            Send(Vk.Oem102, 0x56, true, false);    // клавиша слева от «Z»
            Down(Vk.LWin);
            Send(Vk.Oem102, 0x56, true, false);    // автоповтор при зажатой ⌘
            Clear();
            Send(Vk.Oem102, 0x56, false, false);   // отпустили
            Check("подставленный скан-код отпускается, а не остаётся зажатым",
                  Sent().Contains("scan29^"), Sent());
            Clear();
            Up(Vk.LWin); Clear();

            // Одиночная клавиша взята — аккорд её не отнимает.
            s = Fresh();
            s.NumpadClear = "key.delete";
            Use(s);
            Down(Vk.NumLock);
            Down(Vk.LWin);
            Down(Vk.NumLock);       // автоповтор при зажатой ⌘
            Clear();
            Up(Vk.NumLock);
            Check("одиночная клавиша отпускается своим слоем",
                  Sent().Contains(N(Vk.Delete) + "^"), Sent());
            Clear();
            Up(Vk.LWin); Clear();

            // Верхний ряд взят — аккорд его не отнимает.
            s = Fresh();
            s.MediaFirst = true;
            s.FKeys[4] = "key.home";
            Use(s);
            Down(Vk.F1 + 4);
            Down(Vk.LWin);
            Down(Vk.F1 + 4);
            Clear();
            Up(Vk.F1 + 4);
            Check("верхний ряд отпускается своим слоем",
                  Sent().Contains(N(Vk.Home) + "^"), Sent());
            Clear();
            Up(Vk.LWin); Clear();

            // Удержание ⌘+пробела не открывает поиск два десятка раз.
            s = Fresh();
            s.SpaceSearch = Settings.SpaceCmd;
            Use(s);
            Down(Vk.LWin); Clear();
            Down(Vk.Space); Down(Vk.Space); Down(Vk.Space);
            int n = Sent().Split(new string[] { "Sv" }, StringSplitOptions.None).Length - 1;
            Check("удержание ⌘+пробела открывает поиск один раз", n == 1, "срабатываний " + n);
            Clear();
            Up(Vk.Space); Up(Vk.LWin); Clear();

            // И общее правило ⌘ тоже не повторяется: про клавишу вне таблицы мы не знаем,
            // осмыслен ли для неё повтор.
            s = Fresh();
            Use(s);
            Down(Vk.LWin); Clear();
            Down(0x4B); Down(0x4B); Down(0x4B);   // ⌘K трижды
            n = Sent().Split(new string[] { "Kv" }, StringSplitOptions.None).Length - 1;
            Check("удержание ⌘K срабатывает один раз", n == 1, "срабатываний " + n);
            Clear();
            Up(0x4B); Up(Vk.LWin); Clear();
        }

        static void AppleLayout()

        {
            Head("Раскладки Apple");
            Settings s = Fresh();
            s.AppleLayoutEnabled = false;
            Use(s);
            bool sw = Down(0x41); Up(0x41);   // A
            Untouched("выключено: буква уходит своей раскладке", sw);

            Check("файлы раскладок на месте", Layouts.All.Count > 0, "их " + Layouts.All.Count);
            Check("русская раскладка читается", Layouts.ById("ru") != null, "нет ru");
        }

        static void SnapshotIndependence()
        {
            Head("Снимок настроек");
            Settings live = Fresh();
            live.MapCapsLock = ModKey.LCtrl;
            live.FKeys[0] = "media.play";
            Use(live);

            // Правим живой объект так, как это делает окно: до Apply перехват не должен
            // увидеть ничего.
            live.MapCapsLock = ModKey.Escape;
            live.FKeys[0] = "volume.mute";
            live.MacShortcutsOff = new string[] { "copy" };

            bool sw = Down(Vk.Capital); Up(Vk.Capital);
            Tapped("правка живых настроек не течёт в перехват", sw, Vk.LControl);

            sw = Down(Vk.F1); Up(Vk.F1);
            Tapped("массив клавиш в снимке — свой", sw, Vk.MediaPlay);

            _eng.Apply(live); Clear();
            sw = Down(Vk.Capital); Up(Vk.Capital);
            Tapped("после Apply правки применились", sw, Vk.Escape);
        }

        static void LayoutGuess()
        {
            Head("Подбор раскладки по языку");
            Settings s = Fresh();
            Check("русский подбирается сам", s.LayoutFor(0x0419) == "ru", "«" + s.LayoutFor(0x0419) + "»");
            Check("немецкий подбирается сам", s.LayoutFor(0x0407) == "de", "«" + s.LayoutFor(0x0407) + "»");
            string en = s.LayoutFor(0x0409);
            Check("английский подбирается сам", !String.IsNullOrEmpty(en), "«" + en + "»");

            s.LayoutBindings = new LayoutBinding[] { new LayoutBinding { Lang = "0419", Layout = "de" } };
            Check("выбор руками важнее подбора", s.LayoutFor(0x0419) == "de", "«" + s.LayoutFor(0x0419) + "»");

            s.LayoutBindings = new LayoutBinding[] { new LayoutBinding { Lang = "0419", Layout = "" } };
            Check("«не трогать» руками — и не трогаем", s.LayoutFor(0x0419) == null, "«" + s.LayoutFor(0x0419) + "»");

            // Три ответа должны быть тремя, а не двумя: подбор, отказ и выбор.
            Settings th = Fresh();
            th.SetLayoutFor(0x0419, "");
            Check("отказ запоминается", th.BindingFor(0x0419) != null && th.LayoutFor(0x0419) == null,
                  "привязка " + (th.BindingFor(0x0419) == null ? "исчезла" : "есть") +
                  ", раскладка «" + th.LayoutFor(0x0419) + "»");
            th.ClearLayoutFor(0x0419);
            Check("отказ снимается — подбор возвращается",
                  th.BindingFor(0x0419) == null && th.LayoutFor(0x0419) == "ru", "«" + th.LayoutFor(0x0419) + "»");
            th.SetLayoutFor(0x0419, "de");
            Check("выбор руками запоминается", th.LayoutFor(0x0419) == "de", "«" + th.LayoutFor(0x0419) + "»");
        }

        /// <summary>
        /// Целостность таблиц. Промах здесь ничего не роняет — он просто делает строку
        /// недостижимой, и заметить это можно, только нажав ту самую клавишу.
        /// </summary>
        static void Tables()
        {
            Head("Таблицы");

            var ids = new Dictionary<string, int>();
            foreach (KeyAction a in Actions.All)
            {
                int n;
                ids[a.Id] = ids.TryGetValue(a.Id, out n) ? n + 1 : 1;
            }
            var twice = new List<string>();
            foreach (KeyValuePair<string, int> kv in ids) if (kv.Value > 1) twice.Add(kv.Key);
            Check("у действий нет повторяющихся кодов", twice.Count == 0, String.Join(", ", twice.ToArray()));

            var mids = new Dictionary<string, int>();
            var pairs = new Dictionary<string, string>();
            var clash = new List<string>();
            foreach (MacShortcut s in MacKeys.All)
            {
                int n;
                mids[s.Id] = mids.TryGetValue(s.Id, out n) ? n + 1 : 1;
                string key = s.Vk + "/" + (int)s.Mods;
                string had;
                if (pairs.TryGetValue(key, out had)) clash.Add(had + " и " + s.Id);
                else pairs[key] = s.Id;
            }
            twice = new List<string>();
            foreach (KeyValuePair<string, int> kv in mids) if (kv.Value > 1) twice.Add(kv.Key);
            Check("у сочетаний нет повторяющихся кодов", twice.Count == 0, String.Join(", ", twice.ToArray()));
            Check("нет двух сочетаний на одну клавишу с теми же модификаторами",
                  clash.Count == 0, String.Join("; ", clash.ToArray()));

            // Заводские назначения верхнего ряда обязаны существовать в списке действий:
            // иначе клавиша с завода не делает ничего, а в окне против неё пусто.
            var missing = new List<string>();
            CheckIds(Settings.DefaultFKeys(), missing, "общие");
            foreach (AppleGen g in Enum.GetValues(typeof(AppleGen)))
                CheckIds(Models.DefaultFKeys(g), missing, Models.GenName(g));
            var s0 = new Settings();
            if (Actions.Get(s0.NumpadClear) == null) missing.Add("для ⌧: " + s0.NumpadClear);
            if (Actions.Get(s0.EjectKey) == null) missing.Add("для ⏏: " + s0.EjectKey);
            Check("все заводские назначения существуют", missing.Count == 0,
                  String.Join(", ", missing.ToArray()));
        }

        static void CheckIds(string[] list, List<string> missing, string whose)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
                if (!String.IsNullOrEmpty(list[i]) && Actions.Get(list[i]) == null)
                    missing.Add(whose + " F" + (i + 1) + ": " + list[i]);
        }

        /// <summary>
        /// Снимок обязан быть независим по КАЖДОМУ полю, а не по двум, которые вспомнили.
        /// Правим поля живого объекта отражением и смотрим, не сдвинулся ли снимок.
        /// </summary>
        static void SnapshotByReflection()
        {
            Head("Снимок: каждое поле");
            Settings live = new Settings();
            Settings shot = live.Snapshot();
            var moved = new List<string>();
            const string Mark = "СЛЕД";

            foreach (FieldInfo f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var arr = f.GetValue(live) as Array;
                if (arr != null && arr.Length > 0)
                {
                    // Правим ЭЛЕМЕНТ живого массива: если снимок скопировал только сам
                    // массив, элементы у них общие и правка проступит.
                    object first = arr.GetValue(0);
                    var b = first as LayoutBinding;
                    if (b != null) b.Layout = Mark;
                    else if (first is string) arr.SetValue(Mark, 0);
                    else continue;

                    var mirrorArr = (Array)f.GetValue(shot);
                    object mirror = mirrorArr.Length > 0 ? mirrorArr.GetValue(0) : null;
                    var mb = mirror as LayoutBinding;
                    string seen = mb != null ? mb.Layout : ("" + mirror);
                    if (seen == Mark) moved.Add(f.Name);
                }
                else if (f.FieldType == typeof(string))
                {
                    object before = f.GetValue(live);
                    f.SetValue(live, Mark);
                    if (Mark.Equals(f.GetValue(shot))) moved.Add(f.Name);
                    f.SetValue(live, before);
                }
            }

            // Массив привязок с завода пуст — заполним и проверим отдельно.
            Settings bound = new Settings();
            bound.SetLayoutFor(0x0419, "ru");
            Settings copy = bound.Snapshot();
            bound.LayoutBindings[0].Layout = "de";
            if (copy.LayoutBindings[0].Layout != "ru") moved.Add("LayoutBindings");

            Check("правка живых настроек не проступает в снимке", moved.Count == 0,
                  "проступило: " + String.Join(", ", moved.ToArray()));
        }

        /// <summary>Что приходит из файла, может быть каким угодно.</summary>
        static void BrokenFile()
        {
            Head("Битый файл настроек");
            MethodInfo norm = typeof(Settings).GetMethod("Normalize",
                                  BindingFlags.NonPublic | BindingFlags.Instance);
            if (norm == null) { Check("Normalize на месте", false, "метода нет"); return; }

            Settings s = new Settings();
            s.FKeys = null;
            norm.Invoke(s, null);
            Check("пропавший список клавиш восстанавливается",
                  s.FKeys != null && s.FKeys.Length == Settings.MaxFKeys, "не восстановился");

            s = new Settings();
            s.FKeys = new string[] { "media.play", null, "чепуха" };
            norm.Invoke(s, null);
            Check("короткий список дополняется до полного",
                  s.FKeys.Length == Settings.MaxFKeys && s.FKeys[0] == "media.play", "не дополнился");
            Check("дырка в списке заполняется заводским", s.FKeys[1] != null, "осталась пустой");

            s = new Settings();
            s.LayoutBindings = null;
            norm.Invoke(s, null);
            Check("пропавшие привязки восстанавливаются", s.LayoutBindings != null, "остались пустыми");

            // Неизвестное действие не должно ронять ничего: его просто нет.
            s = Fresh();
            s.MediaFirst = true;
            s.FKeys[0] = "такого.действия.нет";
            Use(s);
            bool sw = Down(Vk.F1); Up(Vk.F1);
            Untouched("неизвестное действие просто ничего не делает", sw);

            s = new Settings();
            s.MacShortcutsOff = null;
            Check("пустой список выключенных не роняет проверку", s.MacEnabled("copy"), "уронил");
        }

        static void OtherSettings()

        {
            Head("Настройки вне перехвата");
            Settings d = new Settings();

            Check("канал обновлений по умолчанию — стабильный",
                  d.UpdateChannel == Settings.ChannelStable, d.UpdateChannel);
            Check("⌘Tab по умолчанию включён", d.CmdTabSwitchesWindows, "выключен");
            Check("сочетания macOS по умолчанию включены", d.MacShortcuts, "выключены");
            Check("медиа по умолчанию сразу", d.MediaFirst, "нет");
            Check("заменитель Fn по умолчанию — правый ⌥", d.FnSubstitute == ModKey.RAlt, "" + d.FnSubstitute);
            Check("свёрнутый запуск по умолчанию выключен", !d.StartMinimized, "включён");
            Check("режим разработчика по умолчанию выключен", !d.DeveloperMode, "включён");
            Check("раскладки Apple по умолчанию выключены", !d.AppleLayoutEnabled, "включены");
            Check("третий уровень по умолчанию никакой", d.OptLevel == OptLevel.Off, "" + d.OptLevel);
            Check("поиск по умолчанию на ⌘, язык на control",
                  d.SpaceSearch == Settings.SpaceCmd
                  && d.CmdSpace == "search" && d.CtrlSpace == "language",
                  d.CmdSpace + " / " + d.CtrlSpace);

            Settings s = Fresh();
            s.MacShortcutsOff = new string[] { "copy", "paste" };
            Check("выключенные сочетания помнятся поимённо",
                  !s.MacEnabled("copy") && !s.MacEnabled("paste") && s.MacEnabled("cut"), "MacEnabled врёт");

            // Сохранение и чтение: что записали, то и читается. Пишем в настоящий файл —
            // другого пути у программы нет, — а прежний возвращаем как был.
            byte[] before = null;
            try { if (File.Exists(Settings.FilePath)) before = File.ReadAllBytes(Settings.FilePath); }
            catch { }
            try
            {
                Settings w = new Settings();
                w.MapCapsLock = ModKey.LCtrl;
                w.FKeys[3] = "key.escape";
                w.MacShortcutsOff = new string[] { "copy" };
                w.LayoutBindings = new LayoutBinding[] { new LayoutBinding { Lang = "0419", Layout = "ru" } };
                w.AppleLayoutEnabled = true;
                w.Save();
                Settings r = Settings.Load();
                Check("настройки переживают запись и чтение",
                      r.MapCapsLock == ModKey.LCtrl && r.FKeys[3] == "key.escape"
                      && !r.MacEnabled("copy") && r.AppleLayoutEnabled
                      && r.LayoutBindings.Length == 1 && r.LayoutBindings[0].Layout == "ru",
                      "прочиталось иначе");
            }
            catch (Exception e) { Check("настройки переживают запись и чтение", false, e.Message); }
            finally
            {
                try
                {
                    if (before != null) File.WriteAllBytes(Settings.FilePath, before);
                    else File.Delete(Settings.FilePath);
                }
                catch { }
            }
        }
    }
}
