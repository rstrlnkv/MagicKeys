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
        public string DevicePath;
        public int Vendor;
        public int ProductId;
        public bool IsApple;
        public bool Bluetooth;
        public string Model;
        public int TotalKeys;
        public AppleModel Apple;
        public string Manufacturer;   // строка HID, например «Apple Inc.»
        public string Product;        // строка HID, например «Magic Keyboard with Numeric Keypad»
        public string Serial;

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
            if (path.IndexOf("VID_05AC", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // По Bluetooth путь записан иначе: VID&xxxx004C, где xxxx — вид шины.
            return Regex.IsMatch(path, "VID&[0-9A-Fa-f]{4}004C", RegexOptions.IgnoreCase);
        }

        public static IList<KeyboardInfo> Known
        {
            get { lock (Sync) return _cache; }
        }

        /// <summary>Перечитать список клавиатур. Возвращает true, если наличие Apple-клавиатуры изменилось.</summary>
        public static bool Rescan()
        {
            List<KeyboardInfo> found = Enumerate();
            bool apple = false;
            AppleModel model = null;
            foreach (KeyboardInfo k in found)
            {
                if (!k.IsApple) continue;
                apple = true;
                if (model == null && k.Apple != null) model = k.Apple;
            }

            bool changed = apple != _appleConnected;
            lock (Sync) { _cache = found; _appleModel = model; }
            _appleConnected = apple;
            return changed;
        }

        private static List<KeyboardInfo> Enumerate()
        {
            List<KeyboardInfo> result = new List<KeyboardInfo>();
            uint count = 0;
            uint stride = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));
            if (Native.GetRawInputDeviceList(null, ref count, stride) == uint.MaxValue || count == 0) return result;

            Native.RAWINPUTDEVICELIST[] list = new Native.RAWINPUTDEVICELIST[count];
            if (Native.GetRawInputDeviceList(list, ref count, stride) == uint.MaxValue) return result;

            // Заодно ищем вендорную коллекцию Apple «Device Management»: через неё
            // спрашивается заряд. Её путь — обычный путь интерфейса HID, его можно открыть.
            string statusPath = null;
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
            lock (Sync) _appleStatusPath = statusPath;

            for (int i = 0; i < count; i++)
            {
                if (list[i].dwType != Native.RIM_TYPEKEYBOARD) continue;
                string path = DeviceName(list[i].hDevice);
                if (String.IsNullOrEmpty(path)) continue;

                // Клавиатуры-«призраки» терминальных служб пропускаем.
                if (path.IndexOf("RDP_KBD", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                KeyboardInfo info = new KeyboardInfo();
                info.DevicePath = path;
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

                // Производитель из самого устройства надёжнее догадок по идентификатору.
                // Само же правило по пути — общее, см. IsAppleDevicePath.
                info.IsApple = info.Vendor == AppleUsb || info.Vendor == AppleBluetooth
                            || (info.Manufacturer != null &&
                                info.Manufacturer.IndexOf("Apple", StringComparison.OrdinalIgnoreCase) >= 0);
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
                info.Product = HidString(h, 2);
                info.Serial = HidString(h, 3);
            }
            finally { Native.CloseHandle(h); }
        }

        private static string HidString(IntPtr handle, int which)
        {
            byte[] buf = new byte[512];
            bool ok;
            try
            {
                switch (which)
                {
                    case 1: ok = Native.HidD_GetManufacturerString(handle, buf, buf.Length); break;
                    case 2: ok = Native.HidD_GetProductString(handle, buf, buf.Length); break;
                    default: ok = Native.HidD_GetSerialNumberString(handle, buf, buf.Length); break;
                }
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
