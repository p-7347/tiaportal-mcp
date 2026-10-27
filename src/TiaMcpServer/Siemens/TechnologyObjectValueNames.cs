using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Meaning of the coded values of axis technology object parameters ("Homing.ApproachDirection"
    /// true = negative direction, "Sensor[1].ActiveHoming.Mode" 2 = digital input, ...). Openness
    /// returns these only as numbers and booleans. Taken from the tag tables of the positioning axis
    /// technology object (technology version V9.0, STEP 7 V20) in the Siemens function manual
    /// "S7-1500/S7-1500T Axis functions"; checked against the TIA Portal dialog for the homing
    /// parameters. Older technology versions may differ.
    /// </summary>
    internal static class TechnologyObjectValueNames
    {
        private static readonly Regex Index = new Regex(@"\[\d+\]");

        /// <summary>The meaning of 'value' for parameter 'name' of an axis, or null when unknown.</summary>
        public static string? Describe(string? objectType, string name, string? value)
        {
            if (value == null || objectType == null || objectType.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            // "Sensor[2].Interface[5].EnableLeading" -> "Sensor[1..4].Interface[1..32].EnableLeading"
            // Some parameters carry a leading '_' in Openness, e.g. "_Units.LengthUnit".
            var counter = 0;
            var key = Index.Replace(name.TrimStart('_'), _ => counter++ == 0 && name.TrimStart('_').StartsWith("Sensor[", StringComparison.Ordinal) ? "[1..4]" : "[1..32]");

            if (!Names.TryGetValue(key, out var values))
            {
                return null;
            }

            var code = value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "TRUE"
                : value.Equals("false", StringComparison.OrdinalIgnoreCase) ? "FALSE"
                : value;

            return values.TryGetValue(code, out var text) ? text : null;
        }

        private static readonly Dictionary<string, Dictionary<string, string>> Names = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
        {
            ["Simulation.Mode"] = new Dictionary<string, string> { ["0"] = "No simulation, normal operation", ["1"] = "Simulation mode" },
            ["VirtualAxis.Mode"] = new Dictionary<string, string> { ["0"] = "No virtual axis", ["1"] = "Virtual axis" },
            ["Actor.Type"] = new Dictionary<string, string> { ["0"] = "Analog output", ["1"] = "PROFIdrive telegram" },
            ["Actor.InverseDirection"] = new Dictionary<string, string> { ["FALSE"] = "No", ["TRUE"] = "Yes" },
            ["Actor.DataAdaption"] = new Dictionary<string, string> { ["0"] = "No automatic transfer, manual configuration of values", ["1"] = "Automatic transfer of values configured in the drive to the configuration of the technology" },
            ["Actor.MotorType"] = new Dictionary<string, string> { ["0"] = "Round-frame motor (standard motor)", ["1"] = "Linear motor" },
            ["Actor.Interface.EnableDriveOutput"] = new Dictionary<string, string> { ["FALSE"] = "Disabled", ["TRUE"] = "Enabled" },
            ["Actor.Interface.DriveReadyInput"] = new Dictionary<string, string> { ["FALSE"] = "Disabled", ["TRUE"] = "Enabled" },
            ["Actor.Interface.EnableTorqueData"] = new Dictionary<string, string> { ["FALSE"] = "Disabled", ["TRUE"] = "Enabled" },
            ["TorqueLimiting.LimitBase"] = new Dictionary<string, string> { ["0"] = "Motor side", ["1"] = "Load side" },
            ["TorqueLimiting.PositionBasedMonitor"] = new Dictionary<string, string> { ["0"] = "Monitoring deactivated", ["1"] = "Monitoring activated" },
            ["Sensor[1..4].Type"] = new Dictionary<string, string> { ["0"] = "Incremental (INCREMENTAL)", ["1"] = "Absolute (ABSOLUTE)", ["2"] = "Cyclic absolute (CYCLIC_ABSOLUTE)" },
            ["Sensor[1..4].InverseDirection"] = new Dictionary<string, string> { ["FALSE"] = "No", ["TRUE"] = "Yes" },
            ["Sensor[1..4].System"] = new Dictionary<string, string> { ["0"] = "Linear encoder (LINEAR)", ["1"] = "Rotary encoder (ROTATORY)" },
            ["Sensor[1..4].MountingMode"] = new Dictionary<string, string> { ["0"] = "On motor shaft", ["1"] = "On load side", ["2"] = "External measuring system" },
            ["Sensor[1..4].DataAdaption"] = new Dictionary<string, string> { ["0"] = "No automatic transfer, manual configuration of values", ["1"] = "Automatic transfer of values configured in the drive to the configuration of the technology" },
            ["Sensor[1..4].ActualVelocityMode"] = new Dictionary<string, string> { ["0"] = "Actual value calculation from differentiation of the position change", ["1"] = "Actual value calculation with NACT value from the PROFIdrive telegram" },
            ["Sensor[1..4].Parameter.BehaviorGx_XIST1"] = new Dictionary<string, string> { ["0"] = "Based on the bits of the encoder resolution", ["1"] = "32-bit value of the encoder value" },
            ["Sensor[1..4].Backlash.Enable"] = new Dictionary<string, string> { ["FALSE"] = "Disabled", ["TRUE"] = "Enabled" },
            ["Sensor[1..4].Backlash.DirectionAbsolute"] = new Dictionary<string, string> { ["0"] = "Positive", ["1"] = "Negative" },
            ["Sensor[1..4].ActiveHoming.Mode"] = new Dictionary<string, string> { ["0"] = "Use zero mark via PROFIdrive telegram", ["1"] = "Zero mark via PROFIdrive telegram and reference output cam", ["2"] = "Use homing mark via digital input" },
            ["Sensor[1..4].ActiveHoming.SideInput"] = new Dictionary<string, string> { ["FALSE"] = "Negative side", ["TRUE"] = "Positive side" },
            ["Sensor[1..4].ActiveHoming.Direction"] = new Dictionary<string, string> { ["0"] = "Positive homing direction", ["1"] = "Negative homing direction" },
            ["Sensor[1..4].ActiveHoming.SwitchLevel"] = new Dictionary<string, string> { ["FALSE"] = "Low level", ["TRUE"] = "High level" },
            ["Sensor[1..4].PassiveHoming.Mode"] = new Dictionary<string, string> { ["0"] = "Use zero mark via PROFIdrive telegram", ["1"] = "Zero mark via PROFIdrive telegram and reference output cam", ["2"] = "Use homing mark via digital input" },
            ["Sensor[1..4].PassiveHoming.SideInput"] = new Dictionary<string, string> { ["FALSE"] = "Negative side", ["TRUE"] = "Positive side" },
            ["Sensor[1..4].PassiveHoming.Direction"] = new Dictionary<string, string> { ["0"] = "Positive homing direction", ["1"] = "Negative homing direction", ["2"] = "Current homing direction" },
            ["Sensor[1..4].PassiveHoming.SwitchLevel"] = new Dictionary<string, string> { ["FALSE"] = "Low level", ["TRUE"] = "High level" },
            ["Sensor[1..4].Interface[1..32].EnableLeading"] = new Dictionary<string, string> { ["FALSE"] = "No", ["TRUE"] = "Yes" },
            ["Extrapolation.Settings.SystemDefinedExtra"] = new Dictionary<string, string> { ["0"] = "Filtered actual velocity (FilteredVelocity)", ["1"] = "Differentiated filtered actual position (VelocityByDifferentiation)" },
            ["Properties.MotionType"] = new Dictionary<string, string> { ["0"] = "Linear axis or motion", ["1"] = "Rotary axis or motion" },
            ["Units.LengthUnit"] = new Dictionary<string, string> { ["1010"] = "m", ["1013"] = "mm", ["1536"] = "mm", ["1011"] = "km", ["1014"] = "µm", ["1015"] = "nm", ["1019"] = "in", ["1018"] = "ft", ["1021"] = "mi", ["1004"] = "rad", ["1005"] = "°", ["1537"] = "°" },
            ["Units.VelocityUnit"] = new Dictionary<string, string> { ["1521"] = "°/s", ["1539"] = "°/s", ["1522"] = "°/min", ["1086"] = "rad/s", ["1523"] = "rad/min", ["1062"] = "mm/s", ["1538"] = "mm/s", ["1061"] = "m/s", ["1524"] = "mm/min", ["1525"] = "m/min", ["1526"] = "mm/h", ["1063"] = "m/h", ["1527"] = "km/min", ["1064"] = "km/h", ["1066"] = "in/s", ["1069"] = "in/min", ["1067"] = "ft/s", ["1070"] = "ft/min", ["1075"] = "mi/h" },
            ["Units.TorqueUnit"] = new Dictionary<string, string> { ["1126"] = "Nm", ["1128"] = "kNm", ["1529"] = "lbf in (pound-force-inch)", ["1530"] = "lbf ft", ["1531"] = "ozf in (ounce-force-inch)", ["1532"] = "ozf ft", ["1533"] = "pdl in (poundal-inch)", ["1534"] = "pdl ft" },
            ["Units.ForceUnit"] = new Dictionary<string, string> { ["1120"] = "N", ["1122"] = "kN", ["1094"] = "lbf (pound-force)", ["1093"] = "ozf (ounce-force)", ["1535"] = "pdl (poundals)" },
            ["Units.MassUnit"] = new Dictionary<string, string> { ["1088"] = "kg", ["1089"] = "g", ["1090"] = "mg", ["1092"] = "t", ["1540"] = "lb" },
            ["Units.IneritaUnit"] = new Dictionary<string, string> { ["1118"] = "kg·m²", ["1541"] = "lb·ft²" },
            ["Modulo.Enable"] = new Dictionary<string, string> { ["FALSE"] = "Modulo conversion disabled", ["TRUE"] = "Modulo conversion enabled" },
            ["PositionLimits_SW.Active"] = new Dictionary<string, string> { ["FALSE"] = "Monitoring deactivated", ["TRUE"] = "Monitoring enabled" },
            ["PositionLimits_SW.LimitReachedBehavior"] = new Dictionary<string, string> { ["0"] = "Stop with maximum dynamic values", ["1"] = "Stop with current dynamic values" },
            ["PositionLimits_SW.LimitExceededBehavior"] = new Dictionary<string, string> { ["0"] = "Disable axis", ["1"] = "Keep emergency stop and axis enable" },
            ["PositionLimits_HW.Active"] = new Dictionary<string, string> { ["FALSE"] = "Monitoring deactivated", ["TRUE"] = "Monitoring enabled" },
            ["PositionLimits_HW.MinSwitchLevel"] = new Dictionary<string, string> { ["FALSE"] = "Low level (Low active)", ["TRUE"] = "High level (high-enabled)" },
            ["PositionLimits_HW.MaxSwitchLevel"] = new Dictionary<string, string> { ["FALSE"] = "Low level (Low active)", ["TRUE"] = "High level (high-enabled)" },
            ["PositionLimits_HW.Mode"] = new Dictionary<string, string> { ["0"] = "Switch non-traversable", ["1"] = "Switch traversable" },
            ["PositionLimits_HW.AppoachBehavior"] = new Dictionary<string, string> { ["0"] = "Disable axis", ["1"] = "Keep emergency stop and axis enabled" },
            ["Homing.AutoReversal"] = new Dictionary<string, string> { ["FALSE"] = "No", ["TRUE"] = "Yes" },
            ["Homing.ApproachDirection"] = new Dictionary<string, string> { ["FALSE"] = "Positive direction", ["TRUE"] = "Negative direction" },
            ["PositionControl.EnableDSC"] = new Dictionary<string, string> { ["FALSE"] = "DSC disabled", ["TRUE"] = "DSC activated" },
            ["PositionControl.Mode"] = new Dictionary<string, string> { ["0"] = "No quantification", ["1"] = "Quantization corresponding to encoder resolution", ["2"] = "Quantization to a direct value" },
            ["PositionControl.VelocityModePowerOn"] = new Dictionary<string, string> { ["0"] = "Velocity is set to \"0\" with maximum dynamic values of the axis (ramp)", ["1"] = "Velocity is immediately set to \"0\" without ramp" },
            ["TorquePreControl.Mode"] = new Dictionary<string, string> { ["0"] = "Torque precontrol not in effect", ["1"] = "Torque precontrol based on the acceleration of the axis" },
            ["SetpointFilter.DynamicFilter.Mode"] = new Dictionary<string, string> { ["0"] = "Dynamic filter not active", ["1"] = "PT1/PT2 filter + dead time", ["2"] = "Sliding window demand + dead time" },
            ["FollowingError.EnableMonitoring"] = new Dictionary<string, string> { ["FALSE"] = "Following error monitoring deactivated", ["TRUE"] = "Following error monitoring enabled" },
        };
    }
}
