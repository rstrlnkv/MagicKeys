// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MagicKeys
{
    /// <summary>
    /// Что менялось от версии к версии — вся история, лежащая внутри программы.
    ///
    /// Прежде кнопка «Что нового» уводила в браузер, на страницу выпусков. Это
    /// работало ровно до того случая, ради которого кнопка и нужна: у копии,
    /// которая новее последнего выпуска — или собрана на своей машине, — читать
    /// там было нечего. Теперь читать нечего только если чейнджлог не попал
    /// в сборку.
    ///
    /// Вид: номер версии и дата одной строкой, ниже — изменения, у каждого свой
    /// бейдж слева. Бейдж вынесен в отдельную колонку, а не приклеен к тексту:
    /// так род изменения виден с одного взгляда, до чтения, и глаз находит
    /// нужное, пробегая список сверху вниз.
    /// </summary>
    internal sealed class ChangelogWindow : Window
    {
        public ChangelogWindow()
        {
            Title = "Что нового";
            Width = 620;
            MinWidth = 480;
            Height = 660;
            MinHeight = 380;
            MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 80);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.Resources["Bg"];
            UseLayoutRounding = true;

            Content = Build();
            SourceInitialized += delegate { Fluent.ApplyWindowStyling(this); };
        }

        private UIElement Build()
        {
            var stack = new StackPanel();
            stack.Margin = new Thickness(28, 22, 28, 24);

            List<ChangelogEntry> entries = Changelog.Read();
            if (entries.Count == 0)
            {
                // Молчать нельзя: пустое окно человек читает как поломку программы,
                // а не как «в эту сборку историю не положили».
                stack.Children.Add(Note("История изменений в эту сборку не попала. " +
                                        "Она есть в файле CHANGELOG.md рядом с исходниками " +
                                        "и на странице выпусков."));
            }
            else
            {
                Version mine = Updater.Current;
                bool first = true;

                foreach (ChangelogEntry e in entries)
                {
                    Version v = Updater.ParseVersion(e.Version);
                    bool yours = v != null && mine != null
                              && v.Major == mine.Major && v.Minor == mine.Minor && v.Build == mine.Build;

                    stack.Children.Add(Head(e, yours, first));
                    first = false;

                    if (e.Note.Length > 0)
                    {
                        TextBlock note = Note(e.Note);
                        note.Margin = new Thickness(0, 12, 0, 0);
                        stack.Children.Add(note);
                    }

                    foreach (ChangelogLine line in e.Lines) stack.Children.Add(Row(line));
                }
            }

            var scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Content = stack;
            return scroll;
        }

        /// <summary>Номер версии и дата одной строкой: номер крупно, дата рядом и тише.</summary>
        private static UIElement Head(ChangelogEntry entry, bool yours, bool first)
        {
            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, first ? 0 : 30, 0, 4);

            var number = new TextBlock();
            number.Text = entry.Version;
            number.FontSize = 19;
            number.FontWeight = FontWeights.SemiBold;
            number.Foreground = (Brush)Application.Current.Resources["Text"];
            row.Children.Add(number);

            // «Ваша версия» — то, ради чего человек сюда и пришёл: понять, где он
            // сам стоит в этом списке.
            string aside = Pretty(entry.Date);
            if (yours) aside = aside.Length > 0 ? aside + " · ваша версия" : "ваша версия";

            if (aside.Length > 0)
            {
                var when = new TextBlock();
                when.Text = aside;
                when.FontSize = 13;
                when.Margin = new Thickness(10, 0, 0, 2);
                when.VerticalAlignment = VerticalAlignment.Bottom;
                when.Foreground = (Brush)Application.Current.Resources["TextTer"];
                row.Children.Add(when);
            }
            return row;
        }

        /// <summary>Одно изменение: бейдж в своей колонке, текст обтекает справа.</summary>
        private static UIElement Row(ChangelogLine line)
        {
            var grid = new Grid();
            grid.Margin = new Thickness(0, 12, 0, 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            if (line.Badge.Length > 0)
            {
                UIElement badge = Badge(line.Badge);
                Grid.SetColumn(badge, 0);
                grid.Children.Add(badge);
            }

            var text = new TextBlock();
            text.Text = line.Text;
            text.TextWrapping = TextWrapping.Wrap;
            text.LineHeight = 21;
            text.FontSize = 13;
            text.Foreground = (Brush)Application.Current.Resources["TextSec"];
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            return grid;
        }

        /// <summary>
        /// Бейдж: подложка своего цвета и надпись на ней. Цвета живут в теме,
        /// а не здесь: в светлой теме те же три рода изменений нужны в других
        /// тонах, иначе надпись на подложке не читается.
        /// </summary>
        private static UIElement Badge(string name)
        {
            string kind = name.Trim().ToUpperInvariant();
            string bg, fg;
            switch (kind)
            {
                case "NEW": bg = "BadgeNewBg"; fg = "BadgeNewText"; break;
                case "FIX": bg = "BadgeFixBg"; fg = "BadgeFixText"; break;
                default: bg = "BadgeUpdBg"; fg = "BadgeUpdText"; break;
            }

            var label = new TextBlock();
            label.Text = kind;
            label.FontSize = 10;
            label.FontWeight = FontWeights.Bold;
            label.Foreground = (Brush)Application.Current.Resources[fg];
            label.HorizontalAlignment = HorizontalAlignment.Center;

            var chip = new Border();
            chip.Background = (Brush)Application.Current.Resources[bg];
            chip.CornerRadius = new CornerRadius(4);
            chip.Padding = new Thickness(8, 3, 8, 4);
            // Ширина одна на все три: «FIX» короче «NEW», и без этого колонка
            // бейджей идёт волной, а глаз ищет в ней ровный край.
            chip.MinWidth = 44;
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            chip.VerticalAlignment = VerticalAlignment.Top;
            chip.Margin = new Thickness(0, 1, 10, 0);
            chip.Child = label;
            return chip;
        }

        private static TextBlock Note(string text)
        {
            var tb = new TextBlock();
            tb.Text = text;
            tb.TextWrapping = TextWrapping.Wrap;
            tb.LineHeight = 21;
            tb.FontSize = 13;
            tb.Foreground = (Brush)Application.Current.Resources["TextSec"];
            return tb;
        }

        /// <summary>
        /// Дата в файле записана как «2026-08-27» — так её не спутает ни один
        /// читатель и ни одна страна. В окне показываем словами; не разобрали —
        /// оставляем как есть.
        /// </summary>
        private static string Pretty(string iso)
        {
            if (String.IsNullOrEmpty(iso)) return "";
            DateTime parsed;
            if (DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out parsed))
                return parsed.ToString("D", CultureInfo.CurrentCulture);
            return iso;
        }
    }
}
