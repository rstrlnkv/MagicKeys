// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MagicKeys
{
    /// <summary>Прямоугольник в клавишах — единица измерения этой схемы.</summary>
    internal struct KeyRect
    {
        public double X, Y, W, H;
        public KeyRect(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
    }

    /// <summary>Одна клавиша на схеме. Размеры в клавишах, а не в точках.</summary>
    internal sealed class KeyCap
    {
        public double X, Y, W, H;
        public string Cap;      // что напечатано на клавише — теми же словами, что у Apple
        public string Sub;      // знак над словом: ⌘, ⌥, ⌃, ⇧ — так их и печатают
        public string Tip;      // объяснение под курсором: на клавише оно не помещается
        public int Vk;          // виртуальный код — по нему клавиша подсвечивается; 0 — не подсвечивается
        public bool Small;      // мелкая подпись: функциональный ряд и слова вроде «page down»

        /// <summary>
        /// Ширина нижней половины у Г-образной клавиши. Ноль — клавиша прямоугольная.
        ///
        /// Такая на клавиатуре одна: Enter у европейского и японского исполнения. Он высотой
        /// в два ряда, и нижняя его половина уже верхней — потому что в среднем ряду
        /// на одну клавишу больше. Рисовать его прямоугольником значило бы рисовать
        /// не ту клавиатуру.
        /// </summary>
        public double LowerW;

        /// <summary>Из каких прямоугольников клавиша состоит: один или, у Г-образной, два.</summary>
        public KeyRect[] Rects()
        {
            if (LowerW <= 0)
                return new KeyRect[] { new KeyRect(X, Y, W, H) };
            return new KeyRect[]
            {
                new KeyRect(X, Y, W, 1),
                new KeyRect(X + W - LowerW, Y + 1, LowerW, H - 1)
            };
        }
    }

    /// <summary>
    /// Нарисованная схема вместе с указателями на её клавиши.
    ///
    /// Перерисовывать схему целиком на каждое нажатие — под сотню фигур на каждую
    /// клавишу человека — значит заикаться при быстром наборе. Поэтому вид держит
    /// указатели и красит только то, что изменилось.
    /// </summary>
    internal sealed class KeyboardView
    {
        private sealed class Piece
        {
            public Path Shape;
            public List<TextBlock> Labels = new List<TextBlock>();
        }

        private readonly Dictionary<int, List<Piece>> _cells = new Dictionary<int, List<Piece>>();
        private readonly Dictionary<int, KeyboardArt.State> _shown = new Dictionary<int, KeyboardArt.State>();
        private KeyboardArt.Skin _skin;

        /// <summary>Готовая схема — её и кладут в карточку.</summary>
        public FrameworkElement Element;

        internal void Dress(KeyboardArt.Skin skin) { _skin = skin; }

        internal void Remember(int vk, Path shape, List<TextBlock> labels, KeyboardArt.State s)
        {
            List<Piece> list;
            if (!_cells.TryGetValue(vk, out list)) { list = new List<Piece>(); _cells[vk] = list; }
            var p = new Piece();
            p.Shape = shape;
            p.Labels = labels;
            list.Add(p);
            _shown[vk] = s;
        }

        /// <summary>Есть ли на схеме клавиша с таким кодом.</summary>
        public bool Has(int vk) { return _cells.ContainsKey(vk); }

        /// <summary>Перекрасить одну клавишу.</summary>
        public void Set(int vk, KeyboardArt.State s)
        {
            List<Piece> list;
            if (!_cells.TryGetValue(vk, out list)) return;
            _shown[vk] = s;
            foreach (Piece p in list) Paint(p, s);
        }

        /// <summary>
        /// Что сейчас показано про эту клавишу. Спрашивает стенд: подсветку нажатия
        /// иначе не проверить — живой клавиатуры на прогоне нет, а по цвету заливки
        /// судить значит проверять кисти вместо смысла.
        /// </summary>
        internal KeyboardArt.State StateOf(int vk)
        {
            KeyboardArt.State s;
            return _shown.TryGetValue(vk, out s) ? s : KeyboardArt.State.Unknown;
        }

        private void Paint(Piece p, KeyboardArt.State s)
        {
            if (_skin == null) return;
            p.Shape.Fill = s == KeyboardArt.State.Down ? _skin.Down : _skin.Key;
            p.Shape.Stroke = s == KeyboardArt.State.Unknown ? _skin.KeyEdge : _skin.Live;
            p.Shape.StrokeThickness = s == KeyboardArt.State.Unknown ? 1 : 1.8;
            for (int i = 0; i < p.Labels.Count; i++)
                p.Labels[i].Foreground = s == KeyboardArt.State.Down ? _skin.DownText
                                       : (p.Labels[i].Tag == null ? _skin.Legend : _skin.LegendDim);
        }
    }

    /// <summary>
    /// Схема клавиатуры Apple — нарисованная, а не снятая.
    ///
    /// Фотографий клавиатур Apple здесь нет и быть не может: это чужие изображения,
    /// а программа свободная. Значок программа тоже рисует сама (Iconography), и схема
    /// живёт по тому же правилу — из чисел, а не из файла.
    ///
    /// Рисуется именно клавиатура Apple, а не «клавиатура вообще»: слова на клавишах
    /// те же, что печатает Apple («return», «delete», «option»), знак модификатора стоит
    /// над словом, у полноразмерной стрелки живут в навигационном блоке, а не в нижнем
    /// ряду, у европейской и японской Enter Г-образный. Человек должен узнать свою
    /// клавиатуру с одного взгляда — иначе схема не отвечает на вопрос, ради которого
    /// нарисована: «правильно ли программа поняла, что у меня на столе».
    ///
    /// Чего в схеме нет: точных долей миллиметра. Пропорции взяты по стандартной сетке
    /// в одну клавишу, а не сняты линейкой с живой клавиатуры.
    ///
    /// Что показано, кроме самих клавиш: какие из них программа уже видела своими
    /// глазами (KeyWatch.Seen) и какая нажата прямо сейчас.
    /// </summary>
    internal static class KeyboardArt
    {
        /// <summary>Состояние клавиши на схеме.</summary>
        public enum State { Unknown, Seen, Down }

        // Ширина главного блока в клавишах: стандартный ряд «13 клавиш и двойной delete».
        private const double Main = 15;

        // Навигационный блок и цифровая клавиатура полноразмерной.
        private const double NavX = Main + 0.6;
        private const double PadX = NavX + 3.6;

        // ---------- раскладка ----------

        /// <summary>
        /// Раскладка клавиш для этой модели и этого исполнения.
        ///
        /// Исполнения отличаются по-настоящему, а не подписью: у ISO лишняя клавиша слева
        /// от «Z» и Г-образный Enter, у JIS — «^» и «¥» в верхнем ряду, «@» и «:» в буквенных,
        /// «ろ» справа от «/» и 英数 с かな по бокам пробела. От этого зависят коды, которыми
        /// клавиши приходят в Windows, а схема обещает именно их.
        /// </summary>
        public static List<KeyCap> Layout(AppleModel m, PhysLayout phys)
        {
            bool numpad = m != null && m.Numpad;
            bool iso = phys == PhysLayout.Iso;
            bool jis = phys == PhysLayout.Jis;

            var keys = new List<KeyCap>();

            FunctionRow(keys, m, numpad);
            NumberRow(keys, jis);
            TopLetterRow(keys, iso, jis);
            MiddleLetterRow(keys, iso, jis);
            BottomLetterRow(keys, iso, jis);
            SpaceRow(keys, numpad, jis);
            if (numpad) RightBlocks(keys);

            return keys;
        }

        /// <summary>
        /// Верхний ряд. У Apple он в половину высоты и идёт вровень с остальными:
        /// клавиши в нём одной ширины и заполняют ряд целиком, а не жмутся к левому краю.
        /// </summary>
        private static void FunctionRow(List<KeyCap> keys, AppleModel m, bool numpad)
        {
            const double Y = 0.42, H = 0.58;

            // Главный блок: esc и F1–F12 ровно на его ширину.
            double w = Main / 13.0;
            Add(keys, 0, Y, w, H, "esc", null, Vk.Escape, true);
            for (int i = 0; i < 12; i++)
                Add(keys, w * (i + 1), Y, w, H, "F" + (i + 1), null, Vk.F1 + i, true);

            if (!numpad)
            {
                // У компактных правее F12 либо ничего, либо датчик, либо ⏏.
                if (m != null && m.TouchId)
                    Add(keys, Main, Y, 0, H, null, null, 0, true);   // места нет — датчик ниже
                return;
            }

            // Полноразмерная: F13–F15 над навигационным блоком, F16–F19 над цифровой
            // клавиатурой — ровно так они и стоят.
            for (int i = 0; i < 3; i++)
                Add(keys, NavX + i, Y, 1, H, "F" + (13 + i), null, Vk.F13 + i, true);

            // Правый край: место под F16–F19 и, если она есть, ⏏.
            bool eject = m != null && m.Eject;
            int tail = eject ? 5 : 4;
            double tw = 4.0 / tail;
            for (int i = 0; i < 4; i++)
                Add(keys, PadX + i * tw, Y, tw, H, "F" + (16 + i), null, Vk.F13 + 3 + i, true);
            if (eject)
                Add(keys, PadX + 4 * tw, Y, tw, H, "eject", null, 0, true,
                    "eject — до Windows не доходит: её обрабатывает сама клавиатура");
        }

        /// <summary>Цифровой ряд. У JIS клавиши слева от «1» нет вовсе, зато справа «^» и «¥».</summary>
        private static void NumberRow(List<KeyCap> keys, bool jis)
        {
            const double Y = 1;
            string[] digits = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
            double x = 0;
            if (!jis) { Add(keys, x, Y, 1, 1, "`", null, Vk.Oem3, false); x = 1; }
            for (int i = 0; i < 10; i++) { Add(keys, x, Y, 1, 1, digits[i], null, Digit(i), false); x += 1; }
            Add(keys, x, Y, 1, 1, "-", null, Vk.OemMinus, false); x += 1;
            if (jis)
            {
                Add(keys, x, Y, 1, 1, "^", null, Vk.Oem7, false); x += 1;
                Add(keys, x, Y, 1, 1, "¥", null, Vk.Oem5, false); x += 1;
            }
            else { Add(keys, x, Y, 1, 1, "=", null, Vk.OemPlus, false); x += 1; }
            Add(keys, x, Y, Main - x, 1, "delete", null, Vk.Back, true,
                "delete — стирает символ слева; Windows зовёт эту клавишу Backspace");
        }

        /// <summary>Ряд с Q. У ISO и JIS отсюда начинается Г-образный Enter.</summary>
        private static void TopLetterRow(List<KeyCap> keys, bool iso, bool jis)
        {
            const double Y = 2;
            Add(keys, 0, Y, 1.5, 1, "tab", null, Vk.Tab, true);
            string[] top = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" };
            for (int i = 0; i < 10; i++) Add(keys, 1.5 + i, Y, 1, 1, top[i], null, Letter(top[i]), false);

            if (jis)
            {
                Add(keys, 11.5, Y, 1, 1, "@", null, Vk.Oem3, false);
                Add(keys, 12.5, Y, 1, 1, "[", null, Vk.Oem4, false);
            }
            else
            {
                Add(keys, 11.5, Y, 1, 1, "[", null, Vk.Oem4, false);
                Add(keys, 12.5, Y, 1, 1, "]", null, Vk.Oem6, false);
            }

            if (iso || jis)
            {
                // Г-образный: верхняя половина от 13.5 до края, нижняя — от 13.75,
                // потому что в среднем ряду на одну клавишу больше.
                KeyCap enter = Add(keys, 13.5, Y, 1.5, 2, "return", null, Vk.Return, true);
                enter.LowerW = 1.25;
            }
            else Add(keys, 13.5, Y, 1.5, 1, "\\", null, Vk.Oem5, false);
        }

        /// <summary>Ряд с A.</summary>
        private static void MiddleLetterRow(List<KeyCap> keys, bool iso, bool jis)
        {
            const double Y = 3;
            string[] mid = { "A", "S", "D", "F", "G", "H", "J", "K", "L" };
            // Caps lock одной ширины у всех исполнений — от этого и нижняя половина
            // Г-образного Enter встаёт ровно под верхнюю: в среднем ряду на одну
            // клавишу больше, и он ровно на неё уже.
            const double caps = 1.75;
            Add(keys, 0, Y, caps, 1, "caps lock", null, Vk.Capital, true);
            for (int i = 0; i < 9; i++) Add(keys, caps + i, Y, 1, 1, mid[i], null, Letter(mid[i]), false);
            double x = caps + 9;

            if (jis)
            {
                // У JIS «;» приходит кодом OemPlus, а «:» — кодом Oem1. У ANSI те же коды
                // достаются «=» и «;»: клавиши разные, коды одни и те же, и путать их нельзя.
                Add(keys, x, Y, 1, 1, ";", null, Vk.OemPlus, false); x += 1;
                Add(keys, x, Y, 1, 1, ":", null, Vk.Oem1, false); x += 1;
                Add(keys, x, Y, 1, 1, "]", null, Vk.Oem6, false);
                return;
            }

            Add(keys, x, Y, 1, 1, ";", null, Vk.Oem1, false); x += 1;
            Add(keys, x, Y, 1, 1, "'", null, Vk.Oem7, false); x += 1;
            if (iso)
            {
                // Тринадцатая клавиша ряда — Windows зовёт её Oem5, тем же кодом, что «\»
                // у ANSI. Клавиши «\» у ISO при этом нет: её место занял Г-образный Enter.
                Add(keys, x, Y, 1, 1, "#", null, Vk.Oem5, false);
            }
            else Add(keys, x, Y, Main - x, 1, "return", null, Vk.Return, true);
        }

        /// <summary>Ряд с Z. Лишняя клавиша ISO стоит слева от «Z», у JIS такая же — справа от «/».</summary>
        private static void BottomLetterRow(List<KeyCap> keys, bool iso, bool jis)
        {
            const double Y = 4;
            double x = 0;
            double shift = iso ? 1.25 : 2.25;
            Add(keys, x, Y, shift, 1, "shift", "⇧", Vk.LShift, true); x += shift;
            if (iso) { Add(keys, x, Y, 1, 1, "<", null, Vk.Oem102, false); x += 1; }
            string[] bot = { "Z", "X", "C", "V", "B", "N", "M" };
            for (int i = 0; i < 7; i++) { Add(keys, x, Y, 1, 1, bot[i], null, Letter(bot[i]), false); x += 1; }
            Add(keys, x, Y, 1, 1, ",", null, Vk.OemComma, false); x += 1;
            Add(keys, x, Y, 1, 1, ".", null, Vk.OemPeriod, false); x += 1;
            Add(keys, x, Y, 1, 1, "/", null, Vk.Oem2, false); x += 1;
            if (jis) { Add(keys, x, Y, 1, 1, "\\", null, Vk.Oem102, false); x += 1; }
            Add(keys, x, Y, Main - x, 1, "shift", "⇧", Vk.RShift, true);
        }

        /// <summary>
        /// Нижний ряд. Он и отличает компактную от полноразмерной сильнее всего: у компактной
        /// здесь же живут fn и стрелки, у полноразмерной fn ушла в навигационный блок,
        /// а стрелки стоят под ним отдельным крестом.
        /// </summary>
        private static void SpaceRow(List<KeyCap> keys, bool numpad, bool jis)
        {
            const double Y = 5;
            double x = 0;
            double arrows = numpad ? 0 : 3;

            if (!numpad)
            {
                Add(keys, x, Y, 1, 1, "fn", null, 0, true,
                    "fn — до Windows не доходит: её обрабатывает сама клавиатура. " +
                    "Её работу берёт на себя заменитель Fn"); x += 1;
                Add(keys, x, Y, 1, 1, "control", "⌃", Vk.LControl, true); x += 1;
                Add(keys, x, Y, 1, 1, "option", "⌥", Vk.LMenu, true); x += 1;
                Add(keys, x, Y, 1.25, 1, "command", "⌘", Vk.LWin, true); x += 1.25;
            }
            else
            {
                Add(keys, x, Y, 1.5, 1, "control", "⌃", Vk.LControl, true); x += 1.5;
                Add(keys, x, Y, 1.25, 1, "option", "⌥", Vk.LMenu, true); x += 1.25;
                Add(keys, x, Y, 1.5, 1, "command", "⌘", Vk.LWin, true); x += 1.5;
            }

            double right = numpad ? 2.75 : 2.25;   // правые command и option
            if (jis)
            {
                Add(keys, x, Y, 1.25, 1, "英数", null, Vk.NonConvert, true,
                    "英数 — переключение на латиницу; японской клавиатуры под рукой не было, " +
                    "код взят из общего для Windows соответствия, а не измерен"); x += 1.25;
                double sp = Main - arrows - right - 1.25 - x;
                Add(keys, x, Y, sp, 1, null, null, Vk.Space, false); x += sp;
                Add(keys, x, Y, 1.25, 1, "かな", null, Vk.Kana, true,
                    "かな — переключение на кану; код взят из общего для Windows соответствия, " +
                    "а не измерен"); x += 1.25;
            }
            else
            {
                double sp = Main - arrows - right - x;
                Add(keys, x, Y, sp, 1, null, null, Vk.Space, false); x += sp;
            }

            if (numpad)
            {
                Add(keys, x, Y, 1.5, 1, "command", "⌘", Vk.RWin, true); x += 1.5;
                Add(keys, x, Y, Main - x, 1, "option", "⌥", Vk.RMenu, true);
                return;
            }

            Add(keys, x, Y, 1.25, 1, "command", "⌘", Vk.RWin, true); x += 1.25;
            Add(keys, x, Y, Main - arrows - x, 1, "option", "⌥", Vk.RMenu, true);

            // Стрелки компактной: ← и → в полную высоту, ↑ и ↓ друг над другом по половине.
            Add(keys, Main - 3, Y, 1, 1, "←", null, Vk.Left, false);
            Add(keys, Main - 2, Y, 1, 0.5, "↑", null, Vk.Up, true);
            Add(keys, Main - 2, Y + 0.5, 1, 0.5, "↓", null, Vk.Down, true);
            Add(keys, Main - 1, Y, 1, 1, "→", null, Vk.Right, false);
        }

        /// <summary>
        /// Навигационный блок, стрелки и цифровая клавиатура — только у полноразмерных.
        ///
        /// Стрелки здесь полной высоты и стоят крестом под навигационным блоком, а не жмутся
        /// в нижний ряд: так они и стоят на живой клавиатуре. Здесь же живут две клавиши,
        /// ради которых написана отдельная карточка настроек: clear приходит в Windows как
        /// Num Lock, а «=» — кодом «очистить».
        /// </summary>
        private static void RightBlocks(List<KeyCap> keys)
        {
            Add(keys, NavX, 1, 1, 1, "fn", null, 0, true,
                "fn — до Windows не доходит: её обрабатывает сама клавиатура. " +
                "Её работу берёт на себя заменитель Fn");
            Add(keys, NavX + 1, 1, 1, 1, "home", null, Vk.Home, true);
            Add(keys, NavX + 2, 1, 1, 1, "page up", null, Vk.Prior, true);
            Add(keys, NavX, 2, 1, 1, "delete", null, Vk.Delete, true,
                "delete — стирает символ справа; Windows зовёт эту клавишу Delete");
            Add(keys, NavX + 1, 2, 1, 1, "end", null, Vk.End, true);
            Add(keys, NavX + 2, 2, 1, 1, "page down", null, Vk.Next, true);

            Add(keys, NavX + 1, 4, 1, 1, "↑", null, Vk.Up, false);
            Add(keys, NavX, 5, 1, 1, "←", null, Vk.Left, false);
            Add(keys, NavX + 1, 5, 1, 1, "↓", null, Vk.Down, false);
            Add(keys, NavX + 2, 5, 1, 1, "→", null, Vk.Right, false);

            Add(keys, PadX, 1, 1, 1, "clear", null, Vk.NumLock, true,
                "clear — приходит в Windows как Num Lock и молча выключает блок; " +
                "что она делает на самом деле, решает страница «Клавиши»");
            Add(keys, PadX + 1, 1, 1, 1, "=", null, Vk.Clear, false,
                "= — приходит кодом «очистить», которого почти никто не понимает; " +
                "программа печатает «=» сама");
            Add(keys, PadX + 2, 1, 1, 1, "/", null, 0x6F, false);
            Add(keys, PadX + 3, 1, 1, 1, "*", null, 0x6A, false);

            Add(keys, PadX, 2, 1, 1, "7", null, 0x67, false);
            Add(keys, PadX + 1, 2, 1, 1, "8", null, 0x68, false);
            Add(keys, PadX + 2, 2, 1, 1, "9", null, 0x69, false);
            Add(keys, PadX + 3, 2, 1, 1, "-", null, 0x6D, false);

            Add(keys, PadX, 3, 1, 1, "4", null, 0x64, false);
            Add(keys, PadX + 1, 3, 1, 1, "5", null, 0x65, false);
            Add(keys, PadX + 2, 3, 1, 1, "6", null, 0x66, false);
            Add(keys, PadX + 3, 3, 1, 1, "+", null, 0x6B, false);

            Add(keys, PadX, 4, 1, 1, "1", null, 0x61, false);
            Add(keys, PadX + 1, 4, 1, 1, "2", null, 0x62, false);
            Add(keys, PadX + 2, 4, 1, 1, "3", null, 0x63, false);
            Add(keys, PadX + 3, 4, 1, 2, "enter", null, Vk.Return, true);

            Add(keys, PadX, 5, 2, 1, "0", null, 0x60, false);
            Add(keys, PadX + 2, 5, 1, 1, ".", null, 0x6E, false);
        }

        private static KeyCap Add(List<KeyCap> keys, double x, double y, double w, double h,
                                  string cap, string sub, int vk, bool small)
        {
            var k = new KeyCap { X = x, Y = y, W = w, H = h, Cap = cap, Sub = sub, Vk = vk, Small = small };
            if (w > 0) keys.Add(k);
            return k;
        }

        private static KeyCap Add(List<KeyCap> keys, double x, double y, double w, double h,
                                  string cap, string sub, int vk, bool small, string tip)
        {
            KeyCap k = Add(keys, x, y, w, h, cap, sub, vk, small);
            k.Tip = tip;
            return k;
        }

        /// <summary>Код буквы — это её же код в ASCII: VK_A равен 'A'.</summary>
        private static int Letter(string s) { return s[0]; }

        /// <summary>Код цифровой клавиши верхнего ряда: «1»…«9» подряд, «0» отдельно.</summary>
        private static int Digit(int i) { return i == 9 ? Vk.D0 : Vk.D1 + i; }

        // ---------- рисование ----------

        /// <summary>
        /// Цвета клавиатуры. Их две, и обе настоящие: серебристая и «серый космос».
        /// Программа берёт ту, что не спорит с темой Windows, — но обе списаны с живого
        /// изделия, а не подогнаны под цвета окна.
        /// </summary>
        internal sealed class Skin
        {
            public Brush Body, Key, KeyEdge, Legend, LegendDim, Live, Down, DownText;

            public static Skin Pick()
            {
                var s = new Skin();
                bool dark = Theme.IsDark;
                s.Body = Solid(dark ? "#FF3B3B3D" : "#FFC9CACC");
                s.Key = Solid(dark ? "#FF212123" : "#FFFCFCFD");
                s.KeyEdge = Solid(dark ? "#FF161617" : "#FFDCDCDF");
                s.Legend = Solid(dark ? "#FFF2F2F5" : "#FF1D1D1F");
                s.LegendDim = Solid(dark ? "#FFA8A8AD" : "#FF6E6E73");
                s.Live = (Brush)Application.Current.Resources["Accent"];
                s.Down = (Brush)Application.Current.Resources["Accent"];
                s.DownText = (Brush)Application.Current.Resources["AccentText"];
                return s;
            }

            private static Brush Solid(string hex)
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                return b;
            }
        }

        /// <summary>
        /// Нарисовать схему. Возвращается Viewbox: внутри холст размечен в клавишах,
        /// снаружи он подгоняется под ширину карточки — так схема остаётся резкой
        /// при любом размере окна и не ломает вёрстку при сжатии.
        ///
        /// state отвечает, что показать про клавишу. Ответ приходит извне: сама схема
        /// не знает ни про перепись клавиш, ни про то, какую страницу показывают.
        /// </summary>
        public static KeyboardView Draw(AppleModel m, PhysLayout phys, Func<int, State> state)
        {
            const double U = 46;          // точек в одной клавише
            const double Gap = 3.4;       // зазор между клавишами
            const double Rim = 0.42;      // поля корпуса вокруг клавиш, в клавишах

            List<KeyCap> keys = Layout(m, phys);
            Skin skin = Skin.Pick();

            double left = Double.MaxValue, top = Double.MaxValue, right = 0, bottom = 0;
            foreach (KeyCap k in keys)
            {
                if (k.X < left) left = k.X;
                if (k.Y < top) top = k.Y;
                if (k.X + k.W > right) right = k.X + k.W;
                if (k.Y + k.H > bottom) bottom = k.Y + k.H;
            }
            if (left > right) { left = 0; top = 0; right = 1; bottom = 1; }

            var view = new KeyboardView();
            view.Dress(skin);

            var canvas = new Canvas
            {
                Width = (right - left + Rim * 2) * U,
                Height = (bottom - top + Rim * 2) * U
            };

            foreach (KeyCap k in keys)
            {
                State s = state == null || k.Vk == 0 ? State.Unknown : state(k.Vk);
                var labels = new List<TextBlock>();

                var shape = new Path
                {
                    Data = Outline(k, left, top, Rim, U, Gap),
                    Fill = s == State.Down ? skin.Down : skin.Key,
                    Stroke = s == State.Unknown ? skin.KeyEdge : skin.Live,
                    StrokeThickness = s == State.Unknown ? 1 : 1.8,
                    SnapsToDevicePixels = true
                };
                canvas.Children.Add(shape);

                // Надпись — отдельно от фигуры: у Г-образной клавиши она стоит по центру
                // верхней половины, а не по центру описанного прямоугольника.
                KeyRect box = k.Rects()[0];
                var stack = new StackPanel
                {
                    Width = box.W * U - Gap,
                    IsHitTestVisible = false
                };
                if (!String.IsNullOrEmpty(k.Sub))
                    stack.Children.Add(Label(labels, k.Sub, U * 0.26, s, skin, true));
                if (!String.IsNullOrEmpty(k.Cap))
                    stack.Children.Add(Label(labels, k.Cap, U * (k.Small ? 0.21 : 0.30), s, skin, false));

                stack.Measure(new Size(box.W * U - Gap, box.H * U));
                double th = stack.DesiredSize.Height;
                Canvas.SetLeft(stack, (box.X - left + Rim) * U + Gap / 2);
                Canvas.SetTop(stack, (box.Y - top + Rim) * U + (box.H * U - Gap - th) / 2 + Gap / 2);
                canvas.Children.Add(stack);

                string tip = k.Tip;
                if (String.IsNullOrEmpty(tip)) tip = k.Cap;
                if (!String.IsNullOrEmpty(tip))
                {
                    shape.ToolTip = tip;
                    stack.ToolTip = tip;
                }

                if (k.Vk != 0) view.Remember(k.Vk, shape, labels, s);
            }

            var body = new Border
            {
                Background = skin.Body,
                CornerRadius = new CornerRadius(U * 0.22),
                Child = canvas
            };

            view.Element = new Viewbox
            {
                Child = body,
                Stretch = Stretch.Uniform,
                // Растёт вместе с карточкой, но не безмерно: на широком окне схема
                // в четыре раза больше настоящей выглядела бы плакатом, а не клавиатурой.
                MaxWidth = canvas.Width * 1.35,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            return view;
        }

        private static TextBlock Label(List<TextBlock> labels, string text, double size,
                                       State s, Skin skin, bool dim)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = size * 1.15,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = s == State.Down ? skin.DownText : (dim ? skin.LegendDim : skin.Legend),
                Tag = dim ? (object)"dim" : null
            };
            labels.Add(tb);
            return tb;
        }

        /// <summary>
        /// Очертание клавиши со скруглёнными углами. У Г-образной это одна фигура,
        /// а не две положенные рядом: два прямоугольника дали бы шов посередине клавиши,
        /// которого на живой клавиатуре нет.
        /// </summary>
        private static Geometry Outline(KeyCap k, double left, double top, double rim,
                                        double u, double gap)
        {
            double r = u * 0.13;
            KeyRect[] parts = k.Rects();
            if (parts.Length == 1)
            {
                KeyRect a = parts[0];
                var rect = new Rect((a.X - left + rim) * u + gap / 2, (a.Y - top + rim) * u + gap / 2,
                                    a.W * u - gap, a.H * u - gap);
                return new RectangleGeometry(rect, r, r);
            }

            // Г-образная: верхний прямоугольник и нижний, объединённые в одну фигуру.
            KeyRect up = parts[0], low = parts[1];
            var upper = new RectangleGeometry(
                new Rect((up.X - left + rim) * u + gap / 2, (up.Y - top + rim) * u + gap / 2,
                         up.W * u - gap, up.H * u - gap / 2), r, r);
            var lower = new RectangleGeometry(
                new Rect((low.X - left + rim) * u + gap / 2, (low.Y - top + rim) * u,
                         low.W * u - gap, low.H * u - gap / 2), r, r);
            var union = new CombinedGeometry(GeometryCombineMode.Union, upper, lower);
            union.Freeze();
            return union;
        }
    }
}
