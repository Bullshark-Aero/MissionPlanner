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
        private const byte AutopilotComponentId =
            (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1;

        private MyButton lightsButton;
        private ToolTip lightsToolTip;
        private TableLayoutPanel actionsTable;
        private int lightsRow = -1;
        private LightsCommandState commandState = LightsCommandState.Unknown;
        private bool commandInProgress;
        private bool lastLinkConnected;
        private byte lastSystemId;

        public override string Name { get { return "Judicar 2600 Aircraft Lights"; } }
        public override string Version { get { return "1.1.0"; } }
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
                "Commands both Judicar 2600 aircraft lights together (SERVO15 and SERVO16 only)."
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
            RefreshButton();
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
                lightsButton.BeginInvoke((Action)RefreshButton);
            }
            else
            {
                RefreshButton();
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
            byte systemId;
            if (!TryGetAutopilotTarget(out systemId))
            {
                commandState = LightsCommandState.Unknown;
                RefreshButton();
                MessageBox.Show(
                    "No live vehicle link is available. No light command was sent.",
                    "Judicar 2600 Aircraft Lights",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            commandInProgress = true;
            lightsButton.Enabled = false;
            try
            {
                int targetPwm = Judicar2600LightsState.NextTargetPwm(
                    commandState,
                    OnPwm,
                    OffPwm
                );
                string targetName = targetPwm == OnPwm ? "ON" : "OFF";
                string stateExplanation = commandState == LightsCommandState.Unknown
                    ? "The current light state is unknown, so this first command will establish the aircraft default ON state.\n\n"
                    : "";

                string prompt =
                    "Command BOTH Judicar 2600 aircraft lights " + targetName + "?\n\n" +
                    stateExplanation +
                    "Only SERVO15 and SERVO16 will be commanded.";

                DialogResult confirmation = MessageBox.Show(
                    prompt,
                    "Judicar 2600 Aircraft Lights",
                    MessageBoxButtons.YesNo,
                    targetPwm == OffPwm ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2
                );

                if (confirmation != DialogResult.Yes)
                {
                    return;
                }

                ServoCommandResult light1 = SetServo(systemId, Light1Servo, targetPwm);
                ServoCommandResult light2 = SetServo(systemId, Light2Servo, targetPwm);
                bool light1Accepted = light1 == ServoCommandResult.Accepted;
                bool light2Accepted = light2 == ServoCommandResult.Accepted;

                if (!light1Accepted || !light2Accepted)
                {
                    // The aircraft's defined default is lights ON. If a paired
                    // command is only partly accepted, make a best-effort return
                    // to that conservative state rather than leaving a split pair.
                    ServoCommandResult recovery1 = SetServo(systemId, Light1Servo, OnPwm);
                    ServoCommandResult recovery2 = SetServo(systemId, Light2Servo, OnPwm);
                    bool recovery1Accepted = recovery1 == ServoCommandResult.Accepted;
                    bool recovery2Accepted = recovery2 == ServoCommandResult.Accepted;
                    commandState = Judicar2600LightsState.ResolveAfterAttempt(
                        targetPwm,
                        OnPwm,
                        light1Accepted,
                        light2Accepted,
                        recovery1Accepted,
                        recovery2Accepted
                    );

                    MessageBox.Show(
                        "The paired light command was not fully accepted.\n\n" +
                        "SERVO15: " + Describe(light1) + "\n" +
                        "SERVO16: " + Describe(light2) + "\n\n" +
                        "Recovery toward the default ON state was attempted.\n" +
                        "SERVO15 recovery: " + Describe(recovery1) + "\n" +
                        "SERVO16 recovery: " + Describe(recovery2) + "\n\n" +
                        CommandStateExplanation(),
                        "Judicar 2600 Aircraft Lights",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                commandState = Judicar2600LightsState.ResolveAfterAttempt(
                    targetPwm,
                    OnPwm,
                    true,
                    true,
                    false,
                    false
                );
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
                commandInProgress = false;
                RefreshButton();
            }
        }

        private static ServoCommandResult SetServo(byte systemId, int servoNumber, int pwm)
        {
            if (!LinkIsOpen())
            {
                return ServoCommandResult.LinkUnavailable;
            }

            try
            {
                bool accepted = MainV2.comPort.doCommand(
                    systemId,
                    AutopilotComponentId,
                    MAVLink.MAV_CMD.DO_SET_SERVO,
                    servoNumber,
                    pwm,
                    0,
                    0,
                    0,
                    0,
                    0
                );
                return accepted ? ServoCommandResult.Accepted : ServoCommandResult.Rejected;
            }
            catch (TimeoutException)
            {
                return ServoCommandResult.TimedOut;
            }
            catch
            {
                return ServoCommandResult.Failed;
            }
        }

        private static bool LinkIsOpen()
        {
            return MainV2.comPort != null &&
                   MainV2.comPort.BaseStream != null &&
                   MainV2.comPort.BaseStream.IsOpen;
        }

        private static bool TryGetAutopilotTarget(out byte systemId)
        {
            systemId = 0;
            if (!LinkIsOpen() || MainV2.comPort.sysidcurrent <= 0 ||
                MainV2.comPort.sysidcurrent > byte.MaxValue)
            {
                return false;
            }

            systemId = (byte)MainV2.comPort.sysidcurrent;
            return true;
        }

        private void RefreshButton()
        {
            if (lightsButton == null || lightsButton.IsDisposed)
            {
                return;
            }

            byte systemId;
            bool connected = TryGetAutopilotTarget(out systemId);
            if (Judicar2600LightsState.ConnectionInvalidatesState(
                    lastLinkConnected,
                    lastSystemId,
                    connected,
                    systemId))
            {
                commandState = LightsCommandState.Unknown;
            }

            lastLinkConnected = connected;
            if (connected)
            {
                lastSystemId = systemId;
            }

            if (commandInProgress)
            {
                return;
            }

            lightsButton.Enabled = connected;

            if (!connected)
            {
                lightsButton.Text = "AIRCRAFT LIGHTS: NO LINK";
                lightsButton.BackColor = Color.DarkRed;
                lightsButton.ForeColor = Color.White;
                lightsToolTip.SetToolTip(lightsButton, "No live vehicle link. No command can be sent.");
            }
            else if (commandState == LightsCommandState.CommandedOn)
            {
                lightsButton.Text = "AIRCRAFT LIGHTS: ON CMD";
                lightsButton.BackColor = Color.DarkGreen;
                lightsButton.ForeColor = Color.White;
                lightsToolTip.SetToolTip(lightsButton, CommandStateExplanation());
            }
            else if (commandState == LightsCommandState.CommandedOff)
            {
                lightsButton.Text = "AIRCRAFT LIGHTS: OFF CMD";
                lightsButton.BackColor = Color.DimGray;
                lightsButton.ForeColor = Color.White;
                lightsToolTip.SetToolTip(lightsButton, CommandStateExplanation());
            }
            else
            {
                lightsButton.Text = "AIRCRAFT LIGHTS: SET ON";
                lightsButton.BackColor = Color.DarkOrange;
                lightsButton.ForeColor = Color.Black;
                lightsToolTip.SetToolTip(
                    lightsButton,
                    "State unknown. Click to command both lights to the default ON state."
                );
            }
        }

        private string CommandStateExplanation()
        {
            if (commandState == LightsCommandState.CommandedOn)
            {
                return "Both flight-controller commands were accepted for ON. This is command state, not physical lamp feedback.";
            }

            if (commandState == LightsCommandState.CommandedOff)
            {
                return "Both flight-controller commands were accepted for OFF. This is command state, not physical lamp feedback.";
            }

            return "The paired light state is unknown. Physical lamp feedback is not available.";
        }

        private static string Describe(ServoCommandResult result)
        {
            switch (result)
            {
                case ServoCommandResult.Accepted:
                    return "accepted";
                case ServoCommandResult.Rejected:
                    return "rejected by the target";
                case ServoCommandResult.TimedOut:
                    return "timed out waiting for an ACK";
                case ServoCommandResult.LinkUnavailable:
                    return "not sent because the vehicle link was unavailable";
                default:
                    return "failed before an ACK was received";
            }
        }

        private enum ServoCommandResult
        {
            Accepted,
            Rejected,
            TimedOut,
            LinkUnavailable,
            Failed
        }
    }
}
