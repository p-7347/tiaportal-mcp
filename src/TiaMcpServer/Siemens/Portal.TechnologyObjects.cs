using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Technology objects (Openness manual V19, 5.11.4.10 - 5.11.4.14): axes, encoders, PID,
        // counters, ... live under PlcSoftware.TechnologicalObjectGroup, not under Program blocks,
        // so the block tools never see them. Each TO exposes its configuration as a flat list of
        // named parameters (e.g. "Homing.Mode", "PositionLimits_SW.Active"). Reading needs nothing;
        // SetTechnologyObjectParameter changes one offline parameter (write tool, --allow-write).

        public sealed class TechnologyObjectInfo
        {
            /// <summary>Group path plus name, e.g. "Axes/SM110".</summary>
            public string Path { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? Type { get; set; }
            public string? Version { get; set; }
        }

        public sealed class TechnologyObjectParameter
        {
            public string Name { get; set; } = string.Empty;
            public string? Value { get; set; }

            /// <summary>Meaning of a coded value of an axis, e.g. "Negative direction"; null when unknown.</summary>
            public string? ValueText { get; set; }

            public string? Error { get; set; }
        }

        public sealed class TechnologyObjectParameters
        {
            public TechnologyObjectInfo Object { get; set; } = new TechnologyObjectInfo();
            public int Total { get; set; }
            public List<TechnologyObjectParameter> Parameters { get; } = new List<TechnologyObjectParameter>();
            public string? StoppedReason { get; set; }
        }

        public List<TechnologyObjectInfo> GetTechnologyObjects(string softwarePath, string regexName)
        {
            return Operation.Run(_logger, nameof(GetTechnologyObjects), PortalErrorCode.NotFound,
                () =>
                {
                    var regex = string.IsNullOrEmpty(regexName) ? null : new Regex(regexName, RegexOptions.IgnoreCase);

                    return EnumerateTechnologyObjects(GetPlcSoftwareOrThrow(softwarePath))
                        .Where(t => regex == null || regex.IsMatch(t.Object.Name))
                        .Select(t => Describe(t.Object, t.Path))
                        .ToList();
                },
                ("softwarePath", softwarePath));
        }

        public TechnologyObjectParameters GetTechnologyObjectParameters(string softwarePath, string objectPath, string regexName, TimeSpan budget)
        {
            return Operation.Run(_logger, nameof(GetTechnologyObjectParameters), PortalErrorCode.NotFound,
                () =>
                {
                    var match = RequireTechnologyObject(softwarePath, objectPath);
                    var result = new TechnologyObjectParameters { Object = Describe(match.Object, match.Path) };
                    var regex = string.IsNullOrEmpty(regexName) ? null : new Regex(regexName, RegexOptions.IgnoreCase);
                    var clock = Stopwatch.StartNew();
                    List<TechnologicalParameter> parameters = match.Object.Parameters.ToList();

                    BeginProgress(nameof(GetTechnologyObjectParameters), parameters.Count);
                    try
                    {
                        foreach (var parameter in parameters)
                        {
                            if (clock.Elapsed > budget)
                            {
                                result.StoppedReason = $"stopped after {budget.TotalSeconds:0} s at parameter {result.Total + 1} of {parameters.Count}; narrow 'regexName' for the rest";
                                break;
                            }

                            result.Total++;

                            string name;
                            try
                            {
                                name = parameter.Name;
                            }
                            catch (Exception)
                            {
                                continue;
                            }

                            ReportProgress(result.Total, name);

                            if (regex != null && !regex.IsMatch(name))
                            {
                                continue;
                            }

                            var entry = new TechnologyObjectParameter { Name = name };
                            try
                            {
                                entry.Value = FormatParameterValue(parameter.Value);
                                entry.ValueText = TechnologyObjectValueNames.Describe(result.Object.Type, name, entry.Value);
                            }
                            catch (Exception ex)
                            {
                                entry.Error = ex.Message;
                            }

                            result.Parameters.Add(entry);
                        }
                    }
                    finally
                    {
                        EndProgress();
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("objectPath", objectPath));
        }

        private (TechnologicalInstanceDB Object, string Path) RequireTechnologyObject(string softwarePath, string objectPath)
        {
            var plcSoftware = GetPlcSoftwareOrThrow(softwarePath);
            var wanted = (objectPath ?? string.Empty).Trim('/');

            // Accept the full path or the bare name; TO names are unique per PLC.
            var all = EnumerateTechnologyObjects(plcSoftware).ToList();
            var match = all.FirstOrDefault(t => string.Equals(t.Path, wanted, StringComparison.OrdinalIgnoreCase));
            if (match.Object == null)
            {
                match = all.FirstOrDefault(t => string.Equals(t.Object.Name, wanted, StringComparison.OrdinalIgnoreCase));
            }

            if (match.Object == null)
            {
                var names = string.Join(", ", all.Select(t => t.Path).Take(20));
                throw new PortalException(PortalErrorCode.NotFound,
                    $"No technology object '{objectPath}' in '{softwarePath}'." +
                    (all.Count > 0 ? $" Existing: {names}{(all.Count > 20 ? ", ..." : string.Empty)}" : " This PLC has no technology objects."));
            }

            return match;
        }

        /// <summary>
        /// Sets one parameter. The text is converted to the type of the current value (bool, int,
        /// uint, double, ...), because Openness rejects e.g. an int for a uint parameter. TIA checks
        /// the range itself and refuses read-only parameters; that message is passed through.
        /// Returns the value before and after, read back from TIA.
        /// </summary>
        public (string Path, string? OldValue, string? NewValue) SetTechnologyObjectParameter(string softwarePath, string objectPath, string parameterName, string value)
        {
            return Operation.Run(_logger, nameof(SetTechnologyObjectParameter), PortalErrorCode.InvalidParams,
                () =>
                {
                    var match = RequireTechnologyObject(softwarePath, objectPath);
                    var parameter = match.Object.Parameters.Find(parameterName)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Technology object '{match.Path}' has no parameter '{parameterName}'. 'GetTechnologyObjectParameters' lists the names (case-sensitive).");

                    var current = parameter.Value;

                    // Address parameters (e.g. _Sensor[1].ActiveHoming.DigitalInputAddress,
                    // _PositionLimits_HW.MinSwitchAddress) hold a PlcTag; take the value as a tag name.
                    var converted = current is PlcTag || (current == null && parameterName.EndsWith("Address", StringComparison.Ordinal))
                        ? RequirePlcTagByName(GetPlcSoftwareOrThrow(softwarePath), value)
                        : ConvertParameterValue(value, current, parameterName);

                    try
                    {
                        parameter.Value = converted;
                    }
                    catch (Exception ex)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"TIA Portal refused '{value}' for '{match.Path}.{parameterName}' (current value {FormatParameterValue(current) ?? "empty"}): {Operation.DescribeChain(ex)}", null, ex);
                    }

                    return (match.Path, FormatParameterValue(current), FormatParameterValue(parameter.Value));
                },
                ("softwarePath", softwarePath), ("objectPath", objectPath), ("parameterName", parameterName));
        }

        private static PlcTag RequirePlcTagByName(PlcSoftware plcSoftware, string name)
        {
            var wanted = (name ?? string.Empty).Trim().Trim('"');

            return FindPlcTag(plcSoftware.TagTableGroup, wanted)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"No PLC tag named '{wanted}' in any tag table. This parameter takes a PLC tag; 'GetTags' lists them.");
        }

        private static PlcTag? FindPlcTag(PlcTagTableGroup group, string name)
        {
            foreach (var table in group.TagTables)
            {
                var tag = table.Tags.Find(name);
                if (tag != null)
                {
                    return tag;
                }
            }

            foreach (var subGroup in group.Groups)
            {
                var tag = FindPlcTag(subGroup, name);
                if (tag != null)
                {
                    return tag;
                }
            }

            return null;
        }

        private static object ConvertParameterValue(string text, object? current, string parameterName)
        {
            var trimmed = (text ?? string.Empty).Trim();

            try
            {
                switch (current)
                {
                    case null:
                    case string _:
                        return text ?? string.Empty;
                    case bool _:
                        if (trimmed == "1") return true;
                        if (trimmed == "0") return false;
                        return bool.Parse(trimmed);
                    case Enum e:
                        return Enum.Parse(e.GetType(), trimmed, ignoreCase: true);
                    default:
                        return Convert.ChangeType(trimmed, current.GetType(), CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException || ex is InvalidCastException)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"'{text}' is not a valid {current?.GetType().Name} for parameter '{parameterName}' (current value {FormatParameterValue(current)}). Use '.' as the decimal separator.", null, ex);
            }
        }

        private static IEnumerable<(TechnologicalInstanceDB Object, string Path)> EnumerateTechnologyObjects(PlcSoftware plcSoftware)
        {
            var root = plcSoftware.TechnologicalObjectGroup;
            if (root == null)
            {
                yield break;
            }

            foreach (var item in EnumerateTechnologyObjects(root, string.Empty))
            {
                yield return item;
            }
        }

        private static IEnumerable<(TechnologicalInstanceDB Object, string Path)> EnumerateTechnologyObjects(TechnologicalInstanceDBGroup group, string prefix)
        {
            foreach (var technologyObject in group.TechnologicalObjects)
            {
                yield return (technologyObject, prefix + technologyObject.Name);
            }

            foreach (var subGroup in group.Groups)
            {
                foreach (var item in EnumerateTechnologyObjects(subGroup, prefix + subGroup.Name + "/"))
                {
                    yield return item;
                }
            }
        }

        private static TechnologyObjectInfo Describe(TechnologicalInstanceDB technologyObject, string path)
        {
            var info = new TechnologyObjectInfo { Path = path, Name = technologyObject.Name };

            try
            {
                info.Type = technologyObject.OfSystemLibElement;
                info.Version = technologyObject.OfSystemLibVersion?.ToString();
            }
            catch (Exception)
            {
                // Type and version are informative only.
            }

            return info;
        }

        private static string? FormatParameterValue(object? value)
        {
            return value switch
            {
                null => null,
                string text => text,
                PlcTag tag => FormatTag(tag),
                bool flag => flag ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                IEngineeringObject engineeringObject => FormatEngineeringObject(engineeringObject),
                _ => value.ToString()
            };
        }

        /// <summary>A tag-valued parameter as "Name (%I10.0)", the way the TIA function view shows it.</summary>
        private static string FormatTag(PlcTag tag)
        {
            string? address = null;
            try
            {
                address = tag.LogicalAddress;
            }
            catch (Exception)
            {
                // The name alone is still useful.
            }

            return string.IsNullOrEmpty(address) ? tag.Name : $"{tag.Name} ({address})";
        }

        /// <summary>Other object-valued parameters (e.g. a connected drive or encoder): their name, not the .NET type.</summary>
        private static string FormatEngineeringObject(IEngineeringObject engineeringObject)
        {
            try
            {
                if (engineeringObject.GetAttribute("Name") is string name && name.Length > 0)
                {
                    return $"{name} ({engineeringObject.GetType().Name})";
                }
            }
            catch (Exception)
            {
                // No Name attribute on this kind of object.
            }

            return engineeringObject.GetType().Name;
        }
    }
}
