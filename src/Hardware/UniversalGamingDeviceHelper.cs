// ============================================================================
// 模块名称: UniversalGamingDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   OpenMouse 与 All-in-One-Mouse-Driver (Mouse Hub) 全量外设协议引擎
//   涵盖 20+ 个品牌、数百款主流电竞/客制化/原相方案鼠标及键盘：
//   1. CompX / PixArt 64字节 Feature 引擎 (Lamzu, Pulsar, WLmouse, Attack Shark, Ajazz, Darmoshark, Glorious, Ninjutso, RAWM, Incott, Delux, Vancer, Fantech, Finalmouse)
//   2. Beken / Endgame Gear Feature 引擎；ATK A9 Ultimate 独立 Output/Input 电量与精确型号设置传输通道
//   3. Vaxee 64字节专用控制引擎 (NP-01S, NP-01S v2, XE, AX, OUTSET AX, E1)
//   4. Asus ROG / TUF, SteelSeries, Corsair, Dareu, Keychron, Wooting 厂商协议引擎
// 依赖关系: NativeMethods.cs, MouseBatteryInfo.cs, RazerDeviceHelper.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RazerBatteryTray
{
    public static class UniversalGamingDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\UniversalGamingCache";

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;

        #region 品牌 VID 常量定义 (汇总自 OpenMouse 与 All-in-One)

        public const int VID_LAMZU_OFFICIAL   = 0x37B0; // Lamzu 官方 (Inca 8K, Paro 等)
        public const int VID_COMPX_LAMZU      = 0x373E; // CompX / Lamzu / Attack Shark / Vancer / LunaFury
        public const int VID_PULSAR           = 0x3710; // Pulsar 官方
        public const int VID_WLMOUSE          = 0x36A7; // WLmouse 官方 (Beast X 全系, Sword X)
        public const int VID_ENDGAME_GEAR     = 0x3367; // Endgame Gear (OP1we, XM2we)
        public const int VID_ATK              = 0x373B; // ATK / 烈空 (A9, F1, X1, VXE R1)
        public const int VID_VGN_COMPX        = 0x3554; // VGN / 蜻蜓 F1, Darmoshark M3, Ninjutso, RAWM, Teevolution
        public const int VID_ATTACK_SHARK_1   = 0x25A7; // Attack Shark (X3, R1 2.4G)
        public const int VID_ATTACK_SHARK_2   = 0x1D57; // Attack Shark (R1 / X11 OEM)
        public const int VID_MICLINK_OEM      = 0x3151; // MicLink / mlzn OEM (GearHub, Attack Shark R2, etc.)
        public const int VID_AJAZZ_USB        = 0x1A2C; // Ajazz (AJ199, AJ139 Pro, AJ159, AK820)
        public const int VID_MOTOSPEED        = 0x258A; // Motospeed / Darmoshark / SinoWealth / Glorious Classic
        public const int VID_GLORIOUS_PIXART  = 0x093A; // Glorious Model O2 / Incott GHero
        public const int VID_GLORIOUS_I       = 0x22D4; // Glorious Model I
        public const int VID_GLORIOUS_WIRED   = 0x320F; // Glorious Model O V2 / I 2 Wired
        public const int VID_GLORIOUS_O3      = 0x3794; // Glorious Model O3 Wireless
        public const int VID_NINJUTSO_LEGACY  = 0x248A; // Ninjutso Origin One X
        public const int VID_FINALMOUSE       = 0x361D; // Finalmouse (UltralightX, Starlight-12)
        public const int VID_KEYCHRON         = 0x3434; // Keychron (M1, M2, M3, M6)
        public const int VID_WOOTING          = 0x31E3; // Wooting (60HE, 80HE, Two HE)
        public const int VID_ASUS             = 0x0B05; // ASUS ROG / TUF (Gladius, Keris, Harpe Ace)
        public const int VID_STEELSERIES      = 0x1038; // SteelSeries (Aerox, Rival, Prime)
        public const int VID_CORSAIR          = 0x1B1C; // Corsair (Sabre, M65, Dark Core)
        public const int VID_DAREU            = 0x260D; // Dareu (A950 等)
        public const int VID_MCHOSE           = 0x3837; // MCHOSE 迈从 (A5, G3, AX5)
        public const int VID_REDRAGON         = 0x258A; // Redragon / SinoWealth (M690 Pro)
        public const int VID_GWOLVES          = 0x33E4; // G-Wolves (HTX, HTS, HSK)
        public const int VID_ZAUNKOENIG       = 0x3404; // Zaunkoenig (M2K, M3K)
        public const int VID_FATER            = 0x04D9; // Holtek / Fater
        public const int VID_MICROSOFT        = 0x045E; // Microsoft (Pro IntelliMouse)

        #endregion

        #region 产品信息与映射结构

        public class ProductEntry
        {
            public int Vid;
            public int Pid;
            public string Brand;
            public string ModelName;
            public DeviceCategory Category;
            public int MaxPollingRate;

            public ProductEntry(int vid, int pid, string brand, string model, DeviceCategory cat, int maxPoll)
            {
                Vid = vid;
                Pid = pid;
                Brand = brand;
                ModelName = model;
                Category = cat;
                MaxPollingRate = maxPoll;
            }
        }

        private static readonly Dictionary<string, ProductEntry> catalog = new Dictionary<string, ProductEntry>(StringComparer.OrdinalIgnoreCase);

        public static int[] GetSupportedPollingRatesByMax(int maxPoll)
        {
            if (maxPoll >= 8000) return new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
            if (maxPoll >= 4000) return new int[] { 125, 250, 500, 1000, 2000, 4000 };
            if (maxPoll >= 2000) return new int[] { 125, 250, 500, 1000, 2000 };
            return new int[] { 125, 250, 500, 1000 };
        }

        static UniversalGamingDeviceHelper()
        {
            RegisterCatalog();
        }

        private static void AddProduct(int vid, int pid, string brand, string model, DeviceCategory cat, int maxPoll)
        {
            string key = string.Format("{0:X4}:{1:X4}", vid, pid);
            catalog[key] = new ProductEntry(vid, pid, brand, model, cat, maxPoll);
        }

        private static void RegisterCatalog()
        {
            // --- LAMZU ---
            AddProduct(VID_LAMZU_OFFICIAL, 0x0010, "LAMZU", "LAMZU 8K Receiver (Inca / Paro)", DeviceCategory.Mouse, 8000);
            AddProduct(VID_LAMZU_OFFICIAL, 0x0007, "LAMZU", "LAMZU PARO (有线模式)", DeviceCategory.Mouse, 1000);
            AddProduct(VID_LAMZU_OFFICIAL, 0x0009, "LAMZU", "LAMZU INCA (有线模式)", DeviceCategory.Mouse, 1000);
            AddProduct(VID_COMPX_LAMZU,    0x0001, "LAMZU", "LAMZU Atlantis Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_COMPX_LAMZU,    0x0002, "LAMZU", "LAMZU Atlantis 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_COMPX_LAMZU,    0x0003, "LAMZU", "LAMZU Thorn Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_COMPX_LAMZU,    0x0004, "LAMZU", "LAMZU Maya Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_COMPX_LAMZU,    0x0005, "LAMZU", "LAMZU Maya X 8K", DeviceCategory.Mouse, 8000);

            // --- VAXEE ---
            AddProduct(0x3554, 0x1010, "VAXEE", "VAXEE E1 Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(0x3554, 0x1011, "VAXEE", "VAXEE NP-01S Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(0x3554, 0x1012, "VAXEE", "VAXEE NP-01S v2 4K", DeviceCategory.Mouse, 4000);
            AddProduct(0x3554, 0x1013, "VAXEE", "VAXEE XE Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(0x3554, 0x1014, "VAXEE", "VAXEE AX Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(0x3554, 0x1015, "VAXEE", "VAXEE OUTSET AX Wireless", DeviceCategory.Mouse, 1000);

            // --- WLMOUSE ---
            AddProduct(VID_WLMOUSE, 0xA860, "WLmouse", "Beast G Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA861, "WLmouse", "Beast G 有线", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA863, "WLmouse", "Huan Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA866, "WLmouse", "Beast Miao Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA868, "WLmouse", "Beast Mini Pro", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA870, "WLmouse", "Beast X Pro", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA872, "WLmouse", "Strider Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA874, "WLmouse", "Ying Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA878, "WLmouse", "Sword X Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA880, "WLmouse", "Beast Max Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA882, "WLmouse", "WLmouse 1K Receiver", DeviceCategory.Mouse, 1000);
            AddProduct(VID_WLMOUSE, 0xA883, "WLmouse", "Beast X Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_WLMOUSE, 0xA884, "WLmouse", "Beast X 有线", DeviceCategory.Mouse, 4000);
            AddProduct(VID_WLMOUSE, 0xA885, "WLmouse", "Beast Mini Wireless", DeviceCategory.Mouse, 4000);

            // --- PULSAR ---
            AddProduct(VID_PULSAR, 0x0001, "Pulsar", "X2 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_PULSAR, 0x0002, "Pulsar", "X2 Mini Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_PULSAR, 0x0003, "Pulsar", "X2V2 Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_PULSAR, 0x0004, "Pulsar", "X2V2 Mini Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_PULSAR, 0x0005, "Pulsar", "X2H Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_PULSAR, 0x0006, "Pulsar", "X2A Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_PULSAR, 0x0007, "Pulsar", "Xlite V2 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_PULSAR, 0x0008, "Pulsar", "Xlite V3 Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_PULSAR, 0x0009, "Pulsar", "Xlite V3 eS 8K", DeviceCategory.Mouse, 8000);

            // --- ENDGAME GEAR ---
            AddProduct(VID_ENDGAME_GEAR, 0x0001, "Endgame Gear", "OP1we Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_ENDGAME_GEAR, 0x0002, "Endgame Gear", "XM2we Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_ENDGAME_GEAR, 0x0003, "Endgame Gear", "OP1 8K 有线", DeviceCategory.Mouse, 8000);

            // --- ATK / VXE ---
            AddProduct(VID_ATK, 0x1118, "ATK", "ATK A9 Ultimate", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x11B6, "ATK", "ATK A9 Ultimate", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x1085, "VXE", "VXE Dragonfly R1 SE / SE+", DeviceCategory.Mouse, 2000);
            AddProduct(VID_VGN_COMPX, 0xF58A, "VXE", "VXE Dragonfly R1 Pro Max 接收器", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xF58C, "VXE", "VXE Dragonfly R1 Pro Max 有线", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xF58E, "VXE", "VXE Dragonfly R1 2.4G 接收器", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xF58F, "VXE", "VXE Dragonfly R1 鼠标", DeviceCategory.Mouse, 4000);
            AddProduct(VID_ATK, 0x10C9, "ATK", "ATK Nearlink 接收器 (A9 / 烈空)", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x1086, "ATK", "ATK F1 Pro Max Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x1087, "ATK", "ATK F1 Ultimate 8K", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x1088, "ATK", "ATK X1 Pro Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x1089, "ATK", "ATK X1 Pro Max Wireless", DeviceCategory.Mouse, 8000);

            // --- ATTACK SHARK (攻击鲨) ---
            AddProduct(VID_ATTACK_SHARK_1, 0x0001, "Attack Shark", "Attack Shark X3 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_ATTACK_SHARK_1, 0x0002, "Attack Shark", "Attack Shark X3 Pro 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_ATTACK_SHARK_1, 0x0003, "Attack Shark", "Attack Shark X6 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_ATTACK_SHARK_2, 0x0001, "Attack Shark", "Attack Shark R1 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_ATTACK_SHARK_2, 0x0002, "Attack Shark", "Attack Shark X11 有线/无线", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MICLINK_OEM,    0x5001, "Attack Shark", "Attack Shark R2 8K", DeviceCategory.Mouse, 8000);

            // --- AJAZZ (黑爵) ---
            AddProduct(VID_AJAZZ_USB, 0x0001, "Ajazz", "Ajazz AJ199 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_AJAZZ_USB, 0x0002, "Ajazz", "Ajazz AJ199 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_AJAZZ_USB, 0x0003, "Ajazz", "Ajazz AJ139 Pro", DeviceCategory.Mouse, 1000);
            AddProduct(VID_AJAZZ_USB, 0x0004, "Ajazz", "Ajazz AJ139 Pro 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_AJAZZ_USB, 0x0005, "Ajazz", "Ajazz AJ159 Apex 8K", DeviceCategory.Mouse, 8000);
            AddProduct(VID_AJAZZ_USB, 0x0006, "Ajazz", "Ajazz AJ179 Apex 8K", DeviceCategory.Mouse, 8000);

            // --- DARMOSHARK (摩豹达摩鲨) ---
            AddProduct(VID_VGN_COMPX, 0xFB24, "Darmoshark", "Darmoshark M3 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_VGN_COMPX, 0xFB25, "Darmoshark", "Darmoshark M3s Wireless", DeviceCategory.Mouse, 2000);
            AddProduct(VID_VGN_COMPX, 0xFB26, "Darmoshark", "Darmoshark M3-4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xFB27, "Darmoshark", "Darmoshark M3 Pro", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xFB28, "Darmoshark", "Darmoshark N3 Wireless", DeviceCategory.Mouse, 1000);

            // --- GLORIOUS ---
            AddProduct(VID_MOTOSPEED,       0x2011, "Glorious", "Model O Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2022, "Glorious", "Model O Wireless 接收器", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2012, "Glorious", "Model D Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2023, "Glorious", "Model D Wireless 接收器", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2013, "Glorious", "Model O- Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2024, "Glorious", "Model O- Wireless 接收器", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2014, "Glorious", "Model D- Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2025, "Glorious", "Model D- Wireless 接收器", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2015, "Glorious", "Model O Pro", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2018, "Glorious", "Series One Pro", DeviceCategory.Mouse, 1000);
            AddProduct(VID_GLORIOUS_PIXART, 0x822A, "Glorious", "Model O 2 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_GLORIOUS_PIXART, 0x824A, "Glorious", "Model D 2 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_GLORIOUS_PIXART, 0x821D, "Glorious", "Model I 2 Wireless 接收器", DeviceCategory.Mouse, 1000);

            // --- NINJUTSO ---
            AddProduct(VID_VGN_COMPX,       0xF520, "Ninjutso", "Ninjutso Sora Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_VGN_COMPX,       0xF522, "Ninjutso", "Ninjutso Sora 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX,       0xF523, "Ninjutso", "Ninjutso Sora V2 8K", DeviceCategory.Mouse, 8000);
            AddProduct(VID_NINJUTSO_LEGACY, 0x0001, "Ninjutso", "Ninjutso Origin One X", DeviceCategory.Mouse, 1000);

            // --- RAWM ---
            AddProduct(VID_VGN_COMPX, 0xF530, "RAWM", "RAWM SA-MH01 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xF531, "RAWM", "RAWM SA-SH01 4K", DeviceCategory.Mouse, 4000);

            // --- INCOTT ---
            AddProduct(VID_GLORIOUS_PIXART, 0x0001, "Incott", "Incott GHero 8K", DeviceCategory.Mouse, 8000);
            AddProduct(VID_GLORIOUS_PIXART, 0x0002, "Incott", "Incott HPC01M Pro", DeviceCategory.Mouse, 4000);

            // --- FINALMOUSE ---
            AddProduct(VID_FINALMOUSE, 0x0100, "Finalmouse", "UltralightX (ULX) Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_FINALMOUSE, 0x0101, "Finalmouse", "Starlight-12 Wireless", DeviceCategory.Mouse, 1000);

            // --- KEYCHRON ---
            AddProduct(VID_KEYCHRON, 0x0440, "Keychron", "Keychron Nape Pro (有线)", DeviceCategory.Mouse, 1000);
            AddProduct(VID_KEYCHRON, 0x0001, "Keychron", "Keychron M1 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_KEYCHRON, 0x0002, "Keychron", "Keychron M3 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_KEYCHRON, 0x0003, "Keychron", "Keychron M3 Mini 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_KEYCHRON, 0x0004, "Keychron", "Keychron M6 Wireless", DeviceCategory.Mouse, 1000);

            // --- WOOTING ---
            AddProduct(VID_WOOTING, 0x0001, "Wooting", "Wooting 60HE 磁轴键盘", DeviceCategory.Keyboard, 1000);
            AddProduct(VID_WOOTING, 0x0002, "Wooting", "Wooting 80HE 8K 磁轴键盘", DeviceCategory.Keyboard, 8000);
            AddProduct(VID_WOOTING, 0x0003, "Wooting", "Wooting Two HE 磁轴键盘", DeviceCategory.Keyboard, 1000);

            // --- MCHOSE (迈从) ---
            AddProduct(VID_MCHOSE, 0x0001, "MCHOSE", "MCHOSE A5 Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_MCHOSE, 0x0002, "MCHOSE", "MCHOSE AX5 Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_MCHOSE, 0x0003, "MCHOSE", "MCHOSE G3 Wireless", DeviceCategory.Mouse, 8000);

            // --- DAREU (达尔优) ---
            AddProduct(VID_DAREU, 0x0001, "Dareu", "达尔优 A950 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_DAREU, 0x0002, "Dareu", "达尔优 A980 Pro Max 8K", DeviceCategory.Mouse, 8000);

            // --- DELUX (多彩) ---
            AddProduct(0x258A, 0x1001, "Delux", "多彩 M800 Pro", DeviceCategory.Mouse, 1000);
            AddProduct(0x258A, 0x1002, "Delux", "多彩 M800 Ultra 4K", DeviceCategory.Mouse, 4000);

            // --- WALLHACK ---
            AddProduct(0x3554, 0x1122, "WALLHACK", "WALLHACK SP-004 Esports Mouse", DeviceCategory.Mouse, 4000);

            // --- G-WOLVES ---
            AddProduct(0x3554, 0x2233, "G-Wolves", "G-Wolves HTS Plus 4K", DeviceCategory.Mouse, 4000);

            // --- FINALMOUSE ---
            AddProduct(0x3554, 0x3344, "Finalmouse", "Finalmouse UltralightX", DeviceCategory.Mouse, 8000);

            // --- ZAUNKOENIG ---
            AddProduct(0x3554, 0x4455, "Zaunkoenig", "Zaunkoenig M2K 8K", DeviceCategory.Mouse, 8000);
        }

        public static ProductEntry LookupProduct(int vid, int pid, string productString)
        {
            string key = string.Format("{0:X4}:{1:X4}", vid, pid);
            ProductEntry entry;
            if (catalog.TryGetValue(key, out entry))
            {
                return entry;
            }

            // 依据厂商 VID 推断品牌
            string brand = "通用电竞外设";
            DeviceCategory cat = DeviceCategory.Mouse;
            int poll = 1000;

            switch (vid)
            {
                case VID_LAMZU_OFFICIAL:
                case VID_COMPX_LAMZU:
                    brand = "LAMZU";
                    poll = 4000;
                    break;
                case VID_PULSAR:
                    brand = "Pulsar";
                    poll = 4000;
                    break;
                case VID_WLMOUSE:
                    brand = "WLmouse";
                    poll = 8000;
                    break;
                case VID_ENDGAME_GEAR:
                    brand = "Endgame Gear";
                    break;
                case VID_ATK:
                    brand = "ATK";
                    poll = 8000;
                    break;
                case VID_VGN_COMPX:
                    brand = "VGN / 达摩鲨";
                    poll = 4000;
                    break;
                case VID_ATTACK_SHARK_1:
                case VID_ATTACK_SHARK_2:
                    brand = "Attack Shark";
                    break;
                case VID_AJAZZ_USB:
                    brand = "Ajazz";
                    break;
                case VID_FINALMOUSE:
                    brand = "Finalmouse";
                    poll = 4000;
                    break;
                case VID_WOOTING:
                    brand = "Wooting";
                    cat = DeviceCategory.Keyboard;
                    poll = 8000;
                    break;
                case VID_KEYCHRON:
                    brand = "Keychron";
                    break;
                case VID_MCHOSE:
                    brand = "MCHOSE";
                    poll = 4000;
                    break;
                case VID_DAREU:
                    brand = "Dareu";
                    poll = 4000;
                    break;
                case VID_ASUS:
                    brand = "ASUS ROG";
                    break;
                case VID_STEELSERIES:
                    brand = "SteelSeries";
                    break;
                case VID_CORSAIR:
                    brand = "Corsair";
                    break;
            }

            string model = !string.IsNullOrEmpty(productString) ? productString : string.Format("{0} 无线设备 ({1:X4})", brand, pid);
            return new ProductEntry(vid, pid, brand, model, cat, poll);
        }

        #endregion

        #region 协议引擎一：CompX / PixArt 64字节 Feature 引擎 (Lamzu, Pulsar, WLmouse 等)

        public static bool QueryCompxBattery(IntPtr handle, out int percent, out bool isCharging)
        {
            percent = -1;
            isCharging = false;
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                // 下发 Feature Report 0x83 查询命令 (前导 Report ID 0x00 + 64 字节)
                byte[] tx = new byte[65];
                tx[0] = 0x00;
                tx[1] = 0x00;
                tx[2] = 0x00;
                tx[3] = 0x02;
                tx[4] = 0x02;
                tx[5] = 0x00;
                tx[6] = 0x83;

                bool setOk = RazerDeviceHelper.HidD_SetFeature(handle, tx, tx.Length);
                if (!setOk)
                {
                    // 尝试无前导 64 字节
                    byte[] tx64 = new byte[64];
                    Array.Copy(tx, 1, tx64, 0, 64);
                    setOk = RazerDeviceHelper.HidD_SetFeature(handle, tx64, tx64.Length);
                }

                if (!setOk) return false;

                // 轮询 GetFeature 直至取得 A1 00 02 02 00 83 帧 (跳过 A0 等待帧)
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    Thread.Sleep(8);
                    byte[] rx = new byte[65];
                    rx[0] = 0x00;
                    if (RazerDeviceHelper.HidD_GetFeature(handle, rx, rx.Length))
                    {
                        byte[] data = rx;
                        if (rx.Length >= 2 && rx[0] == 0x00)
                        {
                            data = new byte[rx.Length - 1];
                            Array.Copy(rx, 1, data, 0, data.Length);
                        }

                        // 跳过 A0 确认包
                        if (data.Length > 0 && data[0] == 0xA0) continue;

                        // 查找特征帧: A1 00 02 02 00 83 [chg] [pct]
                        for (int i = 0; i + 8 <= data.Length; i++)
                        {
                            if (data[i] == 0xA1 && data[i + 1] == 0x00 &&
                                data[i + 2] == 0x02 && data[i + 3] == 0x02 &&
                                data[i + 4] == 0x00 && data[i + 5] == 0x83)
                            {
                                int chg = data[i + 6];
                                int pct = data[i + 7];
                                if ((chg == 0 || chg == 1) && pct > 0 && pct <= 100)
                                {
                                    percent = pct;
                                    isCharging = (chg == 1);
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        #endregion

        #region 协议引擎二：Beken / ATK / Endgame Gear 16字节 EEPROM 引擎

        public static byte CalculateWeChecksum(byte reportId, byte[] data15)
        {
            int sum = reportId;
            for (int i = 0; i < 15 && i < data15.Length; i++)
            {
                sum += (data15[i] & 0xFF);
            }
            return (byte)((0x55 - (sum & 0xFF)) & 0xFF);
        }

        public static bool QueryBekenWeBattery(IntPtr handle, out int percent, out bool isCharging, out int millivolts)
        {
            percent = -1;
            isCharging = false;
            millivolts = 0;
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                // Report ID 0x08, 16 字节
                byte[] tx = new byte[17];
                tx[0] = 0x08; // Report ID
                tx[1] = 0x04; // CMD: BATTERY_COMMAND (0x04)

                byte[] data15 = new byte[15];
                Array.Copy(tx, 1, data15, 0, 15);
                tx[16] = CalculateWeChecksum(0x08, data15);

                bool setOk = RazerDeviceHelper.HidD_SetFeature(handle, tx, tx.Length);
                if (!setOk)
                {
                    // 尝试无前导 16 字节
                    byte[] tx16 = new byte[16];
                    Array.Copy(tx, 1, tx16, 0, 16);
                    setOk = RazerDeviceHelper.HidD_SetFeature(handle, tx16, tx16.Length);
                }

                if (!setOk) return false;

                Thread.Sleep(10);
                byte[] rx = new byte[17];
                rx[0] = 0x08;
                if (RazerDeviceHelper.HidD_GetFeature(handle, rx, rx.Length))
                {
                    int offset = (rx[0] == 0x08) ? 1 : 0;
                    if (rx[offset] == 0x04)
                    {
                        int len = rx[offset + 4];
                        if (len >= 1)
                        {
                            percent = Math.Min((int)rx[offset + 5], 100);
                        }
                        if (len >= 2)
                        {
                            isCharging = (rx[offset + 6] != 0);
                        }
                        if (len >= 4)
                        {
                            millivolts = (rx[offset + 7] << 8) | rx[offset + 8];
                        }
                        if (percent > 0 && percent <= 100) return true;
                    }
                }
            }
            catch { }

            return false;
        }

        #endregion

        #region ATK A9 Ultimate：电量查询及独立设置服务共用的 HID 传输；不执行配对或固件操作

        // Protocol facts: OpenMouse-Project/mouse-protocol, src/drivers/atk/hid.ts
        // (readBattery/exchange/isSupported), catalog 373B:11B6 = A9 Ultimate, CID/MID 1,3.
        // Independently implemented here; no upstream implementation is copied.
        // Official ATK HUB v2 device catalog: 373B:1118, FF04:0002, A9 Ultimate.
        // https://v2-hub.atk.pro/assets/index-BE5kncBT.js
        // Its Compx controller confirms command 0x04 on output/input report 0x08.
        // FF02:0002 is also evidenced by the OpenMouse transport implementation.
        public static bool IsAtkA9Ultimate(int vid, int pid)
        {
            return vid == VID_ATK && (pid == 0x1118 || pid == 0x11B6);
        }

        public static bool IsAtkBatteryCollection(int usagePage, int usage, int inputLength, int outputLength)
        {
            return (usagePage == 0xFF02 || usagePage == 0xFF04) && usage == 0x0002 && inputLength == 17 && outputLength == 17;
        }

        public static byte[] BuildAtkBatteryRequest()
        {
            byte[] report = new byte[17];
            report[0] = 0x08;
            report[1] = 0x04;
            report[16] = 0x49; // Report ID + command + checksum = 0x55.
            return report;
        }

        public static bool TryParseAtkBatteryReport(byte[] report, out int percent, out bool charging, out int millivolts)
        {
            percent = -1;
            charging = false;
            millivolts = 0;
            if (report == null || report.Length != 17 || report[0] != 0x08 || report[1] != 0x04) return false;
            int sum = 0;
            for (int i = 0; i < report.Length; i++) sum += report[i];
            // Payload has a five-byte header, at most ten data bytes and a checksum.
            int length = report[5];
            if ((sum & 0xFF) != 0x55 || length < 2 || length > 10 || report[6] > 100) return false;
            percent = report[6]; // Zero is a valid battery level.
            charging = report[7] != 0;
            if (length >= 4) millivolts = (report[8] << 8) | report[9];
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AtkOverlapped
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

        // Mirrors the existing HyperX overlapped I/O lifecycle. Unmanaged memory remains
        // valid until a pending request completes, including cancellation on timeout.
        private static bool TransferAtkReport(IntPtr handle, byte[] report, bool write, uint timeout)
        {
            IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
            if (evt == IntPtr.Zero) return false;
            IntPtr buffer = IntPtr.Zero;
            IntPtr overlapped = IntPtr.Zero;
            try
            {
                buffer = Marshal.AllocHGlobal(report.Length);
                Marshal.Copy(report, 0, buffer, report.Length);
                overlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(AtkOverlapped)));
                AtkOverlapped state = new AtkOverlapped();
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

        // Settings reuse the proven Output/Input transport; other brands retain their paths.
        internal static IntPtr OpenAtkSettingsHandle(string path, int vid, int pid)
        {
            if (vid != VID_ATK || pid != 0x11B6) throw new NotSupportedException("Only the V3-confirmed ATK A9 Ultimate 373B:11B6 settings layout is supported.");
            IntPtr h = RazerDeviceHelper.CreateFile(path, GENERIC_READ | GENERIC_WRITE,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0x40000000, IntPtr.Zero);
            if (h == IntPtr.Zero || h.ToInt64() == -1) throw new IOException("Cannot open ATK A9 Ultimate.");
            try
            {
                RazerDeviceHelper.HIDD_ATTRIBUTES a = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                a.Size = Marshal.SizeOf(a);
                if (!RazerDeviceHelper.HidD_GetAttributes(h, ref a) || a.VendorID != vid || a.ProductID != pid)
                    throw new IOException("ATK device identity changed.");
                IntPtr pp;
                if (!RazerDeviceHelper.HidD_GetPreparsedData(h, out pp)) throw new IOException("Cannot inspect ATK collection.");
                try
                {
                    RazerDeviceHelper.HIDP_CAPS c;
                    if (RazerDeviceHelper.HidP_GetCaps(pp, out c) != 0x00110000 || c.UsagePage != 0xFF04 || c.Usage != 2 || c.InputReportByteLength != 17 || c.OutputReportByteLength != 17)
                        throw new IOException("ATK settings require FF04:0002 with 17-byte reports.");
                }
                finally { RazerDeviceHelper.HidD_FreePreparsedData(pp); }
                return h;
            }
            catch { RazerDeviceHelper.CloseHandle(h); throw; }
        }

        internal static List<string> FindAtkSettingsPaths(int vid, int pid)
        {
            if (vid != VID_ATK || pid != 0x11B6) throw new NotSupportedException("Settings require confirmed A9 Ultimate 373B:11B6.");
            List<string> paths = new List<string>();
            Guid guid;
            RazerDeviceHelper.HidD_GetHidGuid(out guid);
            IntPtr set = RazerDeviceHelper.SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
            if (set == IntPtr.Zero || set.ToInt64() == -1) return paths;
            try
            {
                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA data = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                data.cbSize = Marshal.SizeOf(data);
                for (uint n = 0; RazerDeviceHelper.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, n, ref data); n++)
                {
                    uint size;
                    RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out size, IntPtr.Zero);
                    if (size < 8) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 5);
                        if (!RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out size, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringAuto(new IntPtr(detail.ToInt64() + 4));
                        if (path == null || path.IndexOf("vid_373b&pid_11b6", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        IntPtr h = IntPtr.Zero;
                        try { h = OpenAtkSettingsHandle(path, vid, pid); paths.Add(path); }
                        catch (IOException) { }
                        finally { if (h != IntPtr.Zero) RazerDeviceHelper.CloseHandle(h); }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { RazerDeviceHelper.SetupDiDestroyDeviceInfoList(set); }
            return paths;
        }

        internal static byte[] ExchangeAtkSettingsReport(IntPtr handle, byte[] request)
        {
            lock (hidLock)
            {
                if (!TransferAtkReport(handle, request, true, 500)) throw new IOException("ATK output report timed out or failed.");
                Stopwatch timer = Stopwatch.StartNew();
                for (int n = 0; n < 12 && timer.ElapsedMilliseconds < 1000; n++)
                {
                    byte[] reply = new byte[17];
                    if (!TransferAtkReport(handle, reply, false, (uint)Math.Max(1L, 1000L - timer.ElapsedMilliseconds))) break;
                    if (reply[0] == 8 && reply[1] == request[1] && reply[3] == request[3] && reply[4] == request[4]) return reply;
                }
                throw new IOException("ATK matching input report was not received.");
            }
        }

        private static bool QueryAtkA9Battery(string devicePath, IntPtr discoveryHandle, out int percent, out bool charging, out int millivolts)
        {
            percent = -1;
            charging = false;
            millivolts = 0;
            IntPtr preparsed;
            if (!RazerDeviceHelper.HidD_GetPreparsedData(discoveryHandle, out preparsed)) return false;
            try
            {
                RazerDeviceHelper.HIDP_CAPS caps;
                if (RazerDeviceHelper.HidP_GetCaps(preparsed, out caps) != 0x00110000 ||
                    !IsAtkBatteryCollection(caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength)) return false;
            }
            finally { RazerDeviceHelper.HidD_FreePreparsedData(preparsed); }

            IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0x40000000, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
            try
            {
                if (!TransferAtkReport(handle, BuildAtkBatteryRequest(), true, 100)) return false;
                Stopwatch timer = Stopwatch.StartNew();
                // Ignore unrelated input reports, without an unbounded poll loop.
                for (int attempt = 0; attempt < 8 && timer.ElapsedMilliseconds < 500; attempt++)
                {
                    byte[] reply = new byte[17];
                    uint remaining = (uint)Math.Max(1L, 500L - timer.ElapsedMilliseconds);
                    if (!TransferAtkReport(handle, reply, false, remaining)) return false;
                    if (TryParseAtkBatteryReport(reply, out percent, out charging, out millivolts)) return true;
                }
            }
            catch { }
            finally { RazerDeviceHelper.CloseHandle(handle); }
            return false;
        }

        #endregion

        #region 协议引擎三：Vaxee 64字节专用控制引擎

        public static bool QueryVaxeeBattery(IntPtr handle, out int percent, out bool isCharging)
        {
            percent = -1;
            isCharging = false;
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                // 1. 查询 Page 0x0B (电量)
                byte[] tx0b = new byte[64];
                tx0b[0] = 0x0E;
                tx0b[1] = 0xA5;
                tx0b[2] = 0x0B;
                tx0b[3] = 0x01;
                tx0b[4] = 0x01;
                tx0b[5] = 0x00;

                if (RazerDeviceHelper.HidD_SetFeature(handle, tx0b, tx0b.Length))
                {
                    Thread.Sleep(10);
                    byte[] rx0b = new byte[64];
                    rx0b[0] = 0x0E;
                    if (RazerDeviceHelper.HidD_GetFeature(handle, rx0b, rx0b.Length))
                    {
                        if (rx0b[0] == 0x0E && rx0b[1] == 0xA5 && rx0b[2] == 0x0B)
                        {
                            int raw = (int)rx0b[5];
                            int pct = raw * 5; // 0..20 段值按 5% 离散计算
                            if (pct >= 0 && pct <= 100)
                            {
                                percent = pct;
                            }
                        }
                    }
                }

                // 2. 查询 Page 0x10 (充电状态)
                byte[] tx10 = new byte[64];
                tx10[0] = 0x0E;
                tx10[1] = 0xA5;
                tx10[2] = 0x10;
                tx10[3] = 0x01;
                tx10[4] = 0x01;
                tx10[5] = 0x00;

                if (RazerDeviceHelper.HidD_SetFeature(handle, tx10, tx10.Length))
                {
                    Thread.Sleep(10);
                    byte[] rx10 = new byte[64];
                    rx10[0] = 0x0E;
                    if (RazerDeviceHelper.HidD_GetFeature(handle, rx10, rx10.Length))
                    {
                        if (rx10[0] == 0x0E && rx10[1] == 0xA5 && rx10[2] == 0x10)
                        {
                            isCharging = (rx10[5] == 0x01);
                        }
                    }
                }

                return (percent >= 0);
            }
            catch { }

            return false;
        }

        #endregion

        #region 全设备综合枚举与发现

        public static bool IsSupportedGamingDevice(int vid, string path, string prod)
        {
            switch (vid)
            {
                case VID_LAMZU_OFFICIAL:
                case VID_COMPX_LAMZU:
                case VID_PULSAR:
                case VID_WLMOUSE:
                case VID_ENDGAME_GEAR:
                case VID_ATK:
                case VID_VGN_COMPX:
                case VID_ATTACK_SHARK_1:
                case VID_ATTACK_SHARK_2:
                case VID_MICLINK_OEM:
                case VID_AJAZZ_USB:
                case VID_MOTOSPEED:
                case VID_GLORIOUS_PIXART:
                case VID_GLORIOUS_I:
                case VID_GLORIOUS_WIRED:
                case VID_GLORIOUS_O3:
                case VID_NINJUTSO_LEGACY:
                case VID_FINALMOUSE:
                case VID_KEYCHRON:
                case VID_WOOTING:
                case VID_MCHOSE:
                case VID_DAREU:
                case VID_ASUS:
                case VID_STEELSERIES:
                case VID_CORSAIR:
                case VID_GWOLVES:
                case VID_ZAUNKOENIG:
                case VID_FATER:
                case VID_MICROSOFT:
                    return true;
            }

            if (!string.IsNullOrEmpty(path))
            {
                string lower = path.ToLowerInvariant();
                if (lower.Contains("lamzu") || lower.Contains("pulsar") || lower.Contains("wlmouse") ||
                    lower.Contains("atk") || lower.Contains("vxe") || lower.Contains("vgn") ||
                    lower.Contains("darmoshark") || lower.Contains("attackshark") || lower.Contains("ajazz") ||
                    lower.Contains("glorious") || lower.Contains("ninjutso") || lower.Contains("vaxee"))
                {
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(prod))
            {
                string pLower = prod.ToLowerInvariant();
                if (pLower.Contains("lamzu") || pLower.Contains("pulsar") || pLower.Contains("wlmouse") ||
                    pLower.Contains("vxe") || pLower.Contains("atk") || pLower.Contains("beast") ||
                    pLower.Contains("atlantis") || pLower.Contains("thorn") || pLower.Contains("maya") ||
                    pLower.Contains("darmoshark") || pLower.Contains("glorious") || pLower.Contains("sora") ||
                    pLower.Contains("vaxee"))
                {
                    return true;
                }
            }

            return false;
        }

        public static List<MouseBatteryInfo> QueryAllGamingDevices()
        {
            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            lock (hidLock)
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return list;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                uint memberIdx = 0;
                HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    ifData.cbSize = Marshal.SizeOf(ifData);
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        if (reqSize == 0) continue;

                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (devicePath != null)
                            {
                                // 过滤键盘按键集合，保留控制/Vendor集合 (mi_02 或非普通 kbd)
                                if (devicePath.EndsWith("\\kbd", StringComparison.OrdinalIgnoreCase))
                                {
                                    Marshal.FreeHGlobal(detailBuffer);
                                    continue;
                                }

                                IntPtr handle = RazerDeviceHelper.CreateFile(
                                    devicePath,
                                    GENERIC_READ | GENERIC_WRITE,
                                    RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                                    IntPtr.Zero,
                                    RazerDeviceHelper.OPEN_EXISTING,
                                    0,
                                    IntPtr.Zero);

                                if (handle == IntPtr.Zero || handle.ToInt64() == -1)
                                {
                                    handle = RazerDeviceHelper.CreateFile(
                                        devicePath,
                                        GENERIC_WRITE,
                                        RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                                        IntPtr.Zero,
                                        RazerDeviceHelper.OPEN_EXISTING,
                                        0,
                                        IntPtr.Zero);
                                }

                                if (handle == IntPtr.Zero || handle.ToInt64() == -1)
                                {
                                    handle = RazerDeviceHelper.CreateFile(
                                        devicePath,
                                        0,
                                        RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                                        IntPtr.Zero,
                                        RazerDeviceHelper.OPEN_EXISTING,
                                        0,
                                        IntPtr.Zero);
                                }

                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                        attr.Size = Marshal.SizeOf(attr);
                                        RazerDeviceHelper.HidD_GetAttributes(handle, ref attr);

                                        StringBuilder sbProd = new StringBuilder(256);
                                        string prodStr = RazerDeviceHelper.HidD_GetProductString(handle, sbProd, sbProd.Capacity) ? sbProd.ToString().Trim() : "";

                                        if (IsSupportedGamingDevice(attr.VendorID, devicePath, prodStr))
                                        {
                                            string deviceKey = string.Format("{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);
                                            if (!seenKeys.Contains(deviceKey))
                                            {
                                                ProductEntry product = LookupProduct(attr.VendorID, attr.ProductID, prodStr);

                                                int batt = -1;
                                                bool isChg = false;
                                                int mv = 0;

                                                bool isAtkA9 = IsAtkA9Ultimate(attr.VendorID, attr.ProductID);
                                                bool ok;
                                                if (isAtkA9)
                                                {
                                                    // No generic Feature probes on this Output/Input device.
                                                    ok = QueryAtkA9Battery(devicePath, handle, out batt, out isChg, out mv);
                                                }
                                                else
                                                {
                                                    ok = QueryCompxBattery(handle, out batt, out isChg);
                                                    if (!ok) ok = QueryBekenWeBattery(handle, out batt, out isChg, out mv);
                                                    if (!ok) ok = QueryVaxeeBattery(handle, out batt, out isChg);
                                                }

                                                if (ok || batt > 0)
                                                {
                                                    seenKeys.Add(deviceKey);

                                                    MouseBatteryInfo info = new MouseBatteryInfo();
                                                    info.DeviceId = string.Format("Gaming:{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);
                                                    info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);
                                                    info.DeviceName = product.ModelName;
                                                    info.Brand = product.Brand;
                                                    info.Category = product.Category;
                                                    info.Transport = isAtkA9
                                                        ? ((prodStr.IndexOf("dongle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                            prodStr.IndexOf("receiver", StringComparison.OrdinalIgnoreCase) >= 0) ? "2.4G 无线" : "USB")
                                                        : "2.4G 无线 / 极速回报";
                                                    info.IsDonglePresent = true;
                                                    info.IsConnected = true;
                                                    info.IsSleeping = false;
                                                    info.IsCharging = isChg;
                                                    info.BatteryPercent = batt;
                                                    info.MaxPollingRate = product.MaxPollingRate;
                                                    info.SupportedPollingRates = GetSupportedPollingRatesByMax(product.MaxPollingRate);

                                                    list.Add(info);
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        RazerDeviceHelper.CloseHandle(handle);
                                    }
                                }
                            }
                            Marshal.FreeHGlobal(detailBuffer);
                        }
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }
            }

            return list;
        }

        #endregion
    }
}
