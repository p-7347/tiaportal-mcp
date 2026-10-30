using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // Technology objects (axes, encoders, PID, counters, ...). See Portal.TechnologyObjects.cs.

        [McpServerTool(Name = "GetTechnologyObjects", Title = "Get technology objects", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("List the technology objects of a PLC software (axes, encoders, cams, PID, counters, ...) with their type and version. They are not program blocks, so the block tools do not show them; read one with 'GetTechnologyObjectParameters'")]
        public static ResponseTechnologyObjects GetTechnologyObjects(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("regexName: optional regular expression on the technology object name")] string regexName = "")
        {
            try
            {
                var items = Portal.GetTechnologyObjects(softwarePath, regexName);

                return new ResponseTechnologyObjects
                {
                    Items = items.Select(t => new ResponseTechnologyObject { Path = t.Path, Name = t.Name, Type = t.Type, Version = t.Version }).ToList(),
                    Message = $"{items.Count} technology object(s) in '{softwarePath}'.",
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing the technology objects of '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetTechnologyObjectParameters", Title = "Get technology object parameters", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Read the configuration parameters of one technology object, e.g. an axis: homing, position limits, hardware limit switches, encoder, drive and mechanics settings as name/value pairs (the offline project values). Address parameters show the assigned PLC tag as 'Name (%I10.0)'; an unassigned one has no value. Coded values of an axis also come with their meaning in 'valueText', e.g. Homing.ApproachDirection true = 'Negative direction'. Filter with 'regexName', e.g. 'Homing|PositionLimits'. Reads only")]
        public static ResponseTechnologyObjectParameters GetTechnologyObjectParameters(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("objectPath: name or group path of the technology object, as listed by 'GetTechnologyObjects'")] string objectPath,
            [Description("regexName: optional regular expression on the parameter name, e.g. 'Homing|PositionLimits'")] string regexName = "",
            [Description("maxParameters: maximum number of parameters listed (default 500); 'matched' is always the full count")] int maxParameters = 500)
        {
            try
            {
                var result = Portal.GetTechnologyObjectParameters(softwarePath, objectPath, regexName, TimeSpan.FromSeconds(40));
                var limit = Math.Max(0, maxParameters);
                var complete = result.StoppedReason == null;

                var response = new ResponseTechnologyObjectParameters
                {
                    Object = new ResponseTechnologyObject
                    {
                        Path = result.Object.Path,
                        Name = result.Object.Name,
                        Type = result.Object.Type,
                        Version = result.Object.Version
                    },
                    Total = result.Total,
                    Matched = result.Parameters.Count,
                    Parameters = result.Parameters.Take(limit).Select(p => new ResponseTechnologyObjectParameter { Name = p.Name, Value = p.Value, ValueText = p.ValueText, Error = p.Error }).ToList(),
                    StoppedReason = result.StoppedReason
                };

                var message = $"{response.Matched} of {response.Total} parameter(s) of '{result.Object.Path}'" +
                              (string.IsNullOrEmpty(regexName) ? string.Empty : $" match '{regexName}'") +
                              (response.Matched > limit ? $" (first {limit} listed)" : string.Empty);

                if (!complete)
                {
                    message += $" - INCOMPLETE: {result.StoppedReason}";
                }

                response.Message = message + ".";
                response.Meta = new JsonObject
                {
                    ["timestamp"] = DateTime.Now,
                    ["success"] = true,
                    ["complete"] = complete
                };

                return response;
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the technology object '{objectPath}' of '{softwarePath}': {ex.Message}", ex);
            }
        }

        [WriteTool]
        [McpServerTool(Name = "SetTechnologyObjectParameter", Title = "Set technology object parameter", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Change one configuration parameter of a technology object in the offline project, e.g. an axis homing mode or a limit switch setting. The value is converted to the parameter's type; TIA Portal checks the range and refuses read-only parameters. Takes effect in the PLC only after compile and download")]
        public static ResponseTechnologyObjectParameterSet SetTechnologyObjectParameter(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("objectPath: name or group path of the technology object, as listed by 'GetTechnologyObjects'")] string objectPath,
            [Description("parameterName: exact parameter name as listed by 'GetTechnologyObjectParameters', e.g. 'Homing.Mode'")] string parameterName,
            [Description("value: new value as text, e.g. 'true', '2' or '12.5' ('.' as decimal separator); for an address parameter (name ends in 'Address') the name of a PLC tag")] string value)
        {
            return Guarded(nameof(SetTechnologyObjectParameter), () =>
            {
                var (path, oldValue, newValue) = Portal.SetTechnologyObjectParameter(softwarePath, objectPath, parameterName, value);

                return new ResponseTechnologyObjectParameterSet
                {
                    ObjectPath = path,
                    ParameterName = parameterName,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Message = $"'{path}' parameter '{parameterName}' changed from {oldValue ?? "empty"} to {newValue ?? "empty"}. Compile and download for the PLC to use it. {SaveHint}",
                    Meta = OkMeta()
                };
            });
        }
    }
}
