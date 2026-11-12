// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MagicKeys
{
    /// <summary>
    /// Значки программы. Они не лежат картинками, а рисуются кодом — так они остаются
    /// свободными (правится исходник, а не двоичный файл), выходят чёткими на любом
    /// масштабе экрана и подстраиваются под тему системы.
    ///
    /// Основа рисунка — знак ⌘ (U+2318, «место интереса»): обычный символ Юникода,
    /// а не собственность Apple. Он строится геометрией, а не шрифтом, потому что
    /// нужного начертания в системных шрифтах Windows нет.
    /// </summary>
    internal static class Iconography
    {
        // Плитка Fluent. Синий взят от системного акцента Windows 11 (#0078D4), а не
        // подобран на глаз: значок должен стоять в одном ряду с системными, а не спорить
        // с ними. Ход градиента короткий и сверху вниз — у Fluent глубина обозначается,
        // а не изображается; длинный диагональный переход с уходом в тёмно-синий читался
        // как значок iOS, чего мы как раз не хотим.
        private static readonly Color TileLight = Color.FromArgb(0x3A, 0x96, 0xDD);
        private static readonly Color TileMid = Color.FromArgb(0x00, 0x78, 0xD4);
        private static readonly Color TileDeep = Color.FromArgb(0x00, 0x5A, 0x9E);

        /// <summary>Размеры, которые кладутся в MagicKeys.ico.</summary>
        public static readonly int[] AppIconSizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

        // ---------------------------------------------------------------- знак ⌘

        /// <summary>
        /// Осевая линия знака ⌘ — замкнутый узел: квадрат в середине и четыре петли
        /// по углам. Пропорции классические: сторона квадрата к радиусу петли 1,25.
        /// <paramref name="box"/> — сторона описанного квадрата вместе с толщиной линии.
        /// </summary>
        public static GraphicsPath CommandPath(float cx, float cy, float box, float stroke)
        {
            float k = stroke / box;
            float r = box * (1f - k) / 6.5f;   // радиус осевой линии петли
            float a = 1.25f * r;               // половина стороны квадрата
            float d = a + r;                   // центр петли от середины знака
            float s = 2f * r;

            var p = new GraphicsPath();
            p.AddArc(cx - d - r, cy - d - r, s, s, 90, 270);    // петля слева сверху
            p.AddArc(cx - d - r, cy + d - r, s, s, 0, 270);     // слева снизу
            p.AddArc(cx + d - r, cy + d - r, s, s, 270, 270);   // справа снизу
            p.AddArc(cx + d - r, cy - d - r, s, s, 180, 270);   // справа сверху
            p.CloseFigure();                                    // верхняя сторона квадрата
            return p;
        }

        private static Pen Stroke(Color color, float width)
        {
            var pen = new Pen(color, width);
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }

        // ---------------------------------------------------------------- плитка

        /// <summary>
        /// Суперэллипс — та самая скруглённая плитка Windows 11. От прямоугольника
        /// со скруглением отличается тем, что переход в угол идёт плавно, без стыка
        /// дуги и прямой.
        /// </summary>
        private static GraphicsPath Squircle(RectangleF r, double n)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float ax = r.Width / 2f, ay = r.Height / 2f;
            const int steps = 512;
            var pts = new PointF[steps];
            for (int i = 0; i < steps; i++)
            {
                double t = 2.0 * Math.PI * i / steps;
                double ct = Math.Cos(t), st = Math.Sin(t);
                double x = Math.Sign(ct) * Math.Pow(Math.Abs(ct), 2.0 / n);
                double y = Math.Sign(st) * Math.Pow(Math.Abs(st), 2.0 / n);
                pts[i] = new PointF((float)(cx + ax * x), (float)(cy + ay * y));
            }
            var path = new GraphicsPath();
            path.AddPolygon(pts);
            return path;
        }

        // ---------------------------------------------------------------- значок программы

        /// <summary>Значок для .exe и окна: плитка Fluent со знаком ⌘.</summary>
        public static Bitmap AppIcon(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // На мелких размерах украшения только мешают: там важен силуэт.
                bool detailed = size >= 32;
                float margin = size * (detailed ? 0.055f : 0.025f);
                var rect = new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin);

                // Показатель суперэллипса: 2 — круг, бесконечность — квадрат. 4,6 даёт
                // ту самую форму, по которой узнаются значки iOS и macOS: угол круглее,
                // чем у плиток Windows, но переход в сторону всё ещё плавный.
                using (GraphicsPath tile = Squircle(rect, 4.6))
                {
                    if (size >= 48) SoftShadow(g, tile, size, size * 0.020f, size * 0.028f, 96);

                    // Сверху вниз, а не по диагонали: диагональный переход — язык iOS,
                    // у плиток Windows свет падает вертикально.
                    using (var fill = new LinearGradientBrush(
                               new PointF(rect.Left, rect.Top),
                               new PointF(rect.Left, rect.Bottom), TileLight, TileDeep))
                    {
                        var blend = new ColorBlend(3);
                        blend.Colors = new Color[] { TileLight, TileMid, TileDeep };
                        blend.Positions = new float[] { 0f, 0.5f, 1f };
                        fill.InterpolationColors = blend;
                        fill.WrapMode = WrapMode.TileFlipXY;
                        g.FillPath(fill, tile);
                    }

                    if (detailed)
                    {
                        // Только тонкая светлая кромка по верхнему краю — у Fluent это всё,
                        // что осталось от объёма. Глянцевый блик на верхней половине убран
                        // нарочно: он и есть главный признак значка iOS, а форму мы взяли
                        // оттуда сознательно, подачу — нет.
                        using (var rim = new Pen(Color.FromArgb(52, 255, 255, 255), Math.Max(1f, size / 128f)))
                            g.DrawPath(rim, tile);
                    }
                }

                // Чем мельче значок, тем крупнее и толще должен быть знак: на 16 точках
                // важно, чтобы он вообще читался, а поля вокруг только съедают место.
                float share = size <= 20 ? 0.74f
                            : size <= 24 ? 0.68f
                            : size <= 32 ? 0.61f
                            : size <= 48 ? 0.56f : 0.52f;
                // Линия тоньше прежней: у Fluent знаки лёгкие, толстая обводка — это
                // опять же язык iOS. На мелких размерах тонкая линия пропадает, поэтому
                // облегчение идёт только там, где есть чему пропадать.
                float k = size <= 20 ? 0.140f
                        : size <= 24 ? 0.122f
                        : size <= 32 ? 0.100f
                        : size <= 48 ? 0.080f : 0.066f;
                float box = rect.Width * share;
                float stroke = box * k;
                float cx = size / 2f, cy = size / 2f;

                // Тени под знаком нет: в Fluent знак лежит на плитке, а не парит над ней.
                using (GraphicsPath cmd = CommandPath(cx, cy, box, stroke))
                using (Pen pen = Stroke(Color.White, stroke))
                    g.DrawPath(pen, cmd);
            }
            return bmp;
        }

        // ---------------------------------------------------------------- значок в трее

        /// <summary>
        /// Значок в трее: один знак ⌘ без подложки, одним цветом — так же немногословно,
        /// как сама клавиатура. Цвет задаёт вызывающий, под тему панели задач;
        /// <paramref name="dim"/> приглушает знак, когда переназначения выключены.
        /// </summary>
        public static Bitmap TrayGlyph(int size, Color color, bool dim)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // Меньше прежнего: 0,88 распирало знак до самых краёв, и в ряду системных
                // значков он выглядел крупнее всех. У Fluent значки в трее занимают
                // примерно три четверти площадки и оставляют воздух по краю.
                float box = size * 0.74f;
                // И тоньше: системные значки Windows 11 рисуются лёгкой линией.
                float k = size <= 16 ? 0.110f : (size <= 24 ? 0.094f : 0.084f);
                float stroke = box * k;
                Color c = Color.FromArgb(dim ? 115 : 255, color.R, color.G, color.B);

                using (GraphicsPath p = CommandPath(size / 2f, size / 2f, box, stroke))
                using (Pen pen = Stroke(c, stroke))
                    g.DrawPath(pen, p);
            }
            return bmp;
        }

        // ---------------------------------------------------------------- мягкая тень

        /// <summary>
        /// Размытая тень под плиткой. В GDI+ размытия нет, поэтому силуэт рисуется
        /// в отдельный слой и трижды сглаживается прямоугольным окном — этого хватает,
        /// чтобы край перестал быть виден.
        /// </summary>
        private static void SoftShadow(Graphics g, GraphicsPath shape, int size,
                                       float offsetY, float radius, int alpha)
        {
            int r = (int)Math.Round(radius);
            if (r < 1) return;
            using (var layer = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics lg = Graphics.FromImage(layer))
                {
                    lg.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var b = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0))) lg.FillPath(b, shape);
                }
                BlurAlpha(layer, r);
                g.DrawImage(layer, 0f, offsetY);
            }
        }

        private static void BlurAlpha(Bitmap bmp, int radius)
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h),
                                        ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = d.Stride;
                byte[] buf = new byte[stride * h];
                Marshal.Copy(d.Scan0, buf, 0, buf.Length);

                int[] a = new int[w * h], t = new int[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        a[y * w + x] = buf[y * stride + x * 4 + 3];

                for (int pass = 0; pass < 3; pass++)
                {
                    BlurRows(a, t, w, h, radius, true);
                    BlurRows(t, a, w, h, radius, false);
                }

                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        buf[y * stride + x * 4 + 3] = (byte)a[y * w + x];
                Marshal.Copy(buf, 0, d.Scan0, buf.Length);
            }
            finally { bmp.UnlockBits(d); }
        }

        private static void BlurRows(int[] src, int[] dst, int w, int h, int radius, bool horizontal)
        {
            int outer = horizontal ? h : w;
            int inner = horizontal ? w : h;
            int step = horizontal ? 1 : w;
            int window = radius * 2 + 1;

            for (int o = 0; o < outer; o++)
            {
                int start = horizontal ? o * w : o;
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                    sum += src[start + Clamp(i, 0, inner - 1) * step];
                for (int i = 0; i < inner; i++)
                {
                    dst[start + i * step] = sum / window;
                    sum -= src[start + Clamp(i - radius, 0, inner - 1) * step];
                    sum += src[start + Clamp(i + radius + 1, 0, inner - 1) * step];
                }
            }
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // ---------------------------------------------------------------- сборка .ico

        /// <summary>Собирает файл .ico из готовых картинок разного размера.</summary>
        public static byte[] BuildIco(IList<Bitmap> images)
        {
            var blobs = new byte[images.Count][];
            for (int i = 0; i < images.Count; i++) blobs[i] = Encode(images[i]);

            using (var ms = new MemoryStream())
            {
                var w = new BinaryWriter(ms);
                w.Write((short)0);                  // резерв
                w.Write((short)1);                  // тип: значок
                w.Write((short)images.Count);

                int offset = 6 + 16 * images.Count;
                for (int i = 0; i < images.Count; i++)
                {
                    Bitmap b = images[i];
                    w.Write((byte)(b.Width >= 256 ? 0 : b.Width));
                    w.Write((byte)(b.Height >= 256 ? 0 : b.Height));
                    w.Write((byte)0);               // цветов в палитре — нет
                    w.Write((byte)0);               // резерв
                    w.Write((short)1);              // плоскостей
                    w.Write((short)32);             // бит на точку
                    w.Write(blobs[i].Length);
                    w.Write(offset);
                    offset += blobs[i].Length;
                }
                for (int i = 0; i < images.Count; i++) w.Write(blobs[i]);
                w.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] Encode(Bitmap b)
        {
            // 256×256 принято хранить сжатым — иначе файл распухает вчетверо.
            if (b.Width >= 256)
            {
                using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
            }
            return Dib(b);
        }

        /// <summary>Несжатый 32-битный DIB с перевёрнутыми строками и пустой маской.</summary>
        private static byte[] Dib(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            int rowXor = w * 4;
            int rowAnd = ((w + 31) / 32) * 4;

            using (var ms = new MemoryStream())
            {
                var bw = new BinaryWriter(ms);
                bw.Write(40);                       // biSize
                bw.Write(w);                        // biWidth
                bw.Write(h * 2);                    // biHeight: картинка и маска
                bw.Write((short)1);
                bw.Write((short)32);
                bw.Write(0);                        // BI_RGB
                bw.Write(rowXor * h + rowAnd * h);
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

                BitmapData d = src.LockBits(new Rectangle(0, 0, w, h),
                                            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] row = new byte[rowXor];
                    for (int y = h - 1; y >= 0; y--)
                    {
                        IntPtr p = new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride);
                        Marshal.Copy(p, row, 0, rowXor);
                        bw.Write(row);
                    }
                }
                finally { src.UnlockBits(d); }

                byte[] mask = new byte[rowAnd];
                for (int y = 0; y < h; y++) bw.Write(mask);
                bw.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Превращает картинку в значок, не теряя полупрозрачных краёв.</summary>
        public static Icon ToIcon(Bitmap bmp)
        {
            byte[] data = BuildIco(new Bitmap[] { bmp });
            using (var ms = new MemoryStream(data)) return new Icon(ms);
        }

        /// <summary>Пишет MagicKeys.ico со всеми размерами.</summary>
        public static void SaveAppIcon(string path)
        {
            var images = new List<Bitmap>();
            try
            {
                foreach (int s in AppIconSizes) images.Add(AppIcon(s));
                File.WriteAllBytes(path, BuildIco(images));
            }
            finally { foreach (Bitmap b in images) b.Dispose(); }
        }
    }
}
