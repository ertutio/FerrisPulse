# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$temp = Join-Path $env:TEMP ("wave75-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $temp | Out-Null
try {
 $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
 $source = Join-Path $temp "test.cs"
 $code = @"
using System;
using RazerBatteryTray;
namespace RazerBatteryTray { public static class Wave75DeviceHelper { public static bool IsWave75Identity(byte[] i) { return i.Length==56 && i[2]==0xb0 && i[3]==0x36 && (i[4]==8 || i[4]==9) && i[5]==0x30 && i[8]<=2 && i[11]==103; } } }
class Test {
 static byte[] info=new byte[56], data=new byte[64]; static bool corrupt, fail, exitFail; static int writes, closes, exits; static byte[] keys=new byte[384], macros=new byte[4096];
 static byte[] Send(byte c,int o,int n,byte[] p) {
  if(c==16)return new byte[n];
  if(c==17){exits++;return exitFail?null:new byte[n];}
  if(c==18){byte[] b=new byte[n];Array.Copy(info,o,b,0,n);return b;}
  if(c==20){byte[] b=new byte[n];Array.Copy(data,o,b,0,n);if(corrupt&&writes>0&&o==0)b[1]^=1;return b;}
  if(c==21){writes++;if(fail)return null;Array.Copy(p,0,data,o,n);return new byte[n];}
  if(c==26){byte[] b=new byte[n];Array.Copy(keys,o,b,0,n);return b;}
  if(c==27){Array.Copy(p,0,keys,o,n);return new byte[n];}
  if(c==44){byte[] b=new byte[n];Array.Copy(macros,o,b,0,n);return b;}
  if(c==45){Array.Copy(p,0,macros,o,n);return new byte[n];}
  throw new Exception("unexpected command");
 }
 static void Assert(bool b,string s){if(!b)throw new Exception(s);}
 static void Main(){
  info[2]=0xb0;info[3]=0x36;info[4]=9;info[5]=0x30;info[8]=2;info[11]=103;info[13]=16;info[14]=1;info[16]=160;info[17]=4;info[19]=1;info[22]=4;info[25]=1;info[28]=160;info[29]=4;info[31]=160;
  for(int i=0;i<64;i++)data[i]=(byte)(i+20);data[1]=0;data[2]=1;data[3]=100;data[4]=2;
  Wave75SettingsProtocol p=new Wave75SettingsProtocol(Send);p.StartAndVerifyIdentity();Wave75Settings s=p.ReadSettings();Assert(s.Capabilities.MacroSize==4096&&s.Main.Speed==2,"decode");
  byte[] before=(byte[])data.Clone();Wave75LightSettings l=new Wave75LightSettings{Enabled=true,Mode=4,Brightness=80,Speed=3,Colorful=0,ColorIndex=2,Red=3,Green=4,Blue=5};
  s=p.WriteLight(Wave75LightArea.Main,l);Assert(writes==3&&s.Main.Mode==4&&!p.PartialWrite,"write");
  for(int i=0;i<64;i++)if(i<1||i>9)Assert(data[i]==before[i],"unrelated data");
  corrupt=true;try{p.WriteLight(Wave75LightArea.Main,l);throw new Exception("missing verification error");}catch(InvalidOperationException){Assert(p.PartialWrite,"verification failure");}
  corrupt=false;fail=true;try{p.WriteLight(Wave75LightArea.Main,l);throw new Exception("missing ACK failure");}catch(InvalidOperationException){Assert(p.PartialWrite,"ACK failure");}
  fail=false; Wave75KeyBinding k=p.WriteKey(0,127,new Wave75KeyBinding{Type=16,Code1=2,Code2=3});Assert(k.Code2==3&&keys[381]==16,"key");
  Wave75Macro[] table=new Wave75Macro[]{new Wave75Macro{Events=new Wave75MacroEvent[]{new Wave75MacroEvent{DelayUnits=5,Code=4,Flags=129}}},new Wave75Macro{Events=new Wave75MacroEvent[0]}};
  Wave75Macro[] result=p.WriteMacros(table);Assert(result.Length==2&&result[0].Events[0].DelayUnits==5&&p.ReadMacros()[0].Events[0].Flags==129,"macro roundtrip");
  try{Wave75SettingsProtocol.DecodeMacros(new byte[]{170,85,8,0,1,0,0,0});throw new Exception("bad table accepted");}catch(ArgumentException){}
  info[11]=0;p=new Wave75SettingsProtocol(Send);try{p.StartAndVerifyIdentity();throw new Exception("identity accepted");}catch(InvalidOperationException){}
  string selected=Wave75ReceiverSelection.Select(new string[]{"other","wave"},delegate(string name){info[11]=(byte)(name=="wave"?103:0);return Wave75ReceiverSelection.Probe(Send,delegate{closes++;});});
  Assert(selected=="wave"&&closes==2&&exits==2,"scan and cleanup");
  try{Wave75ReceiverSelection.Select(new string[]{"wave1","wave2"},delegate(string name){info[11]=103;return Wave75ReceiverSelection.Probe(Send,delegate{closes++;});});throw new Exception("ambiguous receivers accepted");}catch(InvalidOperationException){}
  Assert(closes==4&&exits==4,"ambiguous cleanup");
  exitFail=true;try{Wave75ReceiverSelection.Probe(Send,delegate{closes++;});throw new Exception("exit failure accepted");}catch(InvalidOperationException){} Assert(closes==5&&exits==5,"exit failure cleanup");
  exitFail=false; int errorClose=0,errorExit=0;
  try{Wave75ReceiverSelection.Probe(delegate(byte c,int o,int len,byte[] payload){if(c==17){errorExit++;return new byte[len];} return null;},delegate{errorClose++;});throw new Exception("probe failure accepted");}catch(InvalidOperationException){}
  Assert(errorClose==1&&errorExit==1,"probe failure cleanup");
  int attemptedWrite=0;
  try { string target=Wave75ReceiverSelection.Select(new string[]{"B"},delegate(string x){return true;},"A"); if(target!=null) attemptedWrite++; throw new Exception("changed receiver accepted"); } catch(InvalidOperationException) {}
  Assert(attemptedWrite==0,"changed receiver wrote settings");
  Assert(Wave75ReceiverSelection.Select(new string[]{"PaTh-A"},delegate(string x){return true;},"path-a")=="PaTh-A","case insensitive receiver path");
  try { Wave75ReceiverSelection.EnsureExpectedTarget("A", ""); throw new Exception("empty target accepted"); } catch(InvalidOperationException) {}
  Console.WriteLine("Wave75 simulated settings tests passed");
 }
}
"@
 [System.IO.File]::WriteAllText($source,$code,(New-Object System.Text.UTF8Encoding($true)))
 $exe = Join-Path $temp "test.exe"; & $csc /nologo /codepage:65001 /out:$exe $source (Join-Path $root "src\Hardware\Wave75Settings.cs"); if ($LASTEXITCODE -ne 0) { throw "Test compilation failed" }
 & $exe; if ($LASTEXITCODE -ne 0) { throw "Test failed" }
} finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }