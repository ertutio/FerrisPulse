// ============================================================================
// WAVE75 PMO HUB settings protocol (receiver transport).
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// C# 5 / .NET 4, UTF-8 BOM. Preserve unknown device data and verify writes.
// ============================================================================
using System;

namespace RazerBatteryTray
{
    public enum Wave75LightArea { Main, Logo, Side }
    public sealed class Wave75Result<T>
    {
        public bool Success { get; private set; }
        public string Error { get; private set; }
        public bool PartialWrite { get; private set; }
        public T Value { get; private set; }
        public string TargetPath { get; private set; }
        public static Wave75Result<T> Succeeded(T value) { return new Wave75Result<T> { Success = true, Value = value }; }
        public static Wave75Result<T> Succeeded(T value, string targetPath) { return new Wave75Result<T> { Success = true, Value = value, TargetPath = targetPath }; }
        public static Wave75Result<T> Failure(string error, bool partial) { return new Wave75Result<T> { Error = error, PartialWrite = partial }; }
    }
    public sealed class Wave75Capabilities
    {
        public int ProtocolVersion, MacroSize, MainMaxBrightness, MainMaxSpeed, LogoMaxBrightness, LogoMaxSpeed, SideMaxBrightness, SideMaxSpeed;
        public bool HasMain, HasLogo, HasSide;
    }
    public sealed class Wave75LightSettings
    {
        public bool Enabled;
        public int Mode, Brightness, Speed, Colorful, ColorIndex, Red, Green, Blue, CustomIndex;
    }
    public sealed class Wave75Settings
    {
        public Wave75Capabilities Capabilities;
        public Wave75LightSettings Main, Logo, Side;
    }
    public sealed class Wave75KeyBinding
    {
        public byte Type, Code1, Code2;
    }
    public sealed class Wave75MacroEvent
    {
        // Encoded delay is in two-millisecond units; Code and Flags are protocol bytes.
        public ushort DelayUnits;
        public byte Code, Flags;
    }
    public sealed class Wave75Macro
    {
        public Wave75MacroEvent[] Events;
    }
    // Candidate probes always close their handle, including identity mismatches and failures.
    public static class Wave75ReceiverSelection
    {
        public static bool Probe(Func<byte, int, int, byte[], byte[]> transport, Action close)
        {
            if (transport == null || close == null) throw new ArgumentNullException();
            try
            {
                Wave75SettingsProtocol protocol = new Wave75SettingsProtocol(transport);
                try { return protocol.TryStartAndVerifyIdentity(); }
                finally
                {
                    byte[] exit = transport(0x11, 0, 24, null);
                    if (exit == null || exit.Length != 24) throw new InvalidOperationException("Receiver probe exit was not acknowledged.");
                }
            }
            finally { close(); }
        }
        public static void EnsureExpectedTarget(string actualPath, string expectedPath)
        {
            if (String.IsNullOrEmpty(expectedPath) || !String.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The receiver changed since settings were read; read the device again before writing.");
        }
        public static string Select(string[] candidates, Func<string, bool> probe, string expectedPath)
        {
            string selected = Select(candidates, probe);
            if (expectedPath != null) EnsureExpectedTarget(selected, expectedPath);
            return selected;
        }
        public static string Select(string[] candidates, Func<string, bool> probe)
        {
            string selected = null;
            foreach (string candidate in candidates)
            {
                if (!probe(candidate)) continue;
                if (selected != null) throw new InvalidOperationException("Multiple WAVE75 receivers found; the selected device cannot be identified unambiguously.");
                selected = candidate;
            }
            if (selected == null) throw new InvalidOperationException("No connected WAVE75 keyboard found on the available receivers.");
            return selected;
        }
    }
    // Transport returns response data without the report header, or null on failure.
    public sealed class Wave75SettingsProtocol
    {
        private readonly Func<byte, int, int, byte[], byte[]> transport;
        private Wave75Capabilities capabilities;
        public bool PartialWrite { get; private set; }
        public bool ExitFailed { get; set; }
        public Wave75SettingsProtocol(Func<byte, int, int, byte[], byte[]> transport)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            this.transport = transport;
        }
        private byte[] Request(byte cmd, int offset, int length, byte[] payload)
        {
            byte[] data = transport(cmd, offset, length, payload);
            if (data == null || data.Length != length) throw new InvalidOperationException("WAVE75 command " + cmd.ToString("X2") + " failed at offset " + offset + ".");
            return data;
        }
        private byte[] Read(byte cmd, int count)
        {
            byte[] data = new byte[count];
            for (int offset = 0; offset < count; offset += 24)
            {
                int length = Math.Min(24, count - offset);
                Buffer.BlockCopy(Request(cmd, offset, length, null), 0, data, offset, length);
            }
            return data;
        }
        public void StartAndVerifyIdentity()
        {
            if (!TryStartAndVerifyIdentity()) throw new InvalidOperationException("Connected receiver does not identify a supported WAVE75 keyboard.");
        }
        public bool TryStartAndVerifyIdentity()
        {
            Request(0x10, 0, 24, null);
            byte[] info = Read(0x12, 56);
            if (!Wave75DeviceHelper.IsWave75Identity(info)) return false;
            int version = info[8];
            capabilities = new Wave75Capabilities {
                ProtocolVersion = version, MacroSize = info[13] * 256,
                HasMain = info[14] == 1, HasLogo = info[19] == 1, HasSide = info[25] == 1,
                MainMaxBrightness = info[16] != 0 ? info[16] : 160,
                MainMaxSpeed = info[17] != 0 ? info[17] : 5,
                LogoMaxBrightness = info[31] != 0 ? info[31] : 160,
                LogoMaxSpeed = info[22] != 0 ? info[22] : (info[17] != 0 ? info[17] : 5),
                SideMaxBrightness = (version >= 2 ? info[28] : info[27]) != 0 ? (version >= 2 ? info[28] : info[27]) : 160,
                SideMaxSpeed = (version >= 2 ? info[29] : info[28]) != 0 ? (version >= 2 ? info[29] : info[28]) : (info[17] != 0 ? info[17] : 5)
            };
            return true;
        }
        private void RequireIdentity() { if (capabilities == null) throw new InvalidOperationException("WAVE75 identity must be verified first."); }
        private static Wave75LightSettings Decode(byte[] data, int start, int speedMax)
        {
            return new Wave75LightSettings { Enabled = data[start] == 0, Mode = data[start+1], Brightness = data[start+2],
                Speed = speedMax - data[start+3], Colorful = data[start+4], ColorIndex = data[start+5],
                Red = data[start+6], Green = data[start+7], Blue = data[start+8], CustomIndex = data[10] };
        }
        private Wave75Settings DecodeAll(byte[] data)
        {
            return new Wave75Settings { Capabilities = capabilities, Main = Decode(data,1,capabilities.MainMaxSpeed),
                Logo = Decode(data,11,capabilities.LogoMaxSpeed), Side = Decode(data,20,capabilities.SideMaxSpeed) };
        }
        public Wave75Settings ReadSettings() { RequireIdentity(); return DecodeAll(Read(0x14, 64)); }
        private static int Get16(byte[] data, int offset) { return data[offset] | (data[offset + 1] << 8); }
        private static void Put16(byte[] data, int offset, int value) { data[offset] = (byte)value; data[offset + 1] = (byte)(value >> 8); }
        private static byte KeyCommand(int layer, bool write)
        {
            if (layer < 0 || layer > 3) throw new ArgumentOutOfRangeException("layer");
            return (byte)(0x1A + 2 * layer + (write ? 1 : 0));
        }
        public Wave75KeyBinding ReadKey(int layer, int matrixIndex)
        {
            RequireIdentity();
            if (matrixIndex < 0 || matrixIndex >= 128) throw new ArgumentOutOfRangeException("matrixIndex");
            byte[] b = Request(KeyCommand(layer, false), matrixIndex * 3, 3, null);
            return new Wave75KeyBinding { Type = b[0], Code1 = b[1], Code2 = b[2] };
        }
        public Wave75KeyBinding WriteKey(int layer, int matrixIndex, Wave75KeyBinding value)
        {
            RequireIdentity();
            if (value == null) throw new ArgumentNullException("value");
            if (matrixIndex < 0 || matrixIndex >= 128) throw new ArgumentOutOfRangeException("matrixIndex");
            byte cmd = KeyCommand(layer, true);
            byte[] b = new byte[] { value.Type, value.Code1, value.Code2 };
            PartialWrite = true; Request(cmd, matrixIndex * 3, 3, b);
            Wave75KeyBinding actual = ReadKey(layer, matrixIndex);
            if (actual.Type != b[0] || actual.Code1 != b[1] || actual.Code2 != b[2]) throw new InvalidOperationException("WAVE75 key verification failed.");
            PartialWrite = false; return actual;
        }
        public static byte[] EncodeMacros(Wave75Macro[] macros, int capacity)
        {
            if (macros == null) throw new ArgumentNullException("macros");
            if (capacity <= 0 || macros.Length > 65535) throw new ArgumentOutOfRangeException("capacity");
            long size = 8L + 2L * macros.Length;
            foreach (Wave75Macro macro in macros)
            {
                if (macro == null || macro.Events == null || macro.Events.Length > 65535) throw new ArgumentException("Invalid macro events.");
                if (macro.Events.Length > 0) size += 4L + 4L * macro.Events.Length;
                foreach (Wave75MacroEvent ev in macro.Events) if (ev == null) throw new ArgumentException("Null macro event.");
            }
            if (size > capacity || size > 65535) throw new ArgumentException("Macro table exceeds device capacity.");
            byte[] data = new byte[(int)size]; data[0]=0xAA; data[1]=0x55; Put16(data,2,data.Length); Put16(data,4,macros.Length);
            int offset=8+2*macros.Length;
            for(int i=0;i<macros.Length;i++)
            {
                Wave75MacroEvent[] events=macros[i].Events;
                if(events.Length==0) continue;
                Put16(data,8+2*i,offset); Put16(data,offset,events.Length); offset+=4;
                foreach(Wave75MacroEvent ev in events) { Put16(data,offset,ev.DelayUnits); data[offset+2]=ev.Code; data[offset+3]=ev.Flags; offset+=4; }
            }
            return data;
        }
        public static Wave75Macro[] DecodeMacros(byte[] data)
        {
            if(data==null || data.Length<8 || data[0]!=0xAA || data[1]!=0x55) throw new ArgumentException("Invalid macro header.");
            int size=Get16(data,2), count=Get16(data,4);
            if(size<8 || size>data.Length || 8L+2L*count>size) throw new ArgumentException("Invalid macro table length.");
            Wave75Macro[] macros=new Wave75Macro[count];
            for(int i=0;i<count;i++)
            {
                int offset=Get16(data,8+2*i);
                if(offset==0) { macros[i]=new Wave75Macro { Events=new Wave75MacroEvent[0] }; continue; }
                if(offset<8+2*count || offset+4>size) throw new ArgumentException("Invalid macro offset.");
                int length=Get16(data,offset); if(offset+4L+4L*length>size) throw new ArgumentException("Invalid macro event count.");
                Wave75MacroEvent[] events=new Wave75MacroEvent[length]; offset+=4;
                for(int j=0;j<length;j++,offset+=4) events[j]=new Wave75MacroEvent { DelayUnits=(ushort)Get16(data,offset), Code=data[offset+2], Flags=data[offset+3] };
                macros[i]=new Wave75Macro { Events=events };
            }
            return macros;
        }
        public Wave75Macro[] ReadMacros()
        {
            RequireIdentity();
            if(capabilities.MacroSize<=0) throw new InvalidOperationException("Device macro capacity unavailable.");
            return DecodeMacros(Read(0x2C,capabilities.MacroSize));
        }
        public Wave75Macro[] WriteMacros(Wave75Macro[] macros)
        {
            RequireIdentity(); byte[] data=EncodeMacros(macros,capabilities.MacroSize);
            for(int offset=0;offset<data.Length;offset+=24)
            {
                int length=Math.Min(24,data.Length-offset); byte[] part=new byte[length]; Buffer.BlockCopy(data,offset,part,0,length);
                PartialWrite=true; Request(0x2D,offset,length,part);
            }
            byte[] actual=Read(0x2C,data.Length);
            for(int i=0;i<data.Length;i++) if(data[i]!=actual[i]) throw new InvalidOperationException("WAVE75 macro verification failed at byte " + i + ".");
            Wave75Macro[] result=DecodeMacros(actual); PartialWrite=false; return result;
        }
        private static void Range(int value, int max, string name)
        { if (value < 0 || value > max) throw new ArgumentOutOfRangeException(name); }
        public Wave75Settings WriteLight(Wave75LightArea area, Wave75LightSettings value)
        {
            RequireIdentity();
            if (value == null) throw new ArgumentNullException("value");
            int start, maxBrightness, maxSpeed;
            if (area == Wave75LightArea.Main) { if (!capabilities.HasMain) throw new InvalidOperationException("Main lighting is unavailable."); start=1; maxBrightness=capabilities.MainMaxBrightness; maxSpeed=capabilities.MainMaxSpeed; }
            else if (area == Wave75LightArea.Logo) { if (!capabilities.HasLogo) throw new InvalidOperationException("Logo lighting is unavailable."); start=11; maxBrightness=capabilities.LogoMaxBrightness; maxSpeed=capabilities.LogoMaxSpeed; }
            else if (area == Wave75LightArea.Side) { if (!capabilities.HasSide) throw new InvalidOperationException("Side lighting is unavailable."); start=20; maxBrightness=capabilities.SideMaxBrightness; maxSpeed=capabilities.SideMaxSpeed; }
            else throw new ArgumentOutOfRangeException("area");
            if (area == Wave75LightArea.Main ? !(value.Mode >= 1 && value.Mode <= 20 || value.Mode == 253) : !(value.Mode >= 1 && value.Mode <= 4)) throw new ArgumentOutOfRangeException("Mode");
            Range(value.Brightness,maxBrightness,"Brightness"); Range(value.Speed,maxSpeed,"Speed"); Range(value.Colorful,1,"Colorful"); Range(value.ColorIndex,255,"ColorIndex");
            Range(value.Red,255,"Red"); Range(value.Green,255,"Green"); Range(value.Blue,255,"Blue");
            if (area == Wave75LightArea.Main && value.Mode == 253) Range(value.CustomIndex,4,"CustomIndex");
            byte[] original = Read(0x14,64), desired = (byte[])original.Clone();
            desired[start]=(byte)(value.Enabled ? 0 : 1); desired[start+1]=(byte)value.Mode; desired[start+2]=(byte)value.Brightness;
            desired[start+3]=(byte)(maxSpeed-value.Speed); desired[start+4]=(byte)value.Colorful; desired[start+5]=(byte)value.ColorIndex;
            desired[start+6]=(byte)value.Red; desired[start+7]=(byte)value.Green; desired[start+8]=(byte)value.Blue;
            if (area == Wave75LightArea.Main && value.Mode == 253) desired[10]=(byte)value.CustomIndex;
            for (int offset=0; offset<64; offset+=24)
            {
                int length=Math.Min(24,64-offset); byte[] chunk=new byte[length]; Buffer.BlockCopy(desired,offset,chunk,0,length);
                // A lost ACK may mean this chunk was written, so mark before attempting it.
                PartialWrite=true; Request(0x15,offset,length,chunk);
            }
            byte[] actual=Read(0x14,64);
            for (int i=0;i<64;i++) if (actual[i]!=desired[i]) throw new InvalidOperationException("WAVE75 light write verification failed at byte " + i + ".");
            PartialWrite=false;
            return DecodeAll(actual);
        }
    }
}
