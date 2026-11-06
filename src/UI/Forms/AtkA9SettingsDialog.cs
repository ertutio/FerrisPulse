// ============================================================================
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 模块名称: AtkA9SettingsDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责: ATK A9 Ultimate (373B:11B6) 的 DPI、侧键与宏设置及回读确认。
// 依赖关系: DpiAwareForm, ModernButton, AtkA9SettingsHelper, AtkA9Settings 模型
// 1. 修改逻辑、属性或绘图逻辑时必须同步更新模块说明。
// 2. 严格兼容 C# 5 / .NET 4；3. 文件必须保存为 UTF-8 with BOM。
// ============================================================================
using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class AtkA9SettingsDialog : DpiAwareForm
    {
        private readonly TabControl tabs = new TabControl();
        private DataGridView dpiGrid, macroGrid;
        private NumericUpDown active, stageCount, actionValue, macroSlot, repeatMode, bindRepeat;
        private ComboBox buttonSelect, actionClass, slotSelect;
        private Label status, actionDetail;
        private ModernButton refresh, saveDpi, saveButton, saveMacro, bindMacro;
        private AtkA9Settings settings;
        private AtkA9Macro[] macros;
        private bool busy, loading, macroDirty, buttonDirty, dpiDirty;
        private int selectedSlot, selectedButton;

        public AtkA9SettingsDialog(float scale = 1.0f) : base(scale)
        {
            Text = "FerrisPulse · ATK A9 Ultimate 设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            BackColor = Color.FromArgb(13, 16, 23); ForeColor = Color.FromArgb(235, 240, 248);
            ClientSize = new Size(S(690), S(590));
            Label title = LabelAt("ATK A9 Ultimate · 设备设置", 20, 14, 640);
            title.Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
            Controls.Add(title);
            Label intro = LabelAt("设备读取成功后可编辑；保存后会重新读取确认。", 20, 44, 640);
            intro.ForeColor = Color.FromArgb(151, 163, 180); Controls.Add(intro);
            tabs.SetBounds(S(20), S(76), S(650), S(410));
            Controls.Add(tabs);
            BuildDpi(); BuildButtons(); BuildMacros();
            status = LabelAt("等待设备读取…", 20, 495, 645); status.Height = S(42);
            status.ForeColor = Color.FromArgb(155, 184, 205); Controls.Add(status);
            refresh = ButtonAt("重新读取", 20, 544, 125);
            refresh.Click += delegate { if (ConfirmDiscardAll()) ReadAll(); }; Controls.Add(refresh);
            SetBusy(false);
            Shown += delegate { ReadAll(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy || !ConfirmDiscardAll()) e.Cancel = true;
            };
        }
        private int S(int n) { return (int)Math.Round(n * dpiScale); }
        private Label LabelAt(string text, int x, int y, int width)
        {
            Label l = new Label(); l.Text = text; l.ForeColor = ForeColor; l.BackColor = Color.Transparent;
            l.Font = new Font("Microsoft YaHei UI", 9f); l.SetBounds(S(x), S(y), S(width), S(25)); return l;
        }
        private ModernButton ButtonAt(string text, int x, int y, int width)
        {
            ModernButton b = new ModernButton(); b.Text = text; b.ForeColor = Color.White;
            b.NormalColor = Color.FromArgb(36, 83, 101); b.HoverColor = Color.FromArgb(45, 108, 130);
            b.PressedColor = Color.FromArgb(27, 66, 83); b.SetBounds(S(x), S(y), S(width), S(32)); return b;
        }
        private NumericUpDown NumberAt(int x, int y, int width, int min, int max)
        {
            NumericUpDown n = new NumericUpDown(); n.SetBounds(S(x), S(y), S(width), S(27));
            n.Minimum = min; n.Maximum = max; n.BackColor = Color.FromArgb(37, 43, 55); n.ForeColor = ForeColor; return n;
        }
        private ComboBox ComboAt(int x, int y, int width, params string[] items)
        {
            ComboBox c = new ComboBox(); c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.BackColor = Color.FromArgb(37, 43, 55); c.ForeColor = ForeColor;
            c.SetBounds(S(x), S(y), S(width), S(26)); c.Items.AddRange(items); c.SelectedIndex = 0; return c;
        }
        private TabPage Page(string name)
        {
            TabPage p = new TabPage(name); p.BackColor = Color.FromArgb(22, 27, 37); p.ForeColor = ForeColor;
            tabs.TabPages.Add(p); return p;
        }
        private DataGridView Grid(int x, int y, int width, int height, bool addRows, params string[] columns)
        {
            DataGridView g = new DataGridView(); g.SetBounds(S(x), S(y), S(width), S(height));
            g.BackgroundColor = Color.FromArgb(22, 27, 37); g.BorderStyle = BorderStyle.None;
            g.DefaultCellStyle.BackColor = Color.FromArgb(37, 43, 55); g.DefaultCellStyle.ForeColor = Color.White;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(36, 83, 101);
            g.EnableHeadersVisualStyles = false; g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 51, 63);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White; g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.RowHeadersVisible = false; g.AllowUserToAddRows = addRows; g.AllowUserToDeleteRows = addRows;
            foreach (string c in columns) g.Columns.Add(c, c);
            g.CellValueChanged += delegate { if (!loading) { if (g == dpiGrid) dpiDirty = true; else macroDirty = true; } };
            g.UserDeletedRow += delegate { if (!loading && g == macroGrid) macroDirty = true; };
            return g;
        }
        private void BuildDpi()
        {
            TabPage p = Page("DPI 档位");
            p.Controls.Add(LabelAt("最多 8 档；DPI 10–42000，按区间以 10、50 或 100 为步长。", 16, 14, 580));
            dpiGrid = Grid(16, 46, 605, 250, false, "档位", "X DPI", "Y DPI");
            dpiGrid.Columns[0].ReadOnly = true; p.Controls.Add(dpiGrid);
            p.Controls.Add(LabelAt("当前档位", 16, 310, 90));
            p.Controls.Add(LabelAt("档位数", 205, 310, 65));
            stageCount = NumberAt(270, 308, 70, 1, 8); p.Controls.Add(stageCount);
            stageCount.ValueChanged += delegate { if (!loading) ChangeStageCount(); };
            active = NumberAt(110, 308, 70, 1, 8); active.ValueChanged += delegate { if (!loading) dpiDirty = true; }; p.Controls.Add(active);
            saveDpi = ButtonAt("保存 DPI", 470, 332, 150); saveDpi.Click += delegate { SaveDpi(); }; p.Controls.Add(saveDpi);
        }
        private void BuildButtons()
        {
            TabPage p = Page("侧键");
            p.Controls.Add(LabelAt("选择侧键", 16, 18, 110));
            buttonSelect = ComboAt(130, 15, 180, "侧键 4（设备侧键 1）", "侧键 5（设备侧键 2）"); p.Controls.Add(buttonSelect);
            buttonSelect.SelectedIndexChanged += ButtonChanged;
            p.Controls.Add(LabelAt("动作类型", 16, 60, 110));
            actionClass = ComboAt(130, 57, 180, "鼠标按键", "DPI 动作", "宏", "禁用"); p.Controls.Add(actionClass);
            actionClass.SelectedIndexChanged += delegate { if (!loading) buttonDirty = true; UpdateActionHint(); };
            p.Controls.Add(LabelAt("动作参数", 16, 102, 110)); actionValue = NumberAt(130, 99, 100, 0, 256); p.Controls.Add(actionValue);
            actionValue.ValueChanged += delegate { if (!loading) buttonDirty = true; };
            p.Controls.Add(LabelAt("宏重复模式", 16, 144, 110)); repeatMode = NumberAt(130, 141, 100, 0, 255); p.Controls.Add(repeatMode);
            repeatMode.ValueChanged += delegate { if (!loading) buttonDirty = true; };
            actionDetail = LabelAt("", 16, 192, 605); actionDetail.Height = S(70); actionDetail.ForeColor = Color.FromArgb(151, 163, 180); p.Controls.Add(actionDetail);
            saveButton = ButtonAt("保存当前侧键", 455, 332, 165); saveButton.Click += delegate { SaveButton(); }; p.Controls.Add(saveButton);
            UpdateActionHint();
        }
        private void BuildMacros()
        {
            TabPage p = Page("宏槽");
            p.Controls.Add(LabelAt("宏槽", 16, 14, 75));
            slotSelect = ComboAt(87, 11, 145, "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12"); p.Controls.Add(slotSelect);
            slotSelect.SelectedIndexChanged += SlotChanged;
            Label note = LabelAt("类型 1 键盘、0 修饰键、4 鼠标；状态 128 按下、64 抬起。\n参数1：键盘 HID 用法码 / 鼠标位值；参数2 通常为 0；延迟为毫秒。", 16, 45, 610);
            note.Height = S(44); note.ForeColor = Color.FromArgb(151, 163, 180); p.Controls.Add(note);
            macroGrid = Grid(16, 92, 605, 194, true, "类型", "状态", "参数1", "参数2", "延迟"); p.Controls.Add(macroGrid);
            p.Controls.Add(LabelAt("绑定至侧键", 16, 301, 105));
            macroSlot = NumberAt(125, 298, 65, 4, 5); p.Controls.Add(macroSlot);
            p.Controls.Add(LabelAt("重复", 205, 301, 50)); bindRepeat = NumberAt(250, 298, 65, 1, 255); p.Controls.Add(bindRepeat);
            ToolTip hints = new ToolTip();
            hints.SetToolTip(bindRepeat, "1–252：循环次数；253：同键停止；254：松开停止；255：任意键停止。权限以设备支持为准。");
            hints.SetToolTip(macroGrid, "键盘使用 USB HID usage；鼠标位值 1 左、2 右、4 中、8/16 侧键。其他类型可保留原始值。");
            bindMacro = ButtonAt("绑定当前宏", 330, 296, 155); bindMacro.Click += delegate { BindMacro(); }; p.Controls.Add(bindMacro);
            saveMacro = ButtonAt("保存当前宏", 455, 332, 165); saveMacro.Click += delegate { SaveMacro(); }; p.Controls.Add(saveMacro);
        }
        private void UpdateActionHint()
        {
            if (actionDetail == null) return;
            actionDetail.Text = actionClass.SelectedIndex == 0 ? "鼠标按键参数：1 左键，2 右键，4 中键，8 / 16 侧键。" :
                (actionClass.SelectedIndex == 1 ? "DPI 参数：1 循环、2 增加、3 减少。" :
                (actionClass.SelectedIndex == 2 ? "宏槽编号 1–12；重复模式 1–252 为次数，253 同键停止，254 松开停止，255 任意键停止。" : "禁用此侧键。"));
            repeatMode.Enabled = actionClass.SelectedIndex == 2;
        }
        private bool ConfirmDiscard(string what)
        {
            return MessageBox.Show(this, what + "有未保存的修改，是否放弃？", "ATK A9 设置", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
        private bool ConfirmDiscardAll()
        {
            return !(dpiDirty || buttonDirty || macroDirty) || ConfirmDiscard("设置");
        }
        private void SetBusy(bool value)
        {
            busy = value; tabs.Enabled = !busy && settings != null; refresh.Enabled = !busy;
            if (tabs.TabPages.Count > 2) tabs.TabPages[2].Enabled = !busy && settings != null && macros != null;
        }
        private void ButtonChanged(object sender, EventArgs e)
        {
            if (loading || settings == null) return;
            if (buttonDirty && !ConfirmDiscard("当前侧键")) { loading = true; buttonSelect.SelectedIndex = selectedButton; loading = false; return; }
            selectedButton = buttonSelect.SelectedIndex; DisplayButton();
        }
        private void SlotChanged(object sender, EventArgs e)
        {
            if (loading || macros == null) return;
            if (macroDirty && !ConfirmDiscard("当前宏槽")) { loading = true; slotSelect.SelectedIndex = selectedSlot; loading = false; return; }
            selectedSlot = slotSelect.SelectedIndex; DisplayMacro();
        }
        private void DisplaySettings()
        {
            loading = true; dpiGrid.Rows.Clear();
            for (int i = 0; i < settings.Stages.Length; i++) dpiGrid.Rows.Add((i + 1).ToString(), settings.Stages[i].X.ToString(), settings.Stages[i].Y.ToString());
            stageCount.Value = settings.Stages.Length; active.Value = settings.ActiveStage + 1; dpiDirty = false;
            loading = false; DisplayButton();
        }
        private void DisplayButton()
        {
            AtkA9ButtonAction a = settings.SideButtons[selectedButton]; loading = true;
            actionClass.SelectedIndex = a.Class == 1 ? 0 : (a.Class == 2 ? 1 : (a.Class == 6 ? 2 : (a.Class == 0 ? 3 : -1)));
            actionValue.Value = a.Class == 6 ? a.Value1 + 1 : a.Value1; repeatMode.Value = a.Value2; loading = false; buttonDirty = false;
            UpdateActionHint();
            if (actionClass.SelectedIndex < 0) actionDetail.Text = "当前动作类型 " + a.Class + " 暂不支持编辑；请勿保存，除非选择替代动作。";
        }
        private void DisplayMacro()
        {
            loading = true; macroGrid.Rows.Clear();
            if (macros[selectedSlot] != null && macros[selectedSlot].Events != null)
                foreach (AtkA9MacroEvent ev in macros[selectedSlot].Events) macroGrid.Rows.Add(ev.Type, ev.State, ev.Value1, ev.Value2, ev.Delay);
            loading = false; macroDirty = false;
        }

        private void ChangeStageCount()
        {
            int count = (int)stageCount.Value;
            while (dpiGrid.Rows.Count > count) dpiGrid.Rows.RemoveAt(dpiGrid.Rows.Count - 1);
            while (dpiGrid.Rows.Count < count)
            {
                int i = dpiGrid.Rows.Count;
                object x = i > 0 ? dpiGrid.Rows[i - 1].Cells[1].Value : "800";
                object y = i > 0 ? dpiGrid.Rows[i - 1].Cells[2].Value : "800";
                dpiGrid.Rows.Add((i + 1).ToString(), x, y);
            }
            if (active.Value > count) active.Value = count;
            dpiDirty = true;
        }
        private sealed class ReadResult { public AtkA9Settings Settings; public AtkA9Macro[] Macros; public string MacroError; }
        private void Run(Func<object> work, Action<object> complete, bool writing)
        {
            if (busy) return;
            SetBusy(true); status.Text = writing ? "正在保存并核对设备…" : "正在读取设备…";
            ThreadPool.QueueUserWorkItem(delegate {
                object value = null; Exception error = null;
                try { value = work(); } catch (Exception ex) { error = ex; }
                BeginInvoke(new Action(delegate {
                    if (error == null)
                    {
                        try { complete(value); }
                        catch (Exception ex) { error = ex; }
                    }
                    if (error != null)
                    {
                        if (writing)
                        {
                            settings = null; macros = null;
                            dpiDirty = buttonDirty = macroDirty = false;
                            status.Text = "保存或回读失败，设备状态未确认。请重新读取。";
                            MessageBox.Show(this, "设备可能已部分写入或未能回读，请重新读取后检查。\n" + error.Message,
                                "ATK A9 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        else
                        {
                            settings = null; macros = null; status.Text = "读取失败：" + error.Message;
                        }
                    }
                    SetBusy(false);
                }));
            });
        }
        private void ReadAll()
        {
            Run(delegate {
                using (AtkA9SettingsHelper h = AtkA9SettingsHelper.OpenConnected(0x373B, 0x11B6))
                {
                    ReadResult r = new ReadResult(); r.Settings = h.ReadSettings();
                    try { r.Macros = h.ReadMacros(); } catch (Exception ex) { r.MacroError = ex.Message; }
                    return r;
                }
            }, delegate(object value) {
                ReadResult r = (ReadResult)value; settings = r.Settings; macros = r.Macros;
                DisplaySettings(); if (macros != null) DisplayMacro(); else macroDirty = false;
                status.Text = r.MacroError == null ? "设备状态已读取。" : "DPI 和侧键已读取；宏读取失败：" + r.MacroError;
            }, false);
        }
        private static int CellNumber(DataGridViewRow row, int column, string name)
        {
            int n;
            if (!int.TryParse(Convert.ToString(row.Cells[column].Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                throw new ArgumentException(name + "必须是整数。");
            return n;
        }
        private void SaveDpi()
        {
            try
            {
                dpiGrid.EndEdit(); AtkA9DpiStage[] stages = new AtkA9DpiStage[dpiGrid.Rows.Count];
                for (int i = 0; i < stages.Length; i++)
                {
                    stages[i] = new AtkA9DpiStage { X = CellNumber(dpiGrid.Rows[i], 1, "X DPI"), Y = CellNumber(dpiGrid.Rows[i], 2, "Y DPI") };
                    AtkA9SettingsHelper.EncodeDpi(stages[i]);
                }
                int chosen = (int)active.Value - 1;
                if (chosen >= stages.Length) throw new ArgumentException("当前档位不能超过档位数。");
                Run(delegate { using (AtkA9SettingsHelper h = AtkA9SettingsHelper.OpenConnected(0x373B, 0x11B6)) { h.SaveDpi(stages, chosen); return h.ReadSettings(); } },
                    delegate(object result) {
                        AtkA9Settings actual = (AtkA9Settings)result;
                        settings.Stages = actual.Stages; settings.ActiveStage = actual.ActiveStage;
                        loading = true; dpiGrid.Rows.Clear();
                        for (int i = 0; i < settings.Stages.Length; i++) dpiGrid.Rows.Add(i + 1, settings.Stages[i].X, settings.Stages[i].Y);
                        stageCount.Value = settings.Stages.Length; active.Value = settings.ActiveStage + 1;
                        loading = false; dpiDirty = false; status.Text = "DPI 已保存，显示设备回读值。";
                    }, true);
            }
            catch (Exception ex) { MessageBox.Show(this, "DPI 范围 10–42000；10000 以下步长 10，10050–30000 步长 50，30100–42000 步长 100。\n" + ex.Message, "ATK A9 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void SaveButton()
        {
            try
            {
                int action = (int)actionValue.Value, mode = (int)repeatMode.Value;
                AtkA9ButtonAction value;
                if (actionClass.SelectedIndex == 0) value = AtkA9ButtonAction.Mouse(action);
                else if (actionClass.SelectedIndex == 1) value = AtkA9ButtonAction.Dpi(action);
                else if (actionClass.SelectedIndex == 2) value = AtkA9ButtonAction.Macro(action - 1, mode);
                else if (actionClass.SelectedIndex == 3) value = new AtkA9ButtonAction { Class = 0, Value1 = 0, Value2 = 0 };
                else throw new ArgumentException("请选择支持的动作。");
                AtkA9SettingsHelper.EncodeButton(value);
                int target = selectedButton;
                Run(delegate { using (AtkA9SettingsHelper h = AtkA9SettingsHelper.OpenConnected(0x373B, 0x11B6)) { h.SaveSideButton(target, value); return h.ReadSettings(); } },
                    delegate(object result) { settings.SideButtons = ((AtkA9Settings)result).SideButtons; DisplayButton(); status.Text = "侧键已保存，显示设备回读值。"; }, true);
            }
            catch (Exception ex) { MessageBox.Show(this, "侧键参数无效。\n" + ex.Message, "ATK A9 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void SaveMacro()
        {
            try
            {
                macroGrid.EndEdit(); System.Collections.Generic.List<AtkA9MacroEvent> events = new System.Collections.Generic.List<AtkA9MacroEvent>();
                foreach (DataGridViewRow row in macroGrid.Rows)
                {
                    if (row.IsNewRow) continue;
                    events.Add(new AtkA9MacroEvent { Type = CellNumber(row, 0, "类型"), State = CellNumber(row, 1, "状态"), Value1 = CellNumber(row, 2, "参数1"), Value2 = CellNumber(row, 3, "参数2"), Delay = CellNumber(row, 4, "延迟") });
                }
                AtkA9Macro value = new AtkA9Macro { Events = events.ToArray() };
                AtkA9SettingsHelper.EncodeMacro(value); int slot = selectedSlot;
                Run(delegate { using (AtkA9SettingsHelper h = AtkA9SettingsHelper.OpenConnected(0x373B, 0x11B6)) { h.SaveMacro(slot, value); return h.ReadMacros(); } },
                    delegate(object result) { macros = (AtkA9Macro[])result; DisplayMacro(); status.Text = "宏已保存，显示设备回读值。"; }, true);
            }
            catch (Exception ex) { MessageBox.Show(this, "宏最多 70 行；类型 0–6，状态 0 / 64 / 128，参数 0–255，延迟 0–65535 毫秒。\n" + ex.Message, "ATK A9 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void BindMacro()
        {
            if (macroDirty) { MessageBox.Show(this, "请先保存当前宏，再绑定侧键。", "ATK A9 设置", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (buttonDirty && !ConfirmDiscard("侧键")) return;
            int target = (int)macroSlot.Value - 4, slot = selectedSlot, mode = (int)bindRepeat.Value;
            Run(delegate { using (AtkA9SettingsHelper h = AtkA9SettingsHelper.OpenConnected(0x373B, 0x11B6)) { h.BindMacro(target, slot, mode); return h.ReadSettings(); } },
                delegate(object result) {
                    settings.SideButtons = ((AtkA9Settings)result).SideButtons;
                    loading = true; buttonSelect.SelectedIndex = target; selectedButton = target; loading = false;
                    DisplayButton(); status.Text = "宏已绑定，侧键状态已回读。";
                }, true);
        }
    }
}
