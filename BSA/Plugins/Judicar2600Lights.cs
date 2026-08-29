using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner;
using MissionPlanner.Controls;
using MissionPlanner.Plugin;

namespace BSA.Judicar2600.MissionPlannerPlugins
{
    public sealed class Judicar2600Lights : Plugin
    {
        private const int Light1Servo = 15;
        private const int Light2Servo = 16;
        private const int OffPwm = 1000;
        private const int OnPwm = 1900;
        private const int OnThresholdPwm = 1800;

        private MyButton lightsButton;
        private ToolTip lightsToolTip;
        private TableLayoutPanel actionsTable;
        private int lightsRow = -1;

        public override string Name { get { return "Judicar 2600 Aircraft Lights"; } }
        public override string Version { get { return "1.0.2"; } }
        public override string Author { get { return "BSA"; } }

        public override bool Init()
        {
            loopratehz = 2.0f;
            return true;
        }

        public override bool Loaded()
        {
            lightsButton = new MyButton();
            lightsButton.Name = "Judicar2600LightsToggle";
            lightsButton.Text = "AIRCRAFT LIGHTS: CHECKING";
            lightsButton.Dock = DockStyle.Fill;
            lightsButton.Margin = new Padding(3);
            lightsButton.Click += ToggleLights;

            lightsToolTip = new ToolTip();
            lightsToolTip.SetToolTip(
                lightsButton,
                "Confirmed toggle of both Judicar 2600 aircraft lights (SERVO15 and SERVO16 only)."
            );

            actionsTable = FindTableLayout(MainV2.instance.FlightData.tabActions);
            if (actionsTable == null)
            {
                throw new InvalidOperationException(
                    "Mission Planner's Actions tab table could not be found."
                );
            }

            // Append a dedicated row rather than taking one of Mission Planner's
            // stock action cells or a cell already used by another plugin.
            float actionRowHeight = ExistingActionRowHeight(actionsTable);
            lightsRow = NextUnusedRow(actionsTable);
            actionsTable.RowCount = Math.Max(actionsTable.RowCount, lightsRow + 1);
            while (actionsTable.RowStyles.Count <= lightsRow)
            {
                actionsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, actionRowHeight));
            }
            actionsTable.RowStyles[lightsRow].SizeType = SizeType.Absolute;
            actionsTable.RowStyles[lightsRow].Height = actionRowHeight;
            actionsTable.Controls.Add(lightsButton, 0, lightsRow);
            actionsTable.SetColumnSpan(lightsButton, 1);
            UpdateButtonFromTelemetry();
            return true;
        }

        public override bool Loop()
        {
            if (lightsButton == null || lightsButton.IsDisposed)
            {
                return true;
            }

            if (lightsButton.InvokeRequired)
            {
                lightsButton.BeginInvoke((Action)UpdateButtonFromTelemetry);
            }
            else
            {
                UpdateButtonFromTelemetry();
            }

            return true;
        }

        public override bool Exit()
        {
            if (lightsButton != null)
            {
                lightsButton.Click -= ToggleLights;
                if (lightsButton.Parent != null)
                {
                    lightsButton.Parent.Controls.Remove(lightsButton);
                }
                lightsButton.Dispose();
                lightsButton = null;
            }

            actionsTable = null;
            lightsRow = -1;

            if (lightsToolTip != null)
            {
                lightsToolTip.Dispose();
                lightsToolTip = null;
            }

            return true;
        }

        private static TableLayoutPanel FindTableLayout(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                TableLayoutPanel table = control as TableLayoutPanel;
                if (table != null)
                {
                    return table;
                }

                TableLayoutPanel nested = FindTableLayout(control);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static int NextUnusedRow(TableLayoutPanel table)
        {
            int highestOccupiedRow = -1;
            foreach (Control control in table.Controls)
            {
                highestOccupiedRow = Math.Max(highestOccupiedRow, table.GetRow(control));
            }

            return Math.Max(table.RowCount, highestOccupiedRow + 1);
        }

        private static float ExistingActionRowHeight(TableLayoutPanel table)
        {
            int[] rowHeights = table.GetRowHeights();
            for (int row = rowHeights.Length - 1; row >= 0; row--)
            {
                if (rowHeights[row] > 0)
                {
                    return rowHeights[row];
                }
            }

            return 36F;
        }

        private void ToggleLights(object sender, EventArgs e)
        {
            lightsButton.Enabled = false;
            try
            {
                float servo15 = MainV2.comPort.MAV.cs.ch15out;
                float servo16 = MainV2.comPort.MAV.cs.ch16out;
                bool bothOn = IsOn(servo15) && IsOn(servo16);
                int targetPwm = bothOn ? OffPwm : OnPwm;
                string targetName = bothOn ? "OFF" : "ON";

                string prompt =
                    "Command BOTH Judicar 2600 aircraft lights " + targetName + "?\n\n" +
                    "Current reported outputs:\n" +
                    "  SERVO15: " + servo15.ToString("0") + " us\n" +
                    "  SERVO16: " + servo16.ToString("0") + " us\n\n" +
                    "Only SERVO15 and SERVO16 will be commanded.";

                DialogResult confirmation = MessageBox.Show(
                    prompt,
                    "Judicar 2600 Aircraft Lights",
                    MessageBoxButtons.YesNo,
                    bothOn ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2
                );

                if (confirmation != DialogResult.Yes)
                {
                    return;
                }

                bool light1Accepted = SetServo(Light1Servo, targetPwm);
                bool light2Accepted = SetServo(Light2Servo, targetPwm);

                if (!light1Accepted || !light2Accepted)
                {
                    // The aircraft's defined default is lights ON. If a paired
                    // command is only partly accepted, make a best-effort return
                    // to that conservative state rather than leaving a split pair.
                    bool recovery1Accepted = SetServo(Light1Servo, OnPwm);
                    bool recovery2Accepted = SetServo(Light2Servo, OnPwm);

                    MessageBox.Show(
                        "The paired light command was not fully accepted.\n\n" +
                        "SERVO15 accepted: " + light1Accepted + "\n" +
                        "SERVO16 accepted: " + light2Accepted + "\n\n" +
                        "Recovery toward the default ON state was attempted.\n" +
                        "SERVO15 recovery accepted: " + recovery1Accepted + "\n" +
                        "SERVO16 recovery accepted: " + recovery2Accepted,
                        "Judicar 2600 Aircraft Lights",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                lightsButton.Text = "AIRCRAFT LIGHTS: " + targetName + " CMD";
                lightsButton.BackColor = targetPwm == OnPwm ? Color.DarkGreen : Color.DimGray;
                lightsButton.ForeColor = Color.White;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Aircraft light command failed before completion. No output other than " +
                    "SERVO15 or SERVO16 was targeted.\n\n" + ex.Message,
                    "Judicar 2600 Aircraft Lights",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                lightsButton.Enabled = true;
            }
        }

        private static bool SetServo(int servoNumber, int pwm)
        {
            return MainV2.comPort.doCommand(
                (byte)MainV2.comPort.sysidcurrent,
                (byte)MainV2.comPort.compidcurrent,
                MAVLink.MAV_CMD.DO_SET_SERVO,
                servoNumber,
                pwm,
                0,
                0,
                0,
                0,
                0
            );
        }

        private static bool IsOn(float pwm)
        {
            return pwm >= OnThresholdPwm;
        }

        private void UpdateButtonFromTelemetry()
        {
            if (lightsButton == null || lightsButton.IsDisposed || !lightsButton.Enabled)
            {
                return;
            }

            float servo15 = MainV2.comPort.MAV.cs.ch15out;
            float servo16 = MainV2.comPort.MAV.cs.ch16out;
            bool light1On = IsOn(servo15);
            bool light2On = IsOn(servo16);

            if (light1On && light2On)
            {
                lightsButton.Text = "AIRCRAFT LIGHTS: ON";
                lightsButton.BackColor = Color.DarkGreen;
                lightsButton.ForeColor = Color.White;
            }
            else if (!light1On && !light2On && servo15 > 0 && servo16 > 0)
            {
                lightsButton.Text = "AIRCRAFT LIGHTS: OFF";
                lightsButton.BackColor = Color.DimGray;
                lightsButton.ForeColor = Color.White;
            }
            else
            {
                lightsButton.Text = "AIRCRAFT LIGHTS: CHECK / RESTORE ON";
                lightsButton.BackColor = Color.DarkOrange;
                lightsButton.ForeColor = Color.Black;
            }
        }
    }
}
