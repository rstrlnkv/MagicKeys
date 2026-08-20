// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Globalization;
using System.IO;

namespace MagicKeys
{
    /// <summary>
    /// Журнал для разбора «почему не сработало». По умолчанию выключен: включается
    /// запуском с ключом --log и пишет в файл рядом с настройками.
    /// </summary>
    internal static class Diag
    {
        private static readonly object Sync = new object();
        private static string _path;

        public static bool Enabled { get { return _path != null; } }

        public static void Enable()
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                _path = Path.Combine(Settings.Folder, "log.txt");
                lock (Sync)
                    File.AppendAllText(_path, Environment.NewLine + "=== запуск " +
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " ===" + Environment.NewLine);
            }
            catch { _path = null; }
        }

        public static void Log(string message)
        {
            string path = _path;
            if (path == null) return;
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine;
                lock (Sync) File.AppendAllText(path, line);
            }
            catch { }
        }

        public static void Log(string message, Exception e)
        {
            if (_path == null) return;
            Log(message + " — " + (e == null ? "?" : e.GetType().Name + ": " + e.Message));
        }
    }
}
