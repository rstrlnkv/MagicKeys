// Стенд окна: каждый переключатель и каждый список на страницах щёлкают по очереди
// и смотрят, что от этого изменилось в настройках и дошло ли до перехвата.
//
// Зачем отдельно от стенда настроек. Тот проверяет, что перехват слушается настроек;
// этот — что настройки слушаются человека. Между ними ровно та щель, куда проваливается
// переключатель, забытый при перестройке страницы: он есть, он щёлкает, и он ничей.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MagicKeys
{
    static class WindowTests
    {
        static int _pass, _fail;
        static readonly List<string> _fails = new List<string>();
        static Settings _s;
        static Engine _eng;
        static MainWindow _win;
        static int _applied;

        // Автозапуск пишет в реестр Windows, а не в настройки программы: щёлкать его
        // на стенде значило бы оставить след в чужом хозяйстве. «Свёрнутой в значок» —
        // туда же: при включённом автозапуске окно переписывает ту же запись, и в ней
        // оказывается путь к стенду вместо пути к программе.
        static readonly string[] Skip =
        {
            "Запускать вместе с Windows",
            "Запускаться свёрнутой в значок"
        };

        [STAThread]
        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            // Ввод наружу не уходит. Стенд создаёт живой перехват, а тот при каждом
            // применении настроек включает Num Lock, если ⌧ уведена с него, — то есть
            // прогон стенда переключал Num Lock на машине, где его запустили.
            Input.Sink = delegate(Native.INPUT[] batch) { };

            var app = new Application();
            app.Resources.MergedDictionaries.Add(Theme.Initial());

            _s = new Settings();
            _eng = new Engine();
            _s.DeveloperMode = true;   // иначе половина страниц не собирается
            _win = new MainWindow(_s, _eng, delegate { _applied++; _eng.Apply(_s); });

            string[] pages = { "PageMacKeys", "PageKeys", "PageLayout", "PageDriver", "PageAbout", "PageDiag" };
            foreach (string p in pages) Page(p);

            Console.WriteLine();
            Console.WriteLine("== кнопки ==");
            foreach (string p in pages) Buttons(p);

            RightOptionSymbols();
            MacRowsInGroups();
            UpdateCardStates();
            UpdateCardBusy();
            BrokenSettingsCard();
            AutostartNotice();
            StopButtonFollowsCancellable();
            MarkFollowsBuild();
            DriverProbeReachesWindow();
            YieldedRowIsDead();
            FnNoteMatchesTable();
            SelfTestForgets();
            YieldedRowAboveTwelve();
            FnNoteChecksBothArrows();
            PhysNoteFollowsChoice();
            OffMeansOff();
            EjectWithoutActionIsNotPraised();
            SpaceNoteOnlyWhenRoleMoved();
            CmdNoteThreeStates();
            FnNotesDoNotContradict();
            RightOptionCardStates();
            BatteryCardStates();
            DriverCardTellsTruthAboutMode();
            AppleLayoutNoteFollowsOptLevel();
            DriverOutcomeSurvivesRemoval();
            HookFailureIsSaid();
            NoGlyphsMissingFromFont();
            ChannelButtons();
            FnNoteAboveTwelve();
            PhysNoteAnsiIsNotDetected();
            ManualChoiceDoesNotInventDetection();
            SpaceCardFollowsCmd();
            FnBehaviorUnknownIsNotRegistry();
            NarrowWindowKeepsWordsWhole();
            KeyboardArtIsHonest();
            SettingsFollowTheKeyboard();
            ElevatedWindowIsSaidOutLoud();

            Console.WriteLine();
            Console.WriteLine("прошло " + _pass + ", провалено " + _fail);
            foreach (string f in _fails) Console.WriteLine("  ПРОВАЛ: " + f);
            return _fail == 0 ? 0 : 1;
        }

        /// <summary>
        /// Правый ⌥ в роли «символы». С заводскими настройками эта ветка карточки
        /// не собирается вовсе — клавиша занята заменой Fn, — а вместе с ней стенда
        /// не касался и переключатель «Любой ⌥», у которого своё правило в Normalize.
        /// </summary>
        static void RightOptionSymbols()
        {
            Console.WriteLine();
            Console.WriteLine("== правый ⌥ в роли «символы» ==");
            Settings saved = _s.Snapshot();

            _s.FnSubstitute = ModKey.None;
            _s.OptLevel = OptLevel.RightOption;
            _s.MapRAlt = ModKey.RAlt;
            _s.MapLAlt = ModKey.LAlt;
            _s.MapCapsLock = ModKey.CapsLock;
            _eng.Apply(_s);
            // Состояние, под которое собрана страница: к нему и возвращаемся между
            // случаями, а не к тому, с которым сюда пришли.
            Settings symbols = _s.Snapshot();

            MethodInfo m = typeof(MainWindow).GetMethod("PageKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var boxes = new List<CheckBox>();
            var combos = new List<ComboBox>();
            try { Walk((UIElement)m.Invoke(_win, null), boxes, combos); }
            catch (Exception e) { Check("страница «Клавиши» с ролью «символы»", false, Inner(e)); Restore(saved); return; }

            bool found = false;
            foreach (CheckBox cb in boxes)
            {
                string name = Label(cb);
                if (name == null || name.IndexOf("Любой") < 0) continue;
                found = true;
                Flip(cb);
            }
            Check("переключатель «Любой ⌥» есть, когда правый ⌥ набирает символы",
                  found, "его нет на странице");

            // Отдельно и прямо: «любой ⌥» держится, пока левый Alt хоть чем-то достижим,
            // и опускается, когда его уводят. Через списки этого не спросить — там роль
            // проходит через «Fn», а тот третий уровень выключает по праву.
            Restore(saved);
            _s.FnSubstitute = ModKey.None;
            _s.OptLevel = OptLevel.AnyOption;
            _s.MapRAlt = ModKey.RAlt;
            _s.MapLAlt = ModKey.LAlt;
            _eng.Apply(_s);
            Check("«любой ⌥» держится, пока левый Alt на месте",
                  Normalized() == "", "Normalize поправил " + Normalized());

            _s.OptLevel = OptLevel.AnyOption;
            _s.MapLAlt = ModKey.LCtrl;          // левый ⌥ увели
            _s.MapCapsLock = ModKey.LAlt;       // но левый Alt достижим с Caps Lock
            _eng.Apply(_s);
            Check("«любой ⌥» держится, когда левый Alt переехал на другую клавишу",
                  Normalized() == "", "Normalize поправил " + Normalized());

            _s.OptLevel = OptLevel.AnyOption;
            _s.MapCapsLock = ModKey.CapsLock;   // а теперь левого Alt не даёт никто
            _eng.Apply(_s);
            _s.Normalize();
            Check("«любой ⌥» без левого Alt опускается до правого",
                  _s.OptLevel == OptLevel.RightOption, "" + _s.OptLevel);

            // Со свежего места: проверка выше нарочно уводила левый ⌥, а страница
            // собрана под роль «символы» — к ней и возвращаемся.
            Restore(symbols);

            // И списки той же страницы: под ролью «символы» их не касался никто,
            // а среди них — те пять строк, что решают, до какого модификатора вообще
            // можно дотянуться. Именно они молча отменяли «любой ⌥».
            foreach (ComboBox b in combos) Turn(b);

            Restore(saved);
        }

        /// <summary>
        /// Отодвинуть срок перечёта драйвера, чтобы подменённое состояние не стёрлось.
        ///
        /// AppleDriver.Refresh(false) перечитывает реестр раз в тридцать секунд. Подмена
        /// полей держалась ровно на этом кэше: затянись прогон — и половина проверок
        /// молча начнёт мерить настоящее состояние машины вместо подменённого.
        /// </summary>
        static void Freeze()
        {
            FieldInfo stamp = typeof(AppleDriver).GetField("_stamp",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (stamp != null) stamp.SetValue(null, DateTime.UtcNow);
        }

        /// <summary>
        /// Оговорка о поиске обязана появляться только тогда, когда роль увели с той
        /// клавиши, что названа в списке выше. Caps Lock, превращённый в control, —
        /// самое частое переназначение с мака, и роли ⌘ оно не касается вовсе.
        /// </summary>
        static void SpaceNoteOnlyWhenRoleMoved()
        {
            Console.WriteLine();
            Console.WriteLine("== оговорка о поиске на ровном месте ==");
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageMacKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                _s.MacShortcuts = true;
                _s.MapLWin = ModKey.LWin; _s.MapRWin = ModKey.RWin; _s.MapLCtrl = ModKey.LCtrl;

                _s.SpaceSearch = Settings.SpaceCmd;
                _s.MapCapsLock = ModKey.LCtrl;          // Caps Lock как control
                _eng.Apply(_s);
                Check("Caps Lock как control не отменяет поиск по ⌘",
                      !Says((UIElement)m.Invoke(_win, null), "поиск открывает"),
                      "уверяет, что поиск открывает не та клавиша, что подписана выше");

                _s.MapCapsLock = ModKey.LWin;           // ⌘ добавилась на Caps Lock
                _s.SpaceSearch = Settings.SpaceCtrl;    // а спрашиваем про control
                _eng.Apply(_s);
                Check("уехавшая ⌘ не отменяет поиск по control",
                      !Says((UIElement)m.Invoke(_win, null), "поиск открывает"),
                      "оговорка про роль, о которой список выше не спрашивает");

                // Контроль: настоящий обмен — оговорка обязана быть.
                _s.MapCapsLock = ModKey.CapsLock;
                _s.MapLCtrl = ModKey.LWin; _s.MapLWin = ModKey.LCtrl; _s.MapRWin = ModKey.RCtrl;
                _s.SpaceSearch = Settings.SpaceCmd;
                _eng.Apply(_s);
                Check("контроль: при настоящем обмене оговорка есть",
                      Says((UIElement)m.Invoke(_win, null), "поиск открывает"), "её нет");
            }
            catch (Exception e) { Check("страница «Основные» собирается", false, Inner(e)); }
            Restore(saved);
        }

        /// <summary>Три состояния сноски о том, где живёт ⌘.</summary>
        static void CmdNoteThreeStates()
        {
            Console.WriteLine();
            Console.WriteLine("== где живёт ⌘ ==");
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageMacKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                _s.MacShortcuts = true;
                _s.MapLWin = ModKey.LWin; _s.MapRWin = ModKey.RWin;
                _s.MapCapsLock = ModKey.CapsLock; _s.MapLCtrl = ModKey.LCtrl;
                _s.MapLAlt = ModKey.LAlt; _s.MapRAlt = ModKey.RAlt;
                _eng.Apply(_s);
                UIElement own = (UIElement)m.Invoke(_win, null);
                Check("⌘ на своей клавише — о переезде не говорят",
                      !Says(own, "её роль играет") && !Says(own, "нажать сочетания ниже нечем"),
                      "говорит о переезде на ровном месте");

                _s.MapLWin = ModKey.LAlt; _s.MapRWin = ModKey.RAlt;
                _s.MapCapsLock = ModKey.LWin;      // ⌘ переехала на Caps Lock
                _eng.Apply(_s);
                Check("переехавшую ⌘ называют по имени клавиши",
                      Says((UIElement)m.Invoke(_win, null), "Caps Lock"),
                      "молчит о переезде");

                _s.MapCapsLock = ModKey.CapsLock;  // ⌘ не даёт никто
                _eng.Apply(_s);
                Check("когда ⌘ нет вовсе — об этом сказано",
                      Says((UIElement)m.Invoke(_win, null), "нечем"), "не сказано");
            }
            catch (Exception e) { Check("страница «Основные» собирается", false, Inner(e)); }
            Restore(saved);
        }

        /// <summary>Две сноски карточки «Заменитель Fn» стоят вплотную и не спорят.</summary>
        static void FnNotesDoNotContradict()
        {
            Console.WriteLine();
            Console.WriteLine("== две сноски о настоящей Fn ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo takes = typeof(AppleDriver).GetField("_takesRow", f);
            FieldInfo media = typeof(KeyWatch).GetField("_mediaSeen", f);
            if (takes == null || media == null)
            { Check("состояние драйвера доступно", false, "нет поля"); return; }

            object wasT = takes.GetValue(null), wasM = media.GetValue(null);
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                takes.SetValue(null, true); media.SetValue(null, true); Freeze();
                _s.FnSubstitute = ModKey.None; _s.FnNavigation = true; _s.MediaFirst = true;
                _eng.Apply(_s);

                UIElement page = (UIElement)m.Invoke(_win, null);
                Check("при работающем драйвере не говорят, что настоящая Fn не доходит",
                      !(Says(page, "настоящая Fn до Windows не доходит")
                        && Says(page, "с ним работает настоящая Fn")),
                      "две сноски одной карточки отменяют друг друга");

                // Контроль: без драйвера первая сноска обязана быть.
                takes.SetValue(null, false); media.SetValue(null, false); Freeze();
                _eng.Apply(_s);
                Check("контроль: без драйвера про недоходящую Fn говорят",
                      Says((UIElement)m.Invoke(_win, null), "настоящая Fn до Windows не доходит"),
                      "не говорят");
            }
            catch (Exception e) { Check("страница «Клавиши» собирается", false, Inner(e)); }
            finally
            {
                takes.SetValue(null, wasT); media.SetValue(null, wasM); Freeze();
                Restore(saved);
            }
        }

        /// <summary>Шесть состояний карточки «Правый ⌥» — ни одно не было покрыто.</summary>
        static void RightOptionCardStates()
        {
            Console.WriteLine();
            Console.WriteLine("== карточка «Правый ⌥», все роли ==");
            BindingFlags fs = BindingFlags.NonPublic | BindingFlags.Static;
            BindingFlags fi = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo takes = typeof(AppleDriver).GetField("_takesRow", fs);
            FieldInfo media = typeof(KeyWatch).GetField("_mediaSeen", fs);
            FieldInfo maxF = typeof(KeyWatch).GetField("_maxFunctionKey", fs);
            MethodInfo card = typeof(MainWindow).GetMethod("RightOptionCard", fi);
            if (takes == null || media == null || maxF == null || card == null)
            { Check("карточка «Правый ⌥» доступна", false, "нет поля или метода"); return; }

            object wasT = takes.GetValue(null), wasM = media.GetValue(null), wasX = maxF.GetValue(null);
            Settings saved = _s.Snapshot();
            try
            {
                takes.SetValue(null, true); media.SetValue(null, true);
                maxF.SetValue(null, 19); Freeze();
                _s.FnSubstitute = ModKey.RAlt; _s.MapRAlt = ModKey.RAlt;
                _s.OptLevel = OptLevel.Off; _s.FnNavigation = false;
                _eng.Apply(_s);
                Check("с клавишами выше двенадцатой не говорят «переключать нечего»",
                      !Says((UIElement)card.Invoke(_win, null), "переключать нечего"),
                      "отменяет соседнюю карточку, которая просит заменитель ради F13");

                maxF.SetValue(null, 12); Freeze(); _eng.Apply(_s);
                Check("контроль: без клавиш выше двенадцатой «переключать нечего» сказано",
                      Says((UIElement)card.Invoke(_win, null), "переключать нечего"), "не сказано");

                takes.SetValue(null, false); media.SetValue(null, false); Freeze();
                _eng.Apply(_s);
                Check("без драйвера роль Fn обещает переключение ряда",
                      Says((UIElement)card.Invoke(_win, null), "Переключает верхний ряд"),
                      "не обещает");

                _s.FnSubstitute = ModKey.None; _s.OptLevel = OptLevel.RightOption;
                _s.MapRAlt = ModKey.RAlt; _s.AppleLayoutEnabled = false;
                _eng.Apply(_s);
                Check("роль «символы» при выключенных раскладках честна",
                      Says((UIElement)card.Invoke(_win, null), "Сейчас они выключены"),
                      "молчит об этом");

                _s.AppleLayoutEnabled = true; _eng.Apply(_s);
                Check("контроль: при включённых раскладках оговорки нет",
                      !Says((UIElement)card.Invoke(_win, null), "Сейчас они выключены"),
                      "оговорка лишняя");

                _s.OptLevel = OptLevel.Off; _s.MapRAlt = ModKey.None; _eng.Apply(_s);
                Check("выключенная клавиша названа выключенной",
                      Says((UIElement)card.Invoke(_win, null), "клавиша выключена"), "не названа");

                _s.MapRAlt = ModKey.RAlt; _eng.Apply(_s);
                Check("своя клавиша названа обычным Alt",
                      Says((UIElement)card.Invoke(_win, null), "AltGr"), "об AltGr не сказано");
            }
            catch (Exception e) { Check("карточка «Правый ⌥» собирается", false, Inner(e)); }
            finally
            {
                takes.SetValue(null, wasT); media.SetValue(null, wasM);
                maxF.SetValue(null, wasX); Freeze();
                Restore(saved);
            }
        }

        /// <summary>Четыре состояния карточки заряда — ни одно не было покрыто.</summary>
        static void BatteryCardStates()
        {
            Console.WriteLine();
            Console.WriteLine("== карточка заряда ==");
            BindingFlags fs = BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo pct = typeof(KeyboardBattery).GetField("_percent", fs);
            FieldInfo stamp = typeof(KeyboardBattery).GetField("_stamp", fs);
            MethodInfo diag = typeof(MainWindow).GetMethod("PageDiag",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (pct == null || stamp == null || diag == null)
            { Check("состояние заряда доступно", false, "нет поля или метода"); return; }

            object wasP = pct.GetValue(null), wasS = stamp.GetValue(null);
            try
            {
                // Срок обязателен: без него геттер заводит настоящий опрос, и подменённое
                // число уезжает.
                pct.SetValue(null, KeyboardBattery.Unknown); stamp.SetValue(null, DateTime.UtcNow);
                Check("«ещё не спрашивали» не выдают за отказ",
                      Says((UIElement)diag.Invoke(_win, null), "Спрашиваю у клавиатуры"),
                      "утверждает непроверенное");

                pct.SetValue(null, -1); stamp.SetValue(null, DateTime.UtcNow);
                Check("«не ответила» названа не ответившей",
                      Says((UIElement)diag.Invoke(_win, null), "клавиатура не ответила"),
                      "названа иначе");

                pct.SetValue(null, KeyboardBattery.NoSource); stamp.SetValue(null, DateTime.UtcNow);
                UIElement none = (UIElement)diag.Invoke(_win, null);
                Check("«спрашивать некого» отделено от «не ответила»",
                      Says(none, "спрашивать заряд не у кого") || Says(none, "заряд не сообщает"),
                      "слито с отказом");
                Check("про отсутствующую клавиатуру не говорят «эта клавиатура»",
                      Devices.AppleConnected || !Says(none, "Эта клавиатура заряд не сообщает"),
                      "уверяет про клавиатуру, которой нет");

                pct.SetValue(null, 55); stamp.SetValue(null, DateTime.UtcNow);
                Check("известное число показано числом",
                      Says((UIElement)diag.Invoke(_win, null), "55 %"), "число не показано");
            }
            catch (Exception e) { Check("страница «Диагностика» собирается", false, Inner(e)); }
            finally { pct.SetValue(null, wasP); stamp.SetValue(null, wasS); }
        }

        /// <summary>
        /// Ручной выбор исполнения не отменяет правила «ANSI — это не распознанное».
        /// </summary>
        static void ManualChoiceDoesNotInventDetection()
        {
            Console.WriteLine();
            Console.WriteLine("== ANSI как «распознанное» при ручном выборе ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo det = typeof(KeyWatch).GetField("_detectedPhys", f);
            if (det == null) { Check("признак распознанного доступен", false, "нет поля"); return; }
            object was = det.GetValue(null);
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageLayout",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                _s.Physical = PhysLayout.Iso; _eng.Apply(_s);

                det.SetValue(null, (int)PhysLayout.Ansi);
                Check("при ручном выборе ANSI не выдают за распознанное",
                      !Says((UIElement)m.Invoke(_win, null), "По нажатиям распознано: ANSI"),
                      "уверяет, что по нажатиям распознано ANSI, — а не встретилось ничего");

                // Контроль: настоящее распознанное при ручном выборе называть надо.
                det.SetValue(null, (int)PhysLayout.Jis);
                Check("контроль: распознанное JIS при ручном выборе называют",
                      Says((UIElement)m.Invoke(_win, null), "По нажатиям распознано: JIS"),
                      "не называют — значит проверка выше ничего не доказывает");
            }
            catch (Exception e) { Check("страница «Раскладка» собирается", false, Inner(e)); }
            finally { det.SetValue(null, was); Restore(saved); }
        }

        /// <summary>
        /// Роли ⌘Space и ⌃Space перехват раздаёт по назначению клавиши, а не по надписи.
        /// После схемы «⌘ работает как Ctrl» поиск открывает control+пробел, а карточка
        /// показывала «⌘ + пробел» и молчала об этом.
        /// </summary>
        static void SpaceCardFollowsCmd()
        {
            Console.WriteLine();
            Console.WriteLine("== чья клавиша открывает поиск ==");
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageMacKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                _s.MacShortcuts = true; _s.SpaceSearch = Settings.SpaceCmd;
                _s.MapLWin = ModKey.LWin; _s.MapLCtrl = ModKey.LCtrl; _s.MapRWin = ModKey.RWin;
                _eng.Apply(_s);
                Check("контроль: пока ⌘ на своей клавише, оговорки о поиске нет",
                      !Says((UIElement)m.Invoke(_win, null), "поиск открывает"),
                      "оговорка стоит на ровном месте — проверка ниже ничего не докажет");

                // Схема «⌘ работает как Ctrl»: ⌘ приходит в Windows клавишей Ctrl.
                _s.MapLCtrl = ModKey.LWin; _s.MapLWin = ModKey.LCtrl; _s.MapRWin = ModKey.RCtrl;
                _eng.Apply(_s);
                Check("когда ⌘ увели, карточка поиска об этом говорит",
                      Says((UIElement)m.Invoke(_win, null), "поиск открывает"),
                      "обещает поиск по ⌘+пробел, а он открывается по control+пробел");
            }
            catch (Exception e) { Check("страница «Основные» собирается", false, Inner(e)); }
            Restore(saved);
        }

        /// <summary>
        /// Драйвер установлен, а OSXFnBehavior в реестре нет: программа считает такой
        /// драйвер забирающим ряд по умолчанию Apple. Ссылаться при этом на реестр —
        /// выдавать собственную догадку за прочитанное.
        /// </summary>
        static void FnBehaviorUnknownIsNotRegistry()
        {
            Console.WriteLine();
            Console.WriteLine("== драйвер есть, а значения в реестре нет ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            Type d = typeof(AppleDriver);
            FieldInfo inst = d.GetField("_installed", f), en = d.GetField("_enabled", f),
                      fb = d.GetField("_fnBehavior", f), takes = d.GetField("_takesRow", f),
                      stamp = d.GetField("_stamp", f), where = d.GetField("_fnBehaviorPath", f);
            FieldInfo media = typeof(KeyWatch).GetField("_mediaSeen", f);
            MethodInfo m = typeof(MainWindow).GetMethod("PageDriver",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (inst == null || en == null || fb == null || takes == null || stamp == null
                || where == null || media == null || m == null)
            { Check("состояние драйвера доступно", false, "нет поля или метода"); return; }

            object w1 = inst.GetValue(null), w2 = en.GetValue(null), w3 = fb.GetValue(null),
                   w4 = takes.GetValue(null), w5 = stamp.GetValue(null),
                   w6 = where.GetValue(null), w7 = media.GetValue(null);
            try
            {
                // Свежая отметка: иначе Refresh перечитает настоящий реестр машины.
                stamp.SetValue(null, DateTime.UtcNow);
                inst.SetValue(null, true); en.SetValue(null, true);
                fb.SetValue(null, -1); where.SetValue(null, null);
                takes.SetValue(null, true); media.SetValue(null, false);

                UIElement page = (UIElement)m.Invoke(_win, null);
                Check("о ненайденном значении не говорят «в реестре сказано»",
                      !Says(page, "В реестре сказано"),
                      "ссылается на реестр, в котором значения нет");
                Check("режим, которого в реестре нет, назван предположением",
                      Says(page, "в реестре не задано"),
                      "о догадке молчат вовсе — и обе кнопки режима без отметки");

                // Контрольный опыт: значение прочитано — тогда о реестре говорить можно.
                stamp.SetValue(null, DateTime.UtcNow);
                fb.SetValue(null, 1);
                where.SetValue(null, "HKLM" + (char)92 + "SYSTEM" + (char)92 + "KeyMagic2");
                Check("контроль: с прочитанным значением о реестре говорят",
                      Says((UIElement)m.Invoke(_win, null), "В реестре сказано"),
                      "не говорят — значит проверка выше ничего не доказывает");
            }
            catch (Exception e) { Check("страница драйвера собирается", false, Inner(e)); }
            finally
            {
                inst.SetValue(null, w1); en.SetValue(null, w2); fb.SetValue(null, w3);
                takes.SetValue(null, w4); stamp.SetValue(null, w5);
                where.SetValue(null, w6); media.SetValue(null, w7);
            }
        }

        /// <summary>
        /// Карточка ⏏ не хвалится работающим назначением, когда назначения нет вовсе.
        /// Заводское значение — «Оставить как есть», и обработчик на нём уходит молча.
        /// </summary>
        static void EjectWithoutActionIsNotPraised()
        {
            Console.WriteLine();
            Console.WriteLine("== ⏏ без назначения ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo seen = typeof(KeyWatch).GetField("_ejectSeen", f);
            if (seen == null) { Check("признак прихода ⏏ доступен", false, "нет поля"); return; }
            object was = seen.GetValue(null);
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                seen.SetValue(null, true);
                _s.Enabled = true; _s.PauseWhenAppleAbsent = false;
                _s.EjectKey = "none"; _eng.Apply(_s);
                UIElement none = (UIElement)m.Invoke(_win, null);
                Check("без назначения не обещают, что назначение работает",
                      !Says(none, "назначение работает"),
                      "хвалится назначением, которого человек не задавал");
                Check("а предлагают его выбрать",
                      Says(none, "выберите, что она должна делать"), "не предлагают");

                _s.Enabled = false; _eng.Apply(_s);
                Check("и не пугают поломкой настройки, которой нет",
                      !Says((UIElement)m.Invoke(_win, null), "не срабатывает"),
                      "предупреждает о поломке ненастроенного");
            }
            catch (Exception e) { Check("страница «Клавиши» собирается", false, Inner(e)); }
            finally { seen.SetValue(null, was); Restore(saved); }
        }

        /// <summary>
        /// Карточка «Сейчас этим занимается драйвер Apple» не объявляет погашенным
        /// список, который остаётся живым. Гасят его только при ровно двенадцати
        /// клавишах, а фраза стояла безусловной — и отговаривала трогать тот самый
        /// список, которым назначения F13–F19 и оживляют.
        /// </summary>
        static void DriverCardTellsTruthAboutMode()
        {
            Console.WriteLine();
            Console.WriteLine("== карточка драйвера про основной режим ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo takes = typeof(AppleDriver).GetField("_takesRow", f);
            FieldInfo media = typeof(KeyWatch).GetField("_mediaSeen", f);
            FieldInfo maxF = typeof(KeyWatch).GetField("_maxFunctionKey", f);
            if (takes == null || media == null || maxF == null)
            { Check("состояние драйвера доступно", false, "нет поля"); return; }

            object wasT = takes.GetValue(null), wasM = media.GetValue(null), wasX = maxF.GetValue(null);
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                takes.SetValue(null, true); media.SetValue(null, true);
                maxF.SetValue(null, 19); Freeze();
                _eng.Apply(_s);
                Check("при клавишах выше двенадцатой режим не объявляют погашенным",
                      !Says((UIElement)m.Invoke(_win, null), "погашен и выбор основного режима"),
                      "объявляет, а список режима при этом живой");

                maxF.SetValue(null, 12); Freeze();
                _eng.Apply(_s);
                Check("контроль: при ровно двенадцати про погашенный режим сказано",
                      Says((UIElement)m.Invoke(_win, null), "погашен и выбор основного режима"),
                      "не сказано — значит проверка выше ничего не доказывает");
            }
            catch (Exception e) { Check("страница «Клавиши» собирается", false, Inner(e)); }
            finally
            {
                takes.SetValue(null, wasT); media.SetValue(null, wasM);
                maxF.SetValue(null, wasX); Freeze();
                Restore(saved);
            }
        }

        /// <summary>
        /// Сноска «Раскладки Apple» не обещает третий уровень, когда он выключен, —
        /// а с завода он выключен нарочно.
        /// </summary>
        static void AppleLayoutNoteFollowsOptLevel()
        {
            Console.WriteLine();
            Console.WriteLine("== третий уровень в сноске о раскладках ==");
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageLayout",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                _s.AppleLayoutEnabled = true; _s.FnSubstitute = ModKey.None;
                _s.OptLevel = OptLevel.Off; _s.Normalize(); _eng.Apply(_s);
                Check("при выключенном третьем уровне сноска его не обещает",
                      !Says((UIElement)m.Invoke(_win, null), "третьими, набирает ⌥"),
                      "обещает набор символов, которых сейчас не набрать ничем");

                _s.OptLevel = OptLevel.RightOption; _s.MapRAlt = ModKey.RAlt;
                _s.Normalize(); _eng.Apply(_s);
                Check("контроль: при включённом третьем уровне сноска про него говорит",
                      Says((UIElement)m.Invoke(_win, null), "третьими, набирает ⌥"),
                      "не говорит — значит проверка выше ничего не доказывает");
            }
            catch (Exception e) { Check("страница «Раскладка» собирается", false, Inner(e)); }
            Restore(saved);
        }

        /// <summary>
        /// Итог удаления драйвера показывают и тогда, когда драйвера уже нет: удача
        /// переводит «установлен» в ложь, и отчёт вместе с требованием перезагрузиться
        /// пропадал — а неудача отчитывалась исправно.
        /// </summary>
        static void DriverOutcomeSurvivesRemoval()
        {
            Console.WriteLine();
            Console.WriteLine("== итог удаления драйвера ==");
            BindingFlags fs = BindingFlags.NonPublic | BindingFlags.Static;
            BindingFlags fi = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo inst = typeof(AppleDriver).GetField("_installed", fs);
            FieldInfo notice = typeof(MainWindow).GetField("_fnNotice", fi);
            MethodInfo m = typeof(MainWindow).GetMethod("PageDriver", fi);
            if (inst == null || notice == null || m == null)
            { Check("состояние драйвера доступно", false, "нет поля или метода"); return; }

            object wasI = inst.GetValue(null), wasN = notice.GetValue(_win);
            try
            {
                notice.SetValue(_win, "Драйвер удалён. Windows просит перезагрузить компьютер.");
                inst.SetValue(null, false); Freeze();
                Check("после удаления итог показывают",
                      Says((UIElement)m.Invoke(_win, null), "Драйвер удалён"),
                      "молчит: требование перезагрузиться человек не увидит никогда");

                inst.SetValue(null, true); Freeze();
                Check("контроль: при установленном драйвере итог тоже показывают",
                      Says((UIElement)m.Invoke(_win, null), "Драйвер удалён"), "не показывают");
            }
            catch (Exception e) { Check("страница драйвера собирается", false, Inner(e)); }
            finally { inst.SetValue(null, wasI); notice.SetValue(_win, wasN); Freeze(); }
        }

        /// <summary>
        /// Сорванный перехват — важнее всего остального: без него не работает ничего,
        /// а карточка состояния показывала настройку, а не действительность.
        /// </summary>
        static void HookFailureIsSaid()
        {
            Console.WriteLine();
            Console.WriteLine("== сорванный перехват на первой странице ==");
            MethodInfo m = typeof(MainWindow).GetMethod("PageMacKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            string was = _eng.Failure;
            try
            {
                _eng.Failure = null;
                Check("контроль: пока перехват жив, о нём не говорят",
                      !Says((UIElement)m.Invoke(_win, null), "перехват клавиатуры"),
                      "говорит на ровном месте — проверка ниже ничего не докажет");

                _eng.Failure = "Windows сняла перехват клавиатуры и не дала поставить его заново.";
                Check("при сорванном перехвате карточка состояния об этом говорит",
                      Says((UIElement)m.Invoke(_win, null), "перехват клавиатуры"), "молчит");
            }
            catch (Exception e) { Check("страница «Основные» собирается", false, Inner(e)); }
            finally { _eng.Failure = was; }
        }

        /// <summary>
        /// Значков ⌫, ⌧ и ⏏ в надписях быть не должно: в системном шрифте они
        /// не рисуются и выходят пустым прямоугольником. Правило дома.
        /// </summary>
        static void NoGlyphsMissingFromFont()
        {
            Console.WriteLine();
            Console.WriteLine("== значки, которых нет в шрифте ==");
            var bad = new List<string>();
            MethodInfo[] pages =
            {
                typeof(MainWindow).GetMethod("PageMacKeys", BindingFlags.NonPublic | BindingFlags.Instance),
                typeof(MainWindow).GetMethod("PageKeys", BindingFlags.NonPublic | BindingFlags.Instance),
                typeof(MainWindow).GetMethod("PageLayout", BindingFlags.NonPublic | BindingFlags.Instance),
                typeof(MainWindow).GetMethod("PageDriver", BindingFlags.NonPublic | BindingFlags.Instance),
                typeof(MainWindow).GetMethod("PageDiag", BindingFlags.NonPublic | BindingFlags.Instance),
                typeof(MainWindow).GetMethod("PageAbout", BindingFlags.NonPublic | BindingFlags.Instance)
            };
            foreach (MethodInfo p in pages)
            {
                if (p == null) continue;
                try { Words((UIElement)p.Invoke(_win, null), bad); }
                catch (Exception e) { Check("страница " + p.Name + " собирается", false, Inner(e)); }
            }
            Check("в надписях нет значков, которых нет в шрифте", bad.Count == 0,
                  String.Join(" | ", bad.ToArray()));
        }

        /// <summary>Собрать надписи, в которых стоят непрорисовываемые значки.</summary>
        static void Words(object node, List<string> bad)
        {
            var t = node as TextBlock;
            if (t != null)
            {
                string s = t.Text;
                if (!String.IsNullOrEmpty(s) &&
                    (s.IndexOf((char)0x23CF) >= 0 || s.IndexOf((char)0x2327) >= 0
                     || s.IndexOf((char)0x232B) >= 0))
                    bad.Add("«" + Short(s) + "»");
                return;
            }
            var d = node as DependencyObject;
            if (d == null) return;
            // И внутрь Viewbox: схема клавиатуры лежит там, а её подписи — такие же
            // надписи, как все прочие, и значок ⌫ на клавише выглядел бы пустым
            // прямоугольником ровно так же.
            var deco = node as Decorator;
            if (deco != null) Words(deco.Child, bad);
            var content = node as ContentControl;
            if (content != null) Words(content.Content, bad);
            var panel = node as Panel;
            if (panel != null) foreach (UIElement c in panel.Children) Words(c, bad);
            var border = node as Border;
            if (border != null) Words(border.Child, bad);
            var items = node as ItemsControl;
            if (items != null && !(node is ComboBox))
                foreach (object o in items.Items) Words(o, bad);
        }

        /// <summary>
        /// Кнопки канала обновлений живут по общему признаку занятости, а не по тому,
        /// в каком состоянии их застала последняя сборка страницы.
        /// </summary>
        static void ChannelButtons()
        {
            Console.WriteLine();
            Console.WriteLine("== кнопки канала обновлений ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = typeof(MainWindow);
            FieldInfo busy = t.GetField("_updBusy", f);
            FieldInfo dev = t.GetField("_updDev", f);
            FieldInfo stable = t.GetField("_updStable", f);
            MethodInfo about = t.GetMethod("PageAbout", f);
            MethodInfo show = t.GetMethod("ShowBusy", f);
            if (busy == null || dev == null || stable == null || about == null || show == null)
            { Check("кнопки канала доступны", false, "нет поля или метода"); return; }

            Settings saved = _s.Snapshot();
            try
            {
                _s.UpdateChannel = Settings.ChannelStable;
                busy.SetValue(_win, false);
                about.Invoke(_win, null);

                Button d = (Button)dev.GetValue(_win);
                d.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("«Dev» правда переводит канал",
                      _s.UpdateChannel == Settings.ChannelDev, "канал " + _s.UpdateChannel);

                Button st = (Button)stable.GetValue(_win);
                st.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("«Stable» правда возвращает канал",
                      _s.UpdateChannel == Settings.ChannelStable, "канал " + _s.UpdateChannel);

                // Проверка началась после того, как страницу собрали: гасить кнопки
                // некому, кроме общего признака занятости.
                busy.SetValue(_win, true);
                show.Invoke(_win, null);
                Check("начатая проверка гасит кнопки канала",
                      !((Button)dev.GetValue(_win)).IsEnabled &&
                      !((Button)stable.GetValue(_win)).IsEnabled,
                      "остались живыми, а щелчок по ним молча ничего не делает");

                // И кончилась — тоже без пересборки: на спокойной машине её можно
                // ждать очень долго, а канал всё это время не переключить.
                busy.SetValue(_win, false);
                show.Invoke(_win, null);
                Check("кончившаяся проверка возвращает кнопки канала",
                      ((Button)dev.GetValue(_win)).IsEnabled &&
                      ((Button)stable.GetValue(_win)).IsEnabled,
                      "остались серыми — переключить канал нечем");
            }
            catch (Exception e) { Check("страница «О программе» собирается", false, Inner(e)); }
            finally { busy.SetValue(_win, false); Restore(saved); }
        }

        /// <summary>
        /// Две сноски о заменителе Fn стоят одна под другой и обязаны сойтись:
        /// одна советует выбрать заменитель ради F13 и дальше, вторая говорила,
        /// что для верхнего ряда он не нужен.
        /// </summary>
        static void FnNoteAboveTwelve()
        {
            Console.WriteLine();
            Console.WriteLine("== заменитель Fn при уступленном ряде ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo takes = typeof(AppleDriver).GetField("_takesRow", f);
            FieldInfo media = typeof(KeyWatch).GetField("_mediaSeen", f);
            FieldInfo maxF = typeof(KeyWatch).GetField("_maxFunctionKey", f);
            if (takes == null || media == null || maxF == null)
            { Check("состояние драйвера доступно", false, "нет поля"); return; }

            object wasT = takes.GetValue(null), wasM = media.GetValue(null), wasX = maxF.GetValue(null);
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                takes.SetValue(null, true); media.SetValue(null, true);
                Freeze();
                _s.MediaFirst = false; _s.FnSubstitute = ModKey.None; _s.FnNavigation = false;

                maxF.SetValue(null, 19);
                _eng.Apply(_s);
                UIElement big = (UIElement)m.Invoke(_win, null);
                Check("с клавишами выше двенадцатой сказано, что заменитель нужен",
                      Says(big, "Для F13 и дальше он нужен"), "не сказано");
                Check("и «не нужен» сказано про F1–F12, а не про весь ряд",
                      Says(big, "для F1–F12 заменитель не нужен")
                          && !Says(big, "для верхнего ряда заменитель не нужен"),
                      "отменяет карточку выше, которая советует его выбрать");

                // Контрольный опыт: ровно двенадцать клавиш — и говорить о F13 нечего.
                maxF.SetValue(null, 12);
                Freeze();
                _eng.Apply(_s);
                Check("контроль: без клавиш выше двенадцатой про F13 не говорят",
                      !Says((UIElement)m.Invoke(_win, null), "Для F13 и дальше он нужен"),
                      "говорят");
            }
            catch (Exception e) { Check("страница «Клавиши» собирается", false, Inner(e)); }
            finally
            {
                takes.SetValue(null, wasT); media.SetValue(null, wasM); maxF.SetValue(null, wasX);
                Restore(saved);
            }
        }

        /// <summary>
        /// ANSI — это и есть «по нажатиям ничего не встретилось»: признак заведён им же
        /// и меняется только на ISO и JIS. Выдавать его за распознанное значит уверять
        /// человека с европейской клавиатурой, что две клавиши у него стоят правильно.
        /// </summary>
        static void PhysNoteAnsiIsNotDetected()
        {
            Console.WriteLine();
            Console.WriteLine("== «распознано ANSI» там, где не распознано ничего ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo det = typeof(KeyWatch).GetField("_detectedPhys", f);
            if (det == null) { Check("признак распознанного исполнения доступен", false, "нет поля"); return; }
            object was = det.GetValue(null);
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageLayout",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                _s.Physical = PhysLayout.Auto; _eng.Apply(_s);

                det.SetValue(null, (int)PhysLayout.Ansi);
                Check("ANSI не выдают за распознанное",
                      !Says((UIElement)m.Invoke(_win, null), "Пока распознано"),
                      "уверяет, что распознало ANSI, — а не встретилось ничего");

                // Контрольный опыт: ISO по нажатиям и правда распознаётся, и об этом
                // говорят. Без него проверка выше прошла бы на пустой сноске.
                det.SetValue(null, (int)PhysLayout.Iso);
                Check("контроль: распознанное ISO называют распознанным",
                      Says((UIElement)m.Invoke(_win, null), "Пока распознано"), "не называют");
            }
            catch (Exception e) { Check("страница «Раскладка» собирается", false, Inner(e)); }
            finally { det.SetValue(null, was); Restore(saved); }
        }

        /// <summary>
        /// Драйвер забирает ровно первые двенадцать клавиш. На клавиатуре с цифровым
        /// блоком режим верхнего ряда после этого решает всё для F13–F19 — и гасить его
        /// нельзя: погашенный, он не давал вернуть назначения этих клавиш ничем.
        ///
        /// Состояние драйвера подменяем: на машине без него эта ветка не исполнялась бы
        /// ни разу, и записанное в ней ожидание оставалось бы непроверенным.
        /// </summary>
        static void YieldedRowAboveTwelve()
        {
            Console.WriteLine();
            Console.WriteLine("== ряд отдан драйверу, а клавиш больше двенадцати ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo takes = typeof(AppleDriver).GetField("_takesRow", f);
            FieldInfo media = typeof(KeyWatch).GetField("_mediaSeen", f);
            FieldInfo maxF = typeof(KeyWatch).GetField("_maxFunctionKey", f);
            if (takes == null || media == null || maxF == null)
            { Check("состояние драйвера доступно", false, "нет поля"); return; }

            object wasT = takes.GetValue(null), wasM = media.GetValue(null), wasX = maxF.GetValue(null);
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                takes.SetValue(null, true); media.SetValue(null, true); maxF.SetValue(null, 19);
                Freeze();
                _s.MediaFirst = false; _s.FnSubstitute = ModKey.None;
                _eng.Apply(_s);

                var boxes = new List<CheckBox>();
                var combos = new List<ComboBox>();
                UIElement page = (UIElement)m.Invoke(_win, null);
                Walk(page, boxes, combos);
                int dead = 0;
                foreach (ComboBox b in combos) if (!b.IsEnabled) dead++;
                Check("драйверу отданы ровно двенадцать строк, режим остаётся живым",
                      dead == 12, "погашено списков: " + dead);
                Check("о мёртвых назначениях F13 и дальше сказано",
                      Says(page, "F13 и дальше всегда приходят обычными"), "не сказано");

                // Контрольный опыт: у клавиатуры ровно двенадцать F-клавиш — тогда
                // при уступленном ряде гаснет и режим, а говорить не о чем.
                maxF.SetValue(null, 12);
                Freeze();
                _eng.Apply(_s);
                boxes = new List<CheckBox>(); combos = new List<ComboBox>();
                page = (UIElement)m.Invoke(_win, null);
                Walk(page, boxes, combos);
                dead = 0;
                foreach (ComboBox b in combos) if (!b.IsEnabled) dead++;
                Check("контроль: без клавиш выше двенадцатой гаснет и режим",
                      dead == 13, "погашено списков: " + dead);
            }
            catch (Exception e) { Check("страница «Клавиши» собирается", false, Inner(e)); }
            finally
            {
                takes.SetValue(null, wasT); media.SetValue(null, wasM); maxF.SetValue(null, wasX);
                Restore(saved);
            }
        }

        /// <summary>
        /// Половинки пары стрелок таблица macOS забирает по одной — карточка «Показать
        /// все» для того и есть. Сноска, спрашивающая только про левую и верхнюю,
        /// обещала листание страницы, тогда как Fn+↓ уходил в конец документа.
        /// </summary>
        static void FnNoteChecksBothArrows()
        {
            Console.WriteLine();
            Console.WriteLine("== пара стрелок в сноске о навигации Fn ==");
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                _s.MacShortcuts = true; _s.FnNavigation = true;
                _s.MacShortcutsOff = new string[0];
                _s.FnSubstitute = ModKey.RWin; _s.MapRWin = ModKey.RWin;
                _eng.Apply(_s);
                Check("контроль: пока пара целиком у таблицы, листание не обещают",
                      !Says((UIElement)m.Invoke(_win, null), "Fn+↑↓ листают страницу"), "обещают");

                _s.MacSet("docstart", false);         // выключили только ⌘↑
                _eng.Apply(_s);
                Check("половина пары осталась у таблицы — листание не обещают",
                      !Says((UIElement)m.Invoke(_win, null), "Fn+↑↓ листают страницу"),
                      "обещает листание, а ⌘↓ в таблице жив и даёт конец документа");

                _s.MacSet("docend", false);           // теперь пары нет целиком
                _eng.Apply(_s);
                Check("контроль: пары нет целиком — листание обещают",
                      Says((UIElement)m.Invoke(_win, null), "Fn+↑↓ листают страницу"),
                      "не обещают — значит проверки выше ничего не доказывают");
            }
            catch (Exception e) { Check("страница «Клавиши» собирается", false, Inner(e)); }
            Restore(saved);
        }

        /// <summary>
        /// Выбранное руками исполнение побеждает и модель, и распознавание — так отвечает
        /// Engine.Physical. Сноска обязана говорить то же самое: иначе две клавиши
        /// меняются местами, а карточка уверяет, что распознано другое.
        /// </summary>
        static void PhysNoteFollowsChoice()
        {
            Console.WriteLine();
            Console.WriteLine("== исполнение, выбранное руками ==");
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageLayout",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                _s.Physical = PhysLayout.Auto; _eng.Apply(_s);
                Check("контроль: при «Определять самой» сноска про нажатия есть",
                      Says((UIElement)m.Invoke(_win, null), "определяется по нажатиям"),
                      "её нет — значит проверка ниже ничего не доказывает");

                _s.Physical = PhysLayout.Iso; _eng.Apply(_s);
                Check("выбранное руками исполнение не выдают за распознанное",
                      !Says((UIElement)m.Invoke(_win, null), "определяется по нажатиям"), "выдают");
            }
            catch (Exception e) { Check("страница «Раскладка» собирается", false, Inner(e)); }
            Restore(saved);
        }

        /// <summary>
        /// Выключенные переназначения — это выключенные переназначения. Обещать, что
        /// назначение ⏏ работает, когда обработчик уходит молча, значит врать.
        /// </summary>
        static void OffMeansOff()
        {
            Console.WriteLine();
            Console.WriteLine("== выключенные переназначения на странице «Клавиши» ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo seen = typeof(KeyWatch).GetField("_ejectSeen", f);
            if (seen == null) { Check("признак прихода ⏏ доступен", false, "нет поля"); return; }
            object was = seen.GetValue(null);
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                seen.SetValue(null, true);
                // Назначение обязано быть: без него карточка честно предлагает выбрать,
                // и хвалиться работающим назначением ей нечем.
                _s.EjectKey = "key.delete";
                _s.Enabled = true; _s.PauseWhenAppleAbsent = false; _eng.Apply(_s);
                Check("контроль: при включённых переназначениях так и сказано",
                      Says((UIElement)m.Invoke(_win, null), "назначение работает"),
                      "не сказано — значит проверка ниже ничего не доказывает");

                _s.Enabled = false; _eng.Apply(_s);
                Check("при выключенных переназначениях не обещают, что назначение работает",
                      !Says((UIElement)m.Invoke(_win, null), "назначение работает"), "обещают");

                // Пауза без клавиатуры Apple — вторая причина, по которой обработчик
                // уходит молча, и причина другая: выключатель при этом ВКЛЮЧЁН.
                // Отправлять человека выключать включённое — врать во второй раз.
                _s.Enabled = true; _s.PauseWhenAppleAbsent = true; _eng.Apply(_s);
                UIElement paused = (UIElement)m.Invoke(_win, null);
                Check("на паузе не обещают, что назначение работает",
                      !Says(paused, "назначение работает"), "обещают");
                Check("на паузе не сваливают вину на выключатель",
                      !Says(paused, "переназначения выключены на первой странице"),
                      "называет причиной выключатель, который включён");
            }
            catch (Exception e) { Check("страница «Клавиши» собирается", false, Inner(e)); }
            finally { seen.SetValue(null, was); Restore(saved); }
        }

        /// <summary>
        /// Прокрутить очередь потока окна. Без неё всё, что уходит через ToWindow,
        /// в стенде не выполняется никогда — то есть ответы всякой фоновой работы
        /// стенд не видит вовсе.
        /// </summary>
        static void Pump()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            _win.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                (Action)delegate { frame.Continue = false; });
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        /// <summary>
        /// Счёт по странице драйвера идёт в стороне — и обязан дойти до окна. Пока он
        /// не дошёл, страница обещает «Ищу 7-Zip…», и ни кнопки «Открыть страницу 7-Zip»,
        /// ни кнопки уборки кэша на ней нет вовсе.
        /// </summary>
        static void DriverProbeReachesWindow()
        {
            Console.WriteLine();
            Console.WriteLine("== счёт по странице драйвера ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = typeof(MainWindow);
            FieldInfo probing = t.GetField("_probing", f);
            FieldInfo probedFor = t.GetField("_probedFor", f);
            FieldInfo seven = t.GetField("_sevenZip", f);
            MethodInfo driver = t.GetMethod("PageDriver", f);
            if (probing == null || probedFor == null || seven == null || driver == null)
            { Check("счёт по странице драйвера доступен", false, "нет поля или метода"); return; }

            probedFor.SetValue(_win, null);
            seven.SetValue(_win, null);
            try { driver.Invoke(_win, null); }
            catch (Exception e) { Check("страница драйвера собирается", false, Inner(e)); return; }

            for (int i = 0; i < 300 && (bool)probing.GetValue(_win); i++)
            { Pump(); System.Threading.Thread.Sleep(50); }

            Check("счёт кончился и дошёл до окна",
                  !(bool)probing.GetValue(_win) && seven.GetValue(_win) != null,
                  "страница осталась в состоянии «Ищу 7-Zip…»");

            UIElement page = (UIElement)driver.Invoke(_win, null);
            Check("досчитав, страница не обещает «Ищу 7-Zip…»",
                  !Says(page, "Ищу 7-Zip"), "обещает");
            // Контрольный опыт: пока счёт идёт, «Ищу 7-Zip…» на странице есть —
            // значит проверка выше меряет конец счёта, а не поломку обхода.
            probedFor.SetValue(_win, null);
            seven.SetValue(_win, null);
            probing.SetValue(_win, true);
            Check("контроль: пока счёт идёт, страница так и говорит",
                  Says((UIElement)driver.Invoke(_win, null), "Ищу 7-Zip"), "не говорит");
            probing.SetValue(_win, false);
        }

        /// <summary>
        /// Ряд, отданный драйверу: списки о нём обязаны быть мертвы, и ровно они.
        /// Живой список щёлкается стендом и сохраняется — то есть проходит там, где
        /// человек до него не дотянется.
        /// </summary>
        static void YieldedRowIsDead()
        {
            Console.WriteLine();
            Console.WriteLine("== ряд, отданный драйверу ==");
            bool yield = Engine.YieldsRow(_s, 0);
            MethodInfo m = typeof(MainWindow).GetMethod("PageKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var boxes = new List<CheckBox>();
            var combos = new List<ComboBox>();
            try { Walk((UIElement)m.Invoke(_win, null), boxes, combos); }
            catch (Exception e) { Check("страница «Клавиши» собирается", false, Inner(e)); return; }

            int dead = 0;
            foreach (ComboBox b in combos) if (!b.IsEnabled) dead++;

            // Двенадцать строк F1–F12 плюс список основного режима — и ничего сверх того.
            Check(yield ? "ряд у драйвера: F1–F12 и режим погашены"
                        : "ряд у программы: погашенных списков нет",
                  yield ? dead == 13 : dead == 0,
                  "погашено списков: " + dead + " (уступаем: " + yield + ")");
        }

        /// <summary>
        /// Сноска о навигации Fn обещает только то, чего не забрала таблица macOS.
        /// Забирает она по-разному: у ⌥ это ←→ и Backspace, у ⌘ — ↑↓ и Backspace.
        /// </summary>
        static void FnNoteMatchesTable()
        {
            Console.WriteLine();
            Console.WriteLine("== сноска о навигации Fn ==");
            Settings saved = _s.Snapshot();
            MethodInfo m = typeof(MainWindow).GetMethod("PageKeys",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                _s.MacShortcuts = true; _s.FnNavigation = true;
                _s.MacShortcutsOff = new string[0];
                _s.FnSubstitute = ModKey.RAlt; _s.MapRAlt = ModKey.RAlt; _s.OptLevel = OptLevel.Off;
                _eng.Apply(_s);
                UIElement p = (UIElement)m.Invoke(_win, null);
                Check("контроль: с ⌥-заменителем сноска обещает Fn+↑↓",
                      Says(p, "Fn+↑↓ листают страницу"),
                      "не обещает — значит проверки ниже ничего не доказывают");
                Check("с ⌥-заменителем сноска не обещает Fn+←→",
                      !Says(p, "Fn+←→ уводят"), "обещает начало и конец строки");

                Restore(saved);
                _s.MacShortcuts = true; _s.FnNavigation = true;
                _s.MacShortcutsOff = new string[0];
                _s.FnSubstitute = ModKey.RWin; _s.MapRWin = ModKey.RWin;
                _eng.Apply(_s);
                p = (UIElement)m.Invoke(_win, null);
                Check("с ⌘-заменителем сноска не обещает Fn+↑↓",
                      !Says(p, "Fn+↑↓ листают страницу"), "обещает листание страницы");
                Check("с ⌘-заменителем сноска не обещает Fn+Backspace",
                      !Says(p, "Fn+Backspace удаляет вперёд"), "обещает удаление вперёд");
                // У ⌘ таблица забирает всё, кроме Fn+Enter, — его сноска обязана
                // назвать. Без этого три проверки выше прошли бы и на пустой сноске.
                Check("контроль: с ⌘-заменителем сноска обещает хотя бы Fn+Enter",
                      Says(p, "Fn+Enter — это Insert"),
                      "не обещает — значит проверки выше ничего не доказывают");
            }
            catch (Exception e) { Check("страница «Клавиши» собирается", false, Inner(e)); }
            Restore(saved);
        }

        /// <summary>
        /// Уходя с глаз, страница проверки перестаёт слушать и забывает набранное:
        /// список последних нажатий — это то, что человек печатал.
        /// </summary>
        static void SelfTestForgets()
        {
            Console.WriteLine();
            Console.WriteLine("== подписка страницы проверки ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = typeof(MainWindow);
            FieldInfo test = t.GetField("_selfTest", f);
            FieldInfo lines = t.GetField("_selfTestLines", f);
            MethodInfo diag = t.GetMethod("PageDiag", f);
            MethodInfo mac = t.GetMethod("PageMacKeys", f);
            MethodInfo attach = t.GetMethod("AttachWatch", f);
            MethodInfo detach = t.GetMethod("DetachSelfTest", f);
            MethodInfo forget = t.GetMethod("ForgetSelfTest", f);
            if (test == null || lines == null || diag == null || mac == null
                || attach == null || detach == null || forget == null)
            { Check("страница проверки доступна", false, "нет поля или метода"); return; }

            // Подписку ставит не сама страница, а BuildPage после сборки: слушателей
            // у переписи двое. Здесь повторяется тот же порядок — собрать, потом слушать.

            // Спрятанное окно слушать не должно: страницу пересобирают и мимо событий
            // «спрятали» и «потеряли фокус» — ответ о заряде приходит раз в минуту сам,
            // щелчок по значку в трее зовёт пересборку, смена темы Windows тоже. Снять
            // вернувшуюся подписку было бы уже нечем.
            MainWindow.ShownProbe = delegate { return false; };
            try
            {
                diag.Invoke(_win, null);
                attach.Invoke(_win, null);
                Check("спрятанное окно не подписывается на нажатия",
                      test.GetValue(_win) == null,
                      "невидимое окно снова слушает клавиатуру и копит набранное");
            }
            catch (Exception e) { Check("страница проверки собирается", false, Inner(e)); }
            finally { MainWindow.ShownProbe = null; }

            MainWindow.ShownProbe = delegate { return true; };
            try { diag.Invoke(_win, null); attach.Invoke(_win, null); }
            catch (Exception e)
            {
                Check("страница проверки собирается", false, Inner(e));
                MainWindow.ShownProbe = null; return;
            }
            finally { MainWindow.ShownProbe = null; }
            Check("на странице проверки подписка есть", test.GetValue(_win) != null, "подписки нет");

            detach.Invoke(_win, null);
            Check("уход со страницы снимает подписку", test.GetValue(_win) == null, "подписка осталась");

            // Схема клавиатуры на «Основных» — второй слушатель, и правило у него то же.
            // Подсветка нажатой клавиши показывает, что человек печатает, ничуть не хуже
            // списка: в чужом окне это подглядывание через плечо.
            MainWindow.ShownProbe = delegate { return false; };
            try { mac.Invoke(_win, null); attach.Invoke(_win, null); }
            finally { MainWindow.ShownProbe = null; }
            Check("спрятанное окно не слушает и ради схемы", test.GetValue(_win) == null,
                  "невидимое окно слушает клавиатуру ради подсветки");

            MainWindow.ShownProbe = delegate { return true; };
            try { mac.Invoke(_win, null); attach.Invoke(_win, null); }
            finally { MainWindow.ShownProbe = null; }
            // Схему рисуют только по узнанной модели. Без клавиатуры Apple подсвечивать
            // нечего — и не слушать тогда правильно, а не забывчиво. Оба ответа законны,
            // и проверяется тот, который к делу.
            if (Devices.AppleModel == null)
                Check("без клавиатуры Apple слушать незачем", test.GetValue(_win) == null,
                      "окно слушает клавиатуру, а подсвечивать нечего");
            else
                Check("схема на «Основных» слушает нажатия", test.GetValue(_win) != null,
                      "подсвечивать нажатия нечем");
            forget.Invoke(_win, null);

            MainWindow.ShownProbe = delegate { return true; };
            try { diag.Invoke(_win, null); attach.Invoke(_win, null); }
            finally { MainWindow.ShownProbe = null; }
            var log = (List<string>)lines.GetValue(_win);
            log.Add("клавиша A   vk=41");
            forget.Invoke(_win, null);
            Check("уход с глаз стирает набранное", log.Count == 0, "осталось строк: " + log.Count);
            Check("уход с глаз снимает подписку", test.GetValue(_win) == null, "подписка осталась");
        }

        /// <summary>Есть ли где-нибудь на странице такая надпись.</summary>
        static bool Says(object node, string part)
        {
            var t = node as TextBlock;
            if (t != null) return t.Text != null && t.Text.IndexOf(part, StringComparison.Ordinal) >= 0;
            var content = node as ContentControl;
            if (content != null && Says(content.Content, part)) return true;
            var panel = node as Panel;
            if (panel != null) foreach (UIElement c in panel.Children) if (Says(c, part)) return true;
            var border = node as Border;
            if (border != null && Says(border.Child, part)) return true;
            return false;
        }

        /// <summary>
        /// Пятый вид карточки обновления — работа идёт. Он тоже обязан пережить
        /// самопроизвольную пересборку, а проверен не был.
        /// </summary>
        static void UpdateCardBusy()
        {
            Console.WriteLine();
            Console.WriteLine("== карточка обновления: работа идёт ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = typeof(MainWindow);
            FieldInfo busy = t.GetField("_updBusy", f);
            FieldInfo busyText = t.GetField("_updBusyText", f);
            FieldInfo found = t.GetField("_updFound", f);
            FieldInfo status = t.GetField("_updStatus", f);
            FieldInfo action = t.GetField("_updAction", f);
            MethodInfo about = t.GetMethod("PageAbout", f);
            if (busy == null || busyText == null || found == null || about == null)
            { Check("занятость карточки обновления доступна", false, "нет поля или метода"); return; }

            var release = new Updater.Release();
            release.Tag = "v9.9.9"; release.Version = new Version(9, 9, 9);
            busy.SetValue(_win, true);
            busyText.SetValue(_win, "Скачиваю…");
            found.SetValue(_win, release);
            try { about.Invoke(_win, null); }
            catch (Exception e) { Check("страница «О программе» собирается во время работы", false, Inner(e)); return; }

            var box = (TextBlock)status.GetValue(_win);
            var btn = (Button)action.GetValue(_win);
            Check("«Скачиваю…» переживает пересборку",
                  box != null && box.Text == "Скачиваю…",
                  "показано «" + (box == null ? "ничего" : box.Text) + "»");
            Check("пока идёт работа, кнопка погашена", btn != null && !btn.IsEnabled, "кнопка жива");

            busy.SetValue(_win, false);
            found.SetValue(_win, null);
        }

        /// <summary>Карточка о непрочитанном файле настроек.</summary>
        static void BrokenSettingsCard()
        {
            Console.WriteLine();
            Console.WriteLine("== непрочитанный файл настроек ==");
            string was = Settings.LoadFailed;
            Settings.LoadFailed = "C:" + (char)92 + "проверка" + (char)92 + "settings.xml.bad";
            MethodInfo m = typeof(MainWindow).GetMethod("PageMacKeys",
                               BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                Check("о непрочитанном файле сказано на первой странице",
                      Says((UIElement)m.Invoke(_win, null), Settings.LoadFailed),
                      "карточки нет");
            }
            catch (Exception e) { Check("страница с испорченным файлом собирается", false, Inner(e)); }
            Settings.LoadFailed = was;
        }

        /// <summary>Отказ автозапуска: настройку пишет не файл, а реестр, и он вправе отказать.</summary>
        static void AutostartNotice()
        {
            Console.WriteLine();
            Console.WriteLine("== отказ автозапуска ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo note = typeof(MainWindow).GetField("_autostartNotice", f);
            MethodInfo card = typeof(MainWindow).GetMethod("StartupCard", f);
            if (note == null || card == null) { Check("отказ автозапуска доступен", false, "нет поля или метода"); return; }
            note.SetValue(_win, "Windows не дала записать автозапуск.");
            try
            {
                Check("отказ автозапуска показан в карточке «Запуск»",
                      Says((UIElement)card.Invoke(_win, null), "не дала записать"), "карточка молчит");
            }
            catch (Exception e) { Check("карточка «Запуск» собирается", false, Inner(e)); }
            note.SetValue(_win, null);
        }

        /// <summary>Кнопка «Остановить» показывается только над тем, что правда останавливается.</summary>
        static void StopButtonFollowsCancellable()
        {
            Console.WriteLine();
            Console.WriteLine("== кнопка «Остановить» ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo can = typeof(MainWindow).GetField("_setupCancellable", f);
            FieldInfo stop = typeof(MainWindow).GetField("_setupStop", f);
            MethodInfo driver = typeof(MainWindow).GetMethod("PageDriver", f);
            if (can == null || stop == null || driver == null)
            { Check("кнопка остановки доступна", false, "нет поля или метода"); return; }

            can.SetValue(_win, false);
            try { driver.Invoke(_win, null); }
            catch (Exception e) { Check("страница драйвера собирается", false, Inner(e)); return; }
            var b = (Button)stop.GetValue(_win);
            Check("без отменяемого шага «Остановить» спрятана",
                  b != null && b.Visibility == Visibility.Collapsed, "кнопка показана");

            can.SetValue(_win, true);
            driver.Invoke(_win, null);
            b = (Button)stop.GetValue(_win);
            Check("вокруг закачки «Остановить» показана",
                  b != null && b.Visibility == Visibility.Visible, "кнопки нет");
            can.SetValue(_win, false);
        }

        /// <summary>Отметка о показанном ставится по факту сборки, а не по факту вызова.</summary>
        static void MarkFollowsBuild()
        {
            Console.WriteLine();
            Console.WriteLine("== отметка о показанном ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo building = typeof(MainWindow).GetField("_building", f);
            FieldInfo mark = typeof(MainWindow).GetField("_deviceMark", f);
            MethodInfo refresh = typeof(MainWindow).GetMethod("RefreshDevices",
                                     BindingFlags.Public | BindingFlags.Instance);
            if (building == null || mark == null || refresh == null)
            { Check("отметка о показанном доступна", false, "нет поля или метода"); return; }

            mark.SetValue(_win, null);
            building.SetValue(_win, true);          // будто сборка уже идёт
            try { refresh.Invoke(_win, null); }
            finally { building.SetValue(_win, false); }
            Check("отметка не ставится, пока страница не собралась",
                  mark.GetValue(_win) == null, "отметка уже стоит");

            refresh.Invoke(_win, null);
            Check("после сборки отметка ставится", mark.GetValue(_win) != null, "отметки нет");
        }

        /// <summary>
        /// Три вида карточки обновления. Стенд собирал только четвёртый — покой, — а из
        /// трёх остальных два переживают самопроизвольную пересборку и один нет.
        /// Особенно важен отказ: через него приходит «установщик подписан чужим ключом»,
        /// и он не должен смениться приглашением обновиться от первой же нажатой клавиши.
        /// </summary>
        static void UpdateCardStates()
        {
            Console.WriteLine();
            Console.WriteLine("== карточка обновления ==");
            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = typeof(MainWindow);
            FieldInfo found = t.GetField("_updFound", f);
            FieldInfo said = t.GetField("_updSaid", f);
            FieldInfo status = t.GetField("_updStatus", f);
            FieldInfo action = t.GetField("_updAction", f);
            MethodInfo about = t.GetMethod("PageAbout", f);
            if (found == null || said == null || status == null || action == null || about == null)
            { Check("карточка обновления доступна", false, "нет поля или метода"); return; }

            var release = new Updater.Release();
            release.Tag = "v9.9.9";
            release.Version = new Version(9, 9, 9);

            // Отказ загрузки: выпуск найден, но поставить не вышло.
            found.SetValue(_win, release);
            said.SetValue(_win, "установщик подписан чужим ключом");
            try { about.Invoke(_win, null); }
            catch (Exception e) { Check("страница «О программе» собирается", false, Inner(e)); return; }

            var box = (TextBlock)status.GetValue(_win);
            Check("отказ загрузки виден после пересборки",
                  box != null && box.Text == "установщик подписан чужим ключом",
                  "показано «" + (box == null ? "ничего" : box.Text) + "»");
            var btn = (Button)action.GetValue(_win);
            Check("после отказа кнопка предлагает повтор",
                  btn != null && "" + btn.Content == "Повторить",
                  "" + (btn == null ? "нет кнопки" : "" + btn.Content));

            // Установщик запущен: предлагать больше нечего.
            said.SetValue(_win, "Установщик запущен");
            found.SetValue(_win, null);
            about.Invoke(_win, null);
            box = (TextBlock)status.GetValue(_win);
            btn = (Button)action.GetValue(_win);
            Check("«установщик запущен» переживает пересборку",
                  box != null && box.Text == "Установщик запущен",
                  "показано «" + (box == null ? "ничего" : box.Text) + "»");
            // Кнопка остаётся живой: запрос прав администратора можно отклонить,
            // установщик закрыть, — и проверить заново человек вправе всегда.
            Check("после запуска установщика кнопка предлагает проверить заново",
                  btn != null && btn.IsEnabled && "" + btn.Content == "Проверить",
                  btn == null ? "нет кнопки" : "«" + btn.Content + "», жива=" + btn.IsEnabled);

            // Найденный выпуск без отказа: приглашение обновиться.
            said.SetValue(_win, null);
            found.SetValue(_win, release);
            about.Invoke(_win, null);
            box = (TextBlock)status.GetValue(_win);
            btn = (Button)action.GetValue(_win);
            Check("найденный выпуск переживает пересборку",
                  box != null && box.Text == "Есть новый выпуск v9.9.9",
                  "показано «" + (box == null ? "ничего" : box.Text) + "»");
            Check("над найденным выпуском кнопка предлагает обновить",
                  btn != null && "" + btn.Content == "Обновить" && btn.IsEnabled,
                  "" + (btn == null ? "нет кнопки" : "" + btn.Content));

            found.SetValue(_win, null);
            said.SetValue(_win, null);
        }

        /// <summary>
        /// Строки отдельных сочетаний. Без раскрытой группы они не строятся вовсе —
        /// стенда не касались. А над ними стоит переключатель всей группы, который
        /// считает себя по ним: строка, не пересобравшая страницу, оставляет его лгать.
        /// </summary>
        static void MacRowsInGroups()
        {
            Console.WriteLine();
            Console.WriteLine("== отдельные сочетания macOS ==");
            FieldInfo open = typeof(MainWindow).GetField("_macOpen",
                                 BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo m = typeof(MainWindow).GetMethod("PageMacKeys",
                                 BindingFlags.NonPublic | BindingFlags.Instance);
            if (open == null || m == null) { Check("страница сочетаний доступна", false, "нет поля или метода"); return; }

            // Сколько переключателей на странице, когда все группы свёрнуты, — с этим
            // и сравниваем. Голое «больше одного» истинно всегда: над группами стоят
            // ещё пять переключателей, и проверка не могла провалиться.
            Settings saved = _s.Snapshot();
            open.SetValue(_win, null);
            var shutBoxes = new List<CheckBox>();
            var shutCombos = new List<ComboBox>();
            try { Walk((UIElement)m.Invoke(_win, null), shutBoxes, shutCombos); }
            catch (Exception e) { Check("страница сочетаний собирается свёрнутой", false, Inner(e)); return; }
            int shut = shutBoxes.Count;

            string[] groups = { MacKeys.GroupEdit, MacKeys.GroupText, MacKeys.GroupSystem };
            foreach (string g in groups)
            {
                open.SetValue(_win, g);
                var boxes = new List<CheckBox>();
                var combos = new List<ComboBox>();
                try { Walk((UIElement)m.Invoke(_win, null), boxes, combos); }
                catch (Exception e) { Check("группа «" + g + "» раскрывается", false, Inner(e)); continue; }
                Check("группа «" + g + "»: строки показаны", boxes.Count > shut,
                      "переключателей столько же, сколько в свёрнутом виде — " + boxes.Count);
                foreach (CheckBox cb in boxes) Flip(cb);
                Restore(saved);
            }
            open.SetValue(_win, null);
            Restore(saved);
        }

        static void Check(string name, bool ok, string got)
        {
            if (ok) { _pass++; Console.WriteLine("  + " + name); }
            else { _fail++; _fails.Add(name + " — " + got); Console.WriteLine("  ! " + name + " — " + got); }
        }


        /// <summary>
        /// Ширина, которая достаётся содержимому страницы, когда окно сжато до
        /// предела: заявленный минимум окна минус левая колонка (248), отступы
        /// прокрутки (28 и 22), полоса прокрутки (17) и рамка окна (16).
        ///
        /// Считается от MinWidth самого окна, а не числом. Опустив минимум, окно
        /// само себе ужесточает проверку — иначе вёрстку можно было бы «починить»
        /// тем, что её перестали проверять на узком месте.
        /// </summary>
        static double NarrowPage { get { return _win.MinWidth - 248 - 28 - 22 - 17 - 16; } }

        /// <summary>
        /// Вёрстка при сжатом окне: слова не рвутся посередине и текст не вылезает
        /// за край карточки.
        ///
        /// Меряем, а не смотрим. Каждая надпись знает свою ширину после раскладки;
        /// рядом считаем ширину самого длинного слова в ней тем же шрифтом. Слово
        /// шире отведённого места — значит, WPF разорвёт его по буквам: ровно то,
        /// что видно на экране как «Переназначен / ия». Правый край надписи за краем
        /// страницы — значит, её обрежет карточка, как обрезало подписи переключателей,
        /// пока те лежали в StackPanel.
        /// </summary>
        static void NarrowWindowKeepsWordsWhole()
        {
            Console.WriteLine();
            Console.WriteLine("== вёрстка при сжатом окне ==");

            string[] pages = { "PageMacKeys", "PageKeys", "PageLayout", "PageDriver", "PageAbout", "PageDiag" };
            int torn = 0, spilled = 0, seen = 0;
            string firstTorn = null, firstSpill = null;

            foreach (string p in pages)
            {
                MethodInfo m = typeof(MainWindow).GetMethod(p, BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null) continue;
                UIElement page;
                try { page = (UIElement)m.Invoke(_win, null); }
                catch { continue; }

                var host = new Border { Width = NarrowPage, Child = page as UIElement };
                host.Measure(new Size(NarrowPage, 4000));
                host.Arrange(new Rect(0, 0, NarrowPage, host.DesiredSize.Height));
                host.UpdateLayout();

                var texts = new List<TextBlock>();
                Texts(host, texts);
                foreach (TextBlock tb in texts)
                {
                    if (String.IsNullOrEmpty(tb.Text)) continue;
                    double room = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
                    if (room <= 1) continue;
                    seen++;

                    if (tb.TextWrapping != TextWrapping.NoWrap)
                    {
                        double word = LongestWord(tb);
                        if (word > room + 1)
                        {
                            torn++;
                            if (firstTorn == null)
                                firstTorn = p + ": «" + Short(tb.Text) + "» — слову нужно " +
                                            Math.Round(word) + ", а места " + Math.Round(room);
                        }
                    }

                    double right;
                    try { right = tb.TransformToAncestor(host).Transform(new Point(tb.ActualWidth, 0)).X; }
                    catch { continue; }
                    if (right > NarrowPage + 1)
                    {
                        spilled++;
                        if (firstSpill == null)
                            firstSpill = p + ": «" + Short(tb.Text) + "» кончается на " +
                                         Math.Round(right) + " при ширине " + NarrowPage;
                    }
                }
            }

            Check("надписи вообще нашлись и померились", seen > 200, "померилось всего " + seen);
            Check("при сжатом окне слова не рвутся посередине", torn == 0,
                  "порвано " + torn + ", первое — " + firstTorn);
            Check("при сжатом окне надписи не вылезают за карточку", spilled == 0,
                  "вылезло " + spilled + ", первая — " + firstSpill);
        }

        /// <summary>
        /// Настройки идут за клавиатурой, а не за общим представлением о ней.
        ///
        /// Обмер снимков Apple показал две вещи, которых программа не знала: у полноразмерных
        /// есть правый control, а у японской компактной нет правого ⌥ — того самого, который
        /// с завода стоит заменителем Fn. Здесь проверяется, что окно и настройки об этом
        /// знают: несуществующую клавишу не выдают за рабочую, существующую не прячут.
        /// </summary>
        static void SettingsFollowTheKeyboard()
        {
            Console.WriteLine();
            Console.WriteLine("== настройки идут за клавиатурой ==");

            AppleModel compact = Models.Find(0x029A);      // Magic Keyboard с Touch ID
            AppleModel full = Models.Find(0x029F);         // она же с цифровым блоком
            Check("модели для проверки нашлись", compact != null && full != null, "нет в каталоге");
            if (compact == null || full == null) return;

            // ---- какие клавиши у клавиатуры есть ----
            Check("у полноразмерной есть правый control",
                  KeyboardArt.HasKey(full, PhysLayout.Ansi, Vk.RControl), "его нет");
            Check("у компактной его нет",
                  !KeyboardArt.HasKey(compact, PhysLayout.Ansi, Vk.RControl), "он есть");
            Check("у японской компактной нет правого ⌥",
                  !KeyboardArt.HasKey(compact, PhysLayout.Jis, Vk.RMenu), "он есть");
            Check("контроль: у американской компактной он есть",
                  KeyboardArt.HasKey(compact, PhysLayout.Ansi, Vk.RMenu), "его нет");
            Check("про неузнанную клавиатуру ответ «есть»",
                  KeyboardArt.HasKey(null, PhysLayout.Ansi, Vk.RMenu), "настройку спрячут ни за что");

            // ---- правый control как настоящая клавиша ----
            Settings saved = _s.Snapshot();
            _s.MapRCtrl = ModKey.LWin;
            Check("правый control ходит по своей настройке",
                  _s.TargetOf(ModKey.RCtrl) == ModKey.LWin, "остался " + _s.TargetOf(ModKey.RCtrl));
            _s.MapRCtrl = ModKey.RAlt;
            Check("и делает правый ⌥ достижимым", _s.Reaches(ModKey.RAlt), "не достаёт");
            Restore(saved);
            Check("контроль: сам собой он ничего не меняет",
                  _s.TargetOf(ModKey.RCtrl) == ModKey.RCtrl, "стал " + _s.TargetOf(ModKey.RCtrl));

            // ---- что из этого видно в окне ----
            Check("строка правого control есть при полноразмерной",
                  KeysPageSays(full, PhysLayout.Ansi, "control (правый)"), "строки нет");
            Check("и её нет при компактной",
                  !KeysPageSays(compact, PhysLayout.Ansi, "control (правый)"), "строка появилась");

            Check("про отсутствующий правый ⌥ окно говорит вслух",
                  KeysPageSays(compact, PhysLayout.Jis, "правого ⌥ нет"), "промолчало");
            Check("контроль: на американской не говорит",
                  !KeysPageSays(compact, PhysLayout.Ansi, "правого ⌥ нет"), "сказало лишнего");
        }

        /// <summary>
        /// Собрать страницу «Клавиши» так, будто на столе вот эта клавиатура, и поискать
        /// в ней надпись. Клавиатуру подставляем прямо в Devices: живой на прогоне нет,
        /// а окно спрашивает именно её.
        /// </summary>
        static bool KeysPageSays(AppleModel pretend, PhysLayout phys, string part)
        {
            FieldInfo fld = typeof(Devices).GetField("_appleModel",
                BindingFlags.Static | BindingFlags.NonPublic);
            object had = fld == null ? null : fld.GetValue(null);
            Settings saved = _s.Snapshot();
            try
            {
                if (fld != null) fld.SetValue(null, pretend);
                _s.Physical = phys;
                _eng.Apply(_s);

                MethodInfo mi = typeof(MainWindow).GetMethod("PageKeys",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var page = (UIElement)mi.Invoke(_win, null);
                var host = new Border { Width = 900, Child = page };
                host.Measure(new Size(900, 8000));
                host.Arrange(new Rect(0, 0, 900, host.DesiredSize.Height));
                host.UpdateLayout();

                var texts = new List<TextBlock>();
                Texts(host, texts);
                foreach (TextBlock tb in texts)
                    if (tb.Text != null && tb.Text.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
                return false;
            }
            finally
            {
                if (fld != null) fld.SetValue(null, had);
                Restore(saved);
                _eng.Apply(_s);
            }
        }

        /// <summary>Все надписи в разложенном дереве — по видимому дереву, а не логическому:
        /// подписи переключателей и списков живут в шаблонах.</summary>
        static void Texts(DependencyObject node, List<TextBlock> found)
        {
            var tb = node as TextBlock;
            if (tb != null) found.Add(tb);
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++) Texts(VisualTreeHelper.GetChild(node, i), found);
        }

        /// <summary>
        /// Ширина самого длинного неразрывного куска надписи. Разрывать строку WPF
        /// умеет по пробелу, а также после дефиса и косой черты — эти места здесь
        /// считаются законными, иначе проверка ругалась бы на «F1–F12» и на пути.
        /// </summary>
        static double LongestWord(TextBlock tb)
        {
            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            double widest = 0;
            var word = new StringBuilder();
            string s = tb.Text;
            for (int i = 0; i <= s.Length; i++)
            {
                bool end = i == s.Length;
                char c = end ? ' ' : s[i];
                bool space = c == ' ' || c == '\n' || c == '\r' || c == '\t' || c == ' ';
                bool after = c == '-' || c == '/' || c == '–' || c == '—';
                if (!space) word.Append(c);
                if (space || after || end)
                {
                    if (word.Length > 0)
                    {
                        var ft = new FormattedText(word.ToString(), System.Globalization.CultureInfo.CurrentUICulture,
                                                   FlowDirection.LeftToRight, typeface, tb.FontSize, Brushes.Black);
                        if (ft.Width > widest) widest = ft.Width;
                    }
                    word.Length = 0;
                }
            }
            return widest;
        }


        /// <summary>
        /// Окно с правами администратора: программа обязана и заметить его, и сказать
        /// о нём человеку.
        ///
        /// Настоящего такого окна на стенде взять негде — его пришлось бы запускать
        /// от администратора прямо во время прогона, — поэтому ответ Windows подменяет
        /// шов Elevated.Probe. Проверяется всё остальное: что встреча запоминается,
        /// что карточка называет имя, и что до встречи она не выдумывает её.
        /// </summary>
        static void ElevatedWindowIsSaidOutLoud()
        {
            Console.WriteLine();
            Console.WriteLine("== окно с правами администратора ==");
            MethodInfo card = typeof(MainWindow).GetMethod("ElevatedCard",
                                                           BindingFlags.NonPublic | BindingFlags.Instance);
            if (card == null) { Check("карточка есть", false, "нет метода ElevatedCard"); return; }

            Elevated.Forget();
            Elevated.Probe = delegate { return null; };
            Elevated.Sample();
            Check("досягаемое окно ничего не запоминает",
                  Elevated.SeenName == null && Elevated.SeenCount == 0 && !Elevated.SeenWithin(60000),
                  "запомнилось «" + Elevated.SeenName + "», счёт " + Elevated.SeenCount);

            object before = card.Invoke(_win, null);
            Check("до встречи карточка не выдумывает имени",
                  Says(before, "не попадалось ни разу"), "сказано другое");

            Elevated.Probe = delegate { return "ЧужаяПрограмма"; };
            Elevated.Sample();
            Elevated.Sample();
            Check("встреча запоминается с именем", Elevated.SeenName == "ЧужаяПрограмма",
                  "запомнилось «" + Elevated.SeenName + "»");
            Check("встречи считаются", Elevated.SeenCount == 2, "насчитано " + Elevated.SeenCount);
            Check("встреча свежая", Elevated.SeenWithin(60000), "SeenWithin отвечает «нет»");
            Check("давность считается", !Elevated.SeenWithin(0), "нулевой срок сочтён свежим");

            object after = card.Invoke(_win, null);
            Check("карточка называет виновника", Says(after, "ЧужаяПрограмма"), "имени в карточке нет");
            Check("карточка объясняет, почему сочетания стали старыми",
                  Says(after, "не видит нажатий"), "объяснения в карточке нет");

            // Страница «Основные» обязана показать карточку и при выключенных
            // сочетаниях: недосягаемость окна касается всего перехвата.
            Settings saved = _s.Snapshot();
            _s.MacShortcuts = false;
            MethodInfo page = typeof(MainWindow).GetMethod("PageMacKeys",
                                                           BindingFlags.NonPublic | BindingFlags.Instance);
            object off = page.Invoke(_win, null);
            Check("при выключенных сочетаниях карточка всё равно на странице",
                  Says(off, "ЧужаяПрограмма"), "карточки на странице нет");
            _s.MacShortcuts = true;
            object on = page.Invoke(_win, null);
            Check("и при включённых тоже", Says(on, "ЧужаяПрограмма"), "карточки на странице нет");
            Restore(saved);

            Elevated.Probe = null;
            Elevated.Forget();
            Check("забывание работает", Elevated.SeenName == null && Elevated.SeenTick == 0,
                  "осталось «" + Elevated.SeenName + "»");

            // И настоящая Windows, а не шов: вся затея держится на том, что метку
            // целостности удаётся прочитать. Не удастся — программа промолчит там,
            // где должна была сказать, и проверок выше это не заденет.
            Check("свой уровень целостности читается", Elevated.MyLevel >= 0x1000,
                  "прочитано " + Elevated.MyLevel);
            string name;
            bool higher = Elevated.ForegroundIsHigher(out name);
            Console.WriteLine("  · впереди сейчас: " + (higher ? name : "досягаемое окно"));
        }


        /// <summary>
        /// Схема клавиатуры: то, что нарисовано, должно быть правдой.
        ///
        /// Схема — единственное место в программе, где человек судит о правильности
        /// глазами, а не по надписи. Поэтому её проверяют не «собралась ли», а по
        /// существу: клавиши не налезают друг на друга, у каждой свой код, у клавиш
        /// без кода сказано почему, и отличия исполнений — настоящие.
        ///
        /// Живой клавиатуры для этого не нужно: раскладка считается из модели, а модели
        /// лежат в каталоге. Проверяются все.
        /// </summary>
        static void KeyboardArtIsHonest()
        {
            Console.WriteLine();
            Console.WriteLine("== схема клавиатуры ==");

            PhysLayout[] kinds = { PhysLayout.Ansi, PhysLayout.Iso, PhysLayout.Jis };
            int drawn = 0, overlaps = 0, dupes = 0, mute = 0;
            string firstOverlap = null, firstDupe = null, firstMute = null;

            foreach (int pid in AllProducts())
            {
                AppleModel m = Models.Find(pid);
                if (m == null) continue;
                foreach (PhysLayout phys in kinds)
                {
                    drawn++;
                    List<KeyCap> keys = KeyboardArt.Layout(m, phys);
                    string where = m.Designation + " " + phys;

                    // Налезающие клавиши. Прямоугольники сравниваются попарно: клавиш
                    // меньше сотни, и точность здесь дороже скорости.
                    for (int i = 0; i < keys.Count; i++)
                        for (int j = i + 1; j < keys.Count; j++)
                            if (Overlap(keys[i], keys[j]))
                            {
                                overlaps++;
                                if (firstOverlap == null)
                                    firstOverlap = where + ": «" + keys[i].Cap + "» и «" + keys[j].Cap + "»";
                            }

                    // Один код — одна клавиша. Исключение одно и настоящее: Enter
                    // на цифровом блоке Windows отдаёт тем же кодом, что и обычный.
                    var seen = new Dictionary<int, string>();
                    foreach (KeyCap k in keys)
                    {
                        if (k.Vk == 0) continue;
                        if (k.Vk == Vk.Return) continue;
                        string had;
                        if (seen.TryGetValue(k.Vk, out had))
                        {
                            dupes++;
                            if (firstDupe == null)
                                firstDupe = where + ": код " + k.Vk.ToString("X2") +
                                            " и у «" + had + "», и у «" + k.Cap + "»";
                        }
                        else seen[k.Vk] = k.Cap;
                    }

                    // Клавиша без кода обязана объяснить себя. Их ровно три вида:
                    // fn и Eject до Windows не доходят, Touch ID — не клавиша вовсе.
                    foreach (KeyCap k in keys)
                    {
                        if (k.Vk != 0) continue;
                        bool explained = !String.IsNullOrEmpty(k.Tip);
                        if (!explained)
                        {
                            mute++;
                            if (firstMute == null) firstMute = where + ": «" + k.Cap + "»";
                        }
                    }
                }
            }

            Check("все модели каталога нарисовались", drawn >= 60, "нарисовано " + drawn);
            Check("клавиши не налезают друг на друга", overlaps == 0,
                  "налезают " + overlaps + ", первая пара — " + firstOverlap);
            Check("у каждой клавиши свой код", dupes == 0,
                  "повторов " + dupes + ", первый — " + firstDupe);
            Check("клавиша без кода объясняет себя", mute == 0,
                  "молчунов " + mute + ", первый — " + firstMute);

            // Отличия исполнений — настоящие, а не подписью.
            AppleModel compact = Models.Find(0x029A);      // Magic Keyboard с Touch ID
            AppleModel full = Models.Find(0x029F);         // она же с цифровым блоком
            Check("модели для проверки нашлись", compact != null && full != null, "нет в каталоге");
            if (compact == null || full == null) return;

            Check("у ISO есть клавиша слева от Z", HasVk(compact, PhysLayout.Iso, Vk.Oem102),
                  "Oem102 на схеме нет");
            Check("у ANSI её нет", !HasVk(compact, PhysLayout.Ansi, Vk.Oem102),
                  "Oem102 попала на схему ANSI");
            Check("у ANSI есть клавиша Oem5", HasVk(compact, PhysLayout.Ansi, Vk.Oem5),
                  "Oem5 на схеме нет");
            Check("у JIS есть кана", HasVk(compact, PhysLayout.Jis, Vk.Kana),
                  "кода каны на схеме нет");
            Check("у ANSI её нет", !HasVk(compact, PhysLayout.Ansi, Vk.Kana),
                  "код каны попал на схему ANSI");

            Check("у компактной нет цифрового блока", !HasVk(compact, PhysLayout.Ansi, 0x64),
                  "цифровая 4 на компактной");
            Check("у полноразмерной он есть", HasVk(full, PhysLayout.Ansi, 0x64),
                  "цифровой 4 нет");
            Check("у компактной верхний ряд кончается на F12",
                  HasVk(compact, PhysLayout.Ansi, Vk.F12) && !HasVk(compact, PhysLayout.Ansi, Vk.F13),
                  "ряд не тот");
            Check("у полноразмерной он доходит до F19",
                  HasVk(full, PhysLayout.Ansi, Vk.F13 + 6), "F19 на схеме нет");

            // Clear и «=» цифрового блока — те самые две клавиши, ради которых написана
            // отдельная карточка настроек. На схеме они обязаны сказать о себе правду.
            Check("Clear на схеме названа своим кодом Windows",
                  Mentions(TipOf(full, PhysLayout.Ansi, Vk.NumLock), "Num Lock"),
                  "сказано: " + TipOf(full, PhysLayout.Ansi, Vk.NumLock));
            Check("«=» цифрового блока тоже",
                  Mentions(TipOf(full, PhysLayout.Ansi, Vk.Clear), "очистить"),
                  "сказано: " + TipOf(full, PhysLayout.Ansi, Vk.Clear));

            // ---- пропорции ----
            // Ряд обязан замостить главный блок без щелей: сумма ширин равна его ширине.
            // Это и есть обмер, переведённый в проверку, — 14,5 клавиши в каждом ряду
            // у каждой модели. Прежняя схема считала блок пятнадцатью, и от этого
            // esc с F-клавишами были шире настоящих на восьмую часть.
            int rows = 0, ragged = 0;
            string firstRagged = null;
            foreach (int pid in AllProducts())
            {
                AppleModel mm = Models.Find(pid);
                if (mm == null) continue;
                foreach (PhysLayout phys in kinds)
                {
                    List<KeyCap> keys = KeyboardArt.Layout(mm, phys);
                    for (int row = 0; row <= 5; row++)
                    {
                        double sum = 0;
                        int cnt = 0;
                        foreach (KeyCap k in keys)
                        {
                            if (k.X > 14.6) continue;               // только главный блок
                            if (Math.Floor(k.Y + 0.001) != row) continue;
                            if (k.Y > row + 0.4) continue;          // нижняя половина стрелок
                            sum += k.W;
                            cnt++;
                        }
                        if (cnt == 0) continue;                     // половинный ряд алюминиевой
                        rows++;

                        // У ISO и JIS средний ряд короче ровно на нижнюю половину Enter:
                        // она принадлежит ряду выше и посчитана там.
                        double want = 14.5;
                        foreach (KeyCap k in keys)
                            if (k.LowerW > 0 && Math.Floor(k.Y + 0.001) == row - 1) want = 14.5 - k.LowerW;

                        if (Math.Abs(sum - want) > 0.001)
                        {
                            ragged++;
                            if (firstRagged == null)
                                firstRagged = mm.Designation + " " + phys + ", ряд " + row +
                                              ": " + sum.ToString("F2") + " вместо " + want.ToString("F2");
                        }
                    }
                }
            }
            Check("ряды замощают блок без щелей", ragged == 0 && rows > 60,
                  "рядов " + rows + ", кривых " + ragged + ", первый — " + firstRagged);

            // Число клавиш — то же, что Apple печатает в своих характеристиках
            // беспроводных клавиатур: 78 у ANSI, 79 у ISO, 109 у полноразмерной.
            // Проверка внешняя: сходится она не с нашим обмером, а с чужим счётом.
            AppleModel alu = Models.Find(0x022C);        // Apple Wireless Keyboard A1255
            AppleModel aluFull = Models.Find(0x0220);    // Apple Keyboard с блоком A1243
            Check("у компактной ANSI 78 клавиш", Count(alu, PhysLayout.Ansi) == 78,
                  "их " + Count(alu, PhysLayout.Ansi));
            Check("у компактной ISO 79", Count(alu, PhysLayout.Iso) == 79,
                  "их " + Count(alu, PhysLayout.Iso));
            Check("у полноразмерной ANSI 109", Count(aluFull, PhysLayout.Ansi) == 109,
                  "их " + Count(aluFull, PhysLayout.Ansi));

            // Отношение сторон корпуса — самая внешняя проверка, какая тут возможна:
            // ширину и глубину Apple печатает в характеристиках, и они ни от какого
            // нашего обмера не зависят. 27,9 × 11,49 см даёт 2,4282; 41,87 × 11,49 — 3,6440.
            // Сюда разом входит всё: ширина блока, высота ряда, зазор и поля корпуса.
            Check("корпус компактной той же формы, что у Apple",
                  Near(Shape(Models.Find(0x029C), PhysLayout.Ansi), 2.4282, 0.01),
                  "вышло " + Shape(Models.Find(0x029C), PhysLayout.Ansi).ToString("F4"));
            Check("и полноразмерной тоже",
                  Near(Shape(Models.Find(0x029F), PhysLayout.Ansi), 3.6440, 0.01),
                  "вышло " + Shape(Models.Find(0x029F), PhysLayout.Ansi).ToString("F4"));

            // ---- приметы поколений ----
            // Правый верхний угол пустым не бывает: там ⏏, замок или датчик.
            int cornerless = 0;
            string firstCornerless = null;
            foreach (int pid in AllProducts())
            {
                AppleModel mm = Models.Find(pid);
                if (mm == null) continue;
                bool found = false;
                foreach (KeyCap k in KeyboardArt.Layout(mm, PhysLayout.Ansi))
                    if (k.X >= 13.4 && k.X <= 13.6 && k.Y < 0.6) found = true;
                if (!found)
                {
                    cornerless++;
                    if (firstCornerless == null) firstCornerless = mm.Designation;
                }
            }
            Check("правый верхний угол занят у всех", cornerless == 0,
                  "пустых " + cornerless + ", первая — " + firstCornerless);

            AppleModel a1644 = Models.Find(0x0267);      // Magic Keyboard 2015
            AppleModel a2450 = Models.Find(0x029C);      // она же 2021 года, без Touch ID
            Check("у A1644 в углу ⏏", Says(a1644, "eject"), "её там нет");
            Check("у A2450 там замок", Says(a2450, "lock"), "его там нет");
            Check("а ⏏ у неё уже нет", !Says(a2450, "eject"), "⏏ осталась");
            Check("у модели с Touch ID там датчик", Sensor(compact), "датчика нет");

            // Правый control есть только у полноразмерной.
            Check("у полноразмерной есть правый control",
                  HasVk(full, PhysLayout.Ansi, Vk.RControl), "его нет");
            Check("у компактной его нет",
                  !HasVk(compact, PhysLayout.Ansi, Vk.RControl), "он появился");

            // Клавиша контекстного меню — только у полноразмерных нового поколения.
            AppleModel a1843 = Models.Find(0x026C);
            Check("у A2520 есть клавиша меню", HasVk(full, PhysLayout.Ansi, Vk.Apps), "её нет");
            Check("у A1843 на её месте fn", !HasVk(a1843, PhysLayout.Ansi, Vk.Apps), "меню появилось");

            // ---- японская клавиатура ----
            // Она не «ANSI плюс две клавиши»: control у неё стоит там, где у прочих
            // caps lock, а caps lock — в нижнем ряду. Схема, рисовавшая их наоборот,
            // обещала человеку не ту клавишу под тем же пальцем.
            KeyCap jisCtrl = KeyAt(compact, PhysLayout.Jis, Vk.LControl);
            KeyCap jisCaps = KeyAt(compact, PhysLayout.Jis, Vk.Capital);
            Check("у JIS control в ряду с «A»", jisCtrl != null && jisCtrl.Y == 3,
                  jisCtrl == null ? "его нет вовсе" : "он в ряду " + jisCtrl.Y);
            Check("у JIS caps lock в нижнем ряду", jisCaps != null && jisCaps.Y == 5,
                  jisCaps == null ? "её нет вовсе" : "она в ряду " + jisCaps.Y);

            KeyCap ansiCaps = KeyAt(compact, PhysLayout.Ansi, Vk.Capital);
            Check("контроль: у ANSI caps lock в ряду с «A»", ansiCaps != null && ansiCaps.Y == 3,
                  ansiCaps == null ? "её нет" : "она в ряду " + ansiCaps.Y);

            Check("у компактной JIS нет правой ⌥",
                  !HasVk(compact, PhysLayout.Jis, Vk.RMenu), "она есть");
            Check("контроль: у компактной ANSI она есть",
                  HasVk(compact, PhysLayout.Ansi, Vk.RMenu), "её нет");

            KeyCap jisOne = KeyAt(compact, PhysLayout.Jis, Vk.D1);
            Check("у JIS клавиша «1» шире прочих", jisOne != null && jisOne.W == 1.25,
                  jisOne == null ? "её нет" : "ширина " + jisOne.W);

            // Вторая надпись на цифрах — та, что напечатана над цифрой.
            Check("над «1» напечатана «!»", SubOf(compact, PhysLayout.Ansi, Vk.D1) == "!",
                  "напечатано: " + SubOf(compact, PhysLayout.Ansi, Vk.D1));

            // И сам рисунок вместе с подсветкой. Живой клавиатуры на прогоне нет,
            // поэтому нажатие подаётся тем же путём, каким его подаёт перепись клавиш:
            // через обработчик окна. Проверяется то, ради чего схема и рисуется, —
            // что нажатая клавиша загорается, а потом гаснет сама.
            KeyboardView view;
            try
            {
                view = KeyboardArt.Draw(full, PhysLayout.Iso,
                                        delegate { return KeyboardArt.State.Seen; });
            }
            catch (Exception e) { Check("рисунок строится", false, Inner(e)); return; }

            Check("рисунок строится", view != null && view.Element != null, "пусто");
            Check("рисунок знает свои клавиши", view.Has(Vk.Space) && !view.Has(0),
                  "клавиши не нашлись");
            Check("до нажатия клавиша не горит", view.StateOf(Vk.Space) == KeyboardArt.State.Seen,
                  "показано " + view.StateOf(Vk.Space));

            BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo art = typeof(MainWindow).GetField("_art", f);
            MethodInfo press = typeof(MainWindow).GetMethod("ArtPress", f);
            MethodInfo fade = typeof(MainWindow).GetMethod("ArtFade", f);
            if (art == null || press == null || fade == null)
            { Check("подсветка доступна", false, "нет поля или метода"); return; }

            object hadArt = art.GetValue(_win);
            art.SetValue(_win, view);
            try
            {
                press.Invoke(_win, new object[] { Vk.Space });
                Check("нажатая клавиша загорается",
                      view.StateOf(Vk.Space) == KeyboardArt.State.Down,
                      "показано " + view.StateOf(Vk.Space));

                // Нажали другую, не отпустив первую: отпусканий перепись не присылает,
                // и без явного гашения на схеме горели бы обе — а нажата одна.
                press.Invoke(_win, new object[] { Vk.Tab });
                Check("прежняя гаснет сама",
                      view.StateOf(Vk.Space) != KeyboardArt.State.Down,
                      "горят обе");
                Check("новая загорается",
                      view.StateOf(Vk.Tab) == KeyboardArt.State.Down,
                      "показано " + view.StateOf(Vk.Tab));

                fade.Invoke(_win, null);
                Check("гаснет и последняя",
                      view.StateOf(Vk.Tab) != KeyboardArt.State.Down,
                      "клавиша осталась гореть");

                // Клавиши, которой на схеме нет, подсветка не выдумывает.
                press.Invoke(_win, new object[] { Vk.Scroll });
                Check("чужую клавишу подсветка не выдумывает",
                      view.StateOf(Vk.Scroll) != KeyboardArt.State.Down,
                      "загорелось то, чего на схеме нет");
            }
            finally { art.SetValue(_win, hadArt); }
        }

        /// <summary>
        /// Пересекаются ли две клавиши. Соприкосновение краями — не пересечение.
        ///
        /// Сравниваются составные части, а не описанные прямоугольники: Г-образный Enter
        /// у ISO вписан в прямоугольник, куда попадает и соседняя клавиша среднего ряда, —
        /// а на живой клавиатуре они не спорят, потому что Enter в этом месте уже.
        /// </summary>
        static bool Overlap(KeyCap a, KeyCap b)
        {
            foreach (KeyRect ra in a.Rects())
                foreach (KeyRect rb in b.Rects())
                    if (Hit(ra, rb)) return true;
            return false;
        }

        static bool Hit(KeyRect a, KeyRect b)
        {
            const double Eps = 0.001;
            return a.X + a.W > b.X + Eps && b.X + b.W > a.X + Eps
                && a.Y + a.H > b.Y + Eps && b.Y + b.H > a.Y + Eps;
        }

        static bool HasVk(AppleModel m, PhysLayout phys, int vk)
        {
            foreach (KeyCap k in KeyboardArt.Layout(m, phys)) if (k.Vk == vk) return true;
            return false;
        }

        static string TipOf(AppleModel m, PhysLayout phys, int vk)
        {
            foreach (KeyCap k in KeyboardArt.Layout(m, phys)) if (k.Vk == vk) return k.Tip;
            return null;
        }

        static bool Mentions(string s, string part)
        {
            return s != null && s.IndexOf(part, StringComparison.Ordinal) >= 0;
        }

        /// <summary>Сколько клавиш на схеме — то самое число, которое печатает Apple.</summary>
        static int Count(AppleModel m, PhysLayout phys)
        {
            return m == null ? -1 : KeyboardArt.Layout(m, phys).Count;
        }

        /// <summary>Есть ли на схеме клавиша с такой надписью.</summary>
        static bool Says(AppleModel m, string cap)
        {
            if (m == null) return false;
            foreach (KeyCap k in KeyboardArt.Layout(m, PhysLayout.Ansi)) if (k.Cap == cap) return true;
            return false;
        }

        /// <summary>Есть ли на схеме круглый датчик Touch ID.</summary>
        static bool Sensor(AppleModel m)
        {
            if (m == null) return false;
            foreach (KeyCap k in KeyboardArt.Layout(m, PhysLayout.Ansi)) if (k.Sensor) return true;
            return false;
        }

        /// <summary>
        /// Отношение ширины корпуса к глубине у нарисованной схемы. Берётся с самого
        /// холста: он размечен в клавишах, и его пропорции — это пропорции клавиатуры.
        /// </summary>
        static double Shape(AppleModel m, PhysLayout phys)
        {
            if (m == null) return 0;
            KeyboardView v = KeyboardArt.Draw(m, phys, null);
            var box = v.Element as System.Windows.Controls.Viewbox;
            if (box == null) return 0;
            var body = box.Child as System.Windows.Controls.Border;
            if (body == null) return 0;
            var canvas = body.Child as System.Windows.Controls.Canvas;
            if (canvas == null || canvas.Height <= 0) return 0;
            return canvas.Width / canvas.Height;
        }

        static bool Near(double a, double b, double eps) { return Math.Abs(a - b) <= eps; }

        /// <summary>Сама клавиша с таким кодом — когда важно, где она стоит и какой ширины.</summary>
        static KeyCap KeyAt(AppleModel m, PhysLayout phys, int vk)
        {
            if (m == null) return null;
            foreach (KeyCap k in KeyboardArt.Layout(m, phys)) if (k.Vk == vk) return k;
            return null;
        }

        /// <summary>Верхняя надпись клавиши — та, что напечатана над основной.</summary>
        static string SubOf(AppleModel m, PhysLayout phys, int vk)
        {
            foreach (KeyCap k in KeyboardArt.Layout(m, phys)) if (k.Vk == vk) return k.Sub;
            return null;
        }

        /// <summary>Все коды товаров из каталога — через отражение, чтобы список был один.</summary>
        static List<int> AllProducts()
        {
            FieldInfo f = typeof(Models).GetField("ByProduct",
                                                  BindingFlags.NonPublic | BindingFlags.Static);
            var map = (System.Collections.IDictionary)f.GetValue(null);
            var ids = new List<int>();
            foreach (object key in map.Keys) ids.Add((int)key);
            ids.Sort();
            return ids;
        }

        static void Page(string method)
        {
            Console.WriteLine();
            Console.WriteLine("== " + method + " ==");
            MethodInfo m = typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) { Check(method, false, "нет такого метода"); return; }

            UIElement page;
            try { page = (UIElement)m.Invoke(_win, null); }
            catch (Exception e) { Check(method, false, "страница не собралась: " + Inner(e)); return; }

            var boxes = new List<CheckBox>();
            var combos = new List<ComboBox>();
            Walk(page, boxes, combos);

            if (boxes.Count == 0 && combos.Count == 0)
            {
                Console.WriteLine("  · настраивать нечего");
                return;
            }

            foreach (CheckBox cb in boxes) Flip(cb);
            foreach (ComboBox box in combos) Turn(box);
        }

        /// <summary>
        /// Кнопки, меняющие по нескольку настроек разом. Именно там живут расхождения,
        /// которых не поймать по одному переключателю: схема правит пять полей, и любые
        /// два из них могут оказаться несовместимы.
        ///
        /// Список узкий и по подписи — нарочно. Нажать вслепую всё подряд значит нажать
        /// «Скачать и установить» и «Удалить драйвер».
        /// </summary>
        static readonly string[] SafeButtons =
        {
            "⌘ работает как Ctrl", "Как в Windows", "Без изменений",
            "Вернуть заводские для этой модели", "Настроить программу под драйвер",
            "Stable", "Dev"
        };

        static void Buttons(string page)
        {
            MethodInfo m = typeof(MainWindow).GetMethod(page, BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) return;
            UIElement built;
            try { built = (UIElement)m.Invoke(_win, null); }
            catch { return; }

            var found = new List<Button>();
            Buttons(built, found);
            Settings saved = _s.Snapshot();

            foreach (Button b in found)
            {
                string title = TitleOf(b);
                bool ours = false;
                foreach (string w in SafeButtons) if (w == title) ours = true;
                if (!ours) continue;

                Restore(saved);
                Dictionary<string, string> before = Snap();
                int applied = _applied;
                object shownBtn = Shown();
                try { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                catch (Exception e) { Check("кнопка «" + title + "»", false, "щелчок сорвался: " + Inner(e)); continue; }

                // «Ничего не изменила» — законный ответ: «Настроить программу под драйвер»
                // при отсутствующем драйвере честно говорит, что менять нечего. Незаконно
                // другое: изменить настройки и не отдать их перехвату.
                bool btnRebuilt = !ReferenceEquals(shownBtn, Shown());
                string diff = Diff(before, Snap());
                Check("кнопка «" + title + "»: изменённое дошло до перехвата",
                      diff == "" || _applied > applied, "изменила " + diff + ", а перехвату не отдала");
                string touched = Normalized();
                Check("кнопка «" + title + "»: правки Normalize видны в окне",
                      touched == "" || btnRebuilt,
                      "Normalize поправил " + touched + ", а страницу никто не пересобрал");
            }
            Restore(saved);
        }

        static void Buttons(object node, List<Button> found)
        {
            var b = node as Button;
            if (b != null) { found.Add(b); return; }
            if (!(node is DependencyObject)) return;

            var content = node as ContentControl;
            if (content != null) Buttons(content.Content, found);
            var panel = node as Panel;
            if (panel != null) foreach (UIElement c in panel.Children) Buttons(c, found);
            var border = node as Border;
            if (border != null) Buttons(border.Child, found);
        }

        static string TitleOf(Button b)
        {
            var p = b.Content as StackPanel;
            if (p != null && p.Children.Count > 0)
            {
                var t = p.Children[0] as TextBlock;
                if (t != null) return t.Text;
            }
            var one = b.Content as TextBlock;
            if (one != null) return one.Text;
            return "" + b.Content;
        }

        /// <summary>
        /// Что показано в окне прямо сейчас. Признак пересборки: BuildPage кладёт в _host
        /// новый объект, а другого способа спросить «звали ли BuildPage» у окна нет.
        /// </summary>
        static object Shown()
        {
            if (_hostCtl == null)
                _hostCtl = (ContentControl)typeof(MainWindow)
                    .GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(_win);
            return _hostCtl.Content;
        }
        static ContentControl _hostCtl;

        /// <summary>
        /// Всё, что Normalize поправит после правки, — не только своё поле.
        ///
        /// Спрашивать надо именно так. Прежняя проверка требовала, чтобы Normalize
        /// не трогал поле самого органа управления, — и врала в обе стороны: пропускала
        /// строку, сбросившую соседний OptLevel, и ругалась на законный случай, когда
        /// правило срабатывает, а страница это показывает. Договор зоны другой: правило,
        /// отменяющее выбор человека, обязано быть видно в окне.
        /// </summary>
        static string Normalized()
        {
            Dictionary<string, string> before = Snap();
            _s.Normalize();
            return Diff(before, Snap());
        }

        static void Walk(object node, List<CheckBox> boxes, List<ComboBox> combos)
        {
            var cb = node as CheckBox;
            if (cb != null) { boxes.Add(cb); return; }
            var box = node as ComboBox;
            if (box != null) { combos.Add(box); return; }

            var d = node as DependencyObject;
            if (d == null) return;

            var content = node as ContentControl;
            if (content != null) Walk(content.Content, boxes, combos);

            // Grid — тоже Panel, отдельной ветки ему не нужно: с ней каждая сетка
            // обходилась дважды, и стенд насчитывал по два одинаковых списка.
            var panel = node as Panel;
            if (panel != null) foreach (UIElement c in panel.Children) Walk(c, boxes, combos);

            var border = node as Border;
            if (border != null) Walk(border.Child, boxes, combos);

            var items = node as ItemsControl;
            if (items != null && !(node is ComboBox))
                foreach (object o in items.Items) Walk(o, boxes, combos);
        }

        static readonly FieldInfo[] Fields =
            typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>
        /// Вернуть настройки, какими они были до случая.
        ///
        /// Без этого каждый следующий переключатель проверялся бы на том, что оставил
        /// предыдущий: список «правый ⌥» показался мёртвым только потому, что до него
        /// уже сняли заменитель Fn, и выбранный пункт совпал с тем, что уже стояло.
        /// </summary>
        static void Restore(Settings from)
        {
            // Снимок снимка. Класть обратно поля исходного слепка нельзя: массивы легли бы
            // по ссылке, и живые настройки снова делили бы их со слепком. Обработчик списка
            // действий пишет в массив на месте — после первого же пункта восстанавливать
            // было нечем, а проверка «показанный пункт совпадает с настройками» для FKeys
            // сравнивала массив сам с собой и не могла провалиться никогда.
            Settings fresh = from.Snapshot();
            foreach (FieldInfo f in Fields) f.SetValue(_s, f.GetValue(fresh));
            _eng.Apply(_s);
        }

        /// <summary>Слепок всех полей настроек — по нему видно, что именно изменилось.</summary>
        static Dictionary<string, string> Snap() { return Snap(_s); }

        static Dictionary<string, string> Snap(Settings s)
        {
            var d = new Dictionary<string, string>();
            foreach (FieldInfo f in Fields)
            {
                object v = f.GetValue(s);
                var arr = v as Array;
                if (arr != null)
                {
                    var parts = new List<string>();
                    foreach (object o in arr)
                    {
                        var b = o as LayoutBinding;
                        parts.Add(b != null ? b.Lang + "=" + b.Layout : "" + o);
                    }
                    d[f.Name] = String.Join(",", parts.ToArray());
                }
                else d[f.Name] = "" + v;
            }
            return d;
        }

        static string Diff(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            var changed = new List<string>();
            foreach (KeyValuePair<string, string> kv in a)
                if (b[kv.Key] != kv.Value) changed.Add(kv.Key + ": " + kv.Value + " → " + b[kv.Key]);
            return String.Join("; ", changed.ToArray());
        }

        static string Label(object control)
        {
            var cb = control as CheckBox;
            if (cb != null)
            {
                var t = cb.Content as TextBlock;
                if (t != null) return t.Text;
                if (cb.Content != null) return "" + cb.Content;
                // У строк отдельных сочетаний подпись лежит не в Content, а рядом —
                // и продублирована для чтения с экрана. Оттуда её и берём.
                return "" + System.Windows.Automation.AutomationProperties.GetName(cb);
            }
            return null;
        }

        static void Flip(CheckBox cb)
        {
            string name = Label(cb);
            if (String.IsNullOrEmpty(name)) name = "переключатель без подписи";
            foreach (string s in Skip) if (name == s) { Console.WriteLine("  · " + name + " — пропущен нарочно"); return; }

            // Погашенный переключатель стенд щёлкает всё равно: IsEnabled этому
            // не мешает. Проверять его настройки полезно, а вот молчать о том, что
            // человек до него не дотянется, — нет: ровно за это гасят строки ряда,
            // отданного драйверу.
            if (!cb.IsEnabled)
                Console.WriteLine("  · " + name + " — погашен: щёлкает стенд, но не человек");

            bool was = cb.IsChecked == true;
            Settings saved = _s.Snapshot();
            Dictionary<string, string> before = Snap();
            int applied = _applied;
            object shown = Shown();

            try { cb.IsChecked = !was; }
            catch (Exception e) { Check(name, false, "щелчок сорвался: " + Inner(e)); Restore(saved); return; }

            string diff = Diff(before, Snap());
            bool rebuilt = !ReferenceEquals(shown, Shown());
            bool ok = diff != "" && _applied > applied;
            Check("«" + Short(name) + "» → " + (diff == "" ? "ничего не изменил" : diff),
                  ok, diff == "" ? "ни одно поле настроек не изменилось" : "настройки не применились");
            string touched = Normalized();
            Check("«" + Short(name) + "»: правки Normalize видны в окне",
                  touched == "" || rebuilt,
                  "Normalize поправил " + touched + ", а страницу никто не пересобрал");

            // Возвращаем как было: следующая проверка должна начинаться с чистого места.
            try { cb.IsChecked = was; } catch { }
            Restore(saved);
        }

        static void Turn(ComboBox box)
        {
            // Список из одного пункта — не «нечего проверять», а пропавшие пункты:
            // раньше такой список тихо не давал ни «прошло», ни «провалено».
            if (box.Items.Count < 2)
            {
                Check("список из " + box.Items.Count + " пунктов", false, "выбирать не из чего");
                return;
            }
            if (!box.IsEnabled)
                Console.WriteLine("  · список погашен: щёлкает стенд, но не человек");

            int was = box.SelectedIndex;
            var seen = new List<string>();
            bool ok = true;
            string why = "";
            bool viewOnly = (box.Tag as string) == MainWindow.ViewOnly;
            int alive = 0;

            var dead = new List<string>();
            string badItem = "";
            Settings saved = _s.Snapshot();
            for (int i = 0; i < box.Items.Count; i++)
            {
                if (i == was) continue;
                Restore(saved);              // каждый пункт — с того же места, что и остальные
                Dictionary<string, string> before = Snap();
                int applied = _applied;
                object shownItem = Shown();
                try { box.SelectedIndex = i; }
                catch (Exception e) { ok = false; why = "выбор сорвался: " + Inner(e); break; }

                bool itemRebuilt = !ReferenceEquals(shownItem, Shown());
                string diff = Diff(before, Snap());

                // Список, помеченный «показ», настроек не трогает нарочно — он меняет
                // то, что нарисовано. Мёртвым такой пункт делает не отсутствие правки
                // в настройках, а отсутствие ответа вообще: страница не пересобралась.
                if (viewOnly)
                {
                    if (!itemRebuilt) dead.Add(Text(box, i));
                    else alive++;
                    continue;
                }

                if (diff == "") { dead.Add(Text(box, i)); continue; }
                if (_applied == applied) { ok = false; why = "пункт «" + Text(box, i) + "» не применился"; break; }
                seen.Add(diff);
                string touched = Normalized();
                if (touched != "" && !itemRebuilt && badItem == "")
                    badItem = "пункт «" + Text(box, i) + "»: Normalize поправил " + touched +
                              ", а страницу никто не пересобрал";
            }
            if (ok && dead.Count > 0)
            {
                ok = false;
                why = "ничего не меняют: «" + String.Join("», «", dead.ToArray()) + "»";
            }

            if (viewOnly)
            {
                Check("список «показ»: " + alive + " пунктов меняют показанное", ok && alive > 0,
                      ok ? "ни один пункт не пересобрал страницу" : why);
                Restore(saved);
                try { box.SelectedIndex = was; } catch { }
                return;
            }

            string field = seen.Count > 0 ? Field(seen[0]) : "?";
            Check("список «" + field + "»: " + seen.Count + " пунктов меняют настройки", ok, why);
            Check("список «" + field + "»: правки Normalize видны в окне",
                  badItem == "", badItem);

            // Возвращаемся к тому пункту, который список показывал с самого начала.
            // Настройки обязаны стать теми же, что были: список показывает состояние,
            // и если от возврата к показанному что-то меняется — показывал он не то,
            // что есть, а человек получает настройку, о которой не просил.
            if (was >= 0 && box.Items.Count > 1)
            {
                Restore(saved);
                try
                {
                    box.SelectedIndex = was == 0 ? 1 : 0;   // отойти
                    box.SelectedIndex = was;                // и вернуться
                }
                catch { }
                // Про своё поле, а не про все: чтобы вернуться к показанному пункту,
                // мы проходим через соседний, а у соседнего бывают законные последствия
                // (выбрав правый ⌥ заменителем Fn, третий уровень выключают — одна
                // клавиша делает одно дело). Список отвечает за то, что показывает.
                // Спрашиваем про то поле, которым список себя и опознаёт, — первое
                // из тех, что меняет каждый его пункт.
                //
                // Дальше этого проверка не идёт нарочно. Чтобы вернуться к показанному
                // пункту, надо пройти через соседний, а у соседнего бывают законные
                // последствия для чужих полей: выбрав правый ⌥ заменителем Fn, третий
                // уровень выключают — и, вернувшись к прежнему пункту, его предлагают
                // выбрать заново, а не включают молча. Требовать от списка отменять
                // такое значит требовать от него помнить то, чего он не показывает.
                var mine = new List<string>();
                if (seen.Count > 0)
                {
                    foreach (string nm in Names(seen[0])) mine.Add(nm);
                    for (int j = 1; j < seen.Count; j++)
                    {
                        List<string> also = Names(seen[j]);
                        for (int m = mine.Count - 1; m >= 0; m--)
                            if (!also.Contains(mine[m])) mine.RemoveAt(m);
                    }
                    while (mine.Count > 1) mine.RemoveAt(1);

                    // Общего поля может не быть вовсе: у списка «Переназначения» один
                    // пункт правит выключатель, другой — паузу без клавиатуры Apple,
                    // и пересечение пусто. Пустое пересечение отсекало из сравнения
                    // всё — и самый важный список в программе был прикрыт проверкой,
                    // которая не смотрела ни на что. Тогда берём объединение: это
                    // и есть поля, за которые список отвечает.
                    if (mine.Count == 0)
                        foreach (string d in seen)
                            foreach (string nm in Names(d))
                                if (!mine.Contains(nm)) mine.Add(nm);

                    Check("список «" + field + "»: есть поле, которым он себя опознаёт",
                          mine.Count > 0,
                          "пункты не меняют ни одного поля — возврат к показанному пункту " +
                          "не проверен ничем");
                }
                string moved = Only(Diff(Snap(saved), Snap()), mine);
                Check("список «" + field + "»: показанный пункт совпадает с настройками",
                      moved == "", "возврат к «" + Text(box, was) + "» меняет " + moved);
            }
            Restore(saved);
        }

        // Пункты списка — приватный тип окна; его ToString уже возвращает подпись.
        static string Text(ComboBox box, int i) { return "" + box.Items[i]; }

        static string Field(string diff)
        {
            int colon = diff.IndexOf(':');
            return colon > 0 ? diff.Substring(0, colon) : diff;
        }

        static readonly string[] Semi = { "; " };

        /// <summary>Имена полей, перечисленных в разнице.</summary>
        static List<string> Names(string diff)
        {
            var names = new List<string>();
            if (String.IsNullOrEmpty(diff)) return names;
            foreach (string part in diff.Split(Semi, StringSplitOptions.None))
                if (Field(part) != part) names.Add(Field(part));
            return names;
        }

        /// <summary>Из разницы — только то, что касается перечисленных полей.</summary>
        static string Only(string diff, List<string> fields)
        {
            if (String.IsNullOrEmpty(diff)) return "";
            var keep = new List<string>();
            foreach (string part in diff.Split(Semi, StringSplitOptions.None))
                if (fields.Contains(Field(part))) keep.Add(part);
            return String.Join("; ", keep.ToArray());
        }

        static string Short(string s)
        {
            s = s.Replace("\n", " ");
            return s.Length <= 46 ? s : s.Substring(0, 44) + "…";
        }

        static string Inner(Exception e)
        {
            while (e.InnerException != null) e = e.InnerException;
            return e.Message;
        }
    }
}
