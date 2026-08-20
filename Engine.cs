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
        private const uint ScanIsoExtra = 0x56;
        private static readonly uint[] JisScans = { 0x70, 0x79, 0x7B, 0x73, 0x7D };

        private Thread _thread;
        private uint _threadId;
        private IntPtr _hook;
        private Native.HookProc _proc;
        private Timer _watch;

        private volatile Settings _cfg = new Settings();

        // Состояние — только для потока хука.
        private readonly bool[] _fkeyDown = new bool[Settings.MaxFKeys];
        private readonly Dictionary<ModKey, int> _injected = new Dictionary<ModKey, int>();
        private readonly HashSet<uint> _swallowed = new HashSet<uint>();
        private readonly HashSet<int> _navActive = new HashSet<int>();
        private bool _fnHeld;
        private int _fnEffectiveVk;
        private int _subReleased;
        private bool _cmdHeld;
        private bool _cmdTabAlt;
        private bool _shiftDown, _ctrlDown, _winDown, _altLeft, _altRight;
        private bool _phantomCtrl;
        private bool _capsOn;
        private string _deadPrefix;
        private IntPtr _hkl;
        private int _hklStamp;

        private volatile int _detectedPhys = (int)PhysLayout.Ansi;

        /// <summary>Что удалось понять об исполнении клавиатуры по нажатиям.</summary>
        public PhysLayout DetectedPhysical { get { return (PhysLayout)_detectedPhys; } }

        /// <summary>Что-то изменилось в наборе подключённых клавиатур.</summary>
        public event Action DevicesChanged;

        public bool Running { get { return _hook != IntPtr.Zero; } }

        /// <summary>Пусто, если всё в порядке; иначе — почему перехват не работает.</summary>
        public volatile string Failure;

        public void Apply(Settings s)
        {
            _cfg = s;
            ReleaseEverything();
            EnsureNumLock(s);
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
                    if (Devices.Rescan())
                    {
                        ReleaseEverything();
                        Action h = DevicesChanged;
                        if (h != null) h();
                    }
                }
                catch { }
            }, null, 2000, 2000);

            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "MagicKeys hook";
            _thread.Start();
        }

        public void Stop()
        {
            if (_watch != null) { _watch.Dispose(); _watch = null; }
            if (_threadId != 0) Native.PostThreadMessageW(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread = null;
        }

        private void Run()
        {
            try
            {
                _threadId = Native.GetCurrentThreadId();
                _proc = HookProc;
                _hook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandleW(null), 0);
                if (_hook == IntPtr.Zero)
                {
                    Failure = "Windows не дала установить перехват клавиатуры (ошибка " +
                              System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ").";
                    return;
                }

                Native.MSG msg;
                while (Native.GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessageW(ref msg);
                }
            }
            catch (Exception e)
            {
                // Поток хука не имеет права уронить программу целиком.
                Failure = e.Message;
            }
            finally
            {
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

            NoteScan(k.scanCode);

            if (!s.Enabled) return false;
            if (s.PauseWhenAppleAbsent && !Devices.AppleConnected) return false;

            // Призрачный Ctrl, который Windows добавляет к правому Alt на раскладках с AltGr.
            // Настоящим нажатием Ctrl он не является, и считать его таковым нельзя:
            // иначе третий уровень раскладки перестанет набираться.
            // Windows помечает его либо префиксом E0, либо битом 0x200 в скан-коде.
            bool phantomCtrl = (vk == Vk.LControl || vk == Vk.Control)
                            && (k.scanCode & 0x1FF) == 0x1D
                            && (ext || (k.scanCode & 0x200) != 0);
            if (phantomCtrl) _phantomCtrl = down;
            if (s.DisableAltGr && phantomCtrl) return true;

            ModKey phys;
            if (TryPhysical(vk, ext, out phys))
            {
                if (!phantomCtrl) TrackModifier(phys, down);
                if (phys == ModKey.CapsLock && down) _capsOn = !_capsOn;
                return HandleModifier(s, phys, down);
            }

            // Навигация как в macOS: Fn+стрелки и Fn+Backspace.
            if (_fnHeld && s.FnNavigation)
            {
                int target = FnNavTarget(vk);
                if (target != 0) return HandleNav(vk, target, down);
            }

            // ⌘+Tab ведёт себя как Alt+Tab.
            if (s.CmdTabSwitchesWindows && vk == Vk.Tab && _cmdHeld)
            {
                if (down)
                {
                    if (!_cmdTabAlt)
                    {
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
            if (s.MacShortcuts)
            {
                MacMod mm = MacMod.None;
                if (_cmdHeld) mm |= MacMod.Cmd;
                if (_altLeft || _altRight) mm |= MacMod.Opt;
                if (_shiftDown) mm |= MacMod.Shift;
                if (_ctrlDown && !_phantomCtrl) mm |= MacMod.Ctrl;

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
                        if (down) MacSend(sc);
                        return true;
                    }
                }
            }

            if (vk >= Vk.F1 && vk <= Vk.F24)
                return HandleFunctionKey(s, vk - Vk.F1, vk, down);

            // Цифровой блок Apple: ⌧ приходит как Num Lock и невзначай выключает блок,
            // а «=» шлёт VK_CLEAR со скан-кодом 0x59, который Windows просто игнорирует.
            if (vk == Vk.NumLock) { if (HandleSingle(s, s.NumpadClear, down)) return true; }
            else if (k.scanCode == 0x59 && vk == Vk.Clear) { if (HandleSingle(s, s.NumpadEquals, down)) return true; }

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
            else if (s.SwapIsoKeys && Physical(s) == PhysLayout.Iso)
            {
                if (k.scanCode == 0x29) { Input.Scan(0x56, false, down); return true; }
                if (k.scanCode == 0x56) { Input.Scan(0x29, false, down); return true; }
            }

            return false;
        }

        // ------------------------------------------------------------------
        //  Исполнение клавиатуры
        // ------------------------------------------------------------------

        private void NoteScan(uint scan)
        {
            if (scan == ScanIsoExtra && _detectedPhys == (int)PhysLayout.Ansi)
                _detectedPhys = (int)PhysLayout.Iso;
            else
            {
                for (int i = 0; i < JisScans.Length; i++)
                    if (scan == JisScans[i]) { _detectedPhys = (int)PhysLayout.Jis; break; }
            }
        }

        public PhysLayout Physical(Settings s)
        {
            if (s != null && s.Physical != PhysLayout.Auto) return s.Physical;
            AppleModel m = Devices.AppleModel;
            if (m != null && m.Phys != PhysLayout.Auto) return m.Phys;
            return DetectedPhysical;
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
            if (_ctrlDown || _winDown) return -1;

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
                case OptLevel.RightOption: optWanted = _altRight; break;
                default: optWanted = false; break;
            }
            // Alt, который не просили считать третьим уровнем, оставляем меню.
            if ((_altLeft || _altRight) && !optWanted) return -1;

            if (!down)
            {
                if (_swallowed.Remove(k.scanCode)) return 1;
                return -1;
            }

            string text = key.Text(_shiftDown, optWanted);
            if (text == null) return -1;
            bool dead = key.Dead(_shiftDown, optWanted);

            if (_capsOn && text.Length == 1 && Char.IsLetter(text[0]))
                text = _shiftDown ? text.ToLowerInvariant() : text.ToUpperInvariant();

            // Обычно подменяем только то, что отличается от раскладки Microsoft для этого языка:
            // остальные нажатия пусть идут своим ходом, без синтетического ввода.
            if (_deadPrefix == null && !s.AppleLayoutAll && !key.Differs(_shiftDown, optWanted)) return -1;

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

        private void TrackModifier(ModKey phys, bool down)
        {
            switch (phys)
            {
                case ModKey.LShift: case ModKey.RShift: _shiftDown = down; break;
                case ModKey.LCtrl: case ModKey.RCtrl: _ctrlDown = down; break;
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
                _cmdHeld = down;
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

        /// <summary>Снять с заменителя Fn его обычное значение, пока он работает как Fn.</summary>
        private void SubstituteRelease()
        {
            if (_subReleased++ == 0 && _fnEffectiveVk != 0) Input.Key(_fnEffectiveVk, false);
        }

        private void SubstituteRestore()
        {
            if (--_subReleased <= 0)
            {
                _subReleased = 0;
                if (_fnHeld && _fnEffectiveVk != 0) Input.Key(_fnEffectiveVk, true);
            }
        }

        /// <summary>Одиночная клавиша со своим назначением. false — оставить как есть.</summary>
        private bool HandleSingle(Settings s, string actionId, bool down)
        {
            KeyAction a = Actions.Get(actionId);
            if (a.Kind == ActionKind.PassThrough) return false;
            if (down) Actions.Begin(a, false, s.BrightnessStep);
            else Actions.End(a);
            return true;
        }

        private bool HandleFunctionKey(Settings s, int index, int vk, bool down)
        {
            bool media = s.MediaFirst ^ _fnHeld;

            if (!media)
            {
                // Нужна настоящая F-клавиша. Если её вызвали заменителем Fn, снимаем
                // с него модификатор, иначе получится Alt+F4 вместо F4.
                if (!_fnHeld || _fnEffectiveVk == 0) return false;
                if (down)
                {
                    if (!_fkeyDown[index]) { _fkeyDown[index] = true; SubstituteRelease(); }
                    Input.Key(vk, true);
                }
                else
                {
                    Input.Key(vk, false);
                    if (_fkeyDown[index]) { _fkeyDown[index] = false; SubstituteRestore(); }
                }
                return true;
            }

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
            if (index < 12 && s.YieldToAppleDriver && AppleDriver.TakesFunctionRow && KeyWatch.MediaSeen)
                return false;

            KeyAction a = Actions.Get(s.FKey(index));
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
            bool shift = _shiftDown;
            bool ctrl = _ctrlDown && !_phantomCtrl;

            // Windows открывает «Пуск», если клавишу Win нажали и отпустили, ничего
            // между ними не нажав. У нас выходит именно так: саму букву мы съели,
            // а Win пришлось отпустить перед отправкой аккорда — для Windows это
            // одиночное нажатие. Поэтому пока Win ещё зажата, подсовываем незанятый
            // код: он ничего не делает, но снимает признак одиночного нажатия.
            // Тот же приём спасает от строки меню, которую открывает одиночный Alt.
            if (_winDown || _altLeft || _altRight) Input.Tap(VkNoop);

            ModRelease(ModKey.LWin); ModRelease(ModKey.RWin);
            ModRelease(ModKey.LAlt); ModRelease(ModKey.RAlt);
            if (shift) { ModRelease(ModKey.LShift); ModRelease(ModKey.RShift); }
            if (ctrl) { ModRelease(ModKey.LCtrl); ModRelease(ModKey.RCtrl); }

            MacKeys.Send(sc);

            if (shift) { if (_shiftDown) ModPress(ModKey.LShift); }
            if (ctrl) { if (_ctrlDown) ModPress(ModKey.LCtrl); }
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
                _deadPrefix = null;
                _swallowed.Clear();
                _navActive.Clear();
                for (int i = 0; i < _fkeyDown.Length; i++) _fkeyDown[i] = false;
            }
            catch { }
        }
    }
}
