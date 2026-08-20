// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MagicKeys
{
    internal enum ActionKind
    {
        PassThrough,    // не трогаем — клавиша остаётся собой
        Swallow,        // глотаем, ничего не делаем
        Key,            // одиночная виртуальная клавиша, с зеркалом нажатия и отпускания
        Chord,          // аккорд вроде Win+Tab, срабатывает один раз на нажатие
        BrightnessDown,
        BrightnessUp,
        Launch,         // запустить программу
        Text,           // напечатать готовый символ, минуя раскладку
        EjectTray       // открыть лоток оптического привода
    }

    internal sealed class KeyAction
    {
        public string Id;
        public string Title;
        public string Group;
        
        public ActionKind Kind;
        public int Vk;
        public int[] Mods;
        public string Target;
        public bool Repeats;     // имеет ли смысл автоповтор при удержании

        public override string ToString() { return Title; }
    }

    internal static class Actions
    {
        private static readonly List<KeyAction> _all = new List<KeyAction>();
        private static readonly Dictionary<string, KeyAction> _byId = new Dictionary<string, KeyAction>();

        public static IList<KeyAction> All { get { return _all; } }

        public static KeyAction Get(string id)
        {
            if (id == null) return _byId["none"];
            KeyAction a;
            return _byId.TryGetValue(id, out a) ? a : _byId["none"];
        }

        private static void Add(string id, string group, string title, ActionKind kind,
                                int vk, int[] mods, bool repeats, string target)
        {
            KeyAction a = new KeyAction();
            a.Id = id; a.Group = group; a.Title = title; a.Kind = kind;
            a.Vk = vk; a.Mods = mods; a.Repeats = repeats; a.Target = target;
            _all.Add(a);
            _byId[id] = a;
        }

        static Actions()
        {
            Add("none", "Без изменений", "Оставить как есть", ActionKind.PassThrough, 0, null, false, null);
            Add("swallow", "Без изменений", "Отключить клавишу", ActionKind.Swallow, 0, null, false, null);

            Add("brightness.down", "Экран", "Яркость экрана меньше", ActionKind.BrightnessDown, 0, null, true, null);
            Add("brightness.up", "Экран", "Яркость экрана больше", ActionKind.BrightnessUp, 0, null, true, null);

            Add("volume.mute", "Звук", "Выключить звук", ActionKind.Key, Vk.VolumeMute, null, false, null);
            Add("volume.down", "Звук", "Тише", ActionKind.Key, Vk.VolumeDown, null, true, null);
            Add("volume.up", "Звук", "Громче", ActionKind.Key, Vk.VolumeUp, null, true, null);

            Add("media.prev", "Мультимедиа", "Предыдущий трек", ActionKind.Key, Vk.MediaPrev, null, false, null);
            Add("media.play", "Мультимедиа", "Воспроизведение / пауза", ActionKind.Key, Vk.MediaPlay, null, false, null);
            Add("media.next", "Мультимедиа", "Следующий трек", ActionKind.Key, Vk.MediaNext, null, false, null);
            Add("media.stop", "Мультимедиа", "Остановить", ActionKind.Key, Vk.MediaStop, null, false, null);

            Add("sys.taskview", "Система", "Представление задач (Win+Tab)", ActionKind.Chord, Vk.Tab, new int[] { Vk.LWin }, false, null);
            Add("sys.search", "Система", "Поиск (Win+S)", ActionKind.Chord, Vk.S, new int[] { Vk.LWin }, false, null);
            Add("sys.start", "Система", "Меню «Пуск»", ActionKind.Chord, 0, new int[] { Vk.LWin }, false, null);
            Add("sys.desktop", "Система", "Свернуть всё (Win+D)", ActionKind.Chord, Vk.D, new int[] { Vk.LWin }, false, null);
            Add("sys.explorer", "Система", "Проводник (Win+E)", ActionKind.Chord, Vk.E, new int[] { Vk.LWin }, false, null);
            Add("sys.settings", "Система", "Параметры (Win+I)", ActionKind.Chord, Vk.I, new int[] { Vk.LWin }, false, null);
            Add("sys.lock", "Система", "Заблокировать экран (Win+L)", ActionKind.Chord, Vk.L, new int[] { Vk.LWin }, false, null);
            Add("sys.snip", "Система", "Снимок области (Win+Shift+S)", ActionKind.Chord, Vk.S, new int[] { Vk.LWin, Vk.LShift }, false, null);
            Add("sys.emoji", "Система", "Панель эмодзи (Win+.)", ActionKind.Chord, Vk.OemPeriod, new int[] { Vk.LWin }, false, null);
            Add("sys.notifications", "Система", "Центр уведомлений (Win+N)", ActionKind.Chord, 0x4E, new int[] { Vk.LWin }, false, null);
            Add("sys.projection", "Система", "Проецирование (Win+P)", ActionKind.Chord, 0x50, new int[] { Vk.LWin }, false, null);
            Add("sys.dictation", "Система", "Голосовой ввод (Win+H)", ActionKind.Chord, Vk.H, new int[] { Vk.LWin }, false, null);
            Add("sys.quicksettings", "Система", "Быстрые настройки (Win+A) — там же «Не беспокоить»", ActionKind.Chord, Vk.A, new int[] { Vk.LWin }, false, null);
            Add("sys.calc", "Система", "Калькулятор", ActionKind.Launch, 0, null, false, "calc.exe");

            // Замена клавише ⏏ там, где она есть, и просто полезное действие там, где нет.
            // Настоящего «извлечения» в Windows нет: есть лоток привода и есть список
            // съёмных устройств, откуда безопасно отключают флешки и внешние диски.
            Add("eject.tray", "Извлечение", "Открыть лоток привода", ActionKind.EjectTray, 0, null, false, null);
            Add("eject.devices", "Извлечение", "Безопасное извлечение устройств",
                ActionKind.Launch, 0, null, false, "rundll32.exe|shell32.dll,Control_RunDLL hotplug.dll");

            Add("key.delete", "Клавиши", "Delete (удаление вперёд)", ActionKind.Key, Vk.Delete, null, true, null);
            Add("key.insert", "Клавиши", "Insert", ActionKind.Key, Vk.Insert, null, false, null);
            Add("key.home", "Клавиши", "Home", ActionKind.Key, Vk.Home, null, true, null);
            Add("key.end", "Клавиши", "End", ActionKind.Key, Vk.End, null, true, null);
            Add("key.pageup", "Клавиши", "Page Up", ActionKind.Key, Vk.Prior, null, true, null);
            Add("key.pagedown", "Клавиши", "Page Down", ActionKind.Key, Vk.Next, null, true, null);
            Add("key.printscreen", "Клавиши", "Print Screen", ActionKind.Key, Vk.Snapshot, null, false, null);
            Add("key.escape", "Клавиши", "Escape", ActionKind.Key, Vk.Escape, null, false, null);
            Add("key.pause", "Клавиши", "Pause / Break", ActionKind.Key, Vk.Pause, null, false, null);
            Add("key.scrolllock", "Клавиши", "Scroll Lock", ActionKind.Key, Vk.Scroll, null, false, null);
            Add("key.numlock", "Клавиши", "Num Lock", ActionKind.Key, Vk.NumLock, null, false, null);

            Add("text.equals", "Знаки", "Печатать «=»", ActionKind.Text, 0, null, true, "=");
            Add("text.comma", "Знаки", "Печатать «,»", ActionKind.Text, 0, null, true, ",");
            Add("text.period", "Знаки", "Печатать «.»", ActionKind.Text, 0, null, true, ".");

            Add("ime.kana", "Ввод иероглифов", "Кана / カナ", ActionKind.Key, Vk.Kana, null, false, null);
            Add("ime.on", "Ввод иероглифов", "Включить японский ввод", ActionKind.Key, Vk.ImeOn, null, false, null);
            Add("ime.off", "Ввод иероглифов", "Выключить японский ввод", ActionKind.Key, Vk.ImeOff, null, false, null);
            Add("ime.convert", "Ввод иероглифов", "Преобразовать / 変換", ActionKind.Key, Vk.Convert, null, false, null);
            Add("ime.nonconvert", "Ввод иероглифов", "Не преобразовывать / 無変換", ActionKind.Key, Vk.NonConvert, null, false, null);
        }

        /// <summary>Нажатие. <paramref name="repeat"/> — это автоповтор от удержания.</summary>
        public static void Begin(KeyAction a, bool repeat, int brightnessStep)
        {
            if (a == null) return;
            switch (a.Kind)
            {
                case ActionKind.Key:
                    if (!repeat || a.Repeats) Input.Key(a.Vk, true);
                    break;
                case ActionKind.Chord:
                    if (!repeat) Input.Chord(a.Mods == null ? new int[0] : a.Mods, a.Vk);
                    break;
                case ActionKind.BrightnessDown:
                    Brightness.Nudge(-brightnessStep);
                    break;
                case ActionKind.BrightnessUp:
                    Brightness.Nudge(brightnessStep);
                    break;
                case ActionKind.Launch:
                    if (!repeat) Spawn(a.Target);
                    break;
                case ActionKind.Text:
                    Input.Text(a.Target);
                    break;
                case ActionKind.EjectTray:
                    if (!repeat) EjectTray();
                    break;
            }
        }

        public static void End(KeyAction a)
        {
            if (a == null) return;
            if (a.Kind == ActionKind.Key) Input.Key(a.Vk, false);
        }

        [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int mciSendStringW(string cmd, System.Text.StringBuilder ret, int len, IntPtr cb);

        /// <summary>
        /// Открывает лоток оптического привода — ближайшее, что в Windows есть к ⏏.
        /// Если привода нет, команда просто ничего не сделает: MCI вернёт ошибку,
        /// а придумывать несуществующее устройство мы не станем.
        /// </summary>
        private static void EjectTray()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    int rc = mciSendStringW("set cdaudio door open", null, 0, IntPtr.Zero);
                    if (rc != 0) Diag.Log("лоток привода не открылся, код MCI " + rc);
                }
                catch (Exception e) { Diag.Log("лоток привода: сбой", e); }
            });
        }

        private static void Spawn(string target)
        {
            if (String.IsNullOrEmpty(target)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    // «команда|аргументы» — иначе всё считается именем программы.
                    string cmd = target, args = null;
                    int bar = target.IndexOf('|');
                    if (bar > 0) { cmd = target.Substring(0, bar); args = target.Substring(bar + 1); }

                    ProcessStartInfo psi = new ProcessStartInfo(cmd);
                    if (args != null) psi.Arguments = args;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch (Exception e) { Diag.Log("не удалось запустить: " + target, e); }
            });
        }
    }
}
