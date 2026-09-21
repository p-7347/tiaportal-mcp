using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.Cax;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    [McpServerToolType]
    public static class McpServer
    {
        private static IServiceProvider? _services;
        private static Portal? _portal;

        public static ILogger? Logger { get; set; }

        public static Portal Portal
        {
            get
            {
                if (_services !=null)
                {
                    return _services.GetRequiredService<Portal>();
                }
                else
                {
                    if (_portal == null)
                    {
                        _portal = new Portal();
                    }
                    return _portal;
                }
            }
            set
            {
                _portal = value ?? throw new ArgumentNullException(nameof(value), "Portal cannot be null");
            }
        }

        public static void SetServiceProvider(IServiceProvider services)
        {
            _services = services;
        }

        #region portal

        [McpServerTool(Name = "ListTiaPortalInstances", Title = "List TIA Portal instances", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("List every running TIA Portal process on this machine (id, open project path, mode) without attaching to any of them. Use this before Connect if more than one might be open, to pick the right processId.")]
        public static ResponseTiaPortalInstances ListTiaPortalInstances()
        {
            try
            {
                var processes = Portal.GetTiaPortalProcesses();

                return new ResponseTiaPortalInstances
                {
                    Message = $"{processes.Count} TIA Portal process(es) found",
                    Items = processes.Select(p => new ResponseTiaPortalInstance
                    {
                        Id = p.Id,
                        ProjectPath = p.ProjectPath,
                        Mode = p.Mode
                    }).ToList(),
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing TIA Portal instances: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "Connect", Title = "Connect to TIA Portal", Destructive = false, Idempotent = true, OpenWorld = false), Description("Connect to TIA-Portal. If more than one TIA Portal process is running, this fails and asks you to call ListTiaPortalInstances first, then pass its processId explicitly - it never silently guesses which one you meant.")]
        public static ResponseConnect Connect(
            [Description("processId: PID of a specific running TIA Portal process to attach to, from ListTiaPortalInstances. Omit when only one instance is running.")] int? processId = null)
        {
            Logger?.LogInformation("Connecting to TIA Portal...");

            try
            {
                if (Portal.ConnectPortal(processId))
                {
                    return new ResponseConnect
                    {
                        Message = "Connected to TIA-Portal",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException("Failed to connect to TIA-Portal");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                var detail = $"[{ex.GetType().Name}] {ex.Message}";
                if (ex.InnerException != null)
                    detail += $" | Inner: [{ex.InnerException.GetType().Name}] {ex.InnerException.Message}";
                throw new McpException($"Connect error: {detail}", ex);
            }
        }

        [McpServerTool(Name = "Disconnect", Title = "Disconnect from TIA Portal", Destructive = false, Idempotent = true, OpenWorld = false), Description("Disconnect from TIA-Portal")]
        public static ResponseDisconnect Disconnect()
        {
            try
            {
                if (Portal.DisconnectPortal())
                {
                    return new ResponseDisconnect
                    {
                        Message = "Disconnected from TIA-Portal",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException("Failed disconnecting from TIA-Portal");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error disconnecting from TIA-Portal: {ex.Message}", ex);
            }
        }

        #endregion

        #region state

        [McpServerTool(Name = "GetState", Title = "Get server state", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get the state of the TIA-Portal MCP server")]
        public static ResponseState GetState()
        {
            try
            {
                var state = Portal.GetState();

                if (state != null)
                {
                    return new ResponseState
                    {
                        Message = "TIA-Portal MCP server state retrieved",
                        IsConnected = state.IsConnected,
                        Project = state.Project,
                        Session = state.Session,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException("Failed to retrieve TIA-Portal MCP server state");
                }
                

            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving TIA-Portal MCP server state: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "Doctor", Title = "Diagnose the TIA Portal environment", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Diagnose the TIA-Portal environment: connection, open project, active and installed TIA-Portal versions, Openness user group membership, and the managed export root folder (all Export* tools write inside it)")]
        public static ResponseDoctor Doctor()
        {
            Logger?.LogInformation("Running TIA Portal diagnostics...");

            try
            {
                // Fully qualified: 'Diagnostics' alone would collide with the System.Diagnostics namespace.
                var report = TiaMcpServer.Siemens.Diagnostics.Run(Portal);

                return new ResponseDoctor
                {
                    Message = "TIA-Portal environment diagnosed",
                    Report = report.Text,
                    IsConnected = report.IsConnected,
                    ActiveTiaMajorVersion = report.ActiveTiaMajorVersion,
                    ProjectName = report.ProjectName,
                    ProjectPath = report.ProjectPath,
                    IsUserInGroup = report.IsUserInGroup,
                    ExportRoot = TiaMcpServer.Siemens.OutputPathPolicy.Root,
                    Installations = report.Installations
                        .Select(i => new ResponseTiaInstallation
                        {
                            MajorVersion = i.MajorVersion,
                            InstallPath = i.InstallPath,
                            EngineeringExists = i.EngineeringExists,
                            PortalExeExists = i.PortalExeExists
                        })
                        .ToList(),
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error diagnosing the TIA-Portal environment: {ex.Message}", ex);
            }
        }

        #endregion

        #region project/session

        [McpServerTool(Name = "GetProjects", Title = "List open projects", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("List every open local project/session in this TIA Portal instance. Use GetProject for just the single currently-active one.")]
        public static ResponseGetProjects GetProjects()
        {
            try
            {
                var list = Portal.GetProjects();

                list.AddRange(Portal.GetSessions());

                var responseList = new List<ResponseProjectInfo>();
                foreach (var project in list)
                {
                    var attributes = Helper.GetAttributeList(project);

                    if (project != null)
                    {
                        responseList.Add(new ResponseProjectInfo
                        {
                            Name = project.Name,
                            Attributes = attributes
                        });
                    }
                }

                return new ResponseGetProjects
                {
                    Message = "Open projects and sessions retrieved",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving open projects: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetProject", Title = "Get active project", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get the single currently-active project/session this server is attached to. Use GetProjects to list every open project/session instead.")]
        public static ResponseProjectInfo GetProject()
        {
            try
            {
                var project = Portal.GetActiveProject();

                if (project == null)
                {
                    throw new McpException("No project is open in TIA Portal");
                }

                var attributes = Helper.GetAttributeList(project);

                return new ResponseProjectInfo
                {
                    Name = project.Name,
                    Attributes = attributes,
                    Message = "Active project retrieved",
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving active project: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "OpenProject", Title = "Open project or session", Destructive = false, Idempotent = true, OpenWorld = false), Description("Open a TIA-Portal local project/session")]
        public static ResponseOpenProject OpenProject(
            [Description("path: defines the path where to the project/session")] string path)
        {
            try
            {
                Portal.CloseProject();

                // get project extension
                string extension = Path.GetExtension(path).ToLowerInvariant();

                // use regex to check if extension is .ap\d+ or .als\d+
                if (!Regex.IsMatch(extension, @"^\.ap\d+$") &&
                    !Regex.IsMatch(extension, @"^\.als\d+$"))
                {
                    throw new McpException("Invalid project file extension. Use .apXX for projects or .alsXX for sessions, where XX=18,19,20,....");
                }

                bool success = false;

                if (extension.StartsWith(".ap"))
                {
                    success = Portal.OpenProject(path);
                }
                if (extension.StartsWith(".als"))
                {
                    success = Portal.OpenSession(path);
                }

                if (success)
                {
                    return new ResponseOpenProject
                    {
                        Message = $"Project '{path}' opened",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed to open project '{path}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error opening project '{path}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "SaveProject", Title = "Save project", Destructive = true, Idempotent = true, OpenWorld = false), Description("Save the current TIA-Portal local project/session")]
        public static ResponseSaveProject SaveProject()
        {
            try
            {
                if (Portal.IsLocalSession)
                {
                    if (Portal.SaveSession())
                    {
                        return new ResponseSaveProject
                        {
                            Message = "Local session saved",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed to save local session");
                    }
                }
                else
                {
                    if (Portal.SaveProject())
                    {
                        return new ResponseSaveProject
                        {
                            Message = "Local project saved",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed to save project");
                    }
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error saving local project/session: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "SaveAsProject", Title = "Save project as", Destructive = true, Idempotent = true, OpenWorld = false), Description("Save current TIA-Portal project/session with a new name")]
        public static ResponseSaveAsProject SaveAsProject(
            [Description("newProjectPath: defines the new path where to save the project")] string newProjectPath)
        {
            try
            {
                if (Portal.IsLocalSession)
                {
                    throw new McpException($"Cannot save local session as '{newProjectPath}'");
                }
                else
                {
                    if (Portal.SaveAsProject(newProjectPath))
                    {
                        return new ResponseSaveAsProject
                        {
                            Message = $"Local project saved as '{newProjectPath}'",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException($"Failed saving local project as '{newProjectPath}'");
                    }
                }

            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error saving local project/session as '{newProjectPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CloseProject", Title = "Close project", Destructive = true, Idempotent = true, OpenWorld = false), Description("Close the current TIA-Portal project/session")]
        public static ResponseCloseProject CloseProject()
        {
            try
            {
                bool success;

                if (Portal.IsLocalSession)
                {
                    success = Portal.CloseSession();
                    if (success)
                    {
                        return new ResponseCloseProject
                        {
                            Message = "Local session closed",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed closing local session");
                    }
                }
                else
                {
                    success = Portal.CloseProject();
                    if (success)
                    {
                        return new ResponseCloseProject
                        {
                            Message = "Local project closed",
                            Meta = new JsonObject
                            {
                                ["timestamp"] = DateTime.Now,
                                ["success"] = true
                            }
                        };
                    }
                    else
                    {
                        throw new McpException("Failed closing project");
                    }
                }

            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error closing local project/session: {ex.Message}", ex);
            }
        }

        #endregion

        #region devices

        [McpServerTool(Name = "GetProjectTree", Title = "Get project tree", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get project structure as a tree view on current local project/session")]
        public static ResponseProjectTree GetProjectTree()
        {
            try
            {
                var tree = Portal.GetProjectTree();

                if (!string.IsNullOrEmpty(tree))
                {
                    return new ResponseProjectTree
                    {
                        Message = "Project tree retrieved",
                        Tree = "```\n" + tree + "\n```",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException("Failed retrieving project tree");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving project tree: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetDeviceInfo", Title = "Get device info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get info from a device from the current project/session")]
        public static ResponseDeviceInfo GetDeviceInfo(
            [Description("devicePath: defines the path in the project structure to the device")] string devicePath)
        {
            try
            {
                var device = Portal.GetDevice(devicePath);

                if (device != null)
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

        [McpServerTool(Name = "GetDeviceItemInfo", Title = "Get device item info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get info from a device item from the current project/session")]
        public static ResponseDeviceItemInfo GetDeviceItemInfo(
            [Description("deviceItemPath: defines the path in the project structure to the device item")] string deviceItemPath)
        {
            try
            {
                var deviceItem = Portal.GetDeviceItem(deviceItemPath);

                if (deviceItem != null)
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

        [McpServerTool(Name = "GetDevices", Title = "Get devices", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get a list of all devices in the project/session")]
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

        [McpServerTool(Name = "GetGsdDependencies", Title = "Get GSD device dependencies", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("List every third-party (GSD-based) device/device item in the project's hardware config, with its GSD identity (GsdId/GsdName/GsdType, Profibus/Profinet) - e.g. before moving a project to another machine, check this list against what's installed there. Only sees devices TIA already loaded successfully; a missing GSD can make TIA fail to instantiate the device at all, in which case it won't show up here either - if GetDevices/GetProject come back empty right after a successful Connect, that's a stronger sign of a missing GSD than an empty result from this tool.")]
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
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error scanning for GSD dependencies: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetDeviceMasterCopies", Title = "Get device master copies", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("List master copies (saved device/module templates) in the project's own library, for DeviceComposition.CreateFrom(MasterCopy)/DeviceItemComposition.CreateFrom(MasterCopy). Secondary route for building a device: prefer CreateDevice/CreateDeviceWithItem with a TypeIdentifier from FindHardwareCatalogEntries, which is verified to produce fully-populated devices (confirmed with a real CPU 1516-3 PN/DP, complete DP/PROFINET interfaces). Use this only if you specifically need to replicate a saved project template rather than a plain catalog part.")]
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
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving device master copies: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "FindHardwareCatalogEntries", Title = "Search hardware catalog", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Diagnostic tool: search TIA Portal's hardware catalog (TiaPortal.HardwareCatalog.Find) by a text filter (e.g. article number, order number fragment, or product name like \"G120C\" or \"6ES7 510\"). Returns each match's TypeIdentifier/TypeIdentifierNormalized/ArticleNumber/Version. Live testing showed CreateDevice/CreateDeviceWithItem reject the TypeIdentifier format read off an already-placed device (GetDevices) with \"wrong type\" errors or produce an empty shell - the catalog's own TypeIdentifier string may be the format those creation APIs actually expect. Use this to find a creation-ready TypeIdentifier before calling CreateDevice/CreateDeviceWithItem.")]
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
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error searching hardware catalog: {ex.Message}", ex);
            }
        }

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

        [McpServerTool(Name = "GetNetworkInterfaceInfo", Title = "Get network interface info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get network topology info (nodes, connected subnet, IO controller/connector role, per-port wiring) for a device's network interface. path doesn't need to be the exact interface DeviceItem - give a device or device item path (e.g. just the device name, or 'PLC_1') and it auto-searches nested DeviceItems for the interface, since the interface sits at a different depth depending on device type. Check resolvedPath in the response to see what was actually used. Errors list every interface found if the given path is ambiguous (multiple interfaces underneath it). Each port's connectedDeviceItemName/connectedPortName reflects the project's own *planned* topology (best-effort - based on an undocumented Openness property) - it can be null/absent even for a physically wired port if nobody configured it in TIA's Topology view; it does not reflect what's actually plugged in on the real line (compare against a PRONETA scan for that).")]
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
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving network interface info for '{path}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CompareNetworkCsv", Title = "Compare PRONETA network CSV", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Compare a PRONETA 'Device Table (CSV)' export (Network Analysis > Online > Export, with 'Include device details' checked - that adds the Port ID/Partner Port/Partner Device columns this reads) against the currently open project's devices. Read-only - reports matches and differences, never writes anything. Matching is by case-insensitive device name only (PROFINET device names are lowercase by spec, so 'PLC_1' project vs 'plc_1' PRONETA is the normal case, not a mismatch) - MAC-based matching is not possible, no MAC-address attribute/property exists anywhere in this Openness version's public API (verified against the shipped XML docs). matchMethod null means no TIA device with that name was found - could be a device not yet in the project, offline at scan time, or renamed. ipDiffers flags a live IP that doesn't match the project's configured IP for a matched device. This tool never calls SetIpAddress itself - a human should confirm a reported mismatch before you call that separately to fix it.")]
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
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error comparing network CSV '{csvPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "SetIpAddress", Title = "Set IP address", Destructive = true, Idempotent = true, OpenWorld = false), Description("Set IP address/subnet mask/router on a device's network interface (same auto-resolving path as GetNetworkInterfaceInfo). IMPORTANT: this only changes the TIA project's saved metadata - nothing reaches real hardware until the project is compiled and downloaded. Changing the address can also regenerate the PROFINET device name hash used to match physical hardware on the wire; if that no longer matches what's actually downloaded to a switch/drive, the online connection to that device breaks. Confirm with the user before calling this against a real (non-disposable) project, and make clear to them that this alone does not reach the PLC/line.")]
        public static ResponseSetIpAddress SetIpAddress(
            [Description("path: a device or device item path, same auto-resolving rules as GetNetworkInterfaceInfo")] string path,
            [Description("ipAddress: e.g. '192.168.0.1'")] string ipAddress,
            [Description("subnetMask: e.g. '255.255.255.0'")] string subnetMask,
            [Description("routerAddress: optional - omit to leave routing unset")] string? routerAddress = null)
        {
            try
            {
                var (iface, resolvedPath) = Portal.SetIpAddress(path, ipAddress, subnetMask, routerAddress);

                return new ResponseSetIpAddress
                {
                    Message = $"IP address set to '{ipAddress}' on '{resolvedPath}' - project metadata only, not applied to real hardware until compiled and downloaded",
                    ResolvedPath = resolvedPath,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                var reason = pex.InnerException?.Message?.Trim();
                var msg = pex.Message;
                if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";
                throw new McpException(msg, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error setting IP address for '{path}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ConnectToSubnet", Title = "Connect to subnet", Destructive = true, Idempotent = true, OpenWorld = false), Description("Connect a device's network interface to a named subnet (must already exist - see GetSubnets). Same auto-resolving path as GetNetworkInterfaceInfo, and same project-metadata-only caveat as SetIpAddress - nothing reaches real hardware until compiled and downloaded. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseConnectToSubnet ConnectToSubnet(
            [Description("path: a device or device item path, same auto-resolving rules as GetNetworkInterfaceInfo")] string path,
            [Description("subnetName: name of an existing subnet, from GetSubnets")] string subnetName)
        {
            try
            {
                var (iface, resolvedPath, resolvedSubnetName) = Portal.ConnectToSubnet(path, subnetName);

                return new ResponseConnectToSubnet
                {
                    Message = $"'{resolvedPath}' connected to subnet '{resolvedSubnetName}' - project metadata only, not applied to real hardware until compiled and downloaded",
                    ResolvedPath = resolvedPath,
                    SubnetName = resolvedSubnetName,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error connecting '{path}' to subnet '{subnetName}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "DisconnectFromSubnet", Title = "Disconnect from subnet", Destructive = true, Idempotent = true, OpenWorld = false), Description("Disconnect a device's network interface from its subnet. Same auto-resolving path as GetNetworkInterfaceInfo. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseDisconnectFromSubnet DisconnectFromSubnet(
            [Description("path: a device or device item path, same auto-resolving rules as GetNetworkInterfaceInfo")] string path)
        {
            try
            {
                var (iface, resolvedPath) = Portal.DisconnectFromSubnet(path);

                return new ResponseDisconnectFromSubnet
                {
                    Message = $"'{resolvedPath}' disconnected from its subnet",
                    ResolvedPath = resolvedPath,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error disconnecting '{path}' from its subnet: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CreateDevice", Title = "Create device", Destructive = false, Idempotent = false, OpenWorld = false), Description("Create a new single-item hardware device (e.g. a simple switch) in the project. Most real device types (drives, CPU stations, HMI panels) are modeled by Siemens as a station+item pair and need CreateDeviceWithItem instead - this fails with 'the object cannot be created, it is of the wrong type' for those even with a correct TypeIdentifier, so try CreateDeviceWithItem if this fails. typeIdentifier MUST come from FindHardwareCatalogEntries (format 'OrderNumber:<article>/<version>') - the TypeIdentifier attribute GetDevices/GetDeviceInfo expose off an already-placed device (format 'System:Device.X') is query-only and is rejected by this API.")]
        public static ResponseCreateDevice CreateDevice(
            [Description("typeIdentifier: a creation-ready catalog TypeIdentifier from FindHardwareCatalogEntries, e.g. 'OrderNumber:6SL3210-1KE11-8AF2/4.7.14' - NOT the TypeIdentifier attribute off an existing device")] string typeIdentifier,
            [Description("name: name for the new device")] string name,
            [Description("groupPath: path to the device group to create it under, e.g. 'Group/Subgroup' (empty for the project root)")] string groupPath = "")
        {
            try
            {
                var device = Portal.CreateDevice(typeIdentifier, name, groupPath);

                return new ResponseCreateDevice
                {
                    Message = $"Device '{device.Name}' created under '{groupPath}'",
                    Name = device.Name,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                var reason = pex.InnerException?.Message?.Trim();
                var msg = pex.Message;
                if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";
                throw new McpException(msg, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error creating device '{name}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CreateDeviceWithItem", Title = "Create device with item", Destructive = false, Idempotent = false, OpenWorld = false), Description("Create a new hardware device that has both a station-level name and a first sub-item name in one call - e.g. a CPU station or a drive (mirrors how existing devices are shaped: Device 'S7-1500/ET200MP station_1' containing DeviceItem 'PLC_1'). Use this instead of CreateDevice for anything that isn't a bare single-item catalog entry - verified live for a CPU 1516-3 PN/DP, which came up with real DP/PROFINET interface DeviceItems, not an empty shell. typeIdentifier MUST come from FindHardwareCatalogEntries (format 'OrderNumber:<article>/<version>') - the TypeIdentifier attribute GetDevices/GetDeviceInfo expose off an already-placed device (format 'System:Device.X') is query-only and is rejected by this API ('the object cannot be created, it is of the wrong type'). Note: the resulting Device.Name mirrors deviceItemName, not name - the 'name' argument does not become the queryable device name in practice.")]
        public static ResponseCreateDevice CreateDeviceWithItem(
            [Description("typeIdentifier: a creation-ready catalog TypeIdentifier from FindHardwareCatalogEntries, e.g. 'OrderNumber:6ES7 516-3AN01-0AB0/V2.1' - NOT the TypeIdentifier attribute off an existing device")] string typeIdentifier,
            [Description("name: name for the new device (station) - note this does not end up as the queryable Device.Name; use deviceItemName for the path you'll query/reference afterward")] string name,
            [Description("deviceItemName: name for the first sub-item (e.g. the CPU module) - this is what the resulting device is actually named/found under")] string deviceItemName,
            [Description("groupPath: path to the device group to create it under, e.g. 'Group/Subgroup' (empty for the project root)")] string groupPath = "")
        {
            try
            {
                var device = Portal.CreateDeviceWithItem(typeIdentifier, name, deviceItemName, groupPath);

                return new ResponseCreateDevice
                {
                    Message = $"Device '{device.Name}' (with item '{deviceItemName}') created under '{groupPath}'",
                    Name = device.Name,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                var reason = pex.InnerException?.Message?.Trim();
                var msg = pex.Message;
                if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";
                throw new McpException(msg, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error creating device '{name}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "DeleteDevice", Title = "Delete device", Destructive = true, Idempotent = true, OpenWorld = false), Description("Delete a hardware device from the project. DRY-RUN BY DEFAULT: without confirm=true, this only reports what device would be deleted and changes nothing - call it once to preview, then again with confirm=true to actually delete. TIA's own undo only survives until the next save, and hardware config is entangled with physical wiring/GSD matching, so recovery is more expensive than a block/tag delete - confirm with the user before ever passing confirm=true against a real (non-disposable) project.")]
        public static ResponseDeleteDevice DeleteDevice(
            [Description("devicePath: full path to the device")] string devicePath,
            [Description("confirm: must be explicitly true to actually delete - false (default) only previews what would be deleted")] bool confirm = false)
        {
            try
            {
                var (executed, deviceName, typeName) = Portal.DeleteDevice(devicePath, confirm);

                return new ResponseDeleteDevice
                {
                    Message = executed
                        ? $"Device '{deviceName}' ({typeName}) deleted"
                        : $"Preview only (confirm=false, nothing changed) - would delete device '{deviceName}' ({typeName})",
                    Executed = executed,
                    DeviceName = deviceName,
                    TypeName = typeName,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error deleting device '{devicePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CreateDeviceGroup", Title = "Create device group", Destructive = false, Idempotent = false, OpenWorld = false), Description("Create a new device group (folder) in the project, for organizing devices.")]
        public static ResponseCreateDeviceGroup CreateDeviceGroup(
            [Description("parentGroupPath: path to the parent group to create the new group under, e.g. 'Group/Subgroup' (empty for the project root)")] string parentGroupPath,
            [Description("name: name for the new group")] string name)
        {
            try
            {
                var group = Portal.CreateDeviceGroup(parentGroupPath, name);

                return new ResponseCreateDeviceGroup
                {
                    Message = $"Device group '{group.Name}' created under '{parentGroupPath}'",
                    Name = group.Name,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error creating device group '{name}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "DeleteDeviceGroup", Title = "Delete device group", Destructive = true, Idempotent = true, OpenWorld = false), Description("Delete a device group (folder) from the project - also deletes every device inside it. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseDeleteDeviceGroup DeleteDeviceGroup(
            [Description("groupPath: full path to the group, e.g. 'Group/Subgroup'")] string groupPath)
        {
            try
            {
                Portal.DeleteDeviceGroup(groupPath);

                return new ResponseDeleteDeviceGroup
                {
                    Message = $"Device group '{groupPath}' deleted",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error deleting device group '{groupPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetOnlineState", Title = "Get online state", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get the engineering station's online connection state (Offline/Connecting/Online/...) for a device or device item. This is just the connection used for diagnostics/monitoring - it never starts, stops, or otherwise commands the PLC.")]
        public static ResponseOnlineState GetOnlineState(
            [Description("path: defines the path in the project structure to the device or device item")] string path)
        {
            try
            {
                var state = Portal.GetOnlineState(path);
                if (state == null)
                {
                    throw new McpException($"Device or device item not found, or has no online connection, at '{path}'");
                }

                return new ResponseOnlineState
                {
                    Message = $"Online state retrieved for '{path}'",
                    State = state.ToString(),
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving online state for '{path}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GoOnline", Title = "Go online", Destructive = false, Idempotent = true, OpenWorld = false), Description("Establish the engineering station's online connection to a device or device item for diagnostics/monitoring. Does not start, stop, or otherwise command the PLC - use TIA Portal itself for Run/Stop control.")]
        public static ResponseOnlineState GoOnline(
            [Description("path: defines the path in the project structure to the device or device item")] string path)
        {
            try
            {
                var state = Portal.GoOnline(path);

                return new ResponseOnlineState
                {
                    Message = $"Went online with '{path}'",
                    State = state.ToString(),
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error going online with '{path}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GoOffline", Title = "Go offline", Destructive = false, Idempotent = true, OpenWorld = false), Description("Disconnect the engineering station's online connection to a device or device item. Does not stop the PLC - the PLC keeps running regardless of this connection. Caution: if a human has TIA Portal's own window open, they see this disconnect happen live with no warning - don't call this to unblock an export that fails due to online mode without telling the user first, since they may be relying on that connection (e.g. watching live values).")]
        public static ResponseGoOffline GoOffline(
            [Description("path: defines the path in the project structure to the device or device item")] string path)
        {
            try
            {
                Portal.GoOffline(path);

                return new ResponseGoOffline
                {
                    Message = $"Went offline from '{path}'",
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error going offline from '{path}': {ex.Message}", ex);
            }
        }

        #endregion

        #region plc software

        [McpServerTool(Name = "GetSoftwareInfo", Title = "Get PLC software info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get plc software info")]
        public static ResponseSoftwareInfo GetSoftwareInfo(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath)
        {
            try
            {
                var software = Portal.GetPlcSoftware(softwarePath);
                if (software != null)
                {

                    var attributes = Helper.GetAttributeList(software);

                    return new ResponseSoftwareInfo
                    {
                        Message = $"Software info retrieved from '{softwarePath}'",
                        Name = software.Name,
                        Attributes = attributes,
                        Description = software.ToString(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Software not found at '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving software info from '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CompileSoftware", Title = "Compile PLC software", Destructive = false, Idempotent = true, OpenWorld = false), Description("Compile the plc software")]
        public static ResponseCompileSoftware CompileSoftware(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("password: the password to access adminsitration, default: no password")] string password = "")
        {
            try
            {
                var result = Portal.CompileSoftware(softwarePath, password);
                if (result != null && !result.State.ToString().Equals("Error"))
                {
                    return new ResponseCompileSoftware
                    {
                        Message = $"Software '{softwarePath}' compiled with {result}",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed compiling software '{softwarePath}': {result}");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error compiling software '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetSoftwareTree", Title = "Get PLC software tree", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get the structure/tree of a given PLC software showing blocks, types, and external sources")]
        public static ResponseSoftwareTree GetSoftwareTree(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath)
        {
            try
            {
                var tree = Portal.GetSoftwareTree(softwarePath);

                if (!string.IsNullOrEmpty(tree))
                {
                    return new ResponseSoftwareTree
                    {
                        Message = $"Software tree retrieved from '{softwarePath}'",
                        Tree = "```\n" + tree + "\n```",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed retrieving software tree from '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving software tree from '{softwarePath}': {ex.Message}", ex);
            }
        }

        #endregion

        #region blocks

        [McpServerTool(Name = "GetBlockInfo", Title = "Get block info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get a block info, which is located in the plc software")]
        public static ResponseBlockInfo GetBlockInfo(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("blockPath: defines the path in the project structure to the block")] string blockPath)
        {
            try
            {
                var block = Portal.GetBlock(softwarePath, blockPath);
                if (block != null)
                {
                    var attributes = Helper.GetAttributeList(block);

                    return new ResponseBlockInfo
                    {
                        Message = $"Block info retrieved from '{blockPath}' in '{softwarePath}'",
                        Name = block.Name,
                        TypeName = block.GetType().Name,
                        Namespace = block.Namespace,
                        ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage),block.ProgrammingLanguage),
                        MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                        IsConsistent = block.IsConsistent,
                        HeaderName = block.HeaderName,
                        ModifiedDate = block.ModifiedDate,
                        IsKnowHowProtected = block.IsKnowHowProtected,
                        Attributes = attributes,
                        Description = block.ToString(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Block not found at '{blockPath}' in '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving block info from '{blockPath}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetBlocks", Title = "Get blocks", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get a list of blocks, which are located in plc software")]
        public static ResponseBlocks GetBlocks(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the block. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetBlocks(softwarePath, regexName);

                var responseList = new List<ResponseBlockInfo>();
                foreach (var block in list)
                {
                    if (block != null)
                    {
                        var attributes = Helper.GetAttributeList(block);

                        responseList.Add(new ResponseBlockInfo
                        {
                            Name = block.Name,
                            TypeName = block.GetType().Name,
                            Namespace = block.Namespace,
                            ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), block.ProgrammingLanguage),
                            MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                            IsConsistent = block.IsConsistent,
                            HeaderName = block.HeaderName,
                            ModifiedDate = block.ModifiedDate,
                            IsKnowHowProtected = block.IsKnowHowProtected,
                            Attributes = attributes,
                            Description = block.ToString()
                        });
                    }
                }

                if (list != null)
                {
                    return new ResponseBlocks
                    {
                        Message = $"Blocks with regex '{regexName}' retrieved from '{softwarePath}'",
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
                    throw new McpException($"Failed retrieving blocks with regex '{regexName}' in '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving blocks with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetBlocksWithHierarchy", Title = "Get blocks with hierarchy", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get a list of all blocks with their group hierarchy from the plc software.")]
        public static ResponseBlocksWithHierarchy GetBlocksWithHierarchy(
        [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath)
        {
            try
            {
                var rootGroup = Portal.GetBlockRootGroup(softwarePath);
                if (rootGroup != null)
                {
                    var hierarchy = Helper.BuildBlockHierarchy(rootGroup);
                    return new ResponseBlocksWithHierarchy
                    {
                        Message = $"Block hierarchy retrieved from '{softwarePath}'",
                        Root = hierarchy,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    // Specific failure: root group could not be resolved
                    throw new McpException($"Block root group not found for '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                // Generic unexpected failure wrapper
                throw new McpException($"Unexpected error retrieving block hierarchy for '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetTypeCrossReferences", Title = "Get type cross references", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Find every place a PLC data type (UDT) is used across the project. For an FB used as an instance type, use GetBlockCrossReferences instead.")]
        public static ResponseCrossReferences GetTypeCrossReferences(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("typePath: defines the path in the project structure to the type")] string typePath)
        {
            try
            {
                var result = Portal.GetTypeCrossReferences(softwarePath, typePath);
                if (result == null)
                {
                    throw new McpException($"Type not found, or has no cross-reference service, at '{typePath}' in '{softwarePath}'");
                }

                var typeLeafName = typePath.Contains('/') ? typePath.Substring(typePath.LastIndexOf('/') + 1) : typePath;
                var sources = Helper.BuildCrossReferenceSourceList(result.Sources, typeLeafName);

                return new ResponseCrossReferences
                {
                    Message = $"Cross references retrieved for '{typePath}' in '{softwarePath}'",
                    Sources = sources,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving cross references for '{typePath}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetBlockCrossReferences", Title = "Get block cross references", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Find every place a block is used across the project - e.g. an FB used as an instance type (how many/which blocks declare a Static instance of it), or an FC/OB and who calls it")]
        public static ResponseCrossReferences GetBlockCrossReferences(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("blockPath: defines the path in the project structure to the block")] string blockPath)
        {
            try
            {
                var result = Portal.GetBlockCrossReferences(softwarePath, blockPath);
                if (result == null)
                {
                    throw new McpException($"Block not found, or has no cross-reference service, at '{blockPath}' in '{softwarePath}'");
                }

                var blockLeafName = blockPath.Contains('/') ? blockPath.Substring(blockPath.LastIndexOf('/') + 1) : blockPath;
                var sources = Helper.BuildCrossReferenceSourceList(result.Sources, blockLeafName);

                return new ResponseCrossReferences
                {
                    Message = $"Cross references retrieved for '{blockPath}' in '{softwarePath}'",
                    Sources = sources,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving cross references for '{blockPath}' in '{softwarePath}': {ex.Message}", ex);
            }
        }


        [McpServerTool(Name = "ExportBlock", Title = "Export block to XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export a block from plc software to file. Requires the project to be offline - fails if it's online/monitoring. If it fails for that reason, tell the user rather than calling GoOffline yourself; going offline disconnects TIA Portal's live view for anyone watching it, with no warning.")]
        public static ResponseExportBlock ExportBlock(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("blockPath: full path to the block in the project structure, e.g. 'Group/Subgroup/Name' (single names are ambiguous)")] string blockPath,
            [Description("exportPath: relative subfolder name (or omit) under the server-managed export folder (see Doctor's exportRoot) - NOT a full/absolute path")] string exportPath,
            [Description("preservePath: preserves the path/structure of the plc software")] bool preservePath = false)
        {
            try
            {
                var block = Portal.ExportBlock(softwarePath, blockPath, exportPath, preservePath);
                if (block != null)
                {
                    return new ResponseExportBlock
                    {
                        Message = $"Block exported from '{blockPath}' to '{exportPath}'",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                // Should not be reachable because Portal.ExportBlock throws on failure
                throw new McpException($"Failed exporting block from '{blockPath}' to '{exportPath}'");
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                // Map known portal errors to sharper MCP errors and messages.
                switch (pex.Code)
                {
                    case TiaMcpServer.Siemens.PortalErrorCode.NotFound:
                        {
                            var suggestionNote = string.Empty;
                            // If the path has no '/', it may be incomplete; build suggestions using Portal's regex search and path resolver
                            if (!string.IsNullOrEmpty(blockPath) && !blockPath.Contains('/'))
                            {
                                try
                                {
                                    var escaped = Regex.Escape(blockPath);
                                    var blocks = Portal.GetBlocks(softwarePath, $"^{escaped}$");
                                    if (blocks == null || blocks.Count == 0)
                                    {
                                        blocks = Portal.GetBlocks(softwarePath, escaped);
                                    }

                                    var candidates = blocks
                                        .Take(10)
                                        .Select(b => Portal.GetBlockPath(b))
                                        .Where(p => !string.IsNullOrWhiteSpace(p))
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .ToList();

                                    if (candidates.Count > 0)
                                    {
                                        suggestionNote = $" Did you mean: {string.Join(", ", candidates)}?";
                                    }
                                }
                                catch
                                {
                                    // Best-effort suggestions only
                                }
                            }

                            var msg = $"Block not found.{suggestionNote}".Trim();
                            throw new McpException(msg);
                        }

                    case TiaMcpServer.Siemens.PortalErrorCode.ExportFailed:
                        {
                            // Relay underlying portal error with concise reason; log full details
                            var reason = pex.InnerException?.Message?.Trim();
                            var msg = "Failed to export block.";
                            if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";

                            Logger?.LogError(pex, "MCP ExportBlock failed for {SoftwarePath} {BlockPath} -> {ExportPath}",
                                pex.Data?["softwarePath"], pex.Data?["blockPath"], pex.Data?["exportPath"]);

                            throw new McpException(msg);
                        }

                    case TiaMcpServer.Siemens.PortalErrorCode.InvalidParams:
                    case TiaMcpServer.Siemens.PortalErrorCode.InvalidState:
                        {
                            throw new McpException(pex.Message);
                        }
                }

                // Fallback
                throw new McpException(pex.Message);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting block from '{blockPath}' to '{exportPath}': {ex.Message}", ex);
            }
        }

        private static string BuildBlockPathSuggestion(string softwarePath, string blockPath)
        {
            if (string.IsNullOrEmpty(blockPath) || blockPath.Contains('/')) return string.Empty;
            try
            {
                var escaped = Regex.Escape(blockPath);
                var blocks = Portal.GetBlocks(softwarePath, $"^{escaped}$");
                if (blocks == null || blocks.Count == 0)
                {
                    blocks = Portal.GetBlocks(softwarePath, escaped);
                }

                var candidates = blocks
                    .Take(10)
                    .Select(b =>
                    {
                        var name = b.Name;
                        var parts = new List<string> { name };
                        var parent = b.Parent;
                        while (parent != null)
                        {
                            if (parent is PlcBlockSystemGroup) break;
                            if (parent is PlcBlockGroup grp)
                            {
                                parts.Insert(0, grp.Name);
                                parent = grp.Parent;
                            }
                            else break;
                        }
                        if (parts.Count > 1) parts.RemoveAt(0);
                        return string.Join("/", parts);
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return candidates.Count > 0 ? $" Did you mean: {string.Join(", ", candidates)}?" : string.Empty;
            }
            catch
            {
                return string.Empty; // best effort only
            }
        }
        [McpServerTool(Name = "ImportBlock", Title = "Import block from XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Import a block file to plc software")]
        public static ResponseImportBlock ImportBlock(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("groupPath: defines the path in the project structure to the group, where to import the block")] string groupPath,
            [Description("importPath: defines the path of the xml file from where to import the block")] string importPath)
        {
            try
            {
                if (Portal.ImportBlock(softwarePath, groupPath, importPath))
                {
                    return new ResponseImportBlock
                    {
                        Message = $"Block imported from '{importPath}' to '{groupPath}'",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed importing block from '{importPath}' to '{groupPath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing block from '{importPath}' to '{groupPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ExportBlocks", Title = "Export blocks to XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export all blocks from the plc software to path. Same offline-mode requirement as ExportBlock - if it fails because the project is online, tell the user instead of calling GoOffline yourself.")]
        public static async Task<ResponseExportBlocks> ExportBlocks(
            IProgress<ProgressNotificationValue> progress,
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("exportPath: relative subfolder name (or omit) under the server-managed export folder (see Doctor's exportRoot) - NOT a full/absolute path")] string exportPath,
            [Description("regexName: defines the name or regular expression to find the block. Use empty string (default) to find all")] string regexName = "",
            [Description("preservePath: preserves the path/structure of the plc software")] bool preservePath = false)
        {
            var startTime = DateTime.Now;
            
            try
            {
                // First, get the list of blocks to determine total count
                Logger?.LogInformation($"Starting export of blocks from '{softwarePath}' to '{exportPath}'");
                
                var allBlocks = await Task.Run(() => Portal.GetBlocks(softwarePath, regexName));
                var totalBlocks = allBlocks?.Count ?? 0;

                if (totalBlocks == 0)
                {
                    progress.Report(new ProgressNotificationValue { Progress = 0, Total = 0, Message = "No blocks found to export" });
                    
                    return new ResponseExportBlocks
                    {
                        Message = $"No blocks found with regex '{regexName}' in '{softwarePath}'",
                        Items = new List<ResponseBlockInfo>(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true,
                            ["totalBlocks"] = 0,
                            ["exportedBlocks"] = 0,
                            ["duration"] = (DateTime.Now - startTime).TotalSeconds
                        }
                    };
                }

                // Send initial progress notification
                progress.Report(new ProgressNotificationValue { Progress = 0, Total = totalBlocks, Message = $"Starting export of {totalBlocks} blocks..." });

                // Export blocks asynchronously
                var exportedBlocks = await Task.Run(() => Portal.ExportBlocks(softwarePath, exportPath, regexName, preservePath));

                // Build list of inconsistent (skipped) blocks for reporting
                var inconsistentInfos = new List<ResponseBlockInfo>();
                if (allBlocks != null)
                {
                    foreach (var b in allBlocks)
                    {
                        if (b != null && b.IsConsistent == false)
                        {
                            var attrs = Helper.GetAttributeList(b);
                            inconsistentInfos.Add(new ResponseBlockInfo
                            {
                                Name = b.Name,
                                TypeName = b.GetType().Name,
                                Namespace = b.Namespace,
                                ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), b.ProgrammingLanguage),
                                MemoryLayout = Enum.GetName(typeof(MemoryLayout), b.MemoryLayout),
                                IsConsistent = b.IsConsistent,
                                HeaderName = b.HeaderName,
                                ModifiedDate = b.ModifiedDate,
                                IsKnowHowProtected = b.IsKnowHowProtected,
                                Attributes = attrs,
                                Description = b.ToString()
                            });
                        }
                    }
                }
                
                // Send progress update after export completion
                if (exportedBlocks != null)
                {
                    var exportedCount = exportedBlocks.Count();
                    progress.Report(new ProgressNotificationValue { Progress = exportedCount, Total = totalBlocks, Message = $"Exported {exportedCount} of {totalBlocks} blocks" });
                }

                if (exportedBlocks != null)
                {
                    var responseList = new List<ResponseBlockInfo>();
                    var processedCount = 0;
                    
                    foreach (var block in exportedBlocks)
                    {
                        if (block != null)
                        {
                            var attributes = Helper.GetAttributeList(block);

                            responseList.Add(new ResponseBlockInfo
                            {
                                Name = block.Name,
                                TypeName = block.GetType().Name,
                                Namespace = block.Namespace,
                                ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), block.ProgrammingLanguage),
                                MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                                IsConsistent = block.IsConsistent,
                                HeaderName = block.HeaderName,
                                ModifiedDate = block.ModifiedDate,
                                IsKnowHowProtected = block.IsKnowHowProtected,
                                Attributes = attributes,
                                Description = block.ToString()
                            });
                        }
                        processedCount++;
                    }

                    // Send final progress notification
                    progress.Report(new ProgressNotificationValue { Progress = processedCount, Total = totalBlocks, Message = $"Export completed: {processedCount} blocks exported successfully" });

                    var duration = (DateTime.Now - startTime).TotalSeconds;
                    Logger?.LogInformation($"Export completed: {processedCount} blocks exported in {duration:F2} seconds");

                    return new ResponseExportBlocks
                    {
                        Message = $"Export completed: {processedCount} blocks with regex '{regexName}' exported from '{softwarePath}' to '{exportPath}'",
                        Items = responseList,
                        Inconsistent = inconsistentInfos,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true,
                            ["totalBlocks"] = totalBlocks,
                            ["exportedBlocks"] = processedCount,
                            ["inconsistentBlocks"] = inconsistentInfos.Count,
                            ["duration"] = duration
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed exporting blocks with '{regexName}' from '{softwarePath}' to {exportPath}");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                // Send error progress notification if we have a progress token
                progress.Report(new ProgressNotificationValue { Progress = 0, Total = 0, Message = $"Export failed: {ex.Message}" });
                
                Logger?.LogError(ex, $"Failed exporting blocks with '{regexName}' from '{softwarePath}' to {exportPath}");
                throw new McpException($"Unexpected error exporting blocks with '{regexName}' from '{softwarePath}' to {exportPath}: {ex.Message}", ex);
            }
        }

        #endregion

        #region types

        [McpServerTool(Name = "GetTypeInfo", Title = "Get type info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get a type info from the plc software")]
        public static ResponseTypeInfo GetTypeInfo(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("typePath: defines the path in the project structure to the type")] string typePath)
        {
            try
            {
                var type = Portal.GetType(softwarePath, typePath);
                if (type != null)
                {
                    var attributes = Helper.GetAttributeList(type);

                    return new ResponseTypeInfo
                    {
                        Message = $"Type info retrieved from '{typePath}' in '{softwarePath}'",
                        Name = type.Name,
                        TypeName = type.GetType().Name,
                        Namespace = type.Namespace,
                        IsConsistent = type.IsConsistent,
                        ModifiedDate = type.ModifiedDate,
                        IsKnowHowProtected = type.IsKnowHowProtected,
                        Attributes = attributes,
                        Description = type.ToString(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Type not found at '{typePath}' in '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving type info from '{typePath}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetTypes", Title = "Get types", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get a list of types from the plc software")]
        public static ResponseTypes GetTypes(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the block. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetTypes(softwarePath, regexName);

                var responseList = new List<ResponseTypeInfo>();
                foreach (var type in list)
                {
                    if (type != null)
                    {
                        var attributes = Helper.GetAttributeList(type);

                        responseList.Add(new ResponseTypeInfo
                        {
                            Name = type.Name,
                            TypeName = type.GetType().Name,
                            Namespace = type.Namespace,
                            IsConsistent = type.IsConsistent,
                            ModifiedDate = type.ModifiedDate,
                            IsKnowHowProtected = type.IsKnowHowProtected,
                            Attributes = attributes,
                            Description = type.ToString()
                        });
                    }
                }

                if (list != null)
                {
                    return new ResponseTypes
                    {
                        Message = $"Types with regex '{regexName}' retrieved from '{softwarePath}'",
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
                    throw new McpException($"Failed retrieving user defined types with regex '{regexName}' in '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving user defined types with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ExportType", Title = "Export type to XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export a type from the plc software. Same offline-mode requirement as ExportBlock - if it fails because the project is online, tell the user instead of calling GoOffline yourself.")]
        public static ResponseExportType ExportType(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("exportPath: relative subfolder name (or omit) under the server-managed export folder (see Doctor's exportRoot) - NOT a full/absolute path")] string exportPath,
            [Description("typePath: defines the path in the project structure to the type")] string typePath,
            [Description("preservePath: preserves the path/structure of the plc software")] bool preservePath = false)
        {
            try
            {
                var type = Portal.ExportType(softwarePath, typePath, exportPath, preservePath);
                if (type != null)
                {
                    return new ResponseExportType
                    {
                        Message = $"Type exported from '{typePath}' to '{exportPath}'",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed exporting type from '{typePath}' to '{exportPath}'");
                }
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                switch (pex.Code)
                {
                    case TiaMcpServer.Siemens.PortalErrorCode.NotFound:
                        throw new McpException("Type not found.");
                    case TiaMcpServer.Siemens.PortalErrorCode.InvalidState:
                    case TiaMcpServer.Siemens.PortalErrorCode.InvalidParams:
                        throw new McpException(pex.Message);
                    case TiaMcpServer.Siemens.PortalErrorCode.ExportFailed:
                        {
                            var reason = pex.InnerException?.Message?.Trim();
                            var msg = "Failed to export type.";
                            if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";
                            Logger?.LogError(pex, "MCP ExportType failed for {SoftwarePath} {TypePath} -> {ExportPath}",
                                pex.Data?["softwarePath"], pex.Data?["typePath"], pex.Data?["exportPath"]);
                            throw new McpException(msg);
                        }
                }
                throw new McpException(pex.Message);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting type from '{typePath}' to '{exportPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ImportType", Title = "Import type from XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Import a type from file into the plc software")]
        public static ResponseImportType ImportType(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("groupPath: defines the path in the project structure to the group, where to import the type")] string groupPath,
            [Description("importPath: defines the path of the xml file from where to import the type")] string importPath)
        {
            try
            {
                if (Portal.ImportType(softwarePath, groupPath, importPath))
                {
                    return new ResponseImportType
                    {
                        Message = $"Type imported from '{importPath}' to '{groupPath}'",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed importing type from '{importPath}' to '{groupPath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing type from '{importPath}' to '{groupPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ExportTypes", Title = "Export types to XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export types from the plc software to path. Same offline-mode requirement as ExportBlock - if it fails because the project is online, tell the user instead of calling GoOffline yourself.")]
        public static async Task<ResponseExportTypes> ExportTypes(
            IProgress<ProgressNotificationValue> progress,
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("exportPath: relative subfolder name (or omit) under the server-managed export folder (see Doctor's exportRoot) - NOT a full/absolute path")] string exportPath,
            [Description("regexName: defines the name or regular expression to find the block. Use empty string (default) to find all")] string regexName = "",
            [Description("preservePath: preserves the path/structure of the plc software")] bool preservePath = false)
        {
            var startTime = DateTime.Now;
            
            try
            {
                // First, get the list of types to determine total count
                Logger?.LogInformation($"Starting export of types from '{softwarePath}' to '{exportPath}'");
                
                var allTypes = await Task.Run(() => Portal.GetTypes(softwarePath, regexName));
                var totalTypes = allTypes?.Count ?? 0;

                if (totalTypes == 0)
                {
                    progress.Report(new ProgressNotificationValue { Progress = 0, Total = 0, Message = "No types found to export" });
                    
                    return new ResponseExportTypes
                    {
                        Message = $"No types found with regex '{regexName}' in '{softwarePath}'",
                        Items = new List<ResponseTypeInfo>(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true,
                            ["totalTypes"] = 0,
                            ["exportedTypes"] = 0,
                            ["duration"] = (DateTime.Now - startTime).TotalSeconds
                        }
                    };
                }

                // Send initial progress notification
                progress.Report(new ProgressNotificationValue { Progress = 0, Total = totalTypes, Message = $"Starting export of {totalTypes} types..." });

                // Export types asynchronously
                var exportedTypes = await Task.Run(() => Portal.ExportTypes(softwarePath, exportPath, regexName, preservePath));

                // Build list of inconsistent (skipped) types for reporting
                var inconsistentTypeInfos = new List<ResponseTypeInfo>();
                if (allTypes != null)
                {
                    foreach (var t in allTypes)
                    {
                        if (t != null && t.IsConsistent == false)
                        {
                            var attrs = Helper.GetAttributeList(t);
                            inconsistentTypeInfos.Add(new ResponseTypeInfo
                            {
                                Name = t.Name,
                                TypeName = t.GetType().Name,
                                Namespace = t.Namespace,
                                IsConsistent = t.IsConsistent,
                                ModifiedDate = t.ModifiedDate,
                                IsKnowHowProtected = t.IsKnowHowProtected,
                                Attributes = attrs,
                                Description = t.ToString()
                            });
                        }
                    }
                }
                
                // Send progress update after export completion
                if (exportedTypes != null)
                {
                    var exportedCount = exportedTypes.Count();
                    progress.Report(new ProgressNotificationValue { Progress = exportedCount, Total = totalTypes, Message = $"Exported {exportedCount} of {totalTypes} types" });
                }

                if (exportedTypes != null)
                {
                    var responseList = new List<ResponseTypeInfo>();
                    var processedCount = 0;
                    
                    foreach (var type in exportedTypes)
                    {
                        if (type != null)
                        {
                            var attributes = Helper.GetAttributeList(type);

                            responseList.Add(new ResponseTypeInfo
                            {
                                Name = type.Name,
                                TypeName = type.GetType().Name,
                                Namespace = type.Namespace,
                                IsConsistent = type.IsConsistent,
                                ModifiedDate = type.ModifiedDate,
                                IsKnowHowProtected = type.IsKnowHowProtected,
                                Attributes = attributes,
                                Description = type.ToString()
                            });
                        }
                        processedCount++;
                    }

                    // Send final progress notification
                    progress.Report(new ProgressNotificationValue { Progress = processedCount, Total = totalTypes, Message = $"Export completed: {processedCount} types exported successfully" });

                    var duration = (DateTime.Now - startTime).TotalSeconds;
                    Logger?.LogInformation($"Type export completed: {processedCount} types exported in {duration:F2} seconds");

                    return new ResponseExportTypes
                    {
                        Message = $"Export completed: {processedCount} types with regex '{regexName}' exported from '{softwarePath}' to '{exportPath}'",
                        Items = responseList,
                        Inconsistent = inconsistentTypeInfos,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true,
                            ["totalTypes"] = totalTypes,
                            ["exportedTypes"] = processedCount,
                            ["inconsistentTypes"] = inconsistentTypeInfos.Count,
                            ["duration"] = duration
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed exporting types '{regexName}' from '{softwarePath}' to {exportPath}");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                // Send error progress notification if we have a progress token
                progress.Report(new ProgressNotificationValue { Progress = 0, Total = 0, Message = $"Type export failed: {ex.Message}" });
                
                Logger?.LogError(ex, $"Failed exporting types '{regexName}' from '{softwarePath}' to {exportPath}");
                throw new McpException($"Unexpected error exporting types '{regexName}' from '{softwarePath}' to {exportPath}: {ex.Message}", ex);
            }
        }

        #endregion

        #region documents

        [McpServerTool(Name = "ExportAsDocuments", Title = "Export block as documents", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export as documents (.s7dcl/.s7res) from a block in the plc software to path")]
        public static ResponseExportAsDocuments ExportAsDocuments(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("blockPath: defines the path in the project structure to the block")] string blockPath,
            [Description("exportPath: relative subfolder name (or omit) under the server-managed export folder (see Doctor's exportRoot) - NOT a full/absolute path")] string exportPath,
            [Description("preservePath: preserves the path/structure of the plc software")] bool preservePath = false)
        {
            try
            {
                if (Engineering.TiaMajorVersion < 20)
                {
                    throw new McpException("ExportAsDocuments requires TIA Portal V20 or newer");
                }
                if (Portal.ExportAsDocuments(softwarePath, blockPath, exportPath, preservePath))
                {
                    return new ResponseExportAsDocuments
                    {
                        Message = $"Documents exported from '{blockPath}' to '{exportPath}'",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed exporting documents from '{blockPath}' to '{exportPath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting documents from '{blockPath}' to '{exportPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ExportBlocksAsDocuments", Title = "Export blocks as documents", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export as documents (.s7dcl/.s7res) from blocks in the plc software to path")]
        public static async Task<ResponseExportBlocksAsDocuments> ExportBlocksAsDocuments(
            IProgress<ProgressNotificationValue> progress,
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("exportPath: relative subfolder name (or omit) under the server-managed export folder (see Doctor's exportRoot) - NOT a full/absolute path")] string exportPath,
            [Description("regexName: defines the name or regular expression to find the block. Use empty string (default) to find all")] string regexName = "",
            [Description("preservePath: preserves the path/structure of the plc software")] bool preservePath = false)
        {
            var startTime = DateTime.Now;
            
            try
            {
                if (Engineering.TiaMajorVersion < 20)
                {
                    throw new McpException("ExportBlocksAsDocuments requires TIA Portal V20 or newer");
                }
                // First, get the list of blocks to determine total count
                Logger?.LogInformation($"Starting export of blocks as documents from '{softwarePath}' to '{exportPath}'");
                
                var allBlocks = await Task.Run(() => Portal.GetBlocks(softwarePath, regexName));
                var totalBlocks = allBlocks?.Count ?? 0;

                if (totalBlocks == 0)
                {
                    progress.Report(new ProgressNotificationValue { Progress = 0, Total = 0, Message = "No blocks found to export as documents" });
                    
                    return new ResponseExportBlocksAsDocuments
                    {
                        Message = $"No blocks found with regex '{regexName}' in '{softwarePath}'",
                        Items = new List<ResponseBlockInfo>(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true,
                            ["totalBlocks"] = 0,
                            ["exportedBlocks"] = 0,
                            ["duration"] = (DateTime.Now - startTime).TotalSeconds
                        }
                    };
                }

                // Send initial progress notification
                progress.Report(new ProgressNotificationValue { Progress = 0, Total = totalBlocks, Message = $"Starting export of {totalBlocks} blocks as documents..." });

                // Export blocks as documents asynchronously
                var exportedBlocks = await Task.Run(() => Portal.ExportBlocksAsDocuments(softwarePath, exportPath, regexName, preservePath));
                
                // Send progress update after export completion
                if (exportedBlocks != null)
                {
                    var exportedCount = exportedBlocks.Count();
                    progress.Report(new ProgressNotificationValue { Progress = exportedCount, Total = totalBlocks, Message = $"Exported {exportedCount} of {totalBlocks} blocks as documents" });
                }

                if (exportedBlocks != null)
                {
                    var responseList = new List<ResponseBlockInfo>();
                    var processedCount = 0;
                    
                    foreach (var block in exportedBlocks)
                    {
                        if (block != null)
                        {
                            var attributes = Helper.GetAttributeList(block);

                            responseList.Add(new ResponseBlockInfo
                            {
                                Name = block.Name,
                                TypeName = block.GetType().Name,
                                Namespace = block.Namespace,
                                ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), block.ProgrammingLanguage),
                                MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                                IsConsistent = block.IsConsistent,
                                HeaderName = block.HeaderName,
                                ModifiedDate = block.ModifiedDate,
                                IsKnowHowProtected = block.IsKnowHowProtected,
                                Attributes = attributes,
                                Description = block.ToString()
                            });
                        }
                        processedCount++;
                    }

                    // Send final progress notification
                    progress.Report(new ProgressNotificationValue { Progress = processedCount, Total = totalBlocks, Message = $"Document export completed: {processedCount} blocks exported successfully" });

                    var duration = (DateTime.Now - startTime).TotalSeconds;
                    Logger?.LogInformation($"Document export completed: {processedCount} blocks exported in {duration:F2} seconds");

                    return new ResponseExportBlocksAsDocuments
                    {
                        Message = $"Document export completed: {processedCount} blocks with regex '{regexName}' exported from '{softwarePath}' to '{exportPath}'",
                        Items = responseList,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true,
                            ["totalBlocks"] = totalBlocks,
                            ["exportedBlocks"] = processedCount,
                            ["duration"] = duration
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed exporting documents to '{exportPath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                // Send error progress notification if we have a progress token
                progress.Report(new ProgressNotificationValue { Progress = 0, Total = 0, Message = $"Document export failed: {ex.Message}" });
                
                Logger?.LogError(ex, $"Failed exporting documents to '{exportPath}'");
                throw new McpException($"Unexpected error exporting documents to '{exportPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ImportFromDocuments", Title = "Import block from documents", Destructive = true, Idempotent = true, OpenWorld = false), Description("Import program block from SIMATIC SD documents (.s7dcl/.s7res) into PLC software (V20+)")]
        public static ResponseImportFromDocuments ImportFromDocuments(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("groupPath: optional path within the PLC program where the block should be placed (empty for root)")] string groupPath,
            [Description("importPath: directory containing the document files (.s7dcl/.s7res)")] string importPath,
            [Description("fileNameWithoutExtension: name of the block file without extension") ] string fileNameWithoutExtension,
            [Description("importOption: ImportDocumentOptions value (None, Override, SkipInactiveCultures, ActivateInactiveCultures)")] string importOption = "Override")
        {
            try
            {
                if (Engineering.TiaMajorVersion < 20)
                {
                    throw new McpException("ImportFromDocuments requires TIA Portal V20 or newer");
                }

                var option = ParseImportDocumentOption(importOption);

                // Pre-check .s7res for missing en-US tags
                var warnings = new JsonArray();
                try
                {
                    var missingIds = GetResMissingEnUsIds(importPath, fileNameWithoutExtension);
                    if (missingIds != null && missingIds.Count > 0)
                    {
                        Logger?.LogWarning($".s7res for '{fileNameWithoutExtension}' missing en-US tags for {missingIds.Count} items: {string.Join(", ", missingIds)}");
                        warnings.Add(new JsonObject
                        {
                            ["name"] = fileNameWithoutExtension,
                            ["missingEnUsIds"] = new JsonArray(missingIds.Select(id => (JsonNode)id).ToArray())
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogDebug(ex, "Failed to evaluate .s7res warnings");
                }

                var ok = Portal.ImportFromDocuments(softwarePath, groupPath, importPath, fileNameWithoutExtension, option);
                if (ok)
                {
                    return new ResponseImportFromDocuments
                    {
                        Message = $"Imported '{fileNameWithoutExtension}' from '{importPath}'",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true,
                            ["warnings"] = warnings
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed importing '{fileNameWithoutExtension}' from '{importPath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing from documents: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ImportBlocksFromDocuments", Title = "Import blocks from documents", Destructive = true, Idempotent = true, OpenWorld = false), Description("Import program blocks from SIMATIC SD documents (.s7dcl/.s7res) into PLC software (V20+)")]
        public static async Task<ResponseImportBlocksFromDocuments> ImportBlocksFromDocuments(
            IProgress<ProgressNotificationValue> progress,
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("groupPath: optional path within the PLC program where the blocks should be placed (empty for root)")] string groupPath,
            [Description("importPath: directory containing the document files (.s7dcl/.s7res)")] string importPath,
            [Description("regexName: name or regular expression to select block files (empty for all)")] string regexName = "",
            [Description("importOption: ImportDocumentOptions value (None, Override, SkipInactiveCultures, ActivateInactiveCultures)")] string importOption = "Override")
        {
            var startTime = DateTime.Now;

            try
            {
                if (Engineering.TiaMajorVersion < 20)
                {
                    throw new McpException("ImportBlocksFromDocuments requires TIA Portal V20 or newer");
                }

                // Determine total by scanning .s7dcl files matching regex
                int total = 0;
                var scanWarnings = new JsonArray();
                try
                {
                    if (Directory.Exists(importPath))
                    {
                        var rx = string.IsNullOrWhiteSpace(regexName) ? null : new Regex(regexName, RegexOptions.Compiled);
                        var files = Directory.GetFiles(importPath, "*.s7dcl", SearchOption.TopDirectoryOnly);
                        foreach (var f in files)
                        {
                            var name = Path.GetFileNameWithoutExtension(f);
                            if (rx != null && !rx.IsMatch(name))
                                continue;
                            total++;

                            try
                            {
                                var missingIds = GetResMissingEnUsIds(importPath, name);
                                if (missingIds != null && missingIds.Count > 0)
                                {
                                    scanWarnings.Add(new JsonObject
                                    {
                                        ["name"] = name,
                                        ["missingEnUsIds"] = new JsonArray(missingIds.Select(id => (JsonNode)id).ToArray())
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { /* ignore pre-scan errors */ }

                progress.Report(new ProgressNotificationValue { Progress = 0, Total = total, Message = total > 0 ? $"Starting import of {total} blocks from documents..." : "Scanning import directory..." });

                var option = ParseImportDocumentOption(importOption);
                var imported = await Task.Run(() => Portal.ImportBlocksFromDocuments(softwarePath, groupPath, importPath, regexName, option));

                var responseList = new List<ResponseBlockInfo>();
                int processed = 0;
                if (imported != null)
                {
                    foreach (var block in imported)
                    {
                        if (block != null)
                        {
                            var attributes = Helper.GetAttributeList(block);
                            responseList.Add(new ResponseBlockInfo
                            {
                                Name = block.Name,
                                TypeName = block.GetType().Name,
                                Namespace = block.Namespace,
                                ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), block.ProgrammingLanguage),
                                MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                                IsConsistent = block.IsConsistent,
                                HeaderName = block.HeaderName,
                                ModifiedDate = block.ModifiedDate,
                                IsKnowHowProtected = block.IsKnowHowProtected,
                                Attributes = attributes,
                                Description = block.ToString()
                            });
                        }
                        processed++;
                    }
                }

                progress.Report(new ProgressNotificationValue { Progress = processed, Total = total, Message = $"Document import completed: {processed} blocks imported successfully" });

                var duration = (DateTime.Now - startTime).TotalSeconds;
                Logger?.LogInformation($"Document import completed: {processed} blocks imported in {duration:F2} seconds");

                return new ResponseImportBlocksFromDocuments
                {
                    Message = $"Document import completed: {processed} blocks imported from '{importPath}'",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["totalBlocks"] = total,
                        ["importedBlocks"] = processed,
                        ["duration"] = duration,
                        ["warnings"] = scanWarnings
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                progress.Report(new ProgressNotificationValue { Progress = 0, Total = 0, Message = $"Document import failed: {ex.Message}" });

                Logger?.LogError(ex, $"Failed importing documents from '{importPath}'");
                throw new McpException($"Unexpected error importing documents from '{importPath}': {ex.Message}", ex);
            }
        }

        private static ImportDocumentOptions ParseImportDocumentOption(string option)
        {
            if (string.IsNullOrWhiteSpace(option)) return ImportDocumentOptions.Override;

            var normalized = option.Trim();

            // Primary: accept exact enum names (case-insensitive)
            if (Enum.TryParse<ImportDocumentOptions>(normalized, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            // Aliases and common misspellings
            switch (normalized.ToLowerInvariant())
            {
                case "override": return ImportDocumentOptions.Override;
                case "none": return ImportDocumentOptions.None;
                case "skipinactiveculture":
                case "skipinactivecultures":
                case "skipinactive":
                case "skipinactivecult":
                    return ImportDocumentOptions.SkipInactiveCultures;
                case "activeinactiveculture":
                case "activateinactivecultures":
                case "activeinactivecultures":
                case "activateinactive":
                    return ImportDocumentOptions.ActivateInactiveCultures;
                default:
                    throw new McpException($"Invalid importOption '{option}'. Allowed: None, Override, SkipInactiveCultures, ActivateInactiveCultures");
            }
        }

        private static List<string> GetResMissingEnUsIds(string directory, string baseName)
        {
            var resPath = Path.Combine(directory, baseName + ".s7res");
            var missing = new List<string>();
            if (!File.Exists(resPath))
            {
                return missing;
            }
            var xdoc = XDocument.Load(resPath);
            XNamespace ns = xdoc.Root?.Name.Namespace ?? XNamespace.None;
            foreach (var comment in xdoc.Descendants(ns + "Comment"))
            {
                var hasEnUs = comment.Elements(ns + "MultiLanguageText")
                                     .Any(e => string.Equals((string?)e.Attribute("Lang"), "en-US", StringComparison.OrdinalIgnoreCase));
                if (!hasEnUs)
                {
                    var id = (string?)comment.Attribute("Id") ?? "";
                    missing.Add(id);
                }
            }
            return missing;
        }

        #endregion

        #region block/type write CRUD

        [McpServerTool(Name = "DeleteBlock", Title = "Delete block", Destructive = true, Idempotent = true, OpenWorld = false), Description("Delete a block from plc software. Irreversible except via TIA Portal's own undo (if still available) or project backup - confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseDeleteBlock DeleteBlock(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("blockPath: full path to the block, e.g. 'Group/Subgroup/Name'")] string blockPath)
        {
            try
            {
                Portal.DeleteBlock(softwarePath, blockPath);

                return new ResponseDeleteBlock
                {
                    Message = $"Block '{blockPath}' deleted",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error deleting block '{blockPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "DeleteType", Title = "Delete type", Destructive = true, Idempotent = true, OpenWorld = false), Description("Delete a PLC data type (UDT) from plc software. Irreversible except via TIA Portal's own undo (if still available) or project backup - confirm with the user before calling this against a real (non-disposable) project. Check GetTypeCrossReferences first - deleting a type still used by blocks will break them.")]
        public static ResponseDeleteType DeleteType(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("typePath: full path to the type, e.g. 'Group/Subgroup/Name'")] string typePath)
        {
            try
            {
                Portal.DeleteType(softwarePath, typePath);

                return new ResponseDeleteType
                {
                    Message = $"Type '{typePath}' deleted",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error deleting type '{typePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "SetBlockAttribute", Title = "Set block attribute", Destructive = true, Idempotent = true, OpenWorld = false), Description("Set a ReadWrite attribute on a block - e.g. set 'Name' to rename it, or 'MemoryLayout', 'Number', etc. Use GetBlockInfo first to see which attributes actually exist on this block and their accessMode - attribute names vary by block type (e.g. plain FBs have no 'Comment' attribute). Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseSetBlockAttribute SetBlockAttribute(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("blockPath: full path to the block, e.g. 'Group/Subgroup/Name'")] string blockPath,
            [Description("attributeName: name of the attribute to set - check GetBlockInfo's attributes list for this block first, names vary by block type")] string attributeName,
            [Description("value: new value as a string - converted to match the attribute's current type (bool/int/etc.) automatically")] string value)
        {
            try
            {
                Portal.SetBlockAttribute(softwarePath, blockPath, attributeName, value);

                return new ResponseSetBlockAttribute
                {
                    Message = $"Block '{blockPath}' attribute '{attributeName}' set to '{value}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                var reason = pex.InnerException?.Message?.Trim();
                var msg = pex.Message;
                if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";
                throw new McpException(msg, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error setting attribute '{attributeName}' on block '{blockPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "SetTypeAttribute", Title = "Set type attribute", Destructive = true, Idempotent = true, OpenWorld = false), Description("Set a ReadWrite attribute on a PLC data type (UDT) - e.g. set 'Name' to rename it. Use GetTypeInfo first to see which attributes actually exist on this type and their accessMode. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseSetTypeAttribute SetTypeAttribute(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("typePath: full path to the type, e.g. 'Group/Subgroup/Name'")] string typePath,
            [Description("attributeName: name of the attribute to set - check GetTypeInfo's attributes list for this type first")] string attributeName,
            [Description("value: new value as a string - converted to match the attribute's current type (bool/int/etc.) automatically")] string value)
        {
            try
            {
                Portal.SetTypeAttribute(softwarePath, typePath, attributeName, value);

                return new ResponseSetTypeAttribute
                {
                    Message = $"Type '{typePath}' attribute '{attributeName}' set to '{value}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                var reason = pex.InnerException?.Message?.Trim();
                var msg = pex.Message;
                if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";
                throw new McpException(msg, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error setting attribute '{attributeName}' on type '{typePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CreateBlockGroup", Title = "Create block group", Destructive = false, Idempotent = false, OpenWorld = false), Description("Create a new block group (folder) in plc software, for organizing blocks.")]
        public static ResponseCreateBlockGroup CreateBlockGroup(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("parentGroupPath: path to the parent group to create the new group under, e.g. 'Group/Subgroup' (empty for the root group)")] string parentGroupPath,
            [Description("name: name for the new group")] string name)
        {
            try
            {
                var group = Portal.CreateBlockGroup(softwarePath, parentGroupPath, name);

                return new ResponseCreateBlockGroup
                {
                    Message = $"Block group '{group.Name}' created under '{parentGroupPath}'",
                    Name = group.Name,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error creating block group '{name}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "DeleteBlockGroup", Title = "Delete block group", Destructive = true, Idempotent = true, OpenWorld = false), Description("Delete a block group (folder) from plc software - also deletes every block inside it. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseDeleteBlockGroup DeleteBlockGroup(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("groupPath: full path to the group, e.g. 'Group/Subgroup'")] string groupPath)
        {
            try
            {
                Portal.DeleteBlockGroup(softwarePath, groupPath);

                return new ResponseDeleteBlockGroup
                {
                    Message = $"Block group '{groupPath}' deleted",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error deleting block group '{groupPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CreateTypeGroup", Title = "Create type group", Destructive = false, Idempotent = false, OpenWorld = false), Description("Create a new type group (folder) in plc software, for organizing PLC data types (UDTs).")]
        public static ResponseCreateTypeGroup CreateTypeGroup(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("parentGroupPath: path to the parent group to create the new group under, e.g. 'Group/Subgroup' (empty for the root group)")] string parentGroupPath,
            [Description("name: name for the new group")] string name)
        {
            try
            {
                var group = Portal.CreateTypeGroup(softwarePath, parentGroupPath, name);

                return new ResponseCreateTypeGroup
                {
                    Message = $"Type group '{group.Name}' created under '{parentGroupPath}'",
                    Name = group.Name,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error creating type group '{name}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "DeleteTypeGroup", Title = "Delete type group", Destructive = true, Idempotent = true, OpenWorld = false), Description("Delete a type group (folder) from plc software - also deletes every type inside it. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseDeleteTypeGroup DeleteTypeGroup(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("groupPath: full path to the group, e.g. 'Group/Subgroup'")] string groupPath)
        {
            try
            {
                Portal.DeleteTypeGroup(softwarePath, groupPath);

                return new ResponseDeleteTypeGroup
                {
                    Message = $"Type group '{groupPath}' deleted",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error deleting type group '{groupPath}': {ex.Message}", ex);
            }
        }

        #endregion

        #region external sources (SCL import/export)

        [McpServerTool(Name = "GetExternalSources"), Description("Get a list of external sources (imported SCL/AWL/GRAPH source files) in plc software")]
        public static ResponseExternalSources GetExternalSources(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the external source. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetExternalSources(softwarePath, regexName);

                var responseList = list
                    .Where(source => source != null)
                    .Select(source => new ResponseExternalSourceInfo
                    {
                        Name = source.Name,
                        Attributes = Helper.GetAttributeList(source)
                    })
                    .ToList();

                return new ResponseExternalSources
                {
                    Message = $"External sources with regex '{regexName}' retrieved from '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving external sources with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ImportExternalSource", Title = "Import external source", Destructive = false, Idempotent = false, OpenWorld = false), Description("Import a local SCL/AWL/GRAPH source file into the project as a named external source. This only adds the source object - it does not create or change any blocks by itself; call GenerateBlocksFromSource afterward to compile it. Writing/mutating tool - confirm with the user before calling this against a real project, not just a disposable test one.")]
        public static ResponseImportExternalSource ImportExternalSource(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("groupPath: path to the external source group to import into, e.g. 'Group/Subgroup' (empty for the root group)")] string groupPath,
            [Description("importPath: full path to the local source file (.scl/.awl/.gr7/...)")] string importPath,
            [Description("sourceName: name for the created external source; defaults to the import file's name without extension")] string? sourceName = null)
        {
            try
            {
                var source = Portal.ImportExternalSource(softwarePath, groupPath, importPath, sourceName);

                return new ResponseImportExternalSource
                {
                    Message = $"External source '{source.Name}' imported from '{importPath}'",
                    Name = source.Name,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing external source from '{importPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GenerateBlocksFromSource", Title = "Generate blocks from source", Destructive = true, Idempotent = false, OpenWorld = false), Description("Compile an already-imported external source into real PLC blocks/types - this can create NEW blocks/types or OVERWRITE existing ones of the same name. Writing/mutating tool with real risk to the project's logic - confirm with the user and get an explicit go-ahead against a disposable/test project before calling this for the first time, not a real production project.")]
        public static ResponseGenerateBlocksFromSource GenerateBlocksFromSource(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("sourcePath: full path to the external source, e.g. 'Group/Subgroup/Name'")] string sourcePath,
            [Description("keepOnError: if true, keep whatever blocks were generated even if some failed; if false (default) roll back all generated blocks on any error")] bool keepOnError = false)
        {
            try
            {
                var generated = Portal.GenerateBlocksFromSource(softwarePath, sourcePath, keepOnError);

                var names = generated
                    .Select(o => (o as PlcBlock)?.Name ?? (o as PlcType)?.Name ?? o.ToString() ?? "?")
                    .ToList();

                return new ResponseGenerateBlocksFromSource
                {
                    Message = $"Generated {names.Count} object(s) from source '{sourcePath}'",
                    GeneratedObjectNames = names,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error generating blocks from source '{sourcePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "DeleteExternalSource", Title = "Delete external source", Destructive = true, Idempotent = true, OpenWorld = false), Description("Delete an external source object from the project (the source object only - does not affect blocks already generated from it). Writing/mutating tool - confirm with the user before calling this against a real project.")]
        public static ResponseDeleteExternalSource DeleteExternalSource(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("sourcePath: full path to the external source, e.g. 'Group/Subgroup/Name'")] string sourcePath)
        {
            try
            {
                Portal.DeleteExternalSource(softwarePath, sourcePath);

                return new ResponseDeleteExternalSource
                {
                    Message = $"External source '{sourcePath}' deleted",
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error deleting external source '{sourcePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ExportCax", Title = "Export CAx (AutomationML)", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export the project's hardware config (devices, modules, IO addresses) as an AutomationML (.aml) file for round-tripping with an ECAD tool like EPLAN. Omit devicePath to export the whole project; give it to export a single device only. This only writes a file - the project itself isn't changed.")]
        public static ResponseCaxTransfer ExportCax(
            [Description("exportPath: relative subfolder name (or omit) under the server-managed export folder (see Doctor's exportRoot) - NOT a full/absolute path")] string exportPath,
            [Description("devicePath: full path to a single device to export; omit to export the entire project")] string? devicePath = null)
        {
            try
            {
                var result = Portal.ExportCax(devicePath, exportPath);
                return BuildCaxResponse(result, $"CAx export of {(string.IsNullOrEmpty(devicePath) ? "the whole project" : $"'{devicePath}'")} to '{exportPath}'");
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting CAx to '{exportPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ImportCax", Title = "Import CAx (AutomationML)", Destructive = true, Idempotent = false, OpenWorld = false), Description("Import an AutomationML (.aml) file (e.g. hand-edited in EPLAN after an ExportCax) back into the project - can restructure hardware config. Openness has no preview/dry-run for this, unlike DeleteDevice, so confirm=true is required to actually run it; get an explicit go-ahead and test against a disposable/test project first, not a real production project. mergeOption controls what happens when the AML content conflicts with what's already in the project: 'MoveToParkingLot' (default, safest - conflicting items go to TIA's Parking Lot for a human to resolve instead of being silently applied), 'OverwriteTiaDevice' (conflicting TIA devices get overwritten with the AML content), or 'RetainTiaDevice' (conflicting AML content is discarded, TIA's existing devices win).")]
        public static ResponseCaxTransfer ImportCax(
            [Description("importPath: full path to the .aml file to import")] string importPath,
            [Description("confirm: must be explicitly true to actually import - false (default) refuses with an explanation instead of importing, since there's no preview available")] bool confirm = false,
            [Description("mergeOption: 'MoveToParkingLot' (default), 'OverwriteTiaDevice', or 'RetainTiaDevice'")] string mergeOption = "MoveToParkingLot")
        {
            if (!Enum.TryParse<CaxImportOptions>(mergeOption, ignoreCase: true, out var parsedOption))
            {
                throw new McpException($"Invalid mergeOption '{mergeOption}' - must be one of: MoveToParkingLot, OverwriteTiaDevice, RetainTiaDevice");
            }

            try
            {
                var (success, logText, logPath) = Portal.ImportCax(importPath, parsedOption, confirm);

                const int maxLogChars = 12000;
                var truncated = logText.Length > maxLogChars;
                var shownLog = truncated
                    ? logText.Substring(0, maxLogChars) + $"\n...(truncated, {logText.Length - maxLogChars} more chars - full log at {logPath})"
                    : logText;

                return new ResponseCaxTransfer
                {
                    Message = $"CAx import from '{importPath}' (mergeOption={parsedOption}): {(success ? "Success" : "Error")} - see LogText/LogPath for detail",
                    State = success ? "Success" : "Error",
                    LogPath = logPath,
                    LogText = shownLog,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = success }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing CAx from '{importPath}': {ex.Message}", ex);
            }
        }

        private static ResponseCaxTransfer BuildCaxResponse(TransferResult result, string actionDescription)
        {
            var messages = result.Messages
                .Select(m => new ResponseCaxMessage { Message = m.Message, State = m.State.ToString(), DateTime = m.DateTime })
                .ToList();

            return new ResponseCaxTransfer
            {
                Message = $"{actionDescription}: {result.State} ({result.ErrorCount} error(s), {result.WarningCount} warning(s))",
                State = result.State.ToString(),
                ErrorCount = result.ErrorCount,
                WarningCount = result.WarningCount,
                Messages = messages,
                Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = result.State != TransferResultState.Error }
            };
        }

        [McpServerTool(Name = "ExportSourceFromBlocks", Title = "Export blocks/types as SCL source", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export existing blocks and/or types as combined SCL source text to a file - the real 'export SCL' path, since external source objects themselves can't be exported. Give at least one of blockPaths/typePaths.")]
        public static ResponseExportSourceFromBlocks ExportSourceFromBlocks(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("exportPath: relative subfolder name (or omit) under the server-managed export folder (see Doctor's exportRoot) - NOT a full/absolute path")] string exportPath,
            [Description("fileName: name for the generated .scl file, without extension")] string fileName,
            [Description("blockPaths: full paths of blocks to include, e.g. 'Group/Subgroup/Name' (optional if typePaths is given)")] string[]? blockPaths = null,
            [Description("typePaths: full paths of types to include (optional if blockPaths is given)")] string[]? typePaths = null,
            [Description("withDependencies: also include each object's dependencies in the generated source")] bool withDependencies = false)
        {
            try
            {
                Portal.ExportSourceFromBlocks(softwarePath, blockPaths ?? [], typePaths ?? [], exportPath, fileName, withDependencies);

                return new ResponseExportSourceFromBlocks
                {
                    Message = $"Source exported to '{exportPath}/{fileName}.scl'",
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting source from '{softwarePath}': {ex.Message}", ex);
            }
        }

        #endregion

        #region tag tables

        [McpServerTool(Name = "GetTagTables"), Description("Get a list of tag tables in plc software")]
        public static ResponseTagTables GetTagTables(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the tag table. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetTagTables(softwarePath, regexName);

                var responseList = new List<ResponseTagTableInfo>();
                foreach (var table in list)
                {
                    if (table != null)
                    {
                        var attributes = Helper.GetAttributeList(table);
                        responseList.Add(new ResponseTagTableInfo
                        {
                            Name = table.Name,
                            TypeName = table.GetType().Name,
                            IsDefault = table.IsDefault,
                            Attributes = attributes,
                            Description = table.ToString()
                        });
                    }
                }

                return new ResponseTagTables
                {
                    Message = $"Tag tables with regex '{regexName}' retrieved from '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving tag tables with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetTags"), Description("Get a list of tags from a specific tag table in plc software")]
        public static ResponseTags GetTags(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("tagTablePath: full path to the tag table, e.g. 'Group/Subgroup/Name' (single names allowed at root level)")] string tagTablePath,
            [Description("regexName: defines the name or regular expression to find the tag. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetTags(softwarePath, tagTablePath, regexName);

                var responseList = new List<ResponseTagInfo>();
                foreach (var tag in list)
                {
                    if (tag != null)
                    {
                        responseList.Add(new ResponseTagInfo
                        {
                            Name = tag.Name,
                            DataTypeName = tag.DataTypeName,
                            LogicalAddress = tag.LogicalAddress?.ToString(),
                            Comment = Helper.MultilingualTextToString(tag.Comment)
                        });
                    }
                }

                return new ResponseTags
                {
                    Message = $"Tags with regex '{regexName}' retrieved from tag table '{tagTablePath}' in '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving tags from '{tagTablePath}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ExportTagTable"), Description("Export a tag table from plc software to file")]
        public static ResponseExportTagTable ExportTagTable(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("tagTablePath: full path to the tag table, e.g. 'Group/Subgroup/Name'")] string tagTablePath,
            [Description("exportPath: relative subfolder name (or omit) under the server-managed export folder (see Doctor's exportRoot) - NOT a full/absolute path")] string exportPath,
            [Description("preservePath: preserves the tag-table folder structure under exportPath")] bool preservePath = false)
        {
            try
            {
                Portal.ExportTagTable(softwarePath, tagTablePath, exportPath, preservePath);

                return new ResponseExportTagTable
                {
                    Message = $"Tag table exported from '{tagTablePath}' to '{exportPath}'",
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                switch (pex.Code)
                {
                    case TiaMcpServer.Siemens.PortalErrorCode.NotFound:
                        throw new McpException("Tag table not found.");
                    case TiaMcpServer.Siemens.PortalErrorCode.ExportFailed:
                        var reason = pex.InnerException?.Message?.Trim();
                        var msg = "Failed to export tag table.";
                        if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";
                        throw new McpException(msg, pex);
                    default:
                        throw new McpException(pex.Message, pex);
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting tag table '{tagTablePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CreateTagTable", Title = "Create tag table", Destructive = false, Idempotent = false, OpenWorld = false), Description("Create a new PLC tag table in plc software.")]
        public static ResponseCreateTagTable CreateTagTable(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("groupPath: path to the group to create the new tag table under, e.g. 'Group/Subgroup' (empty for the root group)")] string groupPath,
            [Description("name: name for the new tag table")] string name)
        {
            try
            {
                var table = Portal.CreateTagTable(softwarePath, groupPath, name);

                return new ResponseCreateTagTable
                {
                    Message = $"Tag table '{table.Name}' created under '{groupPath}'",
                    Name = table.Name,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error creating tag table '{name}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "DeleteTagTable", Title = "Delete tag table", Destructive = true, Idempotent = true, OpenWorld = false), Description("Delete a PLC tag table - also deletes every tag inside it. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseDeleteTagTable DeleteTagTable(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("tagTablePath: full path to the tag table, e.g. 'Group/Subgroup/Name'")] string tagTablePath)
        {
            try
            {
                Portal.DeleteTagTable(softwarePath, tagTablePath);

                return new ResponseDeleteTagTable
                {
                    Message = $"Tag table '{tagTablePath}' deleted",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error deleting tag table '{tagTablePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CreateTag", Title = "Create tag", Destructive = false, Idempotent = false, OpenWorld = false), Description("Create a new PLC tag in a tag table. Unlike blocks/types, tags have no SCL-generation route, so this is the only way to create one directly (besides XML import).")]
        public static ResponseCreateTag CreateTag(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("tagTablePath: full path to the tag table, e.g. 'Group/Subgroup/Name'")] string tagTablePath,
            [Description("name: name for the new tag")] string name,
            [Description("dataType: PLC data type for the tag, e.g. 'Bool', 'Int', 'Real'")] string dataType,
            [Description("logicalAddress: e.g. '%M0.0' - omit to let TIA auto-assign the next free address")] string? logicalAddress = null)
        {
            try
            {
                var tag = Portal.CreateTag(softwarePath, tagTablePath, name, dataType, logicalAddress);

                return new ResponseCreateTag
                {
                    Message = $"Tag '{tag.Name}' created in '{tagTablePath}'",
                    Name = tag.Name,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                var reason = pex.InnerException?.Message?.Trim();
                var msg = pex.Message;
                if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";
                throw new McpException(msg, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error creating tag '{name}' in '{tagTablePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "DeleteTag", Title = "Delete tag", Destructive = true, Idempotent = true, OpenWorld = false), Description("Delete a PLC tag from a tag table. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseDeleteTag DeleteTag(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("tagTablePath: full path to the tag table, e.g. 'Group/Subgroup/Name'")] string tagTablePath,
            [Description("tagName: name of the tag to delete")] string tagName)
        {
            try
            {
                Portal.DeleteTag(softwarePath, tagTablePath, tagName);

                return new ResponseDeleteTag
                {
                    Message = $"Tag '{tagName}' deleted from '{tagTablePath}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error deleting tag '{tagName}' from '{tagTablePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "SetTagAttribute", Title = "Set tag attribute", Destructive = true, Idempotent = true, OpenWorld = false), Description("Set a ReadWrite attribute on a PLC tag - e.g. set 'Name' to rename it, or 'LogicalAddress', 'Comment'. Use GetTags first to see which attributes exist. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseSetTagAttribute SetTagAttribute(
            [Description("softwarePath: defines the path in the project structure to the plc software")] string softwarePath,
            [Description("tagTablePath: full path to the tag table, e.g. 'Group/Subgroup/Name'")] string tagTablePath,
            [Description("tagName: name of the tag to modify")] string tagName,
            [Description("attributeName: name of the attribute to set")] string attributeName,
            [Description("value: new value as a string - converted to match the attribute's current type (bool/int/etc.) automatically")] string value)
        {
            try
            {
                Portal.SetTagAttribute(softwarePath, tagTablePath, tagName, attributeName, value);

                return new ResponseSetTagAttribute
                {
                    Message = $"Tag '{tagName}' attribute '{attributeName}' set to '{value}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                var reason = pex.InnerException?.Message?.Trim();
                var msg = pex.Message;
                if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";
                throw new McpException(msg, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error setting attribute '{attributeName}' on tag '{tagName}': {ex.Message}", ex);
            }
        }

        #endregion

        #region hmi tag tables

        [McpServerTool(Name = "GetHmiTagTables"), Description("Get a list of HMI tag tables from a device's HMI software (Unified Comfort/Advanced Panels only - classic WinCC Comfort/Basic panels aren't supported by this tool). Read-only: there's no export tool for HMI tag tables, this Openness version doesn't expose one.")]
        public static ResponseHmiTagTables GetHmiTagTables(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the tag table. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiTagTables(softwarePath, regexName);

                var responseList = list
                    .Where(table => table != null)
                    .Select(table => new ResponseHmiTagTableInfo { Name = table.Name })
                    .ToList();

                return new ResponseHmiTagTables
                {
                    Message = $"HMI tag tables with regex '{regexName}' retrieved from '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI tag tables with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetHmiTags"), Description("Get a list of tags from a specific HMI tag table (Unified Comfort/Advanced Panels only), including address/PLC-link/connection info")]
        public static ResponseHmiTags GetHmiTags(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1'")] string softwarePath,
            [Description("tagTablePath: full path to the HMI tag table, e.g. 'Group/Subgroup/Name' (single names allowed at root level)")] string tagTablePath,
            [Description("regexName: defines the name or regular expression to find the tag. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiTags(softwarePath, tagTablePath, regexName);

                var responseList = list
                    .Where(tag => tag != null)
                    .Select(tag => new ResponseHmiTagInfo
                    {
                        Name = tag.Name,
                        DataType = tag.DataType,
                        HmiDataType = tag.HmiDataType,
                        Address = tag.Address,
                        Connection = tag.Connection,
                        PlcName = tag.PlcName,
                        PlcTag = tag.PlcTag,
                        AccessMode = tag.AccessMode.ToString(),
                        AcquisitionMode = tag.AcquisitionMode.ToString(),
                        Scope = tag.Scope.ToString(),
                        TagType = tag.TagType.ToString(),
                        Comment = Helper.MultilingualTextToString(tag.Comment)
                    })
                    .ToList();

                return new ResponseHmiTags
                {
                    Message = $"HMI tags with regex '{regexName}' retrieved from tag table '{tagTablePath}' in '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI tags from '{tagTablePath}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        #endregion

        #region hmi screens/alarms/text lists

        [McpServerTool(Name = "GetHmiScreens"), Description("Get a list of screens in a device's HMI software (Unified Comfort/Advanced Panels only)")]
        public static ResponseHmiScreens GetHmiScreens(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the screen. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiScreens(softwarePath, regexName);

                var responseList = list
                    .Where(screen => screen != null)
                    .Select(screen => new ResponseHmiScreenInfo
                    {
                        Name = screen.Name,
                        DisplayName = Helper.MultilingualTextToString(screen.DisplayName),
                        ScreenNumber = screen.ScreenNumber,
                        Width = screen.Width,
                        Height = screen.Height
                    })
                    .ToList();

                return new ResponseHmiScreens
                {
                    Message = $"HMI screens with regex '{regexName}' retrieved from '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI screens with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetHmiDiscreteAlarms"), Description("Get a list of discrete (bit-triggered) alarms from a device's HMI software (Unified Comfort/Advanced Panels only)")]
        public static ResponseHmiAlarms GetHmiDiscreteAlarms(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the alarm. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiDiscreteAlarms(softwarePath, regexName);

                var responseList = list
                    .Where(alarm => alarm != null)
                    .Select(alarm => new ResponseHmiAlarmInfo
                    {
                        Name = alarm.Name,
                        EventText = Helper.MultilingualTextToString(alarm.EventText),
                        InfoText = Helper.MultilingualTextToString(alarm.InfoText),
                        AlarmClass = alarm.AlarmClass,
                        Area = alarm.Area,
                        Priority = alarm.Priority,
                        TriggerAddress = alarm.TriggerBitAddress
                    })
                    .ToList();

                return new ResponseHmiAlarms
                {
                    Message = $"HMI discrete alarms with regex '{regexName}' retrieved from '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI discrete alarms with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetHmiAnalogAlarms"), Description("Get a list of analog (limit-triggered) alarms from a device's HMI software (Unified Comfort/Advanced Panels only)")]
        public static ResponseHmiAlarms GetHmiAnalogAlarms(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the alarm. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiAnalogAlarms(softwarePath, regexName);

                var responseList = list
                    .Where(alarm => alarm != null)
                    .Select(alarm => new ResponseHmiAlarmInfo
                    {
                        Name = alarm.Name,
                        EventText = Helper.MultilingualTextToString(alarm.EventText),
                        InfoText = Helper.MultilingualTextToString(alarm.InfoText),
                        AlarmClass = alarm.AlarmClass,
                        Area = alarm.Area,
                        Priority = alarm.Priority,
                        TriggerAddress = alarm.TriggerAddress,
                        Condition = alarm.Condition.ToString()
                    })
                    .ToList();

                return new ResponseHmiAlarms
                {
                    Message = $"HMI analog alarms with regex '{regexName}' retrieved from '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI analog alarms with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetHmiTextLists"), Description("Get a list of text list names from a device's HMI software (Unified Comfort/Advanced Panels only). Names only - this Openness version has no way to read individual text list entries/values.")]
        public static ResponseHmiTextLists GetHmiTextLists(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the text list. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiTextLists(softwarePath, regexName);

                var responseList = list
                    .Where(textList => textList != null)
                    .Select(textList => new ResponseHmiTextListInfo { Name = textList.Name })
                    .ToList();

                return new ResponseHmiTextLists
                {
                    Message = $"HMI text lists with regex '{regexName}' retrieved from '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI text lists with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        #endregion
    }
}

