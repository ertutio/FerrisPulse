// ATK A9 Ultimate settings, independently implemented from ATK HUB 3.2.31 protocol facts.
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️ C#5, UTF-8 BOM; no firmware commands.
using System;
using System.Collections.Generic;
using System.IO;
namespace RazerBatteryTray
{
    public sealed class AtkA9SettingsHelper : IDisposable
    {
        private static readonly object SettingsLock = new object();
        private Func<byte[], byte[]> exchange;
        private IntPtr handle;
        private bool disposed;
        public AtkA9SettingsHelper(string devicePath, int vid, int pid)
        {
            handle = UniversalGamingDeviceHelper.OpenAtkSettingsHandle(devicePath, vid, pid);
            exchange = delegate(byte[] request) { return UniversalGamingDeviceHelper.ExchangeAtkSettingsReport(handle, request); };
            try { CheckIdentity(); } catch { Dispose(); throw; }
        }
        // Deterministic transport injection for tests; constructing this overload never touches HID.
        public AtkA9SettingsHelper(Func<byte[], byte[]> transport)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            exchange = transport;
        }
        public static AtkA9SettingsHelper OpenConnected(int vid, int pid)
        {
            List<string> paths = UniversalGamingDeviceHelper.FindAtkSettingsPaths(vid, pid);
            if (paths.Count == 0) throw new IOException("No matching ATK A9 Ultimate settings interface is connected.");
            if (paths.Count != 1) throw new IOException("Multiple matching ATK A9 Ultimate interfaces exist; select by a unique device path.");
            return new AtkA9SettingsHelper(paths[0], vid, pid);
        }
        private void CheckIdentity()
        {
            byte[] data = Request(0x10, 0, 0, null);
            if (data[5] < 2 || data[6] != 1 || data[7] != 3) throw new NotSupportedException("The connected device is not confirmed A9 Ultimate CID/MID 1,3.");
        }
        public static byte[] BuildRequest(int command, int address, int length, byte[] data)
        {
            if (command != 7 && command != 8 && command != 16) throw new ArgumentOutOfRangeException("command");
            if (address < 0 || address > 65535 || length < 0 || length > 10 || (data != null && data.Length != length)) throw new ArgumentException("Invalid EEPROM request.");
            byte[] frame = new byte[17];
            frame[0] = 8; frame[1] = (byte)command; frame[3] = (byte)(address >> 8); frame[4] = (byte)address; frame[5] = (byte)length;
            if (data != null) Array.Copy(data, 0, frame, 6, length);
            frame[16] = Checksum(frame, 0, 16);
            return frame;
        }
        public static byte Checksum(byte[] bytes, int start, int count)
        {
            int sum = 0; for (int i = start; i < start + count; i++) sum += bytes[i];
            return (byte)((0x55 - sum) & 255);
        }
        private byte[] Request(int cmd, int address, int length, byte[] data)
        {
            if (disposed) throw new ObjectDisposedException("AtkA9SettingsHelper");
            byte[] request = BuildRequest(cmd, address, length, data);
            byte[] reply = exchange(request);
            if (reply == null || reply.Length != 17 || reply[0] != 8 || reply[1] != cmd || reply[2] != 0 ||
                reply[3] != request[3] || reply[4] != request[4] || reply[5] > 10 || Checksum(reply, 0, 17) != 0)
                throw new IOException("Invalid ATK response (identity, status, address, length or checksum).");
            if (cmd == 8 && reply[5] != length) throw new IOException("Incomplete ATK EEPROM read.");
            return reply;
        }
        private byte[] Read(int address, int count)
        {
            byte[] result = new byte[count];
            for (int offset = 0; offset < count; offset += 10)
            {
                int n = Math.Min(10, count - offset);
                byte[] reply = Request(8, address + offset, n, null);
                Array.Copy(reply, 6, result, offset, n);
            }
            return result;
        }
        private void WriteVerified(int address, byte[] bytes)
        {
            for (int offset = 0; offset < bytes.Length; offset += 10)
            {
                int n = Math.Min(10, bytes.Length - offset);
                byte[] chunk = new byte[n]; Array.Copy(bytes, offset, chunk, 0, n);
                Request(7, address + offset, n, chunk);
            }
            byte[] actual = Read(address, bytes.Length);
            for (int i = 0; i < bytes.Length; i++) if (actual[i] != bytes[i]) throw new IOException("ATK write readback differs at EEPROM address " + (address + i));
        }
        private static void VerifyRecord(byte[] bytes, int offset, int count)
        {
            if (Checksum(bytes, offset, count) != 0) throw new IOException("ATK EEPROM record checksum failed.");
        }
        private static int EncodeAxis(int dpi, out int flags)
        {
            if (dpi < 10 || dpi > 42000) throw new ArgumentOutOfRangeException("dpi", "DPI must be 10..42000.");
            int n;
            if (dpi <= 10000) { if (dpi % 10 != 0) throw new ArgumentException("DPI step is 10."); n = dpi / 10 - 1; flags = 0; }
            else if (dpi <= 30000) { if (dpi % 50 != 0) throw new ArgumentException("DPI step is 50."); n = (dpi - 10050) / 50; flags = 2; }
            else { if (dpi % 100 != 0) throw new ArgumentException("DPI step is 100."); n = (dpi / 2 - 10050) / 50; flags = 3; }
            flags |= ((n >> 8) & 3) << 2; return n & 255;
        }
        public static byte[] EncodeDpi(AtkA9DpiStage stage)
        {
            if (stage == null) throw new ArgumentNullException("stage");
            int xFlags, yFlags;
            byte[] b = new byte[4]; b[0] = (byte)EncodeAxis(stage.X, out xFlags); b[1] = (byte)EncodeAxis(stage.Y, out yFlags);
            b[2] = (byte)(xFlags | (yFlags << 4)); b[3] = Checksum(b, 0, 3); return b;
        }
        public static AtkA9DpiStage DecodeDpi(byte[] record)
        {
            if (record == null || record.Length != 4) throw new ArgumentException("DPI record must be 4 bytes.");
            VerifyRecord(record, 0, 4);
            int x = record[0] | (((record[2] >> 2) & 3) << 8), y = record[1] | (((record[2] >> 6) & 3) << 8);
            x = (record[2] & 2) != 0 ? 50 * x + 10050 : 10 * (x + 1);
            y = (record[2] & 32) != 0 ? 50 * y + 10050 : 10 * (y + 1);
            if ((record[2] & 1) != 0) x *= 2; if ((record[2] & 16) != 0) y *= 2;
            if (x < 10 || x > 42000 || y < 10 || y > 42000) throw new IOException("DPI record exceeds sensor limits.");
            return new AtkA9DpiStage { X = x, Y = y };
        }
        public AtkA9Settings ReadSettings()
        {
            lock (SettingsLock)
            {
                CheckIdentity(); byte[] config = Read(0, 10);
                for (int n = 0; n < 10; n += 2) VerifyRecord(config, n, 2);
                int count = config[2], active = config[4];
                if (count < 1 || count > 8 || active >= count) throw new IOException("Invalid ATK DPI stage configuration.");
                byte[] dpi = Read(12, 32); AtkA9DpiStage[] stages = new AtkA9DpiStage[count];
                for (int i = 0; i < count; i++) { byte[] b = new byte[4]; Array.Copy(dpi, i * 4, b, 0, 4); stages[i] = DecodeDpi(b); }
                byte[] sides = Read(108, 8); AtkA9ButtonAction[] buttons = new AtkA9ButtonAction[2];
                for (int i = 0; i < 2; i++) { VerifyRecord(sides, i * 4, 4); buttons[i] = new AtkA9ButtonAction { Class = sides[i * 4], Value1 = sides[i * 4 + 1], Value2 = sides[i * 4 + 2] }; }
                return new AtkA9Settings { Cid = 1, Mid = 3, ActiveStage = active, Stages = stages, SideButtons = buttons };
            }
        }
        public AtkA9Settings SaveDpi(AtkA9DpiStage[] stages, int activeStage)
        {
            if (stages == null || stages.Length < 1 || stages.Length > 8 || activeStage < 0 || activeStage >= stages.Length) throw new ArgumentException("Choose 1..8 DPI stages and a valid zero-based active stage.");
            byte[][] records = new byte[stages.Length][];
            for (int i = 0; i < stages.Length; i++) records[i] = EncodeDpi(stages[i]);
            lock (SettingsLock)
            {
                CheckIdentity(); byte[] config = Read(0, 10);
                for (int n = 0; n < 10; n += 2) VerifyRecord(config, n, 2);
                config[2] = (byte)stages.Length; config[3] = Checksum(config, 2, 1); config[4] = (byte)activeStage; config[5] = Checksum(config, 4, 1);
                try
                {
                    // Four official 8B pair addresses; keep unused stage bytes intact.
                    for (int i = 0; i < records.Length; i += 2)
                    {
                        byte[] pair = Read(12 + i * 4, 8); Array.Copy(records[i], 0, pair, 0, 4);
                        if (i + 1 < records.Length) Array.Copy(records[i + 1], 0, pair, 4, 4);
                        WriteVerified(12 + i * 4, pair);
                    }
                    WriteVerified(0, config);
                    AtkA9Settings actual = ReadSettings();
                    if (actual.ActiveStage != activeStage || actual.Stages.Length != stages.Length) throw new IOException("DPI configuration readback differs.");
                    for (int i = 0; i < stages.Length; i++) if (actual.Stages[i].X != stages[i].X || actual.Stages[i].Y != stages[i].Y) throw new IOException("DPI stage readback differs.");
                    return actual;
                }
                catch (Exception e) { throw new AtkA9WriteException("DPI update", e); }
            }
        }
        public static byte[] EncodeButton(AtkA9ButtonAction action)
        {
            if (action == null) throw new ArgumentNullException("action");
            bool valid = action.Class == 0 && action.Value1 == 0 && action.Value2 == 0;
            valid |= action.Class == 1 && (action.Value1 == 1 || action.Value1 == 2 || action.Value1 == 4 || action.Value1 == 8 || action.Value1 == 16) && action.Value2 == 0;
            valid |= action.Class == 2 && action.Value1 >= 1 && action.Value1 <= 3 && action.Value2 == 0;
            valid |= action.Class == 6 && action.Value1 >= 0 && action.Value1 < 12 && ((action.Value2 >= 1 && action.Value2 <= 252) || action.Value2 == 253 || action.Value2 == 254 || action.Value2 == 255);
            if (!valid) throw new ArgumentException("Unsupported side button action.");
            byte[] b = new byte[] { (byte)action.Class, (byte)action.Value1, (byte)action.Value2, 0 }; b[3] = Checksum(b, 0, 3); return b;
        }
        public AtkA9Settings SaveSideButton(int sideIndex, AtkA9ButtonAction action)
        {
            if (sideIndex < 0 || sideIndex > 1) throw new ArgumentOutOfRangeException("sideIndex");
            byte[] bytes = EncodeButton(action);
            lock (SettingsLock)
            {
                CheckIdentity();
                try { WriteVerified(108 + sideIndex * 4, bytes); return ReadSettings(); }
                catch (Exception e) { throw new AtkA9WriteException("Side button update", e); }
            }
        }
        public AtkA9Settings BindMacro(int sideIndex, int slot, int repeatMode) { return SaveSideButton(sideIndex, AtkA9ButtonAction.Macro(slot, repeatMode)); }
        public static byte[] EncodeMacro(AtkA9Macro macro)
        {
            if (macro == null || macro.Events == null || macro.Events.Length > 70) throw new ArgumentException("A macro must have at most 70 expanded device events.");
            byte[] bytes = new byte[33 + 5 * macro.Events.Length]; bytes[0] = 8;
            for (int i = 1; i <= 8; i++) bytes[i] = 2; for (int i = 9; i < 31; i++) bytes[i] = 255;
            bytes[31] = (byte)macro.Events.Length;
            for (int i = 0; i < macro.Events.Length; i++)
            {
                AtkA9MacroEvent e = macro.Events[i];
                if (e == null || e.Type < 0 || e.Type > 6 || (e.State != 0 && e.State != 64 && e.State != 128) || e.Value1 < 0 || e.Value1 > 255 || e.Value2 < 0 || e.Value2 > 255 || e.Delay < 0 || e.Delay > 65535) throw new ArgumentException("Invalid raw macro event.");
                int p = 32 + i * 5; bytes[p] = (byte)(e.State | e.Type); bytes[p + 1] = (byte)e.Value1; bytes[p + 2] = (byte)e.Value2; bytes[p + 3] = (byte)(e.Delay >> 8); bytes[p + 4] = (byte)e.Delay;
            }
            bytes[bytes.Length - 1] = Checksum(bytes, 31, bytes.Length - 32); return bytes;
        }
        public static AtkA9Macro DecodeMacro(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 33 || bytes[31] > 70 || bytes.Length != 33 + bytes[31] * 5) throw new IOException("Invalid macro size.");
            if (bytes[0] != 8) throw new IOException("Invalid macro header.");
            for (int i = 1; i <= 8; i++) if (bytes[i] != 2) throw new IOException("Invalid macro header.");
            for (int i = 9; i < 31; i++) if (bytes[i] != 255) throw new IOException("Invalid macro header.");
            VerifyRecord(bytes, 31, bytes.Length - 31);
            AtkA9MacroEvent[] events = new AtkA9MacroEvent[bytes[31]];
            for (int i = 0; i < events.Length; i++)
            {
                int p = 32 + i * 5, state = bytes[p] & 192, type = bytes[p] & 7;
                if ((bytes[p] & 56) != 0 || state == 192 || type > 6) throw new IOException("Invalid macro event bits.");
                events[i] = new AtkA9MacroEvent { State = state, Type = type, Value1 = bytes[p + 1], Value2 = bytes[p + 2], Delay = (bytes[p + 3] << 8) | bytes[p + 4] };
            }
            return new AtkA9Macro { Events = events };
        }
        private AtkA9Macro ReadMacro(int slot)
        {
            int address = 768 + 384 * slot; byte[] header = Read(address, 32);
            if (header[31] > 70) throw new IOException("Macro event count exceeds 70.");
            byte[] bytes = new byte[33 + header[31] * 5]; Array.Copy(header, bytes, 32);
            Array.Copy(Read(address + 32, bytes.Length - 32), 0, bytes, 32, bytes.Length - 32);
            return DecodeMacro(bytes);
        }
        public AtkA9Macro[] ReadMacros()
        {
            lock (SettingsLock) { CheckIdentity(); AtkA9Macro[] macros = new AtkA9Macro[12]; for (int i = 0; i < 12; i++) macros[i] = ReadMacro(i); return macros; }
        }
        public AtkA9Macro SaveMacro(int slot, AtkA9Macro macro)
        {
            if (slot < 0 || slot >= 12) throw new ArgumentOutOfRangeException("slot");
            byte[] bytes = EncodeMacro(macro);
            lock (SettingsLock)
            {
                CheckIdentity();
                try
                {
                    // Official firmware writes ten-byte chunks, including zero-padded final chunk.
                    byte[] padded = new byte[Math.Min(384, ((bytes.Length + 9) / 10) * 10)]; Array.Copy(bytes, padded, bytes.Length);
                    WriteVerified(768 + 384 * slot, padded); return ReadMacro(slot);
                }
                catch (Exception e) { throw new AtkA9WriteException("Macro update", e); }
            }
        }
        public void Dispose()
        {
            lock (SettingsLock)
            {
                if (disposed) return; disposed = true;
                if (handle != IntPtr.Zero) { RazerDeviceHelper.CloseHandle(handle); handle = IntPtr.Zero; }
            }
            GC.SuppressFinalize(this);
        }
    }
}
