// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MagicKeys
{
    internal sealed class KeyboardInfo
    {
        public int Vendor;
        public int ProductId;
        public bool IsApple;
        public bool Bluetooth;
        public string Model;
        public int TotalKeys;
        public AppleModel Apple;
        public string Manufacturer;   // строка HID, например «Apple Inc.»
        public string Product;        // строка HID, например «Magic Keyboard with Numeric Keypad»
        public string VendorProduct
        {
            get
            {
                return String.Format(CultureInfo.InvariantCulture, "VID {0:X4} · PID {1:X4}", Vendor, ProductId);
            }
        }
    }

    /// <summary>
    /// Опознание клавиатур через сырой ввод. Нужно ровно для двух вещей:
    /// показать пользователю, что за клавиатура подключена, и приостановить
    /// переназначения, когда Magic Keyboard отсоединили.
    /// </summary>
    internal static class Devices
    {
        // Apple по USB — 0x05AC, по Bluetooth (список SIG) — 0x004C.
        private const int AppleUsb = 0x05AC;
        private const int AppleBluetooth = 0x004C;

        private static readonly Regex UsbIds = new Regex(@"VID_([0-9A-Fa-f]{4}).*?PID_([0-9A-Fa-f]{4})", RegexOptions.Compiled);
        private static readonly Regex BtIds = new Regex(@"VID&[0-9A-Fa-f]{4}([0-9A-Fa-f]{4}).*?PID&([0-9A-Fa-f]{4})", RegexOptions.Compiled);

        private static readonly object Sync = new object();
        private static volatile bool _appleConnected;
        private static volatile string _mark = "";

        /// <summary>Отпечаток набора клавиатур: по нему видно любую замену, а не только уход Apple.</summary>
        private static string Mark(List<KeyboardInfo> list)
        {
            var ids = new List<string>();
            foreach (KeyboardInfo k in list)
                ids.Add(k.Vendor.ToString("X4") + ":" + k.ProductId.ToString("X4") + ":" + (k.IsApple ? "A" : "-"));
            ids.Sort();
            return String.Join(",", ids.ToArray());
        }
        private static AppleModel _appleModel;
        private static string _appleStatusPath;
        private static List<KeyboardInfo> _cache = new List<KeyboardInfo>();

        public static bool AppleConnected { get { return _appleConnected; } }

        /// <summary>Модель подключённой клавиатуры Apple, если она опознана.</summary>
        public static AppleModel AppleModel { get { lock (Sync) return _appleModel; } }

        /// <summary>Путь к вендорной коллекции Apple «Device Management» — через неё спрашивается заряд.</summary>
        public static string AppleStatusPath { get { lock (Sync) return _appleStatusPath; } }

        /// <summary>
        /// Apple ли это устройство, если судить по пути сырого ввода. Одно правило
        /// на программу: раньше их было два — здесь по идентификатору поставщика и
        /// строке производителя, а в переписи клавиш по двум готовым строкам, — и они
        /// расходились. На одной и той же клавиатуре по Bluetooth «переназначения
        /// приостановлены?» и «это событие с Apple?» могли ответить по-разному.
        /// </summary>
        public static bool IsAppleDevicePath(string path)
        {
            if (String.IsNullOrEmpty(path)) return false;

            // Теми же выражениями, что и при перечислении устройств, — иначе «одно
            // правило» осталось бы двумя: прежняя проверка знала только VID_05AC и
            // VID&xxxx004C, а перечисление принимало оба идентификатора в обоих
            // написаниях. Клавиатура, чей путь по Bluetooth записан через USB-образец,
            // считалась бы своей в одном месте и чужой в другом.
            Match m = UsbIds.Match(path);
            if (!m.Success) m = BtIds.Match(path);
            if (!m.Success) return false;
            int vendor;
            if (!Int32.TryParse(m.Groups[1].Value, NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out vendor)) return false;
            return vendor == AppleUsb || vendor == AppleBluetooth;
        }

        public static IList<KeyboardInfo> Known
        {
            get { lock (Sync) return _cache; }
        }

        /// <summary>Набор клавиатур стал другим. Поднимается на том потоке, что опрашивал.</summary>
        public static event Action SetChanged;

        /// <summary>Замок опроса. Не тот, что бережёт поля, — см. Rescan.</summary>
        private static readonly object ScanSync = new object();

        /// <summary>
        /// Перечитать список клавиатур.
        ///
        /// Опрос целиком под своим замком, а не только запись полей. Замок полей
        /// закрывает разрыв между ними, но не порядок: опрос устройств по Bluetooth
        /// идёт секунды, и начатый раньше мог кончиться позже — тогда в полях оставался
        /// старый набор, а событие «набор изменился» поднималось дважды. Окно показывало
        /// отсоединённую клавиатуру как подключённую, а заводские назначения не той
        /// модели успевали подставиться и сохраниться.
        /// </summary>
        public static void Rescan()
        {
            lock (ScanSync) RescanHeld();
        }

        private static void RescanHeld()
        {
            string statusPath;
            List<KeyboardInfo> found = Enumerate(out statusPath);
            bool apple = false;
            AppleModel model = null;
            foreach (KeyboardInfo k in found)
            {
                if (!k.IsApple) continue;
                apple = true;
                if (model == null && k.Apple != null) model = k.Apple;
            }

            // «Изменилось» — это про весь набор, а не только про «есть ли Apple».
            // Читают этот ответ трое: сброс догадки об исполнении, подстановка заводских
            // назначений и обновление окна, и всем троим важна замена одной клавиатуры
            // на другую — а прежнее условие её не замечало.
            // Всё под одним замком: раньше _cache писался под ним, а _mark
            // и _appleConnected рядом, снаружи. Два опроса разом — из окна и со
            // сторожевого таймера — могли оставить список от одного прохода, а признак
            // «Apple на месте» от другого, и оба объявляли, что набор изменился.
            string mark = Mark(found);
            bool changed;
            lock (Sync)
            {
                changed = mark != _mark;
                _cache = found;
                _appleModel = model;
                _appleConnected = apple;
                _appleStatusPath = statusPath;
                _mark = mark;
            }

            // Событием, а не только ответом. Ответ читает один — сторож перехвата,
            // а зовут опрос трое, и любой из них съедал признак: человек менял
            // клавиатуру и сразу жал «Обновить», опрос кнопки обновлял отметку
            // и возвращал «изменилось» в пустоту, а следующий тик сторожа отвечал
            // «не изменилось». Исполнение оставалось угаданным по прежней клавиатуре,
            // заводские назначения новой не подставлялись, зажатое не отпускалось.
            if (changed)
            {
                // Заряд спрашиваем заново: у новой клавиатуры он свой, а прежний ответ
                // держится минуту. Без этого подключённая на ходу клавиатура до минуты
                // числилась «не ответившей».
                KeyboardBattery.Invalidate();
                Action h = SetChanged;
                if (h != null) h();
            }
        }

        /// <summary>
        /// Перечислить клавиатуры. Путь для запроса заряда возвращается вместе
        /// со списком, а не оседает в статике: опрос идут два потока сразу — сторож
        /// перехвата и кнопка «Обновить», — и статикой один проход отдавал путь
        /// другому. Заряд после этого спрашивался у только что отключённой клавиатуры
        /// и объявлялся неизвестным при подключённой.
        /// </summary>
        private static List<KeyboardInfo> Enumerate(out string statusPath)
        {
            List<KeyboardInfo> result = new List<KeyboardInfo>();
            statusPath = null;
            uint stride = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));

            // Спрашиваем дважды — сперва размер, потом сам список, — и между двумя
            // вопросами в системе может появиться любое устройство: мышь, флешка,
            // гарнитура, переподключившаяся по Bluetooth клавиатура. Тогда второй вопрос
            // отвечает «мал буфер», и прежний код возвращал пустой список — не «опрос
            // не удался», а «клавиатур нет ни одной». Дальше это шло по всей программе:
            // подсказка значка мигала на «ожидание Magic Keyboard», зажатое отпускалось,
            // угаданное исполнение терялось, заряд пропадал. Повторяем с запасом.
            uint count = 0;
            Native.RAWINPUTDEVICELIST[] list = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                uint want = 0;
                if (Native.GetRawInputDeviceList(null, ref want, stride) == uint.MaxValue) return result;
                if (want == 0) return result;
                var buf = new Native.RAWINPUTDEVICELIST[want + 8];
                count = (uint)buf.Length;
                uint got = Native.GetRawInputDeviceList(buf, ref count, stride);
                if (got == uint.MaxValue) continue;   // прибавилось между вопросами
                list = buf;
                count = got;
                break;
            }
            if (list == null) return result;

            // Заодно ищем вендорную коллекцию Apple «Device Management»: через неё
            // спрашивается заряд. Её путь — обычный путь интерфейса HID, его можно открыть.
            for (int i = 0; i < count; i++)
            {
                if (list[i].dwType != Native.RIM_TYPEHID) continue;
                Native.RID_DEVICE_INFO hi;
                if (!Info(list[i].hDevice, out hi) || hi.dwType != Native.RIM_TYPEHID) continue;
                if (hi.hid.usUsagePage != 0xFF00 || hi.hid.usUsage != 0x0014) continue;
                if (hi.hid.dwVendorId != AppleUsb && hi.hid.dwVendorId != AppleBluetooth) continue;
                statusPath = DeviceName(list[i].hDevice);
                break;
            }
            // Под тем же замком, что и остальное: раньше это поле писалось отдельным
            // захватом, и два одновременных опроса могли оставить путь от одного прохода,
            // а список устройств от другого.

            for (int i = 0; i < count; i++)
            {
                if (list[i].dwType != Native.RIM_TYPEKEYBOARD) continue;
                string path = DeviceName(list[i].hDevice);
                if (String.IsNullOrEmpty(path)) continue;

                // Клавиатуры-«призраки» терминальных служб пропускаем.
                if (path.IndexOf("RDP_KBD", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                KeyboardInfo info = new KeyboardInfo();
                info.Bluetooth = path.IndexOf("BTHENUM", StringComparison.OrdinalIgnoreCase) >= 0
                              || path.IndexOf("00001124-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase) >= 0;

                Match m = UsbIds.Match(path);
                if (m.Success)
                {
                    info.Vendor = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
                    info.ProductId = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
                }
                else
                {
                    m = BtIds.Match(path);
                    if (m.Success)
                    {
                        info.Vendor = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
                        info.ProductId = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
                    }
                }

                ReadStrings(path, info);

                // Одно правило на программу — то же самое, что у IsAppleDevicePath.
                // Строку производителя сюда не подмешиваем: перепись клавиш её не видит
                // и судит только по пути, а расхождение этих двух ответов — ровно то,
                // ради чего правило и сводили в одно место. Клавиатура с чужим VID,
                // назвавшаяся «Apple», числилась бы своей в списке, а её события
                // перепись бы не считала.
                info.IsApple = IsAppleDevicePath(path);
                if (info.IsApple)
                {
                    info.Apple = Models.Find(info.ProductId);
                    if (info.Apple == null) info.Apple = Models.FromProduct(info.Product, info.ProductId);
                }
                info.Model = Describe(info);

                Native.RID_DEVICE_INFO ri;
                if (Info(list[i].hDevice, out ri) && ri.dwType == Native.RIM_TYPEKEYBOARD)
                    info.TotalKeys = (int)ri.keyboard.dwNumberOfKeysTotal;

                result.Add(info);
            }
            return result;
        }

        /// <summary>
        /// Спросить у самого устройства, как оно называется. Открываем без запроса доступа
        /// (dwDesiredAccess = 0) — иначе Windows не даст открыть клавиатуру.
        /// </summary>
        private static void ReadStrings(string path, KeyboardInfo info)
        {
            IntPtr h = Native.CreateFileW(path, 0, Native.FILE_SHARE_READWRITE, IntPtr.Zero,
                                          Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == IntPtr.Zero || h.ToInt64() == -1) return;
            try
            {
                info.Manufacturer = HidString(h, 1);
                // Серийный номер не спрашиваем: показывать его негде, а лишний обход HID
                // на каждую клавиатуру повторяется раз в две секунды и по Bluetooth
                // стоит дорого.
                info.Product = HidString(h, 2);
            }
            finally { Native.CloseHandle(h); }
        }

        private static string HidString(IntPtr handle, int which)
        {
            byte[] buf = new byte[512];
            bool ok;
            try
            {
                ok = which == 1
                   ? Native.HidD_GetManufacturerString(handle, buf, buf.Length)
                   : Native.HidD_GetProductString(handle, buf, buf.Length);
            }
            catch { return null; }
            if (!ok) return null;
            string s = Encoding.Unicode.GetString(buf);
            int zero = s.IndexOf('\0');
            if (zero >= 0) s = s.Substring(0, zero);
            s = s.Trim();
            return s.Length == 0 ? null : s;
        }

        private static string Describe(KeyboardInfo info)
        {
            if (!info.IsApple)
                return info.Vendor == 0 ? "Клавиатура" : "Клавиатура (не Apple)";

            return info.Apple != null ? info.Apple.Name : "Клавиатура Apple";
        }

        private static string DeviceName(IntPtr handle)
        {
            uint size = 0;
            Native.GetRawInputDeviceInfoW(handle, Native.RIDI_DEVICENAME, IntPtr.Zero, ref size);
            if (size == 0 || size > 4096) return null;
            IntPtr buf = Marshal.AllocHGlobal((int)size * 2);
            try
            {
                if (Native.GetRawInputDeviceInfoW(handle, Native.RIDI_DEVICENAME, buf, ref size) == uint.MaxValue) return null;
                return Marshal.PtrToStringUni(buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static bool Info(IntPtr handle, out Native.RID_DEVICE_INFO info)
        {
            info = new Native.RID_DEVICE_INFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(Native.RID_DEVICE_INFO));
            uint size = info.cbSize;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                Marshal.StructureToPtr(info, buf, false);
                if (Native.GetRawInputDeviceInfoW(handle, Native.RIDI_DEVICEINFO, buf, ref size) == uint.MaxValue) return false;
                info = (Native.RID_DEVICE_INFO)Marshal.PtrToStructure(buf, typeof(Native.RID_DEVICE_INFO));
                return true;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }
}
