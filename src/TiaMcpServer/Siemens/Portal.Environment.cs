using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    // Environment facts that sit around the project rather than in it: which other Openness
    // clients are attached to the running TIA Portal instances, which products/option packages
    // are installed, and the project's language settings.
    //
    // Callers: Diagnostics.Run (Doctor) and McpServer.GetProjects. Affected API: Doctor and
    // GetProject gain fields; nothing is removed. Reads/writes no data files. Read-only: it never
    // disposes another client's session (old Openness samples did that on a TrustAuthority check).
    public partial class Portal
    {
        #region environment

        /// <summary>
        /// Lists the Openness clients attached to every running TIA Portal instance. This
        /// server's own sessions are flagged IsThisServer, so anything else is another client:
        /// a second MCP server, a colleague's tool or a debug session in Visual Studio.
        /// </summary>
        public List<OpennessSessionInfo> GetOpennessSessions()
        {
            _logger?.LogInformation("Listing attached Openness sessions...");

            var ownProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
            var result = new List<OpennessSessionInfo>();

            foreach (var process in TiaPortal.GetProcesses())
            {
                IEnumerable<TiaPortalSession> sessions;
                try
                {
                    sessions = process.AttachedSessions.ToList();
                }
                catch (Exception)
                {
                    // An instance that is starting up or shutting down can refuse this.
                    continue;
                }

                foreach (var session in sessions)
                {
                    var info = new OpennessSessionInfo { PortalId = process.Id };

                    // Each property is read on its own: one failing must not hide the others.
                    try { info.ProcessId = session.ProcessId; } catch (Exception) { }
                    try { info.ProcessPath = session.ProcessPath?.ToString(); } catch (Exception) { }
                    try { info.AttachTime = session.AttachTime; } catch (Exception) { }
                    try { info.UtilizationSeconds = Math.Round(session.UtilizationTime.TotalSeconds, 1); } catch (Exception) { }
                    try { info.AccessLevel = session.AccessLevel.ToString(); } catch (Exception) { }
                    try { info.IsActive = session.IsActive; } catch (Exception) { }

                    info.IsThisServer = info.ProcessId == ownProcessId;
                    result.Add(info);
                }
            }

            return result;
        }

        /// <summary>
        /// Lists the products and option packages (Safety, WinCC Unified, Startdrive, ...)
        /// installed in the attached TIA Portal instance, flattened to "Name Version" entries
        /// with the parent product in front of an option. Uses the first running instance when
        /// none is attached.
        /// </summary>
        public List<string> GetInstalledProducts()
        {
            _logger?.LogInformation("Listing installed TIA Portal products...");

            var processes = TiaPortal.GetProcesses().ToList();
            int? id = null;
            try
            {
                id = CurrentPortalId;
            }
            catch (PortalException)
            {
                // Several instances attached and none selected - fall back to the first one.
            }
            var process = processes.FirstOrDefault(p => p.Id == id) ?? processes.FirstOrDefault();

            var result = new List<string>();
            if (process == null)
            {
                return result;
            }

            AddProducts(process.InstalledSoftware, null, result);
            return result;
        }

        private static void AddProducts(IEnumerable<TiaPortalProduct> products, string? parent, List<string> result)
        {
            foreach (var product in products)
            {
                var name = $"{product.Name} {product.Version}".Trim();
                result.Add(parent == null ? name : $"{parent} / {name}");

                AddProducts(product.Options, parent ?? product.Name, result);
            }
        }

        /// <summary>
        /// Reads the editing, reference and active languages of a project or session. Texts an
        /// Openness write puts into a multilingual field (comments, alarm texts, HMI labels) land
        /// in the editing language, so this tells which column they end up in.
        /// </summary>
        public ProjectLanguageInfo? GetProjectLanguages(ProjectBase? project)
        {
            if (project == null)
            {
                return null;
            }

            try
            {
                var settings = project.LanguageSettings;

                return new ProjectLanguageInfo
                {
                    EditingLanguage = settings.EditingLanguage?.Culture?.Name,
                    ReferenceLanguage = settings.ReferenceLanguage?.Culture?.Name,
                    ActiveLanguages = settings.ActiveLanguages
                        .Select(l => l.Culture?.Name)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Select(n => n!)
                        .ToList()
                };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Reading the project language settings failed");
                return null;
            }
        }

        #endregion
    }

    /// <summary>
    /// One Openness client attached to a TIA Portal instance, as reported by 'Doctor'.
    /// </summary>
    public sealed class OpennessSessionInfo
    {
        /// <summary>Process id of the TIA Portal instance the client is attached to.</summary>
        public int PortalId { get; set; }
        public int? ProcessId { get; set; }
        public string? ProcessPath { get; set; }
        public DateTime? AttachTime { get; set; }

        /// <summary>Time the client has spent inside TIA Portal calls since attaching.</summary>
        public double? UtilizationSeconds { get; set; }
        public string? AccessLevel { get; set; }
        public bool? IsActive { get; set; }
        public bool IsThisServer { get; set; }
    }

    /// <summary>
    /// Language settings of a project or session.
    /// </summary>
    public sealed class ProjectLanguageInfo
    {
        /// <summary>Culture name, e.g. "en-US"; Openness writes to multilingual texts go here.</summary>
        public string? EditingLanguage { get; set; }
        public string? ReferenceLanguage { get; set; }
        public List<string> ActiveLanguages { get; set; } = new List<string>();
    }
}
