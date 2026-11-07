using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CamWatch
{
    internal sealed class DeviceInventory
    {
        private static readonly Guid KSCATEGORY_CAPTURE = new Guid("65E8773D-8F56-11D0-A3B9-00A0C9223196");
        private const int DIGCF_PRESENT = 0x2;
        private const int DIGCF_DEVICEINTERFACE = 0x10;
        private const int SPDRP_DEVICEDESC = 0x0;
        private const int SPDRP_FRIENDLYNAME = 0xC;
        private const int ERROR_NO_MORE_ITEMS = 259;

        private readonly Logger _logger;
        private readonly object _gate = new object();
        private List<DeviceEntry> _devices = new List<DeviceEntry>();
        private string _singleName;
        private Timer _timer;

        public DeviceInventory(Logger logger)
        {
            _logger = logger;
        }

        public void Start()
        {
            ThreadPool.QueueUserWorkItem(delegate { Refresh(true); });
            _timer = new Timer(delegate { Refresh(false); }, null, 10 * 60 * 1000, 10 * 60 * 1000);
        }

        public string AttributionName
        {
            get { lock (_gate) { return _singleName; } }
        }

        public List<DeviceEntry> Snapshot()
        {
            lock (_gate)
            {
                List<DeviceEntry> copy = new List<DeviceEntry>();
                for (int i = 0; i < _devices.Count; i++) copy.Add(_devices[i]);
                return copy;
            }
        }

        private void Refresh(bool forceWrite)
        {
            try
            {
                List<DeviceEntry> list = EnumerateCameras();
                bool changed = false;
                lock (_gate)
                {
                    if (list.Count != _devices.Count) changed = true;
                    else
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i].Name != _devices[i].Name) { changed = true; break; }
                        }
                    }
                    _devices = list;
                    string single = null;
                    if (list.Count == 1 && !string.IsNullOrEmpty(list[0].Name)) single = list[0].Name;
                    _singleName = single;
                }
                if (forceWrite || changed) _logger.SaveDeviceSnapshot(list);
            }
            catch (Exception ex)
            {
                _logger.AppendError("设备枚举失败: " + ex.Message);
                if (forceWrite) _logger.SaveDeviceSnapshot(new List<DeviceEntry>());
            }
        }

        private static List<DeviceEntry> EnumerateCameras()
        {
            List<DeviceEntry> result = new List<DeviceEntry>();
            Guid captureGuid = KSCATEGORY_CAPTURE;
            IntPtr set = SetupDiGetClassDevs(ref captureGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;
            try
            {
                uint index = 0;
                while (true)
                {
                    SP_DEVICE_INTERFACE_DATA did = new SP_DEVICE_INTERFACE_DATA();
                    did.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref captureGuid, index, ref did))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err == ERROR_NO_MORE_ITEMS) break;
                        break;
                    }
                    index++;

                    uint required = 0;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required == 0) continue;

                    IntPtr buffer = Marshal.AllocHGlobal((int)required);
                    IntPtr devInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SP_DEVINFO_DATA)));
                    try
                    {
                        Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                        SP_DEVINFO_DATA devInfo = new SP_DEVINFO_DATA();
                        devInfo.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                        Marshal.StructureToPtr(devInfo, devInfoPtr, false);

                        uint neededSize;
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref did, buffer, required, out neededSize, devInfoPtr)) continue;
                        devInfo = (SP_DEVINFO_DATA)Marshal.PtrToStructure(devInfoPtr, typeof(SP_DEVINFO_DATA));

                        string name = GetRegistryProperty(set, ref devInfo, SPDRP_FRIENDLYNAME);
                        if (string.IsNullOrEmpty(name)) name = GetRegistryProperty(set, ref devInfo, SPDRP_DEVICEDESC);
                        if (string.IsNullOrEmpty(name)) name = "未知摄像头";

                        bool duplicate = false;
                        for (int i = 0; i < result.Count; i++)
                        {
                            if (string.Equals(result[i].Name, name, StringComparison.OrdinalIgnoreCase)) { duplicate = true; break; }
                        }
                        if (!duplicate)
                        {
                            DeviceEntry entry = new DeviceEntry();
                            entry.Name = name;
                            try { entry.Id = Marshal.PtrToStringUni(new IntPtr(buffer.ToInt64() + 4)); }
                            catch { entry.Id = null; }
                            result.Add(entry);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                        Marshal.FreeHGlobal(devInfoPtr);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return result;
        }

        private static string GetRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA devInfo, int property)
        {
            byte[] buffer = new byte[2048];
            int type;
            int required;
            if (SetupDiGetDeviceRegistryProperty(set, ref devInfo, property, out type, buffer, buffer.Length, out required) && required > 2)
            {
                int length = Math.Min(required, buffer.Length);
                string value = Encoding.Unicode.GetString(buffer, 0, length);
                return value.TrimEnd('\0').Trim();
            }
            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, int property, out int propertyRegDataType, byte[] propertyBuffer, int propertyBufferSize, out int requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
    }
}
