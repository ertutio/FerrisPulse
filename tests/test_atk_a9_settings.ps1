# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Pure ATK settings tests; never enumerate/open HID or run the application.
param([string]$AssemblyPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "FerrisPulse.exe"))
$ErrorActionPreference = "Stop"
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
[void][System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($AssemblyPath))
Add-Type -ReferencedAssemblies $AssemblyPath -TypeDefinition @'
using System;
using System.IO;
using RazerBatteryTray;
public sealed class AtkA9MemoryFixture
{
    public byte[] Memory = new byte[6000];
    public int Writes;
    public bool FailAfterWrite, BadStatus, BadChecksum, BadIdentity;
    public bool IgnoreWrites;
    public int FailBeforeWriteNumber;
    public AtkA9SettingsHelper Helper;
    public AtkA9MemoryFixture()
    {
        for(int i=0;i<10;i+=2) { Memory[i]=(byte)(i+1); Memory[i+1]=(byte)(0x55-Memory[i]); }
        Memory[2]=2; Memory[3]=0x53; Memory[4]=0; Memory[5]=0x55;
        for(int i=0;i<8;i++) Array.Copy(AtkA9SettingsHelper.EncodeDpi(new AtkA9DpiStage { X=800*(i+1),Y=800*(i+1) }),0,Memory,12+4*i,4);
        Array.Copy(AtkA9SettingsHelper.EncodeButton(AtkA9ButtonAction.Mouse(8)),0,Memory,108,4);
        Array.Copy(AtkA9SettingsHelper.EncodeButton(AtkA9ButtonAction.Mouse(16)),0,Memory,112,4);
        for(int i=0;i<12;i++) Array.Copy(AtkA9SettingsHelper.EncodeMacro(new AtkA9Macro { Events=new AtkA9MacroEvent[0] }),0,Memory,768+384*i,33);
        Helper=new AtkA9SettingsHelper(Exchange);
    }
    public byte[] Exchange(byte[] tx)
    {
        if(tx.Length!=17 || tx[0]!=8 || AtkA9SettingsHelper.Checksum(tx,0,17)!=0) throw new Exception("Invalid outgoing frame");
        byte[] rx=(byte[])tx.Clone(); int cmd=tx[1],address=(tx[3]<<8)|tx[4],n=tx[5];
        rx[2]=(byte)(BadStatus?1:0);
        if(cmd==16) {rx[5]=2;rx[6]=(byte)(BadIdentity?2:1);rx[7]=3;}
        else if(cmd==8) Array.Copy(Memory,address,rx,6,n);
        else if(cmd==7)
        {
            if(address>=768 && ((address-768)%384)+n>384) throw new Exception("Macro crossed slot boundary");
            Writes++;
            if(Writes==FailBeforeWriteNumber) throw new IOException("Simulated failure before second write chunk");
            if(!IgnoreWrites) Array.Copy(tx,6,Memory,address,n);
            if(FailAfterWrite) throw new IOException("Simulated lost write acknowledgement");
        }
        else throw new Exception("Unexpected command");
        rx[16]=AtkA9SettingsHelper.Checksum(rx,0,16);if(BadChecksum)rx[16]^=1;return rx;
    }
    public static void Run()
    {
        byte[] request=AtkA9SettingsHelper.BuildRequest(8,0,10,null);
        Require(request[0]==8 && request[1]==8 && request[5]==10 && request[16]==0x3B,"Independent EEPROM request vector");
        MustFail(delegate{AtkA9SettingsHelper.OpenConnected(0x373B,0x1118);},"Unconfirmed 1118 settings rejected before HID enumeration");
        MustFail(delegate{AtkA9SettingsHelper.OpenConnected(0x373B,0x101B);},"Shared receiver rejected before HID enumeration");
        int[] values={10,800,1600,10000,10050,30000,30100,42000};
        foreach(int x in values) foreach(int y in values)
        {
            AtkA9DpiStage d=AtkA9SettingsHelper.DecodeDpi(AtkA9SettingsHelper.EncodeDpi(new AtkA9DpiStage{X=x,Y=y}));
            Require(d.X==x && d.Y==y,"DPI axis roundtrip");
        }
        byte[] expected=AtkA9SettingsHelper.EncodeDpi(new AtkA9DpiStage{X=42000,Y=42000});
        Require(expected[0]==0xDB && expected[1]==0xDB && expected[2]==0x33,"3950Ultra high-DPI encoding");
        byte[] simple=AtkA9SettingsHelper.EncodeMacro(new AtkA9Macro{Events=new AtkA9MacroEvent[]{new AtkA9MacroEvent{Type=1,State=128,Value1=4,Delay=1}}});
        Require(simple.Length==38 && simple[31]==1 && simple[32]==0x81 && simple[33]==4 && simple[35]==0 && simple[36]==1 && simple[37]==0xCE,"Independent raw macro event vector");
        MustFail(delegate{AtkA9SettingsHelper.EncodeDpi(new AtkA9DpiStage{X=10010,Y=800});},"Invalid DPI quantization");
        MustFail(delegate{AtkA9SettingsHelper.EncodeDpi(new AtkA9DpiStage{X=42001,Y=800});},"DPI range");
        MustFail(delegate{AtkA9SettingsHelper.EncodeButton(AtkA9ButtonAction.Macro(12,1));},"Macro binding slot range");
        MustFail(delegate{AtkA9SettingsHelper.EncodeButton(AtkA9ButtonAction.Mouse(3));},"Unsupported button combination");
        AtkA9MemoryFixture tooMany=new AtkA9MemoryFixture();
        using(tooMany.Helper)
        {
            bool rejected=false;
            try{tooMany.Helper.SaveMacro(0,new AtkA9Macro{Events=new AtkA9MacroEvent[71]});}
            catch(ArgumentException){rejected=true;}
            Require(rejected && tooMany.Writes==0,"71-event macro rejected before any EEPROM write");
        }
        AtkA9MemoryFixture mismatch=new AtkA9MemoryFixture();
        using(mismatch.Helper)
        {
            mismatch.IgnoreWrites=true;
            bool partial=false,readbackMismatch=false;
            try{mismatch.Helper.SaveSideButton(0,AtkA9ButtonAction.Dpi(1));}
            catch(AtkA9WriteException e)
            {
                partial=e.MayHavePartialWrite;
                readbackMismatch=e.InnerException is IOException && e.InnerException.Message.IndexOf("readback differs",StringComparison.OrdinalIgnoreCase)>=0;
            }
            Require(partial && readbackMismatch && mismatch.Writes==1,"Successful write ACK with stale readback reports partial-write failure without retry");
            Require(mismatch.Memory[108]==1 && mismatch.Memory[109]==8,"Readback mismatch leaves original device record visible");
        }
        AtkA9MemoryFixture secondChunk=new AtkA9MemoryFixture();
        using(secondChunk.Helper)
        {
            const int slotAddress=768+384*2;
            for(int i=0;i<384;i++)secondChunk.Memory[slotAddress+i]=0xCC;
            byte nextSlot=secondChunk.Memory[slotAddress+384];
            secondChunk.FailBeforeWriteNumber=2;
            bool partial=false;
            try{secondChunk.Helper.SaveMacro(2,new AtkA9Macro{Events=new AtkA9MacroEvent[]{new AtkA9MacroEvent{Type=1,State=128,Value1=4,Delay=1}}});}
            catch(AtkA9WriteException e){partial=e.MayHavePartialWrite && e.InnerException is IOException;}
            Require(partial && secondChunk.Writes==2,"Second chunk failure reports partial write and stops without retry or rollback");
            Require(secondChunk.Memory[slotAddress]==8 && secondChunk.Memory[slotAddress+1]==2 && secondChunk.Memory[slotAddress+9]==255,"First macro chunk remains written after second chunk fails");
            for(int i=10;i<384;i++)Require(secondChunk.Memory[slotAddress+i]==0xCC,"Failed and subsequent macro chunks remain untouched");
            Require(secondChunk.Memory[slotAddress+384]==nextSlot,"Second chunk failure does not affect next slot");
        }
        AtkA9MemoryFixture f=new AtkA9MemoryFixture();
        using(f.Helper)
        {
            Require(f.Helper.ReadSettings().SideButtons[0].Value1==8,"Side1 default backward");
            byte[] before=(byte[])f.Memory.Clone();
            AtkA9Settings saved=f.Helper.SaveDpi(new AtkA9DpiStage[]{new AtkA9DpiStage{X=10050,Y=42000},new AtkA9DpiStage{X=10,Y=30000},new AtkA9DpiStage{X=800,Y=1600}},2);
            Require(saved.ActiveStage==2 && saved.Stages.Length==3 && saved.Stages[0].Y==42000,"DPI save readback");
            foreach(int i in new int[]{0,1,6,7,8,9,24,25,26,27,108,112})Require(f.Memory[i]==before[i],"Unrelated EEPROM bytes preserved");
            saved=f.Helper.SaveSideButton(0,AtkA9ButtonAction.Dpi(1));
            Require(saved.SideButtons[0].Class==2 && saved.SideButtons[1].Value1==16,"Side button update scope");
            AtkA9MacroEvent[] events=new AtkA9MacroEvent[]{new AtkA9MacroEvent{Type=4,State=128,Value1=8,Value2=2,Delay=65535},new AtkA9MacroEvent{Type=4,State=64,Value1=8,Delay=10},new AtkA9MacroEvent{Type=6,State=0,Value1=255,Delay=1}};
            AtkA9Macro macro=new AtkA9Macro{Events=events};
            AtkA9Macro read=f.Helper.SaveMacro(11,macro);
            Require(read.Events.Length==3 && read.Events[0].Type==4 && read.Events[0].Value2==2 && read.Events[0].Delay==65535,"Odd macro count and raw type preserved");
            Require(f.Helper.ReadMacros().Length==12,"Macro table size");
            byte[] corrupt=AtkA9SettingsHelper.EncodeMacro(macro);corrupt[32]^=1;
            MustFail(delegate{AtkA9SettingsHelper.DecodeMacro(corrupt);},"Macro checksum validation");
            AtkA9MacroEvent[] maximum=new AtkA9MacroEvent[70];for(int i=0;i<70;i++)maximum[i]=new AtkA9MacroEvent{Type=1,State=128,Value1=4,Delay=1};
            byte next=f.Memory[768+384];f.Helper.SaveMacro(0,new AtkA9Macro{Events=maximum});
            Require(f.Memory[768+384]==next,"70-event macro does not overwrite next slot");
            f.Helper.BindMacro(1,11,254);Require(f.Helper.ReadSettings().SideButtons[1].Value2==254,"Macro binding mode");
            f.BadIdentity=true;int writes=f.Writes;MustFail(delegate{f.Helper.SaveSideButton(0,AtkA9ButtonAction.Mouse(8));},"CID/MID mismatch");Require(f.Writes==writes,"No writes on wrong identity");f.BadIdentity=false;
            f.BadStatus=true;MustFail(delegate{f.Helper.ReadSettings();},"Error status rejected");f.BadStatus=false;
            f.BadChecksum=true;MustFail(delegate{f.Helper.ReadSettings();},"Frame checksum rejected");f.BadChecksum=false;
            f.FailAfterWrite=true;bool partial=false;
            try{f.Helper.SaveSideButton(0,AtkA9ButtonAction.Mouse(8));}catch(AtkA9WriteException e){partial=e.MayHavePartialWrite;}
            Require(partial,"Lost acknowledgement reports possible partial write");
        }
    }
    private static void Require(bool condition,string text){if(!condition)throw new Exception(text);}
    private static void MustFail(Action action,string text){bool failed=false;try{action();}catch{failed=true;}Require(failed,text);}
}
'@
[AtkA9MemoryFixture]::Run()
Write-Output "ATK A9 settings pure tests passed (no device access)."
