// Стенд настроек: каждую настройку включают, шлют в перехват нажатия и смотрят,// что он решил отдать Windows. Ввод никуда не уходит — Input.Sink его записывает.
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
        static MethodInfo _handle, _release, _releaseKeep;
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
            // Без доводов: их два, и без указания типов отражение не выбирает.
            _release = typeof(Engine).GetMethod("ReleaseEverything",
                           BindingFlags.NonPublic | BindingFlags.Instance,
                           null, Type.EmptyTypes, null);
            // Вторая перегрузка — та, что умеет держать дальше. Без неё весь этот код
            // стенд не выполнял ни разу: и Use, и Apply на стенде идут запасным путём,
            // а он держать не просит.
            _releaseKeep = typeof(Engine).GetMethod("ReleaseEverything",
                               BindingFlags.NonPublic | BindingFlags.Instance,
                               null, new Type[] { typeof(Settings), typeof(bool) }, null);
            if (_handle == null || _release == null || _releaseKeep == null)
            { Console.WriteLine("нет Handle/ReleaseEverything"); return 2; }

            Enabled();
            PauseWhenAppleAbsent();
            MediaFirst();
            FnSubstituteAll();
            FKeysActions();
            NumpadClear();
            EjectKey();
            ActionsUnderModifiers();
            Modifiers();
            FnNavigation();
            CmdTab();
            MacShortcuts();
            SpaceRoles();
            AppleLayout();
            ModifierChangedMidPress();
            OneOwnerPerKey();
            SubstituteOnPlainKey();
            RepeatStaysWhereItWent();
            LiftedOnlyOnce();
            RepressAfterChord();
            TwoKeysOneCode();
            SwitcherAltGetsOutOfTheWay();
            OneOwnerForNumpadClear();
            ModifierThroughStaysThrough();
            MediaActionDropsSubstitute();
            ResetKeepsTheOwner();
            SingleKeyLatches();
            LayoutLevels();
            SnapshotIndependence();
            LayoutGuess();
            OtherSettings();
            Tables();
            SnapshotByReflection();
            BrokenFile();
            NormalizeRules();

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

        /// <summary>Считает ли перехват, что AltGr привёл призрачный control.</summary>
        static bool PhantomCtrl()
        {
            return (bool)typeof(Engine)
                .GetField("_phantomCtrl", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_eng);
        }

        /// <summary>Что перехват считает зажатым прямо сейчас.</summary>
        static HashSet<ModKey> ModsDown()
        {
            return (HashSet<ModKey>)typeof(Engine)
                .GetField("_modsDown", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_eng);
        }

        /// <summary>Наш счёт регистра — тот, по которому раскладка Apple выбирает букву.</summary>
        static bool CapsOn()
        {
            return (bool)typeof(Engine)
                .GetField("_capsOn", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_eng);
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
        static int Count(string where, string what)
        {
            int n = 0, at = 0;
            while ((at = where.IndexOf(what, at)) >= 0) { n++; at += what.Length; }
            return n;
        }
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

        /// <summary>
        /// Отправлено ровно одно нажатие с отпусканием — без вопроса о проглатывании.
        /// Клавиша ⏏ до перехвата не доходит вовсе, и передавать за неё «проглочено»
        /// константой значило проверять половиной проверки ничего.
        /// </summary>
        static void Sends(string name, int vk)
        {
            string want = N(vk) + "v " + N(vk) + "^";
            Check(name, Sent() == want, "ждали «" + want + "», пришло «" + Sent() + "»");
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

        /// <summary>
        /// Ровно эти события в этом порядке и никаких других, кроме щита.
        ///
        /// Seq ищет подпоследовательность и про лишнее молчит — а лишнее здесь и есть
        /// беда: возвращённый посреди удержания ⌥ проверку на подпоследовательность
        /// проходит, потому что всё нужное в записи тоже есть.
        /// </summary>
        static void Only(string name, bool swallowed, params string[] want)
        {
            var got = new List<string>();
            foreach (string ev in _log) if (ev != "щитv" && ev != "щит^") got.Add(ev);
            bool same = got.Count == want.Length;
            for (int i = 0; same && i < want.Length; i++) if (got[i] != want[i]) same = false;
            Check(name, swallowed && same,
                  "ждали «" + String.Join(" ", want) + "», пришло «" + Sent() + "»");
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

        /// <summary>
        /// Готовый символ и готовый аккорд из списка действий уходят под зажатыми
        /// модификаторами не туда: текст под Alt приходит как WM_SYSCHAR, а аккорд
        /// в конце отпускает Win, которую держит человек.
        /// </summary>
        static void ActionsUnderModifiers()
        {
            Head("Действия под зажатыми модификаторами");
            Settings s = Fresh();
            Use(s);

            Down(Vk.LMenu); Clear();
            Send(Vk.Clear, 0x59, true, false);
            Send(Vk.Clear, 0x59, false, false);
            int altUp = Sent().IndexOf(N(Vk.LMenu) + "^");
            int sign = Sent().IndexOf("=");
            Check("«=» цифрового блока не печатается под зажатым Alt",
                  altUp >= 0 && sign > altUp, Sent());
            Clear();
            Up(Vk.LMenu); Clear();

            // Готовый аккорд поверх зажатой ⌘: Input.Chord в конце отпустит Win, которую
            // держит человек, и она молча перестанет работать до следующего нажатия.
            s = Fresh();
            s.MediaFirst = true;
            s.FKeys[3] = "sys.search";           // Win+S
            Use(s);
            Down(Vk.LWin); Clear();
            Down(Vk.F1 + 3); Up(Vk.F1 + 3);
            int winUp = Sent().IndexOf(N(Vk.LWin) + "^");
            int sKey = Sent().IndexOf(N(0x53));
            Check("аккорд не жмёт Win поверх той, что держит человек",
                  winUp >= 0 && sKey > winUp, Sent());
            Clear();
            Up(Vk.LWin); Clear();
        }

        static void EjectKey()
        {
            // Приходит она переписью клавиш, а исполняется на потоке перехвата:
            // App просит об этом через Engine.PostAction, и разбирается просьба
            // тем же RunAction, что и всё остальное, — ради щита. Стенд потока
            // не заводит, поэтому PostAction уходит запасным путём — а он ведёт
            // ровно туда же, в RunAsked, и это ровно то, что надо проверить.
            Head("Клавиша ⏏");
            Settings s = Fresh();
            s.EjectKey = "key.delete";
            _eng.Apply(s); Clear();

            // Тем же путём, каким это делает программа, а не мимо него. Прежде стенд
            // звал Actions напрямую и проверял таблицу действий, а не поведение:
            // проверка проходила при любой поломке разбора просьбы.
            _eng.PostAction(_eng.Current.EjectKey);
            Sends("⏏ → Delete", Vk.Delete);

            // Действие вида «клавиша» обязано и отпустить нажатое: физической ⏏ нет,
            // отпускания у неё не будет, а снять зажатую клавишу потом нечем.
            Settings esc = Fresh(); esc.EjectKey = "key.escape";
            _eng.Apply(esc); Clear();
            _eng.PostAction(_eng.Current.EjectKey);
            Sends("⏏ → Escape отпускает клавишу", Vk.Escape);

            // Аккорд идёт через щит: зажатую ⌘ надо убрать с дороги и вернуть.
            Settings ex = Fresh(); ex.EjectKey = "sys.explorer";
            _eng.Apply(ex); Clear();
            _eng.PostAction(_eng.Current.EjectKey);
            Check("⏏ с аккордом посылает аккорд",
                  Count(Sent(), N(Vk.E) + "v") == 1 && Count(Sent(), N(Vk.E) + "^") == 1,
                  Sent());
            Clear();

            // И ради чего всё это заведено: щит. Аккорд в конце отпускает те же
            // модификаторы, что нажал, — в том числе ⌘, которую держит человек.
            // Убрать её с дороги надо ДО посылки, иначе она молча перестанет
            // работать до перенажатия. Без зажатого модификатора спрашивать нечего:
            // прежняя проверка прошла бы и при вызове Actions напрямую.
            _eng.Apply(ex);
            Down(Vk.LWin); Clear();
            _eng.PostAction(_eng.Current.EjectKey);
            int winOff = Sent().IndexOf(N(Vk.LWin) + "^");
            int eDown = Sent().IndexOf(N(Vk.E) + "v");
            Check("⏏ с аккордом убирает с дороги зажатую человеком ⌘",
                  winOff >= 0 && eDown > winOff, Sent());
            Clear(); Up(Vk.LWin); Clear();
            _eng.Apply(s); Clear();

            // Заводское значение спрашиваем у нового объекта: строкой ниже мы его
            // переписываем, и прежняя проверка сверяла только что записанное — она
            // прошла бы при любом заводском назначении.
            Check("⏏ по умолчанию не вмешивается",
                  Actions.Get(new Settings().EjectKey).Kind == ActionKind.PassThrough,
                  "заводское назначение ⏏ — «" + new Settings().EjectKey + "»");
            // Выключенное ⏏ спрашиваем поведением, а не видом настройки: PassThrough
            // у «none» верен по построению таблицы, и проверять его — проверять таблицу.
            s = Fresh(); s.EjectKey = "none";
            _eng.Apply(s); Clear();
            _eng.PostAction(_eng.Current.EjectKey);
            Check("выключенное ⏏ ничего не посылает", Sent() == "", Sent());
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

            // Отпустили вторую ⌘ — переключатель окон не закрывается.
            s = Fresh();
            Use(s);
            Down(Vk.LWin); Down(Vk.Tab); Up(Vk.Tab); Clear();
            DownE(Vk.RWin);
            Check("вторая ⌘ при открытом переключателе не жмёт настоящую Win",
                  Sent().IndexOf(N(Vk.RWin) + "v") < 0, Sent());
            Clear();
            UpE(Vk.RWin);
            Check("вторую ⌘ отпустили — переключатель окон остался открыт",
                  Sent().IndexOf(N(Vk.LMenu) + "^") < 0, Sent());
            Clear();
            Up(Vk.LWin); Clear();

            // Схема «Как в Windows»: ⌘ приходит клавишей ⌥. Снимать подставленную Win
            // и возвращать Alt надо по назначению, а не по имени клавиши.
            s = Fresh();
            s.MapLAlt = ModKey.LWin; s.MapLWin = ModKey.LAlt;
            Use(s);
            Down(Vk.LMenu); Clear();
            Down(Vk.Tab); Up(Vk.Tab);
            Check("переназначенная ⌘: подставленная Win снимается перед переключателем",
                  Sent().IndexOf(N(Vk.LWin) + "^") >= 0
                  && Sent().IndexOf(N(Vk.LWin) + "^") < Sent().IndexOf(N(Vk.LMenu) + "v"), Sent());
            Clear();
            Up(Vk.LMenu);
            Check("переназначенная ⌘: Alt переключателя отпускается",
                  Sent().IndexOf(N(Vk.LMenu) + "^") >= 0, Sent());
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

            // Ветка верхнего ряда защёлкивается на ПЕРВОМ нажатии, чем бы оно ни
            // кончилось. Иначе нажатие уходит в приложение настоящей F-клавишей,
            // а отпускание съедает наш слой — и клавиша остаётся зажатой там навсегда.
            s = Fresh();
            s.MediaFirst = false;               // «сначала F-клавиши»
            s.FnSubstitute = ModKey.RAlt;
            s.FKeys[6] = "media.prev";
            Use(s);
            bool passDown = Down(Vk.F1 + 6);    // F7 ушла насквозь: Fn никто не держит
            Clear();
            DownE(Vk.RMenu);                    // заменитель Fn нажали, не отпуская F7
            bool passRepeat = Down(Vk.F1 + 6);  // автоповтор F7
            bool passUp = Up(Vk.F1 + 6);
            // Вердикты, а не только отправленное: ими и отличается «правильно»
            // от «клавиша ушла в приложение, а отпускание съели мы».
            Check("F-клавишу, ушедшую насквозь, заменитель Fn не отбирает",
                  !passDown && !passRepeat && !passUp,
                  "нажатие=" + passDown + ", повтор=" + passRepeat +
                  ", отпускание=" + passUp + ", пришло «" + Sent() + "»");
            Check("F-клавиша, ушедшая насквозь, не превращается в медиадействие",
                  !passDown && Sent().IndexOf(N(Vk.MediaPrev)) < 0,
                  "нажатие=" + passDown + ", пришло «" + Sent() + "»");
            Check("после этого F7 не остаётся нажатой",
                  Count(Sent(), N(Vk.F1 + 6) + "v") <= Count(Sent(), N(Vk.F1 + 6) + "^"),
                  Sent());
            Clear();
            UpE(Vk.RMenu); Clear();

            // Та же клавиша, ушедшая насквозь, не достаётся и слою аккордов: иначе
            // он съест отпускание, и в приложении F7 останется зажатой.
            s = Fresh();
            s.MediaFirst = false;
            s.FnSubstitute = ModKey.RAlt;
            Use(s);
            Down(Vk.F1 + 6);
            Down(Vk.LWin);
            Down(Vk.F1 + 6);
            Clear();
            bool chordUp = Up(Vk.F1 + 6);
            Check("F-клавишу, ушедшую насквозь, не отнимает слой аккордов",
                  !chordUp, "отпускание=" + chordUp + ", пришло «" + Sent() + "»");
            Clear();
            Up(Vk.LWin); Clear();

            // Промах мимо таблицы ⌘ глотает нажатие в самом конце, а верхний ряд стоит
            // выше и успевает защёлкнуть ветку. Снять защёлку после этого некому:
            // отпускание уходит к слою аккордов. Следующее нажатие пошло бы по вчерашней
            // ветке — и ⌥+F4 дало бы Alt+F4.
            s = Fresh();
            s.MediaFirst = false;               // «сначала F-клавиши»
            s.FnSubstitute = ModKey.RAlt;
            s.FKeys[3] = "media.prev";
            Use(s);
            Down(Vk.LWin);
            Down(Vk.F1 + 3); Up(Vk.F1 + 3);     // ⌘+F4 — такой строки в таблице нет
            Up(Vk.LWin); Clear();
            DownE(Vk.RMenu);                    // теперь держим заменитель Fn
            bool fnDown = Down(Vk.F1 + 3);
            bool fnUp = Up(Vk.F1 + 3);
            Check("защёлку F-клавиши снимает верхний ряд, и следующая ветка выбирается заново",
                  fnDown && fnUp && Sent().IndexOf(N(Vk.MediaPrev)) >= 0,
                  "нажатие=" + fnDown + ", отпускание=" + fnUp + ", пришло «" + Sent() + "»");
            Clear();
            UpE(Vk.RMenu); Clear();

            // Перестановка ISO обязана держать клавишу и на автоповторе: иначе повтор
            // уходит в приложение настоящим скан-кодом, а отпускание снимает подставленный,
            // и настоящий остаётся зажатым.
            s = Fresh();
            s.Physical = PhysLayout.Iso;
            Use(s);
            bool iso0 = Send(Vk.Oem102, 0x56, true, false);
            bool iso1 = Send(Vk.Oem102, 0x56, true, false);   // автоповтор
            bool iso2 = Send(Vk.Oem102, 0x56, false, false);
            Check("перестановка ISO держит клавишу и на автоповторе", iso0 && iso1 && iso2,
                  "нажатие=" + iso0 + ", повтор=" + iso1 + ", отпускание=" + iso2 +
                  ", пришло «" + Sent() + "»");
            Clear();

            // Призрачный Ctrl от AltGr Windows держит по-настоящему, и общий сброс
            // перечитывает зажатое у неё. Записанный в множество, он оставался там
            // навсегда: снять его некому, а с ним раскладка Apple молча переставала
            // работать и ни одно сочетание ⌘ больше не узнавалось.
            s = Fresh();
            Use(s);
            Send(Vk.LControl, 0x21D, true, false);   // AltGr привёл призрака
            // Windows на время сброса отвечает так, как отвечает при зажатом AltGr:
            // призрачный control она держит по-настоящему.
            Engine.HeldProbe = delegate(int probe) { return probe == Vk.LControl; };
            try { _release.Invoke(_eng, null); }
            finally { Engine.HeldProbe = null; }
            Check("призрачный Ctrl не попадает в перечёт зажатого",
                  !ModsDown().Contains(ModKey.LCtrl),
                  "остался в множестве — снять его оттуда некому");
            Send(Vk.LControl, 0x21D, false, false);  // призрак ушёл вместе с AltGr
            Clear();

            // Заменитель Fn на переназначенной клавише: Windows держит подставленный код,
            // а перечёт после сброса спрашивает про собственный. Стерев множество нажатого
            // и перечитав его, мы теряли такую клавишу навсегда — а с ней и заменитель.
            s = Fresh();
            s.FnSubstitute = ModKey.CapsLock;
            s.MapCapsLock = ModKey.LCtrl;
            s.MediaFirst = true;
            s.FKeys[1] = "media.play";
            Use(s);
            Down(Vk.Capital);                     // держим заменитель
            Engine.HeldProbe = delegate(int probe) { return probe == Vk.LControl; };
            try { _release.Invoke(_eng, null); }
            finally { Engine.HeldProbe = null; }
            Clear();
            Down(Vk.F1 + 1); Up(Vk.F1 + 1);
            Check("заменитель Fn на переназначенной клавише переживает общий сброс",
                  Sent().IndexOf(N(Vk.MediaPlay)) < 0,
                  "после сброса F2 снова дала медиа — заменитель забыт: «" + Sent() + "»");
            Clear();
            Up(Vk.Capital); Clear();

            // После сброса щит заменителя Fn не должен «возвращать» клавишу, которой
            // Windows не держала. Подставленный код мы отпустили, а клавиша осталась
            // в множестве нажатого — и WindowsHolds отвечал её собственным кодом.
            s = Fresh();
            s.FnSubstitute = ModKey.CapsLock;
            s.MapCapsLock = ModKey.LCtrl;
            s.MediaFirst = true;
            s.FKeys[1] = "media.play";
            Use(s);
            Down(Vk.Capital);
            _release.Invoke(_eng, null);
            Clear();
            Down(Vk.F1 + 1); Up(Vk.F1 + 1);
            Check("после сброса заменитель Fn не жмёт Caps Lock",
                  Sent().IndexOf(N(Vk.Capital) + "v") < 0,
                  "щит вернул клавишу, которой Windows не держала: " + Sent());
            Clear();
            Up(Vk.Capital); Clear();

            // То же другим щитом: «Как в Windows» — ⌥ приходит клавишей Win.
            s = Fresh();
            s.MapLAlt = ModKey.LWin;
            Use(s);
            Down(Vk.LMenu);
            _release.Invoke(_eng, null);
            Clear();
            Send(Vk.Clear, 0x59, true, false);     // «=» цифрового блока идёт через EmitText
            Send(Vk.Clear, 0x59, false, false);
            Check("после сброса щит не жмёт Alt, которого никто не держал",
                  Sent().IndexOf(N(Vk.LMenu) + "v") < 0, Sent());
            Clear();
            Up(Vk.LMenu); Clear();

            // Подставленный код, о котором Windows ещё не успела узнать, не должен
            // записаться в множество нажатого как чужая настоящая клавиша.
            s = Fresh();
            s.MapCapsLock = ModKey.LCtrl;
            Use(s);
            Down(Vk.Capital);
            Engine.HeldProbe = delegate(int probe) { return probe == Vk.LControl; };
            try { _release.Invoke(_eng, null); }
            finally { Engine.HeldProbe = null; }
            Check("подставленный control не записывается в множество как своя клавиша",
                  !ModsDown().Contains(ModKey.LCtrl),
                  "control остался зажатым навсегда: раскладка Apple молчит, ⌘C не узнаётся");
            Up(Vk.Capital); Clear();

            // Сброс со сменой настроек возвращает подставленный control под зажатую
            // клавишу — иначе Caps Lock в роли control молча перестаёт работать
            // до перенажатия.
            s = Fresh();
            s.MapCapsLock = ModKey.LCtrl;
            Use(s);
            Down(Vk.Capital);
            Clear();
            _releaseKeep.Invoke(_eng, new object[] { s, true });
            Check("сброс со сменой настроек возвращает подставленный control",
                  Sent().IndexOf(N(Vk.LControl) + "v") >= 0, Sent());
            Clear();
            Up(Vk.Capital); Clear();

            // А Win и Alt не возвращает: между нашим нажатием и настоящим отпусканием
            // человеком не происходит ничего, и Windows принимает это за одиночную
            // клавишу — «Пуск» поверх работы.
            s = Fresh();
            s.MapCapsLock = ModKey.LWin;
            Use(s);
            Down(Vk.Capital);
            Clear();
            _releaseKeep.Invoke(_eng, new object[] { s, true });
            Check("сброс не возвращает Win: одиночная Win открыла бы «Пуск»",
                  Sent().IndexOf(N(Vk.LWin) + "v") < 0, Sent());
            Clear();
            Up(Vk.Capital); Clear();

            // И не возвращает вовсе, если новые настройки эту клавишу уже не разбирают:
            // отпускание отсечётся раньше всего, и код останется зажатым навсегда.
            s = Fresh();
            s.MapCapsLock = ModKey.LCtrl;
            Use(s);
            Down(Vk.Capital);
            Clear();
            Settings off = Fresh();
            off.MapCapsLock = ModKey.LCtrl;
            off.Enabled = false;
            _releaseKeep.Invoke(_eng, new object[] { off, true });
            Check("выключенные переназначения не оставляют control нажатым",
                  Sent().IndexOf(N(Vk.LControl) + "v") < 0, Sent());
            Clear();
            _eng.Apply(Fresh());
            Up(Vk.Capital); Clear();

            // Аккорд поверх начатой навигации не возвращает снятый заменителем control:
            // следующая стрелка ушла бы Ctrl+Page Up, то есть на соседнюю вкладку.
            s = Fresh();
            s.FnSubstitute = ModKey.CapsLock;
            s.MapCapsLock = ModKey.LCtrl;
            s.FnNavigation = true;
            // «сначала F-клавиши»: с зажатым заменителем ветка переворачивается в медиа,
            // и назначенное действие правда срабатывает.
            s.MediaFirst = false;
            s.FKeys[2] = "sys.taskview";   // готовый аккорд: он и ходит через ClearHeld
            Use(s);
            Down(Vk.Capital);
            Down(Vk.Up);                 // навигация началась, control снят с дороги
            Clear();
            Down(Vk.F1 + 2); Up(Vk.F1 + 2);
            Check("аккорд не возвращает control поверх начатой навигации",
                  Sent().IndexOf(N(Vk.LControl) + "v") < 0, Sent());
            Clear();
            Up(Vk.Up); Up(Vk.Capital); Clear();

            // Признак призрачного Ctrl снимается только его же отпусканием, а оно приходит
            // мимо разбора, когда переназначения выключены. Оставшись стоять, он заставлял
            // EmitText вернуть control нажатым — тот, которого Windows не держит.
            s = Fresh();
            Use(s);
            Send(Vk.LControl, 0x21D, true, false);   // AltGr привёл призрака
            Settings paused = Fresh();
            paused.Enabled = false;
            _eng.Apply(paused);
            Send(Vk.LControl, 0x21D, false, false);  // отпускание пришло на паузе
            _eng.Apply(Fresh());
            Clear();
            Check("признак призрачного Ctrl не остаётся после паузы", !PhantomCtrl(),
                  "признак стоит — раскладка Apple вернёт control нажатым");

            // Счёт регистра идёт по тому, держит ли Capital сама Windows. За неё здесь
            // отвечает шов: настоящую клавиатуру стенд не трогает.
            s = Fresh();
            s.MapRAlt = ModKey.CapsLock;        // и сам Caps Lock остаётся Caps Lock
            Use(s);
            bool caps0 = CapsOn();
            Engine.HeldProbe = delegate(int probe) { return false; };
            Down(Vk.Capital);                   // Windows Capital ещё не держала
            bool caps1 = CapsOn();
            Engine.HeldProbe = delegate(int probe) { return probe == Vk.Capital; };
            DownE(Vk.RMenu);                    // вторая клавиша: для Windows это повтор
            bool caps2 = CapsOn();
            Check("вторая клавиша на Caps Lock не переворачивает регистр второй раз",
                  caps1 != caps0 && caps2 == caps1,
                  "было=" + caps0 + ", после Caps Lock=" + caps1 + ", после ⌥=" + caps2);

            // Настоящую Caps Lock отпустили — Windows сняла Capital за всех. Следующее
            // её нажатие она переворачивает, и наш счёт обязан пойти следом: своё
            // множество источников на это не отвечало, и лампочка расходилась со счётом.
            Engine.HeldProbe = delegate(int probe) { return false; };
            Up(Vk.Capital);
            bool caps3 = CapsOn();
            Down(Vk.Capital);
            Check("после отпускания настоящей Caps Lock счёт идёт за лампочкой",
                  CapsOn() != caps3, "счёт остался прежним, а Windows перевернула");
            Engine.HeldProbe = null;
            UpE(Vk.RMenu); Up(Vk.Capital); Clear();

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

            // Зажали обе ⌘, отпустили одну — сочетания продолжают узнаваться.
            s = Fresh();
            Use(s);
            Down(Vk.LWin);
            DownE(Vk.RWin);
            UpE(Vk.RWin);           // правую отпустили, левую держим
            Clear();
            bool sw = Down(0x43);   // ⌘C
            Seq("одна из двух ⌘ отпущена — сочетание всё ещё узнаётся", sw,
                N(Vk.LControl) + "v", "Cv", "C^", N(Vk.LControl) + "^");
            Up(0x43); Up(Vk.LWin); Clear();

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

            // Выключенное сочетание на удержании не должно слать ничего — и уж точно
            // не Tab при зажатой клавише Windows: это «Представление задач».
            s = Fresh();
            s.MacShortcutsOff = new string[] { "copy" };
            Use(s);
            Down(Vk.LWin); Clear();
            Down(0x43); Down(0x43); Down(0x43);
            Check("выключенное ⌘C на удержании не шлёт Tab",
                  Sent().IndexOf(N(Vk.Tab)) < 0, Sent());
            Clear();
            Up(0x43); Up(Vk.LWin); Clear();

            // Отпускание клавиши, нажатия которой мы не брали, — не наше.
            s = Fresh();
            Use(s);
            bool sw0 = Down(Vk.LMenu);          // назначения нет: нажатие ушло как есть
            Clear();
            Settings changed = Fresh();
            changed.MapLAlt = ModKey.LCtrl;     // назначение сменили, пока клавишу держат
            _eng.Apply(changed);
            Clear();
            bool sw1 = Up(Vk.LMenu);
            Check("сменили назначение под зажатой клавишей — отпускание не глотаем",
                  !sw0 && !sw1, "нажатие=" + sw0 + ", отпускание=" + sw1);
            Clear();

            // Выключенная клавиша не должна вернуться нажатой: снять её потом нечем.
            s = Fresh();
            s.MapLCtrl = ModKey.None;
            s.SpaceSearch = Settings.SpaceCtrl;
            Use(s);
            Down(Vk.LControl); Clear();
            Down(Vk.Space);
            string ctrlName = N(Vk.LControl);
            Check("выключенный control не возвращается нажатым",
                  Sent().IndexOf(ctrlName) < 0, Sent());
            Clear();
            Up(Vk.Space); Up(Vk.LControl); Clear();

            // Control, подставленный вместо Caps Lock, виден сочетаниям.
            s = Fresh();
            s.MapCapsLock = ModKey.LCtrl;
            Use(s);
            Down(Vk.Capital); Clear();
            bool swc = Down(Vk.Space);
            Seq("⌃Space работает, когда control висит на Caps Lock", swc,
                N(Vk.LControl) + "^", N(Vk.LWin) + "v", N(Vk.Space) + "v");
            Up(Vk.Space); Up(Vk.Capital); Clear();

            // Заменитель Fn на левой ⌘: после ⌘Tab и F-клавиши Windows не должна
            // остаться зажатой — снять её было бы нечем.
            s = Fresh();
            s.FnSubstitute = ModKey.LWin;
            s.MediaFirst = true;
            Use(s);
            Down(Vk.LWin);
            Down(Vk.Tab); Up(Vk.Tab);
            Down(Vk.F1); Up(Vk.F1);
            Up(Vk.LWin);
            // Считаем по всей записи, а не по хвосту: Clear перед отпусканием выбрасывал
            // ровно те события, в которых улика, и обе стороны выходили нулями — проверка
            // не могла провалиться ни при каком поведении.
            int downs = Count(Sent(), N(Vk.LWin) + "v");
            int ups = Count(Sent(), N(Vk.LWin) + "^");
            Check("⌘ в роли Fn не остаётся зажатой после ⌘Tab", downs <= ups,
                  "нажали " + downs + ", отпустили " + ups + ": " + Sent());
            Clear();

            // Обратный порядок: сперва F-клавиша сняла заменитель, потом ⌘+Tab снял
            // подставленную Win насовсем. Возвращать её по концу удержания нельзя —
            // при открытом переключателе окон следующий Tab уйдёт как Win+Alt+Tab.
            s = Fresh();
            s.FnSubstitute = ModKey.LWin;
            s.MediaFirst = true;
            s.CmdTabSwitchesWindows = true;
            Use(s);
            Down(Vk.LWin);
            Down(Vk.F1);            // заменитель снят: Win^ ушла
            Down(Vk.Tab);           // переключатель открыт, Win снята и забыта
            Clear();
            Up(Vk.F1);
            Check("при открытом переключателе окон Win не нажимают заново",
                  Count(Sent(), N(Vk.LWin) + "v") == 0,
                  "следующий Tab уйдёт как Win+Alt+Tab: " + Sent());
            Clear();
            Up(Vk.Tab); Up(Vk.LWin); Clear();

            // Сброс зажатого не должен открывать «Пуск»: Windows считает командой
            // клавишу Win, нажатую и отпущенную без ничего между ними.
            s = Fresh();
            Use(s);
            Down(Vk.LWin);
            Clear();
            _release.Invoke(_eng, null);
            Check("сброс зажатого не открывает «Пуск»",
                  Sent().StartsWith("щитv щит^"), Sent());
            Clear();

            // Аккорд не возвращает нажатым переключатель: второе нажатие Caps Lock
            // зажигает лампочку, а Escape закрывает диалог.
            s = Fresh();
            s.MapCapsLock = ModKey.Escape;
            Use(s);
            Down(Vk.Capital);
            Down(Vk.LWin); Clear();
            Down(0x43);                     // ⌘C при зажатом Caps Lock
            Check("аккорд не возвращает Escape нажатым",
                  Sent().IndexOf(N(Vk.Escape) + "v") < 0, Sent());
            Clear();
            Up(0x43); Up(Vk.LWin); Up(Vk.Capital); Clear();

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

            DeadKeyUnderAlt();
            BrokenLayoutFile();
        }

        /// <summary>
        /// Файл раскладки из пользовательской папки пишет не программа: он может быть
        /// чужим, оборванным или неполным. Ни один такой не должен ни ронять разбор,
        /// ни превращать нажатие в пустоту.
        /// </summary>
        static void BrokenLayoutFile()
        {
            Head("Битый файл раскладки");
            string dir = Path.Combine(Path.GetTempPath(), "magickeys-proba");
            string p = Path.Combine(dir, "proba.xml");
            try
            {
                Directory.CreateDirectory(dir);

                File.WriteAllText(p, "<Chuzhoe sam=\"po sebe\"/>", Encoding.UTF8);
                Check("чужой корень раскладкой не считается", Load(p) == null, "вернул раскладку");

                File.WriteAllText(p, "<AppleLayout id=\"proba\"><Key scan=\"ZZ\" base=\"a\"/></AppleLayout>",
                                  Encoding.UTF8);
                Check("чепуха вместо скан-кода не роняет разбор", Load(p) == null, "вернул раскладку");

                File.WriteAllText(p, "<AppleLayout id=\"proba\"><Key scan=\"1E\" base=\"a\"/>", Encoding.UTF8);
                Check("оборванный файл не роняет разбор", Load(p) == null, "вернул раскладку");

                File.WriteAllText(p,
                    "<AppleLayout id=\"proba\"><Key scan=\"1E\" base=\"a\"/><Dead from=\"^a\"/></AppleLayout>",
                    Encoding.UTF8);
                AppleLayoutFile f = Load(p);
                Check("переход без «to» не превращает нажатие в пустоту",
                      f != null && f.Compose("^", "a") == null, "получили «" +
                      (f == null ? "разбор не удался" : f.Compose("^", "a")) + "»");
            }
            catch (Exception e) { Check("битый файл раскладки", false, e.Message); }
            finally
            {
                try { File.Delete(p); } catch { }
                try { Directory.Delete(dir); } catch { }
            }
        }

        /// <summary>Разбор, который не роняет стенд: битый файл вправе бросить.</summary>
        static AppleLayoutFile Load(string path)
        {
            try { return AppleLayoutFile.Load(path); }
            catch { return null; }
        }

        /// <summary>
        /// Знак ударения, повисший на мёртвой клавише, выпускают в том числе при зажатом
        /// Alt — а под ним синтетический символ приходит как WM_SYSCHAR: пропадает сам
        /// и открывает строку меню. Щит здесь обязателен, и ставиться он должен по тому,
        /// что держит Windows, а не по тому, просили ли считать ⌥ третьим уровнем.
        /// </summary>
        static void DeadKeyUnderAlt()
        {
            // Раскладка окна берётся у того окна, что сейчас впереди, — какое бы это
            // ни было. Привязываем испанскую именно к его языку: у неё «´» на 0x1A
            // объявлена мёртвой на базовом уровне.
            IntPtr w = Native.GetForegroundWindow();
            uint tid = w == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(w, IntPtr.Zero);
            int lang = (int)(Native.GetKeyboardLayout(tid).ToInt64() & 0xFFFF);

            Settings s = Fresh();
            s.AppleLayoutEnabled = true;
            s.OptLevel = OptLevel.Off;          // ⌥ третьим уровнем не просили — он меню
            s.SetLayoutFor(lang, "es");
            Use(s);

            bool sw = Send(0xDB, 0x1A, true, false);
            Send(0xDB, 0x1A, false, false);
            Check("мёртвая клавиша ничего не печатает сразу", sw && Sent().Length == 0, Sent());
            Clear();

            Down(Vk.LMenu); Clear();
            Send(0x41, 0x1E, true, false);
            Send(0x41, 0x1E, false, false);
            int alt = Sent().IndexOf(N(Vk.LMenu) + "^");
            int sign = Sent().IndexOf("´");
            Check("знак ударения не выпускают под зажатым Alt",
                  alt >= 0 && sign > alt, Sent());
            Clear();
            Up(Vk.LMenu); Clear();

            // Клавишу, взятую раскладкой, нельзя отдавать посреди удержания: автоповтор
            // ушёл бы в приложение настоящим нажатием, а отпускание съела бы запись
            // о съеденном — и в приложении клавиша осталась бы зажатой.
            Settings hold = Fresh();
            hold.AppleLayoutEnabled = true;
            hold.OptLevel = OptLevel.Off;
            hold.SetLayoutFor(lang, "es");
            Use(hold);
            bool hd = Send(0xBA, 0x28, true, false);    // «;» — испанская её подменяет
            Down(Vk.LControl);                          // control нажали, не отпуская
            bool hr = Send(0xBA, 0x28, true, false);    // автоповтор
            bool hu = Send(0xBA, 0x28, false, false);
            Check("раскладка не отдаёт клавишу посреди удержания", hd && hr == hu,
                  "нажатие=" + hd + ", повтор=" + hr + ", отпускание=" + hu);
            Clear();
            Up(Vk.LControl); Clear();

            // Таблица раскладки составлена по тем скан-кодам, которых ждёт Windows,
            // а ISO-клавиатура Apple шлёт две клавиши наоборот. Спросив таблицу сырым
            // скан-кодом, мы брали строку соседней клавиши: слева от «Z» печаталось то,
            // что напечатано слева от «1».
            Settings isoLay = Fresh();
            isoLay.AppleLayoutEnabled = true;
            isoLay.Physical = PhysLayout.Iso;
            isoLay.SetLayoutFor(lang, "en");
            Use(isoLay);
            Send(Vk.Oem3, 0x29, true, false);
            Check("раскладка и перестановка ISO не спорят за одну клавишу",
                  Sent().IndexOf("scan56") < 0,
                  "таблица отступила, и клавиша ушла переставленной: " + Sent());
            Send(Vk.Oem3, 0x29, false, false);
            Clear();
        }

        /// <summary>
        /// Автоповтор клавиши, ушедшей в приложение, не достаётся никакому слою.
        ///
        /// Windows начинает повторять через полсекунды удержания, и повторное нажатие
        /// от первого неотличимо. Взяв его себе, слой съедает потом и отпускание —
        /// в приложении клавиша остаётся зажатой навсегда, а снять её нечем: человек
        /// её уже отпустил. Достаточно держать ← и потянуться к ⌘.
        /// </summary>
        static void RepeatStaysWhereItWent()
        {
            Head("Автоповтор идёт туда же, куда ушло нажатие");

            // Слой аккордов macOS: ⌘← из таблицы.
            Settings s = Fresh();
            Use(s);
            bool first = Down(Vk.Left);
            Down(Vk.Left);                      // автоповтор без ⌘
            Down(Vk.LWin); Clear();             // ⌘ нажали, не отпуская ←
            bool repeat = Down(Vk.Left);        // автоповтор УЖЕ при зажатой ⌘
            bool up = Up(Vk.Left);
            Check("аккорд не отбирает автоповтор клавиши, ушедшей насквозь",
                  !first && !repeat && !up,
                  "нажатие=" + first + ", повтор=" + repeat + ", отпускание=" + up +
                  ", пришло «" + Sent() + "»");
            Clear(); Up(Vk.LWin); Clear();

            // Контрольный опыт: без автоповтора между нажатием и ⌘ отпускание доходит —
            // значит проверка выше меряет автоповтор, а не порядок модификаторов.
            Use(s);
            Down(Vk.Left); Down(Vk.LWin); Clear();
            bool ctlUp = Up(Vk.Left);
            Check("контроль: без автоповтора отпускание всё равно доходит", !ctlUp,
                  "отпускание=" + ctlUp);
            Clear(); Up(Vk.LWin); Clear();

            // Промах мимо таблицы: ⌘K глотается, и отпускание «K» съедалось бы.
            Use(s);
            Down(Vk.K); Down(Vk.K);
            Down(Vk.LWin); Clear();
            bool kRepeat = Down(Vk.K);
            bool kUp = Up(Vk.K);
            Check("промах мимо таблицы не отбирает автоповтор буквы",
                  !kRepeat && !kUp, "повтор=" + kRepeat + ", отпускание=" + kUp +
                  ", пришло «" + Sent() + "»");
            Clear(); Up(Vk.LWin); Clear();

            // Навигация Fn: заменитель нажали посреди удержания стрелки.
            Settings n = Fresh();
            n.FnNavigation = true; n.MacShortcuts = false;
            n.FnSubstitute = ModKey.RAlt;
            Use(n);
            Down(Vk.Up); Down(Vk.Up);
            DownE(Vk.RMenu); Clear();
            bool nRepeat = Down(Vk.Up);
            bool nUp = Up(Vk.Up);
            Check("навигация Fn не отбирает автоповтор стрелки",
                  !nRepeat && !nUp, "повтор=" + nRepeat + ", отпускание=" + nUp +
                  ", пришло «" + Sent() + "»");
            Clear(); UpE(Vk.RMenu); Clear();

            // Раскладка Apple: ⌥ нажали посреди удержания буквы.
            Settings l = Fresh();
            l.AppleLayoutEnabled = true; l.OptLevel = OptLevel.AnyOption;
            l.MacShortcuts = false;
            Use(l);
            Down(Vk.A); Down(Vk.A);
            Down(Vk.LMenu); Clear();
            bool aRepeat = Down(Vk.A);
            bool aUp = Up(Vk.A);
            Check("раскладка Apple не отбирает автоповтор буквы",
                  !aRepeat && !aUp, "повтор=" + aRepeat + ", отпускание=" + aUp +
                  ", пришло «" + Sent() + "»");
            Clear(); Up(Vk.LMenu); Clear();
        }

        /// <summary>
        /// Код, уже снятый у Windows, не снимают вторым разом и не возвращают нажатым.
        /// Win и Alt снимают без возврата нарочно: сами по себе они открывают «Пуск»
        /// и строку меню, а нажатая Win при открытом переключателе окон превращает
        /// следующий Tab в Win+Alt+Tab.
        /// </summary>
        static void LiftedOnlyOnce()
        {
            Head("Снятое не снимают дважды");
            Settings s = Fresh();
            s.FnSubstitute = ModKey.LWin;
            Use(s);
            Down(Vk.LWin);
            Down(Vk.C); Up(Vk.C);               // ⌘C: ClearHeld снял Win без возврата
            Clear();
            Down(Vk.F4); Up(Vk.F4);             // Fn+F4 — настоящая F4
            Check("уже снятый код не снимают вторым разом",
                  Count(Sent(), N(Vk.LWin) + "^") == 0, Sent());
            Check("и не возвращают нажатым: Win снимали без возврата",
                  Count(Sent(), N(Vk.LWin) + "v") == 0, Sent());
            Clear(); Up(Vk.LWin); Clear();

            // Контрольный опыт: без предварительного снятия Fn+F4 щит ставит и снимает
            // Win честно — значит проверки выше меряют повторное снятие, а не то, что
            // щита нет вовсе.
            Use(s);
            Down(Vk.LWin); Clear();
            Down(Vk.F4); Up(Vk.F4);
            Check("контроль: неснятый код Fn+F4 снимает сам",
                  Count(Sent(), N(Vk.LWin) + "^") == 1, Sent());
            Clear(); Up(Vk.LWin); Clear();

            // ⌘+Tab после ⌘C: ReleaseWindowsKey слал Win^ вторым разом.
            Settings t = Fresh();
            t.CmdTabSwitchesWindows = true;
            Use(t);
            Down(Vk.LWin);
            Down(Vk.C); Up(Vk.C);
            Clear();
            Down(Vk.Tab);
            Check("⌘+Tab не снимает уже снятую Win второй раз",
                  Count(Sent(), N(Vk.LWin) + "^") == 0, Sent());
            Clear(); Up(Vk.Tab); Up(Vk.LWin); Clear();
        }

        /// <summary>
        /// Автоповтор модификатора после аккорда. Аккорд снимает код у Windows без
        /// возврата и запоминает снятие; автоповтор нажимает его заново — и запись
        /// обязана уйти, иначе отпускание клавиши уходит по ветке «уже снято»,
        /// то есть не уходит вовсе.
        ///
        /// С заводскими настройками это ⌘C плюс полсекунды удержания ⌘: дальше каждая
        /// буква становится Win+буквой — Win+L запирает экран, Win+E открывает
        /// проводник. Общий сброс не чинит: отпускать ему уже нечего.
        /// </summary>
        static void RepressAfterChord()
        {
            Head("Автоповтор модификатора после аккорда");
            Settings s = Fresh();                 // всё заводское
            Use(s);
            Down(Vk.LWin);
            Down(Vk.C); Up(Vk.C);                 // ⌘C: ClearHeld снял Win без возврата
            Down(Vk.LWin);                        // автоповтор ⌘ — Win нажали заново
            Clear();
            Up(Vk.LWin);
            Check("после автоповтора ⌘ клавиша Windows отпускается",
                  Count(Sent(), N(Vk.LWin) + "^") == 1,
                  "Win осталась зажатой, и снять её нечем: " + Sent());
            Clear();

            // Контрольный опыт: без автоповтора отпускать нечего — Win уже снята
            // аккордом, и второе снятие было бы отпусканием без пары.
            Use(s);
            Down(Vk.LWin);
            Down(Vk.C); Up(Vk.C);
            Clear();
            Up(Vk.LWin);
            Check("контроль: без автоповтора Win второй раз не снимают",
                  Count(Sent(), N(Vk.LWin) + "^") == 0, Sent());
            Clear();
        }

        /// <summary>
        /// Один код на двух клавишах, и у одной он снят аккордом. Отпуская вторую,
        /// снимать надо: «его держит ещё кто-то» — про то, что Windows держит сейчас,
        /// а не про то, что мы когда-то посылали.
        /// </summary>
        static void TwoKeysOneCode()
        {
            Head("Один код на двух клавишах");
            Settings s = Fresh();
            s.MapRAlt = ModKey.RWin;              // ⌘ живёт и на правом ⌥ тоже
            Use(s);
            DownE(Vk.RWin);                       // правая ⌘ жмёт Win
            Down(Vk.C); Up(Vk.C);                 // ⌘C: ClearHeld снял её код
            Clear();
            DownE(Vk.RMenu);                      // вторая клавиша жмёт тот же код
            string afterDown = Sent(); Clear();
            UpE(Vk.RMenu);
            Check("отпуская вторую клавишу, снимаем её код: у первой он снят",
                  Count(afterDown, N(Vk.RWin) + "v") == 1
                      && Count(Sent(), N(Vk.RWin) + "^") == 1,
                  "нажатие «" + afterDown + "», отпускание «" + Sent() + "»");
            Clear();
            UpE(Vk.RWin); Clear();

            // Контрольный опыт: пока обе держат неснятый код, отпускание первой
            // его не снимает — вторая правда держит.
            Use(s);
            DownE(Vk.RWin);
            DownE(Vk.RMenu);
            Clear();
            UpE(Vk.RWin);
            Check("контроль: пока вторая клавиша держит код, первая его не снимает",
                  Count(Sent(), N(Vk.RWin) + "^") == 0, Sent());
            Clear();
            UpE(Vk.RMenu); Clear();
        }

        /// <summary>
        /// Alt переключателя окон убирается с дороги у всех, кто посылает в приложение.
        ///
        /// Он не стоит ни за какой физической клавишей: подставленную Win мы сняли,
        /// а Alt нажали сами. Всё посланное под ним меняет смысл: настоящая F4
        /// становится Alt+F4 — закрытием окна, синтетическая Home — Alt+Home, то есть
        /// уходом браузера на домашнюю страницу.
        /// </summary>
        static void SwitcherAltGetsOutOfTheWay()
        {
            Head("Переключатель окон и то, что мы посылаем");

            // Верхний ряд: настоящая F4 при открытом переключателе.
            Settings s = Fresh();
            s.CmdTabSwitchesWindows = true;
            s.MediaFirst = true;
            s.FnSubstitute = ModKey.RAlt;
            Use(s);
            Down(Vk.LWin);                      // ⌘ зажата: подставлена Win
            Down(Vk.Tab); Up(Vk.Tab);           // переключатель открыт, Alt наш
            DownE(Vk.RMenu);                    // заменитель Fn
            Clear();
            Down(Vk.F4);
            Check("F4 при открытом переключателе не уходит под нашим Alt",
                  Count(Sent(), N(Vk.LMenu) + "^") == 1,
                  "это Alt+F4, закрытие окна: " + Sent());
            Clear();
            Up(Vk.F4); UpE(Vk.RMenu); Up(Vk.LWin); Clear();

            // Навигация Fn: синтетическая Home при открытом переключателе.
            Settings n = Fresh();
            n.CmdTabSwitchesWindows = true;
            n.FnNavigation = true;
            n.MacShortcuts = false;
            n.FnSubstitute = ModKey.RAlt;
            Use(n);
            Down(Vk.LWin);
            Down(Vk.Tab); Up(Vk.Tab);
            DownE(Vk.RMenu);
            Clear();
            Down(Vk.Left);
            Check("Fn+← при открытом переключателе не уходит под нашим Alt",
                  Count(Sent(), N(Vk.LMenu) + "^") == 1,
                  "это Alt+Home, уход браузера на домашнюю страницу: " + Sent());
            Clear();
            Up(Vk.Left); UpE(Vk.RMenu); Up(Vk.LWin); Clear();

            // F-клавиша, которую мы НЕ берём: заменителя не держат, клавиша уходит
            // в приложение своим ходом — и обязана уйти без нашего Alt. С завода так
            // ведут себя F5, F6 и весь F13–F19: у них назначение «Оставить как есть».
            Settings p = Fresh();
            p.CmdTabSwitchesWindows = true;
            p.MediaFirst = false;            // «сначала F-клавиши»: F4 уходит настоящей
            p.FnSubstitute = ModKey.None;
            Use(p);
            Down(Vk.LWin);
            Down(Vk.Tab); Up(Vk.Tab);
            Clear();
            bool through = Down(Vk.F4);
            Check("F4, ушедшая насквозь, не уходит под нашим Alt",
                  !through && Count(Sent(), N(Vk.LMenu) + "^") == 1,
                  "проглочено=" + through + ", это Alt+F4, закрытие окна: " + Sent());
            Clear();
            Up(Vk.F4); Up(Vk.LWin); Clear();

            // Контрольный опыт: без переключателя снимать нечего, и лишнего Alt^ нет.
            Use(s);
            DownE(Vk.RMenu);
            Clear();
            Down(Vk.F4);
            Check("контроль: без переключателя лишнего Alt^ не посылают",
                  Count(Sent(), N(Vk.LMenu) + "^") == 0, Sent());
            Clear();
            Up(Vk.F4); UpE(Vk.RMenu); Clear();

            // Заменитель Fn на ⌘ поверх открытого переключателя: возвращать Win нельзя,
            // её сняли насовсем — иначе следующий Tab уйдёт как Win+Alt+Tab.
            Settings c = Fresh();
            c.CmdTabSwitchesWindows = true;
            c.FnNavigation = true;
            c.MacShortcuts = false;
            c.FnSubstitute = ModKey.LWin;
            Use(c);
            Down(Vk.LWin);
            Down(Vk.Tab); Up(Vk.Tab);           // переключатель открыт, Win снята насовсем
            Down(Vk.Left);                      // Fn+← : снимать нечего
            Down(Vk.LWin);                      // автоповтор ⌘
            Clear();
            Up(Vk.Left);
            Check("после ⌘+Tab заменитель не возвращает Win",
                  Count(Sent(), N(Vk.LWin) + "v") == 0,
                  "Win нажата поверх переключателя: следующий Tab уйдёт как Win+Alt+Tab, "
                  + "а любая буква как Win+буква: " + Sent());
            Clear();
            Up(Vk.LWin); Clear();
        }

        /// <summary>
        /// Общий сброс не отдаёт слою клавишу, которую человек ещё держит.
        ///
        /// Сброс случается сам: переподключение по Bluetooth, блокировка экрана,
        /// пробуждение, движение галочки в окне. Стерев записи о том, кто хозяин
        /// клавиши, программа отдавала её первому же слою: автоповтор доставался ему,
        /// а отпускание он же и съедал — настоящая клавиша оставалась зажатой
        /// в приложении навсегда, и снять её было нечем.
        /// </summary>
        static void ResetKeepsTheOwner()
        {
            Head("Сброс не отбирает клавишу у приложения");

            // Стрелка ушла насквозь, случился сброс, человек дожал ⌘.
            Settings s = Fresh();
            Use(s);
            bool first = Down(Vk.Left);
            // Windows держит ←: это и есть то, что сброс обязан у неё спросить.
            Engine.HeldProbe = delegate(int probe) { return probe == Vk.Left; };
            try
            {
                _release.Invoke(_eng, null);
                Down(Vk.LWin); Clear();
                bool repeat = Down(Vk.Left);
                bool up = Up(Vk.Left);
                Check("после сброса аккорд не отбирает клавишу, ушедшую насквозь",
                      !first && !repeat && !up,
                      "нажатие=" + first + ", повтор=" + repeat + ", отпускание=" + up +
                      ", пришло «" + Sent() + "»");
            }
            finally { Engine.HeldProbe = null; }
            Clear(); Up(Vk.LWin); Clear();

            // Контрольный опыт: Windows стрелку не держит — значит человек её отпустил,
            // и запись обязана уйти, иначе слои не возьмут её уже никогда.
            Use(s);
            Down(Vk.Left);
            Engine.HeldProbe = delegate(int probe) { return false; };
            try
            {
                _release.Invoke(_eng, null);
                Down(Vk.LWin); Clear();
                bool again = Down(Vk.Left);
                Check("контроль: отпущенную клавишу сброс забывает", again,
                      "запись осталась, и слои её больше не возьмут: " + Sent());
            }
            finally { Engine.HeldProbe = null; }
            Clear(); Up(Vk.Left); Up(Vk.LWin); Clear();

            // Настоящая F-клавиша: её мы нажали в Windows сами, и до отпускания
            // человеком отдавать её нельзя.
            Settings f = Fresh();
            f.MediaFirst = true;
            f.FnSubstitute = ModKey.RAlt;
            Use(f);
            DownE(Vk.RMenu);
            Down((Vk.F1 + 6));                        // настоящая F7 ушла в приложение нашим нажатием
            Engine.HeldProbe = delegate(int probe) { return probe == (Vk.F1 + 6); };
            try
            {
                _release.Invoke(_eng, null);
                DownE(Vk.RMenu); Clear();
                bool repeat = Down((Vk.F1 + 6));
                bool up = Up((Vk.F1 + 6));
                Check("после сброса верхний ряд не отбирает настоящую F-клавишу",
                      !repeat && !up,
                      "повтор=" + repeat + ", отпускание=" + up + ", пришло «" + Sent() + "»");
            }
            finally { Engine.HeldProbe = null; }
            Clear(); UpE(Vk.RMenu); Clear();
        }

        /// <summary>
        /// Одиночная клавиша защёлкивает решение на первом нажатии — как верхний ряд.
        ///
        /// Иначе нажатие с «Оставить как есть» уходило насквозь без следа, а автоповтор
        /// разбирался заново: сменив назначение ⌧ под её удержанием, человек получал
        /// настоящий Num Lock, уже выключивший блок, и съеденное нами отпускание.
        /// </summary>
        static void SingleKeyLatches()
        {
            Head("Одиночная клавиша защёлкивает решение");

            Settings pass = Fresh(); pass.NumpadClear = "none";
            Use(pass);
            bool first = Down(Vk.NumLock);
            Settings del = Fresh(); del.NumpadClear = "key.delete";
            // Windows держит ⌧: смена настроек идёт через общий сброс, а тот сверяет
            // записи о владении именно с ней.
            Engine.HeldProbe = delegate(int probe) { return probe == Vk.NumLock; };
            bool repeat, up;
            try
            {
                _eng.Apply(del); Clear();
                repeat = Down(Vk.NumLock);
                up = Up(Vk.NumLock);
            }
            finally { Engine.HeldProbe = null; }
            Check("смена назначения под зажатой ⌧ не съедает её отпускание",
                  !first && !repeat && !up,
                  "нажатие=" + first + ", повтор=" + repeat + ", отпускание=" + up +
                  ", пришло «" + Sent() + "»");
            Clear();

            // Контрольный опыт: нажатая при живом назначении ⌧ и берётся, и отпускается
            // нами — значит проверка выше меряет защёлку, а не мёртвый разбор.
            Use(del);
            bool took = Down(Vk.NumLock);
            bool tookUp = Up(Vk.NumLock);
            Check("контроль: с живым назначением ⌧ берут и отпускают",
                  took && tookUp, "нажатие=" + took + ", отпускание=" + tookUp);
            Clear();
        }

        /// <summary>
        /// У ⌧ под зажатой ⌘ один хозяин, а не два.
        ///
        /// Промах мимо таблицы macOS судил по ответу Busy, взятому ДО разбора одиночных
        /// клавиш, а глотал после него: запись об одиночной клавише переживала
        /// собственное отпускание, и второе нажатие ⌘+⌧ отправляло в Windows настоящий
        /// Num Lock — цифровой блок становился навигационным.
        /// </summary>
        static void OneOwnerForNumpadClear()
        {
            Head("Один хозяин у ⌧ под ⌘");
            Settings s = Fresh();
            s.NumpadClear = "none";          // ⌧ уходит насквозь
            s.MacShortcuts = true;
            Use(s);

            Down(Vk.LWin);
            Clear();
            bool first = Down(Vk.NumLock);
            bool firstUp = Up(Vk.NumLock);
            bool second = Down(Vk.NumLock);
            bool secondUp = Up(Vk.NumLock);
            // Хозяин у клавиши один — разбор одиночных, который взял её первым.
            // Промах мимо таблицы, глотавший её вторым, делал первую пару непохожей
            // на вторую: запись об одиночной переживала собственное отпускание.
            Check("оба нажатия ⌧ под ⌘ одинаковы",
                  first == second && firstUp == secondUp,
                  "первая пара " + first + firstUp + ", вторая " + second + secondUp +
                  ": " + Sent());
            Clear();
            Up(Vk.LWin); Clear();

            // Контрольный опыт: без ⌘ обе пары одинаковы — значит проверка выше
            // меряет промах мимо таблицы, а не разбор одиночных клавиш.
            Use(s);
            bool a1 = Down(Vk.NumLock); bool a2 = Up(Vk.NumLock);
            bool b1 = Down(Vk.NumLock); bool b2 = Up(Vk.NumLock);
            Check("контроль: без ⌘ оба нажатия ⌧ одинаковы",
                  a1 == b1 && a2 == b2, "" + a1 + a2 + " против " + b1 + b2);
            Clear();
        }

        /// <summary>
        /// Модификатор, чьё нажатие ушло в приложение, остаётся его до отпускания.
        ///
        /// Слой модификаторов решал «наше ли это» заново на каждом событии, и автоповтор
        /// доставался ему: нажатие ушло в приложение, отпускание съели мы — Alt оставался
        /// зажатым в Windows навсегда. Достаточно было держать ⌥ на клавиатуре ноутбука,
        /// пока просыпается Magic Keyboard: заводская пауза «Только с Magic Keyboard»
        /// кончается ровно между нажатием и его автоповтором.
        /// </summary>
        static void ModifierThroughStaysThrough()
        {
            Head("Модификатор, ушедший в приложение");

            // Пауза кончилась между нажатием и автоповтором.
            Settings s = Fresh();
            s.PauseWhenAppleAbsent = true;
            s.MapLAlt = ModKey.LCtrl;
            Apple(false);
            Use(s);
            bool went = Down(Vk.LMenu);      // на паузе — мимо нас, в приложение
            Apple(true);                     // клавиатура проснулась
            Clear();
            bool repeat = Down(Vk.LMenu);    // автоповтор уже не на паузе
            bool up = Up(Vk.LMenu);
            Check("проснувшаяся клавиатура не отбирает ⌥, нажатие которого уже ушло",
                  !went && !repeat && !up,
                  "нажатие=" + went + ", повтор=" + repeat + ", отпускание=" + up +
                  ", пришло «" + Sent() + "»: левый ⌥ остался зажатым в Windows");
            Clear();
            Apple(false);

            // Контрольный опыт: без паузы клавишу берут с первого нажатия — значит
            // проверка выше меряет «нажатие ушло», а не мёртвый разбор.
            Settings live = Fresh();
            live.MapLAlt = ModKey.LCtrl;
            Apple(true);
            Use(live);
            bool took = Down(Vk.LMenu);
            bool tookUp = Up(Vk.LMenu);
            Check("контроль: без паузы ⌥ берут с первого нажатия",
                  took && tookUp, "нажатие=" + took + ", отпускание=" + tookUp);
            Clear();

            // И смена назначения под зажатой клавишей — та же беда с другой стороны.
            Settings plain = Fresh();
            Use(plain);
            bool wentPlain = Down(Vk.LMenu);     // ⌥ никуда не переназначен: ушёл насквозь
            Settings mapped = Fresh();
            mapped.MapLAlt = ModKey.LCtrl;
            bool rep2, up2;
            // Windows держит ⌥: смена настроек идёт через общий сброс, а тот сверяет
            // записи о владении именно с ней.
            Engine.HeldProbe = delegate(int probe) { return probe == Vk.LMenu; };
            try
            {
                _eng.Apply(mapped); Clear();
                rep2 = Down(Vk.LMenu);
                up2 = Up(Vk.LMenu);
            }
            finally { Engine.HeldProbe = null; }
            Check("смена назначения под зажатым ⌥ не съедает его отпускание",
                  !wentPlain && !rep2 && !up2,
                  "нажатие=" + wentPlain + ", повтор=" + rep2 + ", отпускание=" + up2 +
                  ", пришло «" + Sent() + "»");
            Clear();
        }

        /// <summary>
        /// Медиадействие верхнего ряда убирает с дороги заменитель Fn — как и ветка
        /// настоящей F-клавиши. При режиме «F-клавиши сразу» сюда попадают ровно тогда,
        /// когда заменитель держат: назначенная на F5 клавиша Home уходила под зажатым ⌥,
        /// то есть как Alt+Home — уходом браузера на домашнюю страницу.
        /// </summary>
        static void MediaActionDropsSubstitute()
        {
            Head("Медиадействие и заменитель Fn");

            Settings s = Fresh();
            s.MediaFirst = false;
            s.FnSubstitute = ModKey.RAlt;
            s.MapRAlt = ModKey.RAlt;
            s.FKeys[4] = "key.home";
            Use(s);
            DownE(Vk.RMenu); Clear();
            bool sw = Down(Vk.F1 + 4);
            Check("медиадействие не уходит под модификатором заменителя Fn",
                  sw && Count(Sent(), N(Vk.RMenu) + "^") == 1,
                  "это Alt+Home, уход браузера на домашнюю страницу: " + Sent());
            Clear();
            Up(Vk.F1 + 4); UpE(Vk.RMenu); Clear();

            // Контрольный опыт: ветка настоящей F-клавиши заменитель снимает — и снимала
            // всегда. Значит проверка выше меряет медиаветку, а не общий щит.
            Settings m = Fresh();
            m.MediaFirst = true;
            m.FnSubstitute = ModKey.RAlt;
            m.MapRAlt = ModKey.RAlt;
            Use(m);
            DownE(Vk.RMenu); Clear();
            sw = Down(Vk.F1 + 4);
            Check("контроль: ветка настоящей F-клавиши заменитель снимает",
                  sw && Count(Sent(), N(Vk.RMenu) + "^") == 1, Sent());
            Clear();
            Up(Vk.F1 + 4); UpE(Vk.RMenu); Clear();
        }

        /// <summary>Что перехват считает снятым у Windows на время.</summary>
        static Dictionary<ModKey, int> Lifted()
        {
            return (Dictionary<ModKey, int>)typeof(Engine)
                .GetField("_modLifted", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_eng);
        }

        /// <summary>
        /// Заменитель Fn с завода висит на правом ⌥, а тот ничем не переназначен —
        /// то есть его нажатия перехват не берёт вовсе. Щит обязан работать и так:
        /// снять код у Windows, а после навигации не оставить о нём записи, снять
        /// которую потом некому.
        /// </summary>
        static void SubstituteOnPlainKey()
        {
            Head("Заменитель Fn на неразбираемой клавише");

            Settings s = Fresh();
            s.FnNavigation = true;
            s.FnSubstitute = ModKey.RAlt;   // MapRAlt остаётся RAlt: клавиша идёт насквозь
            s.MacShortcuts = false;         // иначе ⌥+← достанется таблице, а не навигации
            Use(s);

            DownE(Vk.RMenu);
            bool sw = Down(Vk.Left); Up(Vk.Left);
            Only("Fn+← даёт Home, а ⌥ снят на это время", sw,
                 N(Vk.RMenu) + "^", N(Vk.Home) + "v", N(Vk.Home) + "^", N(Vk.RMenu) + "v");
            UpE(Vk.RMenu); Clear();
            Check("после Fn+← запись о снятом не остаётся", Lifted().Count == 0,
                  "осталось записей: " + Lifted().Count +
                  " — снять их некому, и перечёт зажатого перестаёт чинить эту клавишу");

            // Пока идёт навигация, ⌥ у Windows снят. Всё, что спрашивает «что Windows
            // держит сейчас», обязано отвечать так же: иначе готовый символ вернёт Alt
            // нажатым посреди удержания стрелки, и следующий Page Up уйдёт Alt+Page Up.
            Use(s);
            DownE(Vk.RMenu);
            Down(Vk.Up);                        // навигация началась, ⌥ снят
            Clear();
            Send(Vk.Clear, 0x59, true, false);  // «=» цифрового блока идёт через EmitText
            Send(Vk.Clear, 0x59, false, false);
            Check("во время навигации щит не возвращает ⌥ нажатым",
                  Count(Sent(), N(Vk.RMenu) + "v") == 0,
                  "Alt вернулся посреди удержания стрелки: " + Sent());
            Clear();
            Up(Vk.Up); UpE(Vk.RMenu); Clear();

            // Заменитель перенажали, не отпуская F-клавишу: модификатор вернулся,
            // а автоповтор уходит уже под ним — ⌥+F4 превращается в Alt+F4.
            Settings f = Fresh();
            f.MediaFirst = true;
            f.FnSubstitute = ModKey.RAlt;
            Use(f);
            DownE(Vk.RMenu);
            Down(Vk.F4);                        // настоящая F4, ⌥ снят
            UpE(Vk.RMenu);                      // заменитель отпустили, F4 держим
            // Спрашиваем именно про проглатывание: настоящее нажатие неразбираемой
            // клавиши идёт в Windows мимо нас, и в записи посланного его не видно —
            // пропустить его и значит вернуть Alt под зажатой F4, то есть Alt+F4.
            bool again = DownE(Vk.RMenu);
            Check("перенажатый заменитель не доходит до Windows, пока его код снят", again,
                  "нажатие пропущено — F4 уходит под Alt, а это Alt+F4, закрытие окна");
            Check("и код по-прежнему считается снятым", Lifted().ContainsKey(ModKey.RAlt),
                  "запись о снятии потеряна: " + Lifted().Count);
            Clear();
            Down(Vk.F4);                        // автоповтор F4
            Check("автоповтор F-клавиши не тащит за собой модификатор",
                  Count(Sent(), N(Vk.RMenu) + "v") == 0, "" + Sent());
            Clear();
            Up(Vk.F4);
            Check("проглоченное нажатие заменителя даёт проглоченное отпускание",
                  UpE(Vk.RMenu), "отпускание ушло в Windows без пары");
            Clear();
            Check("после всего снятого не остаётся", Lifted().Count == 0,
                  "осталось записей: " + Lifted().Count);
        }

        /// <summary>
        /// Уровень раскладки, объявленный отличным от Microsoft, но состоящий из одних
        /// пробелов, глотает нажатие и печатает пробел. Клавиша молча перестаёт делать
        /// то, что на ней напечатано, и понять почему нельзя ничем.
        /// </summary>
        static void LayoutLevels()
        {
            Head("Уровни раскладок");
            bool[] sh = { false, false, true, true };
            bool[] op = { false, true, false, true };
            string[] nm = { "base", "opt", "shift", "optShift" };

            var bad = new List<string>();
            foreach (AppleLayoutFile f in Layouts.All)
                foreach (KeyValuePair<int, LayoutKey> p in f.Keys)
                    for (int i = 0; i < 4; i++)
                    {
                        string t = p.Value.Text(sh[i], op[i]);
                        if (t != null && t.Trim().Length == 0 && p.Value.Differs(sh[i], op[i]))
                            bad.Add(f.Id + " " + p.Key.ToString("X2") + " " + nm[i]);
                    }
            Check("нет пустых уровней, объявленных отличными от Microsoft",
                  bad.Count == 0, String.Join(", ", bad.ToArray()));

            // Переход мёртвой клавиши, приставку которой не даёт ни один уровень,
            // сработать не может: это просто мёртвая строка в файле.
            var orphan = new List<string>();
            foreach (AppleLayoutFile f in Layouts.All)
            {
                var deads = new List<string>();
                foreach (KeyValuePair<int, LayoutKey> p in f.Keys)
                    for (int i = 0; i < 4; i++)
                        if (p.Value.Dead(sh[i], op[i]))
                        {
                            string t = p.Value.Text(sh[i], op[i]);
                            if (t != null && !deads.Contains(t)) deads.Add(t);
                        }
                foreach (KeyValuePair<string, string> tr in f.Transforms)
                    if (tr.Key.Length > 0 && !deads.Contains(tr.Key.Substring(0, 1)))
                        orphan.Add(f.Id + " «" + tr.Key + "»");
            }
            Check("у каждого перехода есть мёртвая клавиша, которая его начинает",
                  orphan.Count == 0, String.Join(", ", orphan.ToArray()));
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
                // И второй код тоже: у «Следующего окна» их два, и без этой строки
                // пара с ним выпадала из проверки целиком.
                foreach (int code in s.Vk2 != 0 ? new int[] { s.Vk, s.Vk2 } : new int[] { s.Vk })
                {
                    string key = code + "/" + (int)s.Mods;
                    string had;
                    if (pairs.TryGetValue(key, out had)) clash.Add(had + " и " + s.Id);
                    else pairs[key] = s.Id;
                }
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
            // Сверяем по коду, а не по null: Get неизвестный код не возвращает пустотой.
            // Прежняя форма не могла сработать никогда, и опечатка в заводских
            // назначениях прошла бы мимо всех проверок.
            if (Actions.Get(s0.NumpadClear).Id != s0.NumpadClear) missing.Add("для ⌧: " + s0.NumpadClear);
            if (Actions.Get(s0.EjectKey).Id != s0.EjectKey) missing.Add("для ⏏: " + s0.EjectKey);
            Check("все заводские назначения существуют", missing.Count == 0,
                  String.Join(", ", missing.ToArray()));
        }

        static void CheckIds(string[] list, List<string> missing, string whose)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
                if (!String.IsNullOrEmpty(list[i]) && Actions.Get(list[i]).Id != list[i])
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

            // И отдельно — поля, которые снимок скопировать не умеет вовсе. Сторож
            // внутри программы о таком говорит в журнал, а журнал по умолчанию выключен:
            // о нарушении правила «у состояния есть хозяин» не узнал бы никто, пока
            // окно и перехват не разошлись бы молча. Здесь оно должно падать.
            var cannot = new List<string>();
            foreach (FieldInfo f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Type ft = f.FieldType;
                if (ft.IsValueType || ft == typeof(string)) continue;
                if (ft.IsArray)
                {
                    Type el = ft.GetElementType();
                    if (el.IsValueType || el == typeof(string) || el == typeof(LayoutBinding)) continue;
                    cannot.Add(f.Name + " (массив " + el.Name + ")");
                    continue;
                }
                cannot.Add(f.Name + " (" + ft.Name + ")");
            }
            Check("снимок умеет скопировать каждое поле", cannot.Count == 0,
                  "не умеет: " + String.Join(", ", cannot.ToArray()) +
                  " — допишите копирование в Settings.Snapshot");
        }

        /// <summary>
        /// Правила Normalize — по одному. Каждое из них умеет отменить выбор человека,
        /// и до сих пор их «проверял» стенд окна — тем, что спрашивал у Normalize,
        /// выполнил ли Normalize своё же правило.
        /// </summary>
        static void NormalizeRules()
        {
            Head("Normalize: приведение к допустимому");

            Settings s = new Settings();
            s.FnSubstitute = ModKey.LShift;                 // такого окно не предлагает
            s.Normalize();
            Check("недопустимый заменитель Fn обнуляется", s.FnSubstitute == ModKey.None, "" + s.FnSubstitute);

            s = new Settings(); s.SpaceSearch = "чепуха"; s.Normalize();
            Check("неизвестный ответ про пробел — это ⌘", s.SpaceSearch == Settings.SpaceCmd, s.SpaceSearch);

            s = new Settings(); s.UpdateChannel = "nightly"; s.Normalize();
            Check("неизвестный канал — это stable", s.UpdateChannel == Settings.ChannelStable, s.UpdateChannel);

            s = new Settings(); s.NumpadClear = "нет.такого"; s.EjectKey = "и.такого"; s.Normalize();
            Check("неизвестные действия отдельных клавиш обнуляются",
                  s.NumpadClear == "none" && s.EjectKey == "none", s.NumpadClear + " / " + s.EjectKey);

            // Одна клавиша — одно дело, и спрашивается это по назначению, а не по надписи.
            s = new Settings();
            s.FnSubstitute = ModKey.CapsLock; s.MapCapsLock = ModKey.RAlt;
            s.OptLevel = OptLevel.RightOption; s.Normalize();
            Check("заменитель Fn, приходящий правым ⌥, гасит третий уровень",
                  s.OptLevel == OptLevel.Off, "" + s.OptLevel);

            s = new Settings();
            s.FnSubstitute = ModKey.RWin; s.MapRWin = ModKey.LAlt;
            s.OptLevel = OptLevel.AnyOption; s.Normalize();
            Check("заменитель Fn, приходящий левым ⌥, гасит «любой ⌥»",
                  s.OptLevel == OptLevel.Off, "" + s.OptLevel);

            // Достижимость, а не поле: правый Alt, уведённый на Caps Lock, настройку не режет.
            s = new Settings();
            s.MapRAlt = ModKey.RCtrl; s.MapCapsLock = ModKey.RAlt;
            s.OptLevel = OptLevel.RightOption; s.Normalize();
            Check("правый ⌥ на Caps Lock третий уровень сохраняет",
                  s.OptLevel == OptLevel.RightOption, "" + s.OptLevel);

            s = new Settings();
            s.FnSubstitute = ModKey.None;
            s.MapRAlt = ModKey.RCtrl;
            s.OptLevel = OptLevel.RightOption; s.Normalize();
            Check("недостижимый правый ⌥ гасит третий уровень", s.OptLevel == OptLevel.Off, "" + s.OptLevel);

            // Заменитель Fn с завода висит на правом ⌥ и сам погасил бы третий уровень —
            // здесь проверяется другое правило, поэтому его убираем.
            s = new Settings();
            s.FnSubstitute = ModKey.None;
            s.MapLAlt = ModKey.LCtrl;
            s.OptLevel = OptLevel.AnyOption; s.Normalize();
            Check("«любой ⌥» без левого Alt опускается до правого",
                  s.OptLevel == OptLevel.RightOption, "" + s.OptLevel);

            s = new Settings();
            s.FnSubstitute = ModKey.None;
            s.MapLAlt = ModKey.LCtrl; s.MapRAlt = ModKey.RCtrl;
            s.OptLevel = OptLevel.AnyOption; s.Normalize();
            Check("«любой ⌥» без обоих Alt выключается", s.OptLevel == OptLevel.Off, "" + s.OptLevel);

            s = new Settings();
            s.FnSubstitute = ModKey.None;
            s.MapLAlt = ModKey.LCtrl; s.MapCapsLock = ModKey.LAlt;
            s.OptLevel = OptLevel.AnyOption; s.Normalize();
            Check("«любой ⌥» держится, когда левый Alt переехал на Caps Lock",
                  s.OptLevel == OptLevel.AnyOption, "" + s.OptLevel);

            // Normalize должен быть идемпотентен: его зовут на каждой правке из окна,
            // и правило, качающее настройку туда-обратно, дало бы вечную пересборку.
            s = new Settings();
            s.MapLAlt = ModKey.LCtrl; s.OptLevel = OptLevel.AnyOption;
            s.FnSubstitute = ModKey.CapsLock; s.MapCapsLock = ModKey.RAlt;
            s.Normalize();
            string once = Snapshot(s);
            s.Normalize();
            Check("второй Normalize ничего не меняет", Snapshot(s) == once, Snapshot(s));
        }

        /// <summary>Все поля настроек одной строкой — чтобы сравнить два состояния.</summary>
        static string Snapshot(Settings s)
        {
            var parts = new List<string>();
            foreach (FieldInfo f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = f.GetValue(s);
                var arr = v as Array;
                if (arr == null) { parts.Add(f.Name + "=" + v); continue; }
                var items = new List<string>();
                foreach (object o in arr) items.Add("" + o);
                parts.Add(f.Name + "=[" + String.Join(",", items.ToArray()) + "]");
            }
            return String.Join("; ", parts.ToArray());
        }

        /// <summary>Что приходит из файла, может быть каким угодно.</summary>
        static void BrokenFile()
        {
            Head("Битый файл настроек");
            // Normalize теперь зовут не только при чтении файла: ApplyAndSave проверяет
            // согласие настроек на каждой правке из окна.
            MethodInfo norm = typeof(Settings).GetMethod("Normalize",
                                  BindingFlags.Public | BindingFlags.Instance);
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
