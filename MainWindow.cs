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
            SnapsToDevicePixels = true;
            // Прячут окно — снимаем подписку, показывают — собираем страницу заново.
            // Без второй половины страница проверки после закрытия и открытия оставалась
            // на месте, но не отзывалась ни на одно нажатие.
            IsVisibleChanged += delegate
            {
                if (IsVisible) { if (CurrentPage == "diag") BuildPage(); }
                else ForgetSelfTest();
            };
            // Сворачивание IsVisible не гасит, а окно при этом с глаз ушло: историю
            // нажатий копить в нём так же ни к чему.
            StateChanged += delegate
            {
                if (WindowState == WindowState.Minimized) ForgetSelfTest();
                else if (CurrentPage == "diag" && _selfTest == null) BuildPage();
            };
            // И при потере фокуса. Окно может быть видно — на втором мониторе, сбоку, —
            // пока человек печатает в другой программе. Список последних нажатий тогда
            // копился бы дальше, и пароль оказался бы на экране у всех на виду.
            Deactivated += delegate { ForgetSelfTest(); };
            Activated += delegate { if (CurrentPage == "diag" && _selfTest == null) BuildPage(); };
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
        /// Что показывалось в прошлый раз — чтобы не пересобирать страницу зря.
        /// </summary>
        private string _deviceMark;

        /// <summary>
        /// Обновляет строку об устройствах в подвале и пересобирает страницу: пришедшая
        /// клавиатура меняет и подписи клавиш, и заводские назначения верхнего ряда.
        /// </summary>
        public void RefreshDevices()
        {
            IList<KeyboardInfo> all = Devices.Known;
            int apple = 0;
            foreach (KeyboardInfo k in all) if (k.IsApple) apple++;

            string failure = _engine.Failure;
            if (!String.IsNullOrEmpty(failure))
                _deviceLine.Text = "MagicKeys не получает нажатия — переназначения не работают. Перезапустите программу.";
            else if (apple > 0)
                _deviceLine.Text = "Magic Keyboard подключена.";
            else
                _deviceLine.Text = "Клавиатура Apple не найдена. Клавиатур в системе: " + all.Count + ".";

            // Перестраиваем, только когда на экране правда что-то поменялось. Событий
            // приходит много — каждая впервые нажатая клавиша, каждый ответ о заряде,
            // каждый опрос устройств, — а перестройка стирает список на странице проверки
            // и роняет раскрытый список под указателем.
            string now = all.Count + "|" + apple + "|" + KeyWatch.MaxFunctionKey + "|"
                       + (int)KeyWatch.DetectedPhysical + "|" + KeyboardBattery.Percent + "|"
                       + (Devices.AppleModel == null ? "" : Devices.AppleModel.Name) + "|"
                       // Медиакоды и ⏏ страницы показывают не меньше, чем клавиатуры:
                       // от первого медиакода зависит, кто занимается верхним рядом.
                       + KeyWatch.MediaSeen + "|" + KeyWatch.EjectSeen + "|"
                       + KeyWatch.AllUsages().Length + "|" + AppleDriver.TakesFunctionRow;
            if (now == _deviceMark) return;
            // Отметку ставим по факту сборки, а не по факту вызова: BuildPage умеет
            // промолчать, если сборка уже идёт, — и тогда изменение потерялось бы
            // навсегда, потому что отметка уже новая, а страница ещё старая.
            if (BuildPage()) _deviceMark = now;
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
                ? _pages[_nav.SelectedIndex] : "mac";

            _nav.Items.Clear();
            _pages.Clear();

            // Порядок — по вопросам человека, а не по устройству программы. Первой идёт
            // та, ради которой программу ставят: пришедший с мака в первые полчаса
            // замечает не F5, а что ⌘C не копирует. И она единственная, которая не умеет
            // быть пустой: страница функционального ряда при работающем драйвере честно
            // сообщает, что ни на что не влияет, — такая первой быть не может.
            AddPage("mac", "Как на маке");
            AddPage("keys", "Клавиши");
            AddPage("layout", "Раскладка");
            AddPage("driver", "Драйвер Apple");
            AddPage("about", "О программе");
            if (_s.DeveloperMode) AddPage("diag", "Диагностика");

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
                return i >= 0 && i < _pages.Count ? _pages[i] : "mac";
            }
        }

        /// <returns>Собрали ли страницу. Ложь — сборка уже шла, и наша просьба пропала.</returns>
        private bool BuildPage()
        {
            if (_building) return false;
            _building = true;

            // Уходим со страницы проверки — снимаем подписку, иначе они копятся,
            // а надписи, на которые они ссылаются, уже выброшены.
            DetachSelfTest();

            // Со страницы «О программе» уходим — обновление больше некому показывать.
            _updStatus = null;
            _updAction = null;

            // Ушли со страницы драйвера — на возврате пересчитаем размер скачанного
            // и поищем 7-Zip заново: за это время их могли и добавить, и убрать.
            if (CurrentPage != "driver") _probedFor = null;

            try
            {
                switch (CurrentPage)
                {
                    case "mac": Head("Как на маке", "⌘C, ⌘←, ⌘Q и ещё полсотни привычек — чтобы пальцы не переучивались."); _host.Content = PageMacKeys(); break;
                    case "keys": Head("Клавиши", "Что делает каждая клавиша, которая на маке устроена иначе."); _host.Content = PageKeys(); break;
                    case "layout": Head("Раскладка", "Какие символы печатаются — те, что напечатаны на клавишах Apple."); _host.Content = PageLayout(); break;
                    case "driver": Head("Драйвер Apple", "Родной драйвер Boot Camp и как программа с ним уживается."); _host.Content = PageDriver(); break;
                    case "diag": Head("Диагностика", "Что программа видит и что до неё доходит."); _host.Content = PageDiag(); break;
                    default: Head("О программе", "MagicKeys — свободная программа."); _host.Content = PageAbout(); break;
                }
            }
            finally
            {
                _building = false;
                // Отчёты о разовых действиях живут ровно одну перестройку — ту, которую
                // сами же и вызвали. Гасить их на входе было нельзя: их ставят прямо
                // перед BuildPage, а читает их сама страница уже внутри — то есть ни
                // «Записано», ни «Не вышло», ни список изменённого не показывались
                // никогда, и отказ по правам выглядел точно как успех.
                _fnNotice = null;
                _tuneNotice = null;
                _autostartNotice = null;
            }
            return true;
        }

        private void Head(string title, string hint)
        {
            _pageTitle.Text = title;
            _pageHint.Text = hint;
        }

        private UIElement PageKeys()
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
                         "по-прежнему.\n\n" +
                         "Чтобы вернуть ряд себе, переключите режим на странице «Драйвер Apple» — " +
                         "там же объяснено, что при этом меняется."),
                    LinkButton("Перейти к драйверу Apple", delegate { GoTo("driver"); })));
            }

            var modeBox = Combo(new[]
            {
                new Choice { Value = true,  Text = "Медиафункции сразу (как в macOS)" },
                new Choice { Value = false, Text = "F-клавиши сразу (как обычно в Windows)" }
            }, _s.MediaFirst, delegate(object v) { _s.MediaFirst = (bool)v; Save(); BuildPage(); });

            // Драйвер забирает ровно первые двенадцать — F13 и дальше он не трогает.
            // Поэтому на клавиатуре с цифровым блоком режим продолжает решать всё
            // для F13–F19, и гасить его нельзя: погашенный, он не давал вернуть
            // назначения этих клавиш к жизни ничем.
            bool aboveTwelve = Models.FunctionKeyCount() > 12;
            if (YieldingNow && !aboveTwelve) modeBox.IsEnabled = false;

            stack.Children.Add(Card(
                YieldingNow && aboveTwelve ? "Что делают F13 и дальше без модификатора"
                                           : "Что делают F1–F12 без модификатора", null,
                Row("Основной режим", null, modeBox)));

            var fnChoices = new List<Choice>();
            fnChoices.Add(new Choice { Value = ModKey.None, Text = "Нет — переключать только в этом окне" });
            // Правого control в списке нет: на клавиатурах Apple его не бывает,
            // а предлагать несуществующую клавишу — обещать то, чего не нажать.
            //
            // Правый ⌥ есть, хотя о нём спрашивает и карточка ниже. Без него список лгал:
            // с завода заменитель — именно правый ⌥, в списке его не было, и список
            // показывал «Нет». Прикоснувшийся к нему человек не выбирал ничего — и терял
            // замену Fn. Два места об одной клавише — плохо; место, показывающее неправду, —
            // хуже, поэтому карточка перерисовывается вслед за списком.
            foreach (ModKey m in new[] { ModKey.RAlt, ModKey.RWin, ModKey.LWin, ModKey.CapsLock })
                fnChoices.Add(new Choice { Value = m, Text = ModNames.Of(m) });

            var fnBox = Combo(fnChoices.ToArray(), _s.FnSubstitute,
                delegate(object v)
                {
                    ModKey pick = (ModKey)v;
                    _s.FnSubstitute = pick;
                    // Правый ⌥ не может быть и заменителем Fn, и третьим уровнем разом —
                    // о том же спрашивает карточка ниже, и ответы обязаны сойтись.
                    if (pick == ModKey.RAlt) _s.OptLevel = OptLevel.Off;
                    Save(); BuildPage();
                });

            // Уступленный ряд эту карточку не отменяет: F13 и дальше драйвер не трогает,
            // и для них «F-клавиши сразу» без заменителя Fn значит ровно то же самое.
            // Прежде при уступленном ряде карточка молчала — и назначения F13–F19
            // не работали, не сказав об этом ни словом.
            if (!_s.MediaFirst && _s.FnSubstitute == ModKey.None && (!YieldingNow || aboveTwelve))
            {
                string which = YieldingNow ? "F13 и дальше" : "F1–F12";
                stack.Children.Add(Card("Назначения " +
                    (YieldingNow ? "клавиш F13 и дальше" : "ниже") + " сейчас ни на что не влияют", null,
                    Note("Выбран режим «F-клавиши сразу», а заменителя Fn нет — значит " +
                         which + " всегда приходят обычными F-клавишами и уходят в программы " +
                         "как есть. Чтобы назначения заработали, выберите заменитель Fn " +
                         "в карточке ниже или выберите «Медиафункции сразу» в карточке выше.")));
            }

            stack.Children.Add(Card("Заменитель Fn", null,
                Row("Клавиша", "С ней режим временно переворачивается", fnBox),
                Toggle("Навигация как в macOS: Fn со стрелками, Backspace и Enter",
                    _s.FnNavigation, delegate(bool v) { _s.FnNavigation = v; Save(); BuildPage(); }),
                // Заменитель и ⌥ бывают одной клавишей, и тогда три сочетания из шести
                // достаются таблице macOS. Перечислять всё, а следом оговариваться — значит
                // сперва пообещать, потом отобрать: список сразу называет то, что работает.
                Note(!_s.FnNavigation
                        ? "Навигация выключена: Fn со стрелками, Backspace и Enter сейчас " +
                          "не работают." +
                          // Про «заменитель всё равно нужен» — только когда это правда.
                          // При отданном драйверу ряде вызывать заменителем нечего,
                          // а сноска ниже прямо говорит, что он не нужен: две фразы
                          // подряд спорили друг с другом, и обе стояли в одной карточке.
                          (YieldingNow ? "" : " Заменитель при этом остаётся нужен — " +
                                              "им вызывают верхний ряд.")
                    : _s.FnSubstitute == ModKey.None
                        ? "Пока заменителя нет, нажать эти сочетания нечем: настоящая Fn " +
                          "до Windows не доходит."
                    : FnNavigationNote()),
                Note(YieldingNow
                    ? "Сейчас верхним рядом занимается драйвер Apple: с ним работает настоящая Fn, " +
                      "и для F1–F12 заменитель не нужен." +
                      // Драйвер забирает ровно первые двенадцать. Сказать «для верхнего
                      // ряда не нужен» на клавиатуре с цифровым блоком значило отменить
                      // карточку выше, которая двумя сантиметрами раньше советует
                      // выбрать заменитель ради F13 и дальше.
                      (aboveTwelve
                        ? " Для F13 и дальше он нужен: их драйвер не трогает."
                        : "") +
                      // Навигация Fn про уступку ряда не спрашивает вовсе и держится
                      // на заменителе: сказать «не нужен» вообще значило бы отобрать
                      // сочетания, которые сноска выше только что пообещала.
                      (_s.FnNavigation
                        ? " Для Fn со стрелками он по-прежнему нужен, пока навигация включена."
                        : "")
                    : _s.FnSubstitute == ModKey.None
                    ? "Magic Keyboard не отправляет Fn в Windows — её обрабатывает сама " +
                      "клавиатура. Поэтому и нужна замена: выберите клавишу выше."
                    : "Magic Keyboard не отправляет Fn в Windows — её обрабатывает сама " +
                      "клавиатура. Поэтому нужна замена. Выбранная клавиша сохраняет своё " +
                      "обычное значение; если она нужна только как Fn, отключите его — " +
                      (_s.FnSubstitute == ModKey.RAlt
                          ? "в карточке «Правый ⌥» ниже."
                          : "в карточке «Отдельные клавиши» ниже."))));

            string[] legends = Models.Legend(Generation);
            int fcount = Models.FunctionKeyCount();
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
                // Пока драйвер занимается верхним рядом, эти строки ни на что не влияют —
                // карточка сверху так и говорит, а списки при этом щёлкали и сохранялись.
                // Ровно первые двенадцать: F13 и дальше драйвер не трогает.
                if (YieldingNow && i < 12) box.IsEnabled = false;
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
                // Запоминаем и поколение: иначе набор перестаёт совпадать с эталоном,
                // и подстановка заводских при смене клавиатуры выключается навсегда.
                _s.FKeysGen = Generation;
                Save();
                BuildPage();
            };
            AppleModel fm = Devices.AppleModel;
            string howMany = "Показано клавиш: " + fcount + ". ";
            if (fm != null && fm.FunctionKeys > 12)
                howMany += "У этой модели есть ряд F13–F" + fm.FunctionKeys + " над цифровым блоком. ";
            howMany += "Если нажать клавишу, которой в списке нет, она появится здесь " +
                       "при следующем открытии страницы.";

            stack.Children.Add(Card("Действия",
                "Слева — то, что напечатано на клавише Apple (" + Models.GenName(Generation) + ").",
                table, reset, Note(howMany)));

            // Карточка показывается всегда, а не только при цифровом блоке: перехват
            // применяет это назначение к ЛЮБОМУ Num Lock — устройства он не знает. Рядом
            // с компактной Magic Keyboard стоит любая полноразмерная клавиатура, её Num
            // Lock даёт Delete, а настройки, которая это делает, в окне не было ни на
            // одной странице.
            {
                var padGrid = new Grid();
                padGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                padGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

                AddSingleRow(padGrid, 0, "Clear", "Верхняя левая на цифровом блоке, у Apple помечена значком очистки",
                    _s.NumpadClear, delegate(string id) { _s.NumpadClear = id; Save(); });


                stack.Children.Add(Card("Цифровой блок", null, padGrid,
                    Note("Две клавиши цифрового блока в Windows ведут себя не так, как напечатано.\n\n" +
                         "Clear отдаёт Num Lock — то есть попадание по ней молча превращает " +
                         "цифровой блок в навигационный. По умолчанию она делает Delete, как ближайший " +
                         "к маковскому «очистить» смысл. Пока эта клавиша уведена с Num Lock, программа " +
                         "включает блок при запуске и при изменении настроек: переключить его иначе " +
                         "было бы нечем.\n\n" +
                         "Клавиша «=» приходит кодом «очистить», которого почти никто не понимает, — " +
                         "программа печатает «=» сама.")));
            }

            // Клавиша ⏏ — своей карточкой, а не внутри цифрового блока. Внутри она
            // доставалась только клавиатурам с блоком, то есть ровно тем, у которых
            // её нет: ⏏ бывает у алюминиевых компактных, а они без блока. Настройка
            // при этом живая — её применяет перепись клавиш.
            // И всегда, когда назначение уже стоит: настройка живая, её применяет
            // перепись клавиш, — а спрятав карточку, мы отбирали единственный способ
            // её увидеть и выключить. Назначил на алюминиевой, перешёл на Magic
            // Keyboard 2021 — и назначение осталось без хозяина.
            bool ejectSet = Actions.Get(_s.EjectKey).Kind != ActionKind.PassThrough;
            if (KeyWatch.EjectSeen || ejectSet || (fm != null && fm.Eject) || fm == null)
            {
                var ejGrid = new Grid();
                ejGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ejGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                AddSingleRow(ejGrid, 0, "Eject", "Верхний правый угол, со значком извлечения",
                    _s.EjectKey, delegate(string id) { _s.EjectKey = id; Save(); });

                // Исполняет назначение обработчик в App, а он молча уходит при
                // выключенных переназначениях и на паузе без клавиатуры Apple.
                // Сказать в этот миг «назначение работает» — обещать несделанное.
                bool live = _s.Enabled && !(_s.PauseWhenAppleAbsent && !Devices.AppleConnected);
                string ejNote = KeyWatch.EjectSeen
                    ? (live
                        ? "Эта клавиша с вашей клавиатуры приходит — назначение работает."
                        : "Эта клавиша с вашей клавиатуры приходит, но сейчас назначение " +
                          "не срабатывает: " + (_s.Enabled
                              ? "выбрано «Только с Magic Keyboard», а её сейчас нет."
                              : "переназначения выключены на первой странице."))
                    : fm != null && fm.Gen == AppleGen.Alu
                        ? "Эта клавиша ещё не приходила. Алюминиевые модели по USB её присылают — " +
                          "нажмите, и назначение заработает."
                        : "Эта клавиша в Windows не приходит: её обрабатывает сама клавиатура, " +
                          "и драйвер тут не помогает. Настройка сработает на алюминиевых моделях " +
                          "по USB — они её присылают.";

                stack.Children.Add(Card("Клавиша Eject", null, ejGrid, Note(ejNote)));
            }

            // Правый ⌥ — одним вопросом, до модификаторов: он и есть самая спорная клавиша.
            stack.Children.Add(RightOptionCard());
            AddModifierCards(stack);

            return stack;
        }

        /// <summary>
        /// Забрать правый ⌥ у его особых ролей. Зовут схемы, которые уводят клавишу
        /// в другую: заменителем Fn и третьим уровнем она после этого быть не может —
        /// первому нечего временно снимать, второму нечем набирать.
        /// </summary>
        private void TakeRightOption()
        {
            if (_s.FnSubstitute == ModKey.RAlt) _s.FnSubstitute = ModKey.None;
            _s.OptLevel = OptLevel.Off;
        }

        /// <summary>
        /// Модификаторы — карточками на странице «Клавиши», а не отдельной страницей.
        /// Вопрос у человека один: что происходит, когда я жму вот эту клавишу; резать
        /// его надвое по устройству программы незачем.
        /// </summary>
        private void AddModifierCards(StackPanel stack)
        {
            var presets = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, -8, -8) };
            // Не «как в macOS»: так называется перевод сочетаний на первой странице,
            // и два разных механизма под одним именем — то, из-за чего человек включал
            // оба сразу и получал, что половина переводов молча переставала работать.
            presets.Children.Add(PresetButton("⌘ работает как Ctrl", "Обмен клавиш вместо перевода сочетаний", delegate
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
                TakeRightOption();
                Save(); BuildPage();
            }));
            presets.Children.Add(PresetButton("Без изменений", "Как приходит от клавиатуры", delegate
            {
                // И Caps Lock тоже: он стоит первой строкой прямо под этими кнопками
                // и меняется чаще остальных вместе взятых. Не возвращать его — значит
                // обещать «как приходит от клавиатуры» и оставить Escape.
                _s.MapCapsLock = ModKey.CapsLock;
                _s.MapLCtrl = ModKey.LCtrl;
                _s.MapLWin = ModKey.LWin;
                _s.MapLAlt = ModKey.LAlt;
                _s.MapRAlt = ModKey.RAlt;
                _s.MapRWin = ModKey.RWin;
                Save(); BuildPage();
            }));
            stack.Children.Add(Card("Готовые схемы", null, presets));
            // Схема, уводящая правый ⌥ в другую клавишу, забирает его себе целиком.
            // Иначе карточка «Правый ⌥» продолжала уверять «набирает символы третьего
            // уровня», а клавиша в этот момент приходила в Windows клавишей Win —
            // и строки, которой это видно, в карточке нет: при роли «символы» её прячут.

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            // Каждая строка пересобирает страницу. Эти пять полей решают, до какого
            // модификатора вообще можно дотянуться, — а от этого зависят и правила
            // Normalize (третий уровень, «любой ⌥»), и половина сносок на странице.
            // Без пересборки выбор человека молча отменялся: переключатель «Любой ⌥»
            // оставался включённым при уже сброшенной настройке, а сноска про Fn
            // продолжала обещать сочетания, которые перешли к таблице macOS.
            //
            // Caps Lock первой строкой: с мака его меняют чаще, чем все остальные вместе.
            AddModRow(grid, 0, "Caps Lock", "Часто удобнее как control или Escape", _s.MapCapsLock,
                delegate(ModKey m) { _s.MapCapsLock = m; Save(); BuildPage(); });
            AddModRow(grid, 1, "control (левый)", "Крайняя левая клавиша", _s.MapLCtrl,
                delegate(ModKey m) { _s.MapLCtrl = m; Save(); BuildPage(); });
            AddModRow(grid, 2, "⌥ option (левый)", "Между control и ⌘", _s.MapLAlt,
                delegate(ModKey m) { _s.MapLAlt = m; Save(); BuildPage(); });
            AddModRow(grid, 3, "⌘ command (левая)", "Слева от пробела", _s.MapLWin,
                delegate(ModKey m) { _s.MapLWin = m; Save(); BuildPage(); });
            AddModRow(grid, 4, "⌘ command (правая)", "Справа от пробела", _s.MapRWin,
                delegate(ModKey m) { _s.MapRWin = m; Save(); BuildPage(); });
            // Правого ⌥ здесь нет: у него своя карточка выше, где он спрашивается один раз.

            stack.Children.Add(Card("Отдельные клавиши", "Слева — что напечатано на клавише, справа — чем она станет.", grid));
        }

        /// <summary>
        /// Правый ⌥ — один вопрос вместо четырёх.
        ///
        /// По умолчанию эта клавиша делала четыре работы разом, и спрашивали о них
        /// на трёх страницах: заменитель Fn, третий уровень раскладки, обычный модификатор
        /// и отмена AltGr. Что из этого выходило, видно было на живой клавиатуре: правый ⌥
        /// со стрелкой влево давал «на слово влево», а со стрелкой вверх — PgUp. Одна
        /// клавиша, две соседние стрелки, две разные логики. Программа даже предупреждала
        /// об этом карточкой — а предупреждение о собственных умолчаниях означает, что
        /// умолчания разошлись.
        /// </summary>
        private UIElement RightOptionCard()
        {
            Choice[] roles =
            {
                new Choice { Value = "fn",     Text = "Клавиша Fn" },
                new Choice { Value = "symbols",Text = "Символы ⌥ (третий уровень раскладки)" },
                new Choice { Value = "plain",  Text = "Обычный Alt" }
            };

            bool asFn = _s.FnSubstitute == ModKey.RAlt;
            // По назначению, а не по одному полю. OptLevel живёт по правилу «третий
            // уровень даёт любая клавиша, приходящая как ⌥», и состояние «третий уровень
            // включён, а правый ⌥ приходит клавишей Windows» законно — из файла
            // от прежней версии оно и приходит. Карточка при этом объявляла роль
            // «символы» и прятала единственную строку, по которой это видно.
            bool rightIsOption = _s.TargetOf(ModKey.RAlt) == ModKey.RAlt
                              || (_s.OptLevel == OptLevel.AnyOption
                                  && _s.TargetOf(ModKey.RAlt) == ModKey.LAlt);
            bool asSym = _s.OptLevel != OptLevel.Off && rightIsOption;
            string now = asFn ? "fn" : (asSym ? "symbols" : "plain");
            string comes = ModNames.Of(_s.MapRAlt);

            string what =
                now == "fn"
                    ? (YieldingNow
                        ? "Сейчас верхним рядом занимается драйвер Apple, и переключать нечего. " +
                          (_s.FnNavigation ? "Fn+стрелки работают." : "Навигация Fn выключена выше.")
                        : "Переключает верхний ряд" + (_s.FnNavigation ? " и даёт Fn+стрелки" : "") +
                          ". Без драйвера Apple настоящая Fn до Windows не доходит, поэтому нужна замена.")
                : now == "plain" && _s.OptLevel != OptLevel.Off && _s.Reaches(ModKey.RAlt)
                    ? "Ничего не забирает: приходит в Windows как " + comes + ". " +
                      "Третий уровень при этом включён и живёт на другой клавише — той, " +
                      "что приходит правым Alt. Выключить его можно так: вернуть сюда роль " +
                      "«Символы ⌥», а потом снова выбрать «Обычный Alt». Выбор «Символы ⌥» " +
                      "сам по себе третий уровень не выключает, а включает — на этой клавише. " +
                      "И учтите: первый шаг вернёт правому ⌥ обычный Alt, а прежнее его " +
                      "назначение придётся выбрать заново."
                : now == "symbols"
                    ? "Набирает символы, напечатанные на клавише третьими. Работает, только когда " +
                      "включены раскладки Apple." +
                      // Назвать условие мало — надо сказать, выполнено ли оно сейчас.
                      // С заводскими настройками раскладки выключены, и эта роль отбирала
                      // заменитель Fn, не давая взамен ничего.
                      (_s.AppleLayoutEnabled
                        ? ""
                        : "\n\nСейчас они выключены — символы набираться не будут. " +
                          "Включить их можно на странице «Раскладка».")
                : _s.MapRAlt == ModKey.RAlt
                    ? "Ничего не забирает: обычный Alt, и AltGr системной раскладки работает как всегда."
                    : _s.MapRAlt == ModKey.None
                    ? "Ничего не забирает: клавиша выключена и в Windows не приходит вовсе."
                    : "Ничего не забирает и приходит в Windows как " + comes + ".";

            var rows = new StackPanel();
            rows.Children.Add(Row("Делает", null, Combo(roles, now, delegate(object v)
            {
                string pick = (string)v;
                // Снимаем замену Fn только со своей клавиши. Раньше снимали любую:
                // человек назначал Fn на Caps Lock, потом менял роль правого ⌥ —
                // и терял Fn+стрелки, ни разу не сказав об этом.
                if (pick == "fn") _s.FnSubstitute = ModKey.RAlt;
                else if (_s.FnSubstitute == ModKey.RAlt) _s.FnSubstitute = ModKey.None;
                _s.OptLevel = pick == "symbols" ? OptLevel.RightOption : OptLevel.Off;
                // Ролям Fn и «символы» клавиша нужна как Alt: иначе третьего уровня
                // не набрать, а заменителю нечего временно снимать.
                if (pick != "plain") _s.MapRAlt = ModKey.RAlt;
                Save(); BuildPage();
            })));
            rows.Children.Add(Note(what));

            if (now == "symbols")
            {
                // Пересобираем страницу: Normalize вправе отменить этот выбор, если
                // левого Alt не достать ни одной клавишей, — и тогда переключатель обязан
                // погаснуть сам. Молча отменённый выбор — это окно, показывающее одно
                // и держащее другое.
                var anyOpt = Toggle("Любой ⌥, как на маке — но тогда левый Alt перестанет открывать меню",
                    _s.OptLevel == OptLevel.AnyOption,
                    delegate(bool v)
                    {
                        _s.OptLevel = v ? OptLevel.AnyOption : OptLevel.RightOption;
                        Save(); BuildPage();
                    });
                rows.Children.Add(anyOpt);
                if (!_s.Reaches(ModKey.LAlt))
                    rows.Children.Add(Note("Левого Alt сейчас не даёт ни одна клавиша — его увели " +
                                           "переназначения ниже. Пока так, «любой ⌥» включить нечем: " +
                                           "третий уровень остаётся на правом."));
            }

            // Чем клавиша приходит в Windows — спрашиваем всегда, кроме роли «символы»:
            // там она обязана оставаться Alt, иначе третьего уровня не набрать. Заменителю
            // Fn это нужно не меньше: подсказка выше предлагает отключить обычное значение
            // клавиши, если она нужна только как Fn, — и делать это негде, если не спросить.
            // Прежде поле правили только готовые схемы, и карточка уверяла «обычный Alt»,
            // когда клавиша давно приходила клавишей Windows.
            if (now != "symbols")
            {
                var comesGrid = new Grid();
                comesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                comesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
                AddModRow(comesGrid, 0, "Приходит в Windows как", null, _s.MapRAlt,
                    delegate(ModKey m) { _s.MapRAlt = m; Save(); BuildPage(); });
                rows.Children.Add(comesGrid);
            }

            return Card("Правый ⌥ (option)", null, rows);
        }

        private UIElement PageLayout()
        {
            var stack = new StackPanel();
            PhysLayout phys = _engine.Physical(_s);
            PhysLayout detected = KeyWatch.DetectedPhysical;

            AppleModel model = Devices.AppleModel;
            string physNote = "Сейчас программа считает клавиатуру: " + Models.PhysName(phys) +
                              (_s.Physical == PhysLayout.Auto && detected == PhysLayout.Ansi
                                   && Devices.AppleModel == null
                                  ? " (пока по умолчанию — по нажатиям ничего не распознано)" : "") + ".\n";
            // Спрашиваем в том же порядке, в каком отвечает Engine.Physical: выбор
            // человека побеждает и модель, и распознавание. Прежде сноска про него
            // не знала вовсе и при выставленном руками «ISO» уверяла, что исполнение
            // определяют по нажатиям и распознано ANSI, — споря с собственной первой
            // строкой и не давая понять, откуда взялась перестановка клавиш.
            if (_s.Physical != PhysLayout.Auto)
                physNote += "Так выбрано в списке ниже. Распознанное по нажатиям (" +
                            Models.PhysName(detected) + ") и записанное в модели при этом " +
                            "не учитываются: выбор человека главнее.";
            else if (model != null && model.Phys != PhysLayout.Auto)
                physNote += "Исполнение закодировано в идентификаторе модели — " + Models.PhysName(model.Phys) + ".";
            else
                physNote += "У Magic Keyboard исполнение не закодировано в идентификаторе, поэтому оно " +
                            "определяется по нажатиям: клавиша слева от «Z» выдаёт ISO, клавиши у пробела — JIS. " +
                            // ANSI — это и есть «ничего не распознано»: признак заведён
                            // им же и меняется только на ISO и JIS. Выдавать его за
                            // распознанное значит уверять человека с европейской
                            // клавиатурой, что две клавиши у него стоят правильно.
                            (detected == PhysLayout.Ansi
                                ? "Ни ISO, ни JIS по нажатиям пока не встретилось, поэтому " +
                                  "клавиатура считается ANSI. Нажмите клавишу слева от «Z» — " +
                                  "или выберите исполнение в списке ниже."
                                : "Пока распознано: " + Models.PhysName(detected) + ".");

            // Исполнение определяется само, но список показываем всем. Спрятанный
            // за режим разработчика, он оставлял настройку без хозяина: выставленное
            // «ISO» переживало выключение режима и молча переставляло две клавиши
            // на клавиатуре ANSI — а вернуть его было негде.
            {
                var physBox = Combo(new[]
                {
                    new Choice { Value = PhysLayout.Auto, Text = "Определять самой" },
                    new Choice { Value = PhysLayout.Ansi, Text = "ANSI (американское)" },
                    new Choice { Value = PhysLayout.Iso,  Text = "ISO (европейское)" },
                    new Choice { Value = PhysLayout.Jis,  Text = "JIS (японское)" }
                }, _s.Physical, delegate(object v) { _s.Physical = (PhysLayout)v; Save(); BuildPage(); });

                stack.Children.Add(Card("Исполнение клавиатуры", null,
                    Row("Тип", "От него зависит набор клавиш, а не язык", physBox),
                    Note(physNote)));
            }

            // Перестановки двух клавиш ISO здесь больше нет: это не вкус, а исправление
            // аппаратной особенности, и программа делает его сама, когда клавиатура ISO.


            // ---- раскладки Apple ----
            // Третьего уровня здесь больше нет, хотя вопрос о нём напрашивается сам.
            // О нём спрашивает карточка «Правый ⌥» на странице «Клавиши», и спрашивать
            // дважды нельзя: там вопрос стоит целиком — Fn, символы или обычный Alt, —
            // потому что одна клавиша не может делать два дела разом. Отсюда же можно
            // было выбрать «символы», не сняв замену Fn, и получить обе роли на одной
            // клавише; карточка при этом показывала «Клавиша Fn» и молчала о второй.
            stack.Children.Add(Card("Раскладки Apple", null,
                Toggle("Воспроизводить раскладки macOS", _s.AppleLayoutEnabled,
                    delegate(bool v) { _s.AppleLayoutEnabled = v; Save(); BuildPage(); }),
                Note("Apple раскладывает буквы и знаки иначе, чем Microsoft, — и именно поэтому " +
                     "Boot Camp когда-то доставлял в Windows отдельные языки ввода «(Apple)». " +
                     "Здесь то же самое делается без установки раскладок в систему: программа " +
                     "подменяет только те клавиши, которые в действующей раскладке Windows дают " +
                     "не то, что напечатано на клавише Apple. Остальные нажатия идут как обычно.\n\n" +
                     "Таблицы — те же данные, по которым раскладку рисует сама macOS; мёртвые " +
                     "клавиши (´ ` ¨ ˆ ~) работают.\n\n" +
                     "Символы, напечатанные на клавишах третьими, набирает ⌥ — какой именно, " +
                     "решает карточка «Правый ⌥» на странице «Клавиши».")));

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

                // Три разных ответа, а не два: «подобрать самой» — это не то же, что
                // «не подменять», и раньше их было не различить. Подбор показывался так,
                // будто его выбрали руками, а «не подменять» не выбиралось вовсе.
                string guess = Settings.GuessLayout(lang.Key);
                AppleLayoutFile guessed = guess == null ? null : Layouts.ById(guess);
                var choices = new List<Choice>();
                choices.Add(new Choice
                {
                    Value = "auto",
                    Text = guessed == null
                        ? "Подобрать самой (раскладки нет)"
                        : "Подобрать самой — " + guessed.Title
                });
                choices.Add(new Choice { Value = "", Text = "Не подменять" });
                foreach (AppleLayoutFile f in Layouts.All)
                    choices.Add(new Choice { Value = f.Id, Text = f.Title + " · " + f.MacName });

                LayoutBinding picked = _s.BindingFor(lang.Key);
                string current = picked == null ? "auto" : (picked.Layout == null ? "" : picked.Layout);
                // Выбранной раскладки может не оказаться среди файлов: её убрали из папки
                // или переименовали. Список, не найдя своего значения, показал бы первый
                // пункт — то есть уверял бы, что раскладка подбирается сама, тогда как
                // выбор человека на месте и подмены не будет. Показываем как есть.
                if (current != "" && current != "auto" && Layouts.ById(current) == null)
                    choices.Add(new Choice { Value = current, Text = "«" + current + "» — файла нет" });
                int langId = lang.Key;
                var box = Combo(choices.ToArray(), current,
                    delegate(object v)
                    {
                        string id = (string)v;
                        if (id == "auto") _s.ClearLayoutFor(langId);
                        else _s.SetLayoutFor(langId, id);
                        Save();
                    });
                box.Margin = new Thickness(0, 6, 0, 6);
                box.IsEnabled = _s.AppleLayoutEnabled;
                Grid.SetRow(box, row); Grid.SetColumn(box, 1);

                langGrid.Children.Add(left);
                langGrid.Children.Add(box);
                row++;
            }

            var reload = new Button { Content = "Перечитать файлы раскладок", HorizontalAlignment = HorizontalAlignment.Left };
            reload.SetResourceReference(StyleProperty, "Btn");
            // И Save: перехват держит разобранный файл, пока не сменится раскладка окна
            // или снимок настроек. Без нового снимка список в окне обновлялся, а печатались
            // прежние символы — до следующей смены языка или любой другой настройки.
            reload.Click += delegate { Layouts.Reload(); Save(); BuildPage(); };

            stack.Children.Add(Card("Языки ввода Windows",
                "Раскладка Apple применяется к тому языку, который выбран в этот момент.",
                langGrid, reload,
                Note("Файлов раскладок найдено: " + Layouts.All.Count + ". Встроенные лежат в папке " +
                     "«layouts» рядом с программой, свои можно положить в " + Layouts.UserFolder +
                     " — одноимённые вытеснят встроенные.")));


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

        /// <summary>Запуск программы — одной карточкой на странице «О программе».</summary>
        private UIElement StartupCard()
        {
            var rows = new List<UIElement>();
            rows.Add(
                // Пересобираем: запись в реестр может не лечь — политикой, правами, —
                // а переключатель показывал бы щелчок, а не то, что в реестре, до самого
                // ухода со страницы.
                Toggle("Запускать вместе с Windows", Autostart.Enabled,
                    delegate(bool v)
                    {
                        if (!Autostart.Set(v, _s.StartMinimized))
                            _autostartNotice = "Windows не дала записать автозапуск. " +
                                               "Чаще всего это политика или права на раздел реестра.";
                        BuildPage();
                    }));
            rows.Add(
                Toggle("Запускаться свёрнутой в значок", _s.StartMinimized,
                    delegate(bool v)
                    {
                        _s.StartMinimized = v;
                        Save();
                        // Запись автозапуска несёт в себе этот ответ — переписываем её.
                        // И спрашиваем, легла ли: настройка сохранится в любом случае,
                        // а при входе в систему окно выскочит — и объяснить это было
                        // бы нечем.
                        if (Autostart.Enabled && !Autostart.Set(true, v))
                        {
                            // Не «окно откроется по-старому»: прячется оно от самой
                            // настройки, а «--tray» в записи автозапуска — только ремень
                            // на случай, если файл настроек не прочитается.
                            _autostartNotice = "Настройка сохранена. Переписать запись " +
                                               "автозапуска Windows не дала — на поведение " +
                                               "это не влияет, пока цел файл настроек.";
                            BuildPage();
                        }
                    }));
            // Отчёт живёт одну пересборку — ту, которую сам и вызвал.
            if (!String.IsNullOrEmpty(_autostartNotice)) rows.Add(Note(_autostartNotice));
            return Card("Запуск", null, rows.ToArray());
        }

        /// <summary>Почему автозапуск не записался — на одну пересборку, как и прочие отчёты.</summary>
        private string _autostartNotice;

        /// <summary>
        /// Диагностика: что программа видит и что до неё доходит. Прежде это были две
        /// страницы, а вопрос за обеими стоит один.
        /// </summary>
        private UIElement PageDiag()
        {
            var stack = new StackPanel();
            AddDeviceCards(stack);
            AddSelfTestCards(stack);
            return stack;
        }

        private void AddDeviceCards(StackPanel stack)
        {

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
                if (k.IsApple) rows.Children.Add(KeyValue("Исполнение", Models.PhysName(_engine.Physical(_s))));
                if (k.TotalKeys > 0) rows.Children.Add(KeyValue("Клавиш", k.TotalKeys.ToString()));
                stack.Children.Add(Card(k.Model, k.IsApple ? "Правила MagicKeys рассчитаны на эту клавиатуру" : null, rows));
            }

            // Значение может быть ещё неизвестно: чтение идёт в пуле потоков, а страница
            // перестроится сама, когда ответ придёт.
            int battery = KeyboardBattery.Percent;
            if (battery >= 0)
            {
                var brows = new StackPanel();
                brows.Children.Add(KeyValue("Заряд", battery + " %"));
                stack.Children.Add(Card("Заряд батареи", null, brows,
                    Note("Windows этот уровень не показывает: клавиатура его сама не присылает. " +
                         "Программа спрашивает его у клавиатуры напрямую — не чаще раза в минуту " +
                         "и только когда значение кому-то понадобилось.")));
            }
            else
            {
                stack.Children.Add(Card("Заряд батареи", null,
                    Note(battery == KeyboardBattery.NoSource
                        ? "Эта клавиатура заряд не сообщает: вендорной коллекции, через которую " +
                          "его спрашивают, у неё нет. Ждать нечего — так устроены модели " +
                          "без неё, и это не поломка."
                        : battery == KeyboardBattery.Unknown
                        ? "Спрашиваю у клавиатуры… По Bluetooth ответ приходит не сразу; "  +
                          "как только он придёт, число появится здесь само."
                        : "Спросить не удалось: клавиатура не ответила. Придумывать число " +
                          "программа не станет.")));
            }

            var refresh = new Button { Content = "Обновить", HorizontalAlignment = HorizontalAlignment.Left };
            refresh.SetResourceReference(StyleProperty, "Btn");
            // Опрос идёт в сторонке: он открывает каждую клавиатуру и тянет из неё
            // три строки, а по Bluetooth это секунды. На потоке окна Windows успевала
            // нарисовать «не отвечает».
            refresh.Click += delegate
            {
                refresh.IsEnabled = false;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try { Devices.Rescan(); }
                    catch (Exception e) { Diag.Log("не удалось опросить устройства", e); }
                    ToWindow(delegate
                    {
                        refresh.IsEnabled = true;
                        RefreshDevices();
                    });
                });
            };
            stack.Children.Add(refresh);

        }

        // ------------------------------------------------------------------
        //  Проверка клавиш
        // ------------------------------------------------------------------

        private Action<KeyWatch.KeyEvent> _selfTest;
        private TextBlock _selfTestLog;
        private StackPanel _selfTestChecks;
        private readonly List<string> _selfTestLines = new List<string>();

        /// <summary>
        /// Что из навигации Fn работает прямо сейчас — перечислением, а не одной фразой
        /// на три состояния.
        ///
        /// Таблица macOS отбирает у навигации три сочетания, и отбирает по одному:
        /// перехват спрашивает MacEnabled у каждой строки, а выключенную пропускает
        /// дальше. Прежде сноска отвечала на все три состояния одинаково и потому врала
        /// в двух из них — то обещая отобранное, то отбирая работающее.
        ///
        /// И узнаёт ⌥ она по назначению клавиши, а не по её надписи: так же, как таблица.
        /// </summary>
        private string FnNavigationNote()
        {
            var mine = new List<string>();
            var taken = new List<string>();

            // Пару спрашиваем целиком, обе половины. В таблице они заведены отдельными
            // строками и выключаются по одной — карточка «Показать все» для того и есть.
            // Спрашивая только левую и верхнюю, сноска после выключения «В начало
            // документа» обещала листание страницы, тогда как Fn+↓ по-прежнему уходил
            // в конец документа.
            if (TakenByTable(Vk.Up) || TakenByTable(Vk.Down)) taken.Add("Fn+↑↓");
            else mine.Add("Fn+↑↓ листают страницу");

            if (TakenByTable(Vk.Left) || TakenByTable(Vk.Right)) taken.Add("Fn+←→");
            else mine.Add("Fn+←→ уводят в начало и конец строки");

            if (TakenByTable(Vk.Back)) taken.Add("Fn+Backspace");
            else mine.Add("Fn+Backspace удаляет вперёд");

            // Fn+Enter в таблице macOS не встречается ни при одном заменителе, поэтому
            // без единой своей строки этот список не остаётся. Ветка «всё досталось
            // таблице» здесь и стояла — надписью, которую нельзя показать.
            if (TakenByTable(Vk.Return)) taken.Add("Fn+Enter");
            else mine.Add("Fn+Enter — это Insert");

            string said = String.Join(", ", mine.ToArray()) + ".";

            if (taken.Count > 0)
                said += " " + String.Join(" и ", taken.ToArray()) +
                        (taken.Count > 1 ? " достаются" : " достаётся") +
                        " сочетаниям macOS: их видно списком на первой странице, и там же " +
                        "их можно выключить по одному.";

            // ⌘+Tab стоит ещё выше таблицы и своей настройкой не делится ни с кем.
            if (FnSubstituteMod == MacMod.Cmd && _s.CmdTabSwitchesWindows)
                said += " А Fn+Tab открывает переключатель окон: выбранная клавиша " +
                        "приходит в Windows как ⌘.";

            return said;
        }

        /// <summary>
        /// Что клавиша-заменитель Fn приносит в Windows. От этого зависит, кто первым
        /// возьмёт сочетание — таблица macOS или навигация: перехват спрашивает
        /// назначение клавиши, а не её надпись.
        /// </summary>
        private MacMod FnSubstituteMod
        {
            get
            {
                if (_s.FnSubstitute == ModKey.None) return MacMod.None;
                switch (_s.TargetOf(_s.FnSubstitute))
                {
                    case ModKey.LWin: case ModKey.RWin: return MacMod.Cmd;
                    case ModKey.LAlt: case ModKey.RAlt: return MacMod.Opt;
                    case ModKey.LCtrl: case ModKey.RCtrl: return MacMod.Ctrl;
                    case ModKey.LShift: case ModKey.RShift: return MacMod.Shift;
                    default: return MacMod.None;
                }
            }
        }

        /// <summary>
        /// Заберёт ли таблица macOS это сочетание у навигации Fn.
        ///
        /// Спрашиваем саму таблицу, а не список известных наперёд строк. Прежде здесь
        /// стояло «заменитель приходит как ⌥» и три имени сочетаний — а список
        /// заменителей предлагает и клавиши Win, то есть ⌘, и у ⌘ таблица забирает
        /// больше: ⌘↑ и ⌘↓ уводят в начало и конец документа, а ⌘Backspace стирает
        /// строку до курсора. Сноска при этом обещала листание страницы и удаление
        /// одного знака.
        /// </summary>
        private bool TakenByTable(int vk)
        {
            MacMod mm = FnSubstituteMod;
            if (!_s.MacShortcuts || mm == MacMod.None) return false;
            MacShortcut sc = MacKeys.Find(vk, mm);
            return sc != null && _s.MacEnabled(sc.Id);
        }

        /// <summary>
        /// На какой клавише сейчас живёт ⌘. Пустая строка — на своей; null — ни на какой.
        /// Спрашивать надо именно так: слой сочетаний перебирает нажатые клавиши
        /// и смотрит, во что каждая превращается, — а не на надпись.
        /// </summary>
        private string CmdLivesOn()
        {
            var moved = new List<string>();
            bool own = false;
            ModKey[] all =
            {
                ModKey.CapsLock, ModKey.LCtrl, ModKey.LAlt, ModKey.RAlt,
                ModKey.LWin, ModKey.RWin
            };
            string[] titles =
            {
                "Caps Lock", "control (левый)", "⌥ option (левый)", "⌥ option (правый)",
                "⌘ command (левая)", "⌘ command (правая)"
            };
            for (int i = 0; i < all.Length; i++)
            {
                ModKey t = _s.TargetOf(all[i]);
                if (t != ModKey.LWin && t != ModKey.RWin) continue;
                if (all[i] == ModKey.LWin || all[i] == ModKey.RWin) own = true;
                else moved.Add(titles[i]);
            }
            if (own) return "";
            if (moved.Count == 0) return null;
            return String.Join(" и ", moved.ToArray());
        }

        /// <summary>Сочетания macOS: общий выключатель и таблица по разделам.</summary>
        private UIElement PageMacKeys()
        {
            var stack = new StackPanel();

            stack.Children.Add(StateCard());

            if (!String.IsNullOrEmpty(Settings.LoadFailed))
                stack.Children.Add(Card("Прежние настройки не прочитались", null,
                    Note("Файл настроек оказался испорчен — оборванной записью, чужой правкой, " +
                         "сбоем диска. Программа взяла заводские, а прежний файл отложила " +
                         "рядом, чтобы было куда посмотреть:\n\n" + Settings.LoadFailed)));

            // Слой сочетаний узнаёт ⌘ по назначению, а не по надписи на клавише. После
            // готовой схемы «Как в Windows» ⌘ переезжает на клавишу ⌥, и молчать об этом
            // нельзя: на клавише написано одно, а копирует другая.
            string cmdOn = CmdLivesOn();
            stack.Children.Add(Card("Сочетания macOS", null,
                Toggle("⌘C, ⌘←, ⌘Q и остальные работают как на маке", _s.MacShortcuts,
                    delegate(bool v) { _s.MacShortcuts = v; Save(); BuildPage(); }),
                Note(cmdOn == null
                    ? "Сейчас ни одна клавиша не работает как ⌘ — её увели переназначения " +
                      "на странице «Клавиши». Нажать сочетания ниже нечем."
                    : cmdOn.Length == 0
                    ? "⌘ остаётся клавишей Windows: программа не меняет её назначение, " +
                      "а переводит сами сочетания. Поэтому получается и то, чего заменой " +
                      "клавиш не добиться: ⌘← уходит в начало строки, ⌘Q закрывает программу."
                    : "Программа не меняет назначение клавиш, а переводит сами сочетания — " +
                      "поэтому получается и то, чего заменой не добиться: ⌘← уходит в начало " +
                      "строки, ⌘Q закрывает программу. Но саму ⌘ увели переназначения: " +
                      "сейчас её роль играет " + cmdOn + ", а клавиша с надписью ⌘ работает " +
                      "иначе — см. страницу «Клавиши».")));

            // Переключение окон — до раннего возврата: ⌘+Tab работает независимо
            // от общего выключателя (перехват спрашивает свою настройку), и, спрятав
            // карточку вместе с остальными, мы отбирали у настройки единственного хозяина.
            stack.Children.Add(Card("Переключение окон", null,
                Toggle("⌘+Tab переключает окна, как Alt+Tab", _s.CmdTabSwitchesWindows,
                    delegate(bool v) { _s.CmdTabSwitchesWindows = v; Save(); })));

            if (!_s.MacShortcuts) return stack;

            // Один вопрос вместо двух. На маке поиск живёт на одной из двух клавиш,
            // и второй достаётся переключение языка — спрашивать про обе по отдельности
            // значит позволить выбрать бессмыслицу.
            Choice[] spaceChoices =
            {
                new Choice { Value = "cmd",  Text = "⌘ + пробел" },
                new Choice { Value = "ctrl", Text = "control + пробел" },
                new Choice { Value = "none", Text = "Не трогать пробел" }
            };
            string spaceNow = _s.SpaceSearch;

            stack.Children.Add(Card("Поиск и язык", null,
                Row("Поиск открывается по", null, Combo(spaceChoices, spaceNow,
                    delegate(object v)
                    {
                        _s.SpaceSearch = (string)v;
                        // Перестраиваем: рядом стоит текст, который зависит от этого же
                        // выбора, — без этого он обещал язык там, где пробел не трогают.
                        Save(); BuildPage();
                    })),
                Note(_s.SpaceSearch == Settings.SpaceNone
                        ? "Пробел с модификатором остаётся программам как есть."
                        : "Второй клавише достаётся переключение языка — через собственный " +
                          "переключатель Windows.")));

            // Три группы вместо шести десятков строк. Человек либо хочет всё — а хочет он
            // всё почти всегда, — либо ему мешает ровно одно сочетание; ради второго
            // случая держать полсотни переключателей на виду не стоит.
            string[] groups = { MacKeys.GroupEdit, MacKeys.GroupText, MacKeys.GroupSystem };
            string[] hints =
            {
                "Копировать, вставить, найти, вкладки.",
                "Перемещение и правка текста — то, чего на Windows больше всего не хватает.",
                "Окна, снимки экрана, параметры."
            };

            for (int g = 0; g < groups.Length; g++)
            {
                string group = groups[g];
                var rows = new StackPanel();

                int on = 0, all = 0;
                var sample = new List<string>();
                foreach (MacShortcut sc in MacKeys.All)
                {
                    if (sc.Group != group) continue;
                    all++;
                    if (_s.MacEnabled(sc.Id)) on++;
                    if (sample.Count < 6) sample.Add(sc.Mac);
                }

                // Переключатель отвечает «включено хоть одно», и это надо сказать вслух:
                // при одном включённом из двадцати он выглядел так же, как при всех
                // двадцати. Щелчок по нему по-прежнему правит всю группу разом —
                // теперь хотя бы видно, что именно он собирается переписать.
                int onNow = on, allNow = all;
                rows.Children.Add(Toggle(hints[g], on > 0, delegate(bool v)
                {
                    foreach (MacShortcut sc in MacKeys.All)
                        if (sc.Group == group) _s.MacSet(sc.Id, v);
                    Save(); BuildPage();
                }));
                rows.Children.Add(Note(String.Join(" · ", sample.ToArray()) +
                                       (all > sample.Count ? " и ещё " + (all - sample.Count) : "") +
                                       (onNow > 0 && onNow < allNow
                                            ? "  ·  включено " + onNow + " из " + allNow
                                            : "")));

                if (_macOpen == group)
                {
                    foreach (MacShortcut sc in MacKeys.All)
                        if (sc.Group == group) rows.Children.Add(MacRow(sc));
                }

                string g2 = group;
                rows.Children.Add(LinkButton(_macOpen == group ? "Свернуть" : "Показать все " + all,
                    delegate { _macOpen = _macOpen == g2 ? null : g2; BuildPage(); }));

                stack.Children.Add(Card(group, null, rows));
            }

            return stack;
        }

        /// <summary>Какая группа сочетаний сейчас развёрнута. Пусто — все свёрнуты.</summary>
        private string _macOpen;

        /// <summary>
        /// Состояние клавиатуры одной карточкой: что подключено, заряд и когда программа
        /// работает. Три вопроса, на которые человек хочет ответ сразу, а не после того,
        /// как найдёт нужную страницу.
        /// </summary>
        private UIElement StateCard()
        {
            var lines = new StackPanel();

            AppleModel m = Devices.AppleModel;
            int battery = KeyboardBattery.Percent;
            string what = m != null ? m.Name : (Devices.AppleConnected ? "Клавиатура Apple" : "Клавиатура Apple не найдена");
            if (battery >= 0) what += " · заряд " + battery + " %";
            lines.Children.Add(new TextBlock { Text = what, TextWrapping = TextWrapping.Wrap });

            // Один трёхпозиционный выбор вместо двух галочек: из их четырёх сочетаний
            // осмысленны три, а «выключено, но приостанавливать» не значит ничего.
            Choice[] when =
            {
                new Choice { Value = "apple", Text = "Только с Magic Keyboard" },
                new Choice { Value = "always", Text = "На любой клавиатуре" },
                new Choice { Value = "off",    Text = "Выключены" }
            };
            string nowWhen = !_s.Enabled ? "off" : (_s.PauseWhenAppleAbsent ? "apple" : "always");

            lines.Children.Add(Row("Переназначения", null, Combo(when, nowWhen, delegate(object v)
            {
                string pick = (string)v;
                // Выключение — ответ на свой вопрос, и чужой ответ оно не трогает.
                // Иначе «Выключены» молча переводило «На любой клавиатуре» в «Только
                // с Magic Keyboard», а вернуть переназначения можно из значка в трее —
                // он трогает только общий выключатель, и подмены никто не заметит.
                _s.Enabled = pick != "off";
                if (pick != "off") _s.PauseWhenAppleAbsent = pick != "always";
                // Перестраиваем: предупреждение «правила действуют на весь ввод» нужно
                // ровно тому, кто только что выбрал «на любой клавиатуре».
                Save(); BuildPage();
            })));

            if (_s.Enabled && !_s.PauseWhenAppleAbsent)
                lines.Children.Add(Note("Перехват не знает, с какой клавиатуры пришло нажатие, " +
                                        "поэтому правила действуют на весь ввод — включая встроенную " +
                                        "клавиатуру ноутбука."));

            // Без сырого ввода программа перестаёт узнавать клавиатуру и, что хуже,
            // сторожить собственный перехват — сравнивать его показания становится не с чем.
            if (!String.IsNullOrEmpty(KeyWatch.Failure))
                lines.Children.Add(Note(KeyWatch.Failure + " Переназначения " +
                                        "работают, модель клавиатуры известна, а вот всё, что узнаётся " +
                                        "по нажатиям, — нет: исполнение клавиатуры, верхние F-клавиши, " +
                                        "клавиша Eject, страница «Диагностика» и яркость от кодов " +
                                        "драйвера Apple. Помогает перезапуск программы."));

            return Card(null, null, lines);
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
                VerticalAlignment = VerticalAlignment.Center,
                // Подпись самому переключателю, а не только соседней надписи: без неё
                // до неё не дотянуться ни чтению с экрана, ни стенду — полсотни строк
                // отчитывались как «переключатель без подписи».
                ToolTip = sc.Mac + "   " + sc.Title
            };
            System.Windows.Automation.AutomationProperties.SetName(cb, sc.Mac + "   " + sc.Title);
            // Перестраиваем: над строками стоит переключатель всей группы, и он считает
            // себя по ним. Без перестройки он оставался выключенным над включённой строкой.
            cb.Checked += delegate { if (!_building) { _s.MacSet(id, true); Save(); BuildPage(); } };
            cb.Unchecked += delegate { if (!_building) { _s.MacSet(id, false); Save(); BuildPage(); } };
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
        /// Снять подписку страницы проверки.
        ///
        /// Накопленное не трогает: пересборка той же страницы — не уход с глаз.
        /// Стирает его соседний ForgetSelfTest, и зовут его там, где окно правда уходит.
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
        }

        /// <summary>
        /// То же и вдобавок забыть накопленное. Разделено нарочно: пересборка той же
        /// страницы — не уход с глаз. А пересобирается она сама и часто: марка показанного
        /// включает и первый медиакод, и число замеченных клавиш, и ответ о заряде, —
        /// то есть ровно те события, ради которых человек сюда и пришёл. Список стирался
        /// той самой строкой, которую только что показал.
        /// </summary>
        private void ForgetSelfTest()
        {
            // Стираем и то, что уже нарисовано. Подписку снять мало: окно может остаться
            // видимым на другом мониторе, и последние два десятка нажатий висели бы
            // на экране сколько угодно — при том что текст карточки обещает обратное.
            if (_selfTestLog != null) _selfTestLog.Text = "";
            DetachSelfTest();
            _selfTestLines.Clear();
        }

        /// <summary>
        /// Живой просмотр того, что клавиатура на самом деле присылает. Смотрит сырой
        /// ввод, а не собственный перехват: так видно устройство напрямую, независимо
        /// от того, что программа с нажатием потом делает.
        /// </summary>
        private void AddSelfTestCards(StackPanel stack)
        {

            AppleDriver.Refresh(false);
            bool yielding = YieldingNow;

            stack.Children.Add(Card("Как это читать", null,
                Note("Нажимайте клавиши по одной — ниже появится, что именно дошло до Windows. " +
                     "Проверка слушает сырой ввод, поэтому видит клавиатуру напрямую и не зависит " +
                     "от переназначений программы — но только пока это окно впереди. " +
                     "Уходя из-под фокуса, оно перестаёт слушать и стирает набранное: " +
                     "в спрятанном окне не должно копиться то, что вы печатаете в другом.\n\n" +
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
                ToWindow(delegate { OnSelfTestEvent(e); });
            };
            KeyWatch.Activity += _selfTest;

            RefreshSelfTest();
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

            int keys = Models.FunctionKeyCount();
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

        /// <summary>Размер скачанного, посчитанный в стороне. −1 — ещё не считали.</summary>
        private long _cacheBytes = -1;

        /// <summary>Найденный 7-Zip, посчитанный там же. Пустая строка — искали, не нашли.</summary>
        private string _sevenZip;

        /// <summary>Идёт ли счёт прямо сейчас — чтобы не заводить второй.</summary>
        private bool _probing;

        /// <summary>Для какой страницы уже посчитано. Иначе пересборка звала бы счёт,
        /// а счёт — пересборку, и они гоняли бы друг друга без остановки.</summary>
        private string _probedFor;

        /// <summary>
        /// Обойти кэш и найти 7-Zip — в стороне от потока окна.
        ///
        /// Обе работы долгие и обе повторялись на каждой пересборке страницы, а
        /// пересобирается она сама: от ответа о заряде, от впервые нажатой клавиши,
        /// от опроса устройств. CacheSize перебирает всё дерево распакованного Boot Camp
        /// — десятки тысяч файлов, — а поиск 7-Zip считает SHA-256 двух с лишним
        /// мегабайт. Окно на это время замирало, в том числе прямо во время закачки,
        /// которая в тот же каталог и пишет.
        /// </summary>
        private void ProbeDriverPage()
        {
            if (_probing || _probedFor == "driver") return;
            _probedFor = "driver";
            _probing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                long bytes = -1;
                string zip = null;
                try { bytes = AppleDriverSetup.CacheSize(); } catch { }
                try { zip = AppleDriverSetup.SevenZip(); } catch { }
                ToWindow(delegate
                {
                    _probing = false;
                    _cacheBytes = bytes;
                    _sevenZip = zip == null ? "" : zip;
                    if (CurrentPage == "driver") BuildPage();
                });
            });
        }

        private UIElement PageDriver()
        {
            var stack = new StackPanel();
            // Не force: значение обновляет сторожевой таймер, а после своих же правок
            // (запись режима, удаление драйвера) мы перечитываем его сами и в стороне.
            AppleDriver.Refresh(false);
            ProbeDriverPage();
            bool installed = AppleDriver.Installed;

            var status = new StackPanel();
            status.Children.Add(KeyValue("Драйвер Apple", installed ? "установлен" : "не установлен"));
            if (installed)
            {
                status.Children.Add(KeyValue("Состояние", AppleDriver.Active ? "включена" : "отключена"));
                int fb = AppleDriver.FnBehavior;
                if (fb >= 0)
                    status.Children.Add(KeyValue("Верхний ряд у драйвера",
                        fb == 1 ? "медиа сразу, как на маке" : "F-клавиши сразу"));
                // Имя значения и его ветка реестра — для того, кто полезет чинить руками.
                if (_s.DeveloperMode && !String.IsNullOrEmpty(AppleDriver.FnBehaviorPath))
                    status.Children.Add(KeyValue("OSXFnBehavior", AppleDriver.FnBehaviorPath));

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
                // Пока идёт работа с драйвером, кнопки гасим: второй щелчок дал бы
                // второй запрос прав администратора поверх первого.
                modes.IsEnabled = !SetupBusy;
                modes.Children.Add(PresetButton(
                    "Медиа сразу — как на маке" + (fb == 1 ? "  ·  сейчас так" : ""),
                    "F1–F12 сами дают громкость и перемотку, обычные F-клавиши — через Fn. " +
                    "Делает это драйвер; MagicKeys функциональный ряд не трогает. " +
                    "Яркость остаётся: драйвер переводит F1 и F2 в коды, которые Windows " +
                    "применяет только к панели ноутбука, — программа ловит их и отдаёт " +
                    "внешним мониторам сама, пока переназначения включены.",
                    delegate { ApplyFnBehavior(1); }));
                modes.Children.Add(PresetButton(
                    "F-клавиши сразу" + (fb == 0 ? "  ·  сейчас так" : ""),
                    "F1–F12 приходят обычными F-клавишами, и переназначает их MagicKeys — " +
                    "яркостью внешних мониторов занимается программа.",
                    delegate { ApplyFnBehavior(0); }));
                string driverSay = _driverBusy ? _driverText : _fnNotice;
                if (!String.IsNullOrEmpty(driverSay))
                {
                    var outcome = new TextBlock
                    {
                        Text = driverSay,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 10, 0, 0)
                    };
                    outcome.SetResourceReference(StyleProperty, "Caption");
                    modes.Children.Add(outcome);
                }

                if (AppleDriver.TakesFunctionRow && !KeyWatch.MediaSeen)
                {
                    // Медиакоды считает перепись клавиш. Оборвись она — их не будет
                    // никогда, и «драйвер молчит» окажется не про драйвер, а про нашу
                    // собственную слепоту. Тогда и совет другой: переставлять драйвер
                    // с USB на Bluetooth незачем, а «Диагностика» слепа ровно так же.
                    bool blind = !String.IsNullOrEmpty(KeyWatch.Failure);
                    stack.Children.Add(blind
                        ? Card("Проверить, забирает ли драйвер ряд, сейчас нечем", null,
                            Note("В реестре сказано, что медиаклавиши делает драйвер. Так ли это " +
                                 "на деле, программа узнаёт по приходящим медиакодам — а их она " +
                                 "сейчас не видит: " + KeyWatch.Failure + "\n\n" +
                                 "Пока это так, программа ряд себе не отдаёт: она берёт F1–F12 " +
                                 "на себя. Если драйвер их тоже забирает, верхний ряд может " +
                                 "не работать вовсе — до перезапуска программы."))
                        : Card("Драйвер настроен забирать ряд, но пока молчит", null,
                            Note("В реестре сказано, что медиаклавиши делает драйвер, однако ни одного " +
                                 "медиакода с клавиатуры ещё не приходило. Так бывает, когда драйвер " +
                                 "не подключился к тому пути, по которому клавиатура работает сейчас — " +
                                 "например, поставлен для USB, а клавиатура на Bluetooth.\n\n" +
                                 "Пока это так, программа ряд себе не отдаёт молча: она берёт F1–F12 " +
                                 "на себя, чтобы они не оказались мёртвыми. Как только придёт первый " +
                                 "медиакод, она снова уступит драйверу.\n\n" +
                                 "Что именно приходит, видно на «Диагностике» — одного нажатия достаточно."),
                            _pages.Contains("diag")
                                ? (UIElement)LinkButton("Открыть диагностику", delegate { GoTo("diag"); })
                                : (UIElement)Note("Что приходит с клавиатуры, видно на странице «Диагностика» — " +
                                       "она открывается в режиме разработчика.")));
                }

                stack.Children.Add(Card("Кто занимается функциональным рядом", null,
                    Note("Драйвер даёт медиаклавиши и настоящую Fn, но переназначать верхний " +
                         "ряд по-своему с ним нельзя: этим занимается он. MagicKeys переназначает " +
                         "как угодно, но действует на весь ввод, а не только на клавиатуру Apple. " +
                         "Яркость внешних мониторов работает в обоих случаях — коды от драйвера " +
                         "программа ловит и отдаёт мониторам сама."),
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
                Note("Драйвер и программа делают одно и то же — переназначают F1–F12. Если работают " +
                     "оба сразу, нажатие переназначается дважды и получается ерунда. Поэтому " +
                     "программа отдаёт функциональный ряд драйверу, а всё остальное — модификаторы, " +
                     "раскладки, навигацию Fn, цифровой блок, яркость — продолжает делать сама.\n\n" +
                     "Уступает она с разбором: только когда драйвер ряд действительно забирает. " +
                     "В режиме «F-клавиши сразу» уступать нечего, и тогда F1–F12 переназначает " +
                     "MagicKeys.")));

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
            // null — ещё ищем, пустая строка — искали и не нашли. Разницу нельзя терять:
            // поиск идёт в стороне и занимает секунды (обход распакованного Boot Camp,
            // потом SHA-256 двух мегабайт), и всё это время страница уверяла, что 7-Zip
            // нет, — на машине, где он установлен.
            string sevenZip = String.IsNullOrEmpty(_sevenZip) ? null : _sevenZip;
            bool stillLooking = _sevenZip == null;
            _setupLog = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
            _setupLog.SetResourceReference(StyleProperty, "Caption");
            _setupLog.Text = _setupText != null ? _setupText
                : stillLooking
                ? "Ищу 7-Zip…"
                : sevenZip != null
                ? "7-Zip найден: " + sevenZip
                : "7-Zip не найден. Он нужен для распаковки: внутри пакета Apple лежит образ DMG, "
                  + "а его встроенные средства Windows читать не умеют. В программу он не вложен, "
                  + "но она может забрать официальный установщик сама — проверив подпись и ничего "
                  + "не устанавливая в систему. Это произойдёт по кнопке «Скачать и установить».";

            var buttons = new WrapPanel { Margin = new Thickness(0, 0, -8, -8) };

            // Пока идёт работа с драйвером — гасим: удаление и распаковка ходят
            // в один каталог, а запрос прав администратора уже висит наверху.
            var find = new Button
            {
                Content = "Найти пакет у Apple",
                Margin = new Thickness(0, 0, 8, 8),
                IsEnabled = !SetupBusy
            };
            find.SetResourceReference(StyleProperty, "Btn");
            find.Click += delegate { StartSetup(false, null); };
            buttons.Children.Add(find);

            var get = new Button
            {
                Content = "Скачать и установить",
                Margin = new Thickness(0, 0, 8, 8),
                IsEnabled = !SetupBusy
            };
            get.SetResourceReference(StyleProperty, "BtnAccent");
            get.Click += delegate { StartSetup(true, null); };
            buttons.Children.Add(get);

            // Видимость — из состояния, а не из памяти о нажатии. Страница пересобирается
            // сама по себе: пришла новая клавиатура, обновился заряд, сменилась тема, —
            // и кнопка пропадала посреди семисотмегабайтной закачки, а остановить работу
            // становилось нечем.
            _setupStop = new Button
            {
                Content = "Остановить",
                Margin = new Thickness(0, 0, 8, 8),
                // По отменяемому шагу, а не по «жив ли поток». Просьбу об отмене читает
                // только закачка: распаковка, добыча 7-Zip и pnputil её не спрашивают,
                // а кнопка висела и над ними — гасла от щелчка и не делала ничего.
                Visibility = _setupCancellable ? Visibility.Visible : Visibility.Collapsed
            };
            _setupStop.SetResourceReference(StyleProperty, "Btn");
            _setupStop.Click += delegate
            {
                ManualResetEvent c = _setupCancel;
                if (c != null) c.Set();
                _setupStop.IsEnabled = false;
            };
            buttons.Children.Add(_setupStop);

            var pick = new Button
            {
                Content = "Указать распакованную папку…",
                Margin = new Thickness(0, 0, 8, 8),
                IsEnabled = !SetupBusy
            };
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

            long cached = _cacheBytes;
            if (cached > 0)
            {
                var wipe = new Button
                {
                    Content = "Убрать скачанное и распакованное (" + (cached / 1024 / 1024) + " МБ)",
                    Margin = new Thickness(0, 0, 8, 8)
                };
                wipe.SetResourceReference(StyleProperty, "Btn");
                wipe.IsEnabled = !SetupBusy;
                wipe.Click += delegate
                {
                    if (SetupBusy)
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
                    _probedFor = null;   // кэша не стало — размер считаем заново
                    BuildPage();
                };
                buttons.Children.Add(wipe);
            }

            if (sevenZip == null && !stillLooking)
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
                var remove = new Button
                {
                    Content = "Удалить драйвер",
                    Margin = new Thickness(0, 0, 8, 8),
                    IsEnabled = !SetupBusy
                };
                remove.SetResourceReference(StyleProperty, "Btn");
                remove.Click += delegate
                {
                    // Тот же вопрос, что и у StartSetup, и по той же причине: погашенной
                    // эта кнопка становится только на сборке страницы, а установка
                    // из указанной папки страницу не пересобирает вовсе. Без проверки
                    // щелчок запускал удаление драйвера, пока другой поток ставит его же
                    // из того же каталога, — и вторым запросом прав поверх первого.
                    if (SetupBusy) { SetupSay("Уже идёт работа, дождитесь конца."); return; }

                    // В стороне: внутри ждём установщик драйверов Windows до пяти минут,
                    // и всё это время наверху висит запрос прав. На потоке окна это
                    // означало бы «программа не отвечает» ровно тогда, когда человек
                    // на неё смотрит.
                    _driverBusy = true;
                    _driverText = "Удаляю драйвер…";
                    BuildPage();

                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        string output;
                        bool ok = false;
                        try { ok = AppleDriverSetup.Uninstall("keymagic2.inf", out output); }
                        catch (Exception e) { output = e.Message; }
                        try { AppleDriver.Refresh(true); } catch { }

                        string say = (ok ? "Драйвер удалён." : "Удалить не вышло.")
                                   + (String.IsNullOrEmpty(output) ? "" : "\n\n" + output);
                        ToWindow(delegate
                        {
                            _driverBusy = false;
                            _driverText = null;
                            if (CurrentPage != "driver") return;
                            _fnNotice = say;
                            BuildPage();
                        });
                    });
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
                Bullet("Что делает верхний ряд, решает сам драйвер. Настройка чужая, поэтому " +
                       "программа меняет её только по нажатию кнопки выше и только с правами " +
                       "администратора: их спросит Windows.")));

            return stack;
        }

        /// <summary>Строка о ходе добычи драйвера — обновляется из фонового потока.</summary>
        private TextBlock _setupLog;
        private Thread _setupThread;

        /// <summary>
        /// Просьба остановить скачивание. Семьсот мегабайт человеку надо уметь
        /// прервать: проверка стояла на каждом куске, а способа её подать не было.
        /// </summary>
        private ManualResetEvent _setupCancel;
        private Button _setupStop;

        /// <summary>Просили ли остановиться. Спрашивается между шагами длинной работы.</summary>
        private bool Cancelled
        {
            get { ManualResetEvent c = _setupCancel; return c != null && c.WaitOne(0); }
        }

        /// <summary>Последнее сказанное о ходе работы — чтобы пережить перестройку страницы.</summary>
        private volatile string _setupText;

        /// <summary>
        /// Чем кончилась последняя проверка или загрузка обновления. Отдельным полем
        /// по той же причине, что и всё в этой карточке: страница пересобирается сама —
        /// от ответа о заряде, от впервые нажатой клавиши, — и сообщение об отказе
        /// исчезало, а на его месте появлялось «Проверено N дней назад», хотя проверка
        /// только что не удалась и отметку о ней никто не ставил.
        /// </summary>
        private string _updSaid;

        /// <summary>
        /// Идёт шаг, который правда можно остановить. Просьбу об отмене читает одна
        /// закачка; распаковка, добыча 7-Zip и pnputil доводят дело до конца, что бы
        /// им ни говорили, — и кнопка над ними была бы обещанием, которого никто
        /// не исполнит.
        /// </summary>
        private volatile bool _setupCancellable;

        /// <summary>Идёт долгая работа с драйвером: удаление или запись его настройки.</summary>
        private bool _driverBusy;

        /// <summary>
        /// Что сейчас происходит с драйвером. Отдельно от _fnNotice: тот живёт ровно одну
        /// пересборку — так и надо отчёту о законченном деле, — а ход работы обязан
        /// пережить любое их число. Страница пересобирается сама: пришёл ответ о заряде,
        /// впервые нажали клавишу, опросились устройства. Надпись «Удаляю драйвер…»
        /// исчезала, кнопки оставались серыми, и объяснить это было нечем.
        /// </summary>
        private volatile string _driverText;

        /// <summary>
        /// Работа с драйвером кончилась: кнопки обязаны ожить сами.
        ///
        /// Поток обнуляем здесь, а не полагаемся на «жив ли он»: мы внутри него, и до
        /// самого выхода он жив. Из-за этого последняя пересборка страницы всегда
        /// приходилась на «идёт работа», и кнопки оставались серыми до тех пор, пока
        /// страницу не пересоберёт что-то постороннее — уход на другую страницу
        /// и возврат. Нажать «Остановить» и остаться без единой живой кнопки было
        /// обычным делом.
        /// </summary>
        private void SetupFinished()
        {
            _setupThread = null;
            _setupCancellable = false;
            ToWindow(delegate
            {
                if (_setupStop != null)
                {
                    _setupStop.Visibility = Visibility.Collapsed;
                    _setupStop.IsEnabled = true;
                }
                if (CurrentPage == "driver") BuildPage();
            });
        }

        private bool SetupBusy
        {
            get { return _driverBusy || (_setupThread != null && _setupThread.IsAlive); }
        }

        private void SetupSay(string text)
        {
            _setupText = text;
            ToWindow(delegate { if (_setupLog != null) _setupLog.Text = text; });
        }

        /// <summary>
        /// Отдать работу потоку окна — но только пока оно есть. После выхода диспетчер
        /// завершён, и BeginInvoke бросает исключение прямо на фоновом потоке, где его
        /// никто не ловит: «Выход» посреди закачки почти наверняка попадал в эту секунду.
        /// </summary>
        private void ToWindow(Action what)
        {
            try { Dispatcher.BeginInvoke(what); }
            catch (Exception e) { Diag.Log("окно уже закрыто, работа не передана", e); }
        }

        /// <summary>
        /// Весь путь: каталог Apple → скачивание → распаковка → установка.
        /// Если задана готовая папка, первые два шага пропускаются.
        /// </summary>
        private void StartSetup(bool install, string readyFolder)
        {
            // SetupBusy, а не только свой поток: удаление драйвера и распаковка ходят
            // в один каталог, а запрос прав администратора уже висит наверху.
            if (SetupBusy) { SetupSay("Уже идёт работа, дождитесь конца."); return; }
            _setupCancellable = false;

            // Прежний освобождаем: дескриптор ожидания — вещь неуправляемая,
            // и копить их по одному на каждое нажатие кнопки незачем.
            ManualResetEvent old = _setupCancel;
            if (old != null) try { old.Close(); } catch { }
            _setupCancel = new ManualResetEvent(false);
            // Кнопку здесь не показываем: видимость выводится из _setupCancellable,
            // а он поднимается только вокруг закачки. Показанная сразу, она висела над
            // поиском пакета и добычей 7-Zip — шагами, которые просьбу об отмене
            // не читают вовсе, — и щелчок по ней взводил отмену впрок: закачка, начавшись,
            // тут же возвращала «отменено» из-за нажатия, сделанного минуту назад.

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
                        // Семьсот мегабайт человеку надо уметь остановить. Просьба об отмене
                        // и так проверяется на каждом куске — не хватало только способа
                        // её подать; кнопка «Остановить» появляется рядом с ходом загрузки.
                        // Признак поднимаем ровно вокруг закачки: она одна эту просьбу
                        // и читает, а над распаковкой кнопка была бы обещанием впустую.
                        _setupCancellable = true;
                        ToWindow(delegate { if (CurrentPage == "driver") BuildPage(); });
                        bool got;
                        try
                        {
                            got = AppleDriverSetup.DownloadFromApple(url, package,
                                delegate(double part, string what) { SetupSay(head + "\n\n" + what); },
                                _setupCancel, out error);
                        }
                        finally
                        {
                            _setupCancellable = false;
                            ToWindow(delegate { if (CurrentPage == "driver") BuildPage(); });
                        }
                        if (!got)
                        {
                            SetupSay(head + (error == "отменено"
                                ? "\n\nОстановлено."
                                : "\n\nСкачать не вышло: " + error));
                            return;
                        }

                        if (Cancelled) { SetupSay(head + "\n\nОстановлено."); return; }

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

                    // Совет один. При коде 3010 pnputil просит перезагрузиться, и это
                    // вместо «переподключите клавиатуру», а не в придачу к нему: два
                    // совета подряд, да ещё противоположных, хуже одного.
                    string tail = ok
                        ? "Готово. Драйвер установлен: " + inf + "\n\n" +
                          (String.IsNullOrEmpty(output)
                            ? "Переподключите клавиатуру, чтобы он вступил в силу."
                            : output.Trim())
                        : "Установить не удалось. Чаще всего это значит, что запрос прав администратора " +
                          "был отклонён — тогда в системе ничего не изменилось. Можно повторить " +
                          "или поставить вручную: правый щелчок по Keymagic2.inf → «Установить»." +
                          (String.IsNullOrEmpty(output) ? "" : "\n\n" + output.Trim());
                    SetupSay(tail);

                    // По ключу, а не по номеру: номер остался от прежней нумерации
                    // и после появления режима разработчика указывал уже на другую
                    // страницу — так что после установки драйвера она не обновлялась
                    // никогда, и человек видел прежнее «не установлен».
                    ToWindow(delegate { _probedFor = null; if (CurrentPage == "driver") BuildPage(); });
                }
                catch (Exception e)
                {
                    SetupSay("Работа прервалась: добыть драйвер не удалось.\n\n" +
                             "Что сообщила система: " + e.Message + "\n\n" +
                             "Можно скачать пакет Boot Camp самостоятельно и указать " +
                             "распакованную папку кнопкой выше.");
                }
                finally { SetupFinished(); }
            });
            _setupThread.IsBackground = true;
            _setupThread.Start();
        }

        /// <summary>
        /// О программе. Склад тот же, что у соседних программ: крупный значок, название,
        /// одна строка о том, что это, три числа, автор, ссылка на исходники и подвал.
        ///
        /// Здесь нарочно нет ни перечня возможностей, ни объяснения лицензии. Человек
        /// заходит сюда за версией и за тем, куда написать, — а не читать про программу,
        /// которая у него уже установлена.
        /// </summary>
        private UIElement PageAbout()
        {
            var column = new StackPanel
            {
                // Ширина, а не MaxWidth: иначе колонка сжимается по содержимому и карточки
                // перестают быть карточками — их края совпадают с краями текста.
                Width = 420,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 24)
            };

            column.Children.Add(AboutMark());
            column.Children.Add(AboutName());
            column.Children.Add(AboutTagline());
            column.Children.Add(AboutNumbers());
            column.Children.Add(AboutAuthor());
            column.Children.Add(AboutUpdate());
            column.Children.Add(StartupCard());
            column.Children.Add(AboutButtons());
            column.Children.Add(AboutFooter());

            if (_s.DeveloperMode)
                column.Children.Add(Card("Режим разработчика", "Включён",
                    Toggle("Показывать служебные страницы и настройки", _s.DeveloperMode,
                        delegate(bool v) { _s.DeveloperMode = v; Save(); FillNav(); BuildPage(); }),
                    Note("Открывает «Диагностику», а на других страницах — " +
                         "исполнение клавиатуры и коды языков ввода.")));

            return column;
        }

        /// <summary>Значок берётся из самой программы: там он многоразмерный и потому чёткий.</summary>
        private static UIElement AboutMark()
        {
            var image = new System.Windows.Controls.Image
            {
                Width = 96,
                Height = 96,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
            try
            {
                IntPtr icon = Native.LoadImageW(Native.GetModuleHandleW(null), "#32512",
                                                Native.IMAGE_ICON, 256, 256, 0);
                if (icon != IntPtr.Zero)
                {
                    try
                    {
                        image.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon, Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                    finally { Native.DestroyIcon(icon); }
                }
            }
            catch { /* без значка страница всё равно читается */ }
            return image;
        }

        /// <summary>Название. Пять щелчков по нему включают режим разработчика.</summary>
        private UIElement AboutName()
        {
            var name = new TextBlock
            {
                Text = "MagicKeys",
                FontSize = 34,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            name.SetResourceReference(TextBlock.FontFamilyProperty, "UiFontDisplay");

            // Приём старый и намеренно неочевидный: обычному человеку служебные
            // страницы только мешают, а тому, кто их ищет, подсказка не нужна.
            name.MouseLeftButtonUp += delegate
            {
                if (++_aboutClicks < 5) return;
                _aboutClicks = 0;
                _s.DeveloperMode = !_s.DeveloperMode;
                Save();
                FillNav();
                BuildPage();
            };
            return name;
        }

        private static UIElement AboutTagline()
        {
            var t = new TextBlock
            {
                Text = "Клавиатуры Apple в Windows 11",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 18)
            };
            t.SetResourceReference(StyleProperty, "Caption");
            return t;
        }

        /// <summary>Два числа в строку: версия и номер сборки.</summary>
        private static UIElement AboutNumbers()
        {
            var grid = new Grid();
            for (int i = 0; i < 2; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            AboutNumber(grid, 0, BuildInfo.Version, "ВЕРСИЯ");
            AboutNumber(grid, 1, BuildInfo.Number, "СБОРКА");

            return new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Padding = new Thickness(18, 16, 18, 16),
                Margin = new Thickness(0, 0, 0, 12),
                Child = grid
            };
        }

        private static void AboutNumber(Grid grid, int column, string value, string caption)
        {
            var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            box.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            var small = new TextBlock
            {
                Text = caption,
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            small.SetResourceReference(TextBlock.ForegroundProperty, "TextTer");
            box.Children.Add(small);

            Grid.SetColumn(box, column);
            grid.Children.Add(box);
        }

        private static UIElement AboutAuthor()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock { Text = "Автор", VerticalAlignment = VerticalAlignment.Center });

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(right, 1);

            var who = new TextBlock
            {
                Text = "Ростислав Стрельников",
                VerticalAlignment = VerticalAlignment.Center
            };
            who.SetResourceReference(TextBlock.ForegroundProperty, "TextSec");
            right.Children.Add(who);

            UIElement link = AboutLink("@r_strlnkv", "https://t.me/r_strlnkv");
            ((FrameworkElement)link).Margin = new Thickness(10, 0, 0, 0);
            right.Children.Add(link);

            grid.Children.Add(right);

            return new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = grid
            };
        }

        private static UIElement AboutLink(string text, string url)
        {
            var b = new Button { Content = text, Cursor = System.Windows.Input.Cursors.Hand };
            b.SetResourceReference(StyleProperty, "Btn");
            b.Padding = new Thickness(10, 2, 10, 3);
            b.Click += delegate { AboutOpen(url); };
            return b;
        }

        private static void AboutOpen(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) { Diag.Log("не удалось открыть ссылку", e); }
        }

        private static UIElement AboutButtons()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var news = new Button { Content = "Что нового", Height = 38, Margin = new Thickness(0, 0, 6, 0) };
            news.SetResourceReference(StyleProperty, "Btn");
            news.Click += delegate { Updater.OpenPage(Updater.ReleasesPage); };
            grid.Children.Add(news);

            var code = new Button { Content = "GitHub", Height = 38, Margin = new Thickness(6, 0, 0, 0) };
            code.SetResourceReference(StyleProperty, "Btn");
            code.Click += delegate { Updater.OpenPage(Updater.ProjectPage); };
            Grid.SetColumn(code, 1);
            grid.Children.Add(code);

            return grid;
        }

        // ------------------------------------------------------------------
        //  Обновление
        // ------------------------------------------------------------------

        private TextBlock _updStatus;
        private Button _updAction;
        private Button _updStable;
        private Button _updDev;
        private TextBlock _updHint;
        private Updater.Release _updFound;
        private bool _updBusy;
        /// <summary>Что показывать, пока идёт проверка или загрузка, — переживает перестройку.</summary>
        private string _updBusyText = "Проверяю…";

        /// <summary>Проверка обновлений и выбор канала — одной карточкой, как у соседей.</summary>
        private UIElement AboutUpdate()
        {
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _updStatus = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0)
            };
            top.Children.Add(_updStatus);

            _updAction = new Button { MinWidth = 108, Height = 32 };
            _updAction.SetResourceReference(StyleProperty, "Btn");
            _updAction.Click += delegate { UpdateAct(); };
            Grid.SetColumn(_updAction, 1);
            top.Children.Add(_updAction);

            var line = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 14) };
            line.SetResourceReference(Border.BackgroundProperty, "Stroke");

            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            bottom.Children.Add(new TextBlock
            {
                Text = "Канал обновлений",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            });

            var switcher = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(switcher, 1);

            // Гаснут вместе с кнопкой проверки: пока идёт проверка, канал менять нельзя
            // (ответ придёт по прежнему), а щелчок по живой кнопке молча ничего не делал.
            // Считается это здесь только на сборке — за все прочие разы отвечает
            // ShowBusy: без него кнопки то не гасли на начатую проверку, то оставались
            // серыми после кончившейся, и переключить канал было нечем до случайной
            // пересборки страницы.
            _updStable = new Button { Content = "Stable", Height = 30, MinWidth = 96, IsEnabled = !_updBusy };
            _updStable.Click += delegate { SetChannel(Settings.ChannelStable); };
            switcher.Children.Add(_updStable);

            _updDev = new Button { Content = "Dev", Height = 30, MinWidth = 76,
                                   Margin = new Thickness(6, 0, 0, 0), IsEnabled = !_updBusy };
            _updDev.Click += delegate { SetChannel(Settings.ChannelDev); };
            switcher.Children.Add(_updDev);

            bottom.Children.Add(switcher);

            _updHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
            _updHint.SetResourceReference(StyleProperty, "Caption");

            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(line);
            stack.Children.Add(bottom);
            stack.Children.Add(_updHint);

            ShowChannel();
            // Перестройка страницы не должна отменять начатое: она случается сама
            // по себе — пришла клавиатура, обновился заряд, сменилась тема. Раньше
            // «Проверяю…» превращалось обратно в «Проверено N минут назад», кнопка
            // оживала, и можно было запустить вторую проверку поверх первой.
            if (_updBusy)
            {
                _updStatus.Text = _updBusyText;
                _updStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSec");
                _updAction.Content = _updFound != null ? "Обновить" : "Проверить";
                _updAction.IsEnabled = false;
            }
            else if (!String.IsNullOrEmpty(_updSaid))
            {
                // Сказанное в прошлый раз — отказ или «установщик запущен» — важнее
                // найденного выпуска и потому спрашивается раньше. Иначе первая же
                // самопроизвольная пересборка меняла «установщик подписан чужим ключом»
                // на приглашение обновиться: предупреждение о подделке жило до первого
                // нажатия незнакомой клавиши.
                _updStatus.Text = _updSaid;
                _updStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSec");
                // Кнопку не гасим: «Установщик запущен» — не вечное состояние. Запрос
                // прав администратора можно отклонить, установщик закрыть, — и человек
                // оставался с мёртвой кнопкой до перезапуска программы, а сбросить
                // признак было нечем. Проверить заново он вправе всегда.
                bool launched = _updSaid == "Установщик запущен";
                _updAction.Content = _updFound != null && !launched ? "Повторить" : "Проверить";
                _updAction.IsEnabled = true;
            }
            else if (_updFound != null)
            {
                // Найденный выпуск переживает перестройку: она случается сама по себе —
                // от первой же впервые нажатой клавиши, — и человек, нажавший «Проверить»
                // и получивший «Есть новый выпуск», терял и надпись, и кнопку.
                _updStatus.Text = "Есть новый выпуск " + _updFound.Tag;
                _updStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSec");
                _updAction.Content = "Обновить";
                _updAction.IsEnabled = true;
            }
            else ShowIdle();

            return new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Padding = new Thickness(18, 16, 18, 16),
                Margin = new Thickness(0, 0, 0, 12),
                Child = stack
            };
        }

        /// <summary>
        /// Занятость карточки обновлений — одним местом на все три кнопки. Прежде
        /// начало работы гасило только кнопку действия, а конец её же и оживлял:
        /// кнопки канала при этом оставались в том состоянии, в каком их застала
        /// последняя сборка страницы, — то живыми во время проверки (щелчок по ним
        /// молча ничего не делал), то серыми после неё навсегда.
        /// </summary>
        private void ShowBusy()
        {
            if (_updAction != null) _updAction.IsEnabled = !_updBusy;
            if (_updStable != null) _updStable.IsEnabled = !_updBusy;
            if (_updDev != null) _updDev.IsEnabled = !_updBusy;
        }

        private void SetChannel(string channel)
        {
            // Пока идёт проверка, канал не меняем: ответ придёт по прежнему и ляжет
            // как ответ по новому — то есть перешедшему на Stable предложат раннюю сборку.
            if (_updBusy) return;

            if (_s.UpdateChannel == channel) return;
            _s.UpdateChannel = channel;
            Save();
            _updFound = null;
            _updSaid = null;
            ShowChannel();
            ShowIdle();
        }

        private void ShowChannel()
        {
            bool dev = _s.UpdateChannel == Settings.ChannelDev;
            _updStable.SetResourceReference(StyleProperty, dev ? "Btn" : "BtnAccent");
            _updDev.SetResourceReference(StyleProperty, dev ? "BtnAccent" : "Btn");
            // Подпись называет разницу, а не намекает на неё.
            _updHint.Text = dev
                ? "Сборки Dev приходят раньше общего выпуска. Взамен в них чаще что-то не работает."
                : "Приходят только готовые выпуски.";
        }

        /// <summary>Обычный вид: когда проверяли и кнопка «Проверить».</summary>
        private void ShowIdle()
        {
            _updBusy = false;
            _updStatus.Text = Updater.Ago(_s.Checked);
            _updStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSec");
            _updAction.Content = "Проверить";
            _updAction.IsEnabled = true;
        }

        private void UpdateAct()
        {
            if (_updBusy) return;
            if (_updFound != null) { UpdateInstall(); return; }
            UpdateCheck();
        }

        private void UpdateCheck()
        {
            _updSaid = null;
            _updBusy = true;
            ShowBusy();
            _updBusyText = "Проверяю…";
            _updStatus.Text = _updBusyText;

            string channel = _s.UpdateChannel;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                Updater.Checked = false;
                Updater.Release found = Updater.Check(channel, out error);
                ToWindow(delegate
                {
                    // Отметку о проверке ставим здесь, на потоке окна: настройки правит
                    // только он, и Save() отсюда ни с чем не столкнётся.
                    if (Updater.Checked) { _s.Checked = DateTime.UtcNow; Save(); }
                    // Признак занятости снимаем ДО выхода: уйдя со страницы во время
                    // проверки, человек возвращался к вечному «Проверяю…» и мёртвой
                    // кнопке — снять флаг было больше нечем.
                    // Всё, что должно пережить уход со страницы, ставим ДО выхода:
                    // ради этого поля и заведены, а стояв ниже, они не писались ровно
                    // в том случае, для которого нужны.
                    _updBusy = false;
                    _updFound = found;
                    if (found == null && error != null) _updSaid = error;

                    if (_updStatus == null) return;
                    ShowBusy();
                    if (found != null)
                    {
                        _updStatus.Text = "Есть новый выпуск " + found.Tag;
                        _updAction.Content = "Обновить";
                    }
                    else if (error != null) _updStatus.Text = error;
                    else
                    {
                        _updStatus.Text = "Установлена последняя версия";
                        _updAction.Content = "Проверить";
                    }
                });
            });
        }

        private void UpdateInstall()
        {
            Updater.Release release = _updFound;
            if (release == null) return;

            _updSaid = null;
            _updBusy = true;
            ShowBusy();
            _updBusyText = "Скачиваю…";
            _updStatus.Text = _updBusyText;

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                string path = Updater.Download(release, out error);
                if (path != null) Updater.Install(path, out error);

                ToWindow(delegate
                {
                    // Раньше выхода: ушёл со страницы во время «Скачиваю…» — и признак
                    // оставался включённым навсегда, а кнопка мёртвой до перезапуска.
                    // И всё остальное тоже раньше: предупреждение «установщик подписан
                    // чужим ключом» приходит именно сюда, а стояв ниже выхода, оно
                    // пропадало бесследно у всякого, кто ушёл со страницы, — и человеку
                    // снова предлагали обновиться на тот же поддельный пакет.
                    _updBusy = false;
                    if (path == null || error != null) _updSaid = error;
                    else
                    {
                        // Выпуск забываем: установщик уже запущен, и вторая загрузка того же
                        // пакета стёрла бы файл, который в этот миг читает msiexec.
                        _updFound = null;
                        _updSaid = "Установщик запущен";
                    }

                    if (_updStatus == null) return;
                    _updStatus.Text = _updSaid;
                    ShowBusy();
                    // Тот же ответ, что и на пересборке: «Установщик запущен» — не вечное
                    // состояние. Запрос прав можно отклонить, установщик закрыть, —
                    // и человек оставался с мёртвой кнопкой «Обновить» до перезапуска.
                    bool launched = _updSaid == "Установщик запущен";
                    _updAction.Content = _updFound != null && !launched ? "Повторить" : "Проверить";
                });
            });
        }

        private static UIElement AboutFooter()
        {
            var t = new TextBlock
            {
                Text = "© 2026 MagicKeys · GPL-3.0",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 11
            };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextTer");
            return t;
        }

        // ------------------------------------------------------------------
        //  Строительные блоки
        // ------------------------------------------------------------------

        private void Save()
        {
            if (_building) return;
            _apply();
        }

        /// <summary>
        /// Пересобрать страницу. Зовут, когда настройки или оформление поменяли снаружи
        /// окна — из значка в трее или сменой темы Windows.
        /// </summary>
        public void Rebuild() { BuildPage(); }

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

        /// <summary>
        /// Список действий — с разделами. Сорок пять строк подряд глазом не берутся,
        /// а раздел у каждого действия давно записан и до сих пор никем не читался.
        /// </summary>
        private ComboBox ActionCombo(string currentId, Action<string> onSet)
        {
            var box = new ComboBox { Style = (Style)Application.Current.Resources["Combo"] };

            var view = new System.Windows.Data.CollectionViewSource();
            var items = new List<KeyAction>(Actions.All);
            view.Source = items;
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Group"));
            box.ItemsSource = view.View;

            var header = new Style(typeof(GroupItem));
            header.Setters.Add(new Setter(GroupItem.TemplateProperty, GroupTemplate()));
            var style = new GroupStyle();
            style.ContainerStyle = header;
            box.GroupStyle.Add(style);

            KeyAction pick = null;
            foreach (KeyAction a in items) if (a.Id == currentId) { pick = a; break; }
            box.SelectedItem = pick != null ? pick : (items.Count > 0 ? items[0] : null);
            box.SelectionChanged += delegate
            {
                if (_building) return;
                KeyAction a = box.SelectedItem as KeyAction;
                if (a != null) onSet(a.Id);
            };
            return box;
        }

        /// <summary>Заголовок раздела в списке: подпись и сами строки под ней.</summary>
        private static ControlTemplate GroupTemplate()
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));

            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
            title.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 8, 10, 2));
            title.SetResourceReference(FrameworkElement.StyleProperty, "Caption");
            panel.AppendChild(title);

            var items = new FrameworkElementFactory(typeof(ItemsPresenter));
            panel.AppendChild(items);

            var tpl = new ControlTemplate(typeof(GroupItem));
            tpl.VisualTree = panel;
            return tpl;
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
        /// Переключает режим верхнего ряда у драйвера. В стороне от потока окна: внутри
        /// запрос прав администратора и ожидание до тридцати секунд, а окно в это время
        /// не должно превращаться в «не отвечает». Итог кладётся в поле, а не в надпись:
        /// страница после этого пересобирается, и отметка «сейчас так» переезжает
        /// на выбранный режим.
        /// </summary>
        private void ApplyFnBehavior(int value)
        {
            // Отчёт кладём в свой слот и перестраиваем страницу — в чужую карточку
            // про скачивание пакета Apple ему не место: там он вдобавок оставался
            // навсегда, затирая полезный текст про 7-Zip.
            _driverText = "Записываю настройку драйвера…";
            _driverBusy = true;
            BuildPage();

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                bool ok = false;
                try { ok = AppleDriver.SetFnBehavior(value, out error); }
                catch (Exception e) { error = e.Message; }

                ToWindow(delegate
                {
                    _driverBusy = false;
                    _driverText = null;
                    if (CurrentPage != "driver") return;   // ушли со страницы — некому показывать
                    _fnNotice = ok
                        ? "Записано. Драйвер перечитает значение при следующем подключении "
                          + "клавиатуры — переподключите её или перезагрузитесь, до этого ряд "
                          + "работает по-старому.\n\nПрочитал ли драйвер написанное, программе "
                          + "не узнать: это его внутренняя настройка. Что получилось на деле, "
                          + "видно строкой «Преобразует ряд на деле» — она считает приходящие "
                          + "медиакоды, а не читает реестр."
                        : "Не вышло: " + error;
                    if (ok) _apply();
                    BuildPage();
                });
            });
        }

        /// <summary>
        /// Расставляет настройки так, чтобы программа и драйвер не делали одно и то же.
        /// Меняет только то, что действительно мешает, и обо всём отчитывается: молчаливая
        /// перенастройка чужих настроек — худшее, что программа может сделать.
        /// </summary>
        private void ApplyDriverProfile()
        {
            var done = new List<string>();

            if (YieldingNow)
            {
                if (_s.FnSubstitute != ModKey.None)
                {
                    _s.FnSubstitute = ModKey.None;
                    done.Add("• Заменитель Fn выключен: с драйвером работает настоящая клавиша Fn "
                           + "и подменять её больше нечем.");
                }
                if (_s.FnNavigation)
                {
                    _s.FnNavigation = false;
                    done.Add("• Навигация Fn+стрелки выключена: её должен взять на себя драйвер. "
                           + "Проверьте Fn+← — если в начало строки не уходит, включите её "
                           + "обратно в карточке «Заменитель Fn» на странице «Клавиши».");
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
                            + "цифровой блок Apple, яркость внешних мониторов и ⌘+Tab.";
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
            get { return Engine.YieldsRow(_s, 0); }
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
