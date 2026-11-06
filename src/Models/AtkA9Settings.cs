// ATK A9 Ultimate V3-confirmed settings models. C#5 / UTF-8 BOM.
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️ preserve protocol fields and C#5 compatibility.
using System;
using System.IO;
namespace RazerBatteryTray
{
    public sealed class AtkA9DpiStage { public int X { get; set; } public int Y { get; set; } }
    public sealed class AtkA9ButtonAction
    {
        public int Class { get; set; }
        public int Value1 { get; set; }
        public int Value2 { get; set; }
        public static AtkA9ButtonAction Mouse(int bit) { return new AtkA9ButtonAction { Class = 1, Value1 = bit }; }
        public static AtkA9ButtonAction Dpi(int action) { return new AtkA9ButtonAction { Class = 2, Value1 = action }; }
        public static AtkA9ButtonAction Macro(int slot, int mode) { return new AtkA9ButtonAction { Class = 6, Value1 = slot, Value2 = mode }; }
    }
    public sealed class AtkA9Settings
    {
        public int Cid { get; set; }
        public int Mid { get; set; }
        public int ActiveStage { get; set; }
        public AtkA9DpiStage[] Stages { get; set; }
        public AtkA9ButtonAction[] SideButtons { get; set; }
    }
    public sealed class AtkA9MacroEvent
    {
        // State=128 down,64 up,0 scroll; Type is the raw three-bit event type.
        public int Type { get; set; }
        public int State { get; set; }
        public int Value1 { get; set; }
        public int Value2 { get; set; }
        public int Delay { get; set; }
    }
    public sealed class AtkA9Macro { public AtkA9MacroEvent[] Events { get; set; } }
    public sealed class AtkA9WriteException : IOException
    {
        public bool MayHavePartialWrite { get { return true; } }
        public AtkA9WriteException(string operation, Exception inner)
            : base(operation + " failed; device may contain a partial change. Read it again before retrying. " + inner.Message, inner) { }
    }
}
