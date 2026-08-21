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
        private Thread _thread;
        private volatile uint _threadId;
        // volatile: пишет поток хука, читает поток окна через Running.
        private volatile IntPtr _hook;
        private Native.HookProc _proc;
        private Timer _watch;

        private volatile Settings _cfg = new Settings();

        // Состояние — только для потока хука.
        private readonly bool[] _fkeyDown = new bool[Settings.MaxFKeys];
        // Какой веткой пошло нажатие — медиа или настоящая F-клавиша — и что было
        // на клавишу назначено. Запоминается, потому что к отпусканию и заменитель
        // Fn, и настройки успевают стать другими.
        // «Ветка на это нажатие выбрана» — не то же самое, что «мы держим клавишу».
        // Раньше это было одно поле, и три выхода выбирали ветку, не выставив его:
        // уступленный драйверу ряд, «нужна настоящая F-клавиша, а Fn не держат»
        // и действие «оставить как есть». На следующем автоповторе ветка выбиралась
        // заново — уже с другим _fnHeld, — и отпускание съедал наш слой, тогда как
        // нажатие ушло в приложение настоящей F-клавишей. Клавиша оставалась зажатой.
        private readonly bool[] _fkeyLatched = new bool[Settings.MaxFKeys];
        // И решение «берём ли мы это нажатие себе» — там же и по тем же причинам.
        // Оно зависит от _fnHeld, а заменитель Fn нажимают и отпускают под зажатой
        // клавишей сплошь и рядом: решение, принимаемое заново на каждом повторе,
        // отпускало зажатый человеком ⌥ посреди удержания и съедало отпускание
        // клавиши, нажатие которой уже ушло в приложение своим ходом.
        private readonly bool[] _fkeyTake = new bool[Settings.MaxFKeys];
        private readonly bool[] _fkeyMedia = new bool[Settings.MaxFKeys];
        private readonly bool[] _fkeyYield = new bool[Settings.MaxFKeys];
        private readonly string[] _fkeyAction = new string[Settings.MaxFKeys];

        /// <summary>Чьи нажатия взял на себя разбор модификаторов.</summary>
        private readonly HashSet<ModKey> _modTaken = new HashSet<ModKey>();

        /// <summary>Какие клавиши-модификаторы человек держит прямо сейчас.</summary>
        private readonly HashSet<ModKey> _modsDown = new HashSet<ModKey>();

        // Чьё нажатие взял на себя слой аккордов и что по нему сработало: сочетания
        // macOS, ⌘+Tab (значение null — у него своя посылка), ⌘+пробел и промах мимо
        // таблицы при зажатой ⌘.
        //
        // Без этого отпускание решалось по модификаторам, зажатым в этот миг, а не по
        // тому, как ушло нажатие. Достаточно нажать ⌥ уже ПОСЛЕ стрелки: нажатие ушло
        // в приложение обычной стрелкой, а отпускание вдруг оказывалось ⌥+← из таблицы
        // и проглатывалось. Стрелка оставалась зажатой навсегда — снять её нечем,
        // человек её уже отпустил. Тем же путём залипали буква после ⌘, Tab и пробел.
        private readonly Dictionary<int, MacShortcut> _chordTaken = new Dictionary<int, MacShortcut>();

        /// <summary>
        /// Метка «это ⌘+Tab»: у него посылка своя, не из таблицы. Раньше и он, и промах
        /// мимо таблицы клали в отображение null — и автоповтор проглоченной ⌘-клавиши
        /// отправлял Tab при зажатой клавише Windows, то есть открывал «Представление
        /// задач» столько раз, сколько пришло повторов.
        /// </summary>
        private static readonly MacShortcut CmdTab = new MacShortcut();

        // Что мы держим за человека помимо модификаторов: действие на одиночной клавише
        // и подставленный скан-код перестановки ISO. Без учёта их некому отпустить.
        private readonly Dictionary<int, string> _singleAction = new Dictionary<int, string>();
        private readonly HashSet<uint> _isoSwapped = new HashSet<uint>();
        private readonly Dictionary<ModKey, int> _injected = new Dictionary<ModKey, int>();
        /// <summary>
        /// Чей код мы сняли у Windows на время и намерены вернуть: клавиша и снятый код.
        ///
        /// Отдельно от _injected потому, что снимаем мы и у клавиш, которых не разбираем.
        /// Заводской заменитель Fn — правый ⌥, и он никуда не переназначен: записи о нём
        /// нет ни в _injected, ни в _modTaken. Alt мы у Windows на время навигации всё
        /// равно снимаем, а на вопрос «что Windows держит сейчас» без этого множества
        /// продолжали отвечать «Alt» — и готовый символ посреди удержания стрелки
        /// «возвращал» его нажатым, после чего Page Up уходил как Alt+Page Up.
        /// </summary>
        private readonly Dictionary<ModKey, int> _modLifted = new Dictionary<ModKey, int>();
        private readonly HashSet<uint> _swallowed = new HashSet<uint>();
        private readonly HashSet<int> _navActive = new HashSet<int>();
        /// <summary>
        /// Клавиши, нажатие которых ушло в приложение своим ходом и которые человек
        /// ещё держит.
        ///
        /// Седьмой слой, и он не «мы держим», а «держит приложение». Автоповтор Windows
        /// приходит теми же нажатиями и от первого неотличим. Без этой записи слой,
        /// спросивший Busy на автоповторе, брал клавишу себе посреди удержания — и потом
        /// съедал её отпускание. В приложении клавиша оставалась зажатой навсегда, снять
        /// её было нечем: физически человек её уже отпустил. Достаточно было держать ←
        /// и потянуться к ⌘.
        /// </summary>
        private readonly HashSet<int> _letThrough = new HashSet<int>();
        private bool _fnHeld;
        /// <summary>
        /// Какая физическая клавиша сейчас работает заменителем Fn, или None.
        ///
        /// Запоминаем клавишу, а не код, который она держит в Windows: код меняется
        /// под руками. Ветка ⌘+Tab снимает подставленную Win прямо посреди удержания,
        /// и запомненный код превращался в обещание нажать то, чего никто не держит:
        /// заменитель на левой ⌘ после ⌘+Tab и F-клавиши оставлял Win зажатой навсегда,
        /// причём без единой записи об этом — снять её не мог даже общий сброс.
        /// </summary>
        private ModKey _fnPhys;
        private int _subReleased;
        /// <summary>
        /// Чей код сняли МЫ, отступая под верхний ряд и навигацию, — и только его
        /// возвращаем. Снять код мог и кто-то до нас: ClearHeld и ⌘+Tab снимают Win
        /// и Alt намеренно и без возврата. Возвращая по общей записи о снятии, мы
        /// нажимали Win при открытом переключателе окон — и следующий Tab уходил
        /// как Win+Alt+Tab.
        /// </summary>
        private ModKey _subLifted;
        /// <summary>
        /// Alt, которым открыт переключатель окон. За физической клавишей он не стоит:
        /// подставленную Win мы сняли, а Alt нажали сами. Поэтому его приходится
        /// поминать отдельно везде, где спрашивают «что Windows держит сейчас».
        /// </summary>
        private bool _cmdTabAlt;

        /// <summary>
        /// Что человек держит по нашим настройкам — не глядя на то, что мы у Windows
        /// временно сняли. Этим опознаются сочетания: ⌘+Tab снимает подставленную Win
        /// на всё время переключателя окон, и вопрос «держит ли Windows» отвечал бы «нет»
        /// как раз тогда, когда человек ⌘ держит.
        /// </summary>
        private bool Means(ModKey a) { return MeansAny(a, a); }

        private bool MeansAny(ModKey a, ModKey b)
        {
            Settings s = _cfg;
            if (s == null) return false;
            foreach (ModKey phys in _modsDown)
            {
                ModKey t = s.TargetOf(phys);
                if (t == a || t == b) return true;
            }
            return false;
        }

        /// <summary>
        /// А это — что Windows держит прямо сейчас. Спрашивает один: отступает ли
        /// раскладка Apple под зажатой клавишей Windows. Важно тут именно состояние
        /// системы, а не намерение человека: подменять символ, пока Windows держит Win,
        /// нельзя — Win+E перестанет открывать проводник.
        /// </summary>
        private bool HoldsAny(int vk1, int vk2)
        {
            foreach (ModKey phys in _modsDown)
            {
                int vk = WindowsHolds(phys);
                if (vk != 0 && (vk == vk1 || vk == vk2)) return true;
            }
            return false;
        }

        private bool WinDown { get { return HoldsAny(Vk.LWin, Vk.RWin); } }

        /// <summary>
        /// Держат ли ⌘ так, что она и правда ⌘. Множество, а не флаг: зажать обе
        /// и отпустить одну — обычное дело, а флаг при этом гас, и весь слой аккордов
        /// вместе с ⌘+Tab переставал узнаваться до следующего нажатия. Стороны ⇧
        /// и control здесь давно различают — эту клавишу забыли.
        /// </summary>
        private bool CmdHeld { get { return MeansAny(ModKey.LWin, ModKey.RWin); } }

        private bool CtrlHeld { get { return MeansAny(ModKey.LCtrl, ModKey.RCtrl); } }
        private bool AltHeld { get { return MeansAny(ModKey.LAlt, ModKey.RAlt); } }
        private bool AltRightHeld { get { return Means(ModKey.RAlt); } }

        /// <summary>
        /// Держит ли человек ⇧ — своей клавишей или переназначенной. Раскладке нужен
        /// именно этот ответ, а не «что видит Windows»: символ выбираем мы сами
        /// и отправляем его мимо системной раскладки, через Input.Text.
        /// </summary>
        private bool ShiftHeld { get { return MeansAny(ModKey.LShift, ModKey.RShift); } }
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
            // Снимок кладём рядом и просим поток хука забрать его самому. Только так
            // «отпустить старое» и «начать слушаться нового» становятся одним шагом:
            // порядок двух присваиваний с этого потока окна не закрывает ничего — между
            // ними всё равно проходят нажатия, и разбираются они то новыми настройками
            // против старого состояния, то наоборот.
            Settings shot = s == null ? new Settings() : s.Snapshot();
            _pendingCfg = shot;

            uint id = _threadId;
            if (id == 0 || !Native.PostThreadMessageW(id, WmApply, IntPtr.Zero, IntPtr.Zero))
            {
                // Потока нет — применяем сами, здесь и сейчас.
                if (_threadAlive)
                    Diag.Log("настройки применены мимо потока перехвата: он ещё доживает выход");
                // По новым настройкам, как и на пути через поток: иначе перечёт судит
                // о зажатом по прежним назначениям. Держать заново не просим — сюда
                // попадают только до Start и после Stop, и держать станет некому.
                else ReleaseEverything(shot, false);
                _cfg = shot;
            }
            // У своего снимка, а не у поля: поле меняет поток хука, и здесь оно ещё
            // старое. Из-за этого переход «⌧ уводят с Num Lock» читался по прежнему
            // значению, блок оставался навигационным, а переключить его было нечем.
            EnsureNumLock(shot);
        }

        /// <summary>
        /// Если клавишу ⌧ увели с Num Lock, включить его самим: иначе цифровой блок
        /// может навсегда остаться навигационным — переключить его больше нечем.
        /// </summary>
        private static void EnsureNumLock(Settings s)
        {
            try
            {
                // И пауза, а не только выключатель. Выбрав «Только с Magic Keyboard»
                // и отключив её, человек просил не вмешиваться вовсе — а любая правка
                // настроек включала Num Lock на чужой полноразмерной клавиатуре.
                //
                // Но «клавиатуры нет» спрашиваем только после первого опроса. Оба вызова
                // при запуске стоят раньше него — опрос уходит в свой поток, — и без
                // этого условие было истинно всегда: цифровой блок оставался
                // навигационным до первой правки настроек в окне, то есть ровно то,
                // ради чего эта проверка и написана, не срабатывало ни разу.
                if (s == null || !s.Enabled) return;
                if (s.PauseWhenAppleAbsent && Devices.Scanned && !Devices.AppleConnected) return;
                string id = s.NumpadClear;
                if (String.IsNullOrEmpty(id) || id == "none" || id == "key.numlock") return;
                if ((Native.GetKeyState(Vk.NumLock) & 1) != 0) return;
                Input.Tap(Vk.NumLock);
                Diag.Log("цифровой блок был выключен — Num Lock включён");
            }
            catch { }
        }

        /// <summary>
        /// Набор клавиатур стал другим — кто бы его ни опросил. Подписка, а не ответ
        /// своего вызова: опрос зовут трое, и любой из них съедал признак у остальных.
        /// </summary>
        /// <summary>
        /// Набор клавиатур стал другим. Вместе с прочим это второй случай спросить про
        /// цифровой блок: при запуске опрос ещё не проходил, и «клавиатуры Apple нет»
        /// значило «ещё не знаем».
        /// </summary>
        private void OnDevicesChanged()
        {
            try
            {
                KeyWatch.ForgetPhysical();
                PostRelease();
                EnsureNumLock(_cfg);
                Action h = DevicesChanged;
                if (h != null) h();
            }
            catch (Exception e) { Diag.Log("смена набора клавиатур: сбой", e); }
        }

        public void Start()
        {
            if (_thread != null) return;

            _capsOn = (Native.GetKeyState(Vk.Capital) & 1) != 0;
            EnsureNumLock(_cfg);

            // Подписываемся один раз, до первого опроса.
            Devices.SetChanged += OnDevicesChanged;

            // В стороне: опрос открывает каждую клавиатуру и тянет из неё две строки,
            // а по Bluetooth это секунды. Start зовут с потока окна — окно не должно
            // ждать спящую клавиатуру, чтобы появиться.
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Devices.Rescan(); }
                catch (Exception e) { Diag.Log("первый опрос устройств не удался", e); }
            });
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

                    Devices.Rescan();   // о смене набора нам скажет Devices.SetChanged
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
            Devices.SetChanged -= OnDevicesChanged;
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
        private const uint WmApply = 0x8000 + 3;
        private const uint WmAction = 0x8000 + 4;

        /// <summary>Снимок, который поток хука должен забрать себе вместе со сбросом.</summary>
        private volatile Settings _pendingCfg;
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

                    // Отпустить здесь нужнее, чем в удачной ветке: перехвата больше нет
                    // вовсе, и отпускание подставленного кода не придёт уже никогда.
                    // Прочитав «перезапустите программу», человек шёл это делать
                    // с намертво зажатым control — которым ни щёлкнуть, ни набрать.
                    ReleaseEverything();
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

        /// <summary>
        /// Отпустить всё, что программа держит за человека. Зовут при возвращении
        /// с защищённого рабочего стола и из сна: пока на экране был Ctrl+Alt+Del
        /// или запрос прав, отпускания шли мимо перехвата, и синтетический модификатор
        /// оставался нажатым. Со стороны это выглядит как переставшая слушаться
        /// клавиатура, а причина — наша.
        /// </summary>
        public void ReleaseHeld() { PostRelease(); }

        /// <summary>
        /// Выполнить действие руками потока перехвата — с любого потока.
        ///
        /// Заведено ради клавиши ⏏: она приходит переписью клавиш, а не перехватом,
        /// и обработчик звал Actions напрямую. Мимо RunAction, то есть мимо щита:
        /// назначенный на ⏏ аккорд «Проводник (Win+E)» в конце отпускал Win — ту самую,
        /// которую человек держит своей ⌘, — и она молча переставала работать до
        /// перенажатия; печать знака под зажатым ⌥ уходила как WM_SYSCHAR, пропадала
        /// сама и открывала строку меню.
        ///
        /// Правило «убрать зажатое с дороги» живёт в одном месте, и вторая дверь
        /// обязана вести туда же. А состояние перехвата принадлежит его потоку —
        /// поэтому просьба идёт сообщением, как WmRelease и WmApply, а не вызовом.
        /// </summary>
        public void PostAction(string actionId)
        {
            if (String.IsNullOrEmpty(actionId)) return;
            lock (_asked) _asked.Enqueue(actionId);
            uint id = _threadId;
            if (id == 0 || !Native.PostThreadMessageW(id, WmAction, IntPtr.Zero, IntPtr.Zero))
            {
                // Потока нет — до Start или после Stop. Тогда и держать нечего:
                // щит убирал бы то, о чём мы всё равно ничего не знаем.
                string one = TakeAsked();
                while (one != null)
                {
                    KeyAction a = Actions.Get(one);
                    try { Actions.Begin(a, false, Settings.BrightnessStep); Actions.End(a); }
                    catch (Exception e) { Diag.Log("действие мимо потока перехвата: сбой", e); }
                    one = TakeAsked();
                }
            }
        }

        private readonly Queue<string> _asked = new Queue<string>();

        private string TakeAsked()
        {
            lock (_asked) return _asked.Count > 0 ? _asked.Dequeue() : null;
        }

        /// <summary>Попросить поток хука отпустить всё зажатое — с любого потока.</summary>
        private void PostRelease()
        {
            uint id = _threadId;
            if (id != 0)
            {
                if (!Native.PostThreadMessageW(id, WmRelease, IntPtr.Zero, IntPtr.Zero))
                    Diag.Log("просьба отпустить не дошла до потока перехвата, ошибка "
                             + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                return;
            }

            // Потока нет. Если он ещё доживает свой выход — а идентификатор снимается
            // до того, как он отпустит своё, — лезть туда со стороны нельзя: писать
            // в его состояние вдвоём и есть то, от чего заведена эта просьба.
            if (_threadAlive) return;
            ReleaseEverything();
        }

        /// <summary>Жив ли поток перехвата. Снимается позже идентификатора — см. PostRelease.</summary>
        private volatile bool _threadAlive;

        private void Run()
        {
            _threadAlive = true;
            try
            {
                _threadId = Native.GetCurrentThreadId();
                // Разбирать файлы раскладок этому потоку нельзя — только брать готовое.
                Layouts.ThisThreadTakesOnlyReady();
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
                    if (msg.hwnd == IntPtr.Zero && msg.message == WmApply)
                    {
                        // Отпустить старое и взять новое — одним шагом и на своём потоке.
                        // Отпускаем по старым настройкам (иначе снимем не тот код),
                        // а перечитываем зажатое — уже по новым: заменитель Fn, сменённый
                        // под зажатой клавишей, иначе восстанавливался по прежнему,
                        // и снять признак было нечем — отпускание уже не про ту клавишу.
                        Settings shot = _pendingCfg;
                        ReleaseEverything(shot, true);
                        if (shot != null) _cfg = shot;
                        continue;
                    }
                    if (msg.hwnd == IntPtr.Zero && msg.message == WmAction)
                    {
                        // Через тот же RunAction, что и всё остальное: щит, отпускание
                        // зажатого и возврат безобидного — одним местом на всю программу.
                        string one = TakeAsked();
                        while (one != null)
                        {
                            try { RunAction(Actions.Get(one), false); }
                            catch (Exception e) { Diag.Log("действие клавиши ⏏: сбой", e); }
                            one = TakeAsked();
                        }
                        continue;
                    }
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
                _threadAlive = false;
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
                // Дальше не пускаем вовсе, но и не глотаем. Не пускаем — потому что
                // раньше он доходил до HandleModifier и подчинялся переназначению левого
                // control: со схемой «как в macOS» каждое нажатие правого ⌥ слало в Windows
                // клавишу Windows, то есть открывало «Пуск» посреди набора символа.
                // А не глотаем — потому что глотание убивает AltGr системной раскладки,
                // и настройка, которая это делала, обещала «правый ⌥ — обычный Alt»,
                // а получалось наоборот: свой третий уровень оставался, чужой пропадал.
                return false;
            }

            // Выключатель и пауза — уже после призрака. Стояв выше, они съедали его
            // отпускание, и признак оставался стоять до перезапуска: EmitText после
            // этого «возвращал» левый control нажатым — тот, которого Windows не держит,
            // — и снять его было нечем. Заодно ⌃Space и ⌃⌘Q переставали узнаваться.
            if (!s.Enabled) return false;
            if (s.PauseWhenAppleAbsent && !Devices.AppleConnected) return false;

            ModKey phys;
            if (TryPhysical(vk, ext, out phys))
            {
                // Что нажал человек: по этому множеству опознаются сочетания и заменитель
                // Fn. Add и Remove при автоповторе безобидны: множество.
                if (down) _modsDown.Add(phys); else _modsDown.Remove(phys);
                // Считаем по тому, что реально ушло в Windows, а не по тому, что нажали.
                // Caps Lock мог быть переназначен — у тех, кто пришёл с мака, на нём
                // обычно control, — и тогда индикатор не переключается, флаг трогать
                // нельзя. Но бывает и наоборот: на Caps Lock назначили другую клавишу,
                // индикатор переключается, а нажатия самого Caps Lock не было.
                // И только первое нажатие: автоповтор Windows переключателем не считает,
                // а мы перевернули бы флаг столько раз, сколько пришло повторов.
                // Переворачиваем по тому, держит ли Capital сама Windows, а не по своему
                // множеству источников. Множество на это не отвечает: настоящая Caps Lock
                // снимает Capital за всех, сколько бы наших источников его ни «держало», —
                // и следующее её нажатие переворачивало лампочку, а наш счёт нет. Заодно
                // это само собой закрывает автоповтор: он приходит, когда Capital уже
                // держат, и переключателем Windows его не считает.
                if (down && s.TargetOf(phys) == ModKey.CapsLock && !Held(Vk.Capital))
                    _capsOn = !_capsOn;
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
            // Спрашивать «а есть ли у него навигационная пара» здесь незачем: в это
            // множество попадают только коды, у которых она есть, — обе записи стоят
            // за той же проверкой.
            if (_navActive.Contains(vk)) return HandleNav(vk, FnNavTarget(vk), down);

            // Продолжение начатого аккорда: и повтор, и отпускание идут туда же, куда
            // ушло нажатие, — не спрашивая, какие модификаторы зажаты сейчас.
            MacShortcut taken;
            if (_chordTaken.TryGetValue(vk, out taken))
            {
                if (!down) { _chordTaken.Remove(vk); return true; }
                // Повторяем только то, что объявило о себе, что умеет повторяться:
                // ⌘Q, зажатая на полсекунды, не должна закрывать окно за окном.
                // ⌘+Tab листает дальше — но только пока переключатель открыт. Закрыть
                // его мог кто угодно из тех, кому пришлось снять наш Alt (аккорд macOS,
                // символ раскладки), и повторы Tab после этого сыпались обычными
                // табуляциями в окно, которое только что получило фокус.
                if (taken == CmdTab) { if (_cmdTabAlt) Input.Tap(Vk.Tab); }
                else if (taken != null && taken.Repeats) MacSend(taken);
                return true;
            }

            // ⌘+Tab ведёт себя как Alt+Tab.
            if (s.CmdTabSwitchesWindows && vk == Vk.Tab && CmdHeld && !Busy(vk, k.scanCode))
            {
                if (!down) return false;   // нажатия не брали — и отпускание не наше
                _chordTaken[vk] = CmdTab;
                {
                    if (!_cmdTabAlt)
                    {
                        // Win придётся отпустить, а Windows считает нажатие одиночным,
                        // если между нажатием и отпусканием ничего не было, и открывает
                        // «Пуск» — который вдобавок заберёт фокус у переключателя окон.
                        // Подсовываем незанятый код: тот же приём, что в MacSend.
                        Input.Tap(VkNoop);
                        // Снимаем по тому, что клавиша держит в Windows, а не по её
                        // имени. _injected ключуется физической клавишей: со схемой
                        // «Как в Windows» ⌘ приходит клавишей ⌥, запись лежала под
                        // ключом левого ⌥, и оба снятия по имени промахивались —
                        // в Windows уходило Win+Alt+Tab вместо переключателя окон.
                        ReleaseWindowsKey();
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
                // Control и ⇧ спрашиваем у множеств «что видит Windows», а не у флагов
                // физических клавиш. У пришедших с мака control обычно висит на Caps Lock,
                // и по флагам он не считался вовсе: ⌃Space не работал совсем, ⌃⌘Q не
                // опознавалось, а ⌘← давало Ctrl+Home вместо Home — прыжок в начало
                // документа вместо начала строки.
                if (CmdHeld) mm |= MacMod.Cmd;
                if (AltHeld) mm |= MacMod.Opt;
                if (ShiftHeld) mm |= MacMod.Shift;
                if (CtrlHeld && !_phantomCtrl) mm |= MacMod.Ctrl;

                // Пробел разбирается отдельно: у ⌘Space и ⌃Space роль задаёт человек.
                if (vk == Vk.Space && (mm == MacMod.Cmd || mm == MacMod.Ctrl))
                {
                    MacShortcut space = MacKeys.SpaceAction(mm == MacMod.Cmd ? s.CmdSpace : s.CtrlSpace);
                    if (space != null && down && !Busy(vk, k.scanCode))
                    {
                        MacSend(space);
                        _chordTaken[vk] = space;
                        return true;
                    }
                }

                if (mm != MacMod.None && !Busy(vk, k.scanCode))
                {
                    MacShortcut sc = MacKeys.Find(vk, mm);
                    // Выключенное поимённо остаётся выключенным: подставлять вместо него
                    // общее правило значило бы не слушаться человека.
                    bool off = sc != null && !s.MacEnabled(sc.Id);
                    if (sc == null) sc = MacKeys.Generic(vk, mm);
                    if (sc != null && !off)
                    {
                        // Аккорд отправляем один раз на удержание. Автоповтор приходит
                        // теми же нажатиями, а на маке такие сочетания не повторяются:
                        // задержал ⌘Q на полсекунды — и Alt+F4 уходит три десятка раз,
                        // закрывая окно за окном. Движения по тексту, наоборот, повторять
                        // надо, и они это про себя объявляют сами.
                        // Отпускание сюда доходит: после паузы, после снятого Windows
                        // перехвата, после общего сброса — везде, где нажатие прошло
                        // мимо нас. Оно не наше, и брать его нельзя: взяв, мы съели бы
                        // отпускание клавиши, нажатие которой ушло в приложение.
                        if (!down) return false;
                        // Сюда доходит только первое нажатие: повторы разбирает продолжение
                        // выше, и по нему же решается, повторять ли посылку.
                        MacSend(sc);
                        _chordTaken[vk] = sc;
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
            if (down && _fnHeld && s.FnNavigation && !Busy(vk, k.scanCode))
            {
                int target = FnNavTarget(vk);
                if (target != 0) return HandleNav(vk, target, down);
            }

            if (vk >= Vk.F1 && vk <= Vk.F24)
                return HandleFunctionKey(s, vk - Vk.F1, vk, down);

            // Цифровой блок Apple: ⌧ приходит как Num Lock и невзначай выключает блок,
            // а «=» шлёт VK_CLEAR со скан-кодом 0x59, который Windows просто игнорирует.
            if (vk == Vk.NumLock) { if (HandleSingle(s, vk, s.NumpadClear, down)) return true; }
            // «=» печатает «=». Второго осмысленного ответа нет: Windows не понимает
            // того, что эта клавиша шлёт на самом деле, и настройка существовала только
            // потому, что механизм внутри общий с ⌧.
            else if (k.scanCode == 0x59 && vk == Vk.Clear) { if (HandleSingle(s, vk, "text.equals", down)) return true; }

            if (s.AppleLayoutEnabled)
            {
                int r = HandleLayout(s, k, down);
                if (r >= 0) return true;
            }

            // Перестановка не в else: HandleLayout отступает в восьми случаях — для языка
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
            // И повтор туда же. Без этого автоповтор уходил в приложение настоящим,
            // непереставленным скан-кодом — то есть подмена бросала работу на середине
            // удержания, — а отпускание снимало подставленный, и настоящий оставался
            // зажатым.
            if (down && _isoSwapped.Contains(k.scanCode))
            {
                Input.Scan((ushort)(k.scanCode == 0x29 ? 0x56 : 0x29), false, true);
                return true;
            }
            // Без настройки: это не вкус, а исправление аппаратной особенности —
            // две клавиши на ISO-клавиатурах Apple подключены наоборот, и то же самое
            // безусловно правит квирк APPLE_ISO_TILDE_QUIRK в Linux. Желания оставить
            // клавиши перепутанными не бывает.
            // Скан-код спрашиваем первым: Physical идёт в Devices.AppleModel, а там замок.
            // Брать его в обработчике перехвата на каждое нажатие ради двух клавиш из ста
            // незачем.
            if (down && (k.scanCode == 0x29 || k.scanCode == 0x56)
                && !Busy(vk, k.scanCode) && Physical(s) == PhysLayout.Iso)
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
                if (!down) return false;   // нажатия не брали — и отпускание не наше
                if (WindowsHoldsMenuKey()) Input.Tap(VkNoop);
                _chordTaken[vk] = null;
                return true;
            }

            // Клавиша уходит в приложение своим ходом — и до отпускания она его.
            // Записываем это единственный раз, в самом конце: сюда доходит ровно то,
            // от чего отказались все слои.
            if (down) _letThrough.Add(vk); else _letThrough.Remove(vk);
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

        private IntPtr _layHkl;
        private Settings _layFor;
        private AppleLayoutFile _layFile;

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

        /// <summary>
        /// Раскладка Apple для языка окна, которое сейчас впереди.
        ///
        /// Держится вместе с HKL те же 150 мс: LayoutFor при непривязанном языке идёт
        /// подбирать раскладку через CultureInfo, а тот на незнакомом языке бросает
        /// исключение — на каждое нажатие, в потоке с бюджетом в триста миллисекунд.
        /// </summary>
        private AppleLayoutFile CurrentLayout(Settings s)
        {
            IntPtr hkl = ForegroundLayout();
            if (hkl == _layHkl && _layFor == s) return _layFile;
            AppleLayoutFile file = Layouts.ById(s.LayoutFor((int)(hkl.ToInt64() & 0xFFFF)));
            // «Раскладки ещё нет» до конца прогрева не запоминаем. Поток перехвата берёт
            // только готовое, а прогрев идёт в своём потоке: первое же нажатие в первые
            // миллисекунды после запуска запоминало «нет» — и раскладки Apple молча
            // не работали до смены языка окна.
            if (file == null && !Layouts.Ready) return null;
            _layHkl = hkl;
            _layFor = s;
            _layFile = file;
            return file;
        }

        /// <summary>
        /// Скан-код, которого ждёт Windows. Отличается от присланного ровно на двух
        /// клавишах ISO-клавиатур Apple — тех, что подключены наоборот.
        /// </summary>
        private uint LogicalScan(Settings s, uint scan)
        {
            // Скан-код спрашиваем первым: Physical идёт в Devices.AppleModel, а там замок.
            if (scan != 0x29 && scan != 0x56) return scan;
            if (Physical(s) != PhysLayout.Iso) return scan;
            return scan == 0x29 ? 0x56u : 0x29u;
        }

        /// <summary>Выпустить висящий знак ударения, пока для него ещё есть раскладка.</summary>
        private void DropDead(Settings s)
        {
            if (_deadPrefix == null) return;
            AppleLayoutFile lay = CurrentLayout(s);
            if (lay != null) FlushDead(lay, false);
            else _deadPrefix = null;
        }

        /// <summary>-1 — не наше дело; 1 — проглотить. Третьего ответа у неё нет.</summary>
        private int HandleLayout(Settings s, Native.KBDLLHOOKSTRUCT k, bool down)
        {
            // Расширенные клавиши сюда не пускаем. Разбор идёт по голому скан-коду,
            // а «/» цифрового блока приходит тем же 0x35, что и «/» основного: на
            // французской раскладке нажатие на блоке печатало «=». Заодно два разных
            // нажатия делили одну запись в _swallowed.
            if ((k.flags & Native.LLKHF_EXTENDED) != 0) return -1;

            // Отпускание съеденного снимается всегда и раньше всего: куда ушло нажатие,
            // туда же обязано уйти отпускание. Раньше два выхода ниже — «раскладки для
            // языка нет» и «Alt не как третий уровень» — уходили мимо этой строки,
            // и скан-код оставался в списке. Следующее нажатие той же клавиши шло
            // в систему своим ходом, а её отпускание мы съедали как своё: клавиша
            // оставалась зажатой в приложении.
            if (!down && _swallowed.Remove(k.scanCode)) return 1;

            // Клавишу, которую мы уже держим, обратно не отдаём. Восемь отступлений
            // ниже — зажатый control или Win, пропавшая раскладка, клавиши нет в таблице,
            // Alt не третьим уровнем, отпускание, клавишу держит другой слой, пустой
            // уровень, «не отличается от Microsoft» — на автоповторе
            // возвращали нажатие в систему настоящим, а отпускание съедала строка выше:
            // в приложении клавиша оставалась зажатой навсегда. Повтор при этом глотаем
            // молча: потерять несколько знаков автоповтора в редком случае дешевле.
            bool ours = down && _swallowed.Contains(k.scanCode);
            int verdict = LayoutDecide(s, k, down);
            return verdict < 0 && ours ? 1 : verdict;
        }

        /// <summary>-1 — не наше дело; 1 — проглотить.</summary>
        private int LayoutDecide(Settings s, Native.KBDLLHOOKSTRUCT k, bool down)
        {
            if (CtrlHeld || WinDown)
            {
                if (down) DropDead(s);
                return -1;
            }

            AppleLayoutFile lay = CurrentLayout(s);
            if (lay == null)
            {
                // Выпустить знак ударения некуда — раскладки нет. Забываем, иначе
                // он всплывёт получасом позже поверх чужой буквы.
                if (down) _deadPrefix = null;
                return -1;
            }

            // Таблица раскладки составлена по тем скан-кодам, которых ждёт Windows:
            // 0x29 — клавиша слева от «1» (E00), 0x56 — левая нижняя ISO (B00).
            // ISO-клавиатура Apple шлёт их наоборот — ту самую аппаратную особенность,
            // которую правит перестановка ниже. Спрашивать таблицу сырым скан-кодом
            // значит взять строку соседней клавиши: слева от «1» печаталось «§».
            LayoutKey key = lay.Key((int)LogicalScan(s, k.scanCode));
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
                return -1;
            }

            bool optWanted;
            switch (s.OptLevel)
            {
                case OptLevel.AnyOption: optWanted = AltHeld; break;
                // Если правый ⌥ объявлен обычным Alt, третьего уровня на нём быть
                // не может — иначе настройка обещает одно, а делает противоположное:
                // свой третий уровень остаётся, а пропадает системный AltGr.
                case OptLevel.RightOption: optWanted = AltRightHeld; break;
                default: optWanted = false; break;
            }
            // Alt, который не просили считать третьим уровнем, оставляем меню.
            if (AltHeld && !optWanted)
            {
                // Alt+Tab — тоже этот путь: знак ударения выпускаем в то окно, где его
                // набрали, а не в то, куда сейчас уйдут.
                if (down && _deadPrefix != null) FlushDead(lay, false);
                return -1;
            }

            if (!down) return -1;

            // Клавишу, которую уже держит другой слой, не берём. Прямое направление
            // раньше не спрашивало ничего: перестановка ISO брала нажатие, а автоповтор
            // при нажатом ⇧ отбирала раскладка — и подставленный скан-код оставался
            // зажатым навсегда, потому что снять его было уже некому.
            if (!_swallowed.Contains(k.scanCode) && Busy((int)k.vkCode, k.scanCode)) return -1;

            string text = key.Text(ShiftHeld, optWanted);
            if (text == null)
            {
                // Строка в таблице есть, но на этом уровне пусто — в CLDR это обычное
                // дело. Для висящего знака ударения случай тот же, что и «клавиши нет
                // в таблице»: выпустить его надо здесь, иначе он всплывёт получасом
                // позже поверх чужой буквы.
                if (_deadPrefix != null) FlushDead(lay, false);
                return -1;
            }
            bool dead = key.Dead(ShiftHeld, optWanted);

            if (_capsOn && text.Length == 1 && Char.IsLetter(text[0]))
                text = ShiftHeld ? text.ToLowerInvariant() : text.ToUpperInvariant();

            // Обычно подменяем только то, что отличается от раскладки Microsoft для этого языка:
            // остальные нажатия пусть идут своим ходом, без синтетического ввода.
            if (_deadPrefix == null && !key.Differs(ShiftHeld, optWanted)) return -1;

            _swallowed.Add(k.scanCode);

            if (dead)
            {
                if (_deadPrefix != null) EmitText(_deadPrefix);
                _deadPrefix = text;
                return 1;
            }

            if (_deadPrefix != null)
            {
                string composed = lay.Compose(_deadPrefix, text);
                string result = composed != null ? composed : _deadPrefix + text;
                _deadPrefix = null;
                EmitText(result);
                return 1;
            }

            EmitText(text);
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
                EmitText(composed != null ? composed : prefix);
            }
            else EmitText(prefix);
        }

        /// <summary>
        /// Отправить символ и уберечь его от зажатого Alt.
        ///
        /// Вопрос здесь один: держит ли Alt Windows прямо сейчас. Прежде спрашивали
        /// другое — просил ли человек считать ⌥ третьим уровнем, — и ответ передавали
        /// параметром. FlushDead передавала «нет» всегда, а зовут её как раз при
        /// зажатых Alt и Ctrl: знак ударения уходил под Alt, то есть приходил
        /// как WM_SYSCHAR — пропадал сам и открывал строку меню.
        /// </summary>
        private void EmitText(string text)
        {
            var released = new List<int>();
            // Снимаем то, что Windows держит как Alt, — от любой зажатой клавиши,
            // а не только от своих двух.
            foreach (ModKey phys in new List<ModKey>(_modsDown))
                if (IsMenuKeyAlt(WindowsHolds(phys))) Release(phys, released);
            // И Alt переключателя окон: за физической клавишей он не стоит, а символ
            // под ним уходит как WM_SYSCHAR — пропадает сам и открывает строку меню.
            // Снимаем без возврата, как и MacSend: отпустить этот Alt — и значит закрыть
            // переключатель, а повторное нажатие его не откроет. Переключатель закроется
            // и переключит окно; вернуть Alt нажатым — только оставить его висеть.
            if (_cmdTabAlt)
            {
                Input.Tap(VkNoop);
                Input.Key(Vk.LMenu, false);
                _cmdTabAlt = false;
            }
            // Призрачный Ctrl от AltGr в _modsDown не попадает: его отсекают раньше.
            if (_phantomCtrl) { Input.Key(Vk.LControl, false); released.Add(Vk.LControl); }
            Input.Text(text);
            for (int i = released.Count - 1; i >= 0; i--)
            {
                Input.Key(released[i], true);
                // Щит с обеих сторон. Между нашим возвратом и настоящим отпусканием ⌥
                // человеком не происходит ничего, и Windows считает это одиночным Alt —
                // то есть строкой меню поверх набора. Снятие щит уже ставило, возврат нет.
                if (IsMenuKey(released[i])) Input.Tap(VkNoop);
            }
        }

        private void Release(ModKey phys, List<int> released)
        {
            // Тот же вопрос, что у аккорда: не «какая клавиша нажата», а «что она держит
            // в Windows». Прежний ответ не знал про взятые нажатия, и «выключенная»
            // клавиша возвращалась нажатой — а снять её было уже нечем.
            int vk = WindowsHolds(phys);
            if (vk == 0) return;
            // Alt, отпущенный и нажатый вхолостую, открывает строку меню — тот же щит,
            // что и везде. Без него на каждом ⌥-символе дважды всплывали подсказки клавиш.
            if (IsMenuKey(vk)) Input.Tap(VkNoop);
            Input.Key(vk, false);
            released.Add(vk);
        }

        /// <summary>
        /// Какой код эта физическая клавиша прямо сейчас держит нажатым в Windows,
        /// или 0, если не держит ничего.
        ///
        /// Четыре случая, и путать их нельзя. Мы сняли код на время — не держит ничего,
        /// и этот случай впереди всех: снять можно и то, что идёт насквозь.
        /// Мы подставили замену — держит замена.
        /// Мы взяли нажатие себе и ничего не послали («выключить клавишу») — не держит
        /// ничего, и трогать её нельзя: отпускание уйдёт в пустоту, а нажатие подставит
        /// клавишу, которой человек не нажимал, — и снять её потом будет нечем.
        /// Мы нажатия не брали — держит сама себя.
        /// </summary>
        private int WindowsHolds(ModKey phys)
        {
            int vk;
            if (_modLifted.ContainsKey(phys)) return 0;
            if (_injected.TryGetValue(phys, out vk)) return vk;
            if (_modTaken.Contains(phys)) return 0;
            return ModNames.VirtualKey(phys);
        }

        /// <summary>Держит ли Windows сейчас клавишу Windows или Alt — от любой из наших.</summary>
        private bool WindowsHoldsMenuKey()
        {
            // Alt переключателя окон — тоже наш, хоть и не стоит ни за какой физической
            // клавишей: ReleaseWindowsKey стёр запись, и перебором _modsDown его не найти.
            if (_cmdTabAlt) return true;
            foreach (ModKey phys in _modsDown)
                if (IsMenuKey(WindowsHolds(phys))) return true;
            return false;
        }

        // ------------------------------------------------------------------
        //  Модификаторы, F-клавиши, навигация
        // ------------------------------------------------------------------

        private bool HandleModifier(Settings s, ModKey phys, bool down)
        {
            ModKey target = s.TargetOf(phys);

            // Переключатель окон закрываем, когда отпущена последняя ⌘, — не спрашивая,
            // какую клавишу отпустили. Спрашивали: условие стояло на именах клавиш Win,
            // а ⌘ живёт там, куда её положил человек. После схемы «Как в Windows» ⌘
            // приходит клавишей ⌥, условие не срабатывало, и подставленный Alt оставался
            // зажатым в Windows навсегда — снять его было нечем, о нём не знало ни одно
            // из наших множеств.
            // Обе ⌘, а не любая: отпустив вторую при открытом переключателе окон,
            // человек закрывал его и переключался на подсвеченное окно, продолжая
            // держать первую.
            if (!down && _cmdTabAlt && !CmdHeld)
            {
                Input.Key(Vk.LMenu, false);
                _cmdTabAlt = false;
            }

            // Заменитель Fn отслеживаем по физической клавише, каким бы ни было её назначение.
            if (s.FnSubstitute != ModKey.None && phys == s.FnSubstitute)
            {
                _fnHeld = down;
                // Запоминаем не всякое назначение, а только модификатор: снимать на время
                // навигации нужно именно его, иначе Fn+← превратится в ⌥+Home. Всё
                // остальное снимать нечего и нельзя. Caps Lock снять и вернуть — значит
                // зажечь лампочку, и заменитель на нём переключал регистр через нажатие;
                // Escape вернулся бы вторым нажатием и закрыл бы диалог.
                int mod = down && target != ModKey.None ? ModNames.VirtualKey(target) : 0;
                _fnPhys = IsModifierKey(mod) ? phys : ModKey.None;
                // Счётчик не обнуляем: он парный, и его держат начатая навигация
                // и верхний ряд. Обнулив его на отпускании заменителя, мы возвращали
                // Alt нажатым, пока вторая стрелка ещё работает навигационной.
                if (!down && _navActive.Count == 0 && !AnyFKeyDown) _subReleased = 0;

                // Заменитель перенажали, не отпуская клавишу, ради которой его сняли.
                // Нажатие ушло бы в Windows и вернуло модификатор под зажатой F4 —
                // то есть Alt+F4, закрытие окна; на навигации — Ctrl+Page Up вместо
                // Page Up. Пока снятие в силе, нажатие заменителя не пропускаем и берём
                // его себе: иначе отпускание уйдёт в Windows без пары.
                //
                // И записываем снятие заново. Отпустив клавишу и нажав её снова, человек
                // держит её и дальше — а без записи возвращать по концу удержания стало
                // бы нечего, и зажатый ⌥ молча переставал бы работать до перенажатия.
                if (down && _subReleased > 0 && _fnPhys != ModKey.None)
                {
                    _modTaken.Add(phys);
                    // Что mod — модификатор, уже сказано: _fnPhys выставлен именно
                    // по этому вопросу, а он проверен строкой выше.
                    if (!_modLifted.ContainsKey(phys)) { _modLifted[phys] = mod; _subLifted = phys; }
                    return true;
                }
            }

            // Клавиши Win берём под контроль и без переназначения, если включён ⌘+Tab:
            // иначе настоящее нажатие Win уже ушло в систему и отменить его нечем.
            // Взятое нажатие делает клавишу нашей и на отпускании, каким бы ни было
            // её назначение: заменитель Fn мы берём и у клавиши, которую не разбираем.
            bool managed = target != phys
                        || (s.CmdTabSwitchesWindows && (phys == ModKey.LWin || phys == ModKey.RWin))
                        || (!down && _modTaken.Contains(phys));

            // Клавишу отпустили — «снято на время» больше не про неё: возвращать некуда,
            // следующее нажатие придёт заново. Сброс стоит до проверки managed нарочно:
            // снимаем мы коды и у клавиш, идущих насквозь, а такие уходят из метода
            // строкой ниже.
            bool lifted = !down && _modLifted.ContainsKey(phys);
            if (lifted) _modLifted.Remove(phys);

            if (!managed) return false;

            // Отпускание всегда снимает то, что ушло в Windows на нажатии, а не то,
            // что назначено сейчас. Назначение можно сменить, пока клавишу держат —
            // и тогда нынешний код уходил вхолостую, а настоящий оставался нажатым.
            // Снять его после этого было нечем: физическую клавишу человек уже отпустил.
            if (!down)
            {
                // Отпускание клавиши, нажатия которой мы не брали, — не наше дело.
                // Раньше здесь глотали всегда: достаточно было сменить назначение,
                // пока клавишу держат, — настоящее нажатие уже ушло в Windows, а её
                // отпускание мы съедали, и клавиша оставалась зажатой навсегда.
                if (!_modTaken.Remove(phys)) return false;

                int had;
                if (_injected.TryGetValue(phys, out had))
                {
                    _injected.Remove(phys);
                    // Код уже снят с Windows — снимать второй раз значит слать отпускание
                    // без пары. Запись при этом убрать надо: держать больше нечего.
                    if (lifted) return true;
                    // Тот же код может держать и вторая клавиша: назначить две клавиши
                    // на одно и то же окно позволяет. Отпустив его, пока вторую держат,
                    // мы снимали ⇧ посреди набора, и вернуть его было нечем.
                    //
                    // Спрашиваем WindowsHolds, а не запись: _injected значит «мы посылали
                    // нажатие», а держит ли Windows этот код сейчас, знает только он.
                    // У второй клавиши код мог быть снят аккордом — и она считалась
                    // держащей снятое, а первая залипала.
                    bool other = false;
                    foreach (KeyValuePair<ModKey, int> p in _injected)
                        if (WindowsHolds(p.Key) == had) other = true;
                    if (!other) Input.Key(had, false);
                }
                return true;
            }

            _modTaken.Add(phys);

            if (target == ModKey.None) return true;

            int tvk = ModNames.VirtualKey(target);
            if (tvk == 0) return true;

            // Переключатель окон уже открыт, и Win для него снята нарочно. Вторая ⌘
            // нажала бы её снова: следующий Tab ушёл бы как Win+Alt+Tab, а отпускание
            // выкинуло бы «Пуск» поверх переключателя. Нажатие всё равно считаем взятым
            // (_modTaken выше), так что отпускание никуда не денется.
            if (_cmdTabAlt && (tvk == Vk.LWin || tvk == Vk.RWin)) return true;

            Input.Key(tvk, true);
            _injected[phys] = tvk;
            // И код больше не снят: мы только что нажали его заново. Без этой строки
            // WindowsHolds отвечал «не держит ничего» про клавишу, которую мы держим,
            // и её отпускание уходило по ветке «уже снято» — то есть не уходило вовсе.
            // Достаточно было заводского ⌘C и полсекунды удержания ⌘: автоповтор жал
            // Win заново, а отпускание её не снимало. Дальше каждая буква — Win+буква:
            // Win+L запирает экран, Win+E открывает проводник.
            _modLifted.Remove(phys);
            if (_subLifted == phys) _subLifted = ModKey.None;
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
        /// Код, который заменитель Fn держит в Windows, — а если мы его уже сняли,
        /// то тот, что снят и будет возвращён. Ноль — заменитель ни за чем не стоит.
        /// </summary>
        private int FnHoldsVk
        {
            get
            {
                if (_fnPhys == ModKey.None) return 0;
                // Уже снятый код спрашиваем у записи о снятии: WindowsHolds про него
                // отвечает «ничего», и это правда — но возвращать-то надо именно его.
                int lifted;
                if (_modLifted.TryGetValue(_fnPhys, out lifted)) return lifted;
                return WindowsHolds(_fnPhys);
            }
        }

        /// <summary>Держит ли слой верхнего ряда хоть одну клавишу.</summary>
        private bool AnyFKeyDown
        {
            get
            {
                for (int i = 0; i < Settings.MaxFKeys; i++) if (_fkeyDown[i]) return true;
                return false;
            }
        }

        /// <summary>Это Alt — левый или правый.</summary>
        private static bool IsMenuKeyAlt(int vk) { return vk == Vk.LMenu || vk == Vk.RMenu; }

        /// <summary>Клавиша, которая меняет смысл других, пока её держат.</summary>
        private static bool IsModifierKey(int vk)
        {
            return vk == Vk.LControl || vk == Vk.RControl
                || vk == Vk.LShift || vk == Vk.RShift
                || vk == Vk.LMenu || vk == Vk.RMenu
                || vk == Vk.LWin || vk == Vk.RWin;
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
            // Спрашиваем «что Windows держит СЕЙЧАС», а не «что эта клавиша должна
            // держать». Код мог быть снят и до нас — ClearHeld и ⌘+Tab снимают Win
            // и Alt намеренно и без возврата. Снять их вторым разом значит послать
            // отпускание без пары, а вернуть потом — нажать Win при открытом
            // переключателе окон: следующий Tab уходил как Win+Alt+Tab.
            int vk = _fnPhys == ModKey.None ? 0 : WindowsHolds(_fnPhys);
            if (_subReleased++ == 0 && vk != 0)
            {
                if (IsMenuKey(vk)) Input.Tap(VkNoop);
                Input.Key(vk, false);
                // И записываем снятие. Ответ на вопрос «что Windows держит СЕЙЧАС» всё
                // время навигации был неправдой: аккорд, нажатый поверх начатой навигации,
                // «возвращал» снятый control — и следующая стрелка уходила Ctrl+Page Up,
                // то есть на соседнюю вкладку. Записываем именно снятие, а не убираем
                // запись из _injected: у заводского заменителя её там и не было.
                _modLifted[_fnPhys] = vk;
                _subLifted = _fnPhys;
            }
        }

        private void SubstituteRestore()
        {
            if (--_subReleased > 0) return;
            _subReleased = 0;
            ModKey phys = _subLifted;
            _subLifted = ModKey.None;
            if (phys == ModKey.None) return;   // снимали не мы — и возвращать не нам

            // Клавишу могли отпустить, пока мы держали её код снятым: её отпускание
            // убирает запись о снятии, и возвращать тогда нечего.
            int vk;
            if (!_modLifted.TryGetValue(phys, out vk)) return;
            _modLifted.Remove(phys);

            Input.Key(vk, true);
            // Нажатие теперь наше — и отпускать его нам. У разбираемой клавиши запись
            // об этом и есть _injected; у неразбираемой код совпадает с её собственным,
            // и её настоящее отпускание снимет его само.
            if (_modTaken.Contains(phys)) _injected[phys] = vk;
            if (IsMenuKey(vk)) Input.Tap(VkNoop);
        }

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
                    RunAction(Actions.Get(id), false);
                    return true;
                }
                // Автоповтор: то же действие, но повтором — аккорды и запуск программ
                // на нём не срабатывают, иначе удержание плодило бы окна калькулятора.
                RunAction(Actions.Get(id), true);
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
            // Без выключателя: не уступать, когда драйвер ряд забирает, — значит
            // переназначать одно нажатие дважды. Правильное значение здесь одно.
            return s != null && index < 12
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
            // Ветку защёлкиваем на первом нажатии, чем бы оно ни кончилось. Иначе
            // достаточно было сменить настройки под удержанием: первые нажатия ушли
            // насквозь, ветка сменилась, и отпускание съедал уже наш слой — а клавиша
            // оставалась зажатой в приложении.
            if (down && !_fkeyLatched[index])
            {
                _fkeyLatched[index] = true;
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

                // Настоящую F-клавишу мы берём себе, только если её вызвали заменителем
                // Fn: иначе снимать с него нечего, а сама клавиша и так дойдёт куда надо.
                _fkeyTake[index] = _fkeyMedia[index] || (_fnHeld && FnHoldsVk != 0);
            }
            else if (!down)
            {
                // Отпускание клавиши, нажатия которой мы не видели, — не наше дело.
                if (!_fkeyLatched[index]) return false;
                // Защёлку снимаем здесь, до всех отступлений ниже: иначе ветка,
                // ушедшая в приложение, оставалась бы выбранной навсегда.
                _fkeyLatched[index] = false;
            }

            if (_fkeyYield[index]) return false;

            if (!_fkeyMedia[index])
            {
                // Нужна настоящая F-клавиша. Если её вызвали заменителем Fn, снимаем
                // с него модификатор, иначе получится Alt+F4 вместо F4. Решение это
                // защёлкнуто на первом нажатии: не взяли — значит клавиша ушла
                // в приложение своим ходом, и отпускание обязано уйти туда же.
                if (!_fkeyTake[index]) return false;
                if (down)
                {
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
                RunAction(a, repeat);
            }
            else
            {
                _fkeyDown[index] = false;
                _fkeyAction[index] = null;
                Actions.End(a);
            }
            return true;
        }

        /// <summary>
        /// Держит ли эту клавишу какой-нибудь слой.
        ///
        /// Слоёв, умеющих взять нажатие себе, шесть, и каждый помнит взятое по-своему:
        /// навигация — по коду клавиши, раскладка и перестановка ISO — по скан-коду,
        /// верхний ряд — по номеру. Седьмой хозяин — само приложение: клавиша, чьё
        /// нажатие ушло насквозь, принадлежит ему до отпускания.
        /// Пока нажатие держит один, второму брать его нельзя:
        /// иначе на отпускании сработает тот, чья проверка стоит выше, а запись второго
        /// останется навсегда — и следующее нажатие той же клавиши уйдёт в приложение,
        /// а её отпускание съедим мы. Клавиша останется зажатой, и снять её будет нечем.
        /// </summary>
        private bool Busy(int vk, uint scan)
        {
            return _letThrough.Contains(vk)
                || _navActive.Contains(vk)
                || _chordTaken.ContainsKey(vk)
                || _swallowed.Contains(scan)
                || _isoSwapped.Contains(scan)
                || _singleAction.ContainsKey(vk)
                // Верхний ряд спрашиваем по защёлке, а не по «мы держим». Клавиша,
                // ушедшая в приложение настоящей F-клавишей, всё равно наша до конца
                // нажатия: отдав её посреди удержания слою аккордов, мы съели бы
                // отпускание, и в приложении она осталась бы зажатой.
                || (vk >= Vk.F1 && vk <= Vk.F24 && _fkeyLatched[vk - Vk.F1]);
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

        /// <summary>
        /// Отпустить всё, что Windows держит клавишей Windows, — какой бы физической
        /// клавишей её ни нажали. Забыть о снятом обязательно: иначе отпускание уходит
        /// вторым разом — при настоящем отпускании клавиши и потом ещё раз при общем
        /// сбросе, — а WindowsHolds продолжает уверять, что клавиша зажата.
        /// </summary>
        private void ReleaseWindowsKey()
        {
            foreach (KeyValuePair<ModKey, int> pair in new List<KeyValuePair<ModKey, int>>(_injected))
                if (pair.Value == Vk.LWin || pair.Value == Vk.RWin)
                {
                    // Уже снятое не снимаем: запись в _injected при снятии остаётся,
                    // и правду про «держит ли Windows» говорит только WindowsHolds.
                    if (!_modLifted.ContainsKey(pair.Key)) Input.Key(pair.Value, false);
                    _injected.Remove(pair.Key);
                    // И забываем снятие тоже. Win мы сняли здесь без возврата — а по этой
                    // записи её возвращал SubstituteRestore, нажимая Win при открытом
                    // переключателе окон: следующий Tab уходил как Win+Alt+Tab.
                    _modLifted.Remove(pair.Key);
                    if (_subLifted == pair.Key) _subLifted = ModKey.None;
                }
        }

        // Незанятый виртуальный код: ни одна клавиша его не выдаёт, ни одно сочетание
        // на нём не висит. Нужен как безобидная «затычка» — см. ниже.
        private const int VkNoop = 0xE8;

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
        private void MacSend(MacShortcut sc)
        {
            // Снимаем то, что Windows держит СЕЙЧАС, а не то, какие физические клавиши
            // нажаты. Разница ровно в том случае, ради которого всё это заведено:
            // у пришедших с мака control обычно висит на Caps Lock, и по физическим
            // флагам его не видно. ⌃Space уходил как Ctrl+Win+Space и язык не переключал,
            // ⌃⌘Q — как Ctrl+Win+L и экран не блокировал; а «выключенная» клавиша,
            // наоборот, возвращалась нажатой, и снять её было уже нечем.
            // Windows открывает «Пуск», если клавишу Win нажали и отпустили, ничего
            // между ними не нажав. У нас выходит именно так: саму букву мы съели,
            // а Win пришлось отпустить перед отправкой аккорда — для Windows это
            // одиночное нажатие. Поэтому пока Win ещё зажата, подсовываем незанятый
            // код: он ничего не делает, но снимает признак одиночного нажатия.
            // Тот же приём спасает от строки меню, которую открывает одиночный Alt.
            // Alt переключателя окон снимаем там же и без возврата: за физической
            // клавишей он не стоит, перебором _modsDown его не найти, — и не сняв его,
            // мы посылали ⌘C как Alt+Ctrl+C. Переключатель при этом закрывается —
            // ровно то, чего человек и просил, нажав сочетание поверх него.
            var held = new List<KeyValuePair<ModKey, int>>();
            ClearHeld(held);

            MacKeys.Send(sc);
            RestoreHeld(held);
        }

        /// <summary>
        /// Снять с дороги то, что Windows держит, — и вернуть безобидное. Общее у двух
        /// отправителей: сочетаний macOS и готовых аккордов из списка действий.
        /// </summary>
        private void ClearHeld(List<KeyValuePair<ModKey, int>> held)
        {
            foreach (ModKey phys in _modsDown)
            {
                if (_phantomCtrl && (phys == ModKey.LCtrl || phys == ModKey.RCtrl)) continue;
                int vk = WindowsHolds(phys);
                if (vk != 0) held.Add(new KeyValuePair<ModKey, int>(phys, vk));
            }

            bool menu = _cmdTabAlt;
            foreach (KeyValuePair<ModKey, int> p in held) if (IsMenuKey(p.Value)) menu = true;
            if (menu) Input.Tap(VkNoop);

            foreach (KeyValuePair<ModKey, int> p in held)
            {
                Input.Key(p.Value, false);
                // Запомнить снятие обязательно — то же правило, что у ReleaseWindowsKey.
                // Win и Alt мы не возвращаем, и без записи всё вокруг продолжало бы
                // уверять, что клавиша зажата: раскладка Apple отступала, щит ставился
                // вхолостую, а настоящее отпускание уходило вторым разом. Пишем в снятое,
                // а не убираем из _injected: у клавиши, идущей насквозь, записи там нет,
                // и на каждом автоповторе мы слали Alt^ заново — по десятку в секунду.
                if (IsMenuKey(p.Value)) _modLifted[p.Key] = p.Value;
            }
            if (_cmdTabAlt) { Input.Key(Vk.LMenu, false); _cmdTabAlt = false; }
        }

        /// <summary>
        /// Запустить действие из списка, убрав с дороги то, что Windows держит.
        ///
        /// Actions про зажатые модификаторы не знает, а знать надо. Готовый символ
        /// под зажатым Alt приходит как WM_SYSCHAR: пропадает сам и открывает строку
        /// меню — ровно то, ради чего щит и заведён в EmitText, только «=» цифрового
        /// блока шло мимо него. А готовый аккорд в конце отпускает те же модификаторы,
        /// что нажал, — в том числе Win, которую человек держит своей ⌘: она молча
        /// переставала работать до следующего нажатия.
        /// </summary>
        private void RunAction(KeyAction a, bool repeat)
        {
            if (a == null) return;
            if (repeat && !a.Repeats) return;

            if (a.Kind == ActionKind.Text) { EmitText(a.Target); return; }
            if (a.Kind == ActionKind.Chord)
            {
                var held = new List<KeyValuePair<ModKey, int>>();
                ClearHeld(held);
                Input.Chord(a.Mods == null ? new int[0] : a.Mods, a.Vk);
                RestoreHeld(held);
                return;
            }
            Actions.Begin(a, repeat, Settings.BrightnessStep);
        }

        private void RestoreHeld(List<KeyValuePair<ModKey, int>> held)
        {

            // Возвращаем безобидное — ⇧ и control, — и ровно то, что и держали. Клавишу
            // Windows и Alt не возвращаем: сами по себе они открывают «Пуск» и строку
            // меню, а пока они зажаты физически, следующее сочетание всё равно опознается
            // по своему состоянию.
            // Возвращаем только настоящие модификаторы. Caps Lock, нажатый второй раз,
            // зажигает лампочку и разводит наш счёт регистра с Windows; Escape доезжает
            // до приложения настоящим нажатием и закрывает диалог.
            foreach (KeyValuePair<ModKey, int> p in held)
                if (IsModifierKey(p.Value) && !IsMenuKey(p.Value))
                    Input.Key(p.Value, true);
        }

        /// <summary>
        /// Кто отвечает за Windows на вопрос «держит ли она этот код». Пусто — сама
        /// Windows. Шов ровно один и заведён ровно для стенда: перечёт зажатого после
        /// общего сброса иначе не проверить ничем, а он уже дважды оказывался неверным.
        /// Тот же приём, что и Input.Sink.
        /// </summary>
        internal static Func<int, bool> HeldProbe { get; set; }

        /// <summary>Держит ли Windows этот код прямо сейчас.</summary>
        private static bool Held(int vk)
        {
            Func<int, bool> probe = HeldProbe;
            if (probe != null) return probe(vk);
            return (Native.GetKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>
        /// Отпустить всё, что мы могли зажать: иначе после смены настроек, паузы или
        /// пробуждения клавиатуры подставленный модификатор «залипнет». Отпускаем именно
        /// то, что реально ушло в Windows: у переназначенной клавиши это не её
        /// собственный код, а код замены.
        /// </summary>
        private void ReleaseEverything() { ReleaseEverything(null, false); }

        /// <param name="after">
        /// Настройки, по которым перечитывать зажатое. Пусто — нынешние. Отличаются они
        /// ровно в одном случае: настройки меняют, пока человек держит модификатор.
        /// </param>
        /// <param name="keepHolding">
        /// Продолжать ли держать за человека то, что он держит. Правда ровно в одном
        /// случае — смена настроек: только там мы точно знаем, что клавишу держат,
        /// потому что нажатие видели, а отпускания нет.
        ///
        /// Всюду ещё — ложь, и это не осторожность, а единственный верный ответ. Сброс
        /// после «перехват поставлен заново», после возврата с защищённого рабочего стола
        /// и после пропажи клавиатуры затем и делается, что отпускания прошли мимо нас:
        /// человек всё уже отпустил, а мы об этом не знаем. Нажать подставленный код
        /// заново там значит зажать модификатор навсегда — снять его будет нечем.
        /// </param>
        private void ReleaseEverything(Settings after, bool keepHolding)
        {
            try
            {
                // Щит перед отпусканием подставленной Win или Alt. Без него достаточно
                // было держать ⌘ в тот миг, когда случился любой сброс — а он случается
                // сам: пробуждение клавиатуры, движение галочки в окне, возврат с чужого
                // рабочего стола. Windows видела Win нажатой и отпущенной без ничего
                // между ними, то есть одиночное нажатие, и выкидывала «Пуск» поверх работы.
                bool menu = _cmdTabAlt;
                foreach (KeyValuePair<ModKey, int> pair in _injected)
                    if (IsMenuKey(pair.Value) && !_modLifted.ContainsKey(pair.Key)) menu = true;
                if (menu) Input.Tap(VkNoop);

                // Запоминаем, что отпустили. Перечёт ниже спрашивает Windows про
                // собственный код клавиши сразу после этого, а она успевает разобрать
                // посланное не всегда: подставленный нами control записывался бы
                // в множество нажатого как чужая настоящая клавиша — и оставался там
                // навсегда, потому что физически его никто не нажимал.
                var letGo = new List<int>();
                foreach (KeyValuePair<ModKey, int> pair in new List<KeyValuePair<ModKey, int>>(_injected))
                {
                    // Снятое Windows не держит — отпускать нечего. В «уже отпущено»
                    // оно всё равно попадёт: строкой ниже, вместе со всем снятым.
                    if (!_modLifted.ContainsKey(pair.Key)) Input.Key(pair.Value, false);
                    letGo.Add(pair.Value);
                }
                _injected.Clear();
                // Снятое на время тоже кончилось: возвращать его теперь некому — счётчик
                // снятий обнулён ниже вместе со всем, что его держало. Коды кладём в
                // «уже отпущено»: Windows про них ответит «не держит», и перечёт выбросил
                // бы из множества нажатого клавишу, которую человек держит, — а с ней
                // и заменитель Fn.
                foreach (KeyValuePair<ModKey, int> pair in _modLifted) letGo.Add(pair.Value);
                _modLifted.Clear();
                if (_cmdTabAlt) { Input.Key(Vk.LMenu, false); _cmdTabAlt = false; }
                _fnHeld = false;
                _fnPhys = ModKey.None;
                _subReleased = 0;
                _subLifted = ModKey.None;
                _chordTaken.Clear();
                // Настройки могли смениться — запомненную раскладку окна забываем.
                _layHkl = IntPtr.Zero; _layFor = null; _layFile = null;
                _hkl = IntPtr.Zero;
                _deadPrefix = null;
                _swallowed.Clear();
                _letThrough.Clear();


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
                    _fkeyLatched[i] = false;
                    _fkeyTake[i] = false;
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
                _capsOn = (Native.GetKeyState(Vk.Capital) & 1) != 0;

                // Множество нажатого не стираем вовсе, а сверяем с Windows. Стереть
                // и перечитать нельзя: у переназначенной клавиши Windows держит
                // подставленный код, а спросить её про собственный — значит получить
                // «нет». Caps Lock, уведённая на control, выпадала из множества навсегда,
                // и вместе с ней — заменитель Fn, если он висел на ней же: до отпускания
                // клавиши F2 давала яркость вместо настоящей F2.
                //
                // Поэтому только две поправки: добавить то, что Windows держит, а мы
                // прозевали (отпускания проходили мимо, пока перехват стоял на паузе),
                // и убрать то, чего она не держит, — но лишь у клавиш, идущих насквозь:
                // про остальных она ничего сказать не может.
                Settings cur = after != null ? after : _cfg;
                foreach (ModKey phys in new[]
                {
                    ModKey.LCtrl, ModKey.RCtrl, ModKey.LWin, ModKey.RWin,
                    ModKey.LAlt, ModKey.RAlt, ModKey.LShift, ModKey.RShift, ModKey.CapsLock
                })
                {
                    int own = ModNames.VirtualKey(phys);
                    if (own == 0) continue;
                    // Про то, что сами только что отпустили, Windows не спрашиваем.
                    if (letGo.Contains(own)) continue;
                    if (!Held(own))
                    {
                        if (cur == null || cur.TargetOf(phys) == phys) _modsDown.Remove(phys);
                        continue;
                    }
                    // Призрачный Ctrl от AltGr Windows держит по-настоящему, и Held
                    // отвечает про него правдой. Но нажатия его мы не берём и отпускания
                    // не увидим: оно отсекается раньше. Записанный сюда, он оставался
                    // навсегда — а с ним раскладка Apple молча переставала работать
                    // целиком, и ни одно сочетание ⌘ больше не узнавалось.
                    if (_phantomCtrl && phys == ModKey.LCtrl) continue;
                    _modsDown.Add(phys);
                    // Caps Lock тоже: без этого следующий его автоповтор переворачивал
                    // наш счёт регистра второй раз, а Windows не переворачивала ничего.
                    // Но только когда клавиша правда держит Caps Lock. Уведённая
                    // на control, она попадала в множество навсегда — снимают запись
                    // по тому же условию, которое для неё ложно, — и счёт регистра
                    // переставал переворачиваться вовсе.
                }

                // Множество взятого не стирали: оно и есть правда о том, чьи нажатия
                // мы забрали себе. Стерев его и восстановив по «переназначена ли клавиша»,
                // мы записывали себе и то, чего не брали: клавишу, чьё нажатие прошло
                // мимо разбора (пауза, снятый Windows перехват), — и её отпускание потом
                // глотали, оставляя настоящий Alt зажатым навсегда.
                //
                // Здесь только выбрасываем отпущенное: чего человек не держит, того
                // мы и не брали.
                foreach (ModKey phys in new List<ModKey>(_modTaken))
                    if (!_modsDown.Contains(phys)) _modTaken.Remove(phys);

                // И возвращаем то, что держали за него. Подставленный код мы отпустили —
                // а человек клавишу держит, и вернуть её некому: повторного нажатия
                // не будет. Без этого Caps Lock в роли control молча переставал работать
                // до перенажатия: Ctrl+C печатал букву.
                //
                // Только когда новые настройки эту клавишу и правда разбирают: выключенные
                // переназначения и пауза отсекают её отпускание раньше всего, и возвращённый
                // код остался бы зажат навсегда.
                bool live = keepHolding && cur != null && cur.Enabled
                         && !(cur.PauseWhenAppleAbsent && !Devices.AppleConnected);
                if (live)
                    foreach (ModKey phys in _modTaken)
                    {
                        int tvk = ModNames.VirtualKey(cur.TargetOf(phys));
                        // Win и Alt не возвращаем — то же правило, что у RestoreHeld.
                        // Между нашим нажатием и настоящим отпусканием человеком
                        // не происходит ничего, и Windows принимает это за одиночную
                        // клавишу: «Пуск» поверх работы и строка меню. Caps Lock тоже:
                        // повторное нажатие зажгло бы лампочку и развело наш счёт
                        // регистра с Windows.
                        if (tvk == 0 || !IsModifierKey(tvk) || IsMenuKey(tvk)) continue;
                        Input.Key(tvk, true);
                        _injected[phys] = tvk;
                    }

                // И заменитель Fn: он такое же состояние, как множество зажатого, и его
                // забвение видно сразу. Держа правый ⌥, щёлкнуть галочку в окне — и до
                // отпускания клавиши F2 давала бы яркость вместо настоящей F2, а Fn+↑
                // обычную стрелку вместо Page Up.
                if (cur != null && cur.FnSubstitute != ModKey.None
                    && _modsDown.Contains(cur.FnSubstitute))
                {
                    _fnHeld = true;
                    int mod = ModNames.VirtualKey(cur.TargetOf(cur.FnSubstitute));
                    _fnPhys = IsModifierKey(mod) ? cur.FnSubstitute : ModKey.None;
                }
            }
            catch { }
        }
    }
}
