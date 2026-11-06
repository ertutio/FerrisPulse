// ============================================================================
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 模块名称: Wave75SettingsDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责: 读取 WAVE75 灯光，编辑并回读；提供键位和宏高级设置入口。
// 依赖关系: DpiAwareForm, ModernButton, Wave75DeviceHelper
//
// 1. [契约同步更新]: 修改逻辑、属性或绘图逻辑时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为 UTF-8 with BOM。
// ============================================================================
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class Wave75SettingsDialog : DpiAwareForm
    {
        private ComboBox area;
        private CheckBox enabled;
        private NumericUpDown colorful;
        private NumericUpDown mode, brightness, speed, colorIndex, red, green, blue, customIndex;
        private Label status;
        private Panel editor;
        private ModernButton save, refresh, advanced;
        private Wave75Settings confirmed;
        private string confirmedPath;
        private bool busy, populating, dirty;
        private int selectedArea, deviceGeneration;

        public Wave75SettingsDialog(float scale = 1.0f) : base(scale)
        {
            Text = "FerrisPulse · WAVE75 灯光设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(13, 16, 23);
            ForeColor = Color.FromArgb(235, 240, 248);
            ClientSize = new Size(S(510), S(485));

            Label title = MakeLabel("WAVE75 · 灯光设置", 20, 15, 465);
            title.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            Controls.Add(title);
            Label subtitle = MakeLabel("先读取设备状态，修改后按分区保存并回读确认。", 20, 43, 465);
            subtitle.ForeColor = Color.FromArgb(151, 163, 180);
            Controls.Add(subtitle);
            Controls.Add(MakeLabel("灯光分区", 20, 78, 90));
            area = new ComboBox();
            area.DropDownStyle = ComboBoxStyle.DropDownList;
            area.BackColor = Color.FromArgb(32, 38, 50);
            area.ForeColor = ForeColor;
            area.SetBounds(S(115), S(75), S(365), S(28));
            area.Items.AddRange(new object[] { "主灯区", "Logo 灯区", "侧灯区" });
            area.SelectedIndex = 0;
            area.SelectedIndexChanged += AreaChanged;
            Controls.Add(area);

            editor = new Panel();
            editor.SetBounds(S(20), S(112), S(460), S(275));
            editor.BackColor = Color.FromArgb(22, 27, 37);
            Controls.Add(editor);
            enabled = MakeCheck("启用灯光", 16, 14);
            editor.Controls.Add(enabled);
            colorful = AddNumber("多彩", 250, 14, 0, 255);
            mode = AddNumber("模式代码", 16, 53, 0, 255);
            customIndex = AddNumber("自定义槽位 (0–4)", 250, 53, 0, 255);
            brightness = AddNumber("亮度", 16, 94, 0, 255);
            speed = AddNumber("速度", 250, 94, -255, 255);
            colorIndex = AddNumber("颜色索引", 16, 135, 0, 255);
            red = AddNumber("红色 (R)", 250, 135, 0, 255);
            green = AddNumber("绿色 (G)", 16, 176, 0, 255);
            blue = AddNumber("蓝色 (B)", 250, 176, 0, 255);
            Label hint = MakeLabel("参数为设备原始数值；模式 253 (0xFD) 使用自定义槽位。", 16, 225, 425);
            hint.ForeColor = Color.FromArgb(151, 163, 180);
            editor.Controls.Add(hint);
            enabled.CheckedChanged += Changed;
            colorful.ValueChanged += Changed;
            mode.ValueChanged += Changed;
            customIndex.ValueChanged += Changed;
            brightness.ValueChanged += Changed;
            speed.ValueChanged += Changed;
            colorIndex.ValueChanged += Changed;
            red.ValueChanged += Changed;
            green.ValueChanged += Changed;
            blue.ValueChanged += Changed;

            status = MakeLabel("正在读取设备…", 20, 393, 465);
            status.Height = S(35);
            status.ForeColor = Color.FromArgb(155, 184, 205);
            Controls.Add(status);
            refresh = MakeButton("重新读取", 20, 437, 120);
            refresh.Click += delegate { if (ConfirmDiscard()) StartRead(); };
            Controls.Add(refresh);
            advanced = MakeButton("键位与宏…", 153, 437, 155);
            advanced.Click += delegate { using (Wave75KeyMacroDialog dialog = new Wave75KeyMacroDialog(dpiScale)) dialog.ShowDialog(this); };
            Controls.Add(advanced);
            save = MakeButton("保存当前分区", 322, 437, 158);
            save.Click += delegate { StartWrite(); };
            Controls.Add(save);
            SetBusy(false);
            Shown += delegate { StartRead(); };
            FormClosing += OnClosing;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0219)
            {
                deviceGeneration++; confirmed = null; confirmedPath = null;
                if (status != null) status.Text = "设备发生变化，请重新读取；之前的编辑不可保存。";
                if (refresh != null && save != null && advanced != null) SetBusy(busy);
            }
        }
        private bool Stale(int generation, bool writing)
        {
            if (generation == deviceGeneration) return false;
            status.Text = writing ? "设备在保存过程中发生变化，设备可能已部分更改；请重新读取。" : "设备在读取过程中发生变化，请重新读取。";
            if (writing) MessageBox.Show(this, status.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetBusy(false); return true;
        }
        private int S(int value) { return (int)Math.Round(value * dpiScale); }
        private Label MakeLabel(string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.SetBounds(S(x), S(y), S(width), S(25));
            label.ForeColor = ForeColor;
            label.BackColor = Color.Transparent;
            label.Font = new Font("Microsoft YaHei UI", 9f);
            return label;
        }
        private CheckBox MakeCheck(string text, int x, int y)
        {
            CheckBox check = new CheckBox();
            check.Text = text;
            check.ForeColor = ForeColor;
            check.BackColor = Color.Transparent;
            check.SetBounds(S(x), S(y), S(190), S(25));
            return check;
        }
        private NumericUpDown AddNumber(string text, int x, int y, int min, int max)
        {
            editor.Controls.Add(MakeLabel(text, x, y + 3, 135));
            NumericUpDown number = new NumericUpDown();
            number.Minimum = min;
            number.Maximum = max;
            number.BackColor = Color.FromArgb(37, 43, 55);
            number.ForeColor = ForeColor;
            number.SetBounds(S(x + 140), S(y), S(65), S(27));
            editor.Controls.Add(number);
            return number;
        }
        private ModernButton MakeButton(string text, int x, int y, int width)
        {
            ModernButton button = new ModernButton();
            button.Text = text;
            button.ForeColor = Color.White;
            button.NormalColor = Color.FromArgb(36, 83, 101);
            button.HoverColor = Color.FromArgb(45, 108, 130);
            button.PressedColor = Color.FromArgb(27, 66, 83);
            button.SetBounds(S(x), S(y), S(width), S(32));
            return button;
        }
        private void SetBusy(bool value)
        {
            busy = value;
            area.Enabled = !busy && confirmed != null;
            editor.Enabled = !busy && HasArea();
            save.Enabled = !busy && HasArea() && !string.IsNullOrEmpty(confirmedPath);
            refresh.Enabled = !busy;
            advanced.Enabled = !busy;
        }
        private bool HasArea()
        {
            if (confirmed == null || confirmed.Capabilities == null) return false;
            return selectedArea == 0 ? confirmed.Capabilities.HasMain && confirmed.Main != null :
                (selectedArea == 1 ? confirmed.Capabilities.HasLogo && confirmed.Logo != null : confirmed.Capabilities.HasSide && confirmed.Side != null);
        }
        private void Changed(object sender, EventArgs e)
        {
            if (!populating) dirty = true;
            customIndex.Enabled = mode.Value == 253;
        }
        private bool ConfirmDiscard()
        {
            return !dirty || MessageBox.Show(this, "当前分区有未保存的修改，是否放弃？", "WAVE75 灯光设置", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
        private void AreaChanged(object sender, EventArgs e)
        {
            if (populating || confirmed == null) return;
            if (!ConfirmDiscard())
            {
                populating = true;
                area.SelectedIndex = selectedArea;
                populating = false;
                return;
            }
            selectedArea = area.SelectedIndex;
            DisplayConfirmed();
            SetBusy(false);
        }
        private void DisplayConfirmed()
        {
            if (!HasArea())
            {
                dirty = false;
                status.Text = "设备未报告此灯光分区。";
                return;
            }
            Wave75LightSettings light = selectedArea == 0 ? confirmed.Main : (selectedArea == 1 ? confirmed.Logo : confirmed.Side);
            populating = true;
            enabled.Checked = light.Enabled;
            colorful.Value = light.Colorful;
            mode.Value = light.Mode;
            brightness.Value = light.Brightness;
            speed.Value = light.Speed;
            colorIndex.Value = light.ColorIndex;
            red.Value = light.Red;
            green.Value = light.Green;
            blue.Value = light.Blue;
            customIndex.Value = light.CustomIndex;
            customIndex.Enabled = mode.Value == 253;
            populating = false;
            dirty = false;
        }
        private Wave75LightSettings GetEdits()
        {
            return new Wave75LightSettings {
                Enabled = enabled.Checked, Colorful = (byte)colorful.Value,
                Mode = (byte)mode.Value, Brightness = (byte)brightness.Value,
                Speed = (int)speed.Value, ColorIndex = (byte)colorIndex.Value,
                Red = (byte)red.Value, Green = (byte)green.Value, Blue = (byte)blue.Value,
                CustomIndex = (byte)customIndex.Value
            };
        }
        private void StartRead()
        {
            if (busy) return;
            SetBusy(true);
            status.Text = "正在读取设备…";
            int generation = deviceGeneration;
            ThreadPool.QueueUserWorkItem(delegate {
                Wave75Result<Wave75Settings> result = null;
                string error = null;
                try { result = Wave75DeviceHelper.ReadSettings(); }
                catch (Exception ex) { error = ex.Message; }
                BeginInvoke(new Action(delegate {
                    if (Stale(generation, false)) return;
                    if (result != null && result.Success && result.Value != null)
                    {
                        confirmed = result.Value;
                        confirmedPath = result.TargetPath;
                        DisplayConfirmed();
                        status.Text = string.IsNullOrEmpty(confirmedPath) ? "无法确认设备身份，请重新读取后再保存。" : (HasArea() ? "已读取设备状态。" : "设备未报告此灯光分区。");
                    }
                    else
                    {
                        confirmed = null;
                        confirmedPath = null;
                        dirty = false;
                        status.Text = "读取失败：" + (error ?? (result != null ? result.Error : "没有返回结果"));
                    }
                    SetBusy(false);
                }));
            });
        }
        private void StartWrite()
        {
            if (busy || confirmed == null || string.IsNullOrEmpty(confirmedPath)) return;
            int selectedMode = (int)mode.Value;
            if ((selectedArea == 0 && !((selectedMode >= 1 && selectedMode <= 20) || selectedMode == 253)) ||
                (selectedArea != 0 && (selectedMode < 1 || selectedMode > 4)) ||
                (selectedMode == 253 && customIndex.Value > 4))
            {
                MessageBox.Show(this, "主灯模式为 1–20 或 253，自定义槽位为 0–4；Logo 和侧灯模式为 1–4。", "WAVE75 灯光设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int maxBrightness = selectedArea == 0 ? confirmed.Capabilities.MainMaxBrightness : (selectedArea == 1 ? confirmed.Capabilities.LogoMaxBrightness : confirmed.Capabilities.SideMaxBrightness);
            int maxSpeed = selectedArea == 0 ? confirmed.Capabilities.MainMaxSpeed : (selectedArea == 1 ? confirmed.Capabilities.LogoMaxSpeed : confirmed.Capabilities.SideMaxSpeed);
            if (brightness.Value > maxBrightness || speed.Value < 0 || speed.Value > maxSpeed || colorful.Value > 1)
            {
                MessageBox.Show(this, string.Format("设备报告此分区亮度上限为 {0}，速度范围为 0–{1}，多彩值为 0 或 1。", maxBrightness, maxSpeed), "WAVE75 灯光设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string expectedPath = confirmedPath;
            Wave75LightSettings edits = GetEdits();
            Wave75LightArea target = selectedArea == 0 ? Wave75LightArea.Main : (selectedArea == 1 ? Wave75LightArea.Logo : Wave75LightArea.Side);
            SetBusy(true);
            status.Text = "正在保存并回读设备…";
            int generation = deviceGeneration;
            ThreadPool.QueueUserWorkItem(delegate {
                Wave75Result<Wave75Settings> written = null;
                Wave75Result<Wave75Settings> read = null;
                string error = null;
                try
                {
                    written = Wave75DeviceHelper.WriteLight(target, edits, expectedPath);
                    if (written != null && written.Success && !written.PartialWrite)
                        read = Wave75DeviceHelper.ReadSettings();
                }
                catch (Exception ex) { error = ex.Message; }
                BeginInvoke(new Action(delegate {
                    if (Stale(generation, true)) return;
                    if (written != null && written.Success && !written.PartialWrite && read != null && read.Success && read.Value != null &&
                        string.Equals(read.TargetPath, expectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        confirmed = read.Value;
                        DisplayConfirmed();
                        status.Text = "已保存，显示设备回读值。";
                    }
                    else
                    {
                        string detail = error ?? (written == null ? "没有返回结果" : (!written.Success || written.PartialWrite ? written.Error : (read != null ? read.Error : "回读失败")));
                        string message = written != null && written.PartialWrite ? "部分写入，设备状态可能已改变。" : "保存或回读失败，或设备已更换；状态未确认。";
                        confirmed = null;
                        confirmedPath = null;
                        dirty = false;
                        status.Text = message + " 请重新读取。";
                        MessageBox.Show(this, message + "\n" + detail + "\n请重新读取设备状态。", "WAVE75 灯光设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    SetBusy(false);
                }));
            });
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; return; }
            if (!ConfirmDiscard()) e.Cancel = true;
        }
    }
}
