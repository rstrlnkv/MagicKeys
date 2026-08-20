// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Threading;

namespace MagicKeys
{
    /// <summary>
    /// Перехват клавиатуры и всё переназначение.
    ///
    /// Хук живёт в отдельном потоке с собственным циклом сообщений: Windows снимает
    /// низкоуровневый хук, если обработчик думает дольше LowLevelHooksTimeout (по умолчанию
    /// 300 мс), поэтому здесь нельзя делать ничего медленного — тяжёлое (яркость, запуск
    /// программ) уходит в фоновые потоки.
    ///
    /// Ограничение, которое стоит знать: низкоуровневый хук не сообщает, с какой клавиатуры
    /// пришло нажатие (WM_INPUT с этими сведениями приходит уже после хука). Поэтому
    /// переназначения действуют на весь ввод; чтобы это не мешало, есть выключатель
    /// «приостанавливать, когда Magic Keyboard не подключена».
    /// </summary>
    internal sealed class Engine
    {
        // Скан-коды, которых нет на клавиатуре ANSI.
        // Японские клавиши перехват разбирает сам — им нужны скан-коды. А вот исполнение
        // клавиатуры угадывает перепись клавиш: здесь неизвестно, с какого устройства
        // пришло нажатие, и чужая клавиатура сбивала бы догадку. См. KeyWatch.
        private static readonly uint[] JisScans = { 0x70, 0x79, 0x7B, 0x73, 0x7D };

        private Thread _thread;
        private uint _threadId;
        private IntPtr _hook;
        private Native.HookProc _proc;
        private Timer _watch;

        private volatile Settings _cfg = new Settings();

        // Состояние — только для потока хука.
        private readonly bool[] _fkeyDown = new bool[Settings.MaxFKeys];
        // Какой веткой пошло нажатие — медиа или настоящая F-клавиша — и что было
        // на клавишу назначено. Запоминается, потому что к отпусканию и заменитель
        // Fn, и настройки успевают стать другими.
        private readonly bool[] _fkeyMedia = new bool[Settings.MaxFKeys];
        private readonly bool[] _fkeyYield = new bool[Settings.MaxFKeys];
        private readonly string[] _fkeyAction = new string[Settings.MaxFKeys];

        // Какие физические клавиши прямо сейчас выдают в Windows control и Win. Не флаг,
        // а множество: на control могут быть назначены сразу две клавиши, и отпускание
        // одной не значит, что control отпущен. Нужно раскладке — подменять символ, пока
        // зажат control, нельзя, иначе Ctrl+C перестанет копировать. По физической
        // клавише этого не видно: у пришедших с мака control обычно висит на Caps Lock.
        private readonly HashSet<ModKey> _ctrlSources = new HashSet<ModKey>();
        private readonly HashSet<ModKey> _winSources = new HashSet<ModKey>();
        private bool _capsHeld;

        // На каком нажатии аккорд macOS уже сработал. Нужно против автоповтора:
        // он приходит теми же нажатиями, а закрывать окно за окном при удержании ⌘Q
        // никто не просил.
        private int _macFiredVk;

        // Что мы держим за человека помимо модификаторов: действие на одиночной клавише
        // и подставленный скан-код перестановки ISO. Без учёта их некому отпустить.
        private readonly Dictionary<int, string> _singleAction = new Dictionary<int, string>();
        private readonly HashSet<uint> _isoSwapped = new HashSet<uint>();
        private readonly Dictionary<ModKey, int> _injected = new Dictionary<ModKey, int>();
        private readonly HashSet<uint> _swallowed = new HashSet<uint>();
        private readonly HashSet<int> _navActive = new HashSet<int>();
        private bool _fnHeld;
        private int _fnEffectiveVk;
        private int _subReleased;
        private bool _cmdHeld;
        private bool _cmdTabAlt;
        // Стороны различаются нарочно. Один флаг на обе давал залипание: сочетание
        // macOS отпускало оба ⇧, а возвращало всегда левый — и после ⇧⌘Z, набранного
        // правым ⇧, в Windows навсегда оставался нажатым тот, которого не нажимали.
        private bool _shiftLeft, _shiftRight, _ctrlLeft, _ctrlRight;
        private bool _winDown, _altLeft, _altRight;
        private bool ShiftDown { get { return _shiftLeft || _shiftRight; } }
        private bool CtrlDown { get { return _ctrlLeft || _ctrlRight; } }
        private bool _phantomCtrl;
        private bool _capsOn;
        private string _deadPrefix;
        private IntPtr _hkl;
        private int _hklStamp;



        /// <summary>Что-то изменилось в наборе подключённых клавиатур.</summary>
        public event Action DevicesChanged;

        public bool Running { get { return _hook != IntPtr.Zero; } }

        /// <summary>Пусто, если всё в порядке; иначе — почему перехват не работает.</summary>
        public volatile string Failure;

        /// <summary>
        /// Настройки, по которым программа работает прямо сейчас. Это снимок: его можно
        /// читать с любого потока и он не меняется под руками. Живой объект, который
        /// правит окно, сюда не попадает — см. Settings.Snapshot.
        /// </summary>
        public Settings Current { get { return _cfg; } }

        public void Apply(Settings s)
        {
            // Снимок, а не сам объект: окно продолжит править свой экземпляр по полю
            // за раз, а поток перехвата должен видеть настройки целыми — либо прежние,
            // либо новые, но не половину одних и половину других.
            _cfg = s == null ? new Settings() : s.Snapshot();

            // Через сообщение: Apply зовут с потока окна, а состояние принадлежит потоку хука.
            PostRelease();
            EnsureNumLock(_cfg);
        }

        /// <summary>
        /// Если клавишу ⌧ увели с Num Lock, включить его самим: иначе цифровой блок
        /// может навсегда остаться навигационным — переключить его больше нечем.
        /// </summary>
        private static void EnsureNumLock(Settings s)
        {
            try
            {
                if (s == null || !s.Enabled) return;
                string id = s.NumpadClear;
                if (String.IsNullOrEmpty(id) || id == "none" || id == "key.numlock") return;
                if ((Native.GetKeyState(Vk.NumLock) & 1) != 0) return;
                Input.Tap(Vk.NumLock);
                Diag.Log("цифровой блок был выключен — Num Lock включён");
            }
            catch { }
        }

        public void Start()
        {
            if (_thread != null) return;

            _capsOn = (Native.GetKeyState(Vk.Capital) & 1) != 0;
            EnsureNumLock(_cfg);

            Devices.Rescan();
            _watch = new Timer(delegate
            {
                try
                {
                    // Сведения о драйвере обновляем здесь, а не в хуке: внутри Refresh
                    // свой тридцатисекундный срок, так что работы почти нет, — но реестр
                    // читается на этом потоке, а не на пути нажатия клавиши.
                    AppleDriver.Refresh(false);
                    if (_threadId != 0)
                        Native.PostThreadMessageW(_threadId, WmCheck, IntPtr.Zero, IntPtr.Zero);

                    if (Devices.Rescan())
                    {
                        // Набор клавиатур изменился — пусть исполнение угадывается заново.
                        KeyWatch.ForgetPhysical();
                        PostRelease();
                        Action h = DevicesChanged;
                        if (h != null) h();
                    }
                }
                catch { }
                finally
                {
                    // Перепланируем себя, а не тикаем по расписанию: внутри открытие
                    // каждого HID-устройства и перебор реестра, и по Bluetooth это
                    // может занять больше двух секунд. Наложившиеся проходы считали
                    // «изменилось» друг против друга.
                    try { if (_watch != null) _watch.Change(2000, Timeout.Infinite); }
                    catch { }
                }
            }, null, 2000, Timeout.Infinite);

            // Прогреваем раскладки заранее. Иначе первое же нажатие грузило бы 33 файла
            // XML прямо в хуке — сотни миллисекунд там, где их отпущено 300.
            ThreadPool.QueueUserWorkItem(delegate { try { Layouts.Warm(); } catch { } });

            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "MagicKeys hook";
            _thread.Start();
        }

        public void Stop()
        {
            if (_watch != null) { _watch.Dispose(); _watch = null; }

            Thread t = _thread;
            _thread = null;
            if (_threadId != 0) Native.PostThreadMessageW(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            // Дожидаемся: поток хука отпускает в своём finally всё, что мы держали за
            // человека, а он фоновый — процесс волен закончиться раньше, чем тот
            // проснётся. Синтетический control от переназначенной Caps Lock, оставшийся
            // после выхода, снять уже нечем. Секунды хватает с большим запасом.
            if (t != null) { try { t.Join(1000); } catch { } }
            _threadId = 0;
        }

        // WM_APP + 1 и + 2: свои сообщения потоку хука, чужим кодом не заняты.
        private const uint WmRelease = 0x8000 + 1;
        private const uint WmCheck = 0x8000 + 2;

        private volatile uint _lastHookTick;
        private uint _lastCheckTick;

        /// <summary>
        /// Windows снимает низкоуровневый перехват молча, стоит обработчику один раз
        /// задуматься дольше LowLevelHooksTimeout. Ни события, ни ошибки, ни изменения
        /// дескриптора при этом нет: программа продолжает считать, что всё работает,
        /// а не работает ничего, и человек ищет неисправность не там.
        ///
/// Спросить у Windows нельзя, поэтому судим косвенно: сырой ввод видел нажатие
        /// на клавиатуре, а до перехвата оно не дошло. Хук приходит раньше сырого ввода
        /// всегда — это измерено, — так что расхождение может значить только одно.
        ///
        /// Сначала здесь стоял GetLastInputInfo, но он считает и мышь: замер показал,
        /// что исправный перехват переставлялся каждые тридцать секунд, пока в системе
        /// шевелилась мышь. Активная проверка — подать себе клавишу и посмотреть,
        /// дошла ли, — была бы точнее всего, но она сбрасывает счётчик простоя
        /// и мешает машине уснуть.
        /// </summary>
        private void CheckHookAlive()
        {
            try
            {
                uint raw = KeyWatch.LastKeyTick;
                if (raw == 0) return;          // перепись не запускалась — судить не по чему

                // Сравнение знаковое: обе отметки берутся из GetTickCount в разном
                // порядке, и беззнаковая разность на близких значениях уходила
                // в переполнение.
                uint now = Native.GetTickCount();
                if (unchecked((int)(raw - _lastHookTick)) < 5000) return;
                if (unchecked((int)(now - _lastCheckTick)) < 30000) return;
                _lastCheckTick = now;

                if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
                _hook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _proc,
                                                 Native.GetModuleHandleW(null), 0);
                if (_hook == IntPtr.Zero)
                {
                    Failure = "Windows сняла перехват клавиатуры и не дала поставить его заново. " +
                              "Переназначения сейчас не работают — перезапустите программу.";
                    Diag.Log("перехват не удалось поставить заново, ошибка " +
                             System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                }
                else
                {
                    Failure = null;
                    _lastHookTick = Native.GetTickCount();

                    // Пока перехвата не было, отпускания шли мимо нас, и всё зажатое
                    // осталось зажатым. Воскреснуть с залипшим модификатором — та самая
                    // неисправность, из-за которой клавиатура переставала слушаться.
                    Diag.Log("перехват поставлен заново");
                    ReleaseEverything();
                }
            }
            catch (Exception e) { Diag.Log("проверка перехвата: сбой", e); }
        }

        /// <summary>Попросить поток хука отпустить всё зажатое — с любого потока.</summary>
        private void PostRelease()
        {
            uint id = _threadId;
            if (id != 0) Native.PostThreadMessageW(id, WmRelease, IntPtr.Zero, IntPtr.Zero);
            else ReleaseEverything();
        }

        private void Run()
        {
            try
            {
                _threadId = Native.GetCurrentThreadId();
                // Начальные значения — иначе первая же проверка через две секунды
                // сняла бы и поставила заново совершенно исправный перехват.
                _lastHookTick = Native.GetTickCount();
                _lastCheckTick = _lastHookTick;
                _proc = HookProc;
                _hook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandleW(null), 0);
                if (_hook == IntPtr.Zero)
                {
                    // Причину не называем: низкоуровневые перехваты выстраиваются
                    // в цепочку, «занять» их нельзя, а прочие догадки не проверялись.
                    // Придуманное правдоподобное объяснение хуже отсутствия объяснения:
                    // человек пойдёт чинить не то.
                    Diag.Log("перехват не установлен, ошибка " +
                             System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    Failure = "Windows не дала установить перехват клавиатуры — без него не " +
                              "работает ни одно переназначение. Попробуйте перезапустить программу.";
                    return;
                }

                Native.MSG msg;
                while (Native.GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    // Просьба снять всё зажатое приходит сюда сообщением, а не вызовом.
                    // Раньше её звали прямо из потока окна и из потока таймера — они
                    // писали в те же Dictionary и HashSet, которые в этот момент читал
                    // хук. Одновременная запись портит их внутренние цепочки, и в худшем
                    // случае обработчик зацикливается: Windows снимает перехват, а всё
                    // зажатое так и остаётся зажатым.
                    if (msg.hwnd == IntPtr.Zero && msg.message == WmRelease) { ReleaseEverything(); continue; }
                    if (msg.hwnd == IntPtr.Zero && msg.message == WmCheck) { CheckHookAlive(); continue; }
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessageW(ref msg);
                }
            }
            catch (Exception e)
            {
                // Поток хука не имеет права уронить программу целиком.
                // Наружу — что случилось; подробности исключения в журнал, человеку
                // от HRESULT пользы нет.
                Diag.Log("поток перехвата прервался", e);
                Failure = "Перехват клавиатуры прервался из-за сбоя — перезапустите программу.";
            }
            finally
            {
                // Идентификатор снимаем первым: Windows переиспользует номера потоков,
                // и просьба отпустить, посланная после смерти этого, попала бы чужому.
                _threadId = 0;

                // Отпускаем своё до снятия перехвата и обязательно на этом потоке.
                // Синтетический control от переназначенного Caps Lock, оставшийся после
                // выхода, снять уже нечем: программы, которая его нажала, больше нет.
                try { ReleaseEverything(); } catch { }
                if (_hook != IntPtr.Zero)
                {
                    Native.UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
            }
        }

        private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code == Native.HC_ACTION)
            {
                _lastHookTick = Native.GetTickCount();
                bool swallow = false;
                try
                {
                    Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)
                        System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    swallow = Handle((int)wParam, k);
                }
                catch (Exception e) { Diag.Log("сбой в обработчике", e); }
                if (swallow) return new IntPtr(1);
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        /// <summary>Возвращает true, если событие нужно проглотить.</summary>
        private bool Handle(int msg, Native.KBDLLHOOKSTRUCT k)
        {
            if (k.dwExtraInfo == Native.InjectedTag) return false;

            Settings s = _cfg;
            if (s == null) return false;

            bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
            bool ext = (k.flags & Native.LLKHF_EXTENDED) != 0;
            int vk = (int)k.vkCode;

            if (!s.Enabled) return false;
            if (s.PauseWhenAppleAbsent && !Devices.AppleConnected) return false;

            // Призрачный Ctrl, который Windows добавляет к правому Alt на раскладках с AltGr.
            // Настоящим нажатием Ctrl он не является, и считать его таковым нельзя:
            // иначе третий уровень раскладки перестанет набираться.
            // Windows помечает его либо префиксом E0, либо битом 0x200 в скан-коде.
            bool phantomCtrl = (vk == Vk.LControl || vk == Vk.Control)
                            && (k.scanCode & 0x1FF) == 0x1D
                            && (ext || (k.scanCode & 0x200) != 0);
            if (phantomCtrl)
            {
                _phantomCtrl = down;
                // Дальше не пускаем вовсе. Раньше он доходил до HandleModifier и там
                // подчинялся переназначению левого control: со схемой «как в macOS»,
                // где control становится клавишей Windows, каждое нажатие правого ⌥
                // слало в Windows Win — то есть открывало «Пуск» посреди набора символа
                // третьего уровня. А если control переназначен на Caps Lock, ещё и
                // переворачивало регистр.
                return s.DisableAltGr;
            }

            ModKey phys;
            if (TryPhysical(vk, ext, out phys))
            {
                TrackModifier(s, phys, down);
                // Считаем по тому, что реально ушло в Windows, а не по тому, что нажали.
                // Caps Lock мог быть переназначен — у тех, кто пришёл с мака, на нём
                // обычно control, — и тогда индикатор не переключается, флаг трогать
                // нельзя. Но бывает и наоборот: на Caps Lock назначили другую клавишу,
                // индикатор переключается, а нажатия самого Caps Lock не было.
                // И только первое нажатие: автоповтор Windows переключателем не считает,
                // а мы перевернули бы флаг столько раз, сколько пришло повторов.
                bool toCaps = TargetFor(s, phys) == ModKey.CapsLock;
                if (toCaps && down && !_capsHeld) _capsOn = !_capsOn;
                if (toCaps) _capsHeld = down;
                return HandleModifier(s, phys, down);
            }

            // Навигация как в macOS: Fn+стрелки и Fn+Backspace.
            //
            // Ветку выбираем на первом нажатии, а дальше — и повторы, и отпускание — идём
            // по запомненной. Заменитель Fn отпускают раньше самой клавиши сплошь и рядом.
            // Смотри мы _fnHeld на отпускании, синтетическая Home осталась бы нажатой
            // навсегда; а смотри только на нём же при повторах, повторные ← уходили бы
            // в систему настоящими, тогда как отпускание мы бы съели — и уже настоящая
            // стрелка осталась бы нажатой.
            // Продолжение уже начатой навигации — впереди всего: повтор и отпускание
            // обязаны уйти туда же, куда ушло нажатие, иначе синтетическая Home останется
            // нажатой, а снять её нечем — клавиши Home на этой клавиатуре нет.
            if (_navActive.Contains(vk))
            {
                int t = FnNavTarget(vk);
                if (t != 0) return HandleNav(vk, t, down);
            }

            // ⌘+Tab ведёт себя как Alt+Tab.
            if (s.CmdTabSwitchesWindows && vk == Vk.Tab && _cmdHeld)
            {
                if (down)
                {
                    if (!_cmdTabAlt)
                    {
                        // Win придётся отпустить, а Windows считает нажатие одиночным,
                        // если между нажатием и отпусканием ничего не было, и открывает
                        // «Пуск» — который вдобавок заберёт фокус у переключателя окон.
                        // Подсовываем незанятый код: тот же приём, что в MacSend.
                        Input.Tap(VkNoop);
                        ReleaseInjected(ModKey.LWin);
                        ReleaseInjected(ModKey.RWin);
                        Input.Key(Vk.LMenu, true);
                        _cmdTabAlt = true;
                    }
                    Input.Tap(Vk.Tab);
                }
                return true;
            }

            // Сочетания macOS: ⌘C, ⌘←, ⌥← и прочие. Проверяем до функционального
            // ряда, но после модификаторов — состояние ⌘ и ⌥ к этому моменту известно.
            bool cmdMiss = false;
            if (s.MacShortcuts)
            {
                MacMod mm = MacMod.None;
                if (_cmdHeld) mm |= MacMod.Cmd;
                if (_altLeft || _altRight) mm |= MacMod.Opt;
                if (ShiftDown) mm |= MacMod.Shift;
                if (CtrlDown && !_phantomCtrl) mm |= MacMod.Ctrl;

                // Пробел разбирается отдельно: у ⌘Space и ⌃Space роль задаёт человек.
                if (vk == Vk.Space && (mm == MacMod.Cmd || mm == MacMod.Ctrl))
                {
                    MacShortcut space = MacKeys.SpaceAction(mm == MacMod.Cmd ? s.CmdSpace : s.CtrlSpace);
                    if (space != null)
                    {
                        if (down) MacSend(space);
                        return true;
                    }
                }

                if (mm != MacMod.None)
                {
                    MacShortcut sc = MacKeys.Find(vk, mm);
                    if (sc != null && s.MacEnabled(sc.Id))
                    {
                        // Аккорд отправляем один раз на удержание. Автоповтор приходит
                        // теми же нажатиями, а на маке такие сочетания не повторяются:
                        // задержал ⌘Q на полсекунды — и Alt+F4 уходит три десятка раз,
                        // закрывая окно за окном. Движения по тексту, наоборот, повторять
                        // надо, и они это про себя объявляют сами.
                        if (down)
                        {
                            if (sc.Repeats || _macFiredVk != vk) { _macFiredVk = vk; MacSend(sc); }
                        }
                        else if (_macFiredVk == vk) _macFiredVk = 0;
                        return true;
                    }

                    // Промах мимо таблицы отмечаем, но глотаем не здесь — см. конец Handle.
                    if ((mm & MacMod.Cmd) != 0) cmdMiss = true;
                }
            }

            // Начало навигации — уже ПОСЛЕ сочетаний macOS. Заменитель Fn и ⌥ по
            // умолчанию одна и та же клавиша, и тогда ⌥+← значит две разные вещи:
            // «на слово влево» из таблицы и Fn+← = Home. Побеждает таблица — её
            // сочетания человек видит списком и может выключить по одному, а движение
            // по словам в тексте нужнее прыжка в начало строки. Раньше выигрывала
            // навигация, и левый ⌥ вёл себя не так, как правый: ⌥+Backspace слева
            // удалял слово, справа — стирал символ впереди.
            if (down && _fnHeld && s.FnNavigation)
            {
                int target = FnNavTarget(vk);
                if (target != 0) return HandleNav(vk, target, down);
            }

            if (vk >= Vk.F1 && vk <= Vk.F24)
                return HandleFunctionKey(s, vk - Vk.F1, vk, down);

            // Цифровой блок Apple: ⌧ приходит как Num Lock и невзначай выключает блок,
            // а «=» шлёт VK_CLEAR со скан-кодом 0x59, который Windows просто игнорирует.
            if (vk == Vk.NumLock) { if (HandleSingle(s, vk, s.NumpadClear, down)) return true; }
            else if (k.scanCode == 0x59 && vk == Vk.Clear) { if (HandleSingle(s, vk, s.NumpadEquals, down)) return true; }

            // Клавиши японской раскладки: かな, 変換, 無変換, ろ, ¥.
            for (int i = 0; i < JisScans.Length; i++)
            {
                if (k.scanCode != JisScans[i]) continue;
                KeyAction ja = Actions.Get(s.JisKey(i));
                if (ja.Kind == ActionKind.PassThrough) break;
                if (down) Actions.Begin(ja, false, s.BrightnessStep);
                else Actions.End(ja);
                return true;
            }

            if (s.AppleLayoutEnabled)
            {
                int r = HandleLayout(s, k, down);
                if (r >= 0) return r != 0;
            }

            // Перестановка не в else: HandleLayout отступает в четырёх случаях — для языка
            // раскладка не назначена, зажат Ctrl или Win, зажат Alt не как третий уровень,
            // у клавиши нет строки в таблице. Раньше в этих случаях перестановка молча
            // не выполнялась, и выходило, что по-русски клавиши стоят правильно,
            // а по-английски переставлены, — при том что переключатель в окне погашен
            // и обещает, что раскладки Apple всё расставят сами.
            // Отпускание идёт туда же, куда ушло нажатие, — по запомненному, а не по
            // нынешним настройкам. Иначе достаточно, чтобы между нажатием и отпусканием
            // сменилось исполнение (а оно сбрасывается на каждом переподключении
            // клавиатуры по Bluetooth, то есть на каждом пробуждении), — и подставленная
            // клавиша осталась бы зажатой навсегда.
            if (!down && _isoSwapped.Remove(k.scanCode))
            {
                Input.Scan((ushort)(k.scanCode == 0x29 ? 0x56 : 0x29), false, false);
                return true;
            }
            if (down && s.SwapIsoKeys && Physical(s) == PhysLayout.Iso
                && (k.scanCode == 0x29 || k.scanCode == 0x56))
            {
                _isoSwapped.Add(k.scanCode);
                Input.Scan((ushort)(k.scanCode == 0x29 ? 0x56 : 0x29), false, true);
                return true;
            }

            // Сочетание с ⌘, которого в таблице нет. Глотаем — но в самом конце, когда
            // все прочие правила уже отказались: раньше это стояло сразу после таблицы
            // и съедало отпускания, нужные F-ряду, цифровому блоку и раскладке, а те
            // оставались с зажатой синтетической клавишей.
            //
            // Пропустить нельзя: после MacSend клавиша Windows отпущена без возврата,
            // и в приложение улетела бы голая буква; а для пришедшего с мака промах
            // обернулся бы действием Windows — ⌘I это Win+I, ⌘L блокировка экрана.
            //
            // И обязательно затычка. Windows считает нажатие Win одиночным, если между
            // ним и отпусканием ничего не было, — а мы как раз съели то, что было между.
            // Без неё Win+E не «ничего не делает», а вываливает «Пуск» поверх работы:
            // замерено стендом, ⌘1 открывал меню и забирал фокус.
            if (cmdMiss)
            {
                if (down && _winDown) Input.Tap(VkNoop);
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------
        //  Исполнение клавиатуры
        // ------------------------------------------------------------------

        public PhysLayout Physical(Settings s)
        {
            if (s != null && s.Physical != PhysLayout.Auto) return s.Physical;
            AppleModel m = Devices.AppleModel;
            if (m != null && m.Phys != PhysLayout.Auto) return m.Phys;
            return KeyWatch.DetectedPhysical;
        }

        // ------------------------------------------------------------------
        //  Раскладка Apple
        // ------------------------------------------------------------------

        private IntPtr ForegroundLayout()
        {
            int now = Environment.TickCount;
            if (_hkl != IntPtr.Zero && unchecked(now - _hklStamp) < 150) return _hkl;
            IntPtr w = Native.GetForegroundWindow();
            uint tid = w == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(w, IntPtr.Zero);
            _hkl = Native.GetKeyboardLayout(tid);
            _hklStamp = now;
            return _hkl;
        }

        private AppleLayoutFile CurrentLayout(Settings s)
        {
            IntPtr hkl = ForegroundLayout();
            int lang = (int)(hkl.ToInt64() & 0xFFFF);
            return Layouts.ById(s.LayoutFor(lang));
        }

        /// <summary>-1 — не наше дело; 0 — пропустить; 1 — проглотить.</summary>
        private int HandleLayout(Settings s, Native.KBDLLHOOKSTRUCT k, bool down)
        {
            // Отпускание всё равно снимаем с учёта: иначе съеденный скан-код остаётся
            // в списке до следующего сброса, и приложение однажды получит отпускание
            // клавиши, нажатия которой не видело.
            if (_ctrlSources.Count > 0 || _winSources.Count > 0)
            {
                if (!down && _swallowed.Remove(k.scanCode)) return 1;
                return -1;
            }

            AppleLayoutFile lay = CurrentLayout(s);
            if (lay == null) return -1;

            LayoutKey key = lay.Key((int)k.scanCode);
            if (key == null)
            {
                // Пробел после мёртвой клавиши даёт сам знак: «´» + пробел = «´»,
                // и сам пробел при этом не печатается.
                if (_deadPrefix != null && down && k.vkCode == Vk.Space)
                {
                    FlushDead(lay, true);
                    _swallowed.Add(k.scanCode);
                    return 1;
                }
                if (_deadPrefix != null && down) FlushDead(lay, false);
                if (!down && _swallowed.Remove(k.scanCode)) return 1;
                return -1;
            }

            bool optWanted;
            switch (s.OptLevel)
            {
                case OptLevel.AnyOption: optWanted = _altLeft || _altRight; break;
                // Если правый ⌥ объявлен обычным Alt, третьего уровня на нём быть
                // не может — иначе настройка обещает одно, а делает противоположное:
                // свой третий уровень остаётся, а пропадает системный AltGr.
                case OptLevel.RightOption: optWanted = _altRight && !s.DisableAltGr; break;
                default: optWanted = false; break;
            }
            // Alt, который не просили считать третьим уровнем, оставляем меню.
            if ((_altLeft || _altRight) && !optWanted) return -1;

            if (!down)
            {
                if (_swallowed.Remove(k.scanCode)) return 1;
                return -1;
            }

            string text = key.Text(ShiftDown, optWanted);
            if (text == null) return -1;
            bool dead = key.Dead(ShiftDown, optWanted);

            if (_capsOn && text.Length == 1 && Char.IsLetter(text[0]))
                text = ShiftDown ? text.ToLowerInvariant() : text.ToUpperInvariant();

            // Обычно подменяем только то, что отличается от раскладки Microsoft для этого языка:
            // остальные нажатия пусть идут своим ходом, без синтетического ввода.
            if (_deadPrefix == null && !key.Differs(ShiftDown, optWanted)) return -1;

            _swallowed.Add(k.scanCode);

            if (dead)
            {
                if (_deadPrefix != null) EmitText(_deadPrefix, optWanted);
                _deadPrefix = text;
                return 1;
            }

            if (_deadPrefix != null)
            {
                string composed = lay.Compose(_deadPrefix, text);
                string result = composed != null ? composed : _deadPrefix + text;
                _deadPrefix = null;
                EmitText(result, optWanted);
                return 1;
            }

            EmitText(text, optWanted);
            return 1;
        }

        private void FlushDead(AppleLayoutFile lay, bool space)
        {
            string prefix = _deadPrefix;
            _deadPrefix = null;
            if (prefix == null) return;
            if (space)
            {
                string composed = lay.Compose(prefix, " ");
                EmitText(composed != null ? composed : prefix, false);
            }
            else EmitText(prefix, false);
        }

        private void EmitText(string text, bool optHeld)
        {
            var released = new List<int>();
            if (optHeld)
            {
                if (_altLeft) Release(ModKey.LAlt, released);
                if (_altRight) Release(ModKey.RAlt, released);
                if (_phantomCtrl) { Input.Key(Vk.LControl, false); released.Add(Vk.LControl); }
            }
            Input.Text(text);
            for (int i = released.Count - 1; i >= 0; i--) Input.Key(released[i], true);
        }

        private void Release(ModKey phys, List<int> released)
        {
            int vk = EffectiveVk(phys);
            if (vk == 0) return;
            Input.Key(vk, false);
            released.Add(vk);
        }

        private int EffectiveVk(ModKey phys)
        {
            int vk;
            if (_injected.TryGetValue(phys, out vk)) return vk;
            return ModNames.VirtualKey(phys);
        }

        // ------------------------------------------------------------------
        //  Модификаторы, F-клавиши, навигация
        // ------------------------------------------------------------------

        private void TrackModifier(Settings s, ModKey phys, bool down)
        {
            // Что видит Windows. Add и Remove при автоповторе безобидны: множество.
            ModKey target = TargetFor(s, phys);
            if (down && (target == ModKey.LCtrl || target == ModKey.RCtrl)) _ctrlSources.Add(phys);
            else _ctrlSources.Remove(phys);
            if (down && (target == ModKey.LWin || target == ModKey.RWin)) _winSources.Add(phys);
            else _winSources.Remove(phys);

            // А это — что нажал человек: по нему опознаются сочетания и заменитель Fn.
            switch (phys)
            {
                case ModKey.LShift: _shiftLeft = down; break;
                case ModKey.RShift: _shiftRight = down; break;
                case ModKey.LCtrl: _ctrlLeft = down; break;
                case ModKey.RCtrl: _ctrlRight = down; break;
                case ModKey.LWin: case ModKey.RWin: _winDown = down; break;
                case ModKey.LAlt: _altLeft = down; break;
                case ModKey.RAlt: _altRight = down; break;
            }
        }

        private bool HandleModifier(Settings s, ModKey phys, bool down)
        {
            ModKey target = TargetFor(s, phys);

            if (phys == ModKey.LWin || phys == ModKey.RWin)
            {
                // По назначению, а не по клавише. Если ⌘ переназначена — а схема
                // «как в macOS» делает её Ctrl, — то Ctrl+Tab должен листать вкладки,
                // а не открывать переключатель окон, и переводить сочетания ей уже
                // незачем: человек попросил обмен клавиш вместо перевода. Заодно
                // «выключить клавишу» перестаёт работать как ⌘.
                _cmdHeld = down && (target == ModKey.LWin || target == ModKey.RWin);
                if (!down && _cmdTabAlt)
                {
                    Input.Key(Vk.LMenu, false);
                    _cmdTabAlt = false;
                }
            }

            // Заменитель Fn отслеживаем по физической клавише, каким бы ни было её назначение.
            if (s.FnSubstitute != ModKey.None && phys == s.FnSubstitute)
            {
                _fnHeld = down;
                _fnEffectiveVk = down
                    ? (target == ModKey.None ? 0 : ModNames.VirtualKey(target))
                    : 0;
                if (!down) _subReleased = 0;
            }

            // Клавиши Win берём под контроль и без переназначения, если включён ⌘+Tab:
            // иначе настоящее нажатие Win уже ушло в систему и отменить его нечем.
            bool managed = target != phys
                        || (s.CmdTabSwitchesWindows && (phys == ModKey.LWin || phys == ModKey.RWin));
            if (!managed) return false;

            if (target == ModKey.None) return true;

            int tvk = ModNames.VirtualKey(target);
            if (tvk == 0) return true;

            Input.Key(tvk, down);
            if (down) _injected[phys] = tvk;
            else _injected.Remove(phys);
            return true;
        }

        private static int FnNavTarget(int vk)
        {
            switch (vk)
            {
                case Vk.Left: return Vk.Home;
                case Vk.Right: return Vk.End;
                case Vk.Up: return Vk.Prior;
                case Vk.Down: return Vk.Next;
                case Vk.Back: return Vk.Delete;
                case Vk.Return: return Vk.Insert;
                default: return 0;
            }
        }

        private bool HandleNav(int sourceVk, int targetVk, bool down)
        {
            if (down)
            {
                if (_navActive.Add(sourceVk)) SubstituteRelease();
                Input.Key(targetVk, true);
            }
            else
            {
                Input.Key(targetVk, false);
                if (_navActive.Remove(sourceVk)) SubstituteRestore();
            }
            return true;
        }

        /// <summary>Alt и Win, нажатые и отпущенные вхолостую, Windows считает командой.</summary>
        private static bool IsMenuKey(int vk)
        {
            return vk == Vk.LMenu || vk == Vk.RMenu || vk == Vk.LWin || vk == Vk.RWin;
        }

        /// <summary>
        /// Снять с заменителя Fn его обычное значение, пока он работает как Fn.
        ///
        /// Тот же приём с незанятым кодом, что в MacSend и в ⌘+Tab, нужен и здесь, причём
        /// дважды. Между настоящим нажатием заменителя и нашим синтетическим отпусканием
        /// не было ничего — для Windows это одиночный Alt, то есть строка меню; и между
        /// нашим возвратом и настоящим отпусканием тоже ничего нет. С умолчаниями
        /// (заменитель — правый ⌥) на каждом ⌥+← в Word дважды всплывали подсказки клавиш.
        /// </summary>
        private void SubstituteRelease()
        {
            if (_subReleased++ == 0 && _fnEffectiveVk != 0)
            {
                if (IsMenuKey(_fnEffectiveVk)) Input.Tap(VkNoop);
                Input.Key(_fnEffectiveVk, false);
            }
        }

        private void SubstituteRestore()
        {
            if (--_subReleased <= 0)
            {
                _subReleased = 0;
                if (_fnHeld && _fnEffectiveVk != 0)
                {
                    Input.Key(_fnEffectiveVk, true);
                    if (IsMenuKey(_fnEffectiveVk)) Input.Tap(VkNoop);
                }
            }
        }

        /// <summary>Одиночная клавиша со своим назначением. false — оставить как есть.</summary>
        /// <summary>
        /// Клавиша с одним назначением: ⌧, «=» цифрового блока, японские.
        ///
        /// Действие защёлкивается на первом нажатии по тем же трём причинам, что и у
        /// F-ряда: настройки меняются из окна под зажатой клавишей, автоповтор приходит
        /// теми же нажатиями, а между нажатием и отпусканием программу могут поставить
        /// на паузу. Любая из трёх оставляла синтетическую клавишу нажатой — а по
        /// умолчанию на ⌧ висит Delete, которой на Magic Keyboard физически нет,
        /// и снять её было бы нечем.
        /// </summary>
        private bool HandleSingle(Settings s, int sourceVk, string actionId, bool down)
        {
            string id;
            if (down)
            {
                if (!_singleAction.TryGetValue(sourceVk, out id))
                {
                    id = actionId;
                    if (Actions.Get(id).Kind == ActionKind.PassThrough) return false;
                    _singleAction[sourceVk] = id;
                    Actions.Begin(Actions.Get(id), false, s.BrightnessStep);
                    return true;
                }
                // Автоповтор: то же действие, но повтором — аккорды и запуск программ
                // на нём не срабатывают, иначе удержание плодило бы окна калькулятора.
                Actions.Begin(Actions.Get(id), true, s.BrightnessStep);
                return true;
            }

            if (!_singleAction.TryGetValue(sourceVk, out id)) return false;
            _singleAction.Remove(sourceVk);
            Actions.End(Actions.Get(id));
            return true;
        }

        /// <summary>
        /// Уступаем ли эту клавишу ряда драйверу. Одно место на программу: раньше правило
        /// было записано и здесь, и в окне, причём в окне без «только F1–F12» — и страница
        /// проверки уверяла, что драйверу отданы F13–F19, которых он не трогает.
        /// </summary>
        public static bool YieldsRow(Settings s, int index)
        {
            return s != null && index < 12 && s.YieldToAppleDriver
                && AppleDriver.TakesFunctionRow && KeyWatch.MediaSeen;
        }

        private bool HandleFunctionKey(Settings s, int index, int vk, bool down)
        {
            // Всё решается на ПЕРВОМ нажатии и держится до отпускания: и ветка (медиа
            // или настоящая F-клавиша), и решение уступить ряд драйверу, и назначенное
            // действие. Три вещи, которые иначе успевают смениться под зажатой клавишей:
            // заменитель Fn отпускают раньше её сплошь и рядом; автоповтор приходит теми
            // же нажатиями, а _fnHeld к тому времени уже другой; настройки меняются из
            // окна в любой момент. Любая из трёх давала одно и то же: отпускание уходило
            // в другую ветку, синтетическая F4 оставалась нажатой навсегда, а следующее
            // ⌥+F4 давало Alt+F4 — закрытие окна вместо F4.
            if (down && !_fkeyDown[index])
            {
                _fkeyMedia[index] = s.MediaFirst ^ _fnHeld;
                _fkeyAction[index] = s.FKey(index);

                // Уступаем ряд родному драйверу — но с двумя оговорками.
                //
                // Первая: драйвер занимается только F1–F12, потому что медиазначки Apple
                // напечатаны именно там. F13–F19 он не трогает, и уступать их — значит
                // молча их обесточить.
                //
                // Вторая: уступаем, только пока видим, что драйвер работает. Признак не
                // в реестре, а в наблюдении: если драйвер преобразует ряд, F-клавиши до
                // нас не доходят вовсе (проверено — приходит сразу медиакод), а значит
                // хоть один медиакод мы уже видели. Не видели ни одного, а F-клавиши
                // идут — драйвер до этой клавиатуры не добрался (так бывает по Bluetooth),
                // и уступать некому: ряд просто умрёт.
                _fkeyYield[index] = YieldsRow(s, index);
            }
            else if (!_fkeyDown[index])
            {
                // Отпускание клавиши, которую мы не брали, — не наше дело.
                return false;
            }

            if (_fkeyYield[index]) return false;

            if (!_fkeyMedia[index])
            {
                // Нужна настоящая F-клавиша. Если её вызвали заменителем Fn, снимаем
                // с него модификатор, иначе получится Alt+F4 вместо F4.
                if (down)
                {
                    if (!_fnHeld || _fnEffectiveVk == 0) return false;
                    if (!_fkeyDown[index]) { _fkeyDown[index] = true; SubstituteRelease(); }
                    Input.Key(vk, true);
                }
                else
                {
                    Input.Key(vk, false);
                    _fkeyDown[index] = false;
                    SubstituteRestore();
                }
                return true;
            }

            KeyAction a = Actions.Get(_fkeyAction[index]);
            if (a.Kind == ActionKind.PassThrough) return false;

            if (down)
            {
                bool repeat = _fkeyDown[index];
                _fkeyDown[index] = true;
                Actions.Begin(a, repeat, s.BrightnessStep);
            }
            else
            {
                _fkeyDown[index] = false;
                _fkeyAction[index] = null;
                Actions.End(a);
            }
            return true;
        }

        private static ModKey TargetFor(Settings s, ModKey phys)
        {
            switch (phys)
            {
                case ModKey.LCtrl: return s.MapLCtrl;
                case ModKey.LWin: return s.MapLWin;
                case ModKey.LAlt: return s.MapLAlt;
                case ModKey.RAlt: return s.MapRAlt;
                case ModKey.RWin: return s.MapRWin;
                case ModKey.CapsLock: return s.MapCapsLock;
                default: return phys;
            }
        }

        private static bool TryPhysical(int vk, bool ext, out ModKey key)
        {
            switch (vk)
            {
                case Vk.LControl: key = ModKey.LCtrl; return true;
                case Vk.RControl: key = ModKey.RCtrl; return true;
                case Vk.LWin: key = ModKey.LWin; return true;
                case Vk.RWin: key = ModKey.RWin; return true;
                case Vk.LMenu: key = ModKey.LAlt; return true;
                case Vk.RMenu: key = ModKey.RAlt; return true;
                case Vk.LShift: key = ModKey.LShift; return true;
                case Vk.RShift: key = ModKey.RShift; return true;
                case Vk.Capital: key = ModKey.CapsLock; return true;
                case Vk.Control: key = ext ? ModKey.RCtrl : ModKey.LCtrl; return true;
                case Vk.Menu: key = ext ? ModKey.RAlt : ModKey.LAlt; return true;
                case Vk.Shift: key = ModKey.LShift; return true;
                default: key = ModKey.None; return false;
            }
        }

        private void ReleaseInjected(ModKey phys)
        {
            int vk;
            if (_injected.TryGetValue(phys, out vk)) Input.Key(vk, false);
        }

        /// <summary>
        /// Отправляет то, во что переводится сочетание macOS.
        ///
        /// Перед отправкой зажатые модификаторы надо отпустить, иначе Windows увидит
        /// не Ctrl+C, а Win+Ctrl+C. Клавишу Windows и Alt отпускаем без возврата:
        /// сами по себе они открывают «Пуск» и строку меню, а пока они зажаты
        /// физически, следующее сочетание всё равно опознается по своему состоянию.
        /// ⇧ и control безобидны, их возвращаем — иначе после ⇧⌘Z следующая буква
        /// вышла бы строчной.
        /// </summary>
        // Незанятый виртуальный код: ни одна клавиша его не выдаёт, ни одно сочетание
        // на нём не висит. Нужен как безобидная «затычка» — см. ниже.
        private const int VkNoop = 0xE8;

        private void MacSend(MacShortcut sc)
        {
            bool shiftL = _shiftLeft, shiftR = _shiftRight;
            bool ctrlL = _ctrlLeft && !_phantomCtrl, ctrlR = _ctrlRight && !_phantomCtrl;

            // Windows открывает «Пуск», если клавишу Win нажали и отпустили, ничего
            // между ними не нажав. У нас выходит именно так: саму букву мы съели,
            // а Win пришлось отпустить перед отправкой аккорда — для Windows это
            // одиночное нажатие. Поэтому пока Win ещё зажата, подсовываем незанятый
            // код: он ничего не делает, но снимает признак одиночного нажатия.
            // Тот же приём спасает от строки меню, которую открывает одиночный Alt.
            if (_winDown || _altLeft || _altRight) Input.Tap(VkNoop);

            ModRelease(ModKey.LWin); ModRelease(ModKey.RWin);
            ModRelease(ModKey.LAlt); ModRelease(ModKey.RAlt);
            if (shiftL) ModRelease(ModKey.LShift);
            if (shiftR) ModRelease(ModKey.RShift);
            if (ctrlL) ModRelease(ModKey.LCtrl);
            if (ctrlR) ModRelease(ModKey.RCtrl);

            MacKeys.Send(sc);

            // Возвращаем ровно те стороны, что держали. Вернуть «любую» — значит
            // оставить в Windows нажатой клавишу, которой никто не нажимал: снять
            // её потом нечем, отпускание другой стороны пройдёт мимо.
            if (shiftL && _shiftLeft) ModPress(ModKey.LShift);
            if (shiftR && _shiftRight) ModPress(ModKey.RShift);
            if (ctrlL && _ctrlLeft) ModPress(ModKey.LCtrl);
            if (ctrlR && _ctrlRight) ModPress(ModKey.RCtrl);
        }

        // Отпускаем и нажимаем именно то, что реально ушло в Windows: у переназначенной
        // клавиши это не её собственный код, а код замены.
        private void ModRelease(ModKey phys)
        {
            int vk = EffectiveVk(phys);
            if (vk != 0) Input.Key(vk, false);
        }

        private void ModPress(ModKey phys)
        {
            int vk = EffectiveVk(phys);
            if (vk != 0) Input.Key(vk, true);
        }

        /// <summary>Отпустить всё, что мы могли зажать: иначе после смены настроек модификатор «залипнет».</summary>
        private static bool Held(int vk) { return (Native.GetKeyState(vk) & 0x8000) != 0; }

        private void ReleaseEverything()
        {
            try
            {
                foreach (KeyValuePair<ModKey, int> pair in new List<KeyValuePair<ModKey, int>>(_injected))
                    Input.Key(pair.Value, false);
                _injected.Clear();
                if (_cmdTabAlt) { Input.Key(Vk.LMenu, false); _cmdTabAlt = false; }
                _fnHeld = false;
                _fnEffectiveVk = 0;
                _subReleased = 0;
                _phantomCtrl = false;
                _cmdHeld = false;
                _macFiredVk = 0;
                _deadPrefix = null;
                _swallowed.Clear();
                _ctrlSources.Clear();
                _winSources.Clear();
                _capsHeld = false;

                // Отпускаем то, что держим ЗА человека, а не только модификаторы.
                // Синтетическая Home от Fn+← — такая же зажатая клавиша, и снять её
                // после нас нечем: клавиши Home на этой клавиатуре попросту нет.
                foreach (int src in new List<int>(_navActive))
                {
                    int target = FnNavTarget(src);
                    if (target != 0) Input.Key(target, false);
                }
                _navActive.Clear();

                // Одиночные клавиши и перестановка ISO — то же самое: отпустить некому.
                foreach (KeyValuePair<int, string> pair in new List<KeyValuePair<int, string>>(_singleAction))
                {
                    try { Actions.End(Actions.Get(pair.Value)); }
                    catch (Exception e) { Diag.Log("не удалось отпустить одиночную клавишу", e); }
                }
                _singleAction.Clear();

                foreach (uint scan in new List<uint>(_isoSwapped))
                    Input.Scan((ushort)(scan == 0x29 ? 0x56 : 0x29), false, false);
                _isoSwapped.Clear();

                // И начатые действия: зажатая медиаклавиша сама не отпустится.
                // У настоящей F-клавиши синтетическая совпадает с физической, поэтому
                // её отпускание доедет само — там довольно очистки.
                for (int i = 0; i < _fkeyDown.Length; i++)
                {
                    if (_fkeyDown[i] && _fkeyMedia[i] && !_fkeyYield[i] && _fkeyAction[i] != null)
                    {
                        try { Actions.End(Actions.Get(_fkeyAction[i])); }
                        catch (Exception e) { Diag.Log("не удалось отпустить действие", e); }
                    }
                    _fkeyDown[i] = false;
                    _fkeyMedia[i] = false;
                    _fkeyYield[i] = false;
                    _fkeyAction[i] = null;
                }

                // Модификаторы не обнуляем, а перечитываем у Windows. Пока перехват стоял
                // на паузе — выключен галочкой, клавиатура отключена, человек ушёл на
                // защищённый рабочий стол по Ctrl+Alt+Del — отпускания прошли мимо нас.
                // «Зажатый» ⇧ после этого ломал опознание любого сочетания macOS: Find
                // требует точного совпадения набора модификаторов.
                _shiftLeft = Held(Vk.LShift); _shiftRight = Held(Vk.RShift);
                _ctrlLeft = Held(Vk.LControl); _ctrlRight = Held(Vk.RControl);
                _winDown = Held(Vk.LWin) || Held(Vk.RWin);
                _altLeft = Held(Vk.LMenu); _altRight = Held(Vk.RMenu);
                _capsOn = (Native.GetKeyState(Vk.Capital) & 1) != 0;
            }
            catch { }
        }
    }
}
