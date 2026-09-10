using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.TextGraphicList;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.Online;
using Siemens.Engineering.Safety;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Blocks.Interface;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    /// <summary>One running TIA Portal process, as reported by TiaPortal.GetProcesses() - listable without attaching.</summary>
    public class TiaPortalProcessInfo
    {
        public int Id { get; set; }
        public string? ProjectPath { get; set; }
        public string? Mode { get; set; }
    }

    // A single third-party (GSD-based) device or device item found in a project's hardware
    // config, with the catalog identity needed to install the matching GSD file elsewhere.
    public class GsdReference
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string? GsdId { get; set; }
        public string? GsdName { get; set; }
        public string? GsdType { get; set; }
        public bool IsProfibus { get; set; }
        public bool IsProfinet { get; set; }
    }

    public class Portal
    {
        // closing parantheses for regex characters ommitted, because they are not relevant for regex detection
        private readonly char[] _regexChars = ['.', '^', '$', '*', '+', '?', '(', '[', '{', '\\', '|'];

        private TiaPortal? _portal;
        private ProjectBase? _project;
        private LocalSession? _session;
        private readonly ILogger<Portal>? _logger;

        #region ctor

        public Portal(ILogger<Portal>? logger = null)
        {
            _logger = logger;
        }

        #endregion

        #region helper for mcp server

        public bool ProjectIsValid
        {
            get
            {
                if (_project == null)
                {
                    return false;
                }

                // Check if the project is a valid Project instance
                if ((_session == null) && (_project is Project))
                {
                    return true;
                }

                // If it's a MultiuserProject, we can also check its validity
                if ((_session != null) && (_project is MultiuserProject))
                {
                    return true;
                }

                return false;
            }
        }

        public bool IsLocalSession
        {
            get
            {
                return _session != null;
            }
        }

        public bool IsLocalProject
        {
            get
            {
                return _session == null;
            }
        }

        #endregion

        #region helper for unit tests

        public static bool IsLocalSessionFile(string sessionPath)
        {
            // Check if the path ends with '.als\d+' using regex
            var regex = new Regex(@"\.als\d+$", RegexOptions.IgnoreCase);
            return regex.IsMatch(sessionPath);
        }

        public static bool IsLocalProjectFile(string projectPath)
        {
            // Check if the path ends with '.ap\d+' using regex
            var regex = new Regex(@"\.ap\d+$", RegexOptions.IgnoreCase);
            return regex.IsMatch(projectPath);
        }

        public void Dispose()
        {
            try
            {
                (_project as Project)?.Close();
            }
            catch (Exception)
            {
                // Console.WriteLine($"Error closing the project: {ex.Message}");
            }

            try
            {
                _portal?.Dispose();
            }
            catch (Exception)
            {
                // Console.WriteLine($"Error closing the portal: {ex.Message}");
            }
        }

        #endregion

        #region portal

        /// <summary>
        /// Lists all running TIA Portal processes on this machine without attaching to any of
        /// them - use this to see what's available (and which project each has open) before
        /// calling ConnectPortal with a specific processId.
        /// </summary>
        public List<TiaPortalProcessInfo> GetTiaPortalProcesses()
        {
            _logger?.LogInformation("Listing TIA Portal processes...");

            return TiaPortal.GetProcesses()
                .Select(p => new TiaPortalProcessInfo
                {
                    Id = p.Id,
                    ProjectPath = p.ProjectPath?.FullName,
                    Mode = p.Mode.ToString()
                })
                .ToList();
        }

        /// <summary>Human-readable "Id=X (path)" list, embedded directly in Connect's rejection
        /// messages so a bad/ambiguous processId is self-explanatory without a second call.</summary>
        private static string DescribeProcesses(IEnumerable<TiaPortalProcess> processes)
        {
            return string.Join("; ", processes.Select(p => $"Id={p.Id} ({p.ProjectPath?.FullName ?? "no project open"})"));
        }

        public bool ConnectPortal(int? processId = null)
        {
            _logger?.LogInformation(processId.HasValue
                ? $"Connecting to TIA Portal process {processId.Value}..."
                : "Connecting to TIA Portal...");

            _project = null;
            _session = null;
            _portal = null;

            // connect to running TIA Portal
            var processes = TiaPortal.GetProcesses();
            if (processes.Any())
            {
                TiaPortalProcess targetProcess;
                if (processId.HasValue)
                {
                    targetProcess = processes.FirstOrDefault(p => p.Id == processId.Value)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"No running TIA Portal process with Id {processId.Value}. Running processes: {DescribeProcesses(processes)}");
                }
                else
                {
                    // No selection given and more than one instance is running - which one we'd
                    // silently attach to is arbitrary, so make the caller choose explicitly via
                    // processId instead of guessing. Include the list right here so a human/agent
                    // doesn't need a second round trip through GetTiaPortalProcesses() just to see
                    // why this was rejected.
                    if (processes.Count() > 1)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"Multiple TIA Portal processes are running - pass one of these as processId to Connect: {DescribeProcesses(processes)}");
                    }

                    targetProcess = processes.First();
                }

                _portal = targetProcess.Attach();

                // check for existing local sessions
                if (_portal.LocalSessions.Any())
                {
                    _session = _portal.LocalSessions.First();
                    _project = _session.Project;
                }
                // checks for existing projects
                else if (_portal.Projects.Any())
                {
                    _project = _portal.Projects.First();
                }

                return true;
            }

            // start new TIA Portal
            _portal = new TiaPortal(TiaPortalMode.WithUserInterface);

            return true;
        }

        public bool IsConnected()
        {
            return _portal != null;
        }

        public bool DisconnectPortal()
        {
            _logger?.LogInformation("Disconnecting from TIA Portal...");

            try
            {
                _project = null;
                _session = null;

                _portal?.Dispose();
                _portal = null;

                return true;
            }
            catch (Exception)
            {
                // Handle exception if needed, e.g., log it
            }

            return false;
        }

        #endregion

        #region status

        public State GetState()
        {
            _logger?.LogInformation("Getting TIA Portal state...");
            if (_portal != null)
            {
                // check for existing local sessions
                if (_portal.LocalSessions.Any())
                {
                    _session = _portal.LocalSessions.First();
                    _project = _session.Project;
                }
                // checks for existing projects
                else if (_portal.Projects.Any())
                {
                    _project = _portal.Projects.First();
                }
            }

            return new State
            {
                IsConnected = IsConnected(),
                Project = _project != null ? _project.Name : "-",
                Session = _session != null ? _session.Project.Name : "-"
            };
        }

        // Refreshes and returns the single currently-active project/session (or null if none is
        // open), using the same LocalSessions-first-then-Projects lookup as GetState().
        public ProjectBase? GetActiveProject()
        {
            _logger?.LogInformation("Getting active project...");

            if (_portal != null)
            {
                if (_portal.LocalSessions.Any())
                {
                    _session = _portal.LocalSessions.First();
                    _project = _session.Project;
                }
                else if (_portal.Projects.Any())
                {
                    _project = _portal.Projects.First();
                }
            }

            return _project;
        }

        #endregion

        #region project

        public List<ProjectBase> GetProjects()
        {
            _logger?.LogInformation("Getting open projects...");

            if (_portal == null)
            {
                _logger?.LogWarning("No TIA Portal instance available.");

                return [];
            }

            var projects = new List<ProjectBase>();

            if (_portal.Projects != null)
            {
                foreach (var project in _portal.Projects)
                {
                    projects.Add(project);
                }
            }

            return projects;
        }

        public bool OpenProject(string projectPath)
        {
            _logger?.LogInformation($"Opening project: {projectPath}");

            if (IsPortalNull())
            {
                return false;
            }

            if (_project != null)
            {
                (_project as Project)?.Close();
                _project = null;
            }

            if (_session != null)
            {
                _session.Close();
                _session = null;
            }

            try
            {
                var projects = GetProjects();
                var projectName = Path.GetFileNameWithoutExtension(projectPath);

                if (!string.IsNullOrEmpty(projectName) && projects.Any(p => p.Name.Equals(projectName)))
                {
                    // Project is already open
                    _project = _portal?.Projects.FirstOrDefault(p => p.Name == projectName);

                    return _project != null;
                }
                else
                {
                    // see [5.3.1 Projekt öffnen, S.113]
                    _project = _portal?.Projects.OpenWithUpgrade(new FileInfo(projectPath));

                    return _project != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public object? GetProjectInfo()
        {
            _logger?.LogInformation("Getting project info...");

            if (IsPortalNull())
            {
                return null;
            }

            if (IsProjectNull())
            {
                return null;
            }

            var project = _project!;

            var info = new
            {
                Name = project.Name,
                Path = project.Path,
                Type = project.GetType().Name,
                IsMultiuserProject = project is MultiuserProject,
                IsLocalSession = _session != null,
                IsLocalProject = _session == null
            };

            return info;
        }

        public bool SaveProject()
        {
            _logger?.LogInformation("Saving project...");

            if (IsProjectNull())
            {
                return false;
            }

            (_project as Project)?.Save();

            return true;
        }

        public bool SaveAsProject(string path)
        {
            _logger?.LogInformation($"Saving project as: {path}");

            if (IsProjectNull())
            {
                return false;
            }

            var di = new DirectoryInfo(path);

            (_project as Project)?.SaveAs(di);

            return true;
        }

        public bool CloseProject()
        {
            _logger?.LogInformation("Closing project...");

            if (IsProjectNull())
            {
                return false;
            }

            (_project as Project)?.Close();
            _project = null;

            return true;
        }

        #endregion

        #region session

        public List<ProjectBase> GetSessions()
        {
            _logger?.LogInformation("Getting open local sessions...");

            if (IsPortalNull())
            {
                return [];
            }

            var sessions = new List<ProjectBase>();

            if (_portal?.LocalSessions != null)
            {
                foreach (var session in _portal.LocalSessions)
                {
                    sessions.Add(session.Project as ProjectBase);
                }
            }

            return sessions;
        }

        public bool OpenSession(string localSessionPath)
        {
            _logger?.LogInformation($"Opening session: {localSessionPath}");

            if (IsPortalNull())
            {
                return false;
            }

            if (_session != null)
            {
                _project = null;
                _session?.Close();
                _session = null;
            }

            try
            {
                var sessions = GetSessions();
                var projectName = Path.GetFileNameWithoutExtension(localSessionPath);
                var sessionName = Regex.Replace(projectName, @"_(LS|ES)_\d$", string.Empty, RegexOptions.IgnoreCase);

                if (!string.IsNullOrEmpty(sessionName) && sessions.Any(s => s.Name.Equals(sessionName)))
                {
                    // Session is already open  
                    _session = _portal?.LocalSessions.FirstOrDefault(s => s.Project.Name == sessionName);
                    if (_session != null)
                    {
                        // Correctly cast MultiuserProject to Project  
                        _project = _session.Project;
                        return _project != null;
                    }
                }
                else
                {
                    _session = _portal?.LocalSessions.Open(new FileInfo(localSessionPath));
                    if (_session != null)
                    {
                        // Correctly cast MultiuserProject to Project  
                        _project = _session.Project;
                        return _project != null;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        public bool SaveSession()
        {
            _logger?.LogInformation("Saving session...");

            if (IsSessionNull())
            {
                return false;
            }

            // Save session
            _session?.Save();

            return true;
        }

        public bool CloseSession()
        {
            _logger?.LogInformation("Closing session...");

            if (IsSessionNull())
            {
                return false;
            }

            _project = null;
            _session?.Close();
            _session = null;

            return true;
        }

        #endregion

        #region devices

        public string GetProjectTree()
        {
            _logger?.LogInformation("Getting project tree...");

            if (IsProjectNull())
            {
                return string.Empty;
            }

            StringBuilder sb = new();

            sb.AppendLine($"{_project?.Name}");

            var ancestorStates = new List<bool>();
            var sections = new List<Action>();
            
            if (_project?.Devices != null && _project.Devices.Count > 0)
            {
                sections.Add(() => GetProjectTreeDevices(sb, _project.Devices, ancestorStates));
            }
            
            if (_project?.DeviceGroups != null && _project.DeviceGroups.Count > 0)
            {
                sections.Add(() => GetProjectTreeGroups(sb, _project.DeviceGroups, ancestorStates));
            }
            
            if (_project?.UngroupedDevicesGroup != null)
            {
                sections.Add(() => GetProjectTreeUngroupedDeviceGroup(sb, _project.UngroupedDevicesGroup, ancestorStates));
            }
            
            for (int i = 0; i < sections.Count; i++)
            {
                var isLastSection = i == sections.Count - 1;
                if (i == 0)
                {
                    sections[i]();
                }
                else
                {
                    sections[i]();
                }
            }

            return sb.ToString();
        }

        

        public List<Device> GetDevices(string regexName = "")
        {
            _logger?.LogInformation("Getting devices...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<Device>();

            if (_project?.Devices != null)
            {
                foreach (Device device in _project.Devices)
                {
                    list.Add(device);
                }

                foreach (var group in _project.DeviceGroups)
                {
                    GetDevicesRecursive(group, list, regexName);
                }

                //foreach (var group in _project.UngroupedDevicesGroup)
                //{
                //    GetDevicesRecursive(_project.UngroupedDevicesGroup, list, regexName);
                //}
            }

            return list;
        }

        public Device? GetDevice(string devicePath)
        {
            _logger?.LogInformation($"Getting device by path: {devicePath}");

            if (IsProjectNull())
            {
                return null;
            }

            // Retrieve the device by its path
            return GetDeviceByPath(devicePath);
        }

        public DeviceItem? GetDeviceItem(string deviceItemPath)
        {
            _logger?.LogInformation($"Getting device item by path: {deviceItemPath}");

            if (IsProjectNull())
            {
                return null;
            }

            // Retrieve the device by its path
            return GetDeviceItemByPath(deviceItemPath);

        }

        /// <summary>
        /// Lists every third-party (GSD-based) device/device item referenced by this project's
        /// hardware config - e.g. non-Siemens PROFINET/PROFIBUS IO devices - along with the
        /// GSD identity (GsdId/GsdName) needed to pre-install the same GSD file on another
        /// machine before opening this project there. Scoped to one device if devicePath is
        /// given, otherwise scans every device in the project.
        /// Note: this only sees devices that TIA already loaded successfully. If a device's GSD
        /// is missing on *this* machine, TIA's hardware object model may fail to instantiate it
        /// at all (see CHANGES.md 2026-09-10 "empty Projects/LocalSessions" writeup) - so this
        /// tool can't proactively flag a currently-missing GSD, only inventory the ones a
        /// working project already depends on, as a preventive check before moving it elsewhere.
        /// </summary>
        public List<GsdReference> GetGsdDependencies(string? devicePath = null)
        {
            _logger?.LogInformation("Scanning for GSD-based device dependencies...");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var devices = new List<Device>();
            if (string.IsNullOrEmpty(devicePath))
            {
                devices.AddRange(GetDevices());
            }
            else
            {
                var device = GetDeviceByPath(devicePath)
                    ?? throw new PortalException(PortalErrorCode.NotFound, $"Device not found at '{devicePath}'");
                devices.Add(device);
            }

            var results = new List<GsdReference>();
            foreach (var device in devices)
            {
                CollectGsdInfo(device, results);
            }

            return results;
        }

        private void CollectGsdInfo(Device device, List<GsdReference> results)
        {
            var gsdDevice = device.GetService<GsdDevice>();
            if (gsdDevice != null)
            {
                results.Add(new GsdReference
                {
                    Path = device.Name,
                    Name = device.Name,
                    GsdId = gsdDevice.GsdId,
                    GsdName = gsdDevice.GsdName,
                    GsdType = gsdDevice.GsdType,
                    IsProfibus = gsdDevice.IsProfibus,
                    IsProfinet = gsdDevice.IsProfinet
                });
            }

            if (device.DeviceItems != null)
            {
                foreach (var item in device.DeviceItems)
                {
                    CollectGsdInfoFromDeviceItem(item, device.Name, results);
                }
            }
        }

        private void CollectGsdInfoFromDeviceItem(DeviceItem deviceItem, string parentPath, List<GsdReference> results)
        {
            var path = $"{parentPath}/{deviceItem.Name}";

            var gsdItem = deviceItem.GetService<GsdDeviceItem>();
            if (gsdItem != null)
            {
                results.Add(new GsdReference
                {
                    Path = path,
                    Name = deviceItem.Name,
                    GsdId = gsdItem.GsdId,
                    GsdName = gsdItem.GsdName,
                    GsdType = gsdItem.GsdType,
                    IsProfibus = gsdItem.IsProfibus,
                    IsProfinet = gsdItem.IsProfinet
                });
            }

            if (deviceItem.DeviceItems != null)
            {
                foreach (var child in deviceItem.DeviceItems)
                {
                    CollectGsdInfoFromDeviceItem(child, path, results);
                }
            }
        }

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

        // path: full nested path to the interface DeviceItem, e.g.
        // "S7-1500/ET200MP station_1/PLC_1/PROFINET interface_1" - use GetProjectTree to find it
        // (look for a DeviceItem whose name matches the interface shown under a CPU/module).
        public NetworkInterface? GetNetworkInterfaceInfo(string path)
        {
            _logger?.LogInformation($"Getting network interface info for: {path}");

            if (IsProjectNull())
            {
                return null;
            }

            var deviceItem = FindDeviceItemByFullPath(path);
            return deviceItem?.GetService<NetworkInterface>();
        }

        /// <summary>
        /// Resolves the OnlineProvider service for a device or device item path - tries a Device
        /// first (e.g. a whole station like 'S7-1500/ET200MP station_1'), then a DeviceItem (e.g.
        /// the CPU module within it), mirroring how GetDevice/GetDeviceItem are both supported.
        /// This only establishes/reads the engineering station's online *connection* - it never
        /// starts, stops, or otherwise commands the PLC itself.
        /// </summary>
        private OnlineProvider? GetOnlineProvider(string path)
        {
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var device = GetDeviceByPath(path);
            var provider = device?.GetService<OnlineProvider>();
            if (provider != null)
            {
                return provider;
            }

            var deviceItem = GetDeviceItemByPath(path);
            return deviceItem?.GetService<OnlineProvider>();
        }

        public OnlineState? GetOnlineState(string path)
        {
            _logger?.LogInformation($"Getting online state: {path}");
            return GetOnlineProvider(path)?.State;
        }

        public OnlineState GoOnline(string path)
        {
            _logger?.LogInformation($"Going online: {path}");

            var provider = GetOnlineProvider(path)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Device or device item not found, or has no online connection, at '{path}'");

            return provider.GoOnline();
        }

        public void GoOffline(string path)
        {
            _logger?.LogInformation($"Going offline: {path}");

            var provider = GetOnlineProvider(path)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Device or device item not found, or has no online connection, at '{path}'");

            provider.GoOffline();
        }

        #endregion

        #region software

        public PlcSoftware? GetPlcSoftware(string softwarePath)
        {
            _logger?.LogInformation($"Getting software by path: {softwarePath}");

            if (IsProjectNull())
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);

            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                return plcSoftware;
            }

            return null;
        }

        public CompilerResult? CompileSoftware(string softwarePath, string password = "")
        {
            _logger?.LogInformation($"Compiling software by path: {softwarePath}");

            if (IsProjectNull())
            {
                return null; // "Error, no project";
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);

            if (!string.IsNullOrEmpty(password))
            {
                var deviceItem = softwareContainer?.Parent as DeviceItem;

                var admin = deviceItem?.GetService<SafetyAdministration>();
                if (admin != null)
                {
                    if (!admin.IsLoggedOnToSafetyOfflineProgram)
                    {
                        SecureString secString = new NetworkCredential("", password).SecurePassword;
                        try
                        {
                            admin.LoginToSafetyOfflineProgram(secString);
                        }
                        catch (Exception)
                        {
                            return null; // "Error, login to safety offline program failed";
                        }
                    }
                }
            }

            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                try
                {
                    ICompilable compileService = plcSoftware.GetService<ICompilable>();

                    CompilerResult result = compileService.Compile();

                    return result;
                }
                catch (Exception)
                {
                    return null; // "Error, compiling failed";
                }
            }

            return null; // "Error";
        }

        #endregion

        #region blocks/types

        public PlcBlock? GetBlock(string softwarePath, string blockPath)
        {
            _logger?.LogInformation($"Getting block by path: {blockPath}");

            if (IsProjectNull())
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                var blockGroup = plcSoftware?.BlockGroup;

                if (blockGroup != null)
                {
                    var path = blockPath.Contains("/") ? blockPath.Substring(0, blockPath.LastIndexOf("/")) : string.Empty;
                    var regexName = blockPath.Contains("/") ? blockPath.Substring(blockPath.LastIndexOf("/") + 1) : blockPath;

                    PlcBlock? block = null;

                    var group = GetPlcBlockGroupByPath(softwarePath, path);
                    if (group != null)
                    {
                        if (regexName.IndexOfAny(_regexChars) >= 0)
                        {
                            try
                            {
                                var regex = new Regex(regexName, RegexOptions.IgnoreCase);
                                block = group.Blocks.FirstOrDefault(b => regex.IsMatch(b.Name)) as PlcBlock;
                            }
                            catch (Exception)
                            {
                                // Invalid regex, return null
                                return null;
                            }
                        }
                        else
                        {
                            block = group.Blocks.FirstOrDefault(b => b.Name.Equals(regexName, StringComparison.OrdinalIgnoreCase));
                        }

                        return block;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Cross-references for a PLC type (UDT): everywhere the type - or its members - are used
        /// across the project, via Openness's CrossReferenceService.
        /// </summary>
        public CrossReferenceResult? GetTypeCrossReferences(string softwarePath, string typePath, CrossReferenceFilter filter = CrossReferenceFilter.AllObjects)
        {
            _logger?.LogInformation($"Getting cross references for type: {typePath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var type = GetType(softwarePath, typePath);
            var xrefService = type?.GetService<CrossReferenceService>();
            return xrefService?.GetCrossReferences(filter);
        }

        /// <summary>
        /// Cross-references for a block (e.g. an FB used elsewhere as an instance type, or an
        /// FC/OB called from other blocks): everywhere the block is used across the project.
        /// </summary>
        public CrossReferenceResult? GetBlockCrossReferences(string softwarePath, string blockPath, CrossReferenceFilter filter = CrossReferenceFilter.AllObjects)
        {
            _logger?.LogInformation($"Getting cross references for block: {blockPath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var block = GetBlock(softwarePath, blockPath);
            var xrefService = block?.GetService<CrossReferenceService>();
            return xrefService?.GetCrossReferences(filter);
        }

        public PlcType? GetType(string softwarePath, string typePath)
        {
            _logger?.LogInformation($"Getting type by path: {typePath}");

            if (IsProjectNull())
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                var typeGroup = plcSoftware?.TypeGroup;

                if (typeGroup != null)
                {
                    var path = typePath.Contains("/") ? typePath.Substring(0, typePath.LastIndexOf("/")) : string.Empty;
                    var regexName = typePath.Contains("/") ? typePath.Substring(typePath.LastIndexOf("/") + 1) : typePath;

                    PlcType? type = null;

                    var group = GetPlcTypeGroupByPath(softwarePath, path);
                    if (group != null)
                    {
                        if (regexName.IndexOfAny(_regexChars) >= 0)
                        {
                            try
                            {
                                var regex = new Regex(regexName, RegexOptions.IgnoreCase);
                                type = group.Types.FirstOrDefault(t => regex.IsMatch(t.Name)) as PlcType;
                            }
                            catch (Exception)
                            {
                                // Invalid regex, return null
                                return null;
                            }
                        }
                        else
                        {
                            type = group.Types.FirstOrDefault(t => t.Name.Equals(regexName, StringComparison.OrdinalIgnoreCase));
                        }

                        return type;
                    }
                }
            }

            return null;
        }

        public string GetBlockPath(PlcBlock block)
        {
            if (block == null)
            {
                return string.Empty;
            }

            if (block.Parent is PlcBlockGroup parentGroup)
            {
                var groupPath = GetPlcBlockGroupPath(parentGroup);
                return string.IsNullOrEmpty(groupPath) ? block.Name : $"{groupPath}/{block.Name}";
            }

            return block.Name;
        }

        public List<PlcBlock> GetBlocks(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting blocks...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<PlcBlock>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    var group = plcSoftware?.BlockGroup;

                    if (group != null)
                    {
                        GetBlocksRecursive(group, list, regexName);
                    }
                }
            }
            catch (Exception)
            {
                // Console.WriteLine($"Error getting blocks: {ex.Message}");
            }

            return list;
        }

        public PlcBlockGroup? GetBlockRootGroup(string softwarePath)
        {
            _logger?.LogInformation("Getting block root group...");

            if (IsProjectNull())
            {
                return null;
            }

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    return plcSoftware.BlockGroup;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting block root group");
            }

            return null;
        }

        public List<PlcType> GetTypes(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting types...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<PlcType>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    var group = plcSoftware?.TypeGroup;

                    if (group != null)
                    {
                        GetTypesRecursive(group, list, regexName);
                    }
                }
            }
            catch (Exception)
            {
                // Console.WriteLine($"Error getting user defined types: {ex.Message}");
            }

            return list;
        }

        public PlcBlock? ExportBlock(string softwarePath, string blockPath, string exportPath, bool preservePath = false)
        {
            _logger?.LogInformation($"Exporting block by path: {blockPath}");

            try
            {
                exportPath = OutputPathPolicy.ResolveDirectory(exportPath);

                if (IsProjectNull())
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
                }

                var block = GetBlock(softwarePath, blockPath);

                if (block == null)
                {
                    throw new PortalException(PortalErrorCode.NotFound, "Block not found");
                }

                if (preservePath)
                {
                    var groupPath = "";
                    if (block.Parent is PlcBlockGroup parentGroup)
                    {
                        groupPath = GetPlcBlockGroupPath(parentGroup);
                    }

                    exportPath = Path.Combine(exportPath, groupPath.Replace('/', '\\'), $"{SanitizeFileName(block.Name)}.xml");
                }
                else
                {
                    exportPath = Path.Combine(exportPath, $"{SanitizeFileName(block.Name)}.xml");
                }

                // TIA Portal never exports inconsistent blocks
                if (!block.IsConsistent)
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "Block is inconsistent; TIA Portal does not export inconsistent blocks.");
                }

                if (File.Exists(exportPath))
                {
                    File.Delete(exportPath);
                }

                block.Export(new FileInfo(exportPath), ExportOptions.None);

                return block;
            }
            catch (Exception ex)
            {
                //If the exception is already a PortalException, use it; otherwise, wrap it in a new PortalException
                var pex = ex as PortalException ?? new PortalException(PortalErrorCode.ExportFailed, "Export failed", null, ex);

                pex.Data["softwarePath"] = softwarePath;
                pex.Data["blockPath"] = blockPath;
                pex.Data["exportPath"] = exportPath;

                _logger?.LogError(pex, "ExportBlock failed for {SoftwarePath} {BlockPath} -> {ExportPath}", softwarePath, blockPath, exportPath);
                throw pex;
            }
        }

        public PlcType? ExportType(string softwarePath, string typePath, string exportPath, bool preservePath = false)
        {
            _logger?.LogInformation($"Exporting type by path: {typePath}");

            try
            {
                exportPath = OutputPathPolicy.ResolveDirectory(exportPath);

                if (IsProjectNull())
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
                }

                var type = GetType(softwarePath, typePath);

                if (type == null)
                {
                    throw new PortalException(PortalErrorCode.NotFound, "Type not found");
                }

                // TIA Portal never exports inconsistent types
                if (!type.IsConsistent)
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "Type is inconsistent; TIA Portal does not export inconsistent types.");
                }

                if (preservePath)
                {
                    var groupPath = "";
                    if (type.Parent is PlcTypeGroup parentGroup)
                    {
                        groupPath = GetPlcTypeGroupPath(parentGroup);
                    }

                    exportPath = Path.Combine(exportPath, groupPath.Replace('/', '\\'), $"{SanitizeFileName(type.Name)}.xml");
                }
                else
                {
                    exportPath = Path.Combine(exportPath, $"{SanitizeFileName(type.Name)}.xml");
                }

                if (File.Exists(exportPath))
                {
                    File.Delete(exportPath);
                }

                type.Export(new FileInfo(exportPath), ExportOptions.None);

                return type;
            }
            catch (Exception ex)
            {
                var pex = ex as PortalException ?? new PortalException(PortalErrorCode.ExportFailed, "Export failed", null, ex);

                if (!pex.Data.Contains("softwarePath")) pex.Data["softwarePath"] = softwarePath;
                if (!pex.Data.Contains("typePath")) pex.Data["typePath"] = typePath;
                if (!pex.Data.Contains("exportPath")) pex.Data["exportPath"] = exportPath;

                _logger?.LogError(pex, "ExportType failed for {SoftwarePath} {TypePath} -> {ExportPath}", softwarePath, typePath, exportPath);
                throw pex;
            }
        }

        public bool ImportBlock(string softwarePath, string groupPath, string importPath)
        {
            _logger?.LogInformation($"Importing block from path: {importPath}");

            if (IsProjectNull())
            {
                return false;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                var blockGroup = plcSoftware?.BlockGroup;

                if (blockGroup != null)
                {

                    var group = GetPlcBlockGroupByPath(softwarePath, groupPath);
                    if (group == null)
                    {
                        return false;
                    }

                    try
                    {
                        // Correct the argument type by using FileInfo instead of FileStream  
                        var fileInfo = new FileInfo(importPath);
                        if (fileInfo.Exists)
                        {
                            var list = group.Blocks.Import(fileInfo, ImportOptions.Override);
                            if (list != null && list.Count > 0)
                            {
                                return true;
                            }
                        }

                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        public bool ImportType(string softwarePath, string groupPath, string importPath)
        {
            _logger?.LogInformation($"Importing type from path: {importPath}");

            var success = false;

            if (IsProjectNull())
            {
                return success;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                var typeGroup = plcSoftware?.TypeGroup;

                if (typeGroup != null)
                {
                    var group = GetPlcTypeGroupByPath(softwarePath, groupPath);
                    if (group == null)
                    {
                        return false;
                    }

                    try
                    {
                        // Correct the argument type by using FileInfo instead of FileStream  
                        var fileInfo = new FileInfo(importPath);
                        if (fileInfo.Exists)
                        {
                            var list = group.Types.Import(fileInfo, ImportOptions.Override);
                            if (list != null && list.Count > 0)
                            {
                                return true;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
            }

            return success;
        }

        public IEnumerable<PlcBlock>? ExportBlocks(string softwarePath, string exportPath, string regexName = "", bool preservePath = false)
        {
            _logger?.LogInformation("Exporting blocks...");

            exportPath = OutputPathPolicy.ResolveDirectory(exportPath);

            if (IsProjectNull())
            {
                return null;
            }

            var exportList = new List<PlcBlock>();
            var failures = new List<string>();
            
            PlcBlock[] list;

            try
            {
                list = GetBlocks(softwarePath, regexName).ToArray();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to retrieve block list for {SoftwarePath}", softwarePath);
                return exportList;
            }

            for (int k = 0; k < list.Count(); k++)
            {
                var block = list[k];

                _logger?.LogDebug($"- Exporting block {k}/{list.Count()} : {block.Name}");

                string path;
                if (preservePath)
                {
                    var groupPath = "";
                    if (block.Parent is PlcBlockGroup parentGroup)
                    {
                        groupPath = GetPlcBlockGroupPath(parentGroup);
                    }
                    path = Path.Combine(exportPath, groupPath.Replace('/', '\\'), $"{SanitizeFileName(block.Name)}.xml");
                }
                else
                {
                    path = Path.Combine(exportPath, $"{SanitizeFileName(block.Name)}.xml");
                }

                try
                {
                    if (!block.IsConsistent)
                    {
                        _logger?.LogWarning("Skipping inconsistent block {Name}", block.Name);

                        continue;
                    }

                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    if (File.Exists(path))
                    {
                        try { File.Delete(path); }
                        catch (Exception ioEx)
                        {
                            failures.Add($"{block.Name}: cannot delete existing file ({ioEx.Message})");
                            _logger?.LogError(ioEx, "Delete failed for {File}", path);

                            continue;
                        }
                    }

                    try
                    {
                        block.Export(new FileInfo(path), ExportOptions.None);
                    }
                    catch (LicenseNotFoundException licEx)
                    {
                        failures.Add($"{block.Name}: license not found ({licEx.Message})");
                        _logger?.LogError(licEx, "License issue exporting {Block}", block.Name);

                        continue;
                    }
                    catch (EngineeringTargetInvocationException engEx)
                    {
                        failures.Add($"{block.Name}: target invocation failed ({engEx.Message})");
                        _logger?.LogError(engEx, "TargetInvocationException exporting {Block}", block.Name);

                        continue;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{block.Name}: export failed ({ex.Message})");
                        _logger?.LogError(ex, "Export failed for {Block}", block.Name);

                        continue;
                    }

                    exportList.Add(block);
                }
                catch (Exception ex)
                {
                    // Catch only truly unexpected wrapper-level errors
                    failures.Add($"{block.Name}: unexpected exception ({ex.Message})");
                    _logger?.LogError(ex, "Unexpected error at block {Block}", block.Name);
                    // continue with next block
                }
            }

            if (failures.Count > 0)
            {
                _logger?.LogWarning($"ExportBlocks completed with {failures.Count} failures out of {list.Count()}. First failure: {failures[0]}");
                // Optionally: _logger?.LogDebug("All failures: {Failures}", string.Join("; ", failures));
            }
            else
            {
                _logger?.LogInformation($"ExportBlocks completed successfully. Exported {exportList.Count} blocks.");
            }

            return exportList;
        }

        public IEnumerable<PlcType>? ExportTypes(string softwarePath, string exportPath, string regexName = "", bool preservePath = false)
        {
            _logger?.LogInformation("Exporting types...");

            exportPath = OutputPathPolicy.ResolveDirectory(exportPath);

            if (IsProjectNull())
            {
                return null;
            }

            var exportList = new List<PlcType>();
            var failures = new List<string>();

            PlcType[] list;

            try
            {
                list = GetTypes(softwarePath, regexName).ToArray();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to retrieve type list for {SoftwarePath}", softwarePath);
                return exportList;
            }

            for (int i = 0; i < list.Count(); i++)
            {
                var type = list[i];

                _logger?.LogDebug("- Exporting type {Index}/{Total} : {Name}", i, list.Count(), type.Name);

                string path;
                if (preservePath)
                {
                    var groupPath = "";
                    if (type.Parent is PlcTypeGroup parentGroup)
                    {
                        groupPath = GetPlcTypeGroupPath(parentGroup);
                    }
                    path = Path.Combine(exportPath, groupPath.Replace('/', '\\'), $"{SanitizeFileName(type.Name)}.xml");
                }
                else
                {
                    path = Path.Combine(exportPath, $"{SanitizeFileName(type.Name)}.xml");
                }

                try
                {
                    if (!type.IsConsistent)
                    {
                        _logger?.LogWarning("Skipping inconsistent type {Name}", type.Name);
                        continue;
                    }

                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Delete(path);
                        }
                        catch (Exception ioEx)
                        {
                            failures.Add($"{type.Name}: cannot delete existing file ({ioEx.Message})");
                            _logger?.LogError(ioEx, "Delete failed for {File}", path);
                            continue;
                        }
                    }

                    try
                    {
                        type.Export(new FileInfo(path), ExportOptions.None);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{type.Name}: export failed ({ex.Message})");
                        _logger?.LogError(ex, "Export failed for type {Type}", type.Name);
                        continue;
                    }

                    exportList.Add(type);
                }
                catch (Exception ex)
                {
                    failures.Add($"{type.Name}: unexpected exception ({ex.Message})");
                    _logger?.LogError(ex, "Unexpected error at type {Type}", type.Name);
                }
            }

            if (failures.Count > 0)
            {
                _logger?.LogWarning($"ExportTypes completed with {failures.Count} failures out of {list.Count()}. First failure: {failures[0]}");
            }
            else
            {
                _logger?.LogInformation($"ExportTypes completed successfully. Exported {exportList.Count} types.");
            }

            return exportList;
        }
        

        public bool ExportAsDocuments(string softwarePath, string blockPath, string exportPath, bool preservePath = false)
        {
            _logger?.LogInformation($"Exporting block as documents by path: {blockPath}");
            var success = false;
            try
            {
                exportPath = OutputPathPolicy.ResolveDirectory(exportPath);

                if (IsProjectNull())
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
                }

                if (Engineering.TiaMajorVersion < 20)
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "ExportAsDocuments requires TIA Portal V20 or newer");
                }

                
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    if (plcSoftware != null)
                    {
                        // Export code blocks as documents
                        // https://docs.tia.siemens.cloud/r/en-us/v20/creating-and-managing-blocks/exporting-and-importing-blocks-in-simatic-sd-format-s7-1200-s7-1500/exporting-and-importing-blocks-in-simatic-sd-format-s7-1200-s7-1500

                        var groupPath = blockPath.Contains("/") ? blockPath.Substring(0, blockPath.LastIndexOf("/")) : string.Empty;
                        var blockName = blockPath.Contains("/") ? blockPath.Substring(blockPath.LastIndexOf("/") + 1) : blockPath;

                        var group = GetPlcBlockGroupByPath(softwarePath, groupPath);

                        //group?.Blocks.ForEach(b => Console.WriteLine($"Block: {b.Name}, Type: {b.GetType().Name}"));

                        // join exportPath and groupPath
                        if (!Directory.Exists(exportPath))
                        {
                            Directory.CreateDirectory(exportPath);
                        }

                        if (preservePath && !string.IsNullOrEmpty(groupPath))
                        {
                            exportPath = Path.Combine(exportPath, groupPath);

                            if (!Directory.Exists(exportPath))
                            {
                                Directory.CreateDirectory(exportPath);
                            }
                        }

                        try
                        {
                            // delete files s7dcl/s7res if already exists
                            var blockFiles7dclPath = Path.Combine(exportPath, $"{SanitizeFileName(blockName)}.s7dcl");
                            if (File.Exists(blockFiles7dclPath))
                            {
                                File.Delete(blockFiles7dclPath);
                            }
                            var blockFiles7resPath = Path.Combine(exportPath, $"{SanitizeFileName(blockName)}.s7res");
                            if (File.Exists(blockFiles7resPath))
                            {
                                File.Delete(blockFiles7resPath);
                            }

                            var result = group?.Blocks.Find(blockName)?.ExportAsDocuments(new DirectoryInfo(exportPath), blockName);

                            if (result != null && result.State == DocumentResultState.Success)
                            {
                                success = true;
                            }
                        }
                        catch (EngineeringNotSupportedException ex)
                        {
                            // The export or import of blocks with mixed programming languages is not possible
                            throw new PortalException(PortalErrorCode.ExportFailed, $"EngineeringNotSupportedException at block '{blockName}'. {ex.Message}", null, ex);
                        }
                        catch (Exception ex)
                        {
                            throw new PortalException(PortalErrorCode.ExportFailed, $"Exception at block '{blockName}'. {ex.Message}", null, ex);
                        }

                    }

                }


            }
            catch (Exception ex)
            {
                var pex = ex as PortalException ?? new PortalException(PortalErrorCode.ExportFailed, "Export failed", null, ex);

                pex.Data["softwarePath"] = softwarePath;
                pex.Data["blockPath"] = blockPath;
                pex.Data["exportPath"] = exportPath;

                _logger?.LogError(pex, "ExportAsDocuments failed for {SoftwarePath} {BlockPath} -> {ExportPath}", softwarePath, blockPath, exportPath);
                throw pex;
            }
            return success;
        }

        // TIA portal crashes when exporting blocks as documents, :-(
        public IEnumerable<PlcBlock>? ExportBlocksAsDocuments(string softwarePath, string exportPath, string regexName = "", bool preservePath = false)
        {
            _logger?.LogInformation("Exporting blocks as documents...");

            exportPath = OutputPathPolicy.ResolveDirectory(exportPath);

            if (IsProjectNull())
            {
                return null;
            }

            if (Engineering.TiaMajorVersion < 20)
            {
                _logger?.LogWarning("ExportBlocksAsDocuments is only supported on TIA Portal V20 or newer");
                return null;
            }

            var exportList = new List<PlcBlock>();
            var failures = new List<string>();

            PlcBlock[] list;
            try
            {
                list = GetBlocks(softwarePath, regexName).ToArray();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Failed to retrieve block list for {softwarePath}");
                return exportList;
            }

            for (int i = 0; i < list.Count(); i++)
            {
                var block = list[i];

                _logger?.LogDebug($"- Exporting block as document {i}/{list.Count()} : {block.Name}");

                // Skip inconsistent blocks (TIA generally won’t export them)
                if (!block.IsConsistent)
                {
                    _logger?.LogWarning($"Skipping inconsistent block {block.Name}");
                    continue;
                }

                // Determine base directory (preserve group path if requested)
                string targetDir = exportPath;
                if (preservePath && block.Parent is PlcBlockGroup parentGroup)
                {
                    var groupPath = GetPlcBlockGroupPath(parentGroup);
                    if (!string.IsNullOrWhiteSpace(groupPath))
                    {
                        targetDir = Path.Combine(exportPath, groupPath.Replace('/', '\\'));
                    }
                }

                try
                {
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{block.Name}: cannot create directory '{targetDir}' ({ex.Message})");
                    _logger?.LogError(ex, $"Directory creation failed for {targetDir}");
                    continue;
                }

                var fileDcl = Path.Combine(targetDir, $"{SanitizeFileName(block.Name)}.s7dcl");
                var fileRes = Path.Combine(targetDir, $"{SanitizeFileName(block.Name)}.s7res");

                // Clean previous artifacts
                foreach (var f in new[] { fileDcl, fileRes })
                {
                    try
                    {
                        if (File.Exists(f))
                        {
                            File.Delete(f);
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{block.Name}: cannot delete existing '{Path.GetFileName(f)}' ({ex.Message})");
                        _logger?.LogError(ex, $"Failed deleting existing file {f}");
                        // Continue anyway; export might overwrite.
                    }
                }

                try
                {
                    DocumentExportResult? result = null;
                    try
                    {
                        result = block.ExportAsDocuments(new DirectoryInfo(targetDir), block.Name);
                    }
                    catch (EngineeringNotSupportedException ex)
                    {
                        failures.Add($"{block.Name}: not supported ({ex.Message})");
                        _logger?.LogWarning(ex, $"EngineeringNotSupported exporting {block.Name}");
                        continue;
                    }
                    catch (LicenseNotFoundException ex)
                    {
                        failures.Add($"{block.Name}: license not found ({ex.Message})");
                        _logger?.LogError(ex, $"License issue exporting {block.Name}");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{block.Name}: export threw ({ex.Message})");
                        _logger?.LogError(ex, $"ExportAsDocuments failed for {block.Name}");
                        continue;
                    }

                    if (result == null)
                    {
                        failures.Add($"{block.Name}: no result returned");
                        continue;
                    }

                    if (result.State == DocumentResultState.Success)
                    {
                        exportList.Add(block);
                    }
                    else
                    {
                        failures.Add($"{block.Name}: result state {result.State}");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{block.Name}: unexpected exception ({ex.Message})");
                    _logger?.LogError(ex, $"Unexpected wrapper error for {block.Name}");
                }
            }

            if (failures.Count > 0)
            {
                _logger?.LogWarning($"ExportBlocksAsDocuments completed with {failures.Count} failures out of {list.Count()}. First failure: {failures[0]}");
                // Optional verbose list:
                // _logger?.LogDebug("All failures: {Failures}", string.Join("; ", failures));
            }
            else
            {
                _logger?.LogInformation($"ExportBlocksAsDocuments completed successfully. Exported {exportList.Count} blocks.");
            }

            return exportList;
        }

        public bool ImportFromDocuments(string softwarePath, string groupPath, string importPath, string fileNameWithoutExtension, ImportDocumentOptions option)
        {
            _logger?.LogInformation($"Importing block from documents: {fileNameWithoutExtension} in {importPath}");

            if (IsProjectNull())
            {
                return false;
            }

            if (Engineering.TiaMajorVersion < 20)
            {
                _logger?.LogWarning("ImportFromDocuments is only supported on TIA Portal V20 or newer");
                return false;
            }

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    var group = GetPlcBlockGroupByPath(softwarePath, groupPath);
                    var dir = new DirectoryInfo(importPath);
                    if (!dir.Exists)
                    {
                        _logger?.LogWarning($"Import directory does not exist: {importPath}");
                        return false;
                    }

                    DocumentImportResult? result = null;
                    try
                    {
                        result = (group != null)
                            ? group.Blocks.ImportFromDocuments(dir, fileNameWithoutExtension, option)
                            : plcSoftware.BlockGroup.Blocks.ImportFromDocuments(dir, fileNameWithoutExtension, option);
                    }
                    catch (EngineeringNotSupportedException ex)
                    {
                        throw new PortalException(PortalErrorCode.ExportFailed, $"EngineeringNotSupportedException at file '{fileNameWithoutExtension}'. {ex.Message}", null, ex);
                    }

                    if (result != null && result.State == DocumentResultState.Success)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error importing block from documents");
            }
            return false;
        }

        public IEnumerable<PlcBlock>? ImportBlocksFromDocuments(string softwarePath, string groupPath, string importPath, string regexName, ImportDocumentOptions option, bool preservePath = false)
        {
            _logger?.LogInformation($"Importing blocks from documents in {importPath} with regex '{regexName}'");

            if (IsProjectNull())
            {
                return null;
            }

            if (Engineering.TiaMajorVersion < 20)
            {
                _logger?.LogWarning("ImportBlocksFromDocuments is only supported on TIA Portal V20 or newer");
                return null;
            }

            var imported = new List<PlcBlock>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    var group = GetPlcBlockGroupByPath(softwarePath, groupPath);
                    var dir = new DirectoryInfo(importPath);
                    if (!dir.Exists)
                    {
                        _logger?.LogWarning($"Import directory does not exist: {importPath}");
                        return imported;
                    }

                    var rx = string.IsNullOrWhiteSpace(regexName)
                        ? null
                        : new Regex(regexName, RegexOptions.Compiled);

                    // Consider .s7dcl as the primary index; .s7res is optional supplemental
                    var files = dir.GetFiles("*.s7dcl", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        var name = Path.GetFileNameWithoutExtension(file.Name);
                        if (rx != null && !rx.IsMatch(name))
                        {
                            continue;
                        }

                        try
                        {
                            var result = (group != null)
                                ? group.Blocks.ImportFromDocuments(dir, name, option)
                                : plcSoftware.BlockGroup.Blocks.ImportFromDocuments(dir, name, option);

                            if (result != null && result.State == DocumentResultState.Success && result.ImportedPlcBlocks != null)
                            {
                                foreach (var blk in result.ImportedPlcBlocks)
                                {
                                    if (blk != null)
                                    {
                                        imported.Add(blk);
                                    }
                                }
                            }
                        }
                        catch (EngineeringNotSupportedException)
                        {
                            // mixed languages etc.; skip but continue batch
                        }
                        catch (Exception)
                        {
                            // skip problematic item, continue
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error importing blocks from documents");
            }

            return imported;
        }

        #endregion

        #region private helper

        private bool IsPortalNull()
        {
            if (_portal == null)
            {
                _logger?.LogWarning("No TIA portal available.");

                return true;
            }

            return false;
        }

        private bool IsProjectNull()
        {
            if (_project == null)
            {
                _logger?.LogWarning("No TIA project available.");

                return true;
            }

            return false;
        }

        private bool IsSessionNull()
        {
            if (_session == null)
            {
                _logger?.LogWarning("No TIA session available.");

                return true;
            }

            return false;
        }

        #region  GetTree ...

        private string GetTreePrefix(List<bool> ancestorStates, bool isLast)
        {
            var prefix = new StringBuilder();
            
            // Build prefix based on ancestor states
            for (int i = 0; i < ancestorStates.Count; i++)
            {
                prefix.Append(ancestorStates[i] ? "    " : "│   ");
            }
            
            // Add current level connector
            prefix.Append(isLast ? "└── " : "├── ");
            return prefix.ToString();
        }

        private void GetProjectTreeDevices(StringBuilder sb, DeviceComposition devices, List<bool> ancestorStates)
        {
            if (devices.Count == 0) return;
            
            // Check if this is the last main section
            var hasOtherSections = (_project?.DeviceGroups != null && _project.DeviceGroups.Count > 0) ||
                                  (_project?.UngroupedDevicesGroup != null);
            var isLastMainSection = !hasOtherSections;
            
            sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastMainSection)}Devices [Collection]");

            var deviceList = devices.ToList();
            var newAncestorStates = new List<bool>(ancestorStates) { isLastMainSection };
            
            for (int i = 0; i < deviceList.Count; i++)
            {
                var device = deviceList[i];
                var isLastDevice = i == deviceList.Count - 1;
                
                sb.AppendLine($"{GetTreePrefix(newAncestorStates, isLastDevice)}{device.Name} [Device: {device.TypeIdentifier}]");

                if (device.DeviceItems != null && device.DeviceItems.Count > 0)
                {
                    GetProjectTreeDeviceItemsRecursive(sb, device.DeviceItems, new List<bool>(newAncestorStates) { isLastDevice });
                }
            }
        }

        private void GetProjectTreeGroups(StringBuilder sb, DeviceUserGroupComposition groups, List<bool> ancestorStates)
        {
            if (groups.Count == 0) return;
            
            var isLastMainSection = _project?.UngroupedDevicesGroup == null;
            
            sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastMainSection)}Groups [Collection]");

            var groupList = groups.ToList();
            var newAncestorStates = new List<bool>(ancestorStates) { isLastMainSection };
            
            for (int i = 0; i < groupList.Count; i++)
            {
                var group = groupList[i];
                var isLastGroup = i == groupList.Count - 1;
                
                sb.AppendLine($"{GetTreePrefix(newAncestorStates, isLastGroup)}{group.Name} [Group]");

                var groupAncestorStates = new List<bool>(newAncestorStates) { isLastGroup };
                
                if (group.Devices != null && group.Devices.Count > 0)
                {
                    GetProjectTreeGroupDevices(sb, group.Devices, groupAncestorStates, group.Groups != null && group.Groups.Count > 0);
                }
                
                if (group.Groups != null && group.Groups.Count > 0)
                {
                    GetProjectTreeSubGroups(sb, group.Groups, groupAncestorStates);
                }
            }
        }
        
        private void GetProjectTreeGroupDevices(StringBuilder sb, DeviceComposition devices, List<bool> ancestorStates, bool hasSubGroups)
        {
            var deviceList = devices.ToList();
            
            for (int i = 0; i < deviceList.Count; i++)
            {
                var device = deviceList[i];
                var isLastDevice = i == deviceList.Count - 1 && !hasSubGroups;
                
                sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastDevice)}{device.Name} [Device]");
                
                if (device.DeviceItems != null && device.DeviceItems.Count > 0)
                {
                    GetProjectTreeDeviceItemsRecursive(sb, device.DeviceItems, new List<bool>(ancestorStates) { isLastDevice });
                }
            }
        }
        
        private void GetProjectTreeSubGroups(StringBuilder sb, DeviceUserGroupComposition groups, List<bool> ancestorStates)
        {
            var groupList = groups.ToList();
            
            for (int i = 0; i < groupList.Count; i++)
            {
                var group = groupList[i];
                var isLastGroup = i == groupList.Count - 1;
                
                sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastGroup)}{group.Name} [Subgroup]");
                
                var groupAncestorStates = new List<bool>(ancestorStates) { isLastGroup };
                
                if (group.Devices != null && group.Devices.Count > 0)
                {
                    GetProjectTreeGroupDevices(sb, group.Devices, groupAncestorStates, group.Groups != null && group.Groups.Count > 0);
                }
                
                if (group.Groups != null && group.Groups.Count > 0)
                {
                    GetProjectTreeSubGroups(sb, group.Groups, groupAncestorStates);
                }
            }
        }

        private void GetProjectTreeDeviceItemsRecursive(StringBuilder sb, DeviceItemComposition deviceItems, List<bool> ancestorStates)
        {
            var deviceItemsList = deviceItems.ToList();
            
            for (int i = 0; i < deviceItemsList.Count; i++)
            {
                var deviceItem = deviceItemsList[i];
                var isLastDeviceItem = i == deviceItemsList.Count - 1;
                
                sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastDeviceItem)}{deviceItem.Name} [DeviceItem]");
                
                var itemAncestorStates = new List<bool>(ancestorStates) { isLastDeviceItem };
                
                // Get software first
                GetProjectTreeDeviceItemSoftware(sb, deviceItem, itemAncestorStates);
                
                // Then get items
                if (deviceItem.Items != null && deviceItem.Items.Count > 0)
                {
                    GetProjectTreeItems(sb, deviceItem.Items, itemAncestorStates, deviceItem.DeviceItems != null && deviceItem.DeviceItems.Count > 0);
                }
                
                // Finally get sub-device items
                if (deviceItem.DeviceItems != null && deviceItem.DeviceItems.Count > 0)
                {
                    GetProjectTreeDeviceItemsRecursive(sb, deviceItem.DeviceItems, itemAncestorStates);
                }
            }
        }
        
        private void GetProjectTreeItems(StringBuilder sb, DeviceItemAssociation items, List<bool> ancestorStates, bool hasSubDeviceItems)
        {
            var itemsList = items.ToList();
            
            for (int i = 0; i < itemsList.Count; i++)
            {
                var subItem = itemsList[i];
                var isLastItem = i == itemsList.Count - 1 && !hasSubDeviceItems;
                
                sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastItem)}{subItem.Name} [Hardware Component]");
            }
        }


        private void GetProjectTreeDeviceItemSoftware(StringBuilder sb, DeviceItem deviceItem, List<bool> ancestorStates)
        {
            var softwareContainer = deviceItem.GetService<SoftwareContainer>();
            var hasSoftware = false;
            
            //PLC software
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                var hasOtherItems = (deviceItem.Items != null && deviceItem.Items.Count > 0) ||
                                   (deviceItem.DeviceItems != null && deviceItem.DeviceItems.Count > 0);
                sb.AppendLine($"{GetTreePrefix(ancestorStates, !hasOtherItems)}PlcSoftware: {plcSoftware.Name} [PLC Program]");
                hasSoftware = true;
            }

            //WinCC HMI software
            if (softwareContainer?.Software is HmiTarget hmiTarget)
            {
                var hasOtherItems = (deviceItem.Items != null && deviceItem.Items.Count > 0) ||
                                   (deviceItem.DeviceItems != null && deviceItem.DeviceItems.Count > 0);
                sb.AppendLine($"{GetTreePrefix(ancestorStates, !hasOtherItems && !hasSoftware)}HmiTarget: {hmiTarget.Name} [HMI Program]");
            }

            //Unified HMI software: dlls will only exist on TIA Portal V19 and newer.
            if (Engineering.TiaMajorVersion >= 19)
                TryGetUnifiedSoftware(sb, deviceItem, ancestorStates, softwareContainer, hasSoftware);
        }

        private bool TryGetUnifiedSoftware(StringBuilder sb, DeviceItem deviceItem, List<bool> ancestorStates, SoftwareContainer? softwareContainer, bool hasSoftware)
        {
            if (softwareContainer?.Software is HmiSoftware hmiSoftware)
            {
                var hasOtherItems = (deviceItem.Items != null && deviceItem.Items.Count > 0) ||
                                    (deviceItem.DeviceItems != null && deviceItem.DeviceItems.Count > 0);
                sb.AppendLine($"{GetTreePrefix(ancestorStates, !hasOtherItems && !hasSoftware)}HmiSoftware: {hmiSoftware.Name} [HMI Program]");
                hasSoftware = true;
            }

            return hasSoftware;
        }

        private void GetProjectTreeUngroupedDeviceGroup(StringBuilder sb, DeviceSystemGroup ungroupedDevicesGroup, List<bool> ancestorStates)
        {
            sb.AppendLine($"{GetTreePrefix(ancestorStates, true)}UngroupedDevicesGroup: {ungroupedDevicesGroup.Name} [System Group]");

            if (ungroupedDevicesGroup.Devices != null && ungroupedDevicesGroup.Devices.Count > 0)
            {
                var deviceList = ungroupedDevicesGroup.Devices.ToList();
                var newAncestorStates = new List<bool>(ancestorStates) { true };
                
                for (int i = 0; i < deviceList.Count; i++)
                {
                    var device = deviceList[i];
                    var isLastDevice = i == deviceList.Count - 1;
                    
                    sb.AppendLine($"{GetTreePrefix(newAncestorStates, isLastDevice)}{device.Name} [{device.TypeIdentifier}]");
                }
            }
        }

        #endregion

        #region GetSoftwareTree ...

        public string GetSoftwareTree(string softwarePath)
        {
            _logger?.LogInformation("Getting software tree for path: {SoftwarePath}", softwarePath);

            if (IsProjectNull())
            {
                return string.Empty;
            }

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    StringBuilder sb = new();
                    sb.AppendLine($"{plcSoftware.Name} [PLC Software]");
                    
                    var ancestorStates = new List<bool>();
                    var sections = new List<Action>();
                    
                    var hasBlocks = plcSoftware.BlockGroup != null;
                    var hasTypes = plcSoftware.TypeGroup != null;
                    
                    // Add blocks section
                    if (hasBlocks)
                    {
                        var blockGroup = plcSoftware.BlockGroup;
                        if (blockGroup != null)
                        {
                            sections.Add(() => GetSoftwareTreeBlockGroup(sb, blockGroup, ancestorStates, "Program blocks", !hasTypes));
                        }
                    }
                    
                    // Add types section
                    if (hasTypes)
                    {
                        var typeGroup = plcSoftware.TypeGroup;
                        if (typeGroup != null)
                        {
                            sections.Add(() => GetSoftwareTreeTypeGroup(sb, typeGroup, ancestorStates, "PLC data types", true));
                        }
                    }
                    
                    
                    // Execute sections
                    for (int i = 0; i < sections.Count; i++)
                    {
                        sections[i]();
                    }

                    return sb.ToString();
                }
                else
                {
                    return $"No PLC software found at path: {softwarePath}";
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting software tree for {SoftwarePath}", softwarePath);
                return $"Error retrieving software tree: {ex.Message}";
            }
        }
        
        private void GetSoftwareTreeBlockGroup(StringBuilder sb, PlcBlockGroup blockGroup, List<bool> ancestorStates, string groupLabel, bool isLastSection)
        {
            sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastSection)}{groupLabel}"); // [Collection]
            var newAncestorStates = new List<bool>(ancestorStates) { isLastSection };
            
            // Get blocks in this group
            var blocks = blockGroup.Blocks.ToList();
            var subGroups = blockGroup.Groups.ToList();
            
            // First, add all blocks
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                // Block is last only if it's the last block AND there are no subgroups following
                var isLastBlock = (i == blocks.Count - 1) && (subGroups.Count == 0);

                var blockTypeName = new[] { "ArrayDB", "GlobalDB", "InstanceDB" }.Contains(block.GetType().Name)
                    ? "DB"
                    : block.GetType().Name;

                sb.AppendLine($"{GetTreePrefix(newAncestorStates, isLastBlock)}{block.Name} [{blockTypeName}{block.Number}, {block.ProgrammingLanguage}]");
            }
            
            // Then, add all subgroups recursively
            for (int i = 0; i < subGroups.Count; i++)
            {
                var subGroup = subGroups[i];
                var isLastGroup = i == subGroups.Count - 1;
                
                sb.AppendLine($"{GetTreePrefix(newAncestorStates, isLastGroup)}{subGroup.Name}"); // [Block Group]

                var groupAncestorStates = new List<bool>(newAncestorStates) { isLastGroup };
                GetSoftwareTreeBlockGroupRecursive(sb, subGroup, groupAncestorStates);
            }
        }
        
        private void GetSoftwareTreeBlockGroupRecursive(StringBuilder sb, PlcBlockGroup blockGroup, List<bool> ancestorStates)
        {
            // Get blocks in this group
            var blocks = blockGroup.Blocks.ToList();
            var subGroups = blockGroup.Groups.ToList();
            
            // First, add all blocks
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                // Block is last only if it's the last block AND there are no subgroups following
                var isLastBlock = (i == blocks.Count - 1) && (subGroups.Count == 0);

                var blockTypeName = new[] { "ArrayDB", "GlobalDB", "InstanceDB" }.Contains(block.GetType().Name)
                    ? "DB"
                    : block.GetType().Name;

                sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastBlock)}{block.Name} [{blockTypeName}{block.Number}, {block.ProgrammingLanguage}]");
            }
            
            // Then, add all subgroups recursively
            for (int i = 0; i < subGroups.Count; i++)
            {
                var subGroup = subGroups[i];
                var isLastGroup = i == subGroups.Count - 1;
                
                sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastGroup)}{subGroup.Name}"); // [Block Group]

                var groupAncestorStates = new List<bool>(ancestorStates) { isLastGroup };
                GetSoftwareTreeBlockGroupRecursive(sb, subGroup, groupAncestorStates);
            }
        }
        
        private void GetSoftwareTreeTypeGroup(StringBuilder sb, PlcTypeGroup typeGroup, List<bool> ancestorStates, string groupLabel, bool isLastSection)
        {
            
            sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastSection)}{groupLabel}"); // [Collection]
            var newAncestorStates = new List<bool>(ancestorStates) { isLastSection };
            
            // Get types in this group
            var types = typeGroup.Types.ToList();
            var subGroups = typeGroup.Groups.ToList();
            
            // First, add all types
            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];
                // Type is last only if it's the last type AND there are no subgroups following
                var isLastType = (i == types.Count - 1) && (subGroups.Count == 0);

                var typeTypeName = type.GetType().Name;
                typeTypeName = typeTypeName=="PlcStruct" ? "UDT": typeTypeName;

                sb.AppendLine($"{GetTreePrefix(newAncestorStates, isLastType)}{type.Name} [{typeTypeName}]");
            }
            
            // Then, add all subgroups recursively
            for (int i = 0; i < subGroups.Count; i++)
            {
                var subGroup = subGroups[i];
                var isLastGroup = i == subGroups.Count - 1;
                
                sb.AppendLine($"{GetTreePrefix(newAncestorStates, isLastGroup)}{subGroup.Name}"); // [Type Group]

                var groupAncestorStates = new List<bool>(newAncestorStates) { isLastGroup };
                GetSoftwareTreeTypeGroupRecursive(sb, subGroup, groupAncestorStates);
            }
        }
        
        private void GetSoftwareTreeTypeGroupRecursive(StringBuilder sb, PlcTypeGroup typeGroup, List<bool> ancestorStates)
        {
            // Get types in this group
            var types = typeGroup.Types.ToList();
            var subGroups = typeGroup.Groups.ToList();
            
            // First, add all types
            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];
                // Type is last only if it's the last type AND there are no subgroups following
                var isLastType = (i == types.Count - 1) && (subGroups.Count == 0);

                var typeTypeName = type.GetType().Name;
                typeTypeName = typeTypeName == "PlcStruct" ? "UDT" : typeTypeName;

                sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastType)}{type.Name} [{typeTypeName}]");
            }
            
            // Then, add all subgroups recursively
            for (int i = 0; i < subGroups.Count; i++)
            {
                var subGroup = subGroups[i];
                var isLastGroup = i == subGroups.Count - 1;
                
                sb.AppendLine($"{GetTreePrefix(ancestorStates, isLastGroup)}{subGroup.Name}"); // [Type Group]

                var groupAncestorStates = new List<bool>(ancestorStates) { isLastGroup };
                GetSoftwareTreeTypeGroupRecursive(sb, subGroup, groupAncestorStates);
            }
        }

        #endregion

        #region GetSoftwareContainer ...

        private SoftwareContainer? GetSoftwareContainer(string softwarePath)
        {
            if (_project == null)
            {
                return null;
            }

            string[] pathSegments = softwarePath.Split('/');
            int index = 0;

            if (index >= pathSegments.Length)
                return null;

            string segment = pathSegments[index];
            SoftwareContainer? softwareContainer = null;

            // in Devices
            if (_project.Devices != null)
            {
                softwareContainer = GetSoftwareContainerInDevices(_project.Devices, pathSegments, index);
                if (softwareContainer != null)
                {
                    return softwareContainer;
                }
            }

            // in Groups
            if (_project.DeviceGroups != null)
            {
                softwareContainer = GetSoftwareContainerInGroups(_project.DeviceGroups, pathSegments, index);
                if (softwareContainer != null)
                {
                    return softwareContainer;
                }
            }

            return null;
        }

        private SoftwareContainer? GetSoftwareContainerInDevices(DeviceComposition devices, string[] pathSegments, int index)
        {

            if (index >= pathSegments.Length)
                return null;

            string segment = pathSegments[index];
            string nextSegment = index + 1 < pathSegments.Length ? pathSegments[index + 1] : string.Empty;

            if (devices != null)
            {
                SoftwareContainer? softwareContainer = null;
                Device? device = null;
                DeviceItem? deviceItem = null;

                // a pc based plc has a Device.Name = 'PC-System_1' or something like that, which is visible in the TIA-Portal IDE
                // use segment to find device
                device = devices.FirstOrDefault(d => d.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (device != null)
                {
                    // then use next segment to find device item
                    deviceItem = device.DeviceItems.FirstOrDefault(di => di.Name.Equals(nextSegment, StringComparison.OrdinalIgnoreCase));
                    // but here we use next segment to find device item
                    softwareContainer = GetSoftwareContainerInDeviceItem(deviceItem, pathSegments, index + 1);
                    if (softwareContainer != null)
                    {
                        return softwareContainer;
                    }
                }

                // a hardware plc has a Device.Name = 'S7-1500/ET200MP-Station_1' or something like that, which is not visible in the TIA-Portal IDE
                // ignored segment for Device.Name and use it for DeviceItem.Name
                deviceItem = devices
                    .SelectMany(d => d.DeviceItems)
                    .FirstOrDefault(di => di.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (deviceItem != null)
                {
                    return GetSoftwareContainerInDeviceItem(deviceItem, pathSegments, index);
                }

            }

            return null;
        }

        private SoftwareContainer? GetSoftwareContainerInGroups(DeviceUserGroupComposition groups, string[] pathSegments, int index)
        {
            if (index >= pathSegments.Length)
                return null;

            string segment = pathSegments[index];
            SoftwareContainer? softwareContainer = null;

            if (groups != null)
            {
                var group = groups.FirstOrDefault(g => g.Name.Equals(segment));
                if (group != null)
                {
                    // when segment matched
                    softwareContainer = GetSoftwareContainerInDevices(group.Devices, pathSegments, index + 1);
                    if (softwareContainer != null)
                    {
                        return softwareContainer;
                    }

                    return GetSoftwareContainerInGroups(group.Groups, pathSegments, index + 1);
                }
            }

            return null;
        }

        private SoftwareContainer? GetSoftwareContainerInDeviceItem(DeviceItem deviceItem, string[] pathSegments, int index)
        {
            if (deviceItem != null)
            {
                // when segment matched
                if (index == pathSegments.Length - 1)
                {
                    // get from DeviceItem
                    var softwareContainer = deviceItem.GetService<SoftwareContainer>();
                    if (softwareContainer != null)
                    {
                        return softwareContainer;
                    }
                }
            }

            return null;
        }

        #endregion

        #region Get...ByPath

        private Device? GetDeviceByPath(string devicePath)
        {
            if (_project?.Devices == null || string.IsNullOrWhiteSpace(devicePath))
                return null;

            // A hardware PLC's Device.Name can itself contain '/' (e.g. 'S7-1500/ET200MP station_1'),
            // which isn't visible as such in the TIA Portal IDE. Always try an exact whole-string
            // match against real device names first, before treating '/' as a path separator -
            // otherwise a name like that gets mis-split into bogus group/device segments and the
            // device is reported as not found.
            var exactMatch = FindDeviceByFullName(devicePath);
            if (exactMatch != null)
            {
                return exactMatch;
            }

            var pathSegments = devicePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (pathSegments.Length == 0)
            {
                return null;
            }

            // Try top-level device first
            if (pathSegments.Length == 1)
            {
                return _project.Devices.FirstOrDefault(d => d.Name.Equals(pathSegments[0], StringComparison.OrdinalIgnoreCase));
            }

            // Traverse device groups
            DeviceUserGroupComposition? groups = _project.DeviceGroups;
            DeviceUserGroup? group = groups?.FirstOrDefault(g => g.Name.Equals(pathSegments[0], StringComparison.OrdinalIgnoreCase));

            if (group == null)
            {
                return null;
            }

            for (int i = 1; i < pathSegments.Length; i++)
            {
                // Try to find device in current group
                var device = group.Devices.FirstOrDefault(d => d.Name.Equals(pathSegments[i], StringComparison.OrdinalIgnoreCase));
                if (device != null)
                {
                    return device;
                }

                // Try to find subgroup
                group = group.Groups.FirstOrDefault(g => g.Name.Equals(pathSegments[i], StringComparison.OrdinalIgnoreCase));
                if (group == null)
                {
                    break;
                }
            }

            return null;
        }

        // Resolves a nested DeviceItem path of arbitrary depth (e.g. a network interface tucked
        // inside a CPU's DeviceItems, like "S7-1500/ET200MP station_1/PLC_1/PROFINET interface_1")
        // - unlike GetDeviceItemByPath, which only reaches one level below the device. Tries the
        // longest possible prefix as the device name first (device names can themselves contain
        // '/', and devices can live inside nested device groups), then walks every remaining
        // segment through nested .DeviceItems.
        private DeviceItem? FindDeviceItemByFullPath(string path)
        {
            if (_project == null || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return null;
            }

            for (int deviceLen = segments.Length - 1; deviceLen >= 1; deviceLen--)
            {
                var candidatePath = string.Join("/", segments.Take(deviceLen));
                var device = FindDeviceByFullName(candidatePath) ?? GetDeviceByPath(candidatePath);
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

                    current = items.FirstOrDefault(x => x.Name.Equals(segments[i], StringComparison.OrdinalIgnoreCase));
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

        /// <summary>
        /// Searches top-level devices and every device group (recursively) for a device whose
        /// Name matches the given string exactly - used to find devices whose own name contains
        /// '/' before that character gets treated as a path separator.
        /// </summary>
        private Device? FindDeviceByFullName(string name)
        {
            if (_project?.Devices == null)
            {
                return null;
            }

            var device = _project.Devices.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (device != null)
            {
                return device;
            }

            return FindDeviceByFullNameInGroups(_project.DeviceGroups, name);
        }

        private static Device? FindDeviceByFullNameInGroups(DeviceUserGroupComposition? groups, string name)
        {
            if (groups == null)
            {
                return null;
            }

            foreach (var group in groups)
            {
                var device = group.Devices?.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (device != null)
                {
                    return device;
                }

                var found = FindDeviceByFullNameInGroups(group.Groups, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private DeviceItem? GetDeviceItemByPath(string deviceItemPath)
        {
            if (_project == null || _project.Devices == null)
            {
                return null;
            }

            // Split the device path by '/' to get each device name  
            var pathSegments = deviceItemPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            DeviceItem? deviceItem = null;

            // initial devices and groups
            var devices = _project.Devices;
            var groups = _project.DeviceGroups;

            for (int index = 0; index < pathSegments.Length; index++)
            {
                deviceItem = GetDeviceItemFromDevice(pathSegments, devices, index);

                if (deviceItem == null)
                {
                    // search in groups
                    var group = groups?.FirstOrDefault(g => g.Name.Equals(pathSegments[index], StringComparison.OrdinalIgnoreCase));
                    if (group != null)
                    {
                        devices = group.Devices;
                        if (devices != null)
                        {
                            deviceItem = GetDeviceItemFromDevice(pathSegments, devices, index + 1);
                        }

                        if (deviceItem != null)
                        {
                            return deviceItem;
                        }

                        // not found, but on the path
                        groups = group.Groups;
                        devices = group.Devices;
                    }
                }
                else
                {
                    return deviceItem;
                }
            }

            return deviceItem;
        }

        private static DeviceItem? GetDeviceItemFromDevice(string[] pathSegments, DeviceComposition? devices, int index)
        {
            string segment = pathSegments[index];
            string nextSegment = index + 1 < pathSegments.Length ? pathSegments[index + 1] : string.Empty;

            DeviceItem? deviceItem = null;

            // a pc based plc has a Device.Name = 'PC-System_1' or something like that, which is visible in the TIA-Portal IDE
            // use segment to find device
            var device = devices.FirstOrDefault(d => d.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (device != null)
            {
                // then use next segment to find device item
                deviceItem = device.DeviceItems.FirstOrDefault(di => di.Name.Equals(nextSegment, StringComparison.OrdinalIgnoreCase));

            }

            // a hardware plc has a Device.Name = 'S7-1500/ET200MP-Station_1' or something like that, which is not visible in the TIA-Portal IDE
            if (device == null)
            {
                deviceItem = devices
                .SelectMany(d => d.DeviceItems)
                .FirstOrDefault(di => di.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            }

            return deviceItem;
        }

        private PlcBlockGroup? GetPlcBlockGroupByPath(string softwarePath, string groupPath)
        {
            if (_project == null)
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                if (plcSoftware?.BlockGroup == null)
                {
                    return null;
                }


                // Split the path by '/' to get each group name
                var groupNames = groupPath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);

                // GetSoftwareTree labels the block root "Program blocks" for readability, but it
                // isn't a real subgroup - strip it if callers pasted a path straight from that tree.
                if (groupNames.Length > 0 && groupNames[0].Equals("Program blocks", StringComparison.OrdinalIgnoreCase))
                {
                    groupNames = groupNames.Skip(1).ToArray();
                }

                PlcBlockGroup? currentGroup = plcSoftware.BlockGroup;

                foreach (var groupName in groupNames)
                {
                    currentGroup = currentGroup.Groups.FirstOrDefault(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));

                    if (currentGroup == null)
                    {
                        return null;
                    }
                }

                return currentGroup;
            }

            return null;
        }

        private PlcTypeGroup? GetPlcTypeGroupByPath(string softwarePath, string groupPath)
        {
            if (_project == null)
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                if (plcSoftware?.TypeGroup == null)
                {
                    return null;
                }

                var groupNames = groupPath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);

                // GetSoftwareTree labels the type root "PLC data types" for readability, but it
                // isn't a real subgroup - strip it if callers pasted a path straight from that tree.
                if (groupNames.Length > 0 && groupNames[0].Equals("PLC data types", StringComparison.OrdinalIgnoreCase))
                {
                    groupNames = groupNames.Skip(1).ToArray();
                }

                PlcTypeGroup? currentGroup = plcSoftware.TypeGroup;

                foreach (var groupName in groupNames)
                {
                    currentGroup = currentGroup.Groups.FirstOrDefault(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));

                    if (currentGroup == null)
                    {
                        return null;
                    }
                }

                return currentGroup;
            }

            return null;
        }

        // 블록 이름에 파일명 불가 문자가 올 수 있음 (예: 'T_Data->HMI', 'GTS_EQ_I/FLower')
        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }

        private string GetPlcBlockGroupPath(PlcBlockGroup group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            PlcBlockGroup? nullableGroup = group;
            var path = group.Name;

            while (nullableGroup != null && nullableGroup.Parent != null)
            {
                try
                {
                    //group = (PlcBlockGroup) group.Parent;
                    if (group is PlcBlockSystemGroup systemGroup)
                    {
                        // do not get parent for system group
                        break;
                    }

                    nullableGroup = nullableGroup.Parent as PlcBlockGroup;
                }
                catch (Exception)
                {
                    // Handle any exceptions that may occur while accessing the parent
                    break;
                }

                if (nullableGroup != null)
                {
                    path = $"{nullableGroup.Name}/{path}";
                }
            }

            return path;
        }

        private string GetPlcTypeGroupPath(PlcTypeGroup group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            PlcTypeGroup? nullableGroup = group;
            var path = group.Name;

            while (nullableGroup != null && nullableGroup.Parent != null)
            {
                try
                {
                    //group = (PlcTypeGroup) group.Parent;
                    if (group is PlcTypeSystemGroup systemGroup)
                    {
                        // do not get parent for system group
                        break;
                    }

                    nullableGroup = nullableGroup.Parent as PlcTypeGroup;
                }
                catch (Exception)
                {
                    // Handle any exceptions that may occur while accessing the parent
                    break;
                }

                if (nullableGroup != null)
                {
                    path = $"{nullableGroup.Name}/{path}";
                }
            }

            return path;
        }

        #endregion

        #region GetRecursive ...

        private bool GetDevicesRecursive(DeviceUserGroup group, List<Device> list, string regexName = "")
        {
            var anySuccess = false;

            foreach (var composition in group.Devices)
            {
                if (composition is Device device)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(device.Name, regexName, RegexOptions.IgnoreCase))
                        {
                            continue; // Skip this device if it doesn't match the pattern
                        }
                    }
                    catch (Exception)
                    {
                        // Invalid regex pattern, skip this device
                        continue;
                    }

                    list.Add(device);

                    anySuccess = true;
                }
            }

            foreach (var subgroup in group.Groups)
            {
                anySuccess = GetDevicesRecursive(subgroup, list, regexName);
            }

            return anySuccess;
        }

        private bool GetBlocksRecursive(PlcBlockGroup group, List<PlcBlock> list, string regexName = "")
        {
            var anySuccess = false;

            foreach (var composition in group.Blocks)
            {
                if (composition is PlcBlock block)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(block.Name, regexName, RegexOptions.IgnoreCase))
                        {
                            continue; // Skip this block if it doesn't match the pattern
                        }
                    }
                    catch (Exception)
                    {
                        // Invalid regex pattern, skip this block
                        continue;
                    }

                    list.Add(block);

                    anySuccess = true;
                }
            }

            foreach (var subgroup in group.Groups)
            {
                anySuccess = GetBlocksRecursive(subgroup, list, regexName);
            }

            return anySuccess;
        }

        private bool GetTypesRecursive(PlcTypeGroup group, List<PlcType> list, string regexName = "")
        {
            var anySuccess = false;

            foreach (var composition in group.Types)
            {
                if (composition is PlcType type)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(type.Name, regexName, RegexOptions.IgnoreCase))
                        {
                            continue; // Skip this block if it doesn't match the pattern
                        }
                    }
                    catch (Exception)
                    {
                        // Invalid regex pattern, skip this block
                        continue;
                    }

                    list.Add(type);

                    anySuccess = true;
                }

            }

            foreach (PlcTypeGroup subgroup in group.Groups)
            {
                anySuccess = GetTypesRecursive(subgroup, list, regexName);
            }

            return anySuccess;
        }

        #endregion

        #region tag tables

        public List<PlcTagTable> GetTagTables(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting tag tables...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<PlcTagTable>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    var rootGroup = plcSoftware?.TagTableGroup;

                    if (rootGroup != null)
                    {
                        CollectTagTablesFromComposition(rootGroup.TagTables, list, regexName);

                        foreach (var subgroup in rootGroup.Groups)
                        {
                            GetTagTablesRecursive(subgroup, list, regexName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetTagTables failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        public PlcTagTable? GetTagTable(string softwarePath, string tagTablePath)
        {
            _logger?.LogInformation($"Getting tag table by path: {tagTablePath}");

            if (IsProjectNull())
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(tagTablePath))
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                var rootGroup = plcSoftware?.TagTableGroup;
                if (rootGroup == null)
                {
                    return null;
                }

                var parts = tagTablePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    return null;
                }

                var tableName = parts[parts.Length - 1];

                if (parts.Length == 1)
                {
                    // Search root-level tables first, then recurse into user groups
                    var found = rootGroup.TagTables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                    if (found != null) return found;
                    return FindTagTableRecursive(rootGroup.Groups, tableName);
                }
                else
                {
                    PlcTagTableUserGroup? current = rootGroup.Groups.FirstOrDefault(g => g.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                    for (int i = 1; i < parts.Length - 1 && current != null; i++)
                    {
                        current = current.Groups.FirstOrDefault(g => g.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                    }

                    if (current == null)
                    {
                        return null;
                    }

                    return current.TagTables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                }
            }

            return null;
        }

        public List<PlcTag> GetTags(string softwarePath, string tagTablePath, string regexName = "")
        {
            _logger?.LogInformation($"Getting tags for table: {tagTablePath}");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<PlcTag>();

            try
            {
                var table = GetTagTable(softwarePath, tagTablePath);
                if (table != null)
                {
                    foreach (var tag in table.Tags)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(tag.Name, regexName, RegexOptions.IgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        list.Add(tag);
                    }
                }
            }
            catch (Exception)
            {
                // Same swallow style as GetBlocks/GetTypes
            }

            return list;
        }

        public void ExportTagTable(string softwarePath, string tagTablePath, string exportPath, bool preservePath = false)
        {
            _logger?.LogInformation($"Exporting tag table by path: {tagTablePath}");

            try
            {
                exportPath = OutputPathPolicy.ResolveDirectory(exportPath);

                if (IsProjectNull())
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
                }

                var table = GetTagTable(softwarePath, tagTablePath);

                if (table == null)
                {
                    throw new PortalException(PortalErrorCode.NotFound, "Tag table not found");
                }

                if (preservePath)
                {
                    var groupPath = string.Empty;
                    if (table.Parent is PlcTagTableUserGroup parentGroup)
                    {
                        groupPath = GetPlcTagTableUserGroupPath(parentGroup);
                    }

                    exportPath = Path.Combine(exportPath, groupPath.Replace('/', '\\'), $"{SanitizeFileName(table.Name)}.xml");
                }
                else
                {
                    exportPath = Path.Combine(exportPath, $"{SanitizeFileName(table.Name)}.xml");
                }

                var dir = Path.GetDirectoryName(exportPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(exportPath))
                {
                    File.Delete(exportPath);
                }

                table.Export(new FileInfo(exportPath), ExportOptions.None);
            }
            catch (Exception ex)
            {
                var pex = ex as PortalException ?? new PortalException(PortalErrorCode.ExportFailed, "Export failed", null, ex);

                pex.Data["softwarePath"] = softwarePath;
                pex.Data["tagTablePath"] = tagTablePath;
                pex.Data["exportPath"] = exportPath;

                _logger?.LogError(pex, "ExportTagTable failed for {SoftwarePath} {TagTablePath} -> {ExportPath}", softwarePath, tagTablePath, exportPath);
                throw pex;
            }
        }

        private PlcTagTable? FindTagTableRecursive(PlcTagTableUserGroupComposition groups, string tableName)
        {
            foreach (var group in groups)
            {
                var found = group.TagTables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
                found = FindTagTableRecursive(group.Groups, tableName);
                if (found != null) return found;
            }
            return null;
        }

        private void CollectTagTablesFromComposition(PlcTagTableComposition tables, List<PlcTagTable> list, string regexName)
        {
            foreach (var table in tables)
            {
                try
                {
                    if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(table.Name, regexName, RegexOptions.IgnoreCase))
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                list.Add(table);
            }
        }

        private void GetTagTablesRecursive(PlcTagTableUserGroup group, List<PlcTagTable> list, string regexName = "")
        {
            CollectTagTablesFromComposition(group.TagTables, list, regexName);

            foreach (var subgroup in group.Groups)
            {
                GetTagTablesRecursive(subgroup, list, regexName);
            }
        }

        private string GetPlcTagTableUserGroupPath(PlcTagTableUserGroup group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            PlcTagTableUserGroup? nullableGroup = group;
            var path = group.Name;

            while (nullableGroup != null && nullableGroup.Parent != null)
            {
                try
                {
                    nullableGroup = nullableGroup.Parent as PlcTagTableUserGroup;
                }
                catch (Exception)
                {
                    break;
                }

                if (nullableGroup != null)
                {
                    path = $"{nullableGroup.Name}/{path}";
                }
            }

            return path;
        }

        #endregion

        #region plc tag write CRUD

        // Unlike blocks/types, tags have no SCL-generation route - PlcTagComposition.Create is
        // the only way to make one with real content, so (unlike block/type CRUD) a dedicated
        // create tool is warranted here.

        public PlcTagTable CreateTagTable(string softwarePath, string groupPath, string name)
        {
            _logger?.LogInformation($"Creating tag table '{name}' under '{groupPath}'");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var group = GetPlcTagTableGroupByPath(softwarePath, groupPath)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Tag table group not found at '{groupPath}'");

            try
            {
                return group.TagTables.Create(name);
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Create tag table failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["groupPath"] = groupPath;
                pex.Data["name"] = name;
                _logger?.LogError(pex, "CreateTagTable failed for {SoftwarePath} {GroupPath} {Name}", softwarePath, groupPath, name);
                throw pex;
            }
        }

        public void DeleteTagTable(string softwarePath, string tagTablePath)
        {
            _logger?.LogInformation($"Deleting tag table: {tagTablePath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var table = GetTagTable(softwarePath, tagTablePath)
                ?? throw new PortalException(PortalErrorCode.NotFound, "Tag table not found");

            try
            {
                table.Delete();
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Delete tag table failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["tagTablePath"] = tagTablePath;
                _logger?.LogError(pex, "DeleteTagTable failed for {SoftwarePath} {TagTablePath}", softwarePath, tagTablePath);
                throw pex;
            }
        }

        // logicalAddress: pass null/empty to let TIA auto-assign the next free address.
        public PlcTag CreateTag(string softwarePath, string tagTablePath, string name, string dataType, string? logicalAddress = null)
        {
            _logger?.LogInformation($"Creating tag '{name}' in table '{tagTablePath}'");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var table = GetTagTable(softwarePath, tagTablePath)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Tag table not found at '{tagTablePath}'");

            try
            {
                return table.Tags.Create(name, dataType, logicalAddress ?? string.Empty);
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Create tag failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["tagTablePath"] = tagTablePath;
                pex.Data["name"] = name;
                _logger?.LogError(pex, "CreateTag failed for {SoftwarePath} {TagTablePath} {Name}", softwarePath, tagTablePath, name);
                throw pex;
            }
        }

        public void DeleteTag(string softwarePath, string tagTablePath, string tagName)
        {
            _logger?.LogInformation($"Deleting tag '{tagName}' from table '{tagTablePath}'");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var table = GetTagTable(softwarePath, tagTablePath)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Tag table not found at '{tagTablePath}'");

            var tag = table.Tags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase))
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Tag '{tagName}' not found in '{tagTablePath}'");

            try
            {
                tag.Delete();
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Delete tag failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["tagTablePath"] = tagTablePath;
                pex.Data["tagName"] = tagName;
                _logger?.LogError(pex, "DeleteTag failed for {SoftwarePath} {TagTablePath} {TagName}", softwarePath, tagTablePath, tagName);
                throw pex;
            }
        }

        public void SetTagAttribute(string softwarePath, string tagTablePath, string tagName, string attributeName, string value)
        {
            _logger?.LogInformation($"Setting tag attribute {attributeName} on '{tagName}' in '{tagTablePath}'");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var table = GetTagTable(softwarePath, tagTablePath)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Tag table not found at '{tagTablePath}'");

            var tag = table.Tags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase))
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Tag '{tagName}' not found in '{tagTablePath}'");

            try
            {
                var current = tag.GetAttribute(attributeName);
                tag.SetAttribute(attributeName, ConvertAttributeValue(current, value));
            }
            catch (Exception ex)
            {
                var pex = ex as PortalException ?? new PortalException(PortalErrorCode.ExportFailed, "Set tag attribute failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["tagTablePath"] = tagTablePath;
                pex.Data["tagName"] = tagName;
                pex.Data["attributeName"] = attributeName;
                _logger?.LogError(pex, "SetTagAttribute failed for {SoftwarePath} {TagTablePath} {TagName} {AttributeName}", softwarePath, tagTablePath, tagName, attributeName);
                throw pex;
            }
        }

        private PlcTagTableGroup? GetPlcTagTableGroupByPath(string softwarePath, string groupPath)
        {
            if (_project == null)
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                if (plcSoftware?.TagTableGroup == null)
                {
                    return null;
                }

                var groupNames = (groupPath ?? string.Empty).Split(['/'], StringSplitOptions.RemoveEmptyEntries);

                PlcTagTableGroup? currentGroup = plcSoftware.TagTableGroup;

                foreach (var groupName in groupNames)
                {
                    currentGroup = currentGroup.Groups.FirstOrDefault(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));

                    if (currentGroup == null)
                    {
                        return null;
                    }
                }

                return currentGroup;
            }

            return null;
        }

        #endregion

        #region block/type write CRUD

        // "Create with real content" is already covered by ImportBlock/ImportType (from XML) and
        // GenerateBlocksFromSource (from SCL text, any block/type kind) - deliberately not
        // duplicating that here with PlcBlockComposition.CreateFB/CreateInstanceDB (those make an
        // empty GUI-oriented block, less useful for a text-driven agent). What's new here: delete,
        // generic attribute modification (covers rename via the "Name" attribute plus anything
        // else ReadWrite), and group management.

        public void DeleteBlock(string softwarePath, string blockPath)
        {
            _logger?.LogInformation($"Deleting block: {blockPath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var block = GetBlock(softwarePath, blockPath)
                ?? throw new PortalException(PortalErrorCode.NotFound, "Block not found");

            try
            {
                block.Delete();
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Delete failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["blockPath"] = blockPath;
                _logger?.LogError(pex, "DeleteBlock failed for {SoftwarePath} {BlockPath}", softwarePath, blockPath);
                throw pex;
            }
        }

        public void DeleteType(string softwarePath, string typePath)
        {
            _logger?.LogInformation($"Deleting type: {typePath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var type = GetType(softwarePath, typePath)
                ?? throw new PortalException(PortalErrorCode.NotFound, "Type not found");

            try
            {
                type.Delete();
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Delete failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["typePath"] = typePath;
                _logger?.LogError(pex, "DeleteType failed for {SoftwarePath} {TypePath}", softwarePath, typePath);
                throw pex;
            }
        }

        // Sets any ReadWrite attribute (e.g. "Name" to rename, "Comment", "MemoryLayout", ...) -
        // converts the given string to match the attribute's current runtime type (bool/int/
        // uint/double/string) since Openness's SetAttribute takes a typed object, not a string.
        public void SetBlockAttribute(string softwarePath, string blockPath, string attributeName, string value)
        {
            _logger?.LogInformation($"Setting block attribute {attributeName} on: {blockPath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var block = GetBlock(softwarePath, blockPath)
                ?? throw new PortalException(PortalErrorCode.NotFound, "Block not found");

            try
            {
                var current = block.GetAttribute(attributeName);
                block.SetAttribute(attributeName, ConvertAttributeValue(current, value));
            }
            catch (Exception ex)
            {
                var pex = ex as PortalException ?? new PortalException(PortalErrorCode.ExportFailed, "Set attribute failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["blockPath"] = blockPath;
                pex.Data["attributeName"] = attributeName;
                _logger?.LogError(pex, "SetBlockAttribute failed for {SoftwarePath} {BlockPath} {AttributeName}", softwarePath, blockPath, attributeName);
                throw pex;
            }
        }

        public void SetTypeAttribute(string softwarePath, string typePath, string attributeName, string value)
        {
            _logger?.LogInformation($"Setting type attribute {attributeName} on: {typePath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var type = GetType(softwarePath, typePath)
                ?? throw new PortalException(PortalErrorCode.NotFound, "Type not found");

            try
            {
                var current = type.GetAttribute(attributeName);
                type.SetAttribute(attributeName, ConvertAttributeValue(current, value));
            }
            catch (Exception ex)
            {
                var pex = ex as PortalException ?? new PortalException(PortalErrorCode.ExportFailed, "Set attribute failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["typePath"] = typePath;
                pex.Data["attributeName"] = attributeName;
                _logger?.LogError(pex, "SetTypeAttribute failed for {SoftwarePath} {TypePath} {AttributeName}", softwarePath, typePath, attributeName);
                throw pex;
            }
        }

        private static object ConvertAttributeValue(object? current, string value)
        {
            return current switch
            {
                bool => bool.Parse(value),
                int => int.Parse(value),
                uint => uint.Parse(value),
                long => long.Parse(value),
                double => double.Parse(value),
                float => float.Parse(value),
                _ => value
            };
        }

        public PlcBlockUserGroup CreateBlockGroup(string softwarePath, string parentGroupPath, string name)
        {
            _logger?.LogInformation($"Creating block group '{name}' under '{parentGroupPath}'");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var parent = GetPlcBlockGroupByPath(softwarePath, parentGroupPath)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Block group not found at '{parentGroupPath}'");

            try
            {
                return parent.Groups.Create(name);
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Create block group failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["parentGroupPath"] = parentGroupPath;
                pex.Data["name"] = name;
                _logger?.LogError(pex, "CreateBlockGroup failed for {SoftwarePath} {ParentGroupPath} {Name}", softwarePath, parentGroupPath, name);
                throw pex;
            }
        }

        public void DeleteBlockGroup(string softwarePath, string groupPath)
        {
            _logger?.LogInformation($"Deleting block group: {groupPath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var group = GetPlcBlockGroupByPath(softwarePath, groupPath)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Block group not found at '{groupPath}'");

            if (group is not PlcBlockUserGroup userGroup)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "The root block group can't be deleted");
            }

            try
            {
                userGroup.Delete();
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Delete block group failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["groupPath"] = groupPath;
                _logger?.LogError(pex, "DeleteBlockGroup failed for {SoftwarePath} {GroupPath}", softwarePath, groupPath);
                throw pex;
            }
        }

        public PlcTypeUserGroup CreateTypeGroup(string softwarePath, string parentGroupPath, string name)
        {
            _logger?.LogInformation($"Creating type group '{name}' under '{parentGroupPath}'");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var parent = GetPlcTypeGroupByPath(softwarePath, parentGroupPath)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Type group not found at '{parentGroupPath}'");

            try
            {
                return parent.Groups.Create(name);
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Create type group failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["parentGroupPath"] = parentGroupPath;
                pex.Data["name"] = name;
                _logger?.LogError(pex, "CreateTypeGroup failed for {SoftwarePath} {ParentGroupPath} {Name}", softwarePath, parentGroupPath, name);
                throw pex;
            }
        }

        public void DeleteTypeGroup(string softwarePath, string groupPath)
        {
            _logger?.LogInformation($"Deleting type group: {groupPath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var group = GetPlcTypeGroupByPath(softwarePath, groupPath)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Type group not found at '{groupPath}'");

            if (group is not PlcTypeUserGroup userGroup)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "The root type group can't be deleted");
            }

            try
            {
                userGroup.Delete();
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Delete type group failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["groupPath"] = groupPath;
                _logger?.LogError(pex, "DeleteTypeGroup failed for {SoftwarePath} {GroupPath}", softwarePath, groupPath);
                throw pex;
            }
        }

        #endregion

        #region external sources (SCL import/export)

        // Import: PlcExternalSourceComposition.CreateFromFile(name, path) adds a raw source file
        // (.scl/.awl/...) into the project as a PlcExternalSource; GenerateBlocksFromSource then
        // compiles it into real blocks/types - this is the write step, it can create or overwrite
        // project blocks, same risk class as block/type write CRUD.
        // Export: PlcExternalSource itself has no Export() method (verified via reflection) - the
        // real export path is PlcExternalSourceSystemGroup.GenerateSource(blocks, FileInfo[,
        // GenerateOptions]), which takes existing PlcBlock/PlcType objects (both implement
        // IGenerateSource) and writes them out as combined SCL text - i.e. export goes through
        // blocks/types you already have, not through PlcExternalSource.

        public List<PlcExternalSource> GetExternalSources(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting external sources...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<PlcExternalSource>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is PlcSoftware plcSoftware)
                {
                    var rootGroup = plcSoftware?.ExternalSourceGroup;

                    if (rootGroup != null)
                    {
                        CollectExternalSourcesFromComposition(rootGroup.ExternalSources, list, regexName);

                        foreach (var subgroup in rootGroup.Groups)
                        {
                            GetExternalSourcesRecursive(subgroup, list, regexName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetExternalSources failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        public PlcExternalSource? GetExternalSource(string softwarePath, string sourcePath)
        {
            _logger?.LogInformation($"Getting external source by path: {sourcePath}");

            if (IsProjectNull())
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                var rootGroup = plcSoftware?.ExternalSourceGroup;
                if (rootGroup == null)
                {
                    return null;
                }

                var parts = sourcePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    return null;
                }

                var sourceName = parts[parts.Length - 1];

                if (parts.Length == 1)
                {
                    var found = rootGroup.ExternalSources.FirstOrDefault(s => s.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
                    if (found != null) return found;
                    return FindExternalSourceRecursive(rootGroup.Groups, sourceName);
                }
                else
                {
                    PlcExternalSourceUserGroup? current = rootGroup.Groups.FirstOrDefault(g => g.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                    for (int i = 1; i < parts.Length - 1 && current != null; i++)
                    {
                        current = current.Groups.FirstOrDefault(g => g.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                    }

                    if (current == null)
                    {
                        return null;
                    }

                    return current.ExternalSources.FirstOrDefault(s => s.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
                }
            }

            return null;
        }

        // Adds a local .scl/.awl/.gr7 file into the project as a named external source. Does not
        // touch any existing blocks/types by itself - GenerateBlocksFromSource is the step that does.
        public PlcExternalSource ImportExternalSource(string softwarePath, string groupPath, string importPath, string? sourceName = null)
        {
            _logger?.LogInformation($"Importing external source from: {importPath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is not PlcSoftware plcSoftware)
            {
                throw new PortalException(PortalErrorCode.NotFound, $"PLC software not found at '{softwarePath}'");
            }

            var group = GetPlcExternalSourceGroupByPath(softwarePath, groupPath);
            if (group == null)
            {
                throw new PortalException(PortalErrorCode.NotFound, $"External source group not found at '{groupPath}'");
            }

            var fileInfo = new FileInfo(importPath);
            if (!fileInfo.Exists)
            {
                throw new PortalException(PortalErrorCode.NotFound, $"Import file not found at '{importPath}'");
            }

            var name = string.IsNullOrWhiteSpace(sourceName) ? Path.GetFileNameWithoutExtension(importPath) : sourceName;

            try
            {
                return group.ExternalSources.CreateFromFile(name, importPath);
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Import failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["groupPath"] = groupPath;
                pex.Data["importPath"] = importPath;
                _logger?.LogError(pex, "ImportExternalSource failed for {SoftwarePath} {GroupPath} -> {ImportPath}", softwarePath, groupPath, importPath);
                throw pex;
            }
        }

        // The write step: compiles an already-imported external source into real project blocks/
        // types. keepOnError=true keeps whatever got generated even if some of it errored; the
        // default (false) rolls back (deletes generated blocks) on any generation error.
        public List<IEngineeringObject> GenerateBlocksFromSource(string softwarePath, string sourcePath, bool keepOnError = false)
        {
            _logger?.LogInformation($"Generating blocks from source: {sourcePath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var source = GetExternalSource(softwarePath, sourcePath)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"External source not found at '{sourcePath}' in '{softwarePath}'");

            try
            {
                var option = keepOnError ? GenerateBlockOption.KeepOnError : GenerateBlockOption.None;
                return source.GenerateBlocksFromSource(option).ToList();
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Block generation from source failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["sourcePath"] = sourcePath;
                _logger?.LogError(pex, "GenerateBlocksFromSource failed for {SoftwarePath} {SourcePath}", softwarePath, sourcePath);
                throw pex;
            }
        }

        public void DeleteExternalSource(string softwarePath, string sourcePath)
        {
            _logger?.LogInformation($"Deleting external source: {sourcePath}");

            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var source = GetExternalSource(softwarePath, sourcePath)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"External source not found at '{sourcePath}' in '{softwarePath}'");

            try
            {
                source.Delete();
            }
            catch (Exception ex)
            {
                var pex = new PortalException(PortalErrorCode.ExportFailed, "Delete failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["sourcePath"] = sourcePath;
                _logger?.LogError(pex, "DeleteExternalSource failed for {SoftwarePath} {SourcePath}", softwarePath, sourcePath);
                throw pex;
            }
        }

        // Exports existing blocks/types (already in the project) as combined SCL source text -
        // the real "export SCL" path, since PlcExternalSource itself can't export.
        public void ExportSourceFromBlocks(string softwarePath, IEnumerable<string> blockPaths, IEnumerable<string> typePaths, string exportPath, string fileName, bool withDependencies = false)
        {
            _logger?.LogInformation($"Exporting source from {blockPaths?.Count() ?? 0} block(s)/{typePaths?.Count() ?? 0} type(s)...");

            try
            {
                exportPath = OutputPathPolicy.ResolveDirectory(exportPath);

                if (IsProjectNull())
                {
                    throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
                }

                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is not PlcSoftware plcSoftware || plcSoftware.ExternalSourceGroup == null)
                {
                    throw new PortalException(PortalErrorCode.NotFound, $"PLC software not found at '{softwarePath}'");
                }

                var objects = new List<IGenerateSource>();

                foreach (var blockPath in blockPaths ?? [])
                {
                    var block = GetBlock(softwarePath, blockPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound, $"Block not found at '{blockPath}'");
                    objects.Add(block);
                }

                foreach (var typePath in typePaths ?? [])
                {
                    var type = GetType(softwarePath, typePath)
                        ?? throw new PortalException(PortalErrorCode.NotFound, $"Type not found at '{typePath}'");
                    objects.Add(type);
                }

                if (objects.Count == 0)
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, "No blocks or types given to export as source");
                }

                var resolvedFile = Path.Combine(exportPath, $"{SanitizeFileName(fileName)}.scl");
                if (File.Exists(resolvedFile))
                {
                    File.Delete(resolvedFile);
                }

                var options = withDependencies ? GenerateOptions.WithDependencies : GenerateOptions.None;
                plcSoftware.ExternalSourceGroup.GenerateSource(objects, new FileInfo(resolvedFile), options);
            }
            catch (Exception ex)
            {
                var pex = ex as PortalException ?? new PortalException(PortalErrorCode.ExportFailed, "Export failed", null, ex);
                pex.Data["softwarePath"] = softwarePath;
                pex.Data["exportPath"] = exportPath;
                _logger?.LogError(pex, "ExportSourceFromBlocks failed for {SoftwarePath} -> {ExportPath}", softwarePath, exportPath);
                throw pex;
            }
        }

        private PlcExternalSourceGroup? GetPlcExternalSourceGroupByPath(string softwarePath, string groupPath)
        {
            if (_project == null)
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is PlcSoftware plcSoftware)
            {
                if (plcSoftware?.ExternalSourceGroup == null)
                {
                    return null;
                }

                var groupNames = (groupPath ?? string.Empty).Split(['/'], StringSplitOptions.RemoveEmptyEntries);

                PlcExternalSourceGroup? currentGroup = plcSoftware.ExternalSourceGroup;

                foreach (var groupName in groupNames)
                {
                    currentGroup = currentGroup.Groups.FirstOrDefault(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));

                    if (currentGroup == null)
                    {
                        return null;
                    }
                }

                return currentGroup;
            }

            return null;
        }

        private PlcExternalSource? FindExternalSourceRecursive(PlcExternalSourceUserGroupComposition groups, string sourceName)
        {
            foreach (var group in groups)
            {
                var found = group.ExternalSources.FirstOrDefault(s => s.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
                found = FindExternalSourceRecursive(group.Groups, sourceName);
                if (found != null) return found;
            }
            return null;
        }

        private void CollectExternalSourcesFromComposition(PlcExternalSourceComposition sources, List<PlcExternalSource> list, string regexName)
        {
            foreach (var source in sources)
            {
                try
                {
                    if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(source.Name, regexName, RegexOptions.IgnoreCase))
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                list.Add(source);
            }
        }

        private void GetExternalSourcesRecursive(PlcExternalSourceUserGroup group, List<PlcExternalSource> list, string regexName = "")
        {
            CollectExternalSourcesFromComposition(group.ExternalSources, list, regexName);

            foreach (var subgroup in group.Groups)
            {
                GetExternalSourcesRecursive(subgroup, list, regexName);
            }
        }

        #endregion

        #region hmi tag tables

        // Unified Comfort/Advanced Panels only (Siemens.Engineering.HmiUnified.HmiTags) - classic
        // WinCC Comfort/Basic panels use a different, older API (Siemens.Engineering.Hmi.Tag) not
        // covered here since this project hasn't needed it yet. Read-only: HmiTagTable has no
        // Export() method in this Openness version (unlike PlcTagTable/classic Hmi.Tag.TagTable),
        // so there is no ExportHmiTagTable - verified empirically, not just undocumented.

        public List<HmiTagTable> GetHmiTagTables(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting HMI tag tables...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiTagTable>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is HmiSoftware hmiSoftware)
                {
                    CollectHmiTagTablesFromComposition(hmiSoftware.TagTables, list, regexName);

                    foreach (var subgroup in hmiSoftware.TagTableGroups)
                    {
                        GetHmiTagTablesRecursive(subgroup, list, regexName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetHmiTagTables failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        public HmiTagTable? GetHmiTagTable(string softwarePath, string tagTablePath)
        {
            _logger?.LogInformation($"Getting HMI tag table by path: {tagTablePath}");

            if (IsProjectNull())
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(tagTablePath))
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is HmiSoftware hmiSoftware)
            {
                var parts = tagTablePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    return null;
                }

                var tableName = parts[parts.Length - 1];

                if (parts.Length == 1)
                {
                    // Search root-level tables first, then recurse into groups
                    var found = hmiSoftware.TagTables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                    if (found != null) return found;
                    return FindHmiTagTableRecursive(hmiSoftware.TagTableGroups, tableName);
                }
                else
                {
                    HmiTagTableGroup? current = hmiSoftware.TagTableGroups.FirstOrDefault(g => g.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                    for (int i = 1; i < parts.Length - 1 && current != null; i++)
                    {
                        current = current.Groups.FirstOrDefault(g => g.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                    }

                    if (current == null)
                    {
                        return null;
                    }

                    return current.TagTables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                }
            }

            return null;
        }

        public List<HmiTag> GetHmiTags(string softwarePath, string tagTablePath, string regexName = "")
        {
            _logger?.LogInformation($"Getting HMI tags for table: {tagTablePath}");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiTag>();

            try
            {
                var table = GetHmiTagTable(softwarePath, tagTablePath);
                if (table != null)
                {
                    foreach (var tag in table.Tags)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(tag.Name, regexName, RegexOptions.IgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        list.Add(tag);
                    }
                }
            }
            catch (Exception)
            {
                // Same swallow style as GetTags (PLC)
            }

            return list;
        }

        private HmiTagTable? FindHmiTagTableRecursive(HmiTagTableGroupComposition groups, string tableName)
        {
            foreach (var group in groups)
            {
                var found = group.TagTables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
                found = FindHmiTagTableRecursive(group.Groups, tableName);
                if (found != null) return found;
            }
            return null;
        }

        private void CollectHmiTagTablesFromComposition(HmiTagTableComposition tables, List<HmiTagTable> list, string regexName)
        {
            foreach (var table in tables)
            {
                try
                {
                    if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(table.Name, regexName, RegexOptions.IgnoreCase))
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                list.Add(table);
            }
        }

        private void GetHmiTagTablesRecursive(HmiTagTableGroup group, List<HmiTagTable> list, string regexName = "")
        {
            CollectHmiTagTablesFromComposition(group.TagTables, list, regexName);

            foreach (var subgroup in group.Groups)
            {
                GetHmiTagTablesRecursive(subgroup, list, regexName);
            }
        }

        #endregion

        #region hmi screens/alarms/text lists (Unified Comfort/Advanced Panels only)

        // Same Unified-only scope as the hmi tag tables region above. All read-only - no write
        // API found for any of these (not investigated as deeply as tag tables since there's no
        // stated need for it yet).

        public List<HmiScreen> GetHmiScreens(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting HMI screens...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiScreen>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is HmiSoftware hmiSoftware)
                {
                    CollectHmiScreensFromComposition(hmiSoftware.Screens, list, regexName);

                    foreach (var subgroup in hmiSoftware.ScreenGroups)
                    {
                        GetHmiScreensRecursive(subgroup, list, regexName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetHmiScreens failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        private void CollectHmiScreensFromComposition(HmiScreenComposition screens, List<HmiScreen> list, string regexName)
        {
            foreach (var screen in screens)
            {
                try
                {
                    if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(screen.Name, regexName, RegexOptions.IgnoreCase))
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                list.Add(screen);
            }
        }

        private void GetHmiScreensRecursive(HmiScreenGroup group, List<HmiScreen> list, string regexName = "")
        {
            CollectHmiScreensFromComposition(group.Screens, list, regexName);

            foreach (var subgroup in group.Groups)
            {
                GetHmiScreensRecursive(subgroup, list, regexName);
            }
        }

        public List<HmiDiscreteAlarm> GetHmiDiscreteAlarms(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting HMI discrete alarms...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiDiscreteAlarm>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is HmiSoftware hmiSoftware)
                {
                    foreach (var alarm in hmiSoftware.DiscreteAlarms)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(alarm.Name, regexName, RegexOptions.IgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        list.Add(alarm);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetHmiDiscreteAlarms failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        public List<HmiAnalogAlarm> GetHmiAnalogAlarms(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting HMI analog alarms...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiAnalogAlarm>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is HmiSoftware hmiSoftware)
                {
                    foreach (var alarm in hmiSoftware.AnalogAlarms)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(alarm.Name, regexName, RegexOptions.IgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        list.Add(alarm);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetHmiAnalogAlarms failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        // Lists text list names only - Openness in this version has no type exposing individual
        // text list entries/values (verified: no HmiTextListEntry-shaped type exists in the
        // installed Siemens.Engineering.dll), so entry contents aren't reachable via this tool.
        public List<HmiTextList> GetHmiTextLists(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting HMI text lists...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiTextList>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is HmiSoftware hmiSoftware)
                {
                    foreach (var textList in hmiSoftware.HmiTextLists)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(textList.Name, regexName, RegexOptions.IgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        list.Add(textList);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetHmiTextLists failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        #endregion

        #endregion

    }


}
