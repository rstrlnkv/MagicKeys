// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Gdi = System.Drawing;

namespace MagicKeys
{
    /// <summary>Мелочи, которые делают окно похожим на родное окно Windows 11.</summary>
    internal static class Fluent
    {
        public static void ApplyWindowStyling(Window window)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                int dark = Theme.IsDark ? 1 : 0;
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

                int backdrop = Native.DWMSBT_MAINWINDOW;
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            }
            catch { /* без Mica окно просто останется однотонным */ }
        }

        /// <summary>
        /// Значок в трее — один знак ⌘ без подложки, в цвет панели задач.
        /// Размер берётся у системы: на экране с масштабом он не 16 точек, а больше,
        /// и растянутый значок выглядел бы мылом.
        /// </summary>
        /// <param name="dim">Приглушить: переназначения выключены.</param>
        public static Gdi.Icon MakeTrayIcon(bool dim)
        {
            int size = 16;
            try { size = Native.GetSystemMetrics(Native.SM_CXSMICON); }
            catch { }
            if (size < 16) size = 16;
            if (size > 64) size = 64;

            // На тёмной панели знак белый, на светлой — почти чёрный.
            Gdi.Color ink = Theme.SystemIsDark
                ? Gdi.Color.FromArgb(255, 255, 255, 255)
                : Gdi.Color.FromArgb(255, 0x1A, 0x1A, 0x1A);

            using (Gdi.Bitmap bmp = Iconography.TrayGlyph(size, ink, dim))
                return Iconography.ToIcon(bmp);
        }

    }
}
