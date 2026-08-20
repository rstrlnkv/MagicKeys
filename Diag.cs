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


        public static void Enable()
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                _path = Path.Combine(Settings.Folder, "log.txt");
                // Журнал живёт между запусками и до сих пор только рос. Мегабайта хватает
                // на любой разбор, а прошлый оставляем рядом: поломка, ради которой журнал
                // включили, случается один раз и часто в предыдущем запуске.
                try
                {
                    var fi = new FileInfo(_path);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = _path + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(_path, old);
                    }
                }
                catch { }
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
