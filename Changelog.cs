// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MagicKeys
{
    /// <summary>Строка изменения: бейдж и текст порознь — в окне они рисуются по-разному.</summary>
    internal sealed class ChangelogLine
    {
        public string Badge = "";          // NEW, FIX, UPD; пусто — строка без бейджа
        public string Text = "";
    }

    /// <summary>Один выпуск в чейнджлоге: номер, дата и строки изменений.</summary>
    internal sealed class ChangelogEntry
    {
        public string Version = "";
        public string Date = "";
        public string Note = "";           // «Первый выпуск.» и подобное — абзац перед списком
        public List<ChangelogLine> Lines = new List<ChangelogLine>();
    }

    /// <summary>
    /// Чейнджлог, лежащий внутри программы.
    ///
    /// `CHANGELOG.md` кладётся в сборку ресурсом — так же, как номер сборки
    /// вписывается в `BuildInfo.g.cs`. Причина простая: до этого прочитать
    /// «что нового» изнутри было нельзя ни при каких условиях. Кнопка уводила
    /// в браузер, на страницу выпусков, — и работало это ровно до того случая,
    /// ради которого кнопка и нужна: человеку, который ТОЛЬКО ЧТО обновился,
    /// важно, что он получил, а на странице выпусков его версии может не быть
    /// вовсе — у сборки со своей машины её там нет никогда.
    ///
    /// Внутри программы он работает всегда: без сети, без выпуска на GitHub
    /// и у копии, собранной самому себе.
    /// </summary>
    internal static class Changelog
    {
        /// <summary>Выпуски, свежий первым. Пусто — ресурса нет (сборка без чейнджлога).</summary>
        public static List<ChangelogEntry> Read()
        {
            var list = new List<ChangelogEntry>();
            string text = Raw();
            if (String.IsNullOrEmpty(text)) return list;

            ChangelogEntry current = null;
            foreach (string raw in text.Split((char)10))
            {
                string line = raw.Trim();

                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    current = new ChangelogEntry();
                    string head = line.Substring(3).Trim();

                    // «0.2.1 — 2026-08-27»: номер, длинное тире, дата.
                    int dash = head.IndexOf('—');
                    if (dash > 0)
                    {
                        current.Version = head.Substring(0, dash).Trim();
                        current.Date = head.Substring(dash + 1).Trim();
                    }
                    else current.Version = head;

                    list.Add(current);
                    continue;
                }

                if (current == null) continue;      // шапка файла до первого выпуска
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                if (line.StartsWith("- ", StringComparison.Ordinal))
                    current.Lines.Add(Split(line.Substring(2)));
                else if (current.Lines.Count == 0 && current.Note.Length == 0)
                    current.Note = Plain(line);
            }
            return list;
        }

        /// <summary>
        /// Разобрать строку списка на бейдж и текст. «**NEW** Правый control стал
        /// клавишей» — это бейдж «NEW» и всё остальное; звёздочки и обратные кавычки
        /// убираются, потому что в окне их некому превратить в оформление.
        /// </summary>
        public static ChangelogLine Split(string bullet)
        {
            var line = new ChangelogLine();
            string s = bullet.Trim();

            if (s.StartsWith("**", StringComparison.Ordinal))
            {
                int end = s.IndexOf("**", 2, StringComparison.Ordinal);
                if (end > 2)
                {
                    line.Badge = s.Substring(2, end - 2).Trim();
                    s = s.Substring(end + 2).TrimStart();
                }
            }
            line.Text = s.Replace("`", "");
            return line;
        }

        /// <summary>
        /// То же самое одной строкой — для мест, где бейджу негде быть отдельным:
        /// абзац перед списком и подсказка о найденном обновлении.
        /// </summary>
        public static string Plain(string text)
        {
            ChangelogLine line = Split(text);
            return line.Badge.Length > 0 ? line.Badge + " · " + line.Text : line.Text;
        }

        private static string Raw()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("changelog"))
                {
                    if (s == null) return null;
                    using (var reader = new StreamReader(s, Encoding.UTF8)) return reader.ReadToEnd();
                }
            }
            catch { return null; }
        }
    }
}
