// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace MagicKeys
{
    /// <summary>Один физический клавишный «столбик»: что он даёт на четырёх уровнях.</summary>
    internal sealed class LayoutKey
    {
        public int Scan;
        public string Iso;
        public string Base, Shift, Opt, OptShift;
        public bool BaseDead, ShiftDead, OptDead, OptShiftDead;

        // Отличается ли уровень от раскладки Microsoft для того же языка.
        public bool BaseDiff, ShiftDiff, OptDiff, OptShiftDiff;

        public string Text(bool shift, bool opt)
        {
            if (opt) return shift ? OptShift : Opt;
            return shift ? Shift : Base;
        }

        public bool Dead(bool shift, bool opt)
        {
            if (opt) return shift ? OptShiftDead : OptDead;
            return shift ? ShiftDead : BaseDead;
        }

        public bool Differs(bool shift, bool opt)
        {
            if (opt) return shift ? OptShiftDiff : OptDiff;
            return shift ? ShiftDiff : BaseDiff;
        }
    }

    /// <summary>
    /// Раскладка macOS, разобранная из файла. Данные берутся из Unicode CLDR
    /// (keyboards/osx) — это те же таблицы, по которым работает сама macOS.
    /// </summary>
    internal sealed class AppleLayoutFile
    {
        public string Id = "";
        public string Title = "";
        public string MacName = "";
        public string Path = "";
        public readonly Dictionary<int, LayoutKey> Keys = new Dictionary<int, LayoutKey>();
        public readonly Dictionary<string, string> Transforms = new Dictionary<string, string>();

        public override string ToString() { return Title; }

        public LayoutKey Key(int scan)
        {
            LayoutKey k;
            return Keys.TryGetValue(scan, out k) ? k : null;
        }

        /// <summary>Composе мёртвой клавиши: «´» + «a» = «á». Возвращает null, если пары нет.</summary>
        public string Compose(string prefix, string next)
        {
            string result;
            return Transforms.TryGetValue(prefix + next, out result) ? result : null;
        }

        public static AppleLayoutFile Load(string path)
        {
            var f = new AppleLayoutFile();
            f.Path = path;

            var doc = new XmlDocument();
            doc.XmlResolver = null;
            var rs = new XmlReaderSettings();
            rs.DtdProcessing = DtdProcessing.Ignore;
            rs.XmlResolver = null;
            using (XmlReader rd = XmlReader.Create(path, rs)) doc.Load(rd);

            XmlElement root = doc.DocumentElement;
            if (root == null || root.Name != "AppleLayout") return null;
            f.Id = root.GetAttribute("id");
            f.Title = root.GetAttribute("title");
            f.MacName = root.GetAttribute("macName");
            if (String.IsNullOrEmpty(f.Id)) return null;
            if (String.IsNullOrEmpty(f.Title)) f.Title = f.Id;

            foreach (XmlNode n in root.ChildNodes)
            {
                XmlElement e = n as XmlElement;
                if (e == null) continue;

                if (e.Name == "Key")
                {
                    int scan;
                    if (!int.TryParse(e.GetAttribute("scan"), NumberStyles.HexNumber,
                                      CultureInfo.InvariantCulture, out scan)) continue;
                    var k = new LayoutKey();
                    k.Scan = scan;
                    k.Iso = e.GetAttribute("iso");
                    k.Base = Val(e, "base");
                    k.Shift = Val(e, "shift");
                    k.Opt = Val(e, "opt");
                    k.OptShift = Val(e, "optShift");
                    foreach (string d in Flags(e, "dead"))
                    {
                        if (d == "base") k.BaseDead = true;
                        else if (d == "shift") k.ShiftDead = true;
                        else if (d == "opt") k.OptDead = true;
                        else if (d == "optShift") k.OptShiftDead = true;
                    }
                    foreach (string d in Flags(e, "diff"))
                    {
                        if (d == "base") k.BaseDiff = true;
                        else if (d == "shift") k.ShiftDiff = true;
                        else if (d == "opt") k.OptDiff = true;
                        else if (d == "optShift") k.OptShiftDiff = true;
                    }
                    f.Keys[scan] = k;
                }
                else if (e.Name == "Dead")
                {
                    string from = e.GetAttribute("from"), to = e.GetAttribute("to");
                    if (!String.IsNullOrEmpty(from) && to != null) f.Transforms[from] = to;
                }
            }
            return f.Keys.Count > 0 ? f : null;
        }

        private static string[] Flags(XmlElement e, string name)
        {
            string v = e.GetAttribute(name);
            if (String.IsNullOrEmpty(v)) return new string[0];
            return v.Split(new char[] { (char)32 }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string Val(XmlElement e, string name)
        {
            return e.HasAttribute(name) ? e.GetAttribute(name) : null;
        }
    }

    /// <summary>Все найденные файлы раскладок: рядом с программой и в папке настроек.</summary>
    internal static class Layouts
    {
        private static readonly object Sync = new object();
        private static List<AppleLayoutFile> _all = new List<AppleLayoutFile>();
        private static Dictionary<string, AppleLayoutFile> _byId = new Dictionary<string, AppleLayoutFile>();
        private static volatile bool _loaded;

        public static string AppFolder
        {
            get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layouts"); }
        }

        public static string UserFolder
        {
            get { return System.IO.Path.Combine(Settings.Folder, "layouts"); }
        }

        public static IList<AppleLayoutFile> All
        {
            get { EnsureLoaded(); lock (Sync) return _all; }
        }

        public static AppleLayoutFile ById(string id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            EnsureLoaded();
            lock (Sync)
            {
                AppleLayoutFile f;
                return _byId.TryGetValue(id, out f) ? f : null;
            }
        }

        /// <summary>Загрузить заранее, чтобы это не случилось внутри обработчика хука.</summary>
        public static void Warm() { EnsureLoaded(); }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            Reload();
        }

        public static void Reload()
        {
            var all = new List<AppleLayoutFile>();
            var byId = new Dictionary<string, AppleLayoutFile>();

            // Пользовательские файлы читаются последними и вытесняют встроенные.
            foreach (string folder in new string[] { AppFolder, UserFolder })
            {
                if (!Directory.Exists(folder)) continue;
                string[] files;
                try { files = Directory.GetFiles(folder, "*.xml"); }
                catch { continue; }
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string path in files)
                {
                    AppleLayoutFile f = null;
                    try { f = AppleLayoutFile.Load(path); }
                    catch (Exception e) { Diag.Log("раскладка «" + System.IO.Path.GetFileName(path) + "» не прочитана", e); }
                    if (f == null) continue;
                    if (byId.ContainsKey(f.Id)) all.Remove(byId[f.Id]);
                    byId[f.Id] = f;
                    all.Add(f);
                }
            }

            all.Sort(delegate(AppleLayoutFile a, AppleLayoutFile b)
            {
                return String.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
            });

            lock (Sync) { _all = all; _byId = byId; _loaded = true; }
            Diag.Log("раскладок загружено: " + all.Count);
        }
    }
}
