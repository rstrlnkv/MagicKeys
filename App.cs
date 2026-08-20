// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Gdi = System.Drawing;

namespace MagicKeys
{
    internal sealed class App : Application
    {
        private const string MutexName = "MagicKeys.SingleInstance.v1";
        private const string ShowEventName = "MagicKeys.ShowWindow.v1";

        private Settings _settings;
        private Engine _engine;
        private Forms.NotifyIcon _tray;
        private Forms.ToolStripMenuItem _enabledItem;
        private MainWindow _window;
        private EventWaitHandle _showEvent;

        [STAThread]
        public static int Main(string[] args)
        {
            bool created;
            using (var single = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Программа уже работает — попросим её показать окно.
                    try
                    {
                        EventWaitHandle h;
                        if (EventWaitHandle.TryOpenExisting(ShowEventName, out h))
                        {
                            using (h) h.Set();
                        }
                    }
                    catch { }
                    return 0;
                }

                var app = new App();
                return app.Run(args);
            }
        }

        private int Run(string[] args)
        {
            bool startHidden = false, dev = false;
            foreach (string a in args)
                if (String.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)) startHidden = true;
                else if (String.Equals(a, "--log", StringComparison.OrdinalIgnoreCase)) Diag.Enable();
                else if (String.Equals(a, "--dev", StringComparison.OrdinalIgnoreCase)) dev = true;

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Resources.MergedDictionaries.Add(Theme.Initial());

            _settings = Settings.Load();
            if (dev) _settings.DeveloperMode = true;
            _settings.Autostart = Autostart.Enabled;
            if (_settings.StartMinimized) startHidden = true;

            _engine = new Engine();
            _engine.DevicesChanged += delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    UpdateTrayText();
                    if (_window != null) _window.RefreshDevices();
                });
            };
            _engine.Apply(_settings);
            _engine.Start();

            // Перепись клавиш идёт через сырой ввод: он, в отличие от хука, знает устройство.
            KeyWatch.Discovered += delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    int seen = KeyWatch.MaxFunctionKey;
                    if (seen > _settings.ObservedFunctionKeys)
                    {
                        _settings.ObservedFunctionKeys = seen;
                        _settings.Save();
                        Diag.Log("замечена клавиша F" + seen + "; список расширен");
                    }
                    if (_window != null) _window.RefreshDevices();
                });
            };
            KeyWatch.EjectPressed += delegate
            {
                Settings s = _settings;
                if (s == null) return;
                KeyAction a = Actions.Get(s.EjectKey);
                if (a.Kind == ActionKind.PassThrough) return;
                Actions.Begin(a, false, s.BrightnessStep);
                Actions.End(a);
            };
            // Коды яркости с медиастраницы. Когда функциональным рядом занимается драйвер,
            // F1 и F2 до перехвата не доходят: они переводятся сразу в коды 0x70 и 0x6F.
            // Windows применяет такие коды только к встроенной панели ноутбука, поэтому
            // на обычном ПК они пропадают зря. Ловим их здесь и отдаём тому, кто умеет
            // больше, — DDC/CI.
            KeyWatch.Activity += delegate(KeyWatch.KeyEvent e)
            {
                if (!e.Media) return;
                Settings s = _settings;
                if (s == null || !s.Enabled || !s.BrightnessFromMediaKeys) return;
                if (e.Code == Native.UsageBrightnessUp) Brightness.Nudge(s.BrightnessStep);
                else if (e.Code == Native.UsageBrightnessDown) Brightness.Nudge(-s.BrightnessStep);
            };

            KeyWatch.Start();

            // Индикатор показываем на том мониторе, яркость которого изменилась.
            Brightness.ChangedOn += delegate(IntPtr screen, string name, int percent)
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    Diag.Log("индикатор яркости: " + percent + "% на " + (name ?? "?"));
                    if (!_settings.ShowBrightnessOsd) return;
                    try { Osd.Show(percent, screen); }
                    catch (Exception e) { Diag.Log("индикатор яркости: сбой", e); }
                });
            };

            BuildTray();
            WatchForSecondInstance();
            Diag.Log("запуск завершён; перехват " + (_engine.Running ? "установлен" : "НЕ установлен"));

            if (!startHidden) ShowWindow();

            DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                e.Handled = true;
            };

            return base.Run();
        }

        // ------------------------------------------------------------------

        private void BuildTray()
        {
            _tray = new Forms.NotifyIcon();
            ApplyTrayIcon();
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowWindow(); };

            var menu = new Forms.ContextMenuStrip();

            var open = new Forms.ToolStripMenuItem("Настроить…");
            open.Click += delegate { ShowWindow(); };
            open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);

            _enabledItem = new Forms.ToolStripMenuItem("Переназначения включены");
            _enabledItem.CheckOnClick = true;
            _enabledItem.Checked = _settings.Enabled;
            _enabledItem.Click += delegate
            {
                _settings.Enabled = _enabledItem.Checked;
                ApplyAndSave();
            };

            var quit = new Forms.ToolStripMenuItem("Выход");
            quit.Click += delegate { Quit(); };

            menu.Items.Add(open);
            menu.Items.Add(_enabledItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(quit);
            _tray.ContextMenuStrip = menu;

            // Пользователь может переключить тему на ходу — значок должен перекраситься.
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            UpdateTrayText();
        }

        /// <summary>Мониторы переключили, подключили или отключили.</summary>
        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // Кэш мониторов держится 20 секунд, и всё это время после переподключения
            // программа писала бы яркость в недействительный дескриптор, а монитор под
            // указателем не находила вовсе — и говорила «яркость недоступна» на исправном.
            try { Brightness.Invalidate(); } catch { }
        }

        private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General &&
                e.Category != Microsoft.Win32.UserPreferenceCategory.Color &&
                e.Category != Microsoft.Win32.UserPreferenceCategory.VisualStyle) return;

            // Windows шлёт это уведомление щедро и не по одному разу, поэтому Reapply
            // сам решает, изменилось ли что-то на самом деле, и молчит, если нет.
            Dispatcher.BeginInvoke((Action)delegate
            {
                ApplyTrayIcon();
                try
                {
                    if (!Theme.Reapply()) return;
                    Diag.Log("оформление: тема системы изменилась, цвета пересобраны");
                    if (_window != null)
                    {
                        Fluent.ApplyWindowStyling(_window);   // тёмный заголовок и Mica
                        _window.RefreshTheme();
                    }
                }
                catch (Exception ex) { Diag.Log("оформление: не удалось пересобрать", ex); }
            });
        }

        /// <summary>Перерисовывает значок в трее под текущую тему и состояние.</summary>
        private void ApplyTrayIcon()
        {
            if (_tray == null) return;
            try
            {
                Gdi.Icon old = _tray.Icon;
                _tray.Icon = Fluent.MakeTrayIcon(!_settings.Enabled);
                if (old != null) old.Dispose();
            }
            catch (Exception e) { Diag.Log("значок в трее: не удалось обновить", e); }
        }

        private void UpdateTrayText()
        {
            if (_tray == null) return;
            // Сорванный перехват важнее всего остального: без него не работает ничего,
            // а подсказка «работает» отправила бы искать причину не там.
            string state = _engine != null && !String.IsNullOrEmpty(_engine.Failure)
                ? "перехват не установлен"
                : (!_settings.Enabled
                    ? "переназначения выключены"
                    : (_settings.PauseWhenAppleAbsent && !Devices.AppleConnected
                        ? "ожидание Magic Keyboard"
                        : "работает"));
            // NotifyIcon.Text не принимает больше 63 символов.
            string text = "MagicKeys — " + state;
            _tray.Text = text.Length > 62 ? text.Substring(0, 62) : text;
            if (_enabledItem != null) _enabledItem.Checked = _settings.Enabled;
            ApplyTrayIcon();
        }

        private void WatchForSecondInstance()
        {
            try
            {
                bool created;
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out created);
                var t = new Thread(delegate()
                {
                    while (true)
                    {
                        _showEvent.WaitOne();
                        Dispatcher.BeginInvoke((Action)ShowWindow);
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
            catch { }
        }

        private void ShowWindow()
        {
            if (_window == null)
            {
                _window = new MainWindow(_settings, _engine, ApplyAndSave);
                _window.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e)
                {
                    // Программа продолжает работать в значке — закрытие окна её не выключает.
                    e.Cancel = true;
                    _window.Hide();
                };
            }
            _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
            _window.RefreshDevices();
        }

        private void ApplyAndSave()
        {
            _engine.Apply(_settings);
            _settings.Save();
            UpdateTrayText();
        }

        private void Quit()
        {
            try { Osd.Teardown(); } catch { }
            try { if (_engine != null) _engine.Stop(); } catch { }
            try { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; } catch { }
            try { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }
            try
            {
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _tray = null;
                }
            }
            catch { }
            Shutdown();
        }
    }
}
