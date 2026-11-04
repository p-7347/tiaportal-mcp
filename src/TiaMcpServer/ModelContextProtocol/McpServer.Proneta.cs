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
        #region network topology comparison (PRONETA CSV)

        [McpServerTool(Name = "CompareNetworkCsv", Title = "Compare PRONETA network CSV", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Compare a PRONETA 'Device Table (CSV)' export (with 'Include device details') against the open project's devices. Read-only. Matches by device name, case-insensitive (Openness has no MAC address). matchMethod null = no TIA device of that name; ipDiffers = live IP differs from the project. Have a human confirm a mismatch before calling 'SetIpAddress'.")]
        public static ResponseCompareNetworkCsv CompareNetworkCsv(
            [Description("csvPath: full path to the PRONETA CSV export file")] string csvPath)
        {
            try
            {
                var matches = Portal.CompareNetworkCsv(csvPath);

                var items = matches.Select(m => new ResponseNetworkCsvMatch
                {
                    PronetaName = m.PronetaDevice.Name,
                    DeviceType = m.PronetaDevice.DeviceType,
                    PronetaIp = m.PronetaDevice.IpAddress,
                    PronetaSubnetMask = m.PronetaDevice.SubnetMask,
                    MacAddress = m.PronetaDevice.MacAddress,
                    Role = m.PronetaDevice.Role,
                    MatchedTiaDeviceName = m.MatchedTiaDeviceName,
                    MatchMethod = m.MatchMethod,
                    TiaCurrentIp = m.TiaCurrentIp,
                    TiaCurrentSubnetMask = m.TiaCurrentSubnetMask,
                    IpDiffers = m.IpDiffers,
                    Ports = m.PronetaDevice.Ports.Select(p => new ResponsePronetaPort
                    {
                        PortId = p.PortId,
                        PartnerPortId = p.PartnerPortId,
                        PartnerDeviceName = p.PartnerDeviceName
                    }).ToList()
                }).ToList();

                var unmatched = items.Count(i => i.MatchedTiaDeviceName == null);
                var ipMismatches = items.Count(i => i.IpDiffers == true);

                return new ResponseCompareNetworkCsv
                {
                    Message = $"Compared {items.Count} PRONETA device(s): {items.Count - unmatched} matched by name, {unmatched} unmatched, {ipMismatches} with a differing IP",
                    Items = items,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error comparing network CSV '{csvPath}': {ex.Message}", ex);
            }
        }

        #endregion
    }
}
