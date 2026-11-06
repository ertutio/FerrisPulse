// ============================================================================
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 模块名称: Wave75KeyMacroDialog.cs
// 所属区域: UI/Forms (高级设备设置)
// 核心职责: 编辑指定层/矩阵键位及宏表，显示原始值并回读确认。
// 依赖关系: DpiAwareForm, ModernButton, Wave75DeviceHelper
// 1. [契约同步更新]: 修改逻辑、属性或绘图逻辑时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为 UTF-8 with BOM。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public sealed class Wave75KeyMacroDialog : DpiAwareForm
    {
        private TabControl tabs;
        private NumericUpDown layer, index, keyType, keyCode1, keyCode2, slots;
        private ComboBox slot;
        private DataGridView events;
        private Label status, capacityLabel;
        private ModernButton keyRead, keySave, macroRead, macroSave, addDown, addUp;
        private Wave75Macro[] macros;
        private int capacity, selectedSlot = -1, deviceGeneration;
        private string keyPath, macroPath;
        private bool busy, keyLoaded, keyDirty, macroDirty, loading;

        public Wave75KeyMacroDialog(float scale) : base(scale)
        {
            Text = "FerrisPulse · WAVE75 高级键位与宏";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            BackColor = Color.FromArgb(13, 16, 23); ForeColor = Color.FromArgb(235, 240, 248);
            ClientSize = new Size(S(680), S(510));
            tabs = new TabControl(); tabs.SetBounds(S(16), S(16), S(648), S(430)); Controls.Add(tabs);
            TabPage keys = Page("矩阵键位"); TabPage macro = Page("宏表"); tabs.TabPages.Add(keys); tabs.TabPages.Add(macro);
            keys.Controls.Add(L("高级设置：物理键位与矩阵索引的对应关系尚未确认。", 16, 14, 600));
            keys.Controls.Add(L("仅修改所选层与索引的 3 个原始字节；请先读取再编辑。", 16, 40, 600));
            keys.Controls.Add(L("层 (0–3)", 16, 83, 110)); layer = N(130, 80, 80, 0, 3); keys.Controls.Add(layer);
            keys.Controls.Add(L("矩阵索引 (0–127)", 240, 83, 150)); index = N(390, 80, 90, 0, 127); keys.Controls.Add(index);
            keyRead = B("读取此键", 490, 78, 120); keys.Controls.Add(keyRead); keyRead.Click += delegate { ReadKey(); };
            keys.Controls.Add(L("Type", 16, 146, 90)); keyType = N(105, 143, 90, 0, 255); keys.Controls.Add(keyType);
            keys.Controls.Add(L("Code1", 215, 146, 75)); keyCode1 = N(290, 143, 90, 0, 255); keys.Controls.Add(keyCode1);
            keys.Controls.Add(L("Code2", 402, 146, 75)); keyCode2 = N(477, 143, 90, 0, 255); keys.Controls.Add(keyCode2);
            keySave = B("保存此键", 460, 205, 150); keys.Controls.Add(keySave); keySave.Click += delegate { WriteKey(); };
            layer.ValueChanged += KeyTargetChanged; index.ValueChanged += KeyTargetChanged;
            keyType.ValueChanged += KeyChanged; keyCode1.ValueChanged += KeyChanged; keyCode2.ValueChanged += KeyChanged;

            Label note = L("高级设置：事件原样显示。非修饰键 HID 码的按下 Flags=129、抬起 Flags=1。\n延迟为 2 毫秒单位；未知 Flags 会按原值保留。", 14, 10, 610); note.Height = S(47); macro.Controls.Add(note);
            macroRead = B("读取宏表", 14, 64, 120); macro.Controls.Add(macroRead); macroRead.Click += delegate { ReadMacros(); };
            capacityLabel = L("容量：待读取", 145, 69, 250); macro.Controls.Add(capacityLabel);
            macro.Controls.Add(L("槽位数", 405, 69, 65)); slots = N(472, 65, 70, 0, 65535); macro.Controls.Add(slots);
            macro.Controls.Add(L("槽位", 14, 110, 50)); slot = new ComboBox(); slot.DropDownStyle = ComboBoxStyle.DropDownList;
            slot.BackColor = Color.FromArgb(32, 38, 50); slot.ForeColor = ForeColor; slot.SetBounds(S(65), S(106), S(85), S(28)); macro.Controls.Add(slot);
            addDown = B("添加普通键按下", 170, 104, 154); addUp = B("添加普通键抬起", 335, 104, 154); macro.Controls.Add(addDown); macro.Controls.Add(addUp);
            addDown.Click += delegate { AddEvent(129); }; addUp.Click += delegate { AddEvent(1); };
            events = new DataGridView(); events.SetBounds(S(14), S(145), S(600), S(180)); events.AllowUserToAddRows = true; events.AllowUserToDeleteRows = true;
            events.BackgroundColor = Color.FromArgb(22, 27, 37); events.BorderStyle = BorderStyle.None; events.RowHeadersWidth = S(38);
            events.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; events.DefaultCellStyle.BackColor = Color.FromArgb(32, 38, 50);
            events.DefaultCellStyle.ForeColor = ForeColor; events.DefaultCellStyle.SelectionBackColor = Color.FromArgb(45, 93, 110);
            events.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(24, 30, 40); events.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
            events.EnableHeadersVisualStyles = false; events.Columns.Add("delay", "延迟 ×2ms (0–65535)"); events.Columns.Add("code", "Code / HID 码 (0–255)"); events.Columns.Add("flags", "Flags 原值 (0–255)");
            macro.Controls.Add(events);
            Label warning = L("保存会重建并写入整张宏表，未知保留字段和布局可能改变。", 14, 330, 610); warning.ForeColor = Color.FromArgb(230, 181, 120); macro.Controls.Add(warning);
            macroSave = B("保存整张宏表", 444, 362, 170); macro.Controls.Add(macroSave); macroSave.Click += delegate { WriteMacros(); };
            events.CellValueChanged += delegate { if (!loading) macroDirty = true; };
            events.UserDeletedRow += delegate { if (!loading) macroDirty = true; };
            slot.SelectedIndexChanged += SlotChanged; slots.ValueChanged += SlotCountChanged;
            status = L("选择一项读取设备状态。", 18, 459, 640); status.Height = S(42); status.ForeColor = Color.FromArgb(155, 184, 205); Controls.Add(status);
            SetBusy(false); FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!busy) events.EndEdit(); if (busy || !ConfirmDiscard()) e.Cancel = true; };
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0219)
            {
                deviceGeneration++; keyLoaded = false; keyPath = null; macros = null; macroPath = null; selectedSlot = -1;
                if (status != null) status.Text = "设备发生变化，请重新读取；之前的编辑不可保存。";
                if (tabs != null && keyRead != null && keySave != null && macroRead != null && macroSave != null) SetBusy(busy);
            }
        }
        private bool Stale(int generation, bool writing)
        {
            if (generation == deviceGeneration) return false;
            status.Text = writing ? "设备在保存过程中发生变化，设备可能已部分更改；请重新读取。" : "设备在读取过程中发生变化，请重新读取。";
            if (writing) MessageBox.Show(this, status.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetBusy(false); return true;
        }
        private int S(int v) { return (int)Math.Round(v * dpiScale); }
        private Label L(string text, int x, int y, int w) { Label c = new Label(); c.Text = text; c.SetBounds(S(x), S(y), S(w), S(25)); c.ForeColor = ForeColor; c.Font = new Font("Microsoft YaHei UI", 9f); return c; }
        private NumericUpDown N(int x, int y, int width, int min, int max) { NumericUpDown n = new NumericUpDown(); n.Minimum = min; n.Maximum = max; n.SetBounds(S(x), S(y), S(width), S(27)); n.BackColor = Color.FromArgb(37, 43, 55); n.ForeColor = ForeColor; return n; }
        private TabPage Page(string text) { TabPage p = new TabPage(text); p.BackColor = Color.FromArgb(22, 27, 37); p.ForeColor = ForeColor; return p; }
        private ModernButton B(string text, int x, int y, int width) { ModernButton b = new ModernButton(); b.Text = text; b.ForeColor = Color.White; b.NormalColor = Color.FromArgb(36,83,101); b.HoverColor = Color.FromArgb(45,108,130); b.PressedColor = Color.FromArgb(27,66,83); b.SetBounds(S(x), S(y), S(width), S(32)); return b; }
        private void SetBusy(bool value)
        {
            busy = value; tabs.Enabled = !busy; keyRead.Enabled = !busy; keySave.Enabled = !busy && keyLoaded && !string.IsNullOrEmpty(keyPath);
            keyType.Enabled = keyCode1.Enabled = keyCode2.Enabled = !busy && keyLoaded;
            macroRead.Enabled = !busy; slots.Enabled = !busy && macros != null; slot.Enabled = !busy && macros != null && macros.Length > 0;
            events.Enabled = addDown.Enabled = addUp.Enabled = !busy && selectedSlot >= 0 && macros != null;
            macroSave.Enabled = !busy && macros != null && !string.IsNullOrEmpty(macroPath);
        }
        private bool ConfirmDiscard() { return !(keyDirty || macroDirty) || MessageBox.Show(this, "有未保存的键位或宏修改，是否放弃？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }
        private void KeyChanged(object sender, EventArgs e) { if (!loading && keyLoaded) keyDirty = true; }
        private void KeyTargetChanged(object sender, EventArgs e)
        {
            if (loading) return;
            if (keyDirty && MessageBox.Show(this, "切换索引会放弃未保存的键位修改，继续？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            { loading = true; layer.Value = lastLayer; index.Value = lastIndex; loading = false; return; }
            lastLayer = (int)layer.Value; lastIndex = (int)index.Value; keyLoaded = keyDirty = false; keyPath = null; status.Text = "请读取所选键位。"; SetBusy(false);
        }
        private int lastLayer, lastIndex;
        private void PresentKey(Wave75KeyBinding key)
        {
            loading = true; keyType.Value = key.Type; keyCode1.Value = key.Code1; keyCode2.Value = key.Code2; loading = false;
            keyLoaded = true; keyDirty = false;
        }
        private void ReadKey()
        {
            if (keyDirty && MessageBox.Show(this, "重新读取会放弃未保存的键位修改，继续？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int l = (int)layer.Value, i = (int)index.Value; SetBusy(true); status.Text = "正在读取所选键位…";
            int generation = deviceGeneration;
            ThreadPool.QueueUserWorkItem(delegate {
                Wave75Result<Wave75KeyBinding> r = null; string error = null;
                try { r = Wave75DeviceHelper.ReadKey(l, i); } catch (Exception ex) { error = ex.Message; }
                BeginInvoke(new Action(delegate { if (Stale(generation, false)) return; if (r != null && r.Success && r.Value != null) { PresentKey(r.Value); keyPath = r.TargetPath; status.Text = string.IsNullOrEmpty(keyPath) ? "无法确认设备身份，请重新读取后再保存。" : "已读取所选键位原始值。"; } else { keyLoaded = keyDirty = false; keyPath = null; status.Text = "读取键位失败：" + (error ?? (r != null ? r.Error : "无返回值")); } SetBusy(false); }));
            });
        }
        private void WriteKey()
        {
            if (!keyLoaded || string.IsNullOrEmpty(keyPath)) return;
            if (MessageBox.Show(this, "将写入所选层和矩阵索引的三个字节。写入失败时设备可能已部分更改；继续？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!keyLoaded || string.IsNullOrEmpty(keyPath)) { status.Text = "设备已变化，请重新读取。"; return; }
            string expectedPath = keyPath; int l = (int)layer.Value, i = (int)index.Value; Wave75KeyBinding value = new Wave75KeyBinding { Type = (byte)keyType.Value, Code1 = (byte)keyCode1.Value, Code2 = (byte)keyCode2.Value };
            SetBusy(true); status.Text = "正在写入并回读所选键位…";
            int generation = deviceGeneration;
            ThreadPool.QueueUserWorkItem(delegate {
                Wave75Result<Wave75KeyBinding> w = null, r = null; string error = null;
                try { w = Wave75DeviceHelper.WriteKey(l, i, value, expectedPath); if (w != null && w.Success && !w.PartialWrite) r = Wave75DeviceHelper.ReadKey(l, i); } catch (Exception ex) { error = ex.Message; }
                BeginInvoke(new Action(delegate {
                    if (Stale(generation, true)) return;
                    if (w != null && w.Success && !w.PartialWrite && r != null && r.Success && r.Value != null && string.Equals(r.TargetPath, expectedPath, StringComparison.OrdinalIgnoreCase)) { PresentKey(r.Value); keyPath = r.TargetPath; status.Text = "已保存，显示设备回读值。"; }
                    else { keyLoaded = keyDirty = false; keyPath = null; status.Text = "键位写入或回读失败，或设备已更换；请重新读取。"; MessageBox.Show(this, "设备可能已部分更改，请重新读取。\n" + (error ?? (w != null && !w.Success ? w.Error : (r != null ? r.Error : "无返回值"))), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    SetBusy(false);
                }));
            });
        }
        private static Wave75Macro EmptyMacro() { return new Wave75Macro { Events = new Wave75MacroEvent[0] }; }
        private void ReadMacros()
        {
            if (macroDirty && MessageBox.Show(this, "重新读取会放弃未保存的宏修改，继续？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SetBusy(true); status.Text = "正在读取设备宏表和容量…";
            int generation = deviceGeneration;
            ThreadPool.QueueUserWorkItem(delegate {
                Wave75Result<Wave75Settings> info = null; Wave75Result<Wave75Macro[]> r = null; string error = null;
                try { info = Wave75DeviceHelper.ReadSettings(); if (info != null && info.Success && info.Value != null && info.Value.Capabilities != null) r = Wave75DeviceHelper.ReadMacros(); } catch (Exception ex) { error = ex.Message; }
                BeginInvoke(new Action(delegate {
                    if (Stale(generation, false)) return;
                    if (info != null && info.Success && info.Value != null && info.Value.Capabilities != null && r != null && r.Success && r.Value != null && !string.IsNullOrEmpty(info.TargetPath) && string.Equals(info.TargetPath, r.TargetPath, StringComparison.OrdinalIgnoreCase))
                    { macroPath = r.TargetPath; capacity = info.Value.Capabilities.MacroSize; macros = r.Value; macroDirty = false; ResetSlots(0); capacityLabel.Text = "设备容量：" + capacity + " 字节"; status.Text = "宏表已读取；事件 Flags 显示原始值。"; }
                    else { macros = null; macroPath = null; selectedSlot = -1; macroDirty = false; capacityLabel.Text = "容量：待读取"; status.Text = "宏表读取失败或设备已更换，请重新读取：" + (error ?? (r != null ? r.Error : (info != null ? info.Error : "无返回值"))); }
                    SetBusy(false);
                }));
            });
        }
        private void ResetSlots(int target)
        {
            loading = true; slots.Value = macros.Length; slot.Items.Clear();
            for (int i = 0; i < macros.Length; i++) slot.Items.Add("槽 " + (i + 1));
            selectedSlot = macros.Length == 0 ? -1 : Math.Min(target, macros.Length - 1); slot.SelectedIndex = selectedSlot;
            ShowSlot(); loading = false;
        }
        private void ShowSlot()
        {
            events.Rows.Clear();
            if (selectedSlot >= 0 && macros != null)
                foreach (Wave75MacroEvent e in macros[selectedSlot].Events) events.Rows.Add(e.DelayUnits, e.Code, e.Flags);
        }
        private static int Number(DataGridViewRow row, int col, int max)
        {
            int result; if (!int.TryParse(Convert.ToString(row.Cells[col].Value, CultureInfo.InvariantCulture), out result) || result < 0 || result > max) throw new ArgumentException("事件数值为空或超出允许范围。"); return result;
        }
        private void StoreSlot()
        {
            if (selectedSlot < 0) return; events.EndEdit(); List<Wave75MacroEvent> values = new List<Wave75MacroEvent>();
            foreach (DataGridViewRow row in events.Rows) if (!row.IsNewRow) values.Add(new Wave75MacroEvent { DelayUnits = (ushort)Number(row, 0, 65535), Code = (byte)Number(row, 1, 255), Flags = (byte)Number(row, 2, 255) });
            macros[selectedSlot].Events = values.ToArray();
        }
        private void SlotChanged(object sender, EventArgs e)
        {
            if (loading || macros == null) return; int target = slot.SelectedIndex;
            try { StoreSlot(); loading = true; selectedSlot = target; ShowSlot(); loading = false; SetBusy(false); }
            catch (Exception ex) { loading = true; slot.SelectedIndex = selectedSlot; loading = false; MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void SlotCountChanged(object sender, EventArgs e)
        {
            if (loading || macros == null) return;
            int generation = deviceGeneration;
            try
            {
                StoreSlot(); int count = (int)slots.Value;
                if (8L + 2L * count > capacity) throw new ArgumentException("槽位表超出设备容量。");
                if (count < macros.Length)
                {
                    DialogResult answer = MessageBox.Show(this, "减少槽位将删除尾部槽位和事件，继续？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (generation != deviceGeneration || macros == null)
                    {
                        status.Text = "设备已变化，请重新读取宏表。";
                        SetBusy(false); return;
                    }
                    if (answer != DialogResult.Yes) { loading = true; slots.Value = macros.Length; loading = false; return; }
                }
                int old = macros.Length; Array.Resize(ref macros, count); for (int i = old; i < count; i++) macros[i] = EmptyMacro();
                macroDirty = true; ResetSlots(Math.Max(0, selectedSlot)); SetBusy(false);
            }
            catch (Exception ex)
            {
                if (generation != deviceGeneration || macros == null)
                {
                    loading = false; status.Text = "设备已变化，请重新读取宏表。"; SetBusy(false); return;
                }
                loading = true; slots.Value = macros.Length; loading = false; MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private void AddEvent(byte flags) { if (selectedSlot < 0) return; events.Rows.Add(0, 0, flags); macroDirty = true; }
        private void WriteMacros()
        {
            if (macros == null || string.IsNullOrEmpty(macroPath)) return;
            try { StoreSlot(); Wave75SettingsProtocol.EncodeMacros(macros, capacity); }
            catch (Exception ex) { MessageBox.Show(this, "无法保存宏表：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show(this, "将重建并覆盖整张宏表；所有槽位都会写入，未知保留字段或布局可能改变。失败时设备可能已部分更改。继续？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (macros == null || string.IsNullOrEmpty(macroPath)) { status.Text = "设备已变化，请重新读取。"; return; }
            string expectedPath = macroPath; Wave75Macro[] value = macros; int current = Math.Max(0, selectedSlot); SetBusy(true); status.Text = "正在写入并回读整张宏表…";
            int generation = deviceGeneration;
            ThreadPool.QueueUserWorkItem(delegate {
                Wave75Result<Wave75Macro[]> w = null, r = null; string error = null;
                try { w = Wave75DeviceHelper.WriteMacros(value, expectedPath); if (w != null && w.Success && !w.PartialWrite) r = Wave75DeviceHelper.ReadMacros(); } catch (Exception ex) { error = ex.Message; }
                BeginInvoke(new Action(delegate {
                    if (Stale(generation, true)) return;
                    if (w != null && w.Success && !w.PartialWrite && r != null && r.Success && r.Value != null && string.Equals(r.TargetPath, expectedPath, StringComparison.OrdinalIgnoreCase)) { macros = r.Value; macroPath = r.TargetPath; macroDirty = false; ResetSlots(current); status.Text = "宏表已保存，显示设备回读值。"; }
                    else { macros = null; macroPath = null; selectedSlot = -1; macroDirty = false; status.Text = "宏表写入或回读失败，或设备已更换；请重新读取。"; MessageBox.Show(this, "宏表可能已部分更改，请重新读取。\n" + (error ?? (w != null && !w.Success ? w.Error : (r != null ? r.Error : "无返回值"))), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    SetBusy(false);
                }));
            });
        }
    }
}
