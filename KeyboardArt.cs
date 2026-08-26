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
        public bool Sensor;     // круглый датчик Touch ID вместо надписи: он и на клавиатуре круг

        /// <summary>
        /// Значок вместо надписи — тот, что напечатан на живой клавише.
        ///
        /// Рисуется геометрией, а не шрифтом. Правило дома «вместо значков слова»
        /// написано про системный шрифт: ⏏, ⌫ и ⌧ он не рисует вовсе. Своя фигура
        /// под это правило не подпадает — ею уже нарисован датчик Touch ID, и ею же
        /// рисуется значок программы. Слово при этом остаётся в подсказке: стенд
        /// проверяет надписи, и немых клавиш на схеме быть не должно.
        /// </summary>
        public string Art;

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

            /// <summary>Значки, нарисованные фигурой. Красятся вместе с надписями:
            /// иначе нажатая клавиша со стрелкой светилась бы, а стрелка на ней — нет.</summary>
            public List<Path> Glyphs = new List<Path>();
        }

        private readonly Dictionary<int, List<Piece>> _cells = new Dictionary<int, List<Piece>>();
        private readonly Dictionary<int, KeyboardArt.State> _shown = new Dictionary<int, KeyboardArt.State>();
        private KeyboardArt.Skin _skin;

        /// <summary>Готовая схема — её и кладут в карточку.</summary>
        public FrameworkElement Element;

        internal void Dress(KeyboardArt.Skin skin) { _skin = skin; }

        internal void Remember(int vk, Path shape, List<TextBlock> labels, List<Path> glyphs,
                               KeyboardArt.State s)
        {
            List<Piece> list;
            if (!_cells.TryGetValue(vk, out list)) { list = new List<Piece>(); _cells[vk] = list; }
            var p = new Piece();
            p.Shape = shape;
            p.Labels = labels;
            if (glyphs != null) p.Glyphs = glyphs;
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
            // Значок бывает залитым, бывает штриховым — красим ту кисть, которая
            // у него есть. Назначить обе значит обвести залитую фигуру контуром
            // и сделать её жирнее, чем нарисована.
            Brush ink = s == KeyboardArt.State.Down ? _skin.DownText : _skin.Legend;
            for (int i = 0; i < p.Glyphs.Count; i++)
            {
                if (p.Glyphs[i].Fill != null) p.Glyphs[i].Fill = ink;
                if (p.Glyphs[i].Stroke != null) p.Glyphs[i].Stroke = ink;
            }
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
    /// Пропорции обмерены по пикселям официальных снимков Apple — американских,
    /// британских и японских: ширина каждой клавиши, зазоры, поля и скругления корпуса.
    /// Сходится это с тем, что Apple публикует сама: отношение сторон корпуса — до одной
    /// десятой процента, число клавиш — ровно. Числа и то, откуда они, — у констант ниже.
    ///
    /// Что показано, кроме самих клавиш: какие из них программа уже видела своими
    /// глазами (KeyWatch.Seen) и какая нажата прямо сейчас.
    /// </summary>
    internal static class KeyboardArt
    {
        /// <summary>Состояние клавиши на схеме.</summary>
        public enum State { Unknown, Seen, Down }

        /// <summary>
        /// Ширина главного блока в клавишах. Не пятнадцать, как считалось: обмер
        /// официальных снимков Apple по пикселям даёт 14,5 — и у компактной, и у
        /// полноразмерной. Складывается она одинаково во всех рядах: 1,5 + 12 + 1
        /// в верхнем, 13 + 1,5 в цифровом, 1,75 + 12 + 0,75 у ISO.
        /// </summary>
        private const double Main = 14.5;

        // Навигационный блок и цифровая клавиатура полноразмерной. Зазор между
        // блоками — вдвое шире обычного; замерено на двух снимках, сходится
        // до сотых: 14,683 и 14,692; 17,860 и 17,873.
        private const double NavX = 14.69;
        private const double PadX = 17.87;

        /// <summary>
        /// Высота ряда в долях ширины клавиши: клавиатура чуть сплюснута по вертикали,
        /// и квадратная сетка её выдаёт. Замерено на четырёх снимках, сходится:
        /// 63,4/65, 168,8/173,5, 124,2/128, 42,2/43,4.
        /// </summary>
        private const double RowH = 0.972;

        /// <summary>
        /// Зазор между клавишами в долях клавиши — просвет корпуса, который между ними видно.
        ///
        /// Мерить его по светлой середине клавиши нельзя: у клавиши есть тёмный кант,
        /// и порог «белое» отрезает его с обеих сторон, отчего зазор выходит в полтора
        /// раза шире настоящего. Замерено по самому просвету — 11/104,2 на снимке
        /// с чёрными клавишами и 17/153,6 на снимке с белыми, — и обе цифры сошлись.
        /// </summary>
        private const double GapU = 0.108;

        /// <summary>
        /// Поля корпуса вокруг клавиш — за вычетом половины зазора.
        ///
        /// Замерено от края корпуса до края клавиши: 0,144 сбоку, 0,185 сверху, 0,152 снизу.
        /// Здесь они на ползазора меньше, потому что рисунок сам отступает на ползазора
        /// с каждой стороны клавиши. Записать сюда измеренное значит прибавить эти
        /// ползазора дважды — и клавиатура выйдет на процент приземистее настоящей.
        /// Проверяется отношением сторон: оно обязано совпасть с опубликованным
        /// у Apple, 2,4282 у компактной и 3,6440 у полноразмерной.
        /// </summary>
        private const double RimX = 0.090, RimTop = 0.130, RimBottom = 0.095;

        /// <summary>
        /// Скругление корпуса — примета поколения, а не общая величина.
        ///
        /// Замерено: у A1843 угол выходит на прямую через 27 точек при шаге клавиш
        /// 107,4 — это 0,25 клавиши. У A2450, A2520, A3118 и A3119 — от 0,445 до 0,513,
        /// вдвое круглее. Старые клавиатуры заметно прямее, и одинаковое скругление
        /// на всех выдаёт рисунок с одного взгляда.
        ///
        /// Алюминиевой серии снимка сверху не нашлось; ей дан тот же угол, что и её
        /// ближайшей родне, — она уж точно не круглее Magic Keyboard 2015 года.
        /// </summary>
        private static double BodyRound(AppleModel m)
        {
            if (m == null) return 0.48;
            return m.Gen == AppleGen.Magic2021 || m.Gen == AppleGen.Magic2024 ? 0.48 : 0.25;
        }

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
            SpaceRow(keys, m, numpad, jis);
            if (numpad) RightBlocks(keys, m);

            return keys;
        }

        /// <summary>
        /// Есть ли у этой клавиатуры такая клавиша.
        ///
        /// Спрашивают настройки: предлагать правый ⌥ там, где его нет, — обещать то,
        /// чего не нажать, а с завода заменителем Fn стоит именно он, и на японской
        /// компактной навигация Fn+стрелки молча не работала. Ответ берётся из той же
        /// схемы, что и рисуется: другого списка клавиш в программе нет и заводить
        /// второй нельзя — разойдутся.
        ///
        /// Клавиатура не узнана — отвечаем «есть». Спрятать настройку из-за незнания
        /// хуже, чем показать лишнюю: первое человек не найдёт вовсе.
        /// </summary>
        public static bool HasKey(AppleModel m, PhysLayout phys, int vk)
        {
            if (m == null) return true;
            foreach (KeyCap k in Layout(m, phys)) if (k.Vk == vk) return true;
            return false;
        }

        /// <summary>
        /// Верхний ряд: esc, двенадцать F и одна клавиша в правом верхнем углу.
        ///
        /// У Magic Keyboard он такой же высоты, как остальные, — это замерено: esc
        /// и control на снимке одного размера. Половинный ряд остался у алюминиевой
        /// серии, где он и был: её снимков сверху под рукой не было.
        /// </summary>
        private static void FunctionRow(List<KeyCap> keys, AppleModel m, bool numpad)
        {
            bool half = m != null && m.Gen == AppleGen.Alu;
            double y = half ? 0.42 : 0;
            double h = half ? 0.58 : 1;

            Add(keys, 0, y, 1.5, h, "esc", null, Vk.Escape, true);

            // На F1–F12 у Apple стоит значок, а номер под ним мелко. Голое «F1»
            // она не печатает нигде, и без значков верхний ряд выглядит чужим,
            // сколько бы ни были верны пропорции.
            string[] art = Models.FKeyArt(m == null ? AppleGen.Unknown : m.Gen);
            string[] says = Models.Legend(m == null ? AppleGen.Unknown : m.Gen);
            for (int i = 0; i < 12; i++)
            {
                KeyCap f = Add(keys, 1.5 + i, y, 1, h, "F" + (i + 1), null, Vk.F1 + i, true,
                               String.IsNullOrEmpty(says[i]) ? null : "F" + (i + 1) + " — " + says[i]);
                // У половинного ряда алюминиевой значку места нет: клавиша вдвое ниже,
                // и значок со словом на ней встают друг на друга.
                if (!half) f.Art = art[i];
            }
            TopRight(keys, m, 13.5, y, h);

            if (!numpad) return;

            // Полноразмерная: F13–F15 над навигационным блоком, F16–F19 над цифровой
            // клавиатурой — ровно так они и стоят, и все семь обычной ширины.
            for (int i = 0; i < 3; i++)
                Add(keys, NavX + i, y, 1, h, "F" + (13 + i), null, Vk.F13 + i, true);
            for (int i = 0; i < 4; i++)
                Add(keys, PadX + i, y, 1, h, "F" + (16 + i), null, Vk.F13 + 3 + i, true);
        }

        /// <summary>
        /// Правый верхний угол главного блока. Пустым он не бывает ни у одной модели,
        /// и то, что там стоит, — примета поколения: ⏏ до 2021 года, замок у моделей
        /// без Touch ID, датчик у моделей с ним. Замерено по снимкам Apple: у A1644
        /// там ⏏ — прежний комментарий обещал пустоту, — а у A2450 и A3203 замок.
        /// </summary>
        private static void TopRight(List<KeyCap> keys, AppleModel m, double x, double y, double h)
        {
            if (m == null) return;

            if (m.TouchId)
            {
                KeyCap k = Add(keys, x, y, 1, h, null, null, 0, true,
                    "Touch ID — датчик отпечатка, а не клавиша. В Windows он не работает");
                k.Sensor = true;
                return;
            }
            if (m.Lock)
            {
                Add(keys, x, y, 1, h, "lock", null, 0, true,
                    "Замок — на Mac запирает экран. Windows такой клавиши не знает, " +
                    "и программа её не перехватывает");
                return;
            }
            if (m.Eject)
                // Значок ⏏ рисуется фигурой, а слово остаётся в подсказке.
                // Своего кода клавиши у неё нет, и обычным путём она не приходит.
                // Ловить её сырым вводом программа умеет — под это написана карточка
                // на странице «Клавиши», — но дошла ли она С ЭТОЙ клавиатуры, знает
                // только сама карточка: она спрашивает KeyWatch.EjectSeen. Обещать
                // здесь и то, и другое одинаково неверно, поэтому отсылаем к тому,
                // кто знает ответ.
            {
                KeyCap e = Add(keys, x, y, 1, h, null, null, 0, true,
                    "eject — обычной клавишей в Windows не приходит. Программа умеет ловить " +
                    "её сырым вводом; дошла ли она с вашей клавиатуры, скажет карточка " +
                    "«Eject» на странице «Клавиши»");
                e.Art = "eject";
            }
        }

        /// <summary>
        /// Цифровой ряд. У JIS клавиши слева от «1» нет вовсе, зато справа «^» и «¥».
        ///
        /// Здесь же начинается вторая надпись: на живой клавиатуре над цифрой напечатан
        /// её верхний знак, и без него ряд выглядит не клавиатурой Apple, а сеткой цифр.
        /// </summary>
        private static void NumberRow(List<KeyCap> keys, bool jis)
        {
            const double Y = 1;
            string[] digits = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
            string[] over = { "!", "@", "#", "$", "%", "^", "&", "*", "(", ")" };
            double x = 0;
            if (!jis) { Add(keys, x, Y, 1, 1, "`", "~", Vk.Oem3, false); x = 1; }

            // У JIS клавиши слева от «1» нет, и ряд начинается с самой «1» — она за это
            // шире прочих на четверть, а delete на столько же уже. Замерено; прежде схема
            // давала обеим ширину соседей и уводила весь ряд влево на четверть клавиши.
            double one = jis ? 1.25 : 1;
            Add(keys, x, Y, one, 1, digits[0], over[0], Digit(0), false); x += one;
            for (int i = 1; i < 10; i++) { Add(keys, x, Y, 1, 1, digits[i], over[i], Digit(i), false); x += 1; }
            Add(keys, x, Y, 1, 1, "-", "_", Vk.OemMinus, false); x += 1;
            if (jis)
            {
                Add(keys, x, Y, 1, 1, "^", "~", Vk.Oem7, false); x += 1;
                Add(keys, x, Y, 1, 1, "¥", "|", Vk.Oem5, false); x += 1;
            }
            else { Add(keys, x, Y, 1, 1, "=", "+", Vk.OemPlus, false); x += 1; }
            Add(keys, x, Y, Main - x, 1, "delete", null, Vk.Back, true,
                "delete — стирает символ слева; Windows зовёт эту клавишу Backspace");
        }

        /// <summary>Ряд с Q. У ISO и JIS отсюда начинается Г-образный Enter.</summary>
        private static void TopLetterRow(List<KeyCap> keys, bool iso, bool jis)
        {
            const double Y = 2;

            // У JIS tab шириной в обычную клавишу, а не в полторы: ряду надо вместить
            // на одну клавишу больше, и место взято отсюда. Замерено.
            double tab = jis ? 1 : 1.5;
            Add(keys, 0, Y, tab, 1, "tab", null, Vk.Tab, true);
            string[] top = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" };
            for (int i = 0; i < 10; i++) Add(keys, tab + i, Y, 1, 1, top[i], null, Letter(top[i]), false);
            double x = tab + 10;

            if (jis)
            {
                Add(keys, x, Y, 1, 1, "@", "`", Vk.Oem3, false); x += 1;
                Add(keys, x, Y, 1, 1, "[", "{", Vk.Oem4, false); x += 1;

                // Г-образный Enter японской: верхняя половина шире, чем у европейской,
                // и начинается раньше — от 13, а не от 13,5.
                KeyCap jisEnter = Add(keys, x, Y, Main - x, 2, "return", null, Vk.Return, true);
                jisEnter.LowerW = 1.25;
                return;
            }

            Add(keys, x, Y, 1, 1, "[", "{", Vk.Oem4, false); x += 1;
            Add(keys, x, Y, 1, 1, "]", "}", Vk.Oem6, false); x += 1;

            if (iso)
            {
                // Г-образный: верхняя половина от 13.5 до края, нижняя — от 13.75,
                // потому что в среднем ряду на одну клавишу больше.
                KeyCap enter = Add(keys, x, Y, Main - x, 2, "return", null, Vk.Return, true);
                enter.LowerW = 0.75;
            }
            else Add(keys, x, Y, 1, 1, "\\", "|", Vk.Oem5, false);
        }

        /// <summary>Ряд с A.</summary>
        private static void MiddleLetterRow(List<KeyCap> keys, bool iso, bool jis)
        {
            const double Y = 3;
            string[] mid = { "A", "S", "D", "F", "G", "H", "J", "K", "L" };

            // Слева у японской клавиатуры не caps lock, а control: caps lock у неё
            // в нижнем ряду. Это не подпись, а другая клавиша с другим кодом — и она
            // ещё и уже. Замерено по снимкам с японского сайта Apple.
            double head = jis ? 1.25 : 1.75;
            if (jis)
                Add(keys, 0, Y, head, 1, "control", "⌃", Vk.LControl, true,
                    "У японской клавиатуры control стоит здесь — там, где у прочих caps lock. " +
                    "Сама caps lock ушла в нижний ряд");
            else
                Add(keys, 0, Y, head, 1, "caps lock", null, Vk.Capital, true);

            for (int i = 0; i < 9; i++) Add(keys, head + i, Y, 1, 1, mid[i], null, Letter(mid[i]), false);
            double x = head + 9;

            if (jis)
            {
                // У JIS «;» приходит кодом OemPlus, а «:» — кодом Oem1. У ANSI те же коды
                // достаются «=» и «;»: клавиши разные, коды одни и те же, и путать их нельзя.
                Add(keys, x, Y, 1, 1, ";", "+", Vk.OemPlus, false); x += 1;
                Add(keys, x, Y, 1, 1, ":", "*", Vk.Oem1, false); x += 1;
                Add(keys, x, Y, 1, 1, "]", "}", Vk.Oem6, false);
                return;
            }

            Add(keys, x, Y, 1, 1, ";", ":", Vk.Oem1, false); x += 1;
            Add(keys, x, Y, 1, 1, "'", "\"", Vk.Oem7, false); x += 1;
            if (iso)
            {
                // Тринадцатая клавиша ряда — Windows зовёт её Oem5, тем же кодом, что «\»
                // у ANSI. Клавиши «\» у ISO при этом нет: её место занял Г-образный Enter.
                // Что на ней напечатано, зависит от языка: «#» на британской, «*» на
                // немецкой, «ё» на русской. Схема показывает исполнение, а не язык.
                Add(keys, x, Y, 1, 1, "#", "~", Vk.Oem5, false,
                    "Тринадцатая клавиша среднего ряда — она есть только у ISO. " +
                    "Какой знак на ней напечатан, зависит от языка клавиатуры");
            }
            else Add(keys, x, Y, Main - x, 1, "return", null, Vk.Return, true);
        }

        /// <summary>Ряд с Z. Лишняя клавиша ISO стоит слева от «Z», у JIS такая же — справа от «/».</summary>
        private static void BottomLetterRow(List<KeyCap> keys, bool iso, bool jis)
        {
            const double Y = 4;
            double x = 0;
            // У JIS обе клавиши ⇧ одной ширины — 1,75; лишняя клавиша ряда стоит справа,
            // а не слева, и место под неё берётся у правой. Замерено.
            double shift = jis ? 1.75 : (iso ? 1.25 : 2.25);
            Add(keys, x, Y, shift, 1, "shift", "⇧", Vk.LShift, true); x += shift;
            if (iso)
                Add(keys, x++, Y, 1, 1, "<", ">", Vk.Oem102, false,
                    "Лишняя клавиша ISO — она стоит слева от «Z». Какой знак на ней " +
                    "напечатан, зависит от языка клавиатуры");
            string[] bot = { "Z", "X", "C", "V", "B", "N", "M" };
            for (int i = 0; i < 7; i++) { Add(keys, x, Y, 1, 1, bot[i], null, Letter(bot[i]), false); x += 1; }
            Add(keys, x, Y, 1, 1, ",", "<", Vk.OemComma, false); x += 1;
            Add(keys, x, Y, 1, 1, ".", ">", Vk.OemPeriod, false); x += 1;
            Add(keys, x, Y, 1, 1, "/", "?", Vk.Oem2, false); x += 1;
            if (jis) { Add(keys, x, Y, 1, 1, "\\", "_", Vk.Oem102, false); x += 1; }
            Add(keys, x, Y, Main - x, 1, "shift", "⇧", Vk.RShift, true);
        }

        /// <summary>
        /// Нижний ряд. Он и отличает компактную от полноразмерной сильнее всего: у компактной
        /// здесь же живут fn и стрелки, у полноразмерной fn ушла в навигационный блок,
        /// а стрелки стоят под ним отдельным крестом.
        /// </summary>
        private static void SpaceRow(List<KeyCap> keys, AppleModel m, bool numpad, bool jis)
        {
            if (jis) { JisSpaceRow(keys, m, numpad); return; }

            const double Y = 5;
            double x = 0;
            double arrows = numpad ? 0 : 3;

            // fn слева есть у всех, кроме полноразмерных до 2021 года: у тех она ушла
            // в навигационный блок, а нижний ряд начинается прямо с control. Замерено
            // на снимках A1843 и A2520 — у первой слева три клавиши, у второй четыре.
            bool leftFn = !numpad || (m != null && m.Globe);
            if (leftFn)
            {
                Add(keys, x, Y, 1, 1, "fn", null, 0, true, FnTip(m)); x += 1;
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

            // Справа у полноразмерной на одну клавишу больше: там есть правый control,
            // которого у компактной нет вовсе.
            double right = numpad ? 4.25 : 2.25;
            {
                double sp = Main - arrows - right - x;
                Add(keys, x, Y, sp, 1, null, null, Vk.Space, false); x += sp;
            }

            if (numpad)
            {
                Add(keys, x, Y, 1.5, 1, "command", "⌘", Vk.RWin, true); x += 1.5;
                Add(keys, x, Y, 1.25, 1, "option", "⌥", Vk.RMenu, true); x += 1.25;
                Add(keys, x, Y, Main - x, 1, "control", "⌃", Vk.RControl, true);
                return;
            }

            Add(keys, x, Y, 1.25, 1, "command", "⌘", Vk.RWin, true); x += 1.25;
            Add(keys, x, Y, Main - arrows - x, 1, "option", "⌥", Vk.RMenu, true);
            Arrows(keys, Y);
        }

        /// <summary>
        /// Нижний ряд японской клавиатуры. Он не «тот же с двумя добавленными клавишами»,
        /// как здесь считалось, а другой: caps lock стоит слева внизу, control в нижнем
        /// ряду нет вовсе — он ушёл в ряд с «A», — пробел вдвое короче, а у компактной
        /// справа от command стоит fn с глобусом, и правой ⌥ у неё нет.
        ///
        /// Замерено по снимкам сверху с японского сайта Apple, компактной и полноразмерной.
        /// </summary>
        private static void JisSpaceRow(List<KeyCap> keys, AppleModel m, bool numpad)
        {
            const double Y = 5;
            double x = 0;

            Add(keys, x, Y, 1, 1, "caps lock", null, Vk.Capital, true); x += 1;
            Add(keys, x, Y, 1, 1, "option", "⌥", Vk.LMenu, true); x += 1;
            Add(keys, x, Y, 1.25, 1, "command", "⌘", Vk.LWin, true); x += 1.25;
            Add(keys, x, Y, 1.25, 1, "英数", null, Vk.NonConvert, true,
                "英数 — переключение на латиницу; японской клавиатуры под рукой не было, " +
                "код взят из общего для Windows соответствия, а не измерен"); x += 1.25;
            Add(keys, x, Y, 3.5, 1, null, null, Vk.Space, false); x += 3.5;
            Add(keys, x, Y, 1.25, 1, "かな", null, Vk.Kana, true,
                "かな — переключение на кану; код взят из общего для Windows соответствия, " +
                "а не измерен"); x += 1.25;
            Add(keys, x, Y, 1.25, 1, "command", "⌘", Vk.RWin, true); x += 1.25;

            if (numpad)
            {
                Add(keys, x, Y, 1.5, 1, "option", "⌥", Vk.RMenu, true); x += 1.5;
                Add(keys, x, Y, 1.5, 1, "control", "⌃", Vk.RControl, true); x += 1.5;
                Add(keys, x, Y, Main - x, 1, "fn", null, 0, true, FnTip(m));
                return;
            }

            Add(keys, x, Y, Main - 3 - x, 1, "fn", null, 0, true, FnTip(m));
            Arrows(keys, Y);
        }

        /// <summary>Стрелки компактной: ← и → в полную высоту, ↑ и ↓ друг над другом по половине.</summary>
        private static void Arrows(List<KeyCap> keys, double y)
        {
            Arrow(keys, Main - 3, y, 1, 1, "left", Vk.Left);
            Arrow(keys, Main - 2, y, 1, 0.5, "up", Vk.Up);
            Arrow(keys, Main - 2, y + 0.5, 1, 0.5, "down", Vk.Down);
            Arrow(keys, Main - 1, y, 1, 1, "right", Vk.Right);
        }

        /// <summary>
        /// Клавиша со стрелкой. Надписи у неё нет: Apple печатает сплошной треугольник,
        /// и он рисуется фигурой. Слово остаётся подсказкой — по ней клавишу узнаёт
        /// и человек под курсором, и стенд, который проверяет надписи.
        /// </summary>
        private static void Arrow(List<KeyCap> keys, double x, double y, double w, double h,
                                  string art, int vk)
        {
            string[] names = { "left", "влево", "right", "вправо", "up", "вверх", "down", "вниз" };
            string word = art;
            for (int i = 0; i < names.Length; i += 2) if (names[i] == art) word = names[i + 1];

            KeyCap k = Add(keys, x, y, w, h, null, null, vk, true, "Стрелка " + word);
            k.Art = art;
        }

        /// <summary>Подсказка клавиши fn — она одна на все места, где эта клавиша стоит.</summary>
        private static string FnTip(AppleModel m)
        {
            return (m != null && m.Globe ? "fn с глобусом — " : "fn — ") +
                   "до Windows не доходит: её обрабатывает сама клавиатура. " +
                   "Её работу берёт на себя заменитель Fn";
        }

        /// <summary>
        /// Навигационный блок, стрелки и цифровая клавиатура — только у полноразмерных.
        ///
        /// Стрелки здесь полной высоты и стоят крестом под навигационным блоком, а не жмутся
        /// в нижний ряд: так они и стоят на живой клавиатуре. Здесь же живут две клавиши,
        /// ради которых написана отдельная карточка настроек: clear приходит в Windows как
        /// Num Lock, а «=» — кодом «очистить».
        /// </summary>
        private static void RightBlocks(List<KeyCap> keys, AppleModel m)
        {
            // Над клавишей ⌦ у полноразмерных до 2021 года стоит fn. У новых её место
            // занято клавишей контекстного меню — той, что на клавиатуре Windows зовётся
            // «меню»: fn у них переехала в нижний ряд. Замерено по снимкам A1843 и A2520.
            if (m != null && m.ContextMenu)
                Add(keys, NavX, 1, 1, 1, "menu", null, Vk.Apps, true,
                    "Клавиша контекстного меню — то же, что правая кнопка мыши. " +
                    "Код взят из общего для Windows соответствия, а не измерен");
            else
                Add(keys, NavX, 1, 1, 1, "fn", null, 0, true, FnTip(m));
            Add(keys, NavX + 1, 1, 1, 1, "home", null, Vk.Home, true);
            Add(keys, NavX + 2, 1, 1, 1, "page up", null, Vk.Prior, true);
            Add(keys, NavX, 2, 1, 1, "delete", null, Vk.Delete, true,
                "delete — стирает символ справа; Windows зовёт эту клавишу Delete");
            Add(keys, NavX + 1, 2, 1, 1, "end", null, Vk.End, true);
            Add(keys, NavX + 2, 2, 1, 1, "page down", null, Vk.Next, true);

            Arrow(keys, NavX + 1, 4, 1, 1, "up", Vk.Up);
            Arrow(keys, NavX, 5, 1, 1, "left", Vk.Left);
            Arrow(keys, NavX + 1, 5, 1, 1, "down", Vk.Down);
            Arrow(keys, NavX + 2, 5, 1, 1, "right", Vk.Right);

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
            const double U = 46;           // точек в ширине одной клавиши
            double uy = U * RowH;          // и в высоте ряда: она меньше, так замерено
            double gap = U * GapU;         // зазор между клавишами — одинаковый по обеим осям

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
                Width = (right - left + RimX * 2) * U,
                Height = (bottom - top) * uy + (RimTop + RimBottom) * uy
            };

            foreach (KeyCap k in keys)
            {
                State s = state == null || k.Vk == 0 ? State.Unknown : state(k.Vk);
                var labels = new List<TextBlock>();

                var shape = new Path
                {
                    Data = Outline(k, left, top, right, bottom, U, uy, gap, BodyRound(m)),
                    Fill = s == State.Down ? skin.Down : skin.Key,
                    Stroke = s == State.Unknown ? skin.KeyEdge : skin.Live,
                    StrokeThickness = s == State.Unknown ? 1 : 1.8,
                    SnapsToDevicePixels = true
                };
                canvas.Children.Add(shape);

                // Надпись — отдельно от фигуры. У Г-образной клавиши она живёт в НИЖНЕЙ
                // половине: на живой клавиатуре подпись Enter стоит внизу справа, а не
                // посреди верхней половины, где её рисовали раньше.
                KeyRect[] boxes = k.Rects();
                KeyRect box = boxes[boxes.Length - 1];
                double bx = (box.X - left + RimX) * U + gap / 2;
                double by = (box.Y - top + RimTop) * uy + gap / 2;
                double bw = box.W * U - gap, bh = box.H * uy - gap;

                // Датчик Touch ID — круг, а не надпись: он и на клавиатуре круг.
                // Доля клавиши снята с того же снимка, что и всё остальное.
                if (k.Sensor)
                {
                    double d = Math.Min(bw, bh) * 0.62;
                    var dot = new Ellipse
                    {
                        Width = d,
                        Height = d,
                        Stroke = skin.LegendDim,
                        StrokeThickness = 1,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(dot, bx + (bw - d) / 2);
                    Canvas.SetTop(dot, by + (bh - d) / 2);
                    canvas.Children.Add(dot);
                }

                // Слово на широкой клавише прижато к её наружному нижнему углу, а не
                // стоит по центру: «caps lock» — влево, «return» — вправо. Замерено
                // по снимку: у «caps lock» слева 17 точек, справа 87; у «return»
                // наоборот. Буквы и цифры при этом стоят ровно по центру.
                bool word = k.Small && k.W > 1.001;
                bool toRight = word && k.X + k.W / 2 > (left + right) / 2;
                // Значок и надпись на одной клавише — это верхний ряд: значок сверху,
                // номер под ним. По центру их ставить нельзя, они встают друг на друга.
                bool underArt = !String.IsNullOrEmpty(k.Art) && !String.IsNullOrEmpty(k.Cap);
                double pad = word ? U * 0.13 : 0;
                TextAlignment side = !word ? TextAlignment.Center
                                   : (toRight ? TextAlignment.Right : TextAlignment.Left);

                // Значок, нарисованный фигурой, — на тех клавишах, где Apple печатает
                // значок, а не слово: ⏏ и стрелки. Слово при этом живёт в подсказке.
                List<Path> glyphs = KeyboardGlyphs.Draw(k.Art, bx, by, bw, bh,
                                                        s == State.Down ? skin.DownText : skin.Legend,
                                                        !String.IsNullOrEmpty(k.Cap));
                foreach (Path art in glyphs) canvas.Children.Add(art);

                var stack = new StackPanel
                {
                    Width = bw - pad,
                    IsHitTestVisible = false
                };
                if (!String.IsNullOrEmpty(k.Sub))
                    stack.Children.Add(Label(labels, k.Sub, U * 0.26, s, skin, true, side));
                if (!String.IsNullOrEmpty(k.Cap))
                    stack.Children.Add(Label(labels, k.Cap, U * (k.Small ? 0.21 : 0.30), s, skin, false, side));

                stack.Measure(new Size(bw - pad, bh));
                double th = stack.DesiredSize.Height;
                Canvas.SetLeft(stack, bx + (word && !toRight ? pad : 0));
                // Прижимаем ко дну, но не выше середины: в половинном ряду алюминиевой
                // серии слово выше клавиши, и «прижать ко дну» вытолкнуло бы его наружу.
                Canvas.SetTop(stack, underArt ? by + bh - th - U * 0.09
                                    : word ? by + Math.Max((bh - th) / 2, bh - th - pad)
                                           : by + (bh - th) / 2);
                canvas.Children.Add(stack);

                string tip = k.Tip;
                if (String.IsNullOrEmpty(tip)) tip = k.Cap;
                if (!String.IsNullOrEmpty(tip))
                {
                    shape.ToolTip = tip;
                    stack.ToolTip = tip;
                }

                if (k.Vk != 0) view.Remember(k.Vk, shape, labels, glyphs, s);
            }

            var body = new Border
            {
                Background = skin.Body,
                CornerRadius = new CornerRadius(U * BodyRound(m)),
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
                                       State s, Skin skin, bool dim, TextAlignment side)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                TextAlignment = side,
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
        private static Geometry Outline(KeyCap k, double left, double top, double right, double bottom,
                                        double u, double uy, double gap, double bodyR)
        {
            double r = u * 0.13;
            KeyRect[] parts = k.Rects();
            if (parts.Length == 1)
            {
                KeyRect a = parts[0];
                var rect = new Rect((a.X - left + RimX) * u + gap / 2, (a.Y - top + RimTop) * uy + gap / 2,
                                    a.W * u - gap, a.H * uy - gap);

                // Наружный угол угловой клавиши повторяет угол корпуса — он шире
                // прочих ровно на поле. Клавиш таких четыре, и без них рисунок
                // выглядит чертежом: на живой клавиатуре это первое, что видно.
                double big = (bodyR - RimX) * u;
                bool l = a.X <= left, t = a.Y <= top;
                bool rr = a.X + a.W >= right, b = a.Y + a.H >= bottom;
                if (l || t || rr || b)
                    return Frame(rect, l && t ? big : r, rr && t ? big : r,
                                       rr && b ? big : r, l && b ? big : r);
                return new RectangleGeometry(rect, r, r);
            }

            // Г-образная — одной фигурой с настоящим вогнутым углом.
            //
            // Прежде она собиралась объединением двух скруглённых прямоугольников,
            // и на месте стыка торчали наружу два их скругления: вместо одного угла,
            // вырезанного внутрь клавиши, выходил ступенчатый нарост. На живой клавише
            // такого нет, и это первое, что бросается в глаза.
            KeyRect up = parts[0], low = parts[1];
            double xUp = (up.X - left + RimX) * u + gap / 2;
            double xLow = (low.X - left + RimX) * u + gap / 2;
            double xRight = (up.X + up.W - left + RimX) * u - gap / 2;
            double yTop = (up.Y - top + RimTop) * uy + gap / 2;
            // Ступенька идёт на ползазора выше границы рядов: столько же отступает
            // вниз клавиша, что стоит под вырезом, — вместе выходит обычный зазор.
            double yStep = (low.Y - top + RimTop) * uy - gap / 2;
            double yBottom = (low.Y + low.H - top + RimTop) * uy - gap / 2;

            var f = new PathFigure { StartPoint = new Point(xUp + r, yTop), IsClosed = true, IsFilled = true };
            f.Segments.Add(new LineSegment(new Point(xRight - r, yTop), true));
            f.Segments.Add(Bend(new Point(xRight, yTop + r), r));
            f.Segments.Add(new LineSegment(new Point(xRight, yBottom - r), true));
            f.Segments.Add(Bend(new Point(xRight - r, yBottom), r));
            f.Segments.Add(new LineSegment(new Point(xLow + r, yBottom), true));
            f.Segments.Add(Bend(new Point(xLow, yBottom - r), r));
            f.Segments.Add(new LineSegment(new Point(xLow, yStep + r), true));
            // Единственный угол, вырезанный внутрь: дуга идёт против часовой стрелки.
            f.Segments.Add(new ArcSegment(new Point(xLow - r, yStep), new Size(r, r), 0,
                                          false, SweepDirection.Counterclockwise, true));
            f.Segments.Add(new LineSegment(new Point(xUp + r, yStep), true));
            f.Segments.Add(Bend(new Point(xUp, yStep - r), r));
            f.Segments.Add(new LineSegment(new Point(xUp, yTop + r), true));
            f.Segments.Add(Bend(new Point(xUp + r, yTop), r));

            var g = new PathGeometry();
            g.Figures.Add(f);
            g.Freeze();
            return g;
        }


        /// <summary>Прямоугольник, у которого каждый угол скруглён по-своему.</summary>
        private static Geometry Frame(Rect r, double tl, double tr, double br, double bl)
        {
            // Радиус больше половины стороны выворачивает фигуру наизнанку: у половинного
            // ряда алюминиевой серии клавиша ниже, чем угол корпуса, и esc отращивала
            // синий шип выше собственной клавиши.
            double cap = Math.Min(r.Width, r.Height) / 2;
            if (tl > cap) tl = cap;
            if (tr > cap) tr = cap;
            if (br > cap) br = cap;
            if (bl > cap) bl = cap;

            var f = new PathFigure { StartPoint = new Point(r.Left + tl, r.Top), IsClosed = true, IsFilled = true };
            f.Segments.Add(new LineSegment(new Point(r.Right - tr, r.Top), true));
            f.Segments.Add(Bend(new Point(r.Right, r.Top + tr), tr));
            f.Segments.Add(new LineSegment(new Point(r.Right, r.Bottom - br), true));
            f.Segments.Add(Bend(new Point(r.Right - br, r.Bottom), br));
            f.Segments.Add(new LineSegment(new Point(r.Left + bl, r.Bottom), true));
            f.Segments.Add(Bend(new Point(r.Left, r.Bottom - bl), bl));
            f.Segments.Add(new LineSegment(new Point(r.Left, r.Top + tl), true));
            f.Segments.Add(Bend(new Point(r.Left + tl, r.Top), tl));

            var g = new PathGeometry();
            g.Figures.Add(f);
            g.Freeze();
            return g;
        }

        private static ArcSegment Bend(Point to, double r)
        {
            return new ArcSegment(to, new Size(r, r), 0, false, SweepDirection.Clockwise, true);
        }
    }
}
