// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

// Пересобирает MagicKeys.ico из кода в Iconography.cs и рисует лист с образцами,
// чтобы значок можно было посмотреть во всех размерах сразу. Запускается вручную
// через makeicon.cmd — в обычную сборку программы не входит.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace MagicKeys
{
    internal static class MakeIcon
    {
        // Все десять — те же, что в самом значке. Прежде 40 и 96 на лист не попадали,
        // а посмотреть стоит как раз на них: это масштабы 250 % и 600 %.
        private static readonly int[] Show = { 256, 128, 96, 64, 48, 40, 32, 24, 20, 16 };
        private static readonly int[] TraySizes = { 16, 20, 24, 32 };

        private static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
            try
            {
                string ico = Path.Combine(dir, "MagicKeys.ico");
                Iconography.SaveAppIcon(ico);
                Console.WriteLine("значок: " + ico + " (" + new FileInfo(ico).Length + " Б, "
                                  + Iconography.AppIconSizes.Length + " размеров)");

                string sheet = Path.Combine(dir, "tools", "preview.png");
                Sheet(sheet);
                Console.WriteLine("образцы: " + sheet);
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("не вышло: " + e.Message);
                return 1;
            }
        }

        /// <summary>Лист с образцами: значок программы и значок в трее, на светлом и тёмном.</summary>
        private static void Sheet(string path)
        {
            const int W = 980, H = 1000;
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                var light = Color.FromArgb(0xF3, 0xF3, 0xF3);
                var dark = Color.FromArgb(0x20, 0x20, 0x20);

                using (var b = new SolidBrush(light)) g.FillRectangle(b, 0, 0, W, H / 2);
                using (var b = new SolidBrush(dark)) g.FillRectangle(b, 0, H / 2, W, H / 2);

                using (var font = new Font("Segoe UI", 9f))
                using (var onLight = new SolidBrush(Color.FromArgb(0x60, 0x60, 0x60)))
                using (var onDark = new SolidBrush(Color.FromArgb(0xA0, 0xA0, 0xA0)))
                {
                    for (int half = 0; half < 2; half++)
                    {
                        int top = half * H / 2;
                        Brush text = half == 0 ? onLight : onDark;

                        g.DrawString("значок программы", font, text, 24, top + 14);
                        int x = 24, band = top + 40;
                        foreach (int s in Show)
                        {
                            using (Bitmap i = Iconography.AppIcon(s))
                                g.DrawImageUnscaled(i, x, band + (256 - s) / 2);
                            g.DrawString(s.ToString(), font, text, x, band + 264);
                            x += s + 24;
                        }

                        Color glyph = half == 0 ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.White;

                        g.DrawString("значок в трее — включено, выключено", font, text, 24, top + 320);
                        int tx = 24, ty = top + 348;
                        foreach (int s in TraySizes)
                        {
                            using (Bitmap i = Iconography.TrayGlyph(s, glyph, false))
                                g.DrawImageUnscaled(i, tx + (32 - s) / 2, ty + (32 - s) / 2);
                            using (Bitmap i = Iconography.TrayGlyph(s, glyph, true))
                                g.DrawImageUnscaled(i, tx + (32 - s) / 2, ty + 46 + (32 - s) / 2);
                            g.DrawString(s.ToString(), font, text, tx, ty + 86);
                            tx += 32 + 28;
                        }

                        // Как это выглядит на настоящей панели задач: в ряд, вплотную.
                        int px = 300, py = top + 348;
                        using (var panel = new SolidBrush(half == 0
                                   ? Color.FromArgb(0xE8, 0xE8, 0xE8) : Color.FromArgb(0x2B, 0x2B, 0x2B)))
                            g.FillRectangle(panel, px, py - 6, 210, 40);
                        for (int i = 0; i < 5; i++)
                        {
                            using (Bitmap t = Iconography.TrayGlyph(16, glyph, i == 2))
                                g.DrawImageUnscaled(t, px + 18 + i * 36, py + 6);
                        }
                        g.DrawString("в ряду панели задач", font, text, px, py + 42);
                    }
                }

                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}
