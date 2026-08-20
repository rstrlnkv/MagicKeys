// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Threading;

namespace MagicKeys
{
    internal sealed class MainWindow : Window
    {
        private sealed class Choice
        {
            public object Value;
            public string Text;
            public override string ToString() { return Text; }
        }

        /// <summary>Поколение подключённой клавиатуры — от него зависят подписи F1–F12.</summary>
        private AppleGen Generation
        {
            get
            {
                AppleModel m = Devices.AppleModel;
                return m == null ? AppleGen.Unknown : m.Gen;
            }
        }

        private readonly Settings _s;
        private readonly Engine _engine;
        private readonly Action _apply;
        private readonly ContentControl _host;
        private readonly ListBox _nav;
        private readonly TextBlock _pageTitle;
        private readonly TextBlock _pageHint;
        private readonly TextBlock _deviceLine;
        private bool _building;
        private string _fnNotice;
        private int _aboutClicks;
        private string _tuneNotice;

        public MainWindow(Settings settings, Engine engine, Action apply)
        {
            _s = settings;
            _engine = engine;
            _apply = apply;

            Title = "MagicKeys";
            Width = 940;
            Height = 660;
            MinWidth = 780;
            MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SetResourceReference(BackgroundProperty, "Bg");
            // Icon намеренно не задаём: пустое значение заставляет WPF взять значок
            // из самого .exe, а он многоразмерный — и в заголовке, и на панели задач,
            // и в Alt+Tab система возьмёт подходящий размер, а не растянет один.
            if (!Fluent.HasEmbeddedIcon()) Icon = Fluent.MakeWindowIcon();
            SnapsToDevicePixels = true;
            // Прячут окно — снимаем подписку, показывают — собираем страницу заново.
            // Без второй половины страница проверки после закрытия и открытия оставалась
            // на месте, но не отзывалась ни на одно нажатие.
            IsVisibleChanged += delegate
            {
                if (IsVisible) { if (CurrentPage == "selftest") BuildPage(); }
                else DetachSelfTest();
            };
            // Сворачивание IsVisible не гасит, а окно при этом с глаз ушло: историю
            // нажатий копить в нём так же ни к чему.
            StateChanged += delegate
            {
                if (WindowState == WindowState.Minimized) DetachSelfTest();
                else if (CurrentPage == "selftest" && _selfTest == null) BuildPage();
            };
            UseLayoutRounding = true;

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---- левая колонка ----
            var railRows = new Grid { Margin = new Thickness(12, 8, 6, 12) };
            railRows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            railRows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            railRows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var brand = new StackPanel { Margin = new Thickness(10, 6, 10, 14) };
            var brandName = new TextBlock { Text = "MagicKeys" };
            brandName.SetResourceReference(StyleProperty, "Subtitle");
            var brandSub = new TextBlock { Text = "Apple Magic Keyboard в Windows", Margin = new Thickness(0, 2, 0, 0) };
            brandSub.SetResourceReference(StyleProperty, "Caption");
            brand.Children.Add(brandName);
            brand.Children.Add(brandSub);
            Grid.SetRow(brand, 0);

            _nav = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            _nav.SetResourceReference(ListBox.ItemContainerStyleProperty, "ProfileItem");
            FillNav();
            _nav.SelectedIndex = 0;
            _nav.SelectionChanged += delegate { BuildPage(); };
            Grid.SetRow(_nav, 1);

            var footer = new StackPanel { Margin = new Thickness(10, 12, 10, 0) };
            _deviceLine = new TextBlock { TextWrapping = TextWrapping.Wrap };
            _deviceLine.SetResourceReference(StyleProperty, "Caption");
            footer.Children.Add(_deviceLine);
            Grid.SetRow(footer, 2);

            railRows.Children.Add(brand);
            railRows.Children.Add(_nav);
            railRows.Children.Add(footer);
            Grid.SetColumn(railRows, 0);

            // ---- правая колонка ----
            var pane = new Grid();
            pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new StackPanel { Margin = new Thickness(28, 18, 28, 10) };
            _pageTitle = new TextBlock();
            _pageTitle.SetResourceReference(StyleProperty, "Subtitle");
            _pageHint = new TextBlock { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            _pageHint.SetResourceReference(StyleProperty, "Caption");
            head.Children.Add(_pageTitle);
            head.Children.Add(_pageHint);
            Grid.SetRow(head, 0);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(28, 4, 22, 24)
            };
            _host = new ContentControl();
            scroll.Content = _host;
            Grid.SetRow(scroll, 1);

            pane.Children.Add(head);
            pane.Children.Add(scroll);
            Grid.SetColumn(pane, 1);

            root.Children.Add(railRows);
            root.Children.Add(pane);
            Content = root;

            SourceInitialized += delegate { Fluent.ApplyWindowStyling(this); PlaceOnScreen(); };
            Loaded += delegate { BuildPage(); RefreshDevices(); };
        }

        /// <summary>По центру рабочей области и заведомо целиком на экране.</summary>
        private void PlaceOnScreen()
        {
            Rect area = SystemParameters.WorkArea;
            if (Width > area.Width - 32) Width = Math.Max(MinWidth, area.Width - 32);
            if (Height > area.Height - 32) Height = Math.Max(MinHeight, area.Height - 32);
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }

        /// <summary>
        /// Пересобирает содержимое под новое оформление. Кисти и стили, присвоенные
        /// значением при постройке страницы, сами не перекрашиваются, поэтому проще
        /// собрать страницу заново — она и так строится за миллисекунды.
        /// </summary>
        public void RefreshTheme()
        {
            BuildPage();
        }

        public void RefreshDevices()
        {
            IList<KeyboardInfo> all = Devices.Known;
            int apple = 0;
            foreach (KeyboardInfo k in all) if (k.IsApple) apple++;

            string failure = _engine == null ? null : _engine.Failure;
            if (!String.IsNullOrEmpty(failure))
                _deviceLine.Text = "Перехват клавиатуры не работает — переназначения не действуют.";
            else if (apple > 0)
                _deviceLine.Text = "Magic Keyboard подключена. Клавиатур в системе: " + all.Count + ".";
            else
                _deviceLine.Text = "Клавиатура Apple не найдена. Клавиатур в системе: " + all.Count + ".";

            if (CurrentPage == "devices") BuildPage();
        }

        // ------------------------------------------------------------------
        //  Сборка страниц
        // ------------------------------------------------------------------

        /// <summary>
        /// Список страниц. Ключи, а не номера: страницы появляются и исчезают вместе
        /// с режимом разработчика, и нумерация от этого разъезжается.
        /// </summary>
        private readonly List<string> _pages = new List<string>();

        private void FillNav()
        {
            string current = _nav.SelectedIndex >= 0 && _nav.SelectedIndex < _pages.Count
                ? _pages[_nav.SelectedIndex] : "fkeys";

            _nav.Items.Clear();
            _pages.Clear();

            AddPage("fkeys", "Функциональные клавиши");
            AddPage("mods", "Модификаторы");
            AddPage("mac", "Сочетания macOS");
            AddPage("layout", "Раскладка");
            AddPage("driver", "Драйвер Apple");
            if (_s.DeveloperMode)
            {
                AddPage("devices", "Устройства");
                AddPage("selftest", "Проверка клавиш");
            }
            AddPage("general", "Общие");
            AddPage("about", "О программе");

            int idx = _pages.IndexOf(current);
            _nav.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void AddPage(string key, string title)
        {
            _pages.Add(key);
            _nav.Items.Add(title);
        }

        /// <summary>Переход на страницу по ключу. Если её нет — молча остаёмся на месте.</summary>
        private void GoTo(string key)
        {
            int i = _pages.IndexOf(key);
            if (i >= 0) _nav.SelectedIndex = i;
        }

        private string CurrentPage
        {
            get
            {
                int i = _nav.SelectedIndex;
                return i >= 0 && i < _pages.Count ? _pages[i] : "fkeys";
            }
        }

        private void BuildPage()
        {
            if (_building) return;
            _building = true;

            // Уходим со страницы проверки — снимаем подписку, иначе они копятся,
            // а надписи, на которые они ссылаются, уже выброшены.
            DetachSelfTest();

            try
            {
                switch (CurrentPage)
                {
                    case "fkeys": Head("Функциональные клавиши", "Что делают F1–F" + Models.FunctionKeyCount(_s) + ". По умолчанию — то же, что напечатано на клавишах Apple."); _host.Content = PageFKeys(); break;
                    case "mods": Head("Модификаторы", "Куда ведут ⌘, ⌥, control и Caps Lock."); _host.Content = PageModifiers(); break;
                    case "mac": Head("Сочетания macOS", "⌘C, ⌘←, ⌘Q и остальные — чтобы пальцы не переучивались."); _host.Content = PageMacKeys(); break;
                    case "layout": Head("Раскладка", "Раскладки macOS — то, что напечатано на клавишах Apple."); _host.Content = PageLayout(); break;
                    case "driver": Head("Драйвер Apple", "Родной драйвер Boot Camp и как программа с ним уживается."); _host.Content = PageDriver(); break;
                    case "devices": Head("Устройства", "Клавиатуры, которые сейчас видит система."); _host.Content = PageDevices(); break;
                    case "selftest": Head("Проверка клавиш", "Нажимайте клавиши — программа покажет, что из этого дошло до Windows."); _host.Content = PageSelfTest(); break;
                    case "general": Head("Общие", "Поведение самой программы."); _host.Content = PageGeneral(); break;
                    default: Head("О программе", "MagicKeys — свободная программа."); _host.Content = PageAbout(); break;
                }
            }
            finally { _building = false; }
        }

        private void Head(string title, string hint)
        {
            _pageTitle.Text = title;
            _pageHint.Text = hint;
        }

        private UIElement PageFKeys()
        {
            var stack = new StackPanel();

            // Пока ряд отдан драйверу, всё на этой странице бездействует. Молча
            // показывать настройки, которые ни на что не влияют, — обман.
            AppleDriver.Refresh(false);
            if (YieldingNow)
            {
                stack.Children.Add(Card("Сейчас этим занимается драйвер Apple", null,
                    Note("Функциональный ряд обрабатывает родной драйвер Boot Camp, а программа ему " +
                         "уступает — иначе одно нажатие переназначалось бы дважды. Поэтому строки F1–F12 " +
                         "ниже сейчас ни на что не влияют: эти нажатия до программы не доходят, " +
                         "они приходят уже готовыми медиакодами. Остальное на странице работает " +
                         "по-прежнему: F13–F19 и цифровой блок программа обрабатывает сама.\n\n" +
                         "Чтобы вернуть ряд себе, переключите режим на странице «Драйвер Apple» — " +
                         "там же объяснено, что при этом меняется."),
                    LinkButton("Перейти к драйверу Apple", delegate { GoTo("driver"); })));
            }

            var modeBox = Combo(new[]
            {
                new Choice { Value = true,  Text = "Медиафункции сразу (как в macOS)" },
                new Choice { Value = false, Text = "F-клавиши сразу (как обычно в Windows)" }
            }, _s.MediaFirst, delegate(object v) { _s.MediaFirst = (bool)v; Save(); });

            stack.Children.Add(Card("Что делают F1–F12 без модификатора", null,
                Row("Основной режим", null, modeBox)));

            var fnChoices = new List<Choice>();
            fnChoices.Add(new Choice { Value = ModKey.None, Text = "Нет — переключать только в этом окне" });
            foreach (ModKey m in new[] { ModKey.RAlt, ModKey.RWin, ModKey.LWin, ModKey.RCtrl, ModKey.CapsLock })
                fnChoices.Add(new Choice { Value = m, Text = ModNames.Of(m) });

            var fnBox = Combo(fnChoices.ToArray(), _s.FnSubstitute,
                delegate(object v) { _s.FnSubstitute = (ModKey)v; Save(); });

            stack.Children.Add(Card("Заменитель Fn", null,
                Row("Клавиша", "С ней режим временно переворачивается", fnBox),
                Toggle("Навигация как в macOS: Fn+стрелки — Home, End, PgUp, PgDn; Fn+Backspace — Delete; Fn+Enter — Insert",
                    _s.FnNavigation, delegate(bool v) { _s.FnNavigation = v; Save(); }),
                Note(AppleDriver.TakesFunctionRow
                    ? "С родным драйвером Apple настоящая клавиша Fn работает: замерено, что Fn+F1 " +
                      "даёт настоящий F1. Заменитель при этом не нужен — его можно оставить " +
                      "выключенным, а переключать режим ряда настоящей клавишей Fn.\n\n" +
                      "Навигацию Fn+стрелки драйвер, по-видимому, берёт на себя тоже — проверьте " +
                      "Fn+←, — так что и её здесь " +
                      "включать не обязательно."
                    : "Без драйвера Apple настоящая клавиша Fn до Windows не доходит: она " +
                      "обрабатывается внутри клавиатуры и не отправляет ни одного события. " +
                      "Проверено — нажатие Fn не даёт ничего, а Fn+F1 приходит как обычный F1. " +
                      "Поэтому нужен заменитель.\n\n" +
                      "Заменитель сохраняет своё обычное значение. Если он нужен только как Fn, " +
                      "отключите эту клавишу на вкладке «Модификаторы» — переключение режима " +
                      "продолжит работать.")));

            string[] legends = Models.Legend(Generation);
            int fcount = Models.FunctionKeyCount(_s);
            var table = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < fcount; i++)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var cap = new Border
                {
                    Background = (Brush)Application.Current.Resources["LayerAlt"],
                    BorderBrush = (Brush)Application.Current.Resources["StrokeStrong"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Width = 42,
                    Height = 30,
                    Margin = new Thickness(0, 4, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                cap.Child = new TextBlock
                {
                    Text = "F" + (i + 1),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(cap, i); Grid.SetColumn(cap, 0);

                var legend = new TextBlock
                {
                    Text = i < legends.Length ? legends[i] : "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 12, 0)
                };
                legend.SetResourceReference(StyleProperty, "Caption");
                Grid.SetRow(legend, i); Grid.SetColumn(legend, 1);

                int index = i;
                var box = ActionCombo(_s.FKey(i), delegate(string id) { _s.FKeys[index] = id; Save(); });
                box.Margin = new Thickness(0, 4, 0, 4);
                Grid.SetRow(box, i); Grid.SetColumn(box, 2);

                table.Children.Add(cap);
                table.Children.Add(legend);
                table.Children.Add(box);
            }

            var reset = new Button { Content = "Вернуть заводские для этой модели", HorizontalAlignment = HorizontalAlignment.Left };
            reset.SetResourceReference(StyleProperty, "Btn");
            reset.Click += delegate
            {
                _s.FKeys = Models.DefaultFKeys(Generation);
                Save();
                BuildPage();
            };
            AppleModel fm = Devices.AppleModel;
            string howMany = "Показано клавиш: " + fcount + ". ";
            if (fm != null && fm.FunctionKeys > 12)
                howMany += "У этой модели есть ряд F13–F" + fm.FunctionKeys + " над цифровым блоком. ";
            howMany += "Если нажать клавишу, которой в списке нет, она добавится сама — " +
                       "программа следит за этим сама, " +
                       "знает устройство.";

            stack.Children.Add(Card("Действия",
                "Слева — то, что напечатано на клавише Apple (" + Models.GenName(Generation) + ").",
                table, reset, Note(howMany)));

            if (fm == null || fm.Numpad)
            {
                var padGrid = new Grid();
                padGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                padGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

                AddSingleRow(padGrid, 0, "Clear", "Верхняя левая на цифровом блоке, у Apple помечена значком очистки",
                    _s.NumpadClear, delegate(string id) { _s.NumpadClear = id; Save(); });
                AddSingleRow(padGrid, 1, "= на цифровом блоке", "Windows её не понимает",
                    _s.NumpadEquals, delegate(string id) { _s.NumpadEquals = id; Save(); });

                if (fm == null || fm.Eject)
                {
                    var ejGrid = new Grid();
                    ejGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    ejGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                    AddSingleRow(ejGrid, 0, "Eject", "Верхний правый угол, со значком извлечения",
                        _s.EjectKey, delegate(string id) { _s.EjectKey = id; Save(); });

                    string ejNote =
                        "На Magic Keyboard по Bluetooth эта " +
                        "клавиша до Windows не доходит вообще. Проверено — пять нажатий подряд не дали " +
                        "ни одного события ни по одному из путей: ни как клавиша, ни на медиастранице, " +
                        "ни на системной, ни в вендорном отчёте Apple.\n\n" +
                        "Причина в самой клавиатуре, и драйвер здесь не выручает — проверяли и с ним: " +
                        "она заводит в Windows всего две " +
                        "коллекции HID — клавиатуру и вендорную Apple, — а Eject живёт на медиастранице, " +
                        "которой у неё просто нет.\n\n" +
                        "Настройка ниже рассчитана на клавиатуры, которые Eject всё-таки присылают " +
                        "(алюминиевые модели по USB): программа слушает медиастраницу через сырой ввод. " +
                        "Проверить это здесь было не на чем — такой клавиатуры под рукой нет.";
                    if (KeyWatch.EjectSeen)
                        ejNote = "Клавиша Eject уже приходила с вашей клавиатуры — назначение работает.\n\n" + ejNote;

                    stack.Children.Add(Card("Клавиша Eject", null, ejGrid, Note(ejNote)));
                }

                stack.Children.Add(Card("Цифровой блок", null, padGrid,
                    Note("Две клавиши цифрового блока Apple в Windows ведут себя не так, как " +
                         "напечатано. Проверено на подключённой клавиатуре.\n\n" +
                         "Clear отдаёт Num Lock — то есть попадание по ней молча превращает " +
                         "цифровой блок в навигационный. По умолчанию она делает Delete, как ближайший " +
                         "к маковскому «очистить» смысл. Пока эта клавиша уведена с Num Lock, программа " +
                         "сама следит, чтобы цифровой блок оставался включённым: переключить его иначе " +
                         "было бы нечем.\n\n" +
                         "«=» приходит не знаком равенства, а кодом «очистить», которого не понимает почти " +
                         "ни одна программа, поэтому по умолчанию она печатает знак «=» напрямую.")));
            }

            var stepBox = Combo(new[]
            {
                new Choice { Value = 5,  Text = "5 %" },
                new Choice { Value = 10, Text = "10 %" },
                new Choice { Value = 20, Text = "20 %" }
            }, _s.BrightnessStep, delegate(object v) { _s.BrightnessStep = (int)v; Save(); });

            stack.Children.Add(Card("Яркость", null,
                Row("Шаг изменения", null, stepBox),
                Toggle("Показывать индикатор яркости на экране", _s.ShowBrightnessOsd,
                    delegate(bool v) { _s.ShowBrightnessOsd = v; Save(); }),
                Toggle("Подхватывать коды яркости от чужого драйвера",
                    _s.BrightnessFromMediaKeys,
                    delegate(bool v) { _s.BrightnessFromMediaKeys = v; Save(); }),
                Note("Яркостью внешнего монитора программа управляет по служебному каналу самого кабеля, " +
                     "встроенной панелью ноутбука — средствами Windows. Если монитор такого " +
                     "управления не поддерживает, эти клавиши ничего не сделают — тогда назначьте " +
                     "на F1 и F2 что-нибудь другое.\n\n" +
                     "Вторая настройка нужна, когда функциональным рядом занимается чужой драйвер. " +
                     "Тогда F1 и F2 до программы не доходят: они переводятся сразу в коды яркости " +
                     "медиастраницы. Windows применяет такие коды только к встроенной панели " +
                     "ноутбука, и на обычном ПК они пропадают зря — а программа ловит их и " +
                     "применяет к внешним мониторам.\n\n" +
                     "С драйвером Apple это не выручит: замерено, что F1 и F2 он съедает и не " +
                     "присылает ничего. Яркость внешних мониторов с ним доступна только в режиме " +
                     "«F-клавиши сразу» на странице «Драйвер Apple».")));

            return stack;
        }

        private UIElement PageModifiers()
        {
            var stack = new StackPanel();

            var presets = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, -8, -8) };
            presets.Children.Add(PresetButton("Как в macOS", "⌘ работает как Ctrl: ⌘+C копирует", delegate
            {
                _s.MapLCtrl = ModKey.LWin;
                _s.MapLWin = ModKey.LCtrl;
                _s.MapRWin = ModKey.RCtrl;
                _s.MapLAlt = ModKey.LAlt;
                _s.MapRAlt = ModKey.RAlt;
                Save(); BuildPage();
            }));
            presets.Children.Add(PresetButton("Как в Windows", "Ctrl / Win / Alt встают по местам", delegate
            {
                _s.MapLCtrl = ModKey.LCtrl;
                _s.MapLAlt = ModKey.LWin;
                _s.MapLWin = ModKey.LAlt;
                _s.MapRWin = ModKey.RAlt;
                _s.MapRAlt = ModKey.RWin;
                Save(); BuildPage();
            }));
            presets.Children.Add(PresetButton("Без изменений", "Как приходит от клавиатуры", delegate
            {
                _s.MapLCtrl = ModKey.LCtrl;
                _s.MapLWin = ModKey.LWin;
                _s.MapLAlt = ModKey.LAlt;
                _s.MapRAlt = ModKey.RAlt;
                _s.MapRWin = ModKey.RWin;
                Save(); BuildPage();
            }));
            stack.Children.Add(Card("Готовые схемы", null, presets));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            AddModRow(grid, 0, "control (левый)", "Крайняя левая клавиша", _s.MapLCtrl,
                delegate(ModKey m) { _s.MapLCtrl = m; Save(); });
            AddModRow(grid, 1, "⌥ option (левый)", "Между control и ⌘", _s.MapLAlt,
                delegate(ModKey m) { _s.MapLAlt = m; Save(); });
            AddModRow(grid, 2, "⌘ command (левая)", "Слева от пробела", _s.MapLWin,
                delegate(ModKey m) { _s.MapLWin = m; Save(); });
            AddModRow(grid, 3, "⌘ command (правая)", "Справа от пробела", _s.MapRWin,
                delegate(ModKey m) { _s.MapRWin = m; Save(); });
            AddModRow(grid, 4, "⌥ option (правый)", "Правее правой ⌘", _s.MapRAlt,
                delegate(ModKey m) { _s.MapRAlt = m; Save(); });
            AddModRow(grid, 5, "Caps Lock", "Часто удобнее как Ctrl или Escape", _s.MapCapsLock,
                delegate(ModKey m) { _s.MapCapsLock = m; Save(); });

            stack.Children.Add(Card("Отдельные клавиши", "Слева — что напечатано на клавише, справа — чем она станет.", grid));

            stack.Children.Add(Card("Поведение", null,
                Toggle("⌘+Tab переключает окна, как Alt+Tab", _s.CmdTabSwitchesWindows,
                    delegate(bool v) { _s.CmdTabSwitchesWindows = v; Save(); }),
                Toggle("Правый ⌥ — обычный Alt, без AltGr", _s.DisableAltGr,
                    delegate(bool v) { _s.DisableAltGr = v; Save(); }),
                Note("На раскладках с AltGr правый ⌥ приходит именно как AltGr — Windows добавляет " +
                     "к нему невидимый Ctrl, и сочетания вроде ⌥+буква срабатывают не так, как " +
                     "ожидаешь. Включайте, только если не набираете символы через AltGr: с этой " +
                     "настройкой третий уровень раскладки — правый ⌥ и клавиша — перестанет " +
                     "набираться.")));

            return stack;
        }

        private UIElement PageLayout()
        {
            var stack = new StackPanel();
            PhysLayout phys = _engine == null ? PhysLayout.Ansi : _engine.Physical(_s);
            PhysLayout detected = _engine == null ? PhysLayout.Ansi : _engine.DetectedPhysical;

            // ---- исполнение ----
            var physBox = Combo(new[]
            {
                new Choice { Value = PhysLayout.Auto, Text = "Определять самой" },
                new Choice { Value = PhysLayout.Ansi, Text = "ANSI (американское)" },
                new Choice { Value = PhysLayout.Iso,  Text = "ISO (европейское)" },
                new Choice { Value = PhysLayout.Jis,  Text = "JIS (японское)" }
            }, _s.Physical, delegate(object v) { _s.Physical = (PhysLayout)v; Save(); BuildPage(); });

            AppleModel model = Devices.AppleModel;
            string physNote = "Сейчас программа считает клавиатуру: " + Models.PhysName(phys) + ".\n";
            if (model != null && model.Phys != PhysLayout.Auto)
                physNote += "Исполнение закодировано в идентификаторе модели — " + Models.PhysName(model.Phys) + ".";
            else
                physNote += "У Magic Keyboard исполнение не закодировано в идентификаторе, поэтому оно " +
                            "определяется по нажатиям: клавиша слева от «Z» выдаёт ISO, клавиши у пробела — JIS. " +
                            "Пока распознано: " + Models.PhysName(detected) + ".";

            // Исполнение определяется само по нажатиям — обычному человеку крутить его незачем.
            if (_s.DeveloperMode)
                stack.Children.Add(Card("Исполнение клавиатуры", null,
                    Row("Тип", "От него зависит набор клавиш, а не язык", physBox),
                    Note(physNote)));
            else
                physBox.Visibility = Visibility.Collapsed;

            // ---- перестановка двух клавиш ISO ----
            var isoToggle = Toggle("Поменять эти две клавиши местами", _s.SwapIsoKeys,
                delegate(bool v) { _s.SwapIsoKeys = v; Save(); });
            isoToggle.IsEnabled = !_s.AppleLayoutEnabled && phys == PhysLayout.Iso;

            string isoNote =
                "У ISO-клавиатуры Apple эти две клавиши стоят наоборот по отношению к тому, " +
                "что ожидает Windows.\n\n" +
                "Проверено на подключённой клавиатуре: клавиша слева от «1» отдаёт скан-код 0x56 " +
                "(в Windows это дополнительная клавиша ISO, «\\»), а клавиша слева от «Z» — " +
                "скан-код 0x29 (в Windows это клавиша слева от «1»: «ё» в русской раскладке, " +
                "«`~» в английской). Ровно то же исправление делает драйвер hid-apple в Linux.\n\n" +
                "С этой настройкой обе встают на свои места в любой раскладке: символ выбирает " +
                "сама раскладка, программа лишь меняет скан-коды местами.";
            if (_s.AppleLayoutEnabled)
                isoNote = "Не нужно: включены раскладки Apple, а они и так расставляют символы " +
                          "по скан-кодам правильно.\n\n" + isoNote;
            else if (phys != PhysLayout.Iso)
                isoNote = "Не нужно: клавиатура не в исполнении ISO, лишней клавиши на ней нет.\n\n" + isoNote;

            stack.Children.Add(Card("Клавиши слева от «1» и слева от «Z»", null, isoToggle, Note(isoNote)));

            // ---- раскладки Apple ----
            var levelBox = Combo(new[]
            {
                new Choice { Value = OptLevel.RightOption, Text = "Правый ⌥ (как AltGr)" },
                new Choice { Value = OptLevel.AnyOption,   Text = "Любой ⌥ (как в macOS)" },
                new Choice { Value = OptLevel.Off,         Text = "Не набирать: ⌥ остаётся Alt" }
            }, _s.OptLevel, delegate(object v) { _s.OptLevel = (OptLevel)v; Save(); });

            stack.Children.Add(Card("Раскладки Apple", null,
                Toggle("Воспроизводить раскладки macOS", _s.AppleLayoutEnabled,
                    delegate(bool v) { _s.AppleLayoutEnabled = v; Save(); BuildPage(); }),
                _s.DeveloperMode
                    ? Row("Третий уровень", "Символы, напечатанные на клавише третьими", levelBox)
                    : (UIElement)new StackPanel { Visibility = Visibility.Collapsed },
                _s.DeveloperMode
                    ? (UIElement)Toggle("Подменять все клавиши, а не только отличающиеся от раскладки Microsoft",
                        _s.AppleLayoutAll, delegate(bool v) { _s.AppleLayoutAll = v; Save(); })
                    : new StackPanel { Visibility = Visibility.Collapsed },
                Note("Apple раскладывает буквы и знаки иначе, чем Microsoft, — и именно поэтому " +
                     "Boot Camp когда-то доставлял в Windows отдельные языки ввода «(Apple)». " +
                     "Здесь то же самое делается без установки раскладок в систему: программа " +
                     "подменяет только те клавиши, которые в действующей раскладке Windows дают " +
                     "не то, что напечатано на клавише Apple. Остальные нажатия идут как обычно.\n\n" +
                     "Таблицы взяты из Unicode CLDR (keyboards/osx) — это те же данные, по которым " +
                     "работает сама macOS; мёртвые клавиши (´ ` ¨ ˆ ~) поддерживаются.\n\n" +
                     "«Любой ⌥» точнее повторяет Mac, но тогда Alt перестаёт открывать меню программ; " +
                     "«правый ⌥» — привычный для Windows размен.")));

            // Языки ввода «(Apple)», которые ставит драйвер Boot Camp, здесь намеренно
            // не показываются и не предлагаются. Windows 10 и 11 работают с ними плохо
            // и время от времени возвращают в список старые раскладки сами по себе.
            // Подмена символов на лету такого изъяна не имеет: она ничего не ставит
            // в систему и ничего не может вернуть без спроса.

            // ---- языки ----
            var langGrid = new Grid();
            langGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            langGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

            IList<KeyValuePair<int, string>> langs = InputLanguages();
            int row = 0;
            foreach (KeyValuePair<int, string> lang in langs)
            {
                langGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 16, 6) };
                left.Children.Add(new TextBlock { Text = lang.Value, TextWrapping = TextWrapping.Wrap });
                var code = new TextBlock { Text = _s.DeveloperMode ? "LANGID " + lang.Key.ToString("X4") : "", Margin = new Thickness(0, 2, 0, 0) };
                code.SetResourceReference(StyleProperty, "Caption");
                left.Children.Add(code);
                Grid.SetRow(left, row); Grid.SetColumn(left, 0);

                var choices = new List<Choice>();
                choices.Add(new Choice { Value = "", Text = "Не подменять" });
                foreach (AppleLayoutFile f in Layouts.All)
                    choices.Add(new Choice { Value = f.Id, Text = f.Title + " · " + f.MacName });

                string current = _s.LayoutFor(lang.Key) ?? "";
                int langId = lang.Key;
                var box = Combo(choices.ToArray(), current,
                    delegate(object v) { _s.SetLayoutFor(langId, (string)v); Save(); });
                box.Margin = new Thickness(0, 6, 0, 6);
                box.IsEnabled = _s.AppleLayoutEnabled;
                Grid.SetRow(box, row); Grid.SetColumn(box, 1);

                langGrid.Children.Add(left);
                langGrid.Children.Add(box);
                row++;
            }

            var reload = new Button { Content = "Перечитать файлы раскладок", HorizontalAlignment = HorizontalAlignment.Left };
            reload.SetResourceReference(StyleProperty, "Btn");
            reload.Click += delegate { Layouts.Reload(); BuildPage(); };

            stack.Children.Add(Card("Языки ввода Windows",
                "Раскладка Apple применяется к тому языку, который выбран в этот момент.",
                langGrid, reload,
                Note("Файлов раскладок найдено: " + Layouts.All.Count + ". Встроенные лежат в папке " +
                     "«layouts» рядом с программой, свои можно положить в " + Layouts.UserFolder +
                     " — одноимённые вытеснят встроенные.")));

            // ---- японские клавиши ----
            if (phys == PhysLayout.Jis)
            {
                string[] names = { "かな (кана)", "変換 (преобразовать)", "無変換 (не преобразовывать)", "ろ (ро)", "¥ (иена)" };
                var jisGrid = new Grid();
                jisGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                jisGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                for (int i = 0; i < names.Length; i++)
                {
                    jisGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var lbl = new TextBlock { Text = names[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 16, 6) };
                    Grid.SetRow(lbl, i); Grid.SetColumn(lbl, 0);
                    int idx = i;
                    var box = ActionCombo(_s.JisKey(i), delegate(string id) { _s.JisKeys[idx] = id; Save(); });
                    box.Margin = new Thickness(0, 6, 0, 6);
                    Grid.SetRow(box, i); Grid.SetColumn(box, 1);
                    jisGrid.Children.Add(lbl);
                    jisGrid.Children.Add(box);
                }
                if (_s.DeveloperMode)
                    stack.Children.Add(Card("Клавиши японской раскладки", null, jisGrid,
                        Note("Раздел появляется, когда клавиатура опознана как JIS и включён " +
                             "режим разработчика. Проверить его на настоящей японской клавиатуре " +
                             "не удалось — её здесь нет, так что скан-коды взяты из документации, " +
                             "а не из опыта.")));
            }

            return stack;
        }

        /// <summary>Языки ввода, установленные в Windows.</summary>
        private static IList<KeyValuePair<int, string>> InputLanguages()
        {
            var result = new List<KeyValuePair<int, string>>();
            var seen = new HashSet<int>();
            try
            {
                uint n = Native.GetKeyboardLayoutList(0, null);
                if (n == 0) return result;
                IntPtr[] list = new IntPtr[n];
                Native.GetKeyboardLayoutList((int)n, list);
                foreach (IntPtr hkl in list)
                {
                    int lang = (int)(hkl.ToInt64() & 0xFFFF);
                    if (lang == 0 || !seen.Add(lang)) continue;
                    string name;
                    try { name = new System.Globalization.CultureInfo(lang).DisplayName; }
                    catch { name = "Язык " + lang.ToString("X4"); }
                    result.Add(new KeyValuePair<int, string>(lang, name));
                }
            }
            catch { }
            result.Sort(delegate(KeyValuePair<int, string> a, KeyValuePair<int, string> b)
            {
                return String.Compare(a.Value, b.Value, StringComparison.CurrentCulture);
            });
            return result;
        }

        private UIElement PageGeneral()
        {
            var stack = new StackPanel();

            string failure = _engine == null ? null : _engine.Failure;
            if (!String.IsNullOrEmpty(failure))
                stack.Children.Add(Card("Перехват клавиатуры не работает", null,
                    Note(failure + "\n\nПока этого не случится, переназначения, сочетания macOS " +
                         "и раскладки не работают.")));

            stack.Children.Add(Card("Работа программы", null,
                Toggle("Переназначения включены", _s.Enabled,
                    delegate(bool v) { _s.Enabled = v; Save(); }),
                Toggle("Приостанавливать, пока Magic Keyboard не подключена", _s.PauseWhenAppleAbsent,
                    delegate(bool v) { _s.PauseWhenAppleAbsent = v; Save(); }),
                Note("Windows не сообщает низкоуровневому перехватчику, с какой именно клавиатуры " +
                     "пришло нажатие — сведения об устройстве приходят уже после того, как решение " +
                     "принято. Поэтому переназначения действуют на весь ввод. Если рядом работает " +
                     "вторая клавиатура, вторая настройка избавляет хотя бы от случая, когда " +
                     "Magic Keyboard выключена, а её правила всё ещё в силе.")));

            stack.Children.Add(Card("Запуск", null,
                Toggle("Запускать вместе с Windows", _s.Autostart,
                    delegate(bool v) { _s.Autostart = v; Autostart.Set(v); Save(); }),
                Toggle("Запускаться свёрнутой в значок", _s.StartMinimized,
                    delegate(bool v) { _s.StartMinimized = v; Save(); })));

            var openFolder = new Button { Content = "Открыть папку настроек", HorizontalAlignment = HorizontalAlignment.Left };
            openFolder.SetResourceReference(StyleProperty, "Btn");
            openFolder.Click += delegate
            {
                try
                {
                    System.IO.Directory.CreateDirectory(Settings.Folder);
                    Process.Start(new ProcessStartInfo(Settings.Folder) { UseShellExecute = true });
                }
                catch { }
            };
            stack.Children.Add(Card("Настройки", Settings.FilePath, openFolder));

            return stack;
        }

        private UIElement PageDevices()
        {
            var stack = new StackPanel();

            IList<KeyboardInfo> all = Devices.Known;
            if (all.Count == 0)
            {
                stack.Children.Add(Card("Клавиатуры не найдены", null,
                    Note("Система не вернула ни одного устройства ввода. Такое бывает сразу после " +
                         "запуска — нажмите «Обновить».")));
            }

            foreach (KeyboardInfo k in all)
            {
                var rows = new StackPanel();
                if (!String.IsNullOrEmpty(k.Product)) rows.Children.Add(KeyValue("Сообщает о себе", k.Product));
                if (!String.IsNullOrEmpty(k.Manufacturer)) rows.Children.Add(KeyValue("Производитель", k.Manufacturer));
                if (k.Apple != null)
                {
                    if (!String.IsNullOrEmpty(k.Apple.Designation))
                        rows.Children.Add(KeyValue("Номер модели", k.Apple.Designation + " · " + k.Apple.Year + " год"));
                    rows.Children.Add(KeyValue("Функциональных клавиш", "F1–F" + k.Apple.FunctionKeys +
                        (KeyWatch.MaxFunctionKey > 0 ? " · замечено до F" + KeyWatch.MaxFunctionKey : "")));
                    rows.Children.Add(KeyValue("Поколение", Models.GenName(k.Apple.Gen)));
                    string extras = (k.Apple.Numpad ? "цифровой блок" : "без цифрового блока");
                    if (k.Apple.TouchId) extras += ", Touch ID (в Windows не работает)";
                    rows.Children.Add(KeyValue("Особенности", extras));
                }
                rows.Children.Add(KeyValue("Подключение", k.Bluetooth ? "Bluetooth" : "USB"));
                rows.Children.Add(KeyValue("Идентификаторы", k.VendorProduct));
                if (k.IsApple) rows.Children.Add(KeyValue("Исполнение", Models.PhysName(_engine == null ? PhysLayout.Auto : _engine.Physical(_s))));
                if (k.TotalKeys > 0) rows.Children.Add(KeyValue("Клавиш", k.TotalKeys.ToString()));
                stack.Children.Add(Card(k.Model, k.IsApple ? "Правила MagicKeys рассчитаны на эту клавиатуру" : null, rows));
            }

            int battery = KeyboardBattery.Percent;
            if (battery >= 0)
            {
                var brows = new StackPanel();
                brows.Children.Add(KeyValue("Заряд", battery + " %"));
                stack.Children.Add(Card("Заряд батареи", null, brows,
                    Note("Windows этот уровень не публикует — свойство DEVPKEY_Bluetooth_Battery " +
                         "у клавиатуры пустое, и сама она отчёт о заряде не присылает. Зато его можно " +
                         "спросить: в вендорной коллекции Apple «Device Management» есть входной отчёт " +
                         "0x90 со страницей Battery System, и клавиатура отдаёт его по требованию. " +
                         "Значение обновляется раз в минуту.")));
            }
            else
            {
                stack.Children.Add(Card("Заряд батареи", null,
                    Note("Спросить не удалось. Программа запрашивает у клавиатуры входной отчёт 0x90 " +
                         "в вендорной коллекции Apple «Device Management»; если этой коллекции нет " +
                         "или она не отвечает, показывать нечего — придумывать число программа не станет.")));
            }

            var refresh = new Button { Content = "Обновить", HorizontalAlignment = HorizontalAlignment.Left };
            refresh.SetResourceReference(StyleProperty, "Btn");
            refresh.Click += delegate { Devices.Rescan(); RefreshDevices(); };
            stack.Children.Add(refresh);

            return stack;
        }

        // ------------------------------------------------------------------
        //  Проверка клавиш
        // ------------------------------------------------------------------

        private Action<KeyWatch.KeyEvent> _selfTest;
        private TextBlock _selfTestLog;
        private StackPanel _selfTestChecks;
        private readonly List<string> _selfTestLines = new List<string>();

        /// <summary>
        /// Живой просмотр того, что клавиатура на самом деле присылает. Смотрит сырой
        /// ввод, а не собственный перехват: так видно устройство напрямую, независимо
        /// от того, что программа с нажатием потом делает.
        /// </summary>
        /// <summary>Сочетания macOS: общий выключатель и таблица по разделам.</summary>
        private UIElement PageMacKeys()
        {
            var stack = new StackPanel();

            stack.Children.Add(Card("Главный выключатель", null,
                Toggle("Переводить сочетания macOS в виндовые", _s.MacShortcuts,
                    delegate(bool v) { _s.MacShortcuts = v; Save(); BuildPage(); }),
                Note("По умолчанию ⌘ остаётся клавишей Windows: программа не меняет её назначение, " +
                     "а переводит сами сочетания. Поэтому получается и то, чего простой " +
                     "заменой клавиш не добиться: ⌘← уходит в начало строки, ⌘Backspace стирает " +
                     "до начала строки, ⌘Q закрывает программу.\n\n" +
                     "Если на странице «Модификаторы» выбрана схема «Как в macOS», ⌘ уже стала " +
                     "Ctrl — тогда эти переводы ей не нужны.\n\n" +
                     "Любое сочетание можно выключить по отдельности — тогда оно вернётся " +
                     "к обычному поведению Windows.")));

            if (!_s.MacShortcuts) return stack;

            Choice[] spaceChoices =
            {
                new Choice { Value = "search",   Text = "Поиск Windows (Win+S)" },
                new Choice { Value = "language", Text = "Переключить язык (Win+Space)" },
                new Choice { Value = "none",     Text = "Не трогать" }
            };

            stack.Children.Add(Card("Пробел", "На маке привычки расходятся, поэтому обе клавиши отдельно.",
                Row("⌘ + пробел", null, Combo(spaceChoices, _s.CmdSpace,
                    delegate(object v) { _s.CmdSpace = (string)v; Save(); })),
                Row("control + пробел", null, Combo(spaceChoices, _s.CtrlSpace,
                    delegate(object v) { _s.CtrlSpace = (string)v; Save(); })),
                Note("У одних ⌘Space открывает поиск, у других переключает язык, а поиск " +
                     "висит на ⌃Space. Навязывать один вариант неправильно.\n\n" +
                     "Учтите: Win+Space — это переключатель языка самой Windows, он листает " +
                     "раскладки по кругу.")));

            string[] groups = { MacKeys.GroupEdit, MacKeys.GroupText, MacKeys.GroupSystem };
            string[] hints =
            {
                "Привычные действия с ⌘ вместо Ctrl.",
                "Перемещение и правка текста — то, чего на Windows больше всего не хватает.",
                "Окна, поиск и снимки экрана. Часть таких сочетаний Windows забирает себе, " +
                "и тогда перевод может не сработать."
            };

            for (int g = 0; g < groups.Length; g++)
            {
                var rows = new StackPanel();
                foreach (MacShortcut sc in MacKeys.All)
                {
                    if (sc.Group != groups[g]) continue;
                    rows.Children.Add(MacRow(sc));
                }
                stack.Children.Add(Card(groups[g], hints[g], rows));
            }

            return stack;
        }

        private UIElement MacRow(MacShortcut sc)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });

            string id = sc.Id;
            var cb = new CheckBox
            {
                Style = (Style)Application.Current.Resources["Switch"],
                IsChecked = _s.MacEnabled(id),
                VerticalAlignment = VerticalAlignment.Center
            };
            cb.Checked += delegate { if (!_building) { _s.MacSet(id, true); Save(); } };
            cb.Unchecked += delegate { if (!_building) { _s.MacSet(id, false); Save(); } };
            Grid.SetColumn(cb, 0);

            var mid = new StackPanel();
            mid.Children.Add(new TextBlock { Text = sc.Mac + "   " + sc.Title, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(mid, 1);

            var win = new TextBlock { Text = sc.Win, TextWrapping = TextWrapping.Wrap };
            win.SetResourceReference(StyleProperty, "Caption");
            Grid.SetColumn(win, 2);

            grid.Children.Add(cb);
            grid.Children.Add(mid);
            grid.Children.Add(win);
            return grid;
        }

        /// <summary>
        /// Снять подписку страницы проверки и стереть накопленное.
        ///
        /// Важно не только при уходе со страницы, но и когда окно прячут: закрытие окна
        /// программу не выключает, а список последних нажатий — упорядоченная история
        /// с человеческими именами клавиш. Оставленная в спрятанном окне, она через час
        /// показала бы набранный за это время пароль тому, кто снова откроет окно.
        /// </summary>
        private void DetachSelfTest()
        {
            if (_selfTest != null)
            {
                KeyWatch.Activity -= _selfTest;
                _selfTest = null;
            }
            _selfTestLog = null;
            _selfTestChecks = null;
            _selfTestLines.Clear();
        }

        private UIElement PageSelfTest()
        {
            var stack = new StackPanel();

            AppleDriver.Refresh(false);
            bool yielding = YieldingNow;

            stack.Children.Add(Card("Как это читать", null,
                Note("Нажимайте клавиши по одной — ниже появится, что именно дошло до Windows. " +
                     "Проверка слушает сырой ввод, поэтому видит клавиатуру напрямую и не зависит " +
                     "ни от переназначений программы, ни от того, какое окно сейчас впереди.\n\n" +
                     "Если нажатие не даёт строки совсем — значит до Windows не дошло ничего. " +
                     "Это не поломка: так ведут себя клавиши, которые обрабатываются внутри " +
                     "клавиатуры или внутри драйвера и наружу не выходят." +
                     (yielding
                        ? "\n\nСейчас функциональный ряд отдан драйверу Apple. Поэтому F1–F12 " +
                          "должны приходить готовыми медиакодами, а настоящие F-клавиши — " +
                          "только вместе с Fn."
                        : "\n\nСейчас функциональный ряд обрабатывает сама программа, поэтому " +
                          "F1–F12 должны приходить обычными F-клавишами."))));

            _selfTestLog = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
            _selfTestLog.SetResourceReference(StyleProperty, "Caption");
            stack.Children.Add(Card("Что приходит", null, _selfTestLog));

            _selfTestChecks = new StackPanel();
            stack.Children.Add(Card("Что уже замечено", null, _selfTestChecks));

            var reset = new Button { Content = "Начать заново", HorizontalAlignment = HorizontalAlignment.Left };
            reset.SetResourceReference(StyleProperty, "Btn");
            reset.Click += delegate
            {
                KeyWatch.Forget();
                _selfTestLines.Clear();
                RefreshSelfTest();
            };
            stack.Children.Add(reset);

            _selfTest = delegate(KeyWatch.KeyEvent e)
            {
                // Событие приходит из потока переписи — рисовать можно только из своего.
                Dispatcher.BeginInvoke((Action)delegate { OnSelfTestEvent(e); });
            };
            KeyWatch.Activity += _selfTest;

            RefreshSelfTest();
            return stack;
        }

        private void OnSelfTestEvent(KeyWatch.KeyEvent e)
        {
            if (_selfTestLog == null) return;
            string line = e.Media
                ? "медиакод " + e.Code.ToString("X2") + " — " + Native.UsageName(e.Code)
                : "клавиша " + Vk.Name(e.Code) + "   vk=" + e.Code.ToString("X2")
                  + ", скан-код " + e.ScanCode.ToString("X2");
            if (e.Fresh) line += "   · впервые";

            _selfTestLines.Insert(0, line);
            while (_selfTestLines.Count > 24) _selfTestLines.RemoveAt(_selfTestLines.Count - 1);
            RefreshSelfTest();
        }

        private void RefreshSelfTest()
        {
            if (_selfTestLog == null || _selfTestChecks == null) return;

            _selfTestLog.Text = _selfTestLines.Count == 0
                ? "Пока ничего. Нажмите любую клавишу на Magic Keyboard."
                : String.Join("\n", _selfTestLines.ToArray());

            _selfTestChecks.Children.Clear();

            int keys = Models.FunctionKeyCount(_s);
            var frow = new System.Text.StringBuilder();
            int fseen = 0;
            for (int i = 0; i < keys; i++)
            {
                bool ok = KeyWatch.Seen(Vk.F1 + i);
                if (ok) fseen++;
                if (i > 0) frow.Append("   ");
                frow.Append(ok ? "✓ F" : "· F").Append(i + 1);
            }
            _selfTestChecks.Children.Add(KeyValue("Функциональный ряд", fseen + " из " + keys));
            _selfTestChecks.Children.Add(Note(frow.ToString()));

            int[] usages = KeyWatch.AllUsages();
            var media = new System.Text.StringBuilder();
            foreach (int u in usages)
            {
                if (media.Length > 0) media.Append(",   ");
                media.Append(Native.UsageName(u));
            }
            _selfTestChecks.Children.Add(KeyValue("Медиакоды",
                usages.Length == 0 ? "ни одного" : usages.Length.ToString()));
            if (usages.Length > 0) _selfTestChecks.Children.Add(Note(media.ToString()));

            var special = new System.Text.StringBuilder();
            special.Append(KeyWatch.EjectSeen ? "✓ Eject" : "· Eject");
            special.Append("   ").Append(KeyWatch.Seen(Vk.NumLock) ? "✓ Clear" : "· Clear");
            special.Append("   ").Append(KeyWatch.Seen(Vk.Clear) ? "✓ = на блоке" : "· = на блоке");
            special.Append("   ").Append(KeyWatch.Seen(Vk.LWin) || KeyWatch.Seen(Vk.RWin) ? "✓ ⌘" : "· ⌘");
            special.Append("   ").Append(KeyWatch.Seen(Vk.LMenu) || KeyWatch.Seen(Vk.RMenu) ? "✓ ⌥" : "· ⌥");
            special.Append("   ").Append(KeyWatch.Seen(Vk.Capital) ? "✓ Caps Lock" : "· Caps Lock");
            _selfTestChecks.Children.Add(KeyValue("Особые клавиши", ""));
            _selfTestChecks.Children.Add(Note(special.ToString()));
        }

        private UIElement PageDriver()
        {
            var stack = new StackPanel();
            AppleDriver.Refresh(true);
            bool installed = AppleDriver.Installed;

            var status = new StackPanel();
            status.Children.Add(KeyValue("Драйвер Apple", installed ? "установлен" : "не установлен"));
            if (installed)
            {
                status.Children.Add(KeyValue("Состояние", AppleDriver.Active ? "включена" : "отключена"));
                int fb = AppleDriver.FnBehavior;
                if (fb >= 0)
                    status.Children.Add(KeyValue("OSXFnBehavior",
                        fb + (fb == 1 ? " — как на маке: медиа сразу" : " — наоборот: F-клавиши сразу")));
                if (!String.IsNullOrEmpty(AppleDriver.FnBehaviorPath))
                    status.Children.Add(KeyValue("Где это лежит", AppleDriver.FnBehaviorPath));

                // Самое важное здесь — не что записано в реестре, а что происходит на деле.
                int seen = KeyWatch.AllUsages().Length;
                status.Children.Add(KeyValue("Преобразует ряд на деле",
                    KeyWatch.MediaSeen
                        ? "да — медиакодов замечено: " + seen
                        : "не замечено ни одного медиакода"));
            }
            stack.Children.Add(Card("Что найдено", null, status));

            if (installed)
            {
                int fb = AppleDriver.FnBehavior;
                var modes = new StackPanel();
                modes.Children.Add(PresetButton(
                    "Медиа сразу — как на маке" + (fb != 0 ? "  ·  сейчас так" : ""),
                    "F1–F12 сами дают громкость и перемотку, обычные F-клавиши — через Fn. " +
                    "Делает это драйвер; MagicKeys функциональный ряд не трогает. " +
                    "Яркость при этом пропадает: F1 и F2 драйвер съедает и наружу не отдаёт.",
                    delegate { ApplyFnBehavior(1); }));
                modes.Children.Add(PresetButton(
                    "F-клавиши сразу" + (fb == 0 ? "  ·  сейчас так" : ""),
                    "F1–F12 приходят обычными F-клавишами, и переназначает их MagicKeys — " +
                    "тогда доступна и яркость внешних мониторов, которой у драйвера нет.",
                    delegate { ApplyFnBehavior(0); }));
                if (!String.IsNullOrEmpty(_fnNotice))
                {
                    var outcome = new TextBlock
                    {
                        Text = _fnNotice,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 10, 0, 0)
                    };
                    outcome.SetResourceReference(StyleProperty, "Caption");
                    modes.Children.Add(outcome);
                }

                if (AppleDriver.TakesFunctionRow && !KeyWatch.MediaSeen)
                {
                    stack.Children.Add(Card("Драйвер настроен забирать ряд, но пока молчит", null,
                        Note("В реестре сказано, что медиаклавиши делает драйвер, однако ни одного " +
                             "медиакода с клавиатуры ещё не приходило. Так бывает, когда драйвер " +
                             "не подключился к тому пути, по которому клавиатура работает сейчас — " +
                             "например, поставлен для USB, а клавиатура на Bluetooth.\n\n" +
                             "Пока это так, программа ряд себе не отдаёт молча: она берёт F1–F12 " +
                             "на себя, чтобы они не оказались мёртвыми. Как только придёт первый " +
                             "медиакод, она снова уступит драйверу.\n\n" +
                             "Проверить, что именно приходит, можно на странице «Проверка клавиш» — " +
                             "одного нажатия достаточно."),
                        LinkButton("Перейти к проверке клавиш", delegate { GoTo("selftest"); })));
                }

                stack.Children.Add(Card("Кто занимается функциональным рядом", null,
                    Note("Замерено на этой машине с работающим драйвером. При OSXFnBehavior = 1 " +
                         "нажатие F8 приходит уже готовым медиакодом «пуск/пауза», F12 — «громче», " +
                         "и оба помечены как подставленные: до перехвата MagicKeys они не доходят " +
                         "вовсе. Fn при этом наконец работает — Fn+F1 даёт настоящий F1, чего без " +
                         "драйвера добиться было нельзя.\n\n" +
                         "А вот F1 и F2 не дают ничего — и теряет их не Windows, а сам драйвер: " +
                         "коллекция, через которую он выдаёт медиакоды, при нажатии F1 молчит, " +
                         "хотя коды яркости в ней предусмотрены. Похоже, драйвер рассчитан " +
                         "на экран мака и на обычном ПК просто не находит, чему их адресовать.\n\n" +
                         "Отсюда и выбор. Нужна яркость внешних мониторов — берите второй режим: " +
                         "MagicKeys умеет её по DDC/CI, а драйвер нет. Хватает медиаклавиш — первый " +
                         "честнее, потому что драйвер привязан к самой клавиатуре, а перехват " +
                         "действует на весь ввод сразу."),
                    modes));
            }

            if (installed)
            {
                var tuned = new StackPanel();
                tuned.Children.Add(LinkButton("Настроить программу под драйвер", ApplyDriverProfile));
                if (!String.IsNullOrEmpty(_tuneNotice))
                {
                    var t = new TextBlock
                    {
                        Text = _tuneNotice,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 19,
                        Margin = new Thickness(0, 10, 0, 0)
                    };
                    t.SetResourceReference(StyleProperty, "Caption");
                    tuned.Children.Add(t);
                }

                stack.Children.Add(Card("Разделение труда", null,
                    Note("Драйвер умеет то, чего перехват не может в принципе: он привязан к самой " +
                         "клавиатуре, поэтому не задевает вторую, и он видит настоящую клавишу Fn. " +
                         "Программа умеет то, чего нет у драйвера: яркость внешних мониторов, " +
                         "цифровой блок Apple, свободные переназначения и раскладки для языков, " +
                         "которых у Apple нет.\n\n" +
                         "Эта кнопка расставляет настройки так, чтобы каждый занимался своим и " +
                         "никто не делал чужого. Что именно меняется — будет написано ниже, " +
                         "без сюрпризов."),
                    tuned));
            }

            stack.Children.Add(Card("Уживание", null,
                Toggle("Уступать функциональный ряд драйверу Apple, когда тот его забирает",
                    _s.YieldToAppleDriver, delegate(bool v) { _s.YieldToAppleDriver = v; Save(); }),
                Note("Драйвер и программа делают одно и то же — переназначают F1–F12. Если работают " +
                     "оба сразу, нажатие переназначается дважды и получается ерунда. С этой настройкой " +
                     "программа отдаёт функциональный ряд драйверу, а всё остальное — модификаторы, " +
                     "раскладки, навигацию Fn, цифровой блок, яркость — продолжает делать сама.\n\n" +
                     "Уступает она с разбором: только когда драйвер ряд действительно забирает. " +
                     "В режиме «F-клавиши сразу» уступать нечего, и тогда F1–F12 переназначает " +
                     "MagicKeys, даже если настройка включена.")));

            stack.Children.Add(Card("Что тут можно, а что нельзя", null,
                Note("Вложить файлы Apple внутрь программы нельзя: они несвободные, а MagicKeys " +
                     "выпущен под GNU GPL — так нарушилась бы лицензия самой программы.\n\n" +
                     "А вот забрать их с серверов Apple и поставить на этой машине — можно, и именно " +
                     "это программа и делает по кнопке ниже. Никакого распространения чужих файлов " +
                     "здесь нет: тем же путём ходит открытая утилита brigadier.\n\n" +
                     "Лицензия Apple на ПО Boot Camp разрешает использование только на технике Apple. " +
                     "Решение поставить его на обычный ПК — ваше, программа за вас его не принимает " +
                     "и ничего не качает без нажатия кнопки.")));

            // ---- добыча и установка ----
            string sevenZip = AppleDriverSetup.SevenZip();
            _setupLog = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
            _setupLog.SetResourceReference(StyleProperty, "Caption");
            _setupLog.Text = sevenZip != null
                ? "7-Zip найден: " + sevenZip
                : "7-Zip не найден. Он нужен для распаковки: внутри пакета Apple лежит образ DMG, "
                  + "а его встроенные средства Windows читать не умеют. В программу он не вложен, "
                  + "но она может забрать официальный установщик сама — проверив подпись и ничего "
                  + "не устанавливая в систему. Это произойдёт по кнопке «Скачать и установить».";

            var buttons = new WrapPanel { Margin = new Thickness(0, 0, -8, -8) };

            var find = new Button { Content = "Найти пакет у Apple", Margin = new Thickness(0, 0, 8, 8) };
            find.SetResourceReference(StyleProperty, "Btn");
            find.Click += delegate { StartSetup(false, null); };
            buttons.Children.Add(find);

            var get = new Button { Content = "Скачать и установить", Margin = new Thickness(0, 0, 8, 8) };
            get.SetResourceReference(StyleProperty, "BtnAccent");
            get.Click += delegate { StartSetup(true, null); };
            buttons.Children.Add(get);

            var pick = new Button { Content = "Указать распакованную папку…", Margin = new Thickness(0, 0, 8, 8) };
            pick.SetResourceReference(StyleProperty, "Btn");
            pick.Click += delegate
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "Папка с распакованным Boot Camp";
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        StartSetup(true, dlg.SelectedPath);
                }
            };
            buttons.Children.Add(pick);

            long cached = AppleDriverSetup.CacheSize();
            if (cached > 0)
            {
                var wipe = new Button
                {
                    Content = "Убрать скачанное и распакованное (" + (cached / 1024 / 1024) + " МБ)",
                    Margin = new Thickness(0, 0, 8, 8)
                };
                wipe.SetResourceReference(StyleProperty, "Btn");
                wipe.IsEnabled = _setupThread == null || !_setupThread.IsAlive;
                wipe.Click += delegate
                {
                    if (_setupThread != null && _setupThread.IsAlive)
                    {
                        SetupSay("Сейчас идёт работа — дождитесь конца, иначе распаковка " +
                                 "останется без 7-Zip посреди дела.");
                        return;
                    }
                    string err;
                    bool ok = AppleDriverSetup.ClearCache(out err);
                    SetupSay(ok
                        ? "Скачанное и распакованное убрано. На установленный драйвер это не влияет."
                        : "Убрать удалось не всё: " + err +
                          "\n\nОбычно это значит, что какой-то файл сейчас открыт другой программой.");
                    BuildPage();
                };
                buttons.Children.Add(wipe);
            }

            if (sevenZip == null)
            {
                var get7z = new Button { Content = "Открыть страницу 7-Zip", Margin = new Thickness(0, 0, 8, 8) };
                get7z.SetResourceReference(StyleProperty, "Btn");
                get7z.Click += delegate
                {
                    try { Process.Start(new ProcessStartInfo("https://www.7-zip.org/") { UseShellExecute = true }); }
                    catch { }
                };
                buttons.Children.Add(get7z);
            }

            if (installed)
            {
                var remove = new Button { Content = "Удалить драйвер", Margin = new Thickness(0, 0, 8, 8) };
                remove.SetResourceReference(StyleProperty, "Btn");
                remove.Click += delegate
                {
                    string output;
                    bool ok = AppleDriverSetup.Uninstall("keymagic2.inf", out output);
                    SetupSay((ok ? "Драйвер удалён."
                                 : "Удалить не вышло — возможно, отклонён запрос прав администратора.")
                             + "\n\n" + output);
                    AppleDriver.Refresh(true);
                };
                buttons.Children.Add(remove);
            }

            stack.Children.Add(Card("Добыть и установить",
                "Файлы Apple забираются с её же серверов и никуда не распространяются.",
                buttons, _setupLog,
                Note("Пакет весит около 700 МБ, а при распаковке нужна ещё пара гигабайт. " +
                     "Качается он в " + AppleDriverSetup.CacheFolder + " и докачивается, если оборвётся. " +
                     "Установку драйвера выполняет pnputil — Windows спросит права администратора.")));

            stack.Children.Add(Card("Как его добыть вручную", null,
                Bullet("Пакет Boot Camp Support Software Apple отдельной ссылкой не публикует: его " +
                       "забирает Ассистент Boot Camp на маке либо свободная утилита brigadier, которая " +
                       "скачивает пакет прямо с серверов Apple."),
                Bullet("Нужная часть внутри — папка BootCamp\\Drivers\\Apple\\AppleKeyboardMagic2. " +
                       "Устанавливается правым щелчком по Keymagic2.inf → «Установить»."),
                Bullet("После установки клавиатуру стоит переподключить. Служба появится в реестре " +
                       "под именем KeyMagic2, и программа это заметит сама."),
                Bullet("Поведение Fn у драйвера переворачивается значением OSXFnBehavior в его ветке " +
                       "реестра: 1 — медиа сразу, 0 — наоборот. Настройка чужая, поэтому программа " +
                       "меняет её только по нажатию кнопки выше и только с правами администратора: " +
                       "их спросит Windows."),
                Note("Проверено на этой машине: сейчас службы KeyMagic2 нет. Из следов Apple найдены " +
                     "только AppleLowerFilter и AppleSSD — они от другого программного обеспечения " +
                     "и к клавиатуре отношения не имеют.")));

            return stack;
        }

        /// <summary>Строка о ходе добычи драйвера — обновляется из фонового потока.</summary>
        private TextBlock _setupLog;
        private Thread _setupThread;

        private void SetupSay(string text)
        {
            Dispatcher.BeginInvoke((Action)delegate
            {
                if (_setupLog != null) _setupLog.Text = text;
            });
        }

        /// <summary>
        /// Весь путь: каталог Apple → скачивание → распаковка → установка.
        /// Если задана готовая папка, первые два шага пропускаются.
        /// </summary>
        private void StartSetup(bool install, string readyFolder)
        {
            if (_setupThread != null && _setupThread.IsAlive) { SetupSay("Уже идёт работа, дождитесь конца."); return; }

            _setupThread = new Thread(delegate()
            {
                try
                {
                    string inf = null, error = null;

                    if (readyFolder != null)
                    {
                        SetupSay("Ищу файл драйвера в " + readyFolder + "…");
                        inf = AppleDriverSetup.FindInf(readyFolder);
                        if (inf == null) { SetupSay("В этой папке файла драйвера клавиатуры нет."); return; }
                    }
                    else
                    {
                        SetupSay("Спрашиваю каталог обновлений Apple…");
                        string url, posted;
                        if (!AppleDriverSetup.FindNewestPackage(out url, out posted, out error))
                        { SetupSay("Каталог недоступен: " + error); return; }

                        long size = AppleDriverSetup.SizeOf(url);
                        string head = "Самый свежий пакет Boot Camp: от " + posted +
                                      (size > 0 ? ", " + (size / 1024 / 1024) + " МБ" : "") + ".\n" + url;
                        SetupSay(head);
                        if (!install) return;

                        string sevenZip = AppleDriverSetup.SevenZip();
                        if (sevenZip == null)
                        {
                            SetupSay(head + "\n\n7-Zip не найден — забираю официальный установщик…");
                            sevenZip = AppleDriverSetup.FetchSevenZip(
                                delegate(double part, string what) { SetupSay(head + "\n\n" + what); },
                                out error);
                            if (sevenZip == null)
                            {
                                SetupSay(head + "\n\nБез 7-Zip распаковать нечем: " + error +
                                         "\n\nМожно поставить 7-Zip самостоятельно — кнопка ниже открывает его страницу.");
                                return;
                            }
                        }

                        string package = System.IO.Path.Combine(AppleDriverSetup.CacheFolder, "BootCampESD.pkg");
                        if (!AppleDriverSetup.Download(url, package,
                                delegate(double part, string what) { SetupSay(head + "\n\n" + what); },
                                null, out error))
                        { SetupSay(head + "\n\nСкачать не вышло: " + error); return; }

                        SetupSay(head + "\n\nРаспаковываю…");
                        inf = AppleDriverSetup.ExtractUntilInf(sevenZip, package,
                                System.IO.Path.Combine(AppleDriverSetup.CacheFolder, "unpacked"),
                                delegate(double part, string what) { SetupSay(head + "\n\n" + what); },
                                out error);
                        if (inf == null) { SetupSay(head + "\n\nРаспаковка не дала драйвера: " + error); return; }
                    }

                    SetupSay("Найден драйвер: " + inf + "\n\nСтавлю через pnputil — Windows спросит права…");
                    string output;
                    bool ok = AppleDriverSetup.Install(inf, out output);
                    AppleDriver.Refresh(true);

                    string tail = ok
                        ? "Готово. Драйвер установлен: " + inf +
                          "\n\nПереподключите клавиатуру, чтобы он вступил в силу."
                        : "Установить не удалось. Чаще всего это значит, что запрос прав администратора " +
                          "был отклонён — тогда в системе ничего не изменилось. Можно повторить " +
                          "или поставить вручную: правый щелчок по Keymagic2.inf → «Установить».";
                    if (!String.IsNullOrEmpty(output)) tail += "\n\n" + output.Trim();
                    SetupSay(tail);

                    // По ключу, а не по номеру: номер остался от прежней нумерации
                    // и после появления режима разработчика указывал уже на другую
                    // страницу — так что после установки драйвера она не обновлялась
                    // никогда, и человек видел прежнее «не установлен».
                    Dispatcher.BeginInvoke((Action)delegate { if (CurrentPage == "driver") BuildPage(); });
                }
                catch (Exception e)
                {
                    SetupSay("Работа прервалась: добыть драйвер не удалось.\n\n" +
                             "Что сообщила система: " + e.Message + "\n\n" +
                             "Можно скачать пакет Boot Camp самостоятельно и указать " +
                             "распакованную папку кнопкой выше.");
                }
            });
            _setupThread.IsBackground = true;
            _setupThread.Start();
        }

        private UIElement PageAbout()
        {
            var stack = new StackPanel();

            Border about = Card("MagicKeys", "Версия 1.0",
                Note("Свободная программа: её можно использовать, изучать, изменять и передавать " +
                     "дальше на условиях GNU General Public License версии 3 или любой более поздней. " +
                     "Программа распространяется без всяких гарантий. Текст лицензии — в файле LICENSE " +
                     "рядом с программой."));
            // Пять щелчков по этой карточке включают режим разработчика — приём старый
            // и намеренно неочевидный: обычному человеку эти настройки только мешают.
            about.MouseLeftButtonUp += delegate
            {
                if (++_aboutClicks < 5) return;
                _aboutClicks = 0;
                _s.DeveloperMode = !_s.DeveloperMode;
                Save();
                FillNav();
                BuildPage();
            };
            stack.Children.Add(about);

            stack.Children.Add(Card("Что она делает", null,
                Bullet("Возвращает F1–F12 те функции, что напечатаны на клавишах Apple: яркость, " +
                       "громкость, управление воспроизведением."),
                Bullet("Переставляет модификаторы, чтобы ⌘ работала как Ctrl (или наоборот — чтобы " +
                       "Ctrl, Win и Alt встали на привычные для Windows места)."),
                Bullet("Исправляет перестановку двух клавиш ISO-раскладки Apple."),
                Bullet("Управляет яркостью внешних мониторов — сама Windows этого не умеет.")));

            stack.Children.Add(Card("Чего она не делает", null,
                Bullet("Не видит клавишу Fn сама: Magic Keyboard её в Windows не отправляет. " +
                       "Fn работает только с установленным драйвером Apple."),
                Bullet("Не различает клавиатуры: низкоуровневый перехват в Windows не сообщает " +
                       "устройство, поэтому правила действуют на весь ввод."),
                Bullet("Не видит клавишу Eject (извлечение, верхний правый угол): она обрабатывается " +
                       "внутри клавиатуры и наружу не выходит ни по одному каналу — проверено."),
                Bullet("Ничего не меняет в системе без спроса: только свой файл настроек и, " +
                       "по желанию, запись автозапуска. Драйвер Apple ставится отдельной кнопкой.")));

            if (_s.DeveloperMode)
            {
                stack.Children.Add(Card("Режим разработчика", "Включён",
                    Toggle("Показывать служебные страницы и настройки", _s.DeveloperMode,
                        delegate(bool v) { _s.DeveloperMode = v; Save(); FillNav(); BuildPage(); }),
                    Note("Открывает «Устройства» и «Проверку клавиш», а на остальных страницах — " +
                         "исполнение клавиатуры, японские клавиши, третий уровень раскладки, " +
                         "подмену всех клавиш и номера языков ввода.\n\n" +
                         "Журнал сюда не относится: он пишется только при запуске с ключом --log.\n\n" +
                         "Включается пятью щелчками по карточке с названием программы или ключом " +
                         "--dev при запуске.")));
            }

            return stack;
        }

        // ------------------------------------------------------------------
        //  Строительные блоки
        // ------------------------------------------------------------------

        private void Save()
        {
            if (_building) return;
            _apply();
        }

        private static Border Card(string title, string subtitle, params UIElement[] children)
        {
            var card = new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Padding = new Thickness(18, 15, 18, 16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var stack = new StackPanel();
            if (!String.IsNullOrEmpty(title))
            {
                var t = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14 };
                stack.Children.Add(t);
            }
            if (!String.IsNullOrEmpty(subtitle))
            {
                var s = new TextBlock { Text = subtitle, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
                s.SetResourceReference(StyleProperty, "Caption");
                stack.Children.Add(s);
            }
            foreach (UIElement child in children)
            {
                if (child is FrameworkElement)
                {
                    FrameworkElement fe = (FrameworkElement)child;
                    fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 12, fe.Margin.Right, fe.Margin.Bottom);
                }
                stack.Children.Add(child);
            }
            card.Child = stack;
            return card;
        }

        private static UIElement Row(string label, string hint, UIElement control)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
            if (!String.IsNullOrEmpty(hint))
            {
                var h = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                h.SetResourceReference(StyleProperty, "Caption");
                left.Children.Add(h);
            }
            Grid.SetColumn(left, 0);
            Grid.SetColumn(control, 1);
            grid.Children.Add(left);
            grid.Children.Add(control);
            return grid;
        }

        private void AddModRow(Grid grid, int row, string label, string hint, ModKey current, Action<ModKey> onSet)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 16, 6) };
            left.Children.Add(new TextBlock { Text = label, FontSize = 15 });
            var h = new TextBlock { Text = hint, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
            h.SetResourceReference(StyleProperty, "Caption");
            left.Children.Add(h);
            Grid.SetRow(left, row); Grid.SetColumn(left, 0);

            var choices = new List<Choice>();
            foreach (ModKey m in new[]
            {
                ModKey.LCtrl, ModKey.RCtrl, ModKey.LWin, ModKey.RWin,
                ModKey.LAlt, ModKey.RAlt, ModKey.LShift, ModKey.RShift,
                ModKey.CapsLock, ModKey.Escape, ModKey.None
            })
                choices.Add(new Choice { Value = m, Text = ModNames.Of(m) });

            var box = Combo(choices.ToArray(), current, delegate(object v) { onSet((ModKey)v); });
            box.Margin = new Thickness(0, 6, 0, 6);
            Grid.SetRow(box, row); Grid.SetColumn(box, 1);

            grid.Children.Add(left);
            grid.Children.Add(box);
        }

        /// <summary>Строка «название клавиши — что она делает» для отдельной клавиши.</summary>
        private void AddSingleRow(Grid grid, int row, string label, string hint, string current, Action<string> onSet)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 16, 6) };
            left.Children.Add(new TextBlock { Text = label, FontSize = 15 });
            var h = new TextBlock { Text = hint, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
            h.SetResourceReference(StyleProperty, "Caption");
            left.Children.Add(h);
            Grid.SetRow(left, row); Grid.SetColumn(left, 0);

            var box = ActionCombo(current, onSet);
            box.Margin = new Thickness(0, 6, 0, 6);
            Grid.SetRow(box, row); Grid.SetColumn(box, 1);

            grid.Children.Add(left);
            grid.Children.Add(box);
        }

        private ComboBox ActionCombo(string currentId, Action<string> onSet)
        {
            var box = new ComboBox { Style = (Style)Application.Current.Resources["Combo"] };
            int selected = 0, i = 0;
            foreach (KeyAction a in Actions.All)
            {
                box.Items.Add(a);
                if (a.Id == currentId) selected = i;
                i++;
            }
            box.SelectedIndex = selected;
            box.SelectionChanged += delegate
            {
                if (_building) return;
                KeyAction a = box.SelectedItem as KeyAction;
                if (a != null) onSet(a.Id);
            };
            return box;
        }

        private ComboBox Combo(Choice[] choices, object current, Action<object> onSet)
        {
            var box = new ComboBox { Style = (Style)Application.Current.Resources["Combo"] };
            int selected = 0;
            for (int i = 0; i < choices.Length; i++)
            {
                box.Items.Add(choices[i]);
                if (Equals(choices[i].Value, current)) selected = i;
            }
            box.SelectedIndex = selected;
            box.SelectionChanged += delegate
            {
                if (_building) return;
                Choice c = box.SelectedItem as Choice;
                if (c != null) onSet(c.Value);
            };
            return box;
        }

        private CheckBox Toggle(string text, bool value, Action<bool> onSet)
        {
            var cb = new CheckBox
            {
                Style = (Style)Application.Current.Resources["Switch"],
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                // Без растяжения переключатель сжимается по содержимому,
                // и длинная подпись обрезается вместо переноса.
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsChecked = value
            };
            cb.Checked += delegate { if (!_building) onSet(true); };
            cb.Unchecked += delegate { if (!_building) onSet(false); };
            return cb;
        }

        private Button PresetButton(string title, string hint, Action onClick)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            var h = new TextBlock
            {
                Text = hint,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19
            };
            h.SetResourceReference(StyleProperty, "Caption");
            content.Children.Add(h);

            var b = new Button
            {
                Content = content,
                Height = double.NaN,
                // Без выравнивания по ширине кнопка сжимается по содержимому,
                // и длинная подсказка обрезается вместо переноса.
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 10, 14, 11),
                Margin = new Thickness(0, 0, 8, 8)
            };
            b.SetResourceReference(StyleProperty, "Btn");
            b.Click += delegate { onClick(); };
            return b;
        }

        private static TextBlock Note(string text)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
            t.SetResourceReference(StyleProperty, "Caption");
            return t;
        }

        private static UIElement Bullet(string text)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = new TextBlock { Text = "—", VerticalAlignment = VerticalAlignment.Top };
            dot.SetResourceReference(StyleProperty, "Caption");
            var body = Note(text);
            body.Margin = new Thickness(0);
            Grid.SetColumn(dot, 0);
            Grid.SetColumn(body, 1);
            grid.Children.Add(dot);
            grid.Children.Add(body);
            return grid;
        }

        /// <summary>
        /// Переключает режим функционального ряда у драйвера Apple. Итог кладётся
        /// в поле, а не в надпись: страница после этого пересобирается, чтобы
        /// отметка «сейчас так» переехала на выбранный режим.
        /// </summary>
        private void ApplyFnBehavior(int value)
        {
            string error;
            if (AppleDriver.SetFnBehavior(value, out error))
            {
                _fnNotice = "Записано. Драйвер перечитает значение при следующем подключении "
                          + "клавиатуры — переподключите её или перезагрузитесь, до этого ряд "
                          + "работает по-старому.";
                if (_apply != null) _apply();
            }
            else
            {
                _fnNotice = "Не вышло: " + error + ". Значение лежит в HKLM, поэтому нужны права "
                          + "администратора; если запрос был отклонён — ничего не изменилось.";
            }
            BuildPage();
        }

        /// <summary>
        /// Расставляет настройки так, чтобы программа и драйвер не делали одно и то же.
        /// Меняет только то, что действительно мешает, и обо всём отчитывается: молчаливая
        /// перенастройка чужих настроек — худшее, что программа может сделать.
        /// </summary>
        private void ApplyDriverProfile()
        {
            var done = new List<string>();

            if (!_s.YieldToAppleDriver)
            {
                _s.YieldToAppleDriver = true;
                done.Add("• Функциональный ряд отдан драйверу — он с ним справляется лучше, "
                       + "потому что привязан к самой клавиатуре.");
            }

            if (AppleDriver.TakesFunctionRow)
            {
                if (_s.FnSubstitute != ModKey.None)
                {
                    _s.FnSubstitute = ModKey.None;
                    done.Add("• Заменитель Fn выключен: с драйвером работает настоящая клавиша Fn "
                           + "(замерено — Fn+F1 даёт настоящий F1), и подменять её больше нечем.");
                }
                if (_s.FnNavigation)
                {
                    _s.FnNavigation = false;
                    done.Add("• Навигация Fn+стрелки выключена: её должен взять на себя драйвер. "
                           + "Проверьте Fn+← — если в начало строки не уходит, включите её "
                           + "обратно на странице «Функциональные клавиши».");
                }
            }

            // Раскладок здесь не касаемся. Драйвер приносит в систему языки ввода
            // «(Apple)», и когда-то программа предлагала переходить на них, а свою
            // подмену символов выключать. От этого отказались: Windows 10 и 11
            // работают с такими раскладками плохо и сами возвращают в список старые.
            // Подмена на лету ничего в систему не ставит и потому этого изъяна лишена.

            if (done.Count == 0)
            {
                _tuneNotice = "Менять нечего: настройки уже согласованы с драйвером.";
            }
            else
            {
                _tuneNotice = "Изменено:\n" + String.Join("\n", done.ToArray())
                            + "\n\nОстальное программа продолжает делать сама: модификаторы, "
                            + "цифровой блок Apple, яркость внешних мониторов по DDC/CI и ⌘+Tab.";
                Save();
            }
            BuildPage();
        }

        /// <summary>
        /// Уступает ли программа функциональный ряд драйверу прямо сейчас. Мало того,
        /// что драйвер установлен и настроен забирать ряд, — надо ещё видеть, что он
        /// это делает. Иначе получается провал: драйвер молчит, программа отошла,
        /// и нажатие не даёт ничего.
        /// </summary>
        private bool YieldingNow
        {
            get { return _s.YieldToAppleDriver && AppleDriver.TakesFunctionRow && KeyWatch.MediaSeen; }
        }

        /// <summary>Кнопка-переход на другую страницу.</summary>
        private Button LinkButton(string text, Action onClick)
        {
            var b = new Button
            {
                Content = text,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0)
            };
            b.SetResourceReference(StyleProperty, "Btn");
            b.Click += delegate { onClick(); };
            return b;
        }

        private static UIElement KeyValue(string key, string value)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var k = new TextBlock { Text = key };
            k.SetResourceReference(StyleProperty, "Caption");
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(k, 0);
            Grid.SetColumn(v, 1);
            grid.Children.Add(k);
            grid.Children.Add(v);
            return grid;
        }
    }
}
