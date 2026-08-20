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

            Console.WriteLine();
            Console.WriteLine("прошло " + _pass + ", провалено " + _fail);
            foreach (string f in _fails) Console.WriteLine("  ПРОВАЛ: " + f);
            return _fail == 0 ? 0 : 1;
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
            "Вернуть заводские для этой модели", "Настроить программу под драйвер"
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
                Consistent("после «" + title + "»");
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
        /// То, что не должно разойтись ни от какого щелчка. Каждое правило здесь уже
        /// однажды нарушалось: одна клавиша делала два дела, третий уровень оставался
        /// на клавише, которой его не набрать, «любой ⌥» обещал левую клавишу, увёденную
        /// на другую роль.
        /// </summary>
        static void Consistent(string where)
        {
            Check(where + ": правый ⌥ не заменяет Fn и не набирает символы разом",
                  !(_s.FnSubstitute == ModKey.RAlt && _s.OptLevel != OptLevel.Off),
                  "FnSubstitute=" + _s.FnSubstitute + ", OptLevel=" + _s.OptLevel);
            Check(where + ": третьему уровню оставлен правый Alt",
                  _s.OptLevel == OptLevel.Off || _s.MapRAlt == ModKey.RAlt,
                  "MapRAlt=" + _s.MapRAlt + ", OptLevel=" + _s.OptLevel);
            Check(where + ": «любой ⌥» достижим левой клавишей",
                  _s.OptLevel != OptLevel.AnyOption || _s.MapLAlt == ModKey.LAlt,
                  "MapLAlt=" + _s.MapLAlt + ", OptLevel=" + _s.OptLevel);
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
            }
            if (ok && dead.Count > 0)
            {
                ok = false;
                why = "ничего не меняют: «" + String.Join("», «", dead.ToArray()) + "»";
            }

            string field = seen.Count > 0 ? Field(seen[0]) : "?";
            Check("список «" + field + "»: " + seen.Count + " пунктов меняют настройки", ok, why);

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
                string moved = Diff(Snap(saved), Snap());
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
