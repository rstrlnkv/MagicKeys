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
            _s.OptLevel = OptLevel.AnyOption;
            _s.MapRAlt = ModKey.RAlt;
            _s.MapLAlt = ModKey.LAlt;
            _s.MapCapsLock = ModKey.CapsLock;
            _eng.Apply(_s);

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

            // И списки той же страницы: под ролью «символы» их не касался никто,
            // а среди них — те пять строк, что решают, до какого модификатора вообще
            // можно дотянуться. Именно они молча отменяли «любой ⌥».
            foreach (ComboBox b in combos) Turn(b);

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
                try { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                catch (Exception e) { Check("кнопка «" + title + "»", false, "щелчок сорвался: " + Inner(e)); continue; }

                // «Ничего не изменила» — законный ответ: «Настроить программу под драйвер»
                // при отсутствующем драйвере честно говорит, что менять нечего. Незаконно
                // другое: изменить настройки и не отдать их перехвату.
                string diff = Diff(before, Snap());
                Check("кнопка «" + title + "»: изменённое дошло до перехвата",
                      diff == "" || _applied > applied, "изменила " + diff + ", а перехвату не отдала");
                string undone = Undone(diff);
                Check("кнопка «" + title + "»: выбранное переживает Normalize", undone == "",
                      "Normalize вернул " + undone);
                string wrong = Wrong();
                Check("кнопка «" + title + "»: настройки не разошлись", wrong == "", wrong);
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
        /// Что разошлось в настройках — или пусто, если всё сошлось. Отвечает строкой,
        /// а не отчётом: спрашивают это на каждый пункт каждого списка, и по строке
        /// на каждый ответ прогон распухал до тысяч строк, в которых ничего не видно.
        ///
        /// Достижимость, а не поле: перехват узнаёт ⌥ перебором зажатых клавиш
        /// с вопросом «во что она превращается», и третий уровень работает, когда Alt
        /// живёт хоть на Caps Lock.
        /// </summary>
        static string Wrong()
        {
            if (_s.FnSubstitute != ModKey.None && _s.OptLevel != OptLevel.Off)
            {
                // По назначению, а не по надписи: третий уровень даёт всякая клавиша,
                // приходящая в Windows как ⌥.
                ModKey t = _s.TargetOf(_s.FnSubstitute);
                if (t == ModKey.RAlt || (_s.OptLevel == OptLevel.AnyOption && t == ModKey.LAlt))
                    return "заменитель Fn (" + ModNames.Of(_s.FnSubstitute) +
                           ") сам даёт ⌥ третьего уровня: одна клавиша делает два дела";
            }
            if (_s.OptLevel != OptLevel.Off && !_s.Reaches(ModKey.RAlt))
                return "третьего уровня не набрать: правого Alt не даёт ни одна клавиша";
            if (_s.OptLevel == OptLevel.AnyOption && !_s.Reaches(ModKey.LAlt))
                return "«любой ⌥» обещан, а левого Alt не даёт ни одна клавиша";
            return "";
        }

        /// <summary>
        /// Переживает ли выбор человека Normalize. Программа применяет настройки не так,
        /// как стенд: ApplyAndSave сперва зовёт Normalize. Не переживает — значит окно
        /// показывает одно, а держит другое: галочка стоит, настройка сброшена,
        /// и ни одна надпись об этом не сказала.
        /// </summary>
        static string Undone(string own)
        {
            Dictionary<string, string> before = Snap();
            _s.Normalize();
            // Поправить соседнее поле правило вправе — на то оно и правило, и страница
            // после правки пересобирается, так что человек это увидит. Нельзя другое:
            // отменить ровно то, что человек только что выбрал.
            return Only(Diff(before, Snap()), Names(own));
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
                return "" + cb.Content;
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

            try { cb.IsChecked = !was; }
            catch (Exception e) { Check(name, false, "щелчок сорвался: " + Inner(e)); Restore(saved); return; }

            string diff = Diff(before, Snap());
            bool ok = diff != "" && _applied > applied;
            Check("«" + Short(name) + "» → " + (diff == "" ? "ничего не изменил" : diff),
                  ok, diff == "" ? "ни одно поле настроек не изменилось" : "настройки не применились");
            string undone = Undone(diff);
            Check("«" + Short(name) + "»: выбранное переживает Normalize", undone == "",
                  "Normalize вернул " + undone);
            string wrong = Wrong();
            Check("«" + Short(name) + "»: настройки не разошлись", wrong == "", wrong);

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
                try { box.SelectedIndex = i; }
                catch (Exception e) { ok = false; why = "выбор сорвался: " + Inner(e); break; }

                string diff = Diff(before, Snap());
                if (diff == "") { dead.Add(Text(box, i)); continue; }
                if (_applied == applied) { ok = false; why = "пункт «" + Text(box, i) + "» не применился"; break; }
                seen.Add(diff);
                string undone = Undone(diff);
                if (undone != "" && badItem == "")
                    badItem = "пункт «" + Text(box, i) + "»: Normalize вернул " + undone;
                string wrong = Wrong();
                if (wrong != "" && badItem == "")
                    badItem = "пункт «" + Text(box, i) + "»: " + wrong;
            }
            if (ok && dead.Count > 0)
            {
                ok = false;
                why = "ничего не меняют: «" + String.Join("», «", dead.ToArray()) + "»";
            }

            string field = seen.Count > 0 ? Field(seen[0]) : "?";
            Check("список «" + field + "»: " + seen.Count + " пунктов меняют настройки", ok, why);
            Check("список «" + field + "»: ни один пункт не разошёлся с Normalize",
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
                var mine = new List<string>();
                mine.Add(field);
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
