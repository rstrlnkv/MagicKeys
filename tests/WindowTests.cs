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
            Check("после запуска установщика кнопка погашена",
                  btn != null && !btn.IsEnabled, "кнопка жива");

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
            int was = box.SelectedIndex;
            var seen = new List<string>();
            bool ok = true;
            string why = "";

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
