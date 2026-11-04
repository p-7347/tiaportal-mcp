using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        #region devices

        [McpServerTool(Name = "GetDeviceInfo", Title = "Get device info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Show name, description and attributes of one device in the current project or session")]
        public static ResponseDeviceInfo GetDeviceInfo(
            [Description("devicePath: defines the path in the project structure to the device")] string devicePath)
        {
            try
            {
                var device = Portal.GetDevice(devicePath);

                if (device is not null)
                {
                    var attributes = Helper.GetAttributeList(device);

                    return new ResponseDeviceInfo
                    {
                        Message = $"Device info retrieved from '{devicePath}'",
                        Name = device.Name,
                        Attributes = attributes,
                        Description = device.ToString(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Device not found at '{devicePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving device info from '{devicePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetDeviceItemInfo", Title = "Get device item info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Show name, description and attributes of one device item in the current project or session")]
        public static ResponseDeviceItemInfo GetDeviceItemInfo(
            [Description("deviceItemPath: defines the path in the project structure to the device item")] string deviceItemPath)
        {
            try
            {
                var deviceItem = Portal.GetDeviceItem(deviceItemPath);

                if (deviceItem is not null)
                {
                    var attributes = Helper.GetAttributeList(deviceItem);

                    return new ResponseDeviceItemInfo
                    {
                        Message = $"Device item info retrieved from '{deviceItemPath}'",
                        Name = deviceItem.Name,
                        Attributes = attributes,
                        Description = deviceItem.ToString(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Device item not found at '{deviceItemPath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving device item info from '{deviceItemPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetDevices", Title = "Get devices", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("List all devices of the current project or session")]
        public static ResponseDevices GetDevices()
        {
            try
            {
                var list = Portal.GetDevices();
                var responseList = new List<ResponseDeviceInfo>();

                if (list != null)
                {
                    foreach (var device in list)
                    {
                        if (device != null)
                        {
                            var attributes = Helper.GetAttributeList(device);
                            responseList.Add(new ResponseDeviceInfo
                            {
                                Name = device.Name,
                                Attributes = attributes,
                                Description = device.ToString()
                            });
                        }
                    }

                    return new ResponseDevices
                    {
                        Message = "Devices retrieved",
                        Items = responseList,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed retrieving devices");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving devices: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetGsdDependencies", Title = "Get GSD device dependencies", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("List the third-party (GSD-based) devices and items in the hardware config with their GSD identity, e.g. to check before moving a project to another PC. Devices whose GSD is missing are not loaded at all and do not appear; empty GetDevices after Connect points to that.")]
        public static ResponseGsdDependencies GetGsdDependencies(
            [Description("devicePath: optional - scope the scan to a single device (its name, e.g. 'S7-1500/ET200MP station_1'); omit to scan every device in the project")] string? devicePath = null)
        {
            try
            {
                var list = Portal.GetGsdDependencies(devicePath);

                var responseList = list.Select(g => new ResponseGsdReference
                {
                    Path = g.Path,
                    Name = g.Name,
                    GsdId = g.GsdId,
                    GsdName = g.GsdName,
                    GsdType = g.GsdType,
                    IsProfibus = g.IsProfibus,
                    IsProfinet = g.IsProfinet
                }).ToList();

                return new ResponseGsdDependencies
                {
                    Message = responseList.Count > 0
                        ? $"Found {responseList.Count} GSD-based device(s)"
                        : "No GSD-based devices found - every device in scope is a native Siemens catalog device",
                    Items = responseList,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error scanning for GSD dependencies: {ex.Message}", ex);
            }
        }

        #endregion

        #region device master copies / hardware catalog search

        [McpServerTool(Name = "GetDeviceMasterCopies", Title = "Get device master copies", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("List master copies (saved device/module templates) in the project library. To build a device prefer 'CreateDevice'/'CreateDeviceWithItem' with a catalog TypeIdentifier; use master copies only to replicate a saved template.")]
        public static ResponseDeviceMasterCopies GetDeviceMasterCopies()
        {
            try
            {
                var paths = Portal.GetDeviceMasterCopyPaths();

                return new ResponseDeviceMasterCopies
                {
                    Message = paths.Count > 0
                        ? $"Found {paths.Count} master cop(y/ies) in the project library"
                        : "No master copies in the project library - there's nothing to copy a fully-populated device from",
                    Paths = paths,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving device master copies: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "FindHardwareCatalogEntries", Title = "Search hardware catalog", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Search the TIA Portal hardware catalog by text (article number, fragment or product name like \"G120C\" or \"6ES7 510\"). Returns TypeIdentifier/ArticleNumber/Version of each match. Use its TypeIdentifier for 'CreateDevice'/'CreateDeviceWithItem'; one read off a placed device is refused.")]
        public static ResponseHardwareCatalogEntries FindHardwareCatalogEntries(
            [Description("Filter text, e.g. an article number or product name fragment")] string filter)
        {
            try
            {
                var entries = Portal.FindHardwareCatalogEntries(filter);

                return new ResponseHardwareCatalogEntries
                {
                    Message = entries.Count > 0
                        ? $"Found {entries.Count} catalog entr(y/ies) matching '{filter}'"
                        : $"No catalog entries matched '{filter}'",
                    Entries = entries.Select(e => new ResponseCatalogEntry
                    {
                        TypeName = e.TypeName,
                        TypeIdentifier = e.TypeIdentifier,
                        TypeIdentifierNormalized = e.TypeIdentifierNormalized,
                        ArticleNumber = e.ArticleNumber,
                        Version = e.Version,
                        CatalogPath = e.CatalogPath
                    }),
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error searching hardware catalog for '{filter}': {ex.Message}", ex);
            }
        }

        #endregion
    }
}
