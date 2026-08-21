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
            bool startHidden = false;
            foreach (string a in args)
                if (String.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)) startHidden = true;
                else if (String.Equals(a, "--log", StringComparison.OrdinalIgnoreCase)) Diag.Enable();

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Resources.MergedDictionaries.Add(Theme.Initial());

            _settings = Settings.Load();
            if (_settings.StartMinimized) startHidden = true;

            // Запись автозапуска несёт в себе путь к файлу, а он мог переехать.
            Autostart.FixIfBroken();

            _engine = new Engine();
            _engine.DevicesChanged += delegate
            {
                ToWindow(delegate
                {
                    AdoptModelDefaults();   // сам применит и сохранит, если было что менять
                    UpdateTrayText();
                    if (_window != null) _window.RefreshDevices();
                });
            };
            _engine.Apply(_settings);
            _engine.Start();

            // Перепись клавиш идёт через сырой ввод: он, в отличие от хука, знает устройство.
            KeyWatch.Discovered += delegate
            {
                ToWindow(delegate
                {
                    if (_window != null) _window.RefreshDevices();
                });
            };
            KeyWatch.EjectPressed += delegate
            {
                // Тоже с потока переписи клавиш — тоже снимок.
                Settings s = _engine.Current;
                // Общий выключатель спрашиваем и здесь: «Переназначения: Выключены»
                // значит всё. Перехват это делает, соседний обработчик яркости — тоже,
                // а ⏏ идёт мимо перехвата и правило обходила: значок в трее приглушён,
                // а клавиша работает. И паузу тем же порядком, что у яркости.
                if (s == null || !s.Enabled) return;
                if (s.PauseWhenAppleAbsent && !Devices.AppleConnected) return;
                KeyAction a = Actions.Get(s.EjectKey);
                if (a.Kind == ActionKind.PassThrough) return;
                Actions.Begin(a, false, Settings.BrightnessStep);
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
                // Снимок: сюда приходят с потока переписи клавиш, а живой объект правит окно.
                Settings s = _engine.Current;
                // Отдельной настройки нет: чужой драйвер шлёт эти коды или не шлёт,
                // и подхватывать их безвредно в любом случае — Windows на обычном ПК всё
                // равно применяет их только к встроенной панели, которой тут нет. А вот
                // общий выключатель спрашиваем: «Переназначения: Выключены» значит всё.
                // И паузу тоже: выбрав «Только с Magic Keyboard», человек попросил
                // программу не вмешиваться, пока её нет. Перепись клавиш и так отбирает
                // события по устройству, так что сюда чужие коды не доходят, — но правило
                // должно читаться одинаково во всех трёх местах, где его спрашивают.
                if (s == null || !s.Enabled) return;
                if (s.PauseWhenAppleAbsent && !Devices.AppleConnected) return;
                if (e.Code == Native.UsageBrightnessUp) Brightness.Nudge(Settings.BrightnessStep);
                else if (e.Code == Native.UsageBrightnessDown) Brightness.Nudge(-Settings.BrightnessStep);
            };

            // Один раз при запуске: событие DevicesChanged поднимается только когда
            // набор клавиатур изменился, а самое первое появление съедает Devices.Rescan
            // внутри Engine.Start. С уже подключённой клавиатурой заводские назначения
            // иначе не подставлялись бы никогда — а именно так программу и запускают.
            AdoptModelDefaults();   // сам применит и сохранит, если было что менять

            // Заряд читается в пуле потоков; когда ответ придёт, обновляем страницу.
            KeyboardBattery.Updated += delegate
            {
                ToWindow(delegate { if (_window != null) _window.RefreshDevices(); });
            };

            KeyWatch.Start();

            // Индикатор показываем на том мониторе, яркость которого изменилась.
            Brightness.ChangedOn += delegate(IntPtr screen, string name, int percent)
            {
                ToWindow(delegate
                {
                    Diag.Log("индикатор яркости: " + percent + "% на " + (name ?? "?"));
                    // Плашка показывается всегда: системной для внешних мониторов
                    // не существует, и выключить свою — менять яркость вслепую.
                    try { Osd.Show(percent, screen); }
                    catch (Exception e) { Diag.Log("индикатор яркости: сбой", e); }
                });
            };

            // Сетку под ошибки натягиваем ДО того, как что-то может упасть. Прежде
            // она ставилась после первого показа окна — то есть самая первая сборка
            // страницы, когда ни устройств, ни модели, ни заряда ещё нет, проходила
            // без неё, и сбой там ронял программу до появления значка в трее.
            //
            // Windows выключается, человек выходит из системы или установщик просит уйти.
            // Единственный путь, на котором нас закрывают снаружи: окно на обычное
            // закрытие отвечает отказом и прячется в трей. Уходим сами и убираем значок —
            // иначе он остаётся висеть мёртвым, пока по нему не проведут мышью.
            SessionEnding += delegate(object s, SessionEndingCancelEventArgs e)
            {
                Diag.Log("сеанс завершается — выходим");
                Quit();
            };

            // Гасим, но не молча: журнал заведён ровно ради вопроса «почему не сработало».
            DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                Diag.Log("сбой на потоке окна", e.Exception);
                e.Handled = true;
            };

            BuildTray();
            WatchForSecondInstance();
            Diag.Log("запуск завершён; перехват " + (_engine.Running ? "установлен" : "НЕ установлен"));

            if (!startHidden) ShowWindow();

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

            _enabledItem = new Forms.ToolStripMenuItem("Переназначения работают");
            _enabledItem.CheckOnClick = true;
            _enabledItem.Checked = _settings.Enabled;
            _enabledItem.Click += delegate
            {
                _settings.Enabled = _enabledItem.Checked;
                // Окно, если оно открыто, показывает то же самое списком — пересобираем,
                // иначе там останется прежний ответ, и первый же щелчок по списку
                // «выберет» показанное, а настройка к этому времени уже другая.
                // И спрятанное тоже: закрытие прячет окно в трей, то есть спрятанное —
                // обычное его состояние. Без этого страница показывала прежний ответ
                // до следующего запуска, а первый щелчок по списку «выбирал» показанное.
                // Сперва применяем: ApplyAndSave зовёт Normalize, и тот вправе поправить
                // настройки. Показывать надо то, что вышло, а не то, что просили.
                ApplyAndSave();
                if (_window != null) _window.Rebuild();
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
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;

            UpdateTrayText();
        }

        /// <summary>
        /// Машина проснулась. За сон устаревает всё разом, а сроки у кэшей разные —
        /// от двадцати секунд до бессрочного. Гасим те три, которым это правда важно;
        /// остальные обновятся сами: набор устройств опрашивается раз в две секунды,
        /// а описания отчётов снимаются по уходу устройства.
        /// </summary>
        private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
            try { if (_engine != null) _engine.ReleaseHeld(); } catch { }
            try { Brightness.Invalidate(); } catch { }
            try { KeyboardBattery.Invalidate(); } catch { }
            try { AppleDriver.Refresh(true); } catch { }
        }

        /// <summary>
        /// Экран заперли, отперли или ушли на защищённый рабочий стол.
        ///
        /// Пока там Ctrl+Alt+Del или запрос прав администратора, нажатия до перехвата
        /// не доходят — а значит, не доходят и отпускания. Человек отпустил Caps Lock
        /// уже на том экране, а мы всё ещё держим за него синтетический control:
        /// вернувшись, он обнаруживает, что клавиатура «сломалась».
        /// </summary>
        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            try { if (_engine != null) _engine.ReleaseHeld(); } catch { }
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
            ToWindow(delegate
            {
                ApplyTrayIcon();
                try
                {
                    if (!Theme.Reapply()) return;
                    Diag.Log("оформление: тема системы изменилась, цвета пересобраны");
                    if (_window != null)
                    {
                        Fluent.ApplyWindowStyling(_window);   // тёмный заголовок и Mica
                        _window.Rebuild();
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

        /// <summary>
        /// Подставить заводские назначения опознанной модели — но только пока человек
        /// их не менял. У клавиатур 2021 и 2024 годов на F5 и F6 напечатаны диктовка
        /// и «не беспокоить»; подписи в окне их показывали, а действие стояло «ничего»,
        /// потому что набор по умолчанию был общий, от модели 2015 года.
        /// </summary>
        private void AdoptModelDefaults()
        {
            try
            {
                AppleModel m = Devices.AppleModel;
                if (m == null || m.Gen == AppleGen.Unknown) return;
                if (_settings.FKeysGen == m.Gen) return;

                // Меняем, только если набор ровно такой, каким мы его и оставили.
                string[] mine = Models.DefaultFKeys(_settings.FKeysGen);
                string[] now = _settings.FKeys;
                if (now == null || now.Length != mine.Length) return;
                for (int i = 0; i < mine.Length; i++)
                    if (now[i] != mine[i]) return;

                _settings.FKeys = Models.DefaultFKeys(m.Gen);
                _settings.FKeysGen = m.Gen;
                // Через общий путь, а не Save() напрямую: заводские назначения надо
                // не только записать, но и отдать перехвату, иначе до перезапуска
                // верхний ряд остался бы прежним.
                ApplyAndSave();
                Diag.Log("подставлены заводские назначения: " + Models.GenName(m.Gen));
            }
            catch (Exception e) { Diag.Log("не удалось подставить заводские назначения", e); }
        }

        private void UpdateTrayText()
        {
            if (_tray == null) return;
            // Сорванный перехват важнее всего остального: без него не работает ничего,
            // а подсказка «работает» отправила бы искать причину не там.
            string state = _engine != null && !String.IsNullOrEmpty(_engine.Failure)
                ? "клавиши не переназначаются"
                : (!_settings.Enabled
                    ? "переназначения выключены"
                    : (_settings.PauseWhenAppleAbsent && !Devices.AppleConnected
                        ? "ожидание Magic Keyboard"
                        : "работает"));
            // Заряд в подсказке: этого Windows не умеет вовсе — свойство Bluetooth пустое,
            // а программа спрашивает заряд у самой клавиатуры отчётом. Прятать такое
            // на служебной странице жалко.
            int battery = KeyboardBattery.Percent;
            if (battery >= 0) state += " · заряд " + battery + " %";

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
                        ToWindow(ShowWindow);
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
            // Согласие настроек сами с собой проверяем здесь, а не только при чтении
            // файла: правил уже полдюжины, и повторять каждое в окне у каждого списка —
            // значит однажды забыть одно. Место одно, и оно на пути любой правки.
            _settings.Normalize();
            _engine.Apply(_settings);
            _settings.Save();
            UpdateTrayText();
        }

        /// <summary>Уходим. С этой секунды фоновым потокам к окну обращаться не к чему.</summary>
        private volatile bool _leaving;

        private void Quit()
        {
            _leaving = true;
            try { Osd.Teardown(); } catch { }
            try { if (_engine != null) _engine.Stop(); } catch { }
            try { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; } catch { }
            try { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }
            try { Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged; } catch { }
            try { Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
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

        /// <summary>
        /// Отдать работу потоку окна — но только пока оно есть.
        ///
        /// Перепись клавиш, яркость и сторож второго запуска живут своими потоками
        /// и не знают, что программа уходит. После Shutdown диспетчер завершён,
        /// и BeginInvoke бросает исключение прямо на их потоке, где его никто не ловит:
        /// пришедшее в эту секунду событие устройства превращало спокойный выход
        /// в падение.
        /// </summary>
        private void ToWindow(Action what)
        {
            if (_leaving) return;
            try { Dispatcher.BeginInvoke(what); }
            catch (Exception e) { Diag.Log("не удалось передать работу окну", e); }
        }
    }
}
