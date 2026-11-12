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
        public List<ChangelogLine> Lines = new List<ChangelogLine>();
    }

    /// <summary>
    /// Чейнджлог, лежащий внутри программы.
    ///
    /// `CHANGELOG.ru.md` (русский перевод `CHANGELOG.md`) кладётся в сборку ресурсом — так же, как номер сборки
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
            string text = Raw();
            return String.IsNullOrEmpty(text) ? new List<ChangelogEntry>() : Parse(text);
        }

        /// <summary>
        /// Разобрать чейнджлог в формате Keep a Changelog: «## [0.2.1] - 2026-08-27»,
        /// под ним группы «### Добавлено» и строки «- ...». Группа становится бейджем
        /// каждой своей строки — окно рисует бейджи NEW, FIX и UPD, как прежде.
        /// [Unreleased] выпуском не считается: у него нет ни номера, ни даты.
        /// </summary>
        public static List<ChangelogEntry> Parse(string text)
        {
            var list = new List<ChangelogEntry>();
            ChangelogEntry current = null;
            string badge = "";

            foreach (string raw in text.Split((char)10))
            {
                string line = raw.Trim();

                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    current = null;
                    badge = "";
                    string head = line.Substring(3).Trim();
                    int close = head.IndexOf(']');
                    if (!head.StartsWith("[", StringComparison.Ordinal) || close < 0) continue;

                    string version = head.Substring(1, close - 1).Trim();
                    if (version == "Unreleased") continue;

                    current = new ChangelogEntry { Version = version };
                    int dash = head.IndexOf(" - ", close, StringComparison.Ordinal);
                    if (dash > 0) current.Date = head.Substring(dash + 3).Trim();
                    list.Add(current);
                    continue;
                }

                if (current == null) continue;      // шапка файла и [Unreleased]

                if (line.StartsWith("### ", StringComparison.Ordinal))
                    badge = BadgeOf(line.Substring(4).Trim());
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                    current.Lines.Add(new ChangelogLine { Badge = badge, Text = line.Substring(2).Trim().Replace("`", "") });
            }
            return list;
        }

        /// <summary>
        /// Бейдж по группе. Добавленное — NEW, исправленное и закрытые дыры — FIX,
        /// всё прочее (изменено, удалено, устарело) — UPD.
        /// </summary>
        private static string BadgeOf(string group)
        {
            switch (group)
            {
                case "Добавлено": case "Added": return "NEW";
                case "Исправлено": case "Fixed": case "Безопасность": case "Security": return "FIX";
                default: return "UPD";
            }
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
