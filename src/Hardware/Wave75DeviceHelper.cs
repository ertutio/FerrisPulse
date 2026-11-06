// ============================================================================
// 模块名称: Wave75DeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责: 通过 WAVE75 PMO HUB 接收器的 HID 通道读取键盘身份与电量
// 依赖关系: RazerDeviceHelper.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. 修改协议解析或公共方法时同步更新本模块说明与 ARCHITECTURE.md。
// 2. 保持 C# 5 / .NET Framework 4 兼容，并保存为 UTF-8 with BOM。
// 3. 仅在精确识别接收器集合及下游键盘身份后上报电量。
// ============================================================================

using System;
using System.Runtime.InteropServices;

namespace RazerBatteryTray
{
    public static class Wave75DeviceHelper
    {
        private const int ReceiverVid = 0x36B0;
        private const int ReceiverPid = 0x3002;
        private const int ReportLength = 33; // Windows HID includes report ID 0.
        private const int ChunkLength = 24;
        private static readonly object Sync = new object();

        public static byte[] BuildRequest(byte command, int offset, int length)
        {
            if (offset < 0 || offset > 65535 || length < 1 || length > ChunkLength) return null;
            byte[] packet = new byte[ReportLength];
            packet[1] = 0xAA;
            packet[2] = command;
            packet[3] = (byte)offset;
            packet[4] = (byte)(offset >> 8);
            packet[5] = (byte)length;
            return packet;
        }

        public static bool TryParseReply(byte[] report, byte command, int offset, int length, out byte[] data)
        {
            data = null;
            if (report == null || report.Length != ReportLength || length < 1 || length > ChunkLength) return false;
            if (report[0] != 0 || report[1] != 0xAA || report[2] != command ||
                report[3] != (byte)offset || report[4] != (byte)(offset >> 8) ||
                report[5] != length || report[8] != 0x55) return false;
            data = new byte[length];
            Buffer.BlockCopy(report, 9, data, 0, length);
            return true;
        }

        public static bool IsWave75Identity(byte[] info)
        {
            if (info == null || info.Length != 56) return false;
            int vid = info[2] | (info[3] << 8);
            int pid = info[4] | (info[5] << 8);
            return vid == ReceiverVid && (pid == 0x3008 || pid == 0x3009) &&
                info[8] <= 2 && info[11] == 103;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeOverlapped
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr Event;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr handle, IntPtr buffer, uint length, out uint transferred, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr handle, IntPtr buffer, uint length, out uint transferred, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr handle, IntPtr overlapped, out uint transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(IntPtr handle);

        // Keep the unmanaged report and OVERLAPPED alive until completion or cancelled completion.
        private static bool Transfer(IntPtr handle, byte[] report, bool write, uint timeout)
        {
            IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
            if (evt == IntPtr.Zero || evt.ToInt64() == -1) return false;
            IntPtr buffer = IntPtr.Zero;
            IntPtr overlapped = IntPtr.Zero;
            try
            {
                buffer = Marshal.AllocHGlobal(report.Length);
                Marshal.Copy(report, 0, buffer, report.Length);
                overlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
                NativeOverlapped state = new NativeOverlapped();
                state.Event = evt;
                Marshal.StructureToPtr(state, overlapped, false);
                uint transferred;
                bool done = write
                    ? WriteFile(handle, buffer, (uint)report.Length, out transferred, overlapped)
                    : ReadFile(handle, buffer, (uint)report.Length, out transferred, overlapped);
                if (!done)
                {
                    if (Marshal.GetLastWin32Error() != 997) return false;
                    if (WaitForSingleObject(evt, timeout) != 0)
                    {
                        CancelIo(handle);
                        GetOverlappedResult(handle, overlapped, out transferred, true);
                        return false;
                    }
                    if (!GetOverlappedResult(handle, overlapped, out transferred, false)) return false;
                }
                if (transferred != report.Length) return false;
                if (!write) Marshal.Copy(buffer, report, 0, report.Length);
                return true;
            }
            finally
            {
                if (overlapped != IntPtr.Zero) Marshal.FreeHGlobal(overlapped);
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                RazerDeviceHelper.CloseHandle(evt);
            }
        }

        private static byte[] Request(IntPtr handle, byte command, int offset, int length)
        {
            return Request(handle, command, offset, length, null);
        }

        private static byte[] Request(IntPtr handle, byte command, int offset, int length, byte[] payload)
        {
            byte[] packet = BuildRequest(command, offset, length);
            if (packet != null && payload != null)
            {
                if (payload.Length != length) return null;
                Buffer.BlockCopy(payload, 0, packet, 9, length);
            }
            if (packet == null) return null;
            if (!Transfer(handle, packet, true, 100)) return null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                byte[] input = new byte[ReportLength];
                if (!Transfer(handle, input, false, 350)) return null;
                byte[] data;
                if (TryParseReply(input, command, offset, length, out data)) return data;
            }
            return null;
        }

        private static bool TryReadBattery(string path, out int percent, out bool charging, out int keyboardPid)
        {
            percent = -1;
            charging = false;
            keyboardPid = 0;
            const uint access = 0xC0000000;
            IntPtr handle = RazerDeviceHelper.CreateFile(path, access,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0x40000000, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
            bool startAttempted = false;
            try
            {
                try
                {
                    startAttempted = true;
                    if (Request(handle, 0x10, 0, ChunkLength) == null) return false;
                    byte[] info = new byte[56];
                    for (int offset = 0; offset < info.Length; offset += ChunkLength)
                    {
                        int length = Math.Min(ChunkLength, info.Length - offset);
                        byte[] part = Request(handle, 0x12, offset, length);
                        if (part == null) return false;
                        Buffer.BlockCopy(part, 0, info, offset, length);
                    }
                    if (!IsWave75Identity(info)) return false;
                    byte[] battery = Request(handle, 0x30, 0, ChunkLength);
                    if (battery == null || battery[0] > 100 || battery[1] > 1) return false;
                    percent = battery[0];
                    charging = battery[1] != 0;
                    keyboardPid = info[4] | (info[5] << 8);
                    return true;
                }
                finally
                {
                    // A lost start reply can still mean the receiver entered HUB mode.
                    // Treat an unacknowledged exit as a failed query, even if battery data arrived.
                    if (startAttempted && Request(handle, 0x11, 0, ChunkLength) == null)
                        throw new InvalidOperationException("WAVE75 receiver exit was not acknowledged.");
                }
            }
            catch { return false; }
            finally { RazerDeviceHelper.CloseHandle(handle); }
        }

        public static MouseBatteryInfo QueryWave75Receiver()
        {
            lock (Sync)
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devices = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devices == IntPtr.Zero || devices.ToInt64() == -1) return null;
                try
                {
                    RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA iface = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                    iface.cbSize = Marshal.SizeOf(iface);
                    uint index = 0;
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref hidGuid, index++, ref iface))
                    {
                        uint size;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devices, ref iface, IntPtr.Zero, 0, out size, IntPtr.Zero);
                        if (size == 0) continue;
                        IntPtr detail = Marshal.AllocHGlobal((int)size);
                        try
                        {
                            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 5);
                            if (!RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devices, ref iface, detail, size, out size, IntPtr.Zero)) continue;
                            string path = Marshal.PtrToStringAuto(new IntPtr(detail.ToInt64() + 4));
                            if (path == null || path.IndexOf("vid_36b0&pid_3002", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            IntPtr probe = RazerDeviceHelper.CreateFile(path, 0,
                                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                                IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
                            if (probe == IntPtr.Zero || probe.ToInt64() == -1) continue;
                            bool supported = false;
                            try
                            {
                                RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                attr.Size = Marshal.SizeOf(attr);
                                IntPtr preparsed;
                                if (RazerDeviceHelper.HidD_GetAttributes(probe, ref attr) && attr.VendorID == ReceiverVid && attr.ProductID == ReceiverPid &&
                                    RazerDeviceHelper.HidD_GetPreparsedData(probe, out preparsed))
                                {
                                    try
                                    {
                                        RazerDeviceHelper.HIDP_CAPS caps;
                                        if (RazerDeviceHelper.HidP_GetCaps(preparsed, out caps) >= 0)
                                            supported = caps.UsagePage == 0xFF60 && caps.Usage == 0x61 &&
                                                caps.InputReportByteLength == ReportLength && caps.OutputReportByteLength == ReportLength;
                                    }
                                    finally { RazerDeviceHelper.HidD_FreePreparsedData(preparsed); }
                                }
                            }
                            finally { RazerDeviceHelper.CloseHandle(probe); }
                            if (!supported) continue;
                            int percent, keyboardPid;
                            bool charging;
                            if (!TryReadBattery(path, out percent, out charging, out keyboardPid)) continue;
                            return new MouseBatteryInfo
                            {
                                DeviceId = "HID:36B0:" + keyboardPid.ToString("X4"),
                                Brand = "PMO",
                                DeviceName = "WAVE75",
                                Category = DeviceCategory.Keyboard,
                                HardwareId = "VID_36B0&PID_" + keyboardPid.ToString("X4"),
                                Transport = "2.4G 无线",
                                IsConnected = true,
                                IsDonglePresent = true,
                                BatteryPercent = percent,
                                ExactBatteryPercent = percent,
                                IsCharging = charging,
                                LastUpdated = DateTime.Now
                            };
                        }
                        finally { Marshal.FreeHGlobal(detail); iface.cbSize = Marshal.SizeOf(iface); }
                    }
                }
                finally { RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devices); }
                return null;
            }
        }

        private static string[] FindReceiverPaths()
        {
                System.Collections.Generic.List<string> paths = new System.Collections.Generic.List<string>();
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devices = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devices == IntPtr.Zero || devices.ToInt64() == -1) throw new InvalidOperationException("Cannot enumerate receivers.");
                try
                {
                    RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA iface = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                    iface.cbSize = Marshal.SizeOf(iface);
                    uint index = 0;
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref hidGuid, index++, ref iface))
                    {
                        uint size;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devices, ref iface, IntPtr.Zero, 0, out size, IntPtr.Zero);
                        if (size == 0) continue;
                        IntPtr detail = Marshal.AllocHGlobal((int)size);
                        try
                        {
                            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 5);
                            if (!RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devices, ref iface, detail, size, out size, IntPtr.Zero)) continue;
                            string path = Marshal.PtrToStringAuto(new IntPtr(detail.ToInt64() + 4));
                            if (path == null || path.IndexOf("vid_36b0&pid_3002", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            IntPtr probe = RazerDeviceHelper.CreateFile(path, 0,
                                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                                IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
                            if (probe == IntPtr.Zero || probe.ToInt64() == -1) throw new InvalidOperationException("Cannot inspect receiver capabilities; WAVE75 selection is ambiguous.");
                            bool supported = false;
                            try
                            {
                                RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                attr.Size = Marshal.SizeOf(attr);
                                IntPtr preparsed;
                                if (!RazerDeviceHelper.HidD_GetAttributes(probe, ref attr)) throw new InvalidOperationException("Cannot inspect receiver identity.");
                                if (attr.VendorID == ReceiverVid && attr.ProductID == ReceiverPid)
                                {
                                    if (!RazerDeviceHelper.HidD_GetPreparsedData(probe, out preparsed)) throw new InvalidOperationException("Cannot inspect receiver capabilities.");
                                    try
                                    {
                                        RazerDeviceHelper.HIDP_CAPS caps;
                                        if (RazerDeviceHelper.HidP_GetCaps(preparsed, out caps) < 0) throw new InvalidOperationException("Cannot inspect receiver HID capabilities.");
                                        supported = caps.UsagePage == 0xFF60 && caps.Usage == 0x61 &&
                                            caps.InputReportByteLength == ReportLength && caps.OutputReportByteLength == ReportLength;
                                    }
                                    finally { RazerDeviceHelper.HidD_FreePreparsedData(preparsed); }
                                }
                            }
                            finally { RazerDeviceHelper.CloseHandle(probe); }
                            if (!supported) continue;
                            paths.Add(path);
                        }
                        finally { Marshal.FreeHGlobal(detail); iface.cbSize = Marshal.SizeOf(iface); }
                    }
                }
                finally { RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devices); }
                return paths.ToArray();
        }

        private static Wave75Result<T> InSettingsSession<T>(Func<Wave75SettingsProtocol, T> action, string expectedPath)
        {
            lock (Sync)
            {
                string path;
                try
                {
                    path = Wave75ReceiverSelection.Select(FindReceiverPaths(), delegate(string candidate)
                    {
                        IntPtr probe = RazerDeviceHelper.CreateFile(candidate, 0xC0000000,
                            RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                            IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0x40000000, IntPtr.Zero);
                        if (probe == IntPtr.Zero || probe.ToInt64() == -1)
                            throw new InvalidOperationException("Cannot inspect a receiver; WAVE75 selection is ambiguous.");
                        return Wave75ReceiverSelection.Probe(
                            delegate(byte command, int offset, int length, byte[] payload) { return Request(probe, command, offset, length, payload); },
                            delegate { RazerDeviceHelper.CloseHandle(probe); });
                    }, expectedPath);
                }
                catch (Exception ex) { return Wave75Result<T>.Failure(ex.Message, false); }
                IntPtr handle = RazerDeviceHelper.CreateFile(path, 0xC0000000,
                    RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                    IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0x40000000, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle.ToInt64() == -1) return Wave75Result<T>.Failure("Cannot open receiver.", false);
                Wave75SettingsProtocol protocol = new Wave75SettingsProtocol(delegate(byte command, int offset, int length, byte[] payload)
                {
                    return Request(handle, command, offset, length, payload);
                });
                Wave75Result<T> result;
                bool attempted = false;
                try
                {
                    attempted = true;
                    protocol.StartAndVerifyIdentity();
                    result = Wave75Result<T>.Succeeded(action(protocol), path);
                }
                catch (Exception ex) { result = Wave75Result<T>.Failure(ex.Message, protocol.PartialWrite); }
                finally
                {
                    try { if (attempted && Request(handle, 0x11, 0, ChunkLength) == null) protocol.ExitFailed = true; }
                    catch { protocol.ExitFailed = true; }
                    finally { RazerDeviceHelper.CloseHandle(handle); }
                }
                if (protocol.ExitFailed) return Wave75Result<T>.Failure((result.Success ? "Operation completed, but " : result.Error + " ") + "receiver exit was not acknowledged; settings may have changed.", protocol.PartialWrite);
                return result;
            }
        }

        public static Wave75Result<Wave75Settings> ReadSettings()
        {
            return InSettingsSession(delegate(Wave75SettingsProtocol p) { return p.ReadSettings(); }, null);
        }

        public static Wave75Result<Wave75Settings> WriteLight(Wave75LightArea area, Wave75LightSettings value)
        {
            return InSettingsSession(delegate(Wave75SettingsProtocol p) { return p.WriteLight(area, value); }, null);
        }
        public static Wave75Result<Wave75KeyBinding> ReadKey(int layer, int matrixIndex)
        { return InSettingsSession(delegate(Wave75SettingsProtocol p) { return p.ReadKey(layer, matrixIndex); }, null); }
        public static Wave75Result<Wave75KeyBinding> WriteKey(int layer, int matrixIndex, Wave75KeyBinding value)
        { return InSettingsSession(delegate(Wave75SettingsProtocol p) { return p.WriteKey(layer, matrixIndex, value); }, null); }
        public static Wave75Result<Wave75Macro[]> ReadMacros()
        { return InSettingsSession(delegate(Wave75SettingsProtocol p) { return p.ReadMacros(); }, null); }
        public static Wave75Result<Wave75Macro[]> WriteMacros(Wave75Macro[] macros)
        { return InSettingsSession(delegate(Wave75SettingsProtocol p) { return p.WriteMacros(macros); }, null); }
        public static Wave75Result<Wave75Settings> WriteLight(Wave75LightArea area, Wave75LightSettings value, string expectedPath)
        {
            if (String.IsNullOrEmpty(expectedPath)) return Wave75Result<Wave75Settings>.Failure("Read the device before writing settings.", false);
            return InSettingsSession(delegate(Wave75SettingsProtocol p) { return p.WriteLight(area, value); }, expectedPath);
        }
        public static Wave75Result<Wave75KeyBinding> WriteKey(int layer, int matrixIndex, Wave75KeyBinding value, string expectedPath)
        {
            if (String.IsNullOrEmpty(expectedPath)) return Wave75Result<Wave75KeyBinding>.Failure("Read the device before writing settings.", false);
            return InSettingsSession(delegate(Wave75SettingsProtocol p) { return p.WriteKey(layer, matrixIndex, value); }, expectedPath);
        }
        public static Wave75Result<Wave75Macro[]> WriteMacros(Wave75Macro[] macros, string expectedPath)
        {
            if (String.IsNullOrEmpty(expectedPath)) return Wave75Result<Wave75Macro[]>.Failure("Read the device before writing settings.", false);
            return InSettingsSession(delegate(Wave75SettingsProtocol p) { return p.WriteMacros(macros); }, expectedPath);
        }
    }
}
