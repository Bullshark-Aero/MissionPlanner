using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner;
using MissionPlanner.BSA.Telemetry;
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
        private float lightsRowHeight = 36F;
        private bool buttonShown;
        private readonly Judicar2600Identity identity = new Judicar2600Identity();
        private MAVLinkInterface subscribedPort;

        public override string Name { get { return "Judicar 2600 Aircraft Lights"; } }
        public override string Version { get { return "1.0.4"; } }
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
            lightsRowHeight = actionRowHeight;
            actionsTable.RowStyles[lightsRow].SizeType = SizeType.Absolute;
            actionsTable.RowStyles[lightsRow].Height = 0;
            lightsButton.Visible = false;
            buttonShown = false;
            actionsTable.Controls.Add(lightsButton, 0, lightsRow);
            actionsTable.SetColumnSpan(lightsButton, Math.Max(1, actionsTable.ColumnCount));
            SubscribeToCurrentPort();
            UpdateButtonFromTelemetry();
            return true;
        }

        public override bool Loop()
        {
            if (lightsButton == null || lightsButton.IsDisposed)
            {
                return true;
            }

            SubscribeToCurrentPort();

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
            if (subscribedPort != null)
            {
                subscribedPort.OnPacketReceived -= OnPacketReceived;
                subscribedPort = null;
            }

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

        private void SubscribeToCurrentPort()
        {
            MAVLinkInterface port = MainV2.comPort;
            if (ReferenceEquals(port, subscribedPort))
            {
                return;
            }

            if (subscribedPort != null)
            {
                subscribedPort.OnPacketReceived -= OnPacketReceived;
            }

            subscribedPort = port;
            if (port != null)
            {
                port.OnPacketReceived += OnPacketReceived;
            }
        }

        private void OnPacketReceived(object sender, MAVLink.MAVLinkMessage message)
        {
            if (message.msgid != (uint)MAVLink.MAVLINK_MSG_ID.NAMED_VALUE_FLOAT)
            {
                return;
            }

            MAVLink.mavlink_named_value_float_t packet = message.ToStructure<MAVLink.mavlink_named_value_float_t>();
            string name = System.Text.Encoding.ASCII.GetString(packet.name).TrimEnd('\0');
            identity.Record(sender, message.sysid, name);
        }

        private bool IsAircraftIdentified()
        {
            MAVLinkInterface port = MainV2.comPort;
            bool open = port != null && port.BaseStream != null && port.BaseStream.IsOpen;
            return identity.IsIdentified(port, open ? port.sysidcurrent : 0, open);
        }

        private void ToggleLights(object sender, EventArgs e)
        {
            if (!IsAircraftIdentified())
            {
                return;
            }

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

                ShowState("AIRCRAFT LIGHTS: " + targetName + " CMD", targetPwm == OnPwm ? Color.DarkGreen : Color.DimGray, Color.White);
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

        private void ShowState(string text, Color background, Color textColour)
        {
            lightsButton.Text = text;
            lightsButton.BGGradTop = background;
            lightsButton.BGGradBot = background;
            lightsButton.Outline = background;
            lightsButton.TextColor = textColour;
            lightsButton.TextColorNotEnabled = textColour;
        }

        private void UpdateButtonFromTelemetry()
        {
            if (lightsButton == null || lightsButton.IsDisposed)
            {
                return;
            }

            bool identified = IsAircraftIdentified();
            if (identified != buttonShown)
            {
                buttonShown = identified;
                lightsButton.Visible = identified;
                if (actionsTable != null && lightsRow >= 0 && lightsRow < actionsTable.RowStyles.Count)
                {
                    actionsTable.RowStyles[lightsRow].Height = identified ? lightsRowHeight : 0;
                }
            }

            if (!identified || !lightsButton.Enabled)
            {
                return;
            }

            float servo15 = MainV2.comPort.MAV.cs.ch15out;
            float servo16 = MainV2.comPort.MAV.cs.ch16out;
            bool light1On = IsOn(servo15);
            bool light2On = IsOn(servo16);

            if (light1On && light2On)
            {
                ShowState("AIRCRAFT LIGHTS: ON", Color.DarkGreen, Color.White);
            }
            else if (!light1On && !light2On && servo15 > 0 && servo16 > 0)
            {
                ShowState("AIRCRAFT LIGHTS: OFF", Color.DimGray, Color.White);
            }
            else
            {
                ShowState("AIRCRAFT LIGHTS: CHECK / RESTORE ON", Color.DarkOrange, Color.Black);
            }
        }
    }
}
