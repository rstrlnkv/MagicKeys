// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MagicKeys
{
    /// <summary>
    /// Значки на клавишах — те, что Apple печатает вместо слов.
    ///
    /// Рисуются фигурами, а не шрифтом. Правило дома «вместо значков слова» написано
    /// про системный шрифт: ⏏ он не знает вовсе, а солнышка яркости в нём нет и подавно.
    /// Своя фигура под это правило не подпадает — ею уже нарисован значок программы
    /// (Iconography) и датчик Touch ID.
    ///
    /// Зачем вообще: Apple никогда не печатает голое «F1». На клавише стоит значок,
    /// а номер под ним мелко, — и без значков верхний ряд выглядит чужой клавиатурой,
    /// сколько бы ни были верны пропорции.
    ///
    /// Чего здесь нет: точного повторения рисунка Apple. Это чужая работа, и копировать
    /// её незачем — нужен узнаваемый знак того же смысла, набранный из кругов, дуг
    /// и треугольников. Солнце, окошки, треугольники перемотки, динамик со звуковыми
    /// волнами узнаются с одного взгляда и на восьми точках.
    /// </summary>
    internal static class KeyboardGlyphs
    {
        /// <summary>
        /// Фигуры значка, вписанные в клавишу. Пусто — значка с таким именем нет.
        ///
        /// underWord — значок делит клавишу с надписью (верхний ряд: значок сверху,
        /// «F1» под ним мелко). Тогда он и меньше, и поднят выше середины.
        /// </summary>
        public static List<Path> Draw(string art, double x, double y, double w, double h,
                                      Brush ink, bool underWord)
        {
            var made = new List<Path>();
            if (String.IsNullOrEmpty(art)) return made;

            double s = Math.Min(w, h) * (underWord ? 0.30 : 0.34);   // сторона квадрата значка
            double u = s / 2;                                        // половина: от неё все размеры
            double cx = x + w / 2;
            double cy = y + h * (underWord ? 0.33 : 0.5);
            double line = Math.Max(1, s * 0.13);                     // толщина штриха

            switch (art)
            {
                case "eject":
                    made.Add(Solid(ink, Tri(cx, cy - u * 0.95, cx + u, cy + u * 0.1, cx - u, cy + u * 0.1),
                                        Box(cx - u * 0.95, cy + u * 0.45, u * 1.9, u * 0.32)));
                    break;

                case "left": made.Add(Solid(ink, Tri(cx - u, cy, cx + u * 0.75, cy - u, cx + u * 0.75, cy + u))); break;
                case "right": made.Add(Solid(ink, Tri(cx + u, cy, cx - u * 0.75, cy - u, cx - u * 0.75, cy + u))); break;
                case "up": made.Add(Solid(ink, Tri(cx, cy - u, cx + u, cy + u * 0.75, cx - u, cy + u * 0.75))); break;
                case "down": made.Add(Solid(ink, Tri(cx, cy + u, cx + u, cy - u * 0.75, cx - u, cy - u * 0.75))); break;

                // Яркость экрана: солнце. Разница между «темнее» и «ярче» у Apple —
                // длина лучей и размер круга, и она видна с одного взгляда.
                case "bright-down": Sun(made, ink, cx, cy, u, line, 0.34, 0.62); break;
                case "bright-up": Sun(made, ink, cx, cy, u, line, 0.46, 0.50); break;

                // Подсветка клавиш у алюминиевой серии: те же лучи, но над клавиатурой.
                case "backlight-down": Backlight(made, ink, cx, cy, u, line, 0.55); break;
                case "backlight-up": Backlight(made, ink, cx, cy, u, line, 0.30); break;

                // Mission Control: три окошка — одно большое снизу, два поменьше сверху.
                case "mission":
                    made.Add(Solid(ink,
                        Box(cx - u * 0.95, cy - u * 0.9, u * 0.8, u * 0.62),
                        Box(cx + u * 0.15, cy - u * 0.9, u * 0.8, u * 0.62),
                        Box(cx - u * 0.95, cy + u * 0.05, u * 1.9, u * 0.85)));
                    break;

                // Launchpad: сетка из шести квадратиков.
                case "launchpad":
                    {
                        var boxes = new List<Geometry>();
                        double d = u * 0.52, gap = u * 0.28;
                        for (int r = 0; r < 2; r++)
                            for (int c = 0; c < 3; c++)
                                boxes.Add(Box(cx + (c - 1) * (d + gap) - d / 2,
                                              cy + (r - 0.5) * (d + gap) - d / 2, d, d));
                        made.Add(Solid(ink, boxes.ToArray()));
                    }
                    break;

                // Spotlight: лупа.
                case "search":
                    made.Add(Line(ink, line, Circle(cx - u * 0.2, cy - u * 0.2, u * 0.62)));
                    made.Add(Solid(ink, Box(cx + u * 0.2, cy + u * 0.2, u * 0.75, line)
                                        .Turned(45, cx + u * 0.2, cy + u * 0.2)));
                    break;

                // Диктовка: микрофон.
                case "mic":
                    made.Add(Solid(ink, Round(cx - u * 0.34, cy - u * 0.95, u * 0.68, u * 1.15, u * 0.34)));
                    made.Add(Line(ink, line, Arc(cx, cy + u * 0.1, u * 0.72, 20, 140)));
                    made.Add(Solid(ink, Box(cx - line / 2, cy + u * 0.72, line, u * 0.3)));
                    break;

                // Не беспокоить: месяц.
                case "moon":
                    {
                        var full = new EllipseGeometry(new Point(cx, cy), u * 0.92, u * 0.92);
                        var bite = new EllipseGeometry(new Point(cx + u * 0.5, cy - u * 0.28), u * 0.82, u * 0.82);
                        var crescent = new CombinedGeometry(GeometryCombineMode.Exclude, full, bite);
                        crescent.Freeze();
                        made.Add(new Path { Data = crescent, Fill = ink, IsHitTestVisible = false });
                    }
                    break;

                case "prev":
                    made.Add(Solid(ink, Tri(cx - u * 0.95, cy, cx - u * 0.05, cy - u * 0.8, cx - u * 0.05, cy + u * 0.8),
                                        Tri(cx + u * 0.05, cy, cx + u * 0.95, cy - u * 0.8, cx + u * 0.95, cy + u * 0.8)));
                    break;
                case "next":
                    made.Add(Solid(ink, Tri(cx + u * 0.95, cy, cx + u * 0.05, cy - u * 0.8, cx + u * 0.05, cy + u * 0.8),
                                        Tri(cx - u * 0.05, cy, cx - u * 0.95, cy - u * 0.8, cx - u * 0.95, cy + u * 0.8)));
                    break;
                case "play":
                    made.Add(Solid(ink, Tri(cx - u * 0.05, cy, cx - u * 0.95, cy - u * 0.8, cx - u * 0.95, cy + u * 0.8),
                                        Box(cx + u * 0.2, cy - u * 0.8, u * 0.26, u * 1.6),
                                        Box(cx + u * 0.68, cy - u * 0.8, u * 0.26, u * 1.6)));
                    break;

                case "mute": Speaker(made, ink, cx, cy, u, line, 0); break;
                case "vol-down": Speaker(made, ink, cx, cy, u, line, 1); break;
                case "vol-up": Speaker(made, ink, cx, cy, u, line, 2); break;

                // Клавиша контекстного меню: страница с выделенной строкой.
                case "menu":
                    made.Add(Line(ink, line, Round(cx - u * 0.7, cy - u * 0.95, u * 1.4, u * 1.9, u * 0.3)));
                    made.Add(Solid(ink, Box(cx - u * 0.42, cy - u * 0.5, u * 0.84, line),
                                        Box(cx - u * 0.42, cy - u * 0.1, u * 0.84, line * 1.8),
                                        Box(cx - u * 0.42, cy + u * 0.5, u * 0.84, line)));
                    break;

                default: return made;
            }
            return made;
        }

        // ---------- составные значки ----------

        /// <summary>Солнце: круг и восемь лучей. Чем ярче, тем круг больше, а лучи короче.</summary>
        private static void Sun(List<Path> made, Brush ink, double cx, double cy, double u,
                                double line, double core, double rayFrom)
        {
            made.Add(Solid(ink, Circle(cx, cy, u * core)));
            var rays = new List<Geometry>();
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                double x1 = cx + Math.Cos(a) * u * rayFrom, y1 = cy + Math.Sin(a) * u * rayFrom;
                double x2 = cx + Math.Cos(a) * u, y2 = cy + Math.Sin(a) * u;
                rays.Add(new LineGeometry(new Point(x1, y1), new Point(x2, y2)));
            }
            made.Add(Line(ink, line, rays.ToArray()));
        }

        /// <summary>Подсветка клавиш: клавиатура и лучи над ней.</summary>
        private static void Backlight(List<Path> made, Brush ink, double cx, double cy, double u,
                                      double line, double rayFrom)
        {
            made.Add(Solid(ink, Round(cx - u * 0.9, cy + u * 0.2, u * 1.8, u * 0.75, u * 0.18)));
            var rays = new List<Geometry>();
            for (int i = -1; i <= 1; i++)
            {
                double x = cx + i * u * 0.55;
                rays.Add(new LineGeometry(new Point(x, cy - u * 0.95), new Point(x, cy - u * rayFrom)));
            }
            made.Add(Line(ink, line, rays.ToArray()));
        }

        /// <summary>Динамик: корпус, раструб и столько звуковых волн, сколько просят. Ноль — крестик.</summary>
        private static void Speaker(List<Path> made, Brush ink, double cx, double cy, double u,
                                    double line, int waves)
        {
            double left = cx - u * 0.95;
            made.Add(Solid(ink,
                Box(left, cy - u * 0.28, u * 0.45, u * 0.56),
                Tri(left + u * 0.45, cy - u * 0.28, left + u * 0.45, cy + u * 0.28, left + u * 1.05, cy + u * 0.85),
                Tri(left + u * 1.05, cy - u * 0.85, left + u * 1.05, cy + u * 0.85, left + u * 0.45, cy - u * 0.28)));

            var marks = new List<Geometry>();
            if (waves == 0)
            {
                double a = cx + u * 0.35, b = cx + u * 0.95;
                marks.Add(new LineGeometry(new Point(a, cy - u * 0.3), new Point(b, cy + u * 0.3)));
                marks.Add(new LineGeometry(new Point(a, cy + u * 0.3), new Point(b, cy - u * 0.3)));
            }
            else
                for (int i = 0; i < waves; i++)
                    marks.Add(Arc(cx + u * 0.1, cy, u * (0.42 + i * 0.38), -55, 110));

            made.Add(Line(ink, line, marks.ToArray()));
        }

        // ---------- кирпичики ----------

        private static Path Solid(Brush ink, params Geometry[] parts)
        {
            var g = new GeometryGroup();
            g.FillRule = FillRule.Nonzero;
            foreach (Geometry p in parts) g.Children.Add(p);
            g.Freeze();
            return new Path { Data = g, Fill = ink, IsHitTestVisible = false };
        }

        private static Path Line(Brush ink, double thickness, params Geometry[] parts)
        {
            var g = new GeometryGroup();
            foreach (Geometry p in parts) g.Children.Add(p);
            g.Freeze();
            return new Path
            {
                Data = g,
                Stroke = ink,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
        }

        private static Geometry Tri(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            var f = new PathFigure { StartPoint = new Point(x1, y1), IsClosed = true, IsFilled = true };
            f.Segments.Add(new LineSegment(new Point(x2, y2), true));
            f.Segments.Add(new LineSegment(new Point(x3, y3), true));
            var g = new PathGeometry();
            g.Figures.Add(f);
            return g;
        }

        private static Geometry Box(double x, double y, double w, double h)
        {
            return new RectangleGeometry(new Rect(x, y, w, h));
        }

        private static Geometry Round(double x, double y, double w, double h, double r)
        {
            return new RectangleGeometry(new Rect(x, y, w, h), r, r);
        }

        private static Geometry Circle(double cx, double cy, double r)
        {
            return new EllipseGeometry(new Point(cx, cy), r, r);
        }

        /// <summary>Дуга от угла to угол: волны звука и подбородок микрофона.</summary>
        private static Geometry Arc(double cx, double cy, double r, double fromDeg, double sweepDeg)
        {
            double a1 = fromDeg * Math.PI / 180, a2 = (fromDeg + sweepDeg) * Math.PI / 180;
            var f = new PathFigure
            {
                StartPoint = new Point(cx + Math.Cos(a1) * r, cy + Math.Sin(a1) * r),
                IsClosed = false,
                IsFilled = false
            };
            f.Segments.Add(new ArcSegment(new Point(cx + Math.Cos(a2) * r, cy + Math.Sin(a2) * r),
                                          new Size(r, r), 0, Math.Abs(sweepDeg) > 180,
                                          sweepDeg > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                                          true));
            var g = new PathGeometry();
            g.Figures.Add(f);
            return g;
        }
    }

    /// <summary>Повернуть фигуру — нужно одной лишь ручке лупы.</summary>
    internal static class GeometryTurn
    {
        public static Geometry Turned(this Geometry g, double degrees, double cx, double cy)
        {
            var copy = g.Clone();
            copy.Transform = new RotateTransform(degrees, cx, cy);
            copy.Freeze();
            return copy;
        }
    }
}
