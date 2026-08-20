// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace MagicKeys
{
    /// <summary>
    /// Родной драйвер Apple для клавиатур — тот самый KeyMagic из Boot Camp.
    ///
    /// Он делает в ядре примерно то же, что MagicKeys делает в пользовательском режиме,
    /// но с одним важным преимуществом: драйвер привязан к конкретному устройству, а
    /// низкоуровневый перехват — нет. Поэтому если драйвер стоит, разумнее уступить ему
    /// функциональный ряд, иначе одно нажатие переназначится дважды.
    ///
    /// Сам драйвер сюда не входит и войти не может: это несвободные файлы Apple, а
    /// MagicKeys выпущен под GPL. Программа только замечает его и подстраивается.
    /// </summary>
    internal static class AppleDriver
    {
        // Драйвер Magic Keyboard зовётся KeyMagic2, драйвер прежних моделей — KeyMagic.
        private static readonly string[] ServiceKeys =
        {
            @"SYSTEM\CurrentControlSet\Services\KeyMagic2",
            @"SYSTEM\CurrentControlSet\Services\KeyMagic"
        };

        // Значение OSXFnBehavior драйвер пишет в ключ устройства под классом клавиатур.
        private const string KeyboardClass =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96b-e325-11ce-bfc1-08002be10318}";

        private static readonly object Sync = new object();
        private static bool _installed;
        private static bool _enabled;
        private static int _fnBehavior = -1;
        private static string _fnBehaviorPath;
        private static DateTime _stamp = DateTime.MinValue;

        /// <summary>Драйвер установлен в системе.</summary>
        public static bool Installed { get { Refresh(false); lock (Sync) return _installed; } }

        /// <summary>Установлен и не отключён.</summary>
        public static bool Active { get { Refresh(false); lock (Sync) return _installed && _enabled; } }

        /// <summary>Значение OSXFnBehavior: 1 — как на маке, 0 — наоборот. −1, если не найдено.</summary>
        public static int FnBehavior { get { Refresh(false); lock (Sync) return _fnBehavior; } }

        /// <summary>Где именно нашлось OSXFnBehavior — чтобы человек мог поправить сам.</summary>
        public static string FnBehaviorPath { get { lock (Sync) return _fnBehaviorPath; } }

        /// <summary>
        /// Забирает ли драйвер функциональный ряд себе. Замерено на Magic Keyboard
        /// с драйвером KeyMagic2: при OSXFnBehavior = 1 нажатие F8 приходит уже готовым
        /// медиакодом (ПУСК/ПАУЗА) и помечено как подставленное, то есть до нашего
        /// перехвата F-клавиши не доходят вовсе. При 0 ряд приходит F-клавишами,
        /// и переназначать его должны мы. Значение −1 (не нашлось) считаем за 1:
        /// у Apple это поведение по умолчанию.
        /// </summary>
        // Читается из обработчика хука, поэтому здесь не должно быть ничего медленного.
        // Раньше геттер звал Refresh, а тот раз в 30 секунд перебирал подключи класса
        // клавиатур — десятки открытий ключа HKLM прямо в хуке. На холодном кэше или
        // под антивирусом это укладывается в сотни миллисекунд, а после 300 Windows
        // снимает перехват молча. Теперь значение обновляет сторожевой таймер Engine.
        private static volatile bool _takesRow;

        public static bool TakesFunctionRow { get { return _takesRow; } }

        /// <summary>
        /// Меняет OSXFnBehavior. Значение лежит в HKLM, поэтому нужны права
        /// администратора: запускается reg.exe с запросом повышения. Драйвер
        /// перечитывает его не сразу — нужно переподключить клавиатуру или
        /// перезагрузиться.
        /// </summary>
        public static bool SetFnBehavior(int value, out string error)
        {
            error = null;
            string path;
            lock (Sync) path = _fnBehaviorPath;
            if (String.IsNullOrEmpty(path))
            {
                // Значения ещё нет — заводим его в ключе службы.
                foreach (string p in ServiceKeys)
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(p, false))
                        if (k != null) { path = @"HKLM\" + p; break; }
            }
            if (String.IsNullOrEmpty(path)) { error = "драйвер Apple не найден"; return false; }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("reg.exe",
                    "add \"" + path + "\" /v OSXFnBehavior /t REG_DWORD /d " + value + " /f");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    p.WaitForExit(30000);
                    if (!p.HasExited) { error = "Windows не ответила на запрос записи"; return false; }
                    if (p.ExitCode != 0) { error = "Windows отказалась записать значение (код " + p.ExitCode + ")"; return false; }
                }
                Refresh(true);
                return true;
            }
            catch (Exception e)
            {
                // Отказ от повышения прав приходит сюда же — это не поломка.
                error = e.Message;
                Diag.Log("драйвер Apple: не удалось записать OSXFnBehavior", e);
                return false;
            }
        }

        public static void Refresh(bool force)
        {
            lock (Sync)
            {
                if (!force && (DateTime.UtcNow - _stamp) < TimeSpan.FromSeconds(30)) return;
                _stamp = DateTime.UtcNow;
            }

            bool installed = false, enabled = false;
            int behavior = -1;
            string where = null;

            try
            {
                foreach (string path in ServiceKeys)
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(path, false))
                    {
                        if (k == null) continue;
                        installed = true;
                        object start = k.GetValue("Start");
                        // 4 — служба отключена; всё остальное считаем рабочим
                        enabled = !(start is int) || ((int)start) != 4;
                        TryBehavior(k, path, ref behavior, ref where);
                        using (RegistryKey p = k.OpenSubKey("Parameters", false))
                            if (p != null) TryBehavior(p, path + @"\Parameters", ref behavior, ref where);
                        break;
                    }
                }

                if (behavior < 0) FindBehaviorInClass(ref behavior, ref where);
            }
            catch (Exception e) { Diag.Log("драйвер Apple: не удалось посмотреть реестр", e); }

            lock (Sync)
            {
                _installed = installed;
                _enabled = enabled;
                _fnBehavior = behavior;
                _fnBehaviorPath = where;
            }
            _takesRow = installed && enabled && behavior != 0;
        }


        /// <summary>Обходит ключи класса клавиатур: значение живёт у устройства, а не у службы.</summary>
        private static void FindBehaviorInClass(ref int behavior, ref string where)
        {
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(KeyboardClass, false))
                {
                    if (root == null) return;
                    foreach (string sub in root.GetSubKeyNames())
                    {
                        using (RegistryKey k = root.OpenSubKey(sub, false))
                        {
                            if (k == null) continue;
                            if (TryBehavior(k, KeyboardClass + @"\" + sub, ref behavior, ref where)) return;
                        }
                    }
                }
            }
            catch { }
        }

        private static bool TryBehavior(RegistryKey key, string path, ref int behavior, ref string where)
        {
            object v = key.GetValue("OSXFnBehavior");
            if (v == null) return false;
            try
            {
                if (v is int) behavior = (int)v;
                else if (v is byte[]) { byte[] b = (byte[])v; if (b.Length > 0) behavior = b[0]; }
                else { int parsed; if (int.TryParse(Convert.ToString(v), out parsed)) behavior = parsed; }
            }
            catch { return false; }
            where = @"HKLM\" + path;
            return true;
        }
    }
}
