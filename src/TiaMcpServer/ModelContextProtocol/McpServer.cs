using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    [McpServerToolType]
    public static partial class McpServer
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

        [McpServerTool(Name = "Connect", Title = "Connect to TIA Portal", Destructive = false, Idempotent = true, OpenWorld = false),
         Description("Use first in every session. Connect to TIA Portal and return its 'portalId'. Without 'portalId' it attaches to the only running instance, or starts one if none is running; with several running it fails and lists them")]
        public static ResponseConnect Connect(
            [Description("portalId: process id of the TIA Portal instance to attach to (see 'GetPortals'). Leave empty when at most one instance is running")] int? portalId = null)
        {
            Logger?.LogInformation("Connecting to TIA Portal...");

            try
            {
                var attachedId = Portal.AttachPortal(portalId);

                return new ResponseConnect
                {
                    Message = $"Connected to TIA-Portal instance {attachedId}" +
                              (VersionInfo.StaleBuildHint() is { } stale ? ". " + stale : string.Empty),
                    PortalId = attachedId,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(PortalSelection.Describe(pex), pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error connecting to TIA-Portal: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "Disconnect", Title = "Disconnect from TIA Portal", Destructive = false, Idempotent = true, OpenWorld = false), Description("Disconnect from a TIA Portal instance (the one named by portalId, or the only attached one). Other attached instances stay connected")]
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

        [PortalIndependent]
        [McpServerTool(Name = "GetPortals", Title = "Get portals", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("List the running TIA Portal instances with their 'portalId', open project path and whether this server is attached. The top-level 'portalId' is the attached instance tools use by default (empty when none or several are attached). Use it to choose the 'portalId' when more than one instance is running")]
        public static ResponsePortals GetPortals()
        {
            try
            {
                var portals = Portal.GetPortals();

                // the one instance tools use without 'portalId'; none or several attached: no default
                var attached = portals.Where(p => p.IsAttached).Select(p => p.PortalId).ToList();
                int? portalId = attached.Count == 1 ? attached[0] : null;

                return new ResponsePortals
                {
                    Message = $"{portals.Count} TIA-Portal instance(s) running" +
                              (portalId is int id ? $", attached to {id}" : attached.Count > 1 ? $", {attached.Count} attached" : ", none attached"),
                    PortalId = portalId,
                    Portals = portals,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing TIA-Portal instances: {ex.Message}", ex);
            }
        }

        #endregion

        #region state

        [McpServerTool(Name = "GetState", Title = "Get server state", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Show the portalId this server is attached to, whether it is connected, the open project and session, and whether write mode (--allow-write) is enabled. Also reports 'progress' while a long-running call (e.g. FindInCode) is in flight on this instance - call this from a second request to check whether such a call looks stuck or just slow, without waiting for it to finish. Declares 'portalId' itself and never waits for a busy instance, unlike other tools")]
        public static ResponseState GetState(
            [Description("portalId: process id of the TIA Portal instance to report on (see 'GetPortals'). Leave empty when at most one instance is attached")] int? portalId = null)
        {
            try
            {
                var state = Portal.GetState(portalId);

                if (state != null)
                {
                    return new ResponseState
                    {
                        Message = "TIA-Portal MCP server state retrieved",
                        PortalId = state.PortalId,
                        IsConnected = state.IsConnected,
                        Project = state.Project,
                        Session = state.Session,
                        AllowWrite = WritePolicy.AllowWrite,
                        Presets = ToolDefaults.Presets.Count == 0
                            ? null
                            : new JsonObject(ToolDefaults.Presets.Select(p =>
                                new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(p.Value)))),
                        Progress = state.Progress == null
                            ? null
                            : new ResponseOperationProgress
                            {
                                Tool = state.Progress.Tool,
                                Total = state.Progress.Total,
                                Processed = state.Progress.Processed,
                                CurrentPath = state.Progress.CurrentPath,
                                ElapsedSeconds = (DateTime.Now - state.Progress.StartedAt).TotalSeconds,
                                LastProgressSecondsAgo = (DateTime.Now - state.Progress.LastProgressAt).TotalSeconds,
                                Summary = state.Progress.Describe()
                            },
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
            catch (PortalException pex)
            {
                throw new McpException(PortalSelection.Describe(pex), pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving TIA-Portal MCP server state: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "Doctor", Title = "Run Doctor diagnostics", ReadOnly = true, OpenWorld = true, UseStructuredContent = true), Description("Diagnose the TIA Portal environment: server version, connection, open project, active and installed TIA Portal versions, Openness user group membership. Also reports which git commit this running exe was actually built from and - unless checkRemote=false - whether the source tree has moved on since (needs rebuilding) or is behind its branch on GitHub (needs a pull); a quick 'git ls-remote', no fetch/pull. Added after a rebuilt exe was confirmed to silently keep serving old code with no way to tell from outside it.")]
        public static ResponseDoctor Doctor(
            [Description("checkRemote: also check origin/main on GitHub via 'git ls-remote' (default true). Set false to stay offline/local-only.")] bool checkRemote = true)
        {
            Logger?.LogInformation("Running doctor diagnostics...");

            try
            {
                // Fully qualified: 'Diagnostics' alone would collide with the System.Diagnostics namespace.
                var report = TiaMcpServer.Siemens.Diagnostics.Run(Portal, WritePolicy.AllowWrite);
                var v = TiaMcpServer.VersionInfo.Get(checkRemote);

                return new ResponseDoctor
                {
                    Message = "TIA-Portal environment diagnosed",
                    Report = report.Text,
                    ServerVersion = report.ServerVersion,
                    IsConnected = report.IsConnected,
                    ActiveTiaMajorVersion = report.ActiveTiaMajorVersion,
                    ProjectName = report.ProjectName,
                    ProjectPath = report.ProjectPath,
                    IsUserInGroup = report.IsUserInGroup,
                    AllowWrite = report.AllowWrite,
                    Installations = report.Installations
                        .Select(i => new ResponseTiaInstallation
                        {
                            MajorVersion = i.MajorVersion,
                            InstallPath = i.InstallPath,
                            EngineeringExists = i.EngineeringExists,
                            PortalExeExists = i.PortalExeExists
                        })
                        .ToList(),
                    BuiltCommit = v.BuiltCommit,
                    BuiltCommitShort = v.BuiltCommitShort,
                    BuiltBranch = v.BuiltBranch,
                    BuiltCommitDate = v.BuiltCommitDate,
                    BuiltDirty = v.BuiltDirty,
                    CurrentSourceCommit = v.CurrentSourceCommit,
                    CurrentSourceDirty = v.CurrentSourceDirty,
                    BuiltMatchesCurrentSource = v.BuiltMatchesCurrentSource,
                    RemoteMainCommit = v.RemoteMainCommit,
                    CurrentSourceMatchesRemote = v.CurrentSourceMatchesRemote,
                    RemoteCheckError = v.RemoteCheckError,
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

        [McpServerTool(Name = "GetProject", Title = "Get open project", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("List the projects and local sessions open in the connected TIA Portal, with name and path")]
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

        [McpServerTool(Name = "OpenProject", Title = "Open project/session", Destructive = true, Idempotent = true, OpenWorld = false), Description("Open a local project (.apXX) or local session (.alsXX) in the connected TIA Portal and make it the current project. Closes the project or session that is open first. Needs a prior Connect; OpenTiaProject connects and opens in one call")]
        public static ResponseOpenProject OpenProject(
            [Description("path: full path of the .apXX project or .alsXX session file on the machine running this server")] string path)
        {
            try
            {
                // get project extension
                string extension = Path.GetExtension(path).ToLowerInvariant();

                // use regex to check if extension is .ap\d+ or .als\d+
                if (!Regex.IsMatch(extension, @"^\.ap\d+$") &&
                    !Regex.IsMatch(extension, @"^\.als\d+$"))
                {
                    throw new McpException("Invalid project file extension. Use .apXX for projects or .alsXX for sessions, where XX=18,19,20,....");
                }

                bool success = false;
                bool wasAlreadyOpen = false;

                // An already open project or session is used as it is; the current one is only
                // closed when another has to be opened (Portal.OpenProject/OpenSession decide).
                if (extension.StartsWith(".ap"))
                {
                    success = Portal.OpenProject(path, out wasAlreadyOpen);
                }
                if (extension.StartsWith(".als"))
                {
                    Portal.CloseProject();
                    success = Portal.OpenSession(path, out wasAlreadyOpen);
                }

                if (success)
                {
                    return new ResponseOpenProject
                    {
                        Message = wasAlreadyOpen
                            ? $"Project '{path}' was already open and is used"
                            : $"Project '{path}' opened",
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

        [McpServerTool(Name = "SaveProject", Title = "Save project", Destructive = true, Idempotent = true, OpenWorld = false), Description("Use only when the user wants the changes kept. Save the current local project or local session")]
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

        [McpServerTool(Name = "SaveAsProject", Title = "Save project as", Destructive = true, Idempotent = true, OpenWorld = false), Description("Save the current local project to a new location. Not possible for local sessions")]
        public static ResponseSaveAsProject SaveAsProject(
            [Description("newProjectPath: target directory on the machine running this server that receives the saved project")] string newProjectPath)
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

        [McpServerTool(Name = "CloseProject", Title = "Close project", Destructive = true, Idempotent = true, OpenWorld = false), Description("Close the current local project or local session. Save first to keep changes")]
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

        [McpServerTool(Name = "GetProjectTree", Title = "Get project tree", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Use right after Connect to learn the device and software paths (e.g. 'PLC_1'). Show the structure of the current project or session as a tree of devices, device items, groups and PLC/HMI software. Use it to find the device and software paths other tools expect")]
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

        #endregion

        #region lookup

        [McpServerTool(Name = "OpenTiaProject", Title = "Connect/open a project", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Connect to TIA Portal if not already connected, open the given project or session, and return the 'portalId' plus the device and PLC software paths the other tools need. Replaces the Connect then OpenProject then GetProjectTree sequence")]
        public static async Task<ResponseOpenTiaProject> OpenTiaProject(
            [Description("path: full path of the .apXX project or .alsXX session file on the machine running this server")] string path,
            [Description("portalId: process id of the TIA Portal instance to open it in (see 'GetPortals'). Leave empty when at most one instance is running")] int? portalId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                int attachedId;
                bool wasAttached;

                try
                {
                    // Attaching is unscoped, as in 'Connect': the instance may not be attached yet,
                    // which is why this tool declares 'portalId' itself and bypasses the filter.
                    attachedId = Portal.AttachPortal(portalId, out wasAttached);
                }
                catch (Exception ex) when (ex is not PortalException)
                {
                    throw new McpException(
                        "Failed to connect to TIA Portal. Run the 'Doctor' tool to check the installation and user group membership.", ex);
                }

                // Everything after attaching runs scoped to that instance and under its lock.
                return await Portal.RunScopedAsync(
                    attachedId,
                    () => new ValueTask<ResponseOpenTiaProject>(OpenAndDescribe(path, attachedId, wasAttached)),
                    cancellationToken);
            }
            catch (PortalException pex)
            {
                throw new McpException(PortalSelection.Describe(pex), pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error opening '{path}': {ex.Message}", ex);
            }
        }

        private static ResponseOpenTiaProject OpenAndDescribe(string path, int portalId, bool wasAttached)
        {
            // Reuse the existing tool rather than duplicating its extension validation and
            // project/session branching; it also closes whatever was open first.
            var opened = OpenProject(path);

            var softwarePaths = CollectSoftwarePaths();

            return new ResponseOpenTiaProject
            {
                Message = $"{opened.Message} in TIA-Portal instance {portalId}. PLC software: " +
                          (softwarePaths.Count > 0 ? string.Join(", ", softwarePaths) : "none found"),
                PortalId = portalId,
                ProjectPath = path,
                WasAlreadyConnected = wasAttached,
                SoftwarePaths = softwarePaths,
                Tree = Portal.GetProjectTree(),
                Meta = Ok(new JsonObject { ["softwareCount"] = softwarePaths.Count })
            };
        }

        #endregion

        // From the former McpServer.Preview.cs:
        // Looking before writing.
        //
        // Callers: the MCP host, through tool registration. Affected API: none - PreviewImport is
        // new and no write tool changed. File I/O: reads the caller's import directory; writes
        // nothing, and never touches the project.
        //
        // This is the read-only half of write safety; the other half is the transaction wrapper in
        // Portal.cs (InTransaction), which every write tool now runs inside. It is one tool rather
        // than a 'dryRun' flag on all 39 write tools: the flag would have changed the execution
        // path of every write for a report that only imports really need, and imports are where
        // the collisions actually happen.
        //
        // What it can and cannot know: the object name is taken from the file name, which is how
        // every exporter in this server names its output. A hand-edited file whose content
        // declares a different name than its file name would be reported under the file name.

        #region preview

        [McpServerTool(Name = "PreviewImport", Title = "Preview import", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("Report what importing a directory would create, overwrite or collide with, without touching the project. Checks each file against the objects already in the PLC, including the rule that a PLC data type name must be unique across the whole PLC - importing an existing type name into a different group fails even with importOption 'Override'")]
        public static ResponseImportPreview PreviewImport(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("importPath: directory holding the files to import (.s7dcl source documents or .xml)")] string importPath,
            [Description("kind: what the files contain - 'type' for PLC data types, 'block' for program blocks")] string kind,
            [Description("groupPath: the group the import would target; empty means the root of that area. A leading system folder segment is accepted")] string groupPath = "")
        {
            try
            {
                if (!Directory.Exists(importPath))
                {
                    throw new McpException($"Import directory '{importPath}' does not exist.");
                }

                var isType = kind.Equals("type", StringComparison.OrdinalIgnoreCase);

                if (!isType && !kind.Equals("block", StringComparison.OrdinalIgnoreCase))
                {
                    throw new McpException($"Unknown kind '{kind}'. Use 'type' or 'block'.");
                }

                // An .xml file names its object inside; the file name may differ, since exports
                // replace characters such as '>' or '/' that a file name cannot hold.
                var files = Directory
                    .GetFiles(importPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => PreviewExtensions.Contains(Path.GetExtension(f)))
                    .Select(f => (File: f, Name: ObjectNameOf(f)))
                    .Where(x => !string.IsNullOrEmpty(x.Name))
                    .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.File).ToList(), StringComparer.OrdinalIgnoreCase);

                var names = files.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

                var target = string.IsNullOrEmpty(groupPath) ? "the root" : groupPath;
                var items = new List<ResponseImportPreviewItem>();

                foreach (var name in names)
                {
                    var existing = Portal.ResolveObjectPath(softwarePath, name, isType ? "type" : "block")
                        .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        items.Add(new ResponseImportPreviewItem { Name = name, Effect = "create", TargetPath = Join(groupPath, name) });

                        continue;
                    }

                    var existingGroup = GroupOf(existing.Path, existing.Name);

                    var sameGroup = string.Equals(
                        existingGroup,
                        StripLeadingSystemFolder(groupPath, isType),
                        StringComparison.OrdinalIgnoreCase);

                    items.Add(new ResponseImportPreviewItem
                    {
                        Name = name,
                        Effect = sameGroup ? "overwrite" : (isType ? "conflict" : "overwrite-elsewhere"),
                        TargetPath = Join(groupPath, name),
                        ExistingPath = existing.Path,
                        Note = sameGroup
                            ? "Already exists in the target group; importOption 'Override' replaces it."
                            : isType
                                ? "A PLC data type name must be unique across the whole PLC. This import fails even with " +
                                  $"'Override' - target '{existingGroup}' instead, or rename the type."
                                : $"A block of this name already exists at '{existing.Path}'; importing here may collide on the block number."
                    });
                }

                // REQUESTS.md P12-2: check the SimaticML files themselves for what TIA Portal rejects
                // with no useful reason - found by bisecting a failed import on 2026-10-12.
                foreach (var item in items)
                {
                    var problems = files[item.Name!]
                        .Where(f => Path.GetExtension(f).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                        .SelectMany(CheckSimaticMl)
                        .ToList();

                    item.Problems = problems.Count > 0 ? problems : null;
                }

                var problemFiles = items.Count(i => i.Problems != null);
                var creates = items.Count(i => i.Effect == "create");
                var overwrites = items.Count(i => i.Effect == "overwrite");
                var conflicts = items.Count(i => i.Effect == "conflict" || i.Effect == "overwrite-elsewhere");

                return new ResponseImportPreview
                {
                    Message = $"Importing '{importPath}' into {target} would create {creates}, overwrite {overwrites}, " +
                              $"and hit {conflicts} conflict(s)." +
                              (problemFiles > 0 ? $" WARNING: {problemFiles} file(s) have problems TIA Portal will reject - see 'problems'." : string.Empty) +
                              " Nothing was changed.",
                    Items = items,
                    CreateCount = creates,
                    OverwriteCount = overwrites,
                    ConflictCount = conflicts,
                    ProblemFileCount = problemFiles,
                    Meta = Ok(new JsonObject
                    {
                        ["files"] = names.Count,
                        ["create"] = creates,
                        ["overwrite"] = overwrites,
                        ["conflict"] = conflicts
                    })
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error previewing the import of '{importPath}': {ex.Message}", ex);
            }
        }

        /// <summary>The object name inside a SimaticML file, else the file name without extension.</summary>
        private static string ObjectNameOf(string file)
        {
            if (Path.GetExtension(file).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var name = System.Xml.Linq.XDocument.Load(file).Descendants()
                        .Where(e => e.Name.LocalName.StartsWith("SW.Blocks.", StringComparison.Ordinal)
                                    || e.Name.LocalName.StartsWith("SW.Types.", StringComparison.Ordinal))
                        .Select(e => e.Elements().FirstOrDefault(a => a.Name.LocalName == "AttributeList")?
                            .Elements().FirstOrDefault(n => n.Name.LocalName == "Name")?.Value)
                        .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

                    if (name != null)
                    {
                        return name;
                    }
                }
                catch (Exception)
                {
                    // Unreadable: fall back to the file name; CheckSimaticMl reports the XML error.
                }
            }

            return Path.GetFileNameWithoutExtension(file);
        }

        /// <summary>
        /// Static checks on one SimaticML file. Each rule is a real rejection seen live:
        /// - every ID attribute must be unique in the file (a new CompileUnit reusing ID "60");
        /// - UIds must be unique inside one network's source;
        /// - an SCL (StructuredText) Bool literal carries ConstantValue only - a ConstantType on TRUE failed.
        /// </summary>
        private static List<string> CheckSimaticMl(string file)
        {
            var problems = new List<string>();
            System.Xml.Linq.XDocument doc;

            try
            {
                doc = System.Xml.Linq.XDocument.Load(file);
            }
            catch (Exception ex)
            {
                problems.Add($"Not readable as XML: {ex.Message}");
                return problems;
            }

            foreach (var duplicate in doc.Descendants()
                         .Select(e => e.Attribute("ID")?.Value)
                         .Where(v => v != null)
                         .GroupBy(v => v!, StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1))
            {
                problems.Add($"ID '{duplicate.Key}' is used {duplicate.Count()} times; IDs must be unique in the file (they are not network numbers).");
            }

            var networkNo = 0;

            foreach (var source in doc.Descendants().Where(e => e.Name.LocalName == "NetworkSource"))
            {
                networkNo++;

                // NameCon/IdentCon inside a Wire carry the UId of the part they connect - a reference,
                // not a definition - so they repeat legitimately (live: every clean export has them).
                foreach (var duplicate in source.Descendants()
                             .Where(e => e.Name.LocalName != "NameCon" && e.Name.LocalName != "IdentCon")
                             .Select(e => e.Attribute("UId")?.Value)
                             .Where(v => v != null)
                             .GroupBy(v => v!)
                             .Where(g => g.Count() > 1))
                {
                    problems.Add($"Network {networkNo}: UId '{duplicate.Key}' is used {duplicate.Count()} times; UIds must be unique within a network.");
                }

                var scl = source.Elements().FirstOrDefault(e => e.Name.LocalName == "StructuredText");

                var typedBool = scl?.Descendants()
                    .Where(e => e.Name.LocalName == "ConstantType")
                    .Select(e => e.Parent?.Elements().FirstOrDefault(v => v.Name.LocalName == "ConstantValue")?.Value?.Trim())
                    .FirstOrDefault(v => string.Equals(v, "TRUE", StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(v, "FALSE", StringComparison.OrdinalIgnoreCase));

                if (typedBool != null)
                {
                    problems.Add($"Network {networkNo}: the SCL literal {typedBool} has a <ConstantType>; an SCL Bool literal takes <ConstantValue> only (rejected live on 2026-10-12).");
                }
            }

            return problems;
        }

        /// <summary>File kinds an export of this server produces, and therefore an import consumes.</summary>
        private static readonly HashSet<string> PreviewExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".s7dcl", ".xml" };

        private static string Join(string groupPath, string name) =>
            string.IsNullOrEmpty(groupPath) ? name : $"{groupPath.TrimEnd('/')}/{name}";

        /// <summary>
        /// The group part of a resolved path. A block or type name may itself contain '/'
        /// (live: 'GTS_EQ_I/FLower'), so strip the known name instead of splitting at the last '/'.
        /// </summary>
        private static string GroupOf(string path, string? name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                if (string.Equals(path, name, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                var suffix = "/" + name;

                if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return path.Substring(0, path.Length - suffix.Length);
                }
            }

            return Parent(path);
        }

        private static string Parent(string path)
        {
            var slash = path.LastIndexOf('/');

            return slash < 0 ? string.Empty : path.Substring(0, slash);
        }

        /// <summary>
        /// A groupPath copied from a preservePath export starts with the system folder ('PLC data
        /// types', 'Program blocks'), which the resolved paths from ResolveObjectPath do not
        /// carry - strip it so both sides compare equal. A groupPath passed directly (no system
        /// folder prefix, e.g. just "0_OBs") has nothing to strip and must be returned unchanged -
        /// live-tested: the previous version blanked ANY groupPath without a '/' to "", so a
        /// same-group import (the common case: re-importing a block into the group it already
        /// lives in, one segment deep) always compared unequal to the resolved existing path and
        /// was misreported as a conflict instead of a plain overwrite.
        /// </summary>
        private static string StripLeadingSystemFolder(string groupPath, bool isType)
        {
            if (string.IsNullOrEmpty(groupPath))
            {
                return string.Empty;
            }

            var systemFolder = isType ? "PLC data types" : "Program blocks";
            var slash = groupPath.IndexOf('/');
            var firstSegment = slash < 0 ? groupPath : groupPath.Substring(0, slash);

            if (!string.Equals(firstSegment, systemFolder, StringComparison.OrdinalIgnoreCase))
            {
                return groupPath;
            }

            return slash < 0 ? string.Empty : groupPath.Substring(slash + 1);
        }

        #endregion

        // From the former McpServerWrite.cs:
        // The project-mutating MCP tools (create, rename, delete, import into the project).
        //
        // Callers: registered by Program.BuildTools() only when '--allow-write' was passed, and
        // invoked directly by the write test classes. Affected API: none existing. Data: returns the
        // shared ResponseCreated / ResponseDeleted / ResponseRenamed / ResponseImported /
        // ResponseGenerateBlocks DTOs. The import tools read a caller-supplied file; nothing here
        // writes files. Project changes live in memory until SaveProject (or SaveSession).
        //
        // Conventions, enforced by the Guarded helper below:
        // - WritePolicy.EnsureEnabled runs first, because these are public static methods that the
        // MSTest suite calls directly, bypassing tool registration.
        // - A PortalException is surfaced with its own guidance message; anything else is wrapped.
        // - Destructive = true; Idempotent = true only for renames and deletes-by-name.
        //
        // Filesystem-only exports stay in McpServer: they never modify the project.

        #region plumbing (write)

        /// <summary>
        /// Reminds the caller that Openness edits are in-memory, naming the right save tool for
        /// the current mode: a multiuser local session saves through SaveSession, not SaveProject.
        /// </summary>
        private static string SaveHint =>
            Portal.IsLocalSession
                ? "The change is in memory; call 'SaveSession' to persist it."
                : "The change is in memory; call 'SaveProject' to persist it.";

        /// <param name="oneTransaction">false when the body opens its own transactions, one per step (CopyHmiScreen)</param>
        private static T Guarded<T>(string toolName, Func<T> body, bool oneTransaction = true)
        {
            WritePolicy.EnsureEnabled(toolName);

            try
            {
                // One transaction per tool call: the edits commit together or not at all, and
                // the operator sees a single named entry in the TIA Portal undo stack instead of
                // an unlabelled pile of steps. Falls back to an unwrapped write when TIA Portal
                // refuses exclusive access, so this can never turn a working write into a
                // failure - see Portal.InTransaction in Portal.cs.
                return oneTransaction ? Portal.InTransaction($"MCP: {toolName}", body) : body();
            }
            catch (PortalException pex)
            {
                // PortalException messages are already written for the caller (what went wrong
                // and which tool lists the valid paths), so they pass through unchanged.
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error in '{toolName}': {ex.Message}", ex);
            }
        }

        private static JsonObject OkMeta() => new JsonObject
        {
            ["timestamp"] = DateTime.Now,
            ["success"] = true,
            ["pendingSave"] = true
        };

        private static ResponseCreated Created(string kind, string name, string path) => new ResponseCreated
        {
            Kind = kind,
            Name = name,
            Path = path,
            Message = $"{kind} '{name}' created. {SaveHint}",
            Meta = OkMeta()
        };

        private static ResponseDeleted Deleted(string kind, string path) => new ResponseDeleted
        {
            Kind = kind,
            Path = path,
            Message = $"{kind} '{path}' deleted. {SaveHint}",
            Meta = OkMeta()
        };

        private static ResponseDeleted WithBackup(ResponseDeleted response, Portal.BackupResult backup)
        {
            response.Backup = backup.Path;
            response.BackupWarning = backup.Warning;
            response.Message += BackupNote(backup);
            return response;
        }

        /// <summary>Message suffix naming the backup file, or why there is none.</summary>
        private static string BackupNote(Portal.BackupResult backup) =>
            backup.Path != null ? $" Previous version saved to '{backup.Path}'."
            : backup.Warning != null ? $" {backup.Warning}"
            : string.Empty;

        private static ResponseRenamed Renamed(string kind, string oldPath, string newName, string newPath) => new ResponseRenamed
        {
            Kind = kind,
            OldPath = oldPath,
            NewName = newName,
            NewPath = newPath,
            Message = $"{kind} '{oldPath}' renamed to '{newName}'. {SaveHint}",
            Meta = OkMeta()
        };

        private static ResponseImported Imported(string kind, string groupPath, string importPath) => new ResponseImported
        {
            Kind = kind,
            GroupPath = groupPath,
            ImportPath = importPath,
            Message = $"{kind} imported from '{importPath}' into '{groupPath}'. {SaveHint}",
            Meta = OkMeta()
        };

        /// <summary>Replaces the last segment of a path, for reporting a rename's new path.</summary>
        private static string ReplaceLeaf(string path, string newName)
        {
            var trimmed = (path ?? string.Empty).Trim('/');
            var index = trimmed.LastIndexOf('/');

            return index < 0 ? newName : trimmed.Substring(0, index + 1) + newName;
        }

        private static string JoinPath(string groupPath, string name) =>
            string.IsNullOrEmpty(groupPath) ? name : $"{groupPath}/{name}";

        #endregion
    }
}
