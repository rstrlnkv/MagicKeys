// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System.Reflection;

// Сведения о версии в свойствах файла. Без них Windows показывала «0.0.0.0» и пустое
// описание — и в свойствах .exe, и в диспетчере задач, и в списке автозагрузки, где
// программа опознаётся именно так. Компилятор собирает этот ресурс сам, из атрибутов;
// отдельного файла ресурсов на этой машине нечем собрать.
[assembly: AssemblyTitle("MagicKeys")]
[assembly: AssemblyDescription("Клавиатуры Apple в Windows")]
[assembly: AssemblyProduct("MagicKeys")]
[assembly: AssemblyCompany("r.strlnkv")]
[assembly: AssemblyCopyright("© 2026 r.strlnkv · GNU GPL v3 или новее")]
[assembly: AssemblyVersion(MagicKeys.BuildInfo.Version)]
[assembly: AssemblyFileVersion(MagicKeys.BuildInfo.Version)]

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
        public const string Version = "0.1.0";
    }
}
