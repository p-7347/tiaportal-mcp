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
        #region network topology (read)

        [McpServerTool(Name = "GetSubnets", Title = "Get subnets", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("List every subnet in the project (network topology) with its type and the devices/IO systems connected to it. Only works when attached to a full project - subnets aren't exposed for a multiuser local session (.als), returns an empty list there rather than erroring.")]
        public static ResponseSubnets GetSubnets()
        {
            try
            {
                var list = Portal.GetSubnets();

                var responseList = list.Select(subnet => new ResponseSubnetInfo
                {
                    Name = subnet.Name,
                    NetType = subnet.NetType.ToString(),
                    TypeIdentifier = subnet.TypeIdentifier,
                    NodeNames = subnet.Nodes.Select(n => n.Name).ToList(),
                    IoSystemNames = subnet.IoSystems.Select(s => s.Name).ToList(),
                    Attributes = Helper.GetAttributeList(subnet)
                }).ToList();

                return new ResponseSubnets
                {
                    Message = $"Found {responseList.Count} subnet(s)",
                    Items = responseList,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving subnets: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetNetworkInterfaceInfo", Title = "Get network interface info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get network topology info (nodes, subnet, IO controller/device role, per-port wiring) for a device's network interface. 'path' may be the device or any item above the interface (e.g. 'PLC_1'); 'resolvedPath' shows what was used, and an ambiguous path lists the interfaces found. Port partners are the project's planned topology (may be empty), not what is plugged in on the line.")]
        public static ResponseNetworkInterfaceInfo GetNetworkInterfaceInfo(
            [Description("path: a device or device item path, e.g. 'S7-1500/ET200MP station_1/PLC_1' or just 'HMI_1' - doesn't need to be the exact interface DeviceItem")] string path)
        {
            try
            {
                var (iface, resolvedPath, ports) = Portal.GetNetworkInterfaceInfo(path);

                var nodes = iface.Nodes.Select(n => new ResponseNetworkNodeInfo
                {
                    Name = n.Name,
                    ConnectedSubnetName = n.ConnectedSubnet?.Name,
                    Attributes = Helper.GetAttributeList(n)
                }).ToList();

                var responsePorts = ports.Select(p => new ResponsePortInfo
                {
                    Name = p.Name,
                    ConnectedDeviceItemName = p.ConnectedDeviceItemName,
                    ConnectedPortName = p.ConnectedPortName
                }).ToList();

                return new ResponseNetworkInterfaceInfo
                {
                    Message = resolvedPath == path
                        ? $"Network interface info retrieved for '{path}'"
                        : $"Network interface info retrieved - resolved '{path}' to '{resolvedPath}'",
                    ResolvedPath = resolvedPath,
                    InterfaceType = iface.InterfaceType.ToString(),
                    Nodes = nodes,
                    PortCount = iface.Ports.Count(),
                    Ports = responsePorts,
                    IoControllerOfIoSystem = iface.IoControllers.FirstOrDefault()?.IoSystem?.Name,
                    IoConnectorOfIoSystem = iface.IoConnectors.FirstOrDefault()?.ConnectedToIoSystem?.Name,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving network interface info for '{path}': {ex.Message}", ex);
            }
        }

        #endregion

        #region network topology (write)

        [WriteTool]
        [McpServerTool(Name = "SetIpAddress", Title = "Set IP address", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Set IP address/subnet mask/router on a device's network interface (same auto-resolving path as GetNetworkInterfaceInfo). IMPORTANT: this only changes the TIA project's saved metadata - nothing reaches real hardware until the project is compiled and downloaded. Changing the address can also regenerate the PROFINET device name hash used to match physical hardware on the wire; if that no longer matches what's actually downloaded to a switch/drive, the online connection to that device breaks. Confirm with the user before calling this against a real (non-disposable) project, and make clear to them that this alone does not reach the PLC/line.")]
        public static ResponseSetIpAddress SetIpAddress(
            [Description("path: a device or device item path, same auto-resolving rules as GetNetworkInterfaceInfo")] string path,
            [Description("ipAddress: e.g. '192.168.0.1'")] string ipAddress,
            [Description("subnetMask: e.g. '255.255.255.0'")] string subnetMask,
            [Description("routerAddress: optional - omit to leave routing unset")] string? routerAddress = null)
        {
            return Guarded(nameof(SetIpAddress), () =>
            {
                var (_, resolvedPath) = Portal.SetIpAddress(path, ipAddress, subnetMask, routerAddress);

                return new ResponseSetIpAddress
                {
                    Message = $"IP address set to '{ipAddress}' on '{resolvedPath}' - project metadata only, not applied to real hardware until compiled and downloaded. {SaveHint}",
                    ResolvedPath = resolvedPath,
                    Meta = OkMeta()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "ConnectToSubnet", Title = "Connect to subnet", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Connect a device's network interface to a named subnet (must already exist - see GetSubnets). Same auto-resolving path as GetNetworkInterfaceInfo, and same project-metadata-only caveat as SetIpAddress - nothing reaches real hardware until compiled and downloaded. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseConnectToSubnet ConnectToSubnet(
            [Description("path: a device or device item path, same auto-resolving rules as GetNetworkInterfaceInfo")] string path,
            [Description("subnetName: name of an existing subnet, from GetSubnets")] string subnetName)
        {
            return Guarded(nameof(ConnectToSubnet), () =>
            {
                var (_, resolvedPath, resolvedSubnetName) = Portal.ConnectToSubnet(path, subnetName);

                return new ResponseConnectToSubnet
                {
                    Message = $"'{resolvedPath}' connected to subnet '{resolvedSubnetName}' - project metadata only, not applied to real hardware until compiled and downloaded. {SaveHint}",
                    ResolvedPath = resolvedPath,
                    SubnetName = resolvedSubnetName,
                    Meta = OkMeta()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DisconnectFromSubnet", Title = "Disconnect from subnet", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Disconnect a device's network interface from its subnet. Same auto-resolving path as GetNetworkInterfaceInfo. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseDisconnectFromSubnet DisconnectFromSubnet(
            [Description("path: a device or device item path, same auto-resolving rules as GetNetworkInterfaceInfo")] string path)
        {
            return Guarded(nameof(DisconnectFromSubnet), () =>
            {
                var (_, resolvedPath) = Portal.DisconnectFromSubnet(path);

                return new ResponseDisconnectFromSubnet
                {
                    Message = $"'{resolvedPath}' disconnected from its subnet. {SaveHint}",
                    ResolvedPath = resolvedPath,
                    Meta = OkMeta()
                };
            });
        }

        #endregion
    }
}
