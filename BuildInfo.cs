// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace MagicKeys
{
    /// <summary>
    /// Чем себя называет эта сборка. Версию правят руками при выпуске; номер сборки
    /// не хранится нигде — он берётся из даты самого .exe, потому что это единственное
    /// число, которое нельзя забыть обновить.
    /// </summary>
    internal static class BuildInfo
    {
        public const string Version = "1.0";

        private static string _number;

        public static string Number
        {
            get
            {
                if (_number != null) return _number;
                string n = "—";
                try
                {
                    string exe = Assembly.GetEntryAssembly().Location;
                    if (!String.IsNullOrEmpty(exe) && File.Exists(exe))
                        n = File.GetLastWriteTime(exe).ToString("yyMMdd", CultureInfo.InvariantCulture);
                }
                catch { }
                _number = n;
                return n;
            }
        }
    }
}
