using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;
using TiaMcpServer.ModelContextProtocol;
using TiaMcpServer.Siemens;

namespace TiaMcpServer
{
    public class Program
    {

        public static async Task Main(string[] args)
        {
            var options = CliOptions.ParseArgs(args);

            Engineering.TiaMajorVersion = options.TiaMajorVersion ?? Openness.BuildTiaMajorVersion;

            // A V20 build asked for V21 used to die inside Openness with 'Exception.ToString() failed'
            // before the client saw anything. Say what is wrong instead.
            if (Engineering.TiaMajorVersion > Openness.BuildTiaMajorVersion)
            {
                Console.Error.WriteLine(
                    $"This TiaMcpServer.exe was built for TIA Portal V{Openness.BuildTiaMajorVersion} Openness and cannot " +
                    $"attach to V{Engineering.TiaMajorVersion}. Build it for V{Engineering.TiaMajorVersion} on a PC with that " +
                    $"version installed: dotnet build -c Release -p:TiaVersion={Engineering.TiaMajorVersion} (output in bin\\V{Engineering.TiaMajorVersion}).");
                Environment.Exit(1);
            }

            // Set before the --doctor branch below, which reports the write mode, and before the
            // host registers its tool types.
            WritePolicy.AllowWrite = options.AllowWrite;
            TiaMcpServer.Siemens.Portal.BackupRoot = options.BackupPath;

            // Before the host registers its tool types: Apply reads the presets.
            ToolDefaults.Configure(options);

            if (Engineering.TiaMajorVersion >= 20)
            {
                Openness.Initialize(Engineering.TiaMajorVersion);
            }

            // Fallback: the Siemens resolver does not cover every Siemens.Engineering.* satellite assembly.
            AppDomain.CurrentDomain.AssemblyResolve += Engineering.Resolver;

            // '--doctor' only reports the environment - it must run before the user group gate below,
            // because a missing group membership is one of the things it is meant to diagnose.
            if (options.Doctor)
            {
                RunDoctor();
                return;
            }

            // Ensure user is in user group 'Siemens TIA Openness'
            if (await Openness.IsUserInGroup())
            {
                await RunStdioHost(options);
            }
            else
            {
                Console.WriteLine("User is not in the required group. Exiting...");
            }
        }

        /// <summary>
        /// Prints the environment diagnostics of the '--doctor' command. Read-only: it neither
        /// connects to TIA Portal nor changes user group membership.
        /// </summary>
        public static void RunDoctor()
        {
            try
            {
                // Fully qualified: 'Diagnostics' alone would collide with the System.Diagnostics namespace.
                var report = TiaMcpServer.Siemens.Diagnostics.Run(new Portal(), WritePolicy.AllowWrite);

                Console.WriteLine(report.Text);
            }
            catch (Exception ex)
            {
                // A diagnostics command must report a problem, not crash with a stack trace.
                Console.Error.WriteLine($"Diagnose failed: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }

        /// <summary>
        /// The read-only tools are always registered. The project-mutating tools are the
        /// McpServer methods marked [WriteTool]; they are only registered with '--allow-write',
        /// which is what keeps them out of 'tools/list' rather than merely refusing them when
        /// called. Tools are created one by one (the SDK's type-based registration would take
        /// every [McpServerTool] method of the class).
        /// </summary>
        public static IEnumerable<global::ModelContextProtocol.Server.McpServerTool> BuildTools(bool allowWrite)
        {
            return typeof(McpServer)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<global::ModelContextProtocol.Server.McpServerToolAttribute>() != null)
                .Where(m => allowWrite || m.GetCustomAttribute<WriteToolAttribute>() == null)
                .Select(m => ToolDefaults.Apply(m, PortalSelection.Apply(m, global::ModelContextProtocol.Server.McpServerTool.Create(m))))
                .ToList();
        }

        public static async Task RunStdioHost(CliOptions? options)
        {
            var builder = Host.CreateEmptyApplicationBuilder(settings: null);
            if (builder != null)
            {
                if (options != null && options.Logging != null)
                {
                    switch (options.Logging)
                    {
                        case 1:
                            // ATTENTION: For STDIO, logs must go to stderr!
                            builder.Logging.AddConsole(options =>
                            {
                                options.LogToStandardErrorThreshold = LogLevel.Trace;
                            });
                            break;

                        case 2:
                            // Visual Studio Debug Output / Sysinternals.DebugView
                            builder.Logging.AddDebug();
                            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
                            builder.Logging.AddFilter("ModelContextProtocol", LogLevel.Information);
                            builder.Logging.AddFilter("TiaMcpServer", LogLevel.Debug);

                            // Log Level for Debug Output
                            builder.Logging.SetMinimumLevel(LogLevel.Debug);
                            break;

                        case 3:
                            // Windows Event Log
                            builder.Logging.AddEventLog();
                            break;

                        default:
                            // no logging
                            break;
                    }
                }

                builder.Services
                    .AddMcpServer(serverOptions =>
                    {
                        serverOptions.ServerInfo = new global::ModelContextProtocol.Protocol.Implementation
                        {
                            Name = "TiaMcpServer",
                            Title = "TIA Portal MCP Server",
                            Version = TiaMcpServer.Siemens.Diagnostics.ServerVersion
                        };

                        serverOptions.ServerInstructions =
                            "Exposes Siemens TIA Portal via Openness. Call 'Connect' first, then 'OpenProject' with an " +
                            "absolute .apXX project or .alsXX session path. Use 'GetProjectTree' or 'GetSoftwareTree' to " +
                            "discover the path strings that the other tools expect. Export and import tools operate on " +
                            "the local file system of the machine running this server. Every request is independent: " +
                            "when more than one TIA Portal instance is running, call 'GetPortals' and pass the chosen " +
                            "'portalId' to 'Connect' and to every other tool." +
                            " TIA projects are binary and reachable only through this server, so use these tools rather " +
                            "than reading project files or writing your own Openness scripts. Task guide: " +
                            "(1) Start: 'Connect' attaches to the running TIA Portal and picks up the project already open " +
                            "there ('GetState' shows it); call 'OpenProject' only for a different project, since it closes the " +
                            "open one. Then 'GetProjectTree' for the software path (e.g. 'PLC_1') and 'GetBlocks' or " +
                            "'ResolveObjectPath' for block paths. " +
                            "(2) Understand code: 'GetBlockSource' or 'GetNetworkSource' to read a block, 'FindInCode' to " +
                            "search code text, 'GetCrossReferences' or 'WhereUsed' for who uses a tag or block, 'GetTags' for " +
                            "tags, 'GetDbStartValues' for DB contents. " +
                            "(3) Change code, one block at a time, in this order: 'GoOffline' if the PLC is online (compile " +
                            "and import fail while online); read the block ('GetBlockSource', 'GetNetworkSource', for LAD XML " +
                            "'ExportXmlBlock'); 'GetBlockFingerprints' with 'saveFile'; change it with the smallest tool - " +
                            "'EditBlockNetwork' for one network (LAD/FBD from an XML file, preview with 'previewFile' first; SCL " +
                            "statements via 'sclFile'), 'SetDbStartValue' for one DB start value, 'EditBlockInterface' to add, change or delete interface variables (several at once with 'moreMembers'; 'EditTypeInterface' for UDT members); then 'CompileCodeBlock' for " +
                            "that block only; on an error fix once from its message; finally 'GetBlockFingerprints' with " +
                            "'compareFile' and report what changed and the backup path. Do not hand-rewrite and re-import a " +
                            "whole block's XML, do not compile the whole PLC again and again, do not repeat a failing call " +
                            "unchanged, and do not work around this server with scripts or file edits. If the same step fails " +
                            "twice, stop and show the user the full error text. The 'ModifyBlockWorkflow' prompt spells out the " +
                            "routine. Changes stay in memory until 'SaveProject'; save only when the user asks. If this text or " +
                            "'Connect' says the server build is old, pass that on to the user once; it does not stop anything. " +
                            "(4) Paths: 'softwarePath' is the PLC software name from 'GetProjectTree', e.g. 'PLC_1'. Block, " +
                            "type and group paths are '/'-separated and relative to their system folder ('Program blocks', " +
                            "'PLC data types', 'PLC tags'), e.g. '009_Servo/zGDBs/SM_UserData_Origin'; an empty groupPath means " +
                            "that root. File paths ('exportPath', 'importPath') are absolute paths on the PC running this server. " +
                            "(5) Formats: XML (SimaticML) is complete and is what 'ImportXmlBlock' and 'EditBlockNetwork' use; " +
                            "documents (.s7dcl/.s7res, V20+) are readable text for review; external source (.scl/.db/.awl) " +
                            "exists only for SCL, STL and DB blocks. " +
                            "(6) WinCC Unified screens: before tools that change a screen's items (create, delete, update, copy, " +
                            "styles, scripts, dynamizations) ask the user to close that screen in the TIA Portal screen editor; " +
                            "changing a screen that is open there can crash TIA Portal (NullReferenceException in the editor's " +
                            "arrange toolbar) and lose every unsaved change." +
                            (ToolDefaults.Presets.Count > 0
                                ? " The server presets " + string.Join(", ", ToolDefaults.Presets.Keys.OrderBy(k => k, StringComparer.Ordinal)) +
                                  ": leave them out of a call to use the preset, pass a value to override it. 'GetState' shows the presets."
                                : string.Empty) +
                            (VersionInfo.StaleBuildHint() is { } stale ? " " + stale : string.Empty) +
                            (WritePolicy.AllowWrite
                                ? " Write mode is enabled: tools that create, rename or delete project objects are " +
                                  "available. Their changes stay in memory until 'SaveProject' (or 'SaveSession')."
                                : " Read-only mode: the server was started without '--allow-write', so the tools that change " +
                                  "the project (create, rename, delete, import, HMI screen edits and copies) are not listed. " +
                                  "If the user asks for such a change, tell them to add \"--allow-write\" to this server's " +
                                  "args in the MCP client configuration and restart the client.");
                    })
                    .WithStdioServerTransport()
                    .WithRequestFilters(filters =>
                    {
                        filters.AddCallToolFilter(PortalSelection.Filter);
                        filters.AddCallToolFilter(ToolDefaults.Filter);
                        filters.AddCallToolFilter(AuditLog.Filter);
                        filters.AddListToolsFilter(ToolOrdering.Filter);
                        filters.AddGetPromptFilter(PromptArguments.Filter);
                    })
                    .WithTools(BuildTools(WritePolicy.AllowWrite))
                    .WithPrompts((IEnumerable<Type>)new[] { typeof(McpPrompts) });

                // Register the Portal service for dependency injection
                builder.Services.AddSingleton<Portal>();
                builder.Services.AddSingleton<S7Diagnostics>();

                var host = builder.Build();

                // Set the service provider for the MCP server, to retrieve Portal with injected logger
                McpServer.SetServiceProvider(host.Services);

                // Set the logger for the MCP server
                McpServer.Logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("McpServer");

                // log a bit of information about the server start
                if (options != null && options.Logging != null && options.Logging > 0)
                {
                    var logger = host.Services.GetRequiredService<ILogger<Program>>();

                    logger.LogInformation($"=== TIA Portal MCP Server '{DateTime.Now.ToShortTimeString()}' ===");

                    switch (options.Logging)
                    {
                        case 1:
                            logger.LogInformation("Logging to stderr");
                            break;
                        case 2:
                            logger.LogInformation("Logging to debug output");
                            break;
                        case 3:
                            logger.LogInformation("Logging to Windows event log");
                            break;
                    }
                }

                await host.RunAsync();
            }

        }
    }
}
