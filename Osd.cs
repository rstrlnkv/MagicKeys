// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MagicKeys
{
    /// <summary>
    /// Экранный индикатор яркости.
    ///
    /// Системного взять неоткуда: Windows показывает свою плашку только для той яркости,
    /// которой управляет сама, а это встроенная панель ноутбука. Проверено — на машине
    /// с одними внешними мониторами `WmiMonitorBrightness` отвечает «не поддерживается»,
    /// то есть у Windows нет самого значения, которое можно было бы показать. Поэтому
    /// индикатор свой, но сделан по образцу системного: та же плашка внизу по центру,
    /// значок слева, полоса, число справа.
    ///
    /// Показывается на том мониторе, яркость которого изменилась, — а меняется тот,
    /// на котором указатель. С тремя экранами это единственное понятное правило.
    /// </summary>
    internal sealed class Osd : Window
    {
        private static Osd _instance;

        private readonly Border _fill;
        private Border _panel;
        private bool _backdropLogged;
        private double _targetTop;

        // Замерено покадрово по записи, где системная плашка и наша идут подряд.
        // Системная выезжает снизу на 54 точки экрана (36 логических) за 264 мс
        // с замедлением к концу и уезжает обратно за 165 мс с разгоном.
        private const double SlideUp = 36;
        private const int AppearMs = 280;
        private const int VanishMs = 165;

        // Плотность акрила. Подобрана по контрасту с фоном на записи: у системной
        // плашки он 332, и меньшая плотность даёт меньший контраст.
        private const byte AcrylicAlpha = 0x8C;
        private readonly Border _track;
        private Grid _bar;
        private TextBlock _unsupported;
        private readonly TextBlock _value;
        private readonly TextBlock _glyph;
        private readonly DispatcherTimer _timer;

        /// <summary>
        /// Насколько плашка опущена ниже своего места. Анимируется именно это свойство,
        /// а не Top: у окна Top и Left анимации не поддаются — значение применяется
        /// только при прямом присваивании, и анимация молча не даёт ничего. Проверено
        /// опытом: с ходом в 200 точек окно так и осталось за краем экрана.
        /// </summary>
        private static readonly DependencyProperty SlideProperty =
            DependencyProperty.Register("Slide", typeof(double), typeof(Osd),
                new PropertyMetadata(0.0, OnSlideChanged));

        private static void OnSlideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var osd = (Osd)d;
            osd.Top = osd._targetTop + (double)e.NewValue;
        }

        /// <param name="screen">HMONITOR, на котором показывать; ноль — там, где указатель.</param>
        public static void Show(int percent, IntPtr screen)
        {
            if (_instance == null) _instance = new Osd();
            _instance.Display(percent, screen);
        }

        public static void Teardown()
        {
            if (_instance == null) return;
            Osd o = _instance;
            _instance = null;
            try { o.Hide(); ((Window)o).Close(); } catch { }
        }

        private Osd()
        {
            WindowStyle = WindowStyle.None;
            // Прозрачности средствами WPF здесь быть не должно: размытие даёт DWM,
            // а слоёное окно его перекрыло бы своей заливкой. Углы скругляет тоже
            // система — тем же способом, что у собственных плашек Windows.
            AllowsTransparency = false;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Manual;
            Width = 192;      // замерено с системной плашки: 288 точек экрана при 150 %
            Height = 47.3;    // 71 точка экрана

            // Ни заливки, ни обводки: у системных плашек их нет — сквозь них видно
            // размытый фон, а край очерчен только скруглением окна.
            var panel = new Border
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 0, 8, 0),
                Opacity = 0
            };
            _panel = panel;

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Значок солнца из системного шрифта — тот же, что Windows рисует у яркости.
            _glyph = new TextBlock
            {
                Text = "",
                FontSize = 15,   // замерено: при 19 значок выходил 28x29 против системных 20x24
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            _glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            _glyph.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            Grid.SetColumn(_glyph, 0);

            _track = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _track.SetResourceReference(Border.BackgroundProperty, "StrokeStrong");
            _fill = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0
            };
            _fill.SetResourceReference(Border.BackgroundProperty, "Accent");
            _bar = new Grid();
            _bar.Children.Add(_track);
            _bar.Children.Add(_fill);
            Grid.SetColumn(_bar, 1);

            // Показывается вместо шкалы, когда монитор под указателем яркостью
            // не управляется, — например, телевизор без DDC/CI.
            _unsupported = new TextBlock
            {
                Text = "яркость недоступна",
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            _unsupported.SetResourceReference(StyleProperty, "Caption");
            Grid.SetColumn(_unsupported, 1);

            _value = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 14,   // у системной плашки число 15x31, у нас при 15 было 16x32
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(6, 0, 0, 0)
            };
            _value.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            Grid.SetColumn(_value, 2);

            row.Children.Add(_glyph);
            row.Children.Add(_bar);
            row.Children.Add(_unsupported);
            row.Children.Add(_value);
            panel.Child = row;
            Content = panel;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(1600);
            _timer.Tick += delegate { _timer.Stop(); FadeTo(0); };

            SourceInitialized += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Native.AddExStyle(hwnd, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT);
                ApplyBackdrop();
            };

            // Тема сменилась — пересобираем и подложку: её цвет задаётся не кистью,
            // а числом в вызове DWM, и сам он не обновится.
            Theme.Changed += delegate
            {
                if (!IsLoaded) return;
                try { ApplyBackdrop(); } catch { }
            };
        }

        private void Display(int percent, IntPtr screen)
        {
            // Отрицательное значение — этот монитор яркостью не управляется.
            bool unsupported = percent < 0;
            if (percent > 100) percent = 100;

            _unsupported.Visibility = unsupported ? Visibility.Visible : Visibility.Collapsed;
            _bar.Visibility = unsupported ? Visibility.Collapsed : Visibility.Visible;
            _value.Visibility = unsupported ? Visibility.Collapsed : Visibility.Visible;
            _glyph.Opacity = unsupported ? 0.5 : 1.0;
            if (!unsupported) _value.Text = percent.ToString();

            bool wasHidden = !IsVisible;
            Place(screen);

            // Появляется снизу и подъезжает на место; если уже видна — просто
            // остаётся где стояла, чтобы повторные нажатия не дёргали её.
            BeginAnimation(SlideProperty, null);
            SetValue(SlideProperty, wasHidden ? SlideUp : 0.0);
            Top = _targetTop + (wasHidden ? SlideUp : 0.0);

            if (wasHidden) base.Show();

            // Подложку назначаем при каждом показе: после Hide() DWM её теряет,
            // и плашка возвращается непрозрачной.
            ApplyBackdrop();

            // Ширина шкалы известна только после раскладки.
            if (!unsupported)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)delegate
                {
                    double full = _track.ActualWidth;
                    if (full <= 0) return;
                    _fill.BeginAnimation(WidthProperty,
                        new DoubleAnimation(full * percent / 100.0, TimeSpan.FromMilliseconds(110)));
                });

            FadeTo(1);
            _timer.Stop();
            _timer.Start();
        }

        /// <summary>
        /// Ставит плашку внизу по центру нужного монитора — там же, где её показывает
        /// Windows. Координаты монитора приходят в точках экрана, а WPF работает в своих
        /// единицах, поэтому переводим через матрицу окна.
        /// </summary>
        private void Place(IntPtr screen)
        {
            try
            {
                if (screen == IntPtr.Zero)
                {
                    Native.POINT pt;
                    if (Native.GetCursorPos(out pt))
                        screen = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
                }

                var info = new Native.MONITORINFO();
                info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFO));
                if (screen != IntPtr.Zero && Native.GetMonitorInfoW(screen, ref info))
                {
                    // Масштаб берём тот же, каким пользуется WPF, — иначе координаты
                    // разойдутся с тем, как он их потом истолкует. При первом показе
                    // Place вызывается до base.Show(), окна ещё нет и PresentationSource
                    // отдаёт null; тогда спрашиваем системный масштаб, потому что
                    // программа осведомлена о нём, а не о масштабе каждого монитора.
                    // Раньше в этом случае молча брался масштаб 1, и на 150 % плашка при
                    // первом нажатии уезжала за край экрана.
                    double sx = 1, sy = 1;
                    PresentationSource src = PresentationSource.FromVisual(this);
                    if (src != null && src.CompositionTarget != null)
                    {
                        sx = src.CompositionTarget.TransformFromDevice.M11;
                        sy = src.CompositionTarget.TransformFromDevice.M22;
                    }
                    else
                    {
                        uint dpi = Native.SystemDpi();
                        if (dpi > 0) { sx = 96.0 / dpi; sy = sx; }
                    }

                    double left = info.rcWork.Left * sx;
                    double top = info.rcWork.Top * sy;
                    double width = (info.rcWork.Right - info.rcWork.Left) * sx;
                    double bottom = info.rcWork.Bottom * sy;

                    Left = left + (width - Width) / 2;
                    _targetTop = bottom - Height - 15;
                    return;
                }
            }
            catch (Exception e) { Diag.Log("индикатор яркости: не удалось выбрать монитор", e); }

            Rect area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            _targetTop = area.Bottom - Height - 15;
        }

        /// <summary>
        /// Размытая подложка и скругление углов — теми же средствами, что у Windows.
        /// Оттенок берётся из темы: тёмный при тёмной, светлый при светлой.
        /// </summary>
        private void ApplyBackdrop()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            try
            {
                // У поверхности отрисовки WPF собственный фон, и он непрозрачный
                // независимо от Background окна. Пока его не обнулить, окно остаётся
                // чёрным, сколько ни включай размытие под ним.
                HwndSource src = HwndSource.FromHwnd(hwnd);
                if (src != null && src.CompositionTarget != null)
                    src.CompositionTarget.BackgroundColor = Colors.Transparent;

                // Отрицательные поля означают «рамка DWM на всю клиентскую область».
                var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Native.DwmExtendFrameIntoClientArea(hwnd, ref m);

                int round = Native.DWMWCP_ROUND;
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

                // У системных плашек рамки нет вовсе.
                uint none = Native.DWMWA_COLOR_NONE;
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, ref none, sizeof(uint));
            }
            catch (Exception e) { Diag.Log("индикатор яркости: не удалось настроить подложку", e); }

            // Тип подложки «всплывающее окно» (DWMSBT_TRANSIENTWINDOW) здесь не годится:
            // DWM рисует его прозрачным только для активного окна, а наше намеренно
            // никогда не активируется — оно с WS_EX_NOACTIVATE, чтобы не воровать
            // ввод. Для неактивного он выходит плоским, что и было видно на записи:
            // контраст с фоном 480 против 332 у системной плашки.
            //
            // Поэтому прозрачностью управляем сами: прямой акрил не зависит от
            // активности окна, и её степень задаётся здесь же.
            byte tone = Theme.IsDark ? (byte)0x2B : (byte)0xF9;
            Native.EnableAcrylic(hwnd, AcrylicAlpha, tone, tone, tone);

            if (!_backdropLogged)
            {
                _backdropLogged = true;
                Diag.Log("индикатор яркости: подложка — акрил, плотность " + AcrylicAlpha);
            }
        }

        /// <summary>
        /// Появление и исчезновение. Прозрачность самого окна недоступна —
        /// AllowsTransparency выключен ради размытия, — поэтому гаснет содержимое,
        /// а окно вдобавок чуть съезжает вниз-вверх: движение и делает появление
        /// заметным, раз подложка гаснуть не умеет.
        /// </summary>
        private void FadeTo(double target)
        {
            bool showing = target > 0;

            // На уходе затухание идёт ровно столько же, сколько движение: если гасить
            // быстрее, окно прячется раньше, чем плашка доедет, и уход обрывается —
            // на записи он выходил 100 мс вместо 165.
            var fade = new DoubleAnimation(target, TimeSpan.FromMilliseconds(showing ? 200 : VanishMs));
            if (!showing)
                fade.Completed += delegate { if (_panel.Opacity <= 0.01) Hide(); };
            _panel.BeginAnimation(OpacityProperty, fade);

            var slide = new DoubleAnimation(
                showing ? 0.0 : SlideUp,
                TimeSpan.FromMilliseconds(showing ? AppearMs : VanishMs));

            // Появление тормозит к концу, исчезновение разгоняется. Степень кривой
            // выведена из записи: по двум точкам системного хода она получается
            // около 2,3 — то есть ближе к квадратичной, чем к кубической. С кубической
            // движение почти целиком проглатывается в начале и читается как рывок.
            slide.EasingFunction = showing
                ? (IEasingFunction)new QuadraticEase { EasingMode = EasingMode.EaseOut }
                : new QuadraticEase { EasingMode = EasingMode.EaseIn };

            BeginAnimation(SlideProperty, slide);
        }
    }
}
