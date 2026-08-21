// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MagicKeys
{

    /// <summary>
    /// Нарисованная схема вместе с указателями на её клавиши.
    ///
    /// Перерисовывать схему целиком на каждое нажатие — восемьдесят с лишним рамок
    /// на каждую клавишу человека — значит заикаться при быстром наборе. Поэтому
    /// вид держит указатели и красит только то, что изменилось.
    /// </summary>
    internal sealed class KeyboardView
    {
        private readonly Dictionary<int, List<Border>> _cells = new Dictionary<int, List<Border>>();
        private readonly Dictionary<Border, Panel> _inside = new Dictionary<Border, Panel>();
        private readonly Dictionary<int, KeyboardArt.State> _shown = new Dictionary<int, KeyboardArt.State>();

        /// <summary>Готовая схема — её и кладут в карточку.</summary>
        public FrameworkElement Element;

        internal void Remember(int vk, Border cell, Panel inside, KeyboardArt.State s)
        {
            List<Border> list;
            if (!_cells.TryGetValue(vk, out list)) { list = new List<Border>(); _cells[vk] = list; }
            list.Add(cell);
            _inside[cell] = inside;
            _shown[vk] = s;
        }

        /// <summary>Есть ли на схеме клавиша с таким кодом.</summary>
        public bool Has(int vk) { return _cells.ContainsKey(vk); }

        /// <summary>Перекрасить одну клавишу.</summary>
        public void Set(int vk, KeyboardArt.State s)
        {
            List<Border> list;
            if (!_cells.TryGetValue(vk, out list)) return;
            _shown[vk] = s;
            foreach (Border cell in list) Paint(cell, s);
        }

        /// <summary>
        /// Что сейчас показано про эту клавишу. Спрашивает стенд: подсветку нажатия
        /// иначе не проверить — живой клавиатуры на прогоне нет, а по цвету рамки
        /// судить значит проверять кисти вместо смысла.
        /// </summary>
        internal KeyboardArt.State StateOf(int vk)
        {
            KeyboardArt.State s;
            return _shown.TryGetValue(vk, out s) ? s : KeyboardArt.State.Unknown;
        }

        private void Paint(Border cell, KeyboardArt.State s)
        {
            var stroke = (Brush)Application.Current.Resources["Stroke"];
            var strokeOn = (Brush)Application.Current.Resources["Accent"];
            var fill = (Brush)Application.Current.Resources["LayerAlt"];
            var fillOn = (Brush)Application.Current.Resources["Accent"];
            var text = (Brush)Application.Current.Resources["Text"];
            var textDim = (Brush)Application.Current.Resources["TextSec"];
            var textOn = (Brush)Application.Current.Resources["AccentText"];

            cell.BorderThickness = new Thickness(s == KeyboardArt.State.Seen ? 1.6 : 1);
            cell.BorderBrush = s == KeyboardArt.State.Unknown ? stroke : strokeOn;
            cell.Background = s == KeyboardArt.State.Down ? fillOn : fill;

            Panel inside;
            if (!_inside.TryGetValue(cell, out inside)) return;
            for (int i = 0; i < inside.Children.Count; i++)
            {
                var tb = inside.Children[i] as TextBlock;
                if (tb == null) continue;
                tb.Foreground = s == KeyboardArt.State.Down ? textOn : (i == 0 ? text : textDim);
            }
        }
    }

    /// <summary>Одна клавиша на схеме. Размеры в клавишах, а не в точках.</summary>
    internal sealed class KeyCap
    {
        public double X, Y, W, H;
        public string Cap;      // что напечатано сверху
        public string Sub;      // мелкая приписка снизу — только короткая: ⌘, ⌥, ⌃, ⇧
        public string Tip;      // объяснение под курсором: на клавише в сорок точек оно не помещается
        public int Vk;          // виртуальный код — по нему клавиша подсвечивается; 0 — не подсвечивается
        public bool Half;       // половинной высоты: функциональный ряд и стрелки
    }

    /// <summary>
    /// Схема клавиатуры — нарисованная, а не снятая.
    ///
    /// Фотографий клавиатур Apple здесь нет и быть не может: это чужие изображения,
    /// а программа свободная. Значок программа тоже рисует сама (Iconography), и схема
    /// живёт по тому же правилу — из чисел, а не из файла.
    ///
    /// Схема — именно схема: пропорции взяты стандартные, а не снятые с живой
    /// клавиатуры линейкой, и L-образный Enter у ISO нарисован прямоугольником.
    /// Она отвечает на вопрос «те ли у меня клавиши и доходят ли они до Windows»,
    /// а не на вопрос «как это выглядит на столе».
    ///
    /// Что показано, кроме самих клавиш: какие из них программа уже видела своими
    /// глазами (KeyWatch.Seen) и какая нажата прямо сейчас. Ради этого всё и затевалось —
    /// человек нажимает клавишу и видит, что она дошла.
    /// </summary>
    internal static class KeyboardArt
    {
        /// <summary>Состояние клавиши на схеме.</summary>
        public enum State { Unknown, Seen, Down }

        // Ширина главного блока в клавишах. Пятнадцать — это стандартный ряд
        // «13 клавиш и двойной ⌫»; от него пляшут все остальные ряды.
        private const double Main = 15;

        /// <summary>
        /// Раскладка клавиш для этой модели и этого исполнения.
        ///
        /// Отличий между исполнениями ровно три, и все три — настоящие: у ISO есть
        /// клавиша слева от «Z» и высокий Enter, у JIS — 英数 и かな по бокам пробела
        /// и ¥ в верхнем ряду. Всё остальное общее.
        /// </summary>
        public static List<KeyCap> Layout(AppleModel m, PhysLayout phys)
        {
            bool numpad = m != null && m.Numpad;
            bool iso = phys == PhysLayout.Iso;
            bool jis = phys == PhysLayout.Jis;

            var keys = new List<KeyCap>();

            // ---- функциональный ряд ----
            // У полноразмерных он длиннее: F13–F19 идут над навигационным блоком
            // и цифровой клавиатурой, а не втискиваются в главный.
            int fcount = numpad ? 19 : 12;
            double fw = numpad ? 1.0 : (Main - 1.5) / 12.0;
            Add(keys, 0, 0, 1.5, "esc", null, Vk.Escape, true);
            for (int i = 0; i < fcount; i++)
                Add(keys, 1.5 + i * fw, 0, fw, "F" + (i + 1), null, Vk.F1 + i, true);

            // Правый край верхнего ряда: у алюминиевых там ⏏, у моделей с датчиком —
            // Touch ID, у Magic Keyboard 2015 — ничего. Ни ⏏, ни датчик до Windows
            // не доходят, поэтому кода у них нет и подсвечиваться им нечем.
            double fend = 1.5 + fcount * fw;
            if (m != null && m.Eject)
                Add(keys, fend, 0, 1.5, "Eject", null, 0, true,
                    "Eject — до Windows не доходит: её обрабатывает сама клавиатура");
            else if (m != null && m.TouchId)
                Add(keys, fend, 0, 1.5, "Touch ID", null, 0, true,
                    "Touch ID — датчик, а не клавиша; в Windows он не работает");

            // ---- цифровой ряд ----
            //
            // У JIS он свой: клавиши слева от «1» нет вовсе, зато справа стоят «^» и «¥».
            // Это не украшение — от этого зависят коды, которыми клавиши приходят
            // в Windows, а схема обещает именно их.
            double y = 1;
            string[] digits = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
            if (jis)
            {
                for (int i = 0; i < 10; i++) Add(keys, i, y, 1, digits[i], null, Digit(i), false);
                Add(keys, 10, y, 1, "-", null, Vk.OemMinus, false);
                Add(keys, 11, y, 1, "^", null, Vk.Oem7, false);
                Add(keys, 12, y, 1, "¥", null, Vk.Oem5, false);
                Add(keys, 13, y, 2, "Delete", null, Vk.Back, false);
            }
            else
            {
                Add(keys, 0, y, 1, "`", null, Vk.Oem3, false);
                for (int i = 0; i < 10; i++) Add(keys, 1 + i, y, 1, digits[i], null, Digit(i), false);
                Add(keys, 11, y, 1, "-", null, Vk.OemMinus, false);
                Add(keys, 12, y, 1, "=", null, Vk.OemPlus, false);
                Add(keys, 13, y, 2, "Delete", null, Vk.Back, false);
            }

            // ---- верхний буквенный ряд ----
            // Высокий Enter есть и у ISO, и у JIS; у ANSI на его месте «\».
            // На живой клавиатуре он Г-образный, здесь — прямоугольник в два ряда:
            // разница видна глазу и делу не мешает.
            y = 2;
            Add(keys, 0, y, 1.5, "Tab", null, Vk.Tab, false);
            string[] top = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" };
            for (int i = 0; i < 10; i++) Add(keys, 1.5 + i, y, 1, top[i], null, Letter(top[i]), false);
            if (jis)
            {
                Add(keys, 11.5, y, 1, "@", null, Vk.Oem3, false);
                Add(keys, 12.5, y, 1, "[", null, Vk.Oem4, false);
                Add(keys, 13.5, y, 1.5, "Enter", null, Vk.Return, false).H = 2;
            }
            else
            {
                Add(keys, 11.5, y, 1, "[", null, Vk.Oem4, false);
                Add(keys, 12.5, y, 1, "]", null, Vk.Oem6, false);
                if (iso) Add(keys, 13.5, y, 1.5, "Enter", null, Vk.Return, false).H = 2;
                else Add(keys, 13.5, y, 1.5, "\\", null, Vk.Oem5, false);
            }

            // ---- средний ряд ----
            y = 3;
            string[] mid = { "A", "S", "D", "F", "G", "H", "J", "K", "L" };
            if (jis)
            {
                Add(keys, 0, y, 1.5, "Caps Lock", null, Vk.Capital, false);
                for (int i = 0; i < 9; i++) Add(keys, 1.5 + i, y, 1, mid[i], null, Letter(mid[i]), false);
                // У JIS «;» приходит кодом OemPlus, а «:» — кодом Oem1. У ANSI те же
                // коды достаются «=» и «;»: клавиши разные, коды одни и те же, и путать
                // их нельзя — от этого зависит подсветка.
                Add(keys, 10.5, y, 1, ";", null, Vk.OemPlus, false);
                Add(keys, 11.5, y, 1, ":", null, Vk.Oem1, false);
                Add(keys, 12.5, y, 1, "]", null, Vk.Oem6, false);
            }
            else
            {
                Add(keys, 0, y, 1.75, "Caps Lock", null, Vk.Capital, false);
                for (int i = 0; i < 9; i++) Add(keys, 1.75 + i, y, 1, mid[i], null, Letter(mid[i]), false);
                Add(keys, 10.75, y, 1, ";", null, Vk.Oem1, false);
                Add(keys, 11.75, y, 1, "'", null, Vk.Oem7, false);
                // У ISO слева от Enter стоит тринадцатая клавиша ряда — Windows зовёт её
                // Oem5, тем же кодом, что «\» у ANSI. Клавиши «\» у ISO при этом нет:
                // её место занял высокий Enter, так что и путаницы не выходит.
                if (iso) Add(keys, 12.75, y, 0.75, "#", null, Vk.Oem5, false);
                else Add(keys, 12.75, y, 2.25, "Enter", null, Vk.Return, false);
            }

            // ---- нижний буквенный ряд ----
            y = 4;
            double x = 0;
            Add(keys, x, y, iso ? 1.25 : 2.25, "Shift", "⇧", Vk.LShift, false);
            x += iso ? 1.25 : 2.25;
            // Лишняя клавиша ISO стоит слева от «Z», у JIS такая же — справа от «/».
            // Код у обеих один: Oem102.
            if (iso) { Add(keys, x, y, 1, "<", null, Vk.Oem102, false); x += 1; }
            string[] bot = { "Z", "X", "C", "V", "B", "N", "M" };
            for (int i = 0; i < 7; i++) { Add(keys, x, y, 1, bot[i], null, Letter(bot[i]), false); x += 1; }
            Add(keys, x, y, 1, ",", null, Vk.OemComma, false); x += 1;
            Add(keys, x, y, 1, ".", null, Vk.OemPeriod, false); x += 1;
            Add(keys, x, y, 1, "/", null, Vk.Oem2, false); x += 1;
            if (jis) { Add(keys, x, y, 1, "\\", null, Vk.Oem102, false); x += 1; }
            Add(keys, x, y, Main - x, "Shift", "⇧", Vk.RShift, false);

            // ---- нижний ряд ----
            // Fn у Magic Keyboard до Windows не доходит вовсе — её обрабатывает сама
            // клавиатура. Клавиша на схеме есть, кода у неё нет, и приписка говорит
            // об этом прямо: иначе схема обещала бы то, чего не бывает.
            y = 5;
            x = 0;
            Add(keys, x, y, 1, "fn", null, 0, false,
                "fn — до Windows не доходит: её обрабатывает сама клавиатура. " +
                "Её работу берёт на себя заменитель Fn"); x += 1;
            Add(keys, x, y, 1, "control", "⌃", Vk.LControl, false); x += 1;
            Add(keys, x, y, 1.25, "option", "⌥", Vk.LMenu, false); x += 1.25;
            Add(keys, x, y, 1.25, "command", "⌘", Vk.LWin, false); x += 1.25;

            const double Arrows = 3;
            if (jis)
            {
                // 英数 и かな: японской клавиатуры под рукой не было, коды взяты
                // из общего для Windows соответствия, а не измерены. Если они окажутся
                // другими, схема покажет клавишу непожатой — но соврать не сможет:
                // подсвечивается только то, что и правда пришло.
                Add(keys, x, y, 1, "英数", null, Vk.NonConvert, false); x += 1;
                Add(keys, x, y, 3, "", null, Vk.Space, false); x += 3;
                Add(keys, x, y, 1, "かな", null, Vk.Kana, false); x += 1;
                Add(keys, x, y, 1.25, "command", "⌘", Vk.RWin, false); x += 1.25;
                Add(keys, x, y, Main - Arrows - x, "option", "⌥", Vk.RMenu, false);
            }
            else
            {
                double space = Main - Arrows - x - 2.5;
                Add(keys, x, y, space, "", null, Vk.Space, false); x += space;
                Add(keys, x, y, 1.25, "command", "⌘", Vk.RWin, false); x += 1.25;
                Add(keys, x, y, 1.25, "option", "⌥", Vk.RMenu, false);
            }

            // Стрелки: ← и → во весь ряд, ↑ и ↓ друг над другом по половине.
            Add(keys, Main - 3, y, 1, "←", null, Vk.Left, false);
            Add(keys, Main - 2, y, 1, "↑", null, Vk.Up, true).H = 0.5;
            Add(keys, Main - 2, y + 0.5, 1, "↓", null, Vk.Down, true).H = 0.5;
            Add(keys, Main - 1, y, 1, "→", null, Vk.Right, false);

            if (numpad) AddRightBlocks(keys);
            return keys;
        }

        /// <summary>Код цифровой клавиши верхнего ряда: «1»…«9» подряд, «0» отдельно.</summary>
        private static int Digit(int i) { return i == 9 ? Vk.D0 : Vk.D1 + i; }

        /// <summary>
        /// Навигационный блок и цифровая клавиатура — только у полноразмерных.
        ///
        /// Здесь живут две клавиши, ради которых написана отдельная страница настроек:
        /// ⌧ приходит в Windows как Num Lock и молча выключает блок, а «=» приходит
        /// кодом «очистить». Приписки говорят про них правду.
        /// </summary>
        private static void AddRightBlocks(List<KeyCap> keys)
        {
            const double Nav = Main + 0.5;      // левый край навигационного блока
            const double Pad = Nav + 3.5;       // левый край цифровой клавиатуры

            Add(keys, Nav, 1, 1, "fn", null, 0, false,
                "fn — до Windows не доходит: её обрабатывает сама клавиатура");
            Add(keys, Nav + 1, 1, 1, "Home", null, Vk.Home, false);
            Add(keys, Nav + 2, 1, 1, "Page Up", null, Vk.Prior, false);
            Add(keys, Nav, 2, 1, "Delete", null, Vk.Delete, false);
            Add(keys, Nav + 1, 2, 1, "End", null, Vk.End, false);
            Add(keys, Nav + 2, 2, 1, "Page Down", null, Vk.Next, false);

            Add(keys, Pad, 1, 1, "Clear", null, Vk.NumLock, false,
                "Clear — приходит в Windows как Num Lock и молча выключает блок; " +
                "что она делает на самом деле, решает страница «Клавиши»");
            Add(keys, Pad + 1, 1, 1, "=", null, Vk.Clear, false,
                "= — приходит кодом «очистить», которого почти никто не понимает; " +
                "программа печатает «=» сама");
            Add(keys, Pad + 2, 1, 1, "/", null, 0x6F, false);
            Add(keys, Pad + 3, 1, 1, "*", null, 0x6A, false);

            Add(keys, Pad, 2, 1, "7", null, 0x67, false);
            Add(keys, Pad + 1, 2, 1, "8", null, 0x68, false);
            Add(keys, Pad + 2, 2, 1, "9", null, 0x69, false);
            Add(keys, Pad + 3, 2, 1, "-", null, 0x6D, false);

            Add(keys, Pad, 3, 1, "4", null, 0x64, false);
            Add(keys, Pad + 1, 3, 1, "5", null, 0x65, false);
            Add(keys, Pad + 2, 3, 1, "6", null, 0x66, false);
            Add(keys, Pad + 3, 3, 1, "+", null, 0x6B, false);

            Add(keys, Pad, 4, 1, "1", null, 0x61, false);
            Add(keys, Pad + 1, 4, 1, "2", null, 0x62, false);
            Add(keys, Pad + 2, 4, 1, "3", null, 0x63, false);
            Add(keys, Pad + 3, 4, 1, "Enter", null, Vk.Return, false).H = 2;

            Add(keys, Pad, 5, 2, "0", null, 0x60, false);
            Add(keys, Pad + 2, 5, 1, ".", null, 0x6E, false);
        }

        /// <summary>Та же клавиша, но с объяснением под курсором.</summary>
        private static KeyCap Add(List<KeyCap> keys, double x, double y, double w,
                                  string cap, string sub, int vk, bool half, string tip)
        {
            KeyCap k = Add(keys, x, y, w, cap, sub, vk, half);
            k.Tip = tip;
            return k;
        }

        private static KeyCap Add(List<KeyCap> keys, double x, double y, double w,
                                  string cap, string sub, int vk, bool half)
        {
            var k = new KeyCap
            {
                X = x,
                Y = half && y == 0 ? y + 0.28 : y,
                W = w,
                H = half ? 0.72 : 1,
                Cap = cap,
                Sub = sub,
                Vk = vk,
                Half = half
            };
            keys.Add(k);
            return k;
        }

        /// <summary>Код буквы — это её же код в ASCII: VK_A равен 'A'.</summary>
        private static int Letter(string s) { return s[0]; }

        /// <summary>
        /// Нарисовать схему. Ширина подгоняется сама: возвращается Viewbox, а внутри
        /// холст в клавишах — так схема остаётся резкой при любом размере окна
        /// и не ломает вёрстку при сжатии.
        ///
        /// state отвечает, что показать про клавишу. Ответ приходит извне: сама схема
        /// не знает ни про перепись клавиш, ни про то, какую страницу показывают.
        /// </summary>
        public static KeyboardView Draw(AppleModel m, PhysLayout phys, Func<int, State> state)
        {
            const double U = 40;        // точек в одной клавише
            const double Gap = 3;       // зазор между клавишами

            List<KeyCap> keys = Layout(m, phys);
            double wide = 0, tall = 0;
            foreach (KeyCap k in keys)
            {
                if (k.X + k.W > wide) wide = k.X + k.W;
                if (k.Y + k.H > tall) tall = k.Y + k.H;
            }

            var canvas = new Canvas { Width = wide * U, Height = tall * U };
            var view = new KeyboardView();
            var stroke = (Brush)Application.Current.Resources["Stroke"];
            var strokeOn = (Brush)Application.Current.Resources["Accent"];
            var fill = (Brush)Application.Current.Resources["LayerAlt"];
            var fillOn = (Brush)Application.Current.Resources["Accent"];
            var text = (Brush)Application.Current.Resources["Text"];
            var textDim = (Brush)Application.Current.Resources["TextSec"];
            var textOn = (Brush)Application.Current.Resources["AccentText"];

            foreach (KeyCap k in keys)
            {
                State s = state == null || k.Vk == 0 ? State.Unknown : state(k.Vk);

                var cell = new Border
                {
                    Width = k.W * U - Gap,
                    Height = k.H * U - Gap,
                    CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(s == State.Seen ? 1.6 : 1),
                    BorderBrush = s == State.Unknown ? stroke : strokeOn,
                    Background = s == State.Down ? fillOn : fill,
                    SnapsToDevicePixels = true
                };
                Canvas.SetLeft(cell, k.X * U + Gap / 2);
                Canvas.SetTop(cell, k.Y * U + Gap / 2);

                var inside = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                if (!String.IsNullOrEmpty(k.Cap))
                    inside.Children.Add(new TextBlock
                    {
                        Text = k.Cap,
                        FontSize = k.Cap.Length > 4 ? 9 : (k.Half ? 10 : 12),
                        Foreground = s == State.Down ? textOn : text,
                        TextAlignment = TextAlignment.Center,
                        // Переносим, а не обрезаем: «Page Down» на клавише в одну
                        // ширину не помещается никак, и «Page D…» — это не подпись.
                        TextWrapping = TextWrapping.Wrap
                    });
                if (!String.IsNullOrEmpty(k.Sub) && !k.Half)
                    inside.Children.Add(new TextBlock
                    {
                        Text = k.Sub,
                        FontSize = 8,
                        Margin = new Thickness(0, 1, 0, 0),
                        Foreground = s == State.Down ? textOn : textDim,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                cell.Child = inside;
                if (k.Vk != 0) view.Remember(k.Vk, cell, inside, s);

                // Подсказка на каждой клавише: на схеме в четыреста точек шириной
                // подпись «приходит Num Lock» не читается, а сказать это надо.
                string tip = k.Tip;
                if (String.IsNullOrEmpty(tip))
                {
                    tip = k.Cap;
                    if (!String.IsNullOrEmpty(k.Sub)) tip = tip + " — " + k.Sub;
                }
                if (!String.IsNullOrEmpty(tip)) cell.ToolTip = tip;

                canvas.Children.Add(cell);
            }

            view.Element = new Viewbox
            {
                Child = canvas,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            return view;
        }
    }
}
