using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MissionPlanner.Warnings
{
    public class WarningControl : UserControl
    {
        public event EventHandler ReloadList;

        public WarningControl()
        {
            InitializeComponent();
        }

        public WarningControl(CustomWarning item)
        {
            InitializeComponent();

            CustomWarning.defaultsrc = MainV2.comPort.MAV.cs;
            item.SetField(item.Name);

            CMB_condition.DataSource = Enum.GetNames(typeof(CustomWarning.Conditional));
            CMB_color.DataSource = Enum.GetNames(typeof(CustomWarning.WarningColors));

            CMB_Source.DataSource = item.GetOptions();

            var options2 = new List<string> { NoExtraField };
            options2.AddRange(item.GetOptions());
            CMB_Source2.DataSource = options2;

            var options3 = new List<string> { NoExtraField };
            options3.AddRange(item.GetOptions());
            CMB_Source3.DataSource = options3;

            CMB_Op1.DataSource = new List<string>(OperatorDisplay);
            CMB_Op2.DataSource = new List<string>(OperatorDisplay);

            custwarning = item;

            updateDisplay();
        }

        private const string NoExtraField = "(none)";
        private const string NoSecondField = NoExtraField; // backwards-compat alias

        private static readonly string[] OperatorDisplay = new[] { "", "+", "-", "*", "/", "%" };

        private static CustomWarning.MathOp OpFromDisplay(string s)
        {
            switch (s)
            {
                case "+": return CustomWarning.MathOp.ADD;
                case "-": return CustomWarning.MathOp.SUB;
                case "*": return CustomWarning.MathOp.MUL;
                case "/": return CustomWarning.MathOp.DIV;
                case "%": return CustomWarning.MathOp.MOD;
                default: return CustomWarning.MathOp.NONE;
            }
        }

        private static string DisplayFromOp(CustomWarning.MathOp op)
        {
            switch (op)
            {
                case CustomWarning.MathOp.ADD: return "+";
                case CustomWarning.MathOp.SUB: return "-";
                case CustomWarning.MathOp.MUL: return "*";
                case CustomWarning.MathOp.DIV: return "/";
                case CustomWarning.MathOp.MOD: return "%";
                default: return "";
            }
        }

        public void updateDisplay()
        {

            CMB_condition.Text = custwarning.ConditionType.ToString();
            CMB_Source.Text = custwarning.Name;
            CMB_Source2.Text = string.IsNullOrEmpty(custwarning.Name2) ? NoExtraField : custwarning.Name2;
            CMB_Source3.Text = string.IsNullOrEmpty(custwarning.Name3) ? NoExtraField : custwarning.Name3;
            CMB_Op1.Text = DisplayFromOp(custwarning.Operator1);
            CMB_Op2.Text = DisplayFromOp(custwarning.Operator2);
            NUM_warning.Value = (decimal)custwarning.Warning;

            NUM_warning.Enabled = custwarning.HasExpression || !custwarning.HasSecondField;

            if (custwarning.type == CustomWarning.WarningType.SpeakAndText)
            {
                //Fire the checkedchange event to enable/disable fields
                CB_type.Checked = false;
                CB_type_CheckedChanged(null, EventArgs.Empty);

                NUM_repeattime.Value = custwarning.RepeatTime;
                TXT_warningtext.Text = custwarning.Text;
                CMB_color.Text = "NoColor";
                             
            }
            else
            {
                //Fire the checkedchange event to enable/disable fields
                CB_type.Checked = true;
                CB_type_CheckedChanged(null, EventArgs.Empty);               
                CMB_color.Text = custwarning.color;
            }

        }

        CustomWarning _custwarn;

        public CustomWarning custwarning
        {
            get { return _custwarn; }
            set { _custwarn = value; }
        }

        private ComboBox CMB_condition;
        private NumericUpDown NUM_warning;
        private NumericUpDown NUM_repeattime;
        public TextBox TXT_warningtext;
        private Controls.MyButton but_addchild;
        private Controls.MyButton but_remove;
        private CheckBox CB_type;
        private ComboBox CMB_color;
        private ComboBox CMB_Source;
        private ComboBox CMB_Source2;
        private ComboBox CMB_Source3;
        private ComboBox CMB_Op1;
        private ComboBox CMB_Op2;

        private void InitializeComponent()
        {
            this.CMB_Source = new System.Windows.Forms.ComboBox();
            this.CMB_Op1 = new System.Windows.Forms.ComboBox();
            this.CMB_Source2 = new System.Windows.Forms.ComboBox();
            this.CMB_Op2 = new System.Windows.Forms.ComboBox();
            this.CMB_Source3 = new System.Windows.Forms.ComboBox();
            this.CMB_condition = new System.Windows.Forms.ComboBox();
            this.NUM_warning = new System.Windows.Forms.NumericUpDown();
            this.NUM_repeattime = new System.Windows.Forms.NumericUpDown();
            this.TXT_warningtext = new System.Windows.Forms.TextBox();
            this.but_addchild = new MissionPlanner.Controls.MyButton();
            this.but_remove = new MissionPlanner.Controls.MyButton();
            this.CB_type = new System.Windows.Forms.CheckBox();
            this.CMB_color = new System.Windows.Forms.ComboBox();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_warning)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_repeattime)).BeginInit();
            this.SuspendLayout();
            //
            // CMB_Source (Field 1)
            //
            this.CMB_Source.FormattingEnabled = true;
            this.CMB_Source.Location = new System.Drawing.Point(131, 3);
            this.CMB_Source.Name = "CMB_Source";
            this.CMB_Source.Size = new System.Drawing.Size(100, 21);
            this.CMB_Source.TabIndex = 0;
            this.CMB_Source.Text = "gpsstat";
            this.CMB_Source.SelectedIndexChanged += new System.EventHandler(this.CMB_Source_SelectedIndexChanged);
            //
            // CMB_Op1
            //
            this.CMB_Op1.FormattingEnabled = true;
            this.CMB_Op1.Location = new System.Drawing.Point(235, 3);
            this.CMB_Op1.Name = "CMB_Op1";
            this.CMB_Op1.Size = new System.Drawing.Size(42, 21);
            this.CMB_Op1.TabIndex = 10;
            this.CMB_Op1.SelectedIndexChanged += new System.EventHandler(this.CMB_Op1_SelectedIndexChanged);
            //
            // CMB_Source2 (Field 2)
            //
            this.CMB_Source2.FormattingEnabled = true;
            this.CMB_Source2.Location = new System.Drawing.Point(281, 3);
            this.CMB_Source2.Name = "CMB_Source2";
            this.CMB_Source2.Size = new System.Drawing.Size(100, 21);
            this.CMB_Source2.TabIndex = 11;
            this.CMB_Source2.Text = NoExtraField;
            this.CMB_Source2.SelectedIndexChanged += new System.EventHandler(this.CMB_Source2_SelectedIndexChanged);
            //
            // CMB_Op2
            //
            this.CMB_Op2.FormattingEnabled = true;
            this.CMB_Op2.Location = new System.Drawing.Point(385, 3);
            this.CMB_Op2.Name = "CMB_Op2";
            this.CMB_Op2.Size = new System.Drawing.Size(42, 21);
            this.CMB_Op2.TabIndex = 12;
            this.CMB_Op2.SelectedIndexChanged += new System.EventHandler(this.CMB_Op2_SelectedIndexChanged);
            //
            // CMB_Source3 (Field 3)
            //
            this.CMB_Source3.FormattingEnabled = true;
            this.CMB_Source3.Location = new System.Drawing.Point(431, 3);
            this.CMB_Source3.Name = "CMB_Source3";
            this.CMB_Source3.Size = new System.Drawing.Size(100, 21);
            this.CMB_Source3.TabIndex = 13;
            this.CMB_Source3.Text = NoExtraField;
            this.CMB_Source3.SelectedIndexChanged += new System.EventHandler(this.CMB_Source3_SelectedIndexChanged);
            //
            // CMB_condition
            //
            this.CMB_condition.FormattingEnabled = true;
            this.CMB_condition.Location = new System.Drawing.Point(535, 3);
            this.CMB_condition.Name = "CMB_condition";
            this.CMB_condition.Size = new System.Drawing.Size(54, 21);
            this.CMB_condition.TabIndex = 1;
            this.CMB_condition.Text = "EQ";
            this.CMB_condition.SelectedIndexChanged += new System.EventHandler(this.CMB_condition_SelectedIndexChanged);
            //
            // NUM_warning
            //
            this.NUM_warning.DecimalPlaces = 2;
            this.NUM_warning.Location = new System.Drawing.Point(593, 4);
            this.NUM_warning.Maximum = new decimal(new int[] {
            99999,
            0,
            0,
            0});
            this.NUM_warning.Minimum = new decimal(new int[] {
            99999,
            0,
            0,
            -2147483648});
            this.NUM_warning.Name = "NUM_warning";
            this.NUM_warning.Size = new System.Drawing.Size(65, 20);
            this.NUM_warning.TabIndex = 2;
            this.NUM_warning.ValueChanged += new System.EventHandler(this.NUM_warning_ValueChanged);
            //
            // NUM_repeattime
            //
            this.NUM_repeattime.Location = new System.Drawing.Point(960, 4);
            this.NUM_repeattime.Name = "NUM_repeattime";
            this.NUM_repeattime.Size = new System.Drawing.Size(39, 20);
            this.NUM_repeattime.TabIndex = 3;
            this.NUM_repeattime.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_repeattime.ValueChanged += new System.EventHandler(this.NUM_repeattime_ValueChanged);
            //
            // TXT_warningtext
            //
            this.TXT_warningtext.Location = new System.Drawing.Point(756, 3);
            this.TXT_warningtext.Name = "TXT_warningtext";
            this.TXT_warningtext.Size = new System.Drawing.Size(200, 20);
            this.TXT_warningtext.TabIndex = 4;
            this.TXT_warningtext.Text = "WARNING: {name} is {value}";
            this.TXT_warningtext.TextChanged += new System.EventHandler(this.TXT_warningtext_TextChanged);
            //
            // but_addchild
            //
            this.but_addchild.Location = new System.Drawing.Point(1003, 4);
            this.but_addchild.Name = "but_addchild";
            this.but_addchild.Size = new System.Drawing.Size(25, 20);
            this.but_addchild.TabIndex = 5;
            this.but_addchild.Text = "+";
            this.but_addchild.UseVisualStyleBackColor = true;
            this.but_addchild.Click += new System.EventHandler(this.but_addchild_Click);
            //
            // but_remove
            //
            this.but_remove.Location = new System.Drawing.Point(1032, 4);
            this.but_remove.Name = "but_remove";
            this.but_remove.Size = new System.Drawing.Size(25, 20);
            this.but_remove.TabIndex = 6;
            this.but_remove.Text = "-";
            this.but_remove.UseVisualStyleBackColor = true;
            this.but_remove.Click += new System.EventHandler(this.but_remove_Click);
            //
            // cbType
            //
            this.CB_type.AutoSize = true;
            this.CB_type.Location = new System.Drawing.Point(3, 5);
            this.CB_type.Name = "cbType";
            this.CB_type.Size = new System.Drawing.Size(122, 17);
            this.CB_type.TabIndex = 7;
            this.CB_type.Text = "QuickPanel Coloring";
            this.CB_type.UseVisualStyleBackColor = true;
            this.CB_type.CheckedChanged += new System.EventHandler(this.CB_type_CheckedChanged);
            //
            // cmbColor
            //
            this.CMB_color.FormattingEnabled = true;
            this.CMB_color.Location = new System.Drawing.Point(662, 3);
            this.CMB_color.Name = "cmbColor";
            this.CMB_color.Size = new System.Drawing.Size(90, 21);
            this.CMB_color.TabIndex = 8;
            this.CMB_color.Text = "NoColor";
            this.CMB_color.SelectedIndexChanged += new System.EventHandler(this.cmbColor_SelectedIndexChanged);
            //
            // WarningControl
            //
            this.Controls.Add(this.CMB_color);
            this.Controls.Add(this.CB_type);
            this.Controls.Add(this.but_remove);
            this.Controls.Add(this.but_addchild);
            this.Controls.Add(this.TXT_warningtext);
            this.Controls.Add(this.NUM_repeattime);
            this.Controls.Add(this.NUM_warning);
            this.Controls.Add(this.CMB_condition);
            this.Controls.Add(this.CMB_Source3);
            this.Controls.Add(this.CMB_Op2);
            this.Controls.Add(this.CMB_Source2);
            this.Controls.Add(this.CMB_Op1);
            this.Controls.Add(this.CMB_Source);
            this.Name = "WarningControl";
            this.Size = new System.Drawing.Size(1062, 27);
            ((System.ComponentModel.ISupportInitialize)(this.NUM_warning)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_repeattime)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void CMB_Source_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (custwarning != null)
                custwarning.SetField(CMB_Source.Text);
        }

        private void CMB_Source2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (custwarning == null)
                return;

            var sel = CMB_Source2.Text;
            if (string.IsNullOrEmpty(sel) || sel == NoExtraField)
            {
                custwarning.SetField2(null);
            }
            else
            {
                custwarning.SetField2(sel);
            }

            NUM_warning.Enabled = custwarning.HasExpression || !custwarning.HasSecondField;
        }

        private void CMB_Source3_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (custwarning == null)
                return;

            var sel = CMB_Source3.Text;
            if (string.IsNullOrEmpty(sel) || sel == NoExtraField)
            {
                custwarning.SetField3(null);
            }
            else
            {
                custwarning.SetField3(sel);
            }

            NUM_warning.Enabled = custwarning.HasExpression || !custwarning.HasSecondField;
        }

        private void CMB_Op1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (custwarning == null)
                return;

            custwarning.Operator1 = OpFromDisplay(CMB_Op1.Text);
            NUM_warning.Enabled = custwarning.HasExpression || !custwarning.HasSecondField;
        }

        private void CMB_Op2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (custwarning == null)
                return;

            custwarning.Operator2 = OpFromDisplay(CMB_Op2.Text);
            NUM_warning.Enabled = custwarning.HasExpression || !custwarning.HasSecondField;
        }

        private void CMB_condition_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (custwarning != null)
                custwarning.ConditionType =
                    (CustomWarning.Conditional)Enum.Parse(typeof(CustomWarning.Conditional), CMB_condition.Text);
        }

        private void cmbColor_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (custwarning != null)
                custwarning.color = CMB_color.Text;

        }

        private void NUM_warning_ValueChanged(object sender, EventArgs e)
        {
            if (custwarning != null)
                custwarning.Warning = (double)NUM_warning.Value;
        }

        private void TXT_warningtext_TextChanged(object sender, EventArgs e)
        {
            if (custwarning != null)
                custwarning.Text = TXT_warningtext.Text;
        }

        private void NUM_repeattime_ValueChanged(object sender, EventArgs e)
        {
            if (custwarning != null)
                custwarning.RepeatTime = (int)NUM_repeattime.Value;
        }

        private void but_addchild_Click(object sender, EventArgs e)
        {
            custwarning.Child = new CustomWarning();

            if (ReloadList != null)
                ReloadList(this, null);
        }

        private void but_remove_Click(object sender, EventArgs e)
        {
            lock (WarningEngine.warnings)
            {
                WarningEngine.warnings.Remove(custwarning);

                foreach (var item in WarningEngine.warnings)
                {
                    removewarning(item, custwarning);
                }
            }

            if (ReloadList != null)
                ReloadList(this, null);
        }

        void removewarning(CustomWarning lookin, CustomWarning removeme)
        {
            // depth first check children
            if (lookin.Child != null)
                removewarning(lookin.Child, removeme);

            if (lookin.Child == removeme)
            {
                if (lookin.Child.Child != null)
                {
                    lookin.Child = lookin.Child.Child;
                }
                else
                {
                    lookin.Child = null;
                }
                return;
            }
        }

        private void CB_type_CheckedChanged(object sender, EventArgs e)
        {
            if (CB_type.Checked)
            {
                //It is a coloring item
                but_addchild.Enabled = false;
                TXT_warningtext.Text = "";
                TXT_warningtext.Enabled = false;
                NUM_repeattime.Value = 0;
                NUM_repeattime.Enabled = false;

                CMB_color.Enabled = true;

                _custwarn.type = CustomWarning.WarningType.Coloring;

            }
            else
            {
                //It is regular SpeakAndText item
                but_addchild.Enabled = true;
                TXT_warningtext.Text = "WARNING: {name} is {value}";
                TXT_warningtext.Enabled = true;
                NUM_repeattime.Value = 10;
                NUM_repeattime.Enabled = true;

                CMB_color.Enabled = false;

                _custwarn.type = CustomWarning.WarningType.SpeakAndText;

            }
        }
    }
}