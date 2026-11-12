// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MagicKeys
{
    /// <summary>
    /// Окна, до которых программе не дотянуться.
    ///
    /// Windows не даёт процессу вмешиваться в работу процесса с более высоким уровнем
    /// целостности — это UIPI, и обходить его нечем. Для программы это значит две вещи
    /// сразу: низкоуровневый перехват не получает нажатий, предназначенных такому окну,
    /// и SendInput в него не проходит. Пока впереди окно, запущенное от администратора,
    /// клавиатура ведёт себя ровно так, как вела бы без программы, — а человек видит,
    /// что «сочетания вдруг стали старыми», и ищет неисправность в настройках.
    ///
    /// Здесь программа это замечает. Не чинит — починить нельзя, не запросив прав
    /// администратора себе, — но перестаёт делать вид, что всё в порядке.
    ///
    /// Уровень читается, а не угадывается: у процесса окна впереди берётся метка
    /// целостности из его же токена. Замер на этой машине: обычный процесс читает
    /// метки чужих без всяких прав — из ста тридцати семи открывшихся процессов
    /// двадцать один оказался высоким, среди них PowerToys и Armoury Crate.
    /// </summary>
    internal static class Elevated
    {
        private const int High = 0x3000;   // SECURITY_MANDATORY_HIGH_RID

        private static readonly object Sync = new object();
        private static int _mine = -1;
        private static string _seenName;
        private static uint _seenTick;
        private static int _seenCount;

        /// <summary>Имя последней программы, чьё окно оказалось недосягаемым. null — такого не было.</summary>
        public static string SeenName { get { lock (Sync) return _seenName; } }

        /// <summary>Сколько раз опрос заставал недосягаемое окно впереди.</summary>
        public static int SeenCount { get { lock (Sync) return _seenCount; } }

        /// <summary>Отметка GetTickCount последней такой встречи; 0 — не было.</summary>
        public static uint SeenTick { get { lock (Sync) return _seenTick; } }

        /// <summary>Было ли недосягаемое окно впереди за последние столько-то миллисекунд.</summary>
        public static bool SeenWithin(uint ms)
        {
            uint tick;
            lock (Sync) tick = _seenTick;
            if (tick == 0) return false;
            return unchecked((int)(Native.GetTickCount() - tick)) < (int)ms;
        }

        /// <summary>Забыть увиденное — для стенда и для сброса настроек.</summary>
        public static void Forget()
        {
            lock (Sync) { _seenName = null; _seenTick = 0; _seenCount = 0; }
        }

        /// <summary>
        /// Кто отвечает на вопрос «до окна впереди не дотянуться?». Обычно — Windows.
        /// Шов для стенда: недосягаемое окно на стенде взять негде — его пришлось бы
        /// запускать от администратора прямо во время прогона, — а проверять надо
        /// и то, что программа его запоминает, и то, что сторож перехвата после этого
        /// молчит. Возвращает имя программы или null, если всё в досягаемости.
        /// В самой программе не подменяется никогда.
        /// </summary>
        internal static Func<string> Probe { get; set; }

        /// <summary>
        /// Посмотреть, что за окно сейчас впереди, и запомнить, если до него не дотянуться.
        /// Зовётся с того же двухсекундного таймера, что и опрос устройств: на пути
        /// нажатия клавиши этой работе не место.
        /// </summary>
        public static void Sample()
        {
            try
            {
                string name;
                Func<string> probe = Probe;
                if (probe != null) { name = probe(); if (name == null) return; }
                else if (!ForegroundIsHigher(out name)) return;
                lock (Sync)
                {
                    _seenCount++;
                    _seenTick = Native.GetTickCount();
                    bool fresh = _seenName != name;
                    _seenName = name;
                    if (fresh) Diag.Log("окно впереди недосягаемо: " + name +
                                        " — перехват его нажатий не видит, подставить своё нечем");
                }
            }
            catch (Exception e) { Diag.Log("проверка окна впереди: сбой", e); }
        }

        /// <summary>
        /// Выше ли уровень целостности у окна впереди, чем у нас. Своё окно и рабочий
        /// стол не в счёт: у первого уровень наш, второй окном не является.
        /// </summary>
        public static bool ForegroundIsHigher(out string name)
        {
            name = null;
            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return false;
            if (pid == (uint)Native.GetCurrentProcessId()) return false;

            int mine = Mine();
            if (mine < 0) return false;

            IntPtr proc = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (proc == IntPtr.Zero)
            {
                // Процесс не открылся вовсе. Так отвечают процессы уровня «система»
                // — это тоже «не дотянуться», и врать про это нечего. Имени у нас
                // при этом нет: за ним пришлось бы лезть в тот же закрытый процесс.
                if (Marshal.GetLastWin32Error() == 5) { name = "процесс уровня системы"; return true; }
                return false;
            }
            try
            {
                int level = LevelOf(proc);
                if (level < mine || level < High) return false;
                name = NameOf(proc);
                return true;
            }
            finally { Native.CloseHandle(proc); }
        }

        /// <summary>Свой уровень целостности — для стенда: вся затея держится на том,
        /// что метку удаётся прочитать вообще.</summary>
        internal static int MyLevel { get { return Mine(); } }

        /// <summary>Свой уровень целостности — считается один раз: он не меняется на ходу.</summary>
        private static int Mine()
        {
            lock (Sync)
            {
                if (_mine == -1) _mine = LevelOf(Native.GetCurrentProcess());
                return _mine;
            }
        }

        /// <summary>
        /// Метка целостности процесса: последняя подчинённая часть SID из TokenIntegrityLevel.
        /// −1, если прочитать не удалось, — и тогда мы молчим, а не выдумываем.
        /// </summary>
        private static int LevelOf(IntPtr proc)
        {
            IntPtr token;
            if (!Native.OpenProcessToken(proc, Native.TOKEN_QUERY, out token)) return -1;
            try
            {
                int need;
                Native.GetTokenInformation(token, Native.TokenIntegrityLevel, IntPtr.Zero, 0, out need);
                if (need <= 0) return -1;
                IntPtr buf = Marshal.AllocHGlobal(need);
                try
                {
                    if (!Native.GetTokenInformation(token, Native.TokenIntegrityLevel, buf, need, out need))
                        return -1;
                    // TOKEN_MANDATORY_LABEL — это SID_AND_ATTRIBUTES, первое поле указатель на SID.
                    IntPtr sid = Marshal.ReadIntPtr(buf);
                    if (sid == IntPtr.Zero) return -1;
                    IntPtr countPtr = Native.GetSidSubAuthorityCount(sid);
                    if (countPtr == IntPtr.Zero) return -1;
                    int count = Marshal.ReadByte(countPtr);
                    if (count <= 0) return -1;
                    IntPtr sub = Native.GetSidSubAuthority(sid, (uint)(count - 1));
                    if (sub == IntPtr.Zero) return -1;
                    return Marshal.ReadInt32(sub);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { Native.CloseHandle(token); }
        }

        /// <summary>Имя программы по уже открытому дескриптору — без файла и расширения.</summary>
        private static string NameOf(IntPtr proc)
        {
            var buf = new StringBuilder(520);
            int size = buf.Capacity;
            if (!Native.QueryFullProcessImageNameW(proc, 0, buf, ref size)) return "программа с правами администратора";
            string path = buf.ToString();
            int slash = path.LastIndexOf('\\');
            if (slash >= 0) path = path.Substring(slash + 1);
            if (path.Length > 4 && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - 4);
            return path.Length == 0 ? "программа с правами администратора" : path;
        }
    }
}
