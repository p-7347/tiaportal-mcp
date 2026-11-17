using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region network topology (read)

        // Read-only network topology. Subnets live on Project, not ProjectBase - this returns
        // empty for a local multiuser session (.als) rather than throwing, since that's a real
        // Openness limitation, not a bug (verified via reflection: Project.Subnets exists,
        // ProjectBase does not).
        public List<Subnet> GetSubnets()
        {
            _logger?.LogInformation("Getting subnets...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<Subnet>();

            if (_project is Project project && project.Subnets != null)
            {
                foreach (var subnet in project.Subnets)
                {
                    list.Add(subnet);
                }
            }

            return list;
        }

        // path: a device or device item path - doesn't need to be the exact interface DeviceItem.
        // The network interface sits at an inconsistent depth depending on device type (e.g. one
        // level under a PLC's CPU DeviceItem, two levels under an HMI's communication-processor
        // DeviceItem) - live testing showed an agent burning several failed guesses and a full
        // GetProjectTree call trying to find the exact nested name by hand. So: try path as an
        // exact match first (fast path for a name copied straight out of GetProjectTree), and if
        // that doesn't itself carry a NetworkInterface, search every DeviceItem nested under it
        // and auto-resolve if exactly one is found. Returns the resolved path alongside the
        // interface so the caller can see what was actually used when it wasn't an exact match.
        public (NetworkInterface Interface, string ResolvedPath, List<PortConnectionInfo> Ports) GetNetworkInterfaceInfo(string path)
        {
            _logger?.LogInformation($"Getting network interface info for: {path}");
            var (iface, resolvedPath) = ResolveNetworkInterface(path);
            return (iface, resolvedPath, GetPortConnections(iface));
        }

        // Best-effort - NetworkPort.ConnectedPorts is documented "Internal use only" in the
        // shipped Openness XML docs, so a per-port try/catch keeps one misbehaving port from
        // breaking the whole list rather than trusting it unconditionally.
        private List<PortConnectionInfo> GetPortConnections(NetworkInterface iface)
        {
            var results = new List<PortConnectionInfo>();

            foreach (var port in iface.Ports)
            {
                var name = (port.Parent as DeviceItem)?.Name ?? port.ToString() ?? "?";
                string? connectedDeviceItem = null;
                string? connectedPortName = null;

                try
                {
                    var connected = port.ConnectedPorts?.FirstOrDefault();
                    if (connected != null)
                    {
                        connectedPortName = (connected.Parent as DeviceItem)?.Name;
                        connectedDeviceItem = connected.Interface?.OwnedBy?.Name;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "ConnectedPorts lookup failed for port {PortName} - leaving it unconnected in the response", name);
                }

                results.Add(new PortConnectionInfo
                {
                    Name = name,
                    ConnectedDeviceItemName = connectedDeviceItem,
                    ConnectedPortName = connectedPortName
                });
            }

            return results;
        }

        // Shared by GetNetworkInterfaceInfo (read) and the network write tools below - one
        // resolution path so the auto-descend/ambiguity behavior can't drift between them.
        private (NetworkInterface Interface, string ResolvedPath) ResolveNetworkInterface(string path)
        {
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var itemRoot = FindDeviceItemByFullPath(path);
            Device? deviceRoot = null;

            if (itemRoot != null)
            {
                var directIface = itemRoot.GetService<NetworkInterface>();
                if (directIface != null)
                {
                    return (directIface, path);
                }
            }
            else
            {
                deviceRoot = FindDeviceByFullName(path) ?? GetDevice(path);
            }

            if (itemRoot == null && deviceRoot == null)
            {
                throw new PortalException(PortalErrorCode.NotFound, $"Device or device item not found at '{path}'");
            }

            var childItems = itemRoot?.DeviceItems ?? deviceRoot?.DeviceItems;
            var candidates = new List<(DeviceItem Item, string Path)>();
            if (childItems != null)
            {
                foreach (var child in childItems)
                {
                    CollectNetworkInterfaceDeviceItems(child, path, candidates);
                }
            }

            if (candidates.Count == 0)
            {
                throw new PortalException(PortalErrorCode.NotFound, $"No network interface found at or under '{path}' - try GetProjectTree to locate the exact DeviceItem");
            }

            if (candidates.Count > 1)
            {
                var list = string.Join("; ", candidates.Select(c => c.Path));
                throw new PortalException(PortalErrorCode.InvalidParams, $"Multiple network interfaces found under '{path}' - pass one of these as path: {list}");
            }

            var (foundItem, resolvedPath) = candidates[0];
            return (foundItem.GetService<NetworkInterface>()!, resolvedPath);
        }

        private static void CollectNetworkInterfaceDeviceItems(DeviceItem item, string parentPath, List<(DeviceItem Item, string Path)> results)
        {
            var path = $"{parentPath}/{item.Name}";

            if (item.GetService<NetworkInterface>() != null)
            {
                results.Add((item, path));
            }

            if (item.DeviceItems != null)
            {
                foreach (var child in item.DeviceItems)
                {
                    CollectNetworkInterfaceDeviceItems(child, path, results);
                }
            }
        }

        // Resolves a nested DeviceItem path of arbitrary depth (e.g. a network interface tucked
        // inside a CPU's DeviceItems, like "S7-1500/ET200MP station_1/PLC_1/PROFINET interface_1")
        // - unlike GetDeviceItem, which only reaches one level below the device. Tries the
        // longest possible prefix as the device name first (device names can themselves contain
        // '/', and devices can live inside nested device groups), then walks every remaining
        // segment through nested .DeviceItems.
        private DeviceItem? FindDeviceItemByFullPath(string path)
        {
            if (IsProjectNull() || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var segments = path.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                // A bare name such as 'PLC_1' is usually the CPU, a device item, not the station.
                return FindDeviceByFullName(path) == null ? FindDeviceItemInAnyDevice(path.Trim()) : null;
            }

            for (int deviceLen = segments.Length - 1; deviceLen >= 1; deviceLen--)
            {
                var candidatePath = string.Join("/", segments.Take(deviceLen));
                var device = FindDeviceByFullName(candidatePath) ?? GetDevice(candidatePath);
                if (device == null)
                {
                    continue;
                }

                DeviceItem? current = null;
                DeviceItemComposition? items = device.DeviceItems;
                for (int i = deviceLen; i < segments.Length; i++)
                {
                    if (items == null)
                    {
                        current = null;
                        break;
                    }

                    // The project tree does not show the rack between a station and its CPU
                    // ('station/Rail_0/PLC_1'), so a segment may sit deeper than the next level.
                    current = items.FirstOrDefault(x => x.Name.Equals(segments[i], StringComparison.OrdinalIgnoreCase))
                        ?? FindDeviceItemBelow(items, segments[i]);
                    if (current == null)
                    {
                        break;
                    }

                    items = current.DeviceItems;
                }

                if (current != null)
                {
                    return current;
                }
            }

            return null;
        }

        /// <summary>The first device item of that name anywhere below <paramref name="items"/>, nearest level first.</summary>
        private static DeviceItem? FindDeviceItemBelow(DeviceItemComposition items, string name)
        {
            var queue = new Queue<DeviceItem>(items);

            while (queue.Count > 0)
            {
                var item = queue.Dequeue();

                if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }

                foreach (var child in item.DeviceItems)
                {
                    queue.Enqueue(child);
                }
            }

            return null;
        }

        private DeviceItem? FindDeviceItemInAnyDevice(string name)
        {
            foreach (var device in GetDevices())
            {
                var found = FindDeviceItemBelow(device.DeviceItems, name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        // FindDeviceByFullName (device names can contain '/', and some devices live only under
        // "Ungrouped devices") lives in Portal.Devices.cs - shared with GetDeviceByPath, same
        // partial class.

        #endregion

        #region network topology (write)

        // Sets IP/subnet mask/router on every node under a network interface (same auto-resolving
        // path as GetNetworkInterfaceInfo). Important: this only changes the TIA *project's*
        // metadata - nothing reaches real hardware until the project is compiled and downloaded.
        // Changing the address can also regenerate the PROFINET device name hash
        // (PnDeviceNameConverted) - if that no longer matches what's actually downloaded to a
        // switch/drive on the line, the online connection to that device breaks. The response
        // message says this explicitly so a caller can't mistake "set" for "applied".
        public (NetworkInterface Interface, string ResolvedPath) SetIpAddress(string path, string ipAddress, string subnetMask, string? routerAddress = null)
        {
            return Operation.Run(_logger, nameof(SetIpAddress), PortalErrorCode.ExportFailed,
                () =>
                {
                    var (iface, resolvedPath) = ResolveNetworkInterface(path);

                    foreach (var node in iface.Nodes)
                    {
                        node.SetAttribute("Address", ipAddress);
                        node.SetAttribute("SubnetMask", subnetMask);
                        if (!string.IsNullOrEmpty(routerAddress))
                        {
                            node.SetAttribute("UseRouter", true);
                            node.SetAttribute("RouterAddress", routerAddress);
                        }
                    }

                    return (iface, resolvedPath);
                },
                ("path", path), ("ipAddress", ipAddress), ("subnetMask", subnetMask));
        }

        // Connects every node under a network interface to the named subnet (must already exist -
        // see GetSubnets). Same "project metadata only" caveat as SetIpAddress.
        public (NetworkInterface Interface, string ResolvedPath, string SubnetName) ConnectToSubnet(string path, string subnetName)
        {
            return Operation.Run(_logger, nameof(ConnectToSubnet), PortalErrorCode.ExportFailed,
                () =>
                {
                    if (_project is not Project project)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState, "Subnets (and ConnectToSubnet) are only available on a full project, not a multiuser local session");
                    }

                    var subnet = project.Subnets?.FirstOrDefault(s => s.Name.Equals(subnetName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new PortalException(PortalErrorCode.NotFound, $"Subnet '{subnetName}' not found - see GetSubnets for the real names");

                    var (iface, resolvedPath) = ResolveNetworkInterface(path);

                    foreach (var node in iface.Nodes)
                    {
                        node.ConnectToSubnet(subnet);
                    }

                    return (iface, resolvedPath, subnet.Name);
                },
                ("path", path), ("subnetName", subnetName));
        }

        public (NetworkInterface Interface, string ResolvedPath) DisconnectFromSubnet(string path)
        {
            return Operation.Run(_logger, nameof(DisconnectFromSubnet), PortalErrorCode.ExportFailed,
                () =>
                {
                    var (iface, resolvedPath) = ResolveNetworkInterface(path);

                    foreach (var node in iface.Nodes)
                    {
                        node.DisconnectFromSubnet();
                    }

                    return (iface, resolvedPath);
                },
                ("path", path));
        }

        #endregion
    }

    // One physical port on a network interface, and (best-effort) what it's wired to per the
    // project's own planned topology - the same shape PRONETA's "Include device details" CSV
    // export uses (Port ID / Partner Port ID / Partner Device Name), so a project's own topology
    // can be diff'd against a real PRONETA scan without needing a shared format beyond this.
    public class PortConnectionInfo
    {
        public string Name { get; set; } = "";
        public string? ConnectedDeviceItemName { get; set; }
        public string? ConnectedPortName { get; set; }
    }
}
