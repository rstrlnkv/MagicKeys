// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

namespace MagicKeys
{
    /// <summary>
    /// Чем себя называет эта сборка.
    ///
    /// Версия правится руками при выпуске: она говорит «что это». Номер сборки лежит
    /// в BuildInfo.g.cs, который создаёт build.cmd, и говорит «какая по счёту» — это
    /// число коммитов, оно растёт само и не требует ведения вручную.
    /// </summary>
    internal static partial class BuildInfo
    {
        public const string Version = "1.0.0";
    }
}
