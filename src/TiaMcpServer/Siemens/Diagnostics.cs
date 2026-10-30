using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// One installed TIA Portal major version, as discovered in the registry.
    /// </summary>
    public sealed class TiaInstallation
    {
        public int MajorVersion { get; set; }
        public string? InstallPath { get; set; }
        public bool EngineeringExists { get; set; }
        public bool PortalExeExists { get; set; }
    }

    /// <summary>
    /// Result of a 'doctor' run: structured findings plus a human readable tree.
    /// </summary>
    public sealed class DiagnosticsReport
    {
        /// <summary>Assembly version of TiaMcpServer.exe, e.g. "0.2.0.0".</summary>
        public string? ServerVersion { get; set; }
        public bool IsConnected { get; set; }
        public int ActiveTiaMajorVersion { get; set; }
        public string? ProjectName { get; set; }
        public string? ProjectPath { get; set; }
        public bool IsUserInGroup { get; set; }

        /// <summary>Whether the server was started with --allow-write.</summary>
        public bool AllowWrite { get; set; }
        public IReadOnlyList<TiaInstallation> Installations { get; set; } = new List<TiaInstallation>();

        /// <summary>Openness clients attached to the running TIA Portal instances, this server included.</summary>
        public IReadOnlyList<OpennessSessionInfo> OpennessSessions { get; set; } = new List<OpennessSessionInfo>();

        /// <summary>Products and option packages of the attached instance, "Name Version" each.</summary>
        public IReadOnlyList<string> InstalledProducts { get; set; } = new List<string>();
        public string? Text { get; set; }
    }

    /// <summary>
    /// Environment diagnostics for the TIA Portal MCP server. Read-only: nothing here connects,
    /// opens a project, or changes group membership.
    /// </summary>
    public static class Diagnostics
    {
        // Openness of this server targets V20+ (one build per version, see TiaMcpServer.csproj).
        private const int MinTiaMajorVersion = 20;
        private const int MaxTiaMajorVersion = 30;

        // V20+ splits Openness into Siemens.Engineering.Base.dll and friends; older installations
        // ship a single Siemens.Engineering.dll. Either one counts as a usable Openness API.
        private static readonly string[] EngineeringAssemblyNames =
        {
            "Siemens.Engineering.Base.dll",
            "Siemens.Engineering.dll"
        };

        // The main executable is named Siemens.Automation.Portal.exe; 'Portal.exe' is a legacy name.
        private static readonly string[] PortalExecutableNames =
        {
            "Siemens.Automation.Portal.exe",
            "Portal.exe"
        };

        /// <summary>
        /// Assembly version of TiaMcpServer.exe (AssemblyVersion in TiaMcpServer.csproj). Also
        /// reported to clients as ServerInfo.Version, so both always show the same value.
        /// </summary>
        public static string ServerVersion =>
            typeof(Diagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        public static DiagnosticsReport Run(Portal portal, bool allowWrite = false)
        {
            if (portal == null)
            {
                throw new ArgumentNullException(nameof(portal));
            }

            var isConnected = GetIsConnected(portal);
            var (projectName, projectPath) = GetProject(portal);
            var installations = GetInstalledTiaPortalVersions();
            var userInGroup = GetUserInGroup();
            var sessions = GetOpennessSessions(portal);
            var products = GetInstalledProducts(portal);

            var serverVersion = ServerVersion;

            var status = $"Diagnose:";

            status += $"\n├─ Server: TiaMcpServer {serverVersion}";
            status += $"\n├─ Connected = {isConnected}";

            if (projectName != null || projectPath != null)
            {
                status += $"\n├─ Project: {projectName ?? "Unknown"}, {projectPath ?? "Unknown"}";
            }
            else
            {
                status += $"\n├─ Project: No project open";
            }

            status += $"\n├─ Active Version: V{Openness.TiaMajorVersion} (exe built for V{Openness.BuildTiaMajorVersion} Openness)";

            if (installations.Count > 0)
            {
                status += $"\n├─ Installed TIA Portal versions:";
                for (int i = 0; i < installations.Count; i++)
                {
                    var inst = installations[i];
                    var isLast = i == installations.Count - 1;
                    var prefix = isLast ? "│  └─" : "│  ├─";
                    status += $"\n{prefix} V{inst.MajorVersion}: {inst.InstallPath}";
                    status += $"\n{(isLast ? "│   " : "│  │")}  ├─ Engineering: {(inst.EngineeringExists ? "OK" : "missing")}";
                    status += $"\n{(isLast ? "│   " : "│  │")}  └─ Portal:      {(inst.PortalExeExists ? "OK" : "missing")}";
                }
            }
            else
            {
                status += $"\n├─ Installed TIA Portal versions: none found (>= V{MinTiaMajorVersion})";
            }

            var others = sessions.Where(s => !s.IsThisServer).ToList();
            if (others.Count > 0)
            {
                status += $"\n├─ Other Openness clients attached ({others.Count}):";
                for (int i = 0; i < others.Count; i++)
                {
                    var other = others[i];
                    var prefix = i == others.Count - 1 ? "│  └─" : "│  ├─";
                    status += $"\n{prefix} TIA {other.PortalId}: {other.ProcessPath ?? "unknown"} (pid {other.ProcessId?.ToString() ?? "?"}, since {other.AttachTime?.ToString("yyyy-MM-dd HH:mm") ?? "?"})";
                }
            }
            else
            {
                status += $"\n├─ Other Openness clients attached: none";
            }

            if (products.Count > 0)
            {
                status += $"\n├─ Installed products: {string.Join("; ", products)}";
            }

            status += $"\n├─ User in 'Siemens TIA Openness' user group: {userInGroup}";
            status += $"\n└─ Write mode (--allow-write): {(allowWrite ? "enabled" : "disabled, read-only tools only")}";

            return new DiagnosticsReport
            {
                ServerVersion = serverVersion,
                IsConnected = isConnected,
                ActiveTiaMajorVersion = Openness.TiaMajorVersion,
                ProjectName = projectName,
                ProjectPath = projectPath,
                IsUserInGroup = userInGroup,
                AllowWrite = allowWrite,
                Installations = installations,
                OpennessSessions = sessions,
                InstalledProducts = products,
                Text = status
            };
        }

        /// <summary>
        /// Discovers all installed TIA Portal versions >= V20 and checks whether the Openness
        /// assembly and Portal.exe are actually present in each installation.
        /// </summary>
        public static IReadOnlyList<TiaInstallation> GetInstalledTiaPortalVersions()
        {
            var installations = new List<TiaInstallation>();

            for (var majorVersion = MinTiaMajorVersion; majorVersion <= MaxTiaMajorVersion; majorVersion++)
            {
                string? installPath;
                try
                {
                    installPath = Engineering.GetTiaPortalInstallPath(majorVersion);
                }
                catch (Exception)
                {
                    // Registry hive unreadable for this version - report it as not installed.
                    continue;
                }

                if (string.IsNullOrEmpty(installPath))
                {
                    continue;
                }

                installations.Add(new TiaInstallation
                {
                    MajorVersion = majorVersion,
                    InstallPath = installPath,
                    EngineeringExists = EngineeringAssemblyExists(installPath!, majorVersion),
                    PortalExeExists = PortalExecutableExists(installPath!)
                });
            }

            return installations;
        }

        private static bool GetIsConnected(Portal portal)
        {
            try
            {
                return portal.IsConnected();
            }
            catch (Exception)
            {
                // A faulted TIA Portal instance must not break the report.
                return false;
            }
        }

        private static (string? Name, string? Path) GetProject(Portal portal)
        {
            object? info;
            try
            {
                info = portal.GetProjectInfo();
            }
            catch (Exception)
            {
                // Reading a live project can throw through Openness - report 'no project' instead.
                return (null, null);
            }

            if (info == null)
            {
                return (null, null);
            }

            // GetProjectInfo returns an anonymous type, so read it via reflection.
            var nameProp = info.GetType().GetProperty("Name");
            var pathProp = info.GetType().GetProperty("Path");

            return (
                nameProp?.GetValue(info)?.ToString(),
                pathProp?.GetValue(info)?.ToString());
        }

        private static IReadOnlyList<OpennessSessionInfo> GetOpennessSessions(Portal portal)
        {
            try
            {
                return portal.GetOpennessSessions();
            }
            catch (Exception)
            {
                // No TIA Portal running, or Openness not initialized - nothing to list.
                return new List<OpennessSessionInfo>();
            }
        }

        private static IReadOnlyList<string> GetInstalledProducts(Portal portal)
        {
            try
            {
                return portal.GetInstalledProducts();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        private static bool GetUserInGroup()
        {
            try
            {
                return Openness.CheckUserInGroup();
            }
            catch (Exception)
            {
                // Openness not initialized or not installed - treat as 'not a member'.
                return false;
            }
        }

        private static bool EngineeringAssemblyExists(string installPath, int majorVersion)
        {
            try
            {
                return EngineeringAssemblyNames
                    .Any(name => Engineering.FindAssembly(installPath, majorVersion, name) != null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool PortalExecutableExists(string installPath)
        {
            try
            {
                return PortalExecutableNames
                    .Any(name => File.Exists(Path.Combine(installPath, "Bin", name)));
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
