using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TiaMcpServer.Siemens
{
    // Connect while TIA Portal is busy (field report 2026-11-17, No.7).
    //
    // Attaching through Openness waits until TIA Portal's UI thread is free: during a compile, a
    // download, a project being opened or a modal dialog (including the first-time "allow Openness
    // access" prompt) 'Connect' hung until the client timed out, and the server then looked dead.
    // The attach now runs in the background: Connect waits a few seconds, then looks at TIA
    // Portal's windows (not responding, main window disabled by a dialog, the dialog titles) and
    // says what blocks it instead of waiting on. The attach itself keeps going; the next Connect
    // picks up its result as soon as TIA Portal is free.
    //
    // Callers: McpServer.Connect and OpenTiaProject. The command-line tools keep the plain
    // AttachPortal, which waits as long as it takes.
    public partial class Portal
    {
        public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(25);

        /// <summary>Longest a Connect waits for the attach before it answers "busy" (stays below the usual 60 s client timeout).</summary>
        public TimeSpan ConnectTimeout { get; set; } = DefaultConnectTimeout;

        /// <summary>After this long without an answer TIA Portal's windows are checked for a dialog or a busy UI.</summary>
        private static readonly TimeSpan BusyProbeDelay = TimeSpan.FromSeconds(3);

        private readonly object _pendingAttachLock = new();

        private (int? PortalId, Task<(int Id, bool WasAttached)> Task, DateTime Started)? _pendingAttach;

        /// <summary>
        /// Like <see cref="AttachPortal(int?, out bool)"/>, but answers within <see cref="ConnectTimeout"/>:
        /// when TIA Portal does not respond it throws "busy" with what it found, and the attach goes on in
        /// the background so a later call returns at once.
        /// </summary>
        public int AttachPortalWithin(int? portalId, out bool wasAttached)
        {
            Task<(int Id, bool WasAttached)> task;
            DateTime started;

            lock (_pendingAttachLock)
            {
                if (_pendingAttach is { } pending && !pending.Task.IsCompleted)
                {
                    if (pending.PortalId != portalId)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            $"An earlier 'Connect' ({Target(pending.PortalId)}) is still waiting for TIA Portal since {pending.Started:HH:mm:ss}." +
                            (BusyDetail(pending.PortalId) is { } detail ? " " + detail : string.Empty) +
                            " Call 'Connect' again once TIA Portal is free.");
                    }

                    task = pending.Task;
                    started = pending.Started;
                }
                else
                {
                    task = Task.Run(() =>
                    {
                        var id = AttachPortal(portalId, out var was);
                        return (id, was);
                    });
                    started = DateTime.Now;
                    _pendingAttach = (portalId, task, started);
                }
            }

            if (!task.Wait(BusyProbeDelay))
            {
                var detail = BusyDetail(portalId);

                if (detail != null || !task.Wait(ConnectTimeout - BusyProbeDelay))
                {
                    throw new PortalException(PortalErrorCode.InvalidState,
                        $"TIA Portal did not answer 'Connect' ({Target(portalId)}, waiting since {started:HH:mm:ss}). " +
                        (detail ?? BusyDetail(portalId) ?? "It may be starting, compiling, downloading or opening a project.") +
                        " The connection keeps waiting in the background: finish or close that in TIA Portal, then call 'Connect' again; it returns at once when TIA Portal is free.");
                }
            }

            lock (_pendingAttachLock)
            {
                if (_pendingAttach is { } done && ReferenceEquals(done.Task, task))
                {
                    _pendingAttach = null;
                }
            }

            try
            {
                var result = task.GetAwaiter().GetResult();
                wasAttached = result.WasAttached;

                return result.Id;
            }
            catch (AggregateException ae) when (ae.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ae.InnerException).Throw();
                throw;
            }
        }

        private static string Target(int? portalId) => portalId.HasValue ? $"portalId {portalId.Value}" : "any instance";

        /// <summary>
        /// What TIA Portal's windows show: not responding, its main window disabled by a dialog, and
        /// the titles of its other open windows. Null when nothing points to TIA Portal being busy.
        /// </summary>
        internal string? BusyDetail(int? portalId)
        {
            var notes = new List<string>();

            foreach (var process in TiaProcesses(portalId))
            {
                using (process)
                {
                    var found = new List<string>();
                    IntPtr main;

                    try
                    {
                        main = process.MainWindowHandle;

                        if (!process.Responding)
                        {
                            found.Add("its window does not respond (working: e.g. compiling, downloading, opening or saving)");
                        }
                    }
                    catch (Exception)
                    {
                        // elevated or exiting: nothing to tell
                        continue;
                    }

                    var blocked = main != IntPtr.Zero && !IsWindowEnabled(main);

                    if (blocked)
                    {
                        found.Add("a dialog blocks its main window");
                    }

                    if (found.Count > 0)
                    {
                        var titles = WindowTitles(process.Id, main);

                        if (titles.Count > 0)
                        {
                            found.Add("open: " + string.Join(", ", titles.Take(4).Select(t => $"'{t}'")));
                        }

                        if (titles.Any(t => t.IndexOf("Openness", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            found.Add("answer TIA Portal's Openness access prompt (allow this server)");
                        }
                    }

                    if (_contexts.TryGetValue(process.Id, out var context) && context.Progress is { } progress)
                    {
                        found.Add($"this server is running {progress.Describe()}");
                    }

                    if (found.Count > 0)
                    {
                        notes.Add($"TIA Portal {process.Id}: {string.Join("; ", found)}.");
                    }
                }
            }

            return notes.Count > 0 ? string.Join(" ", notes) : null;
        }

        private static IEnumerable<Process> TiaProcesses(int? portalId)
        {
            if (portalId is int id)
            {
                Process? process = null;

                try
                {
                    process = Process.GetProcessById(id);
                }
                catch (ArgumentException)
                {
                    // not running
                }

                return process != null ? new[] { process } : Array.Empty<Process>();
            }

            try
            {
                return Process.GetProcessesByName("Siemens.Automation.Portal");
            }
            catch (Exception)
            {
                return Array.Empty<Process>();
            }
        }

        /// <summary>Titles of the visible top-level windows of a process other than its main window (dialogs, progress windows).</summary>
        private static List<string> WindowTitles(int processId, IntPtr main)
        {
            var titles = new List<string>();

            try
            {
                EnumWindows((window, _) =>
                {
                    if (window != main && IsWindowVisible(window) &&
                        GetWindowThreadProcessId(window, out var owner) != 0 && owner == (uint)processId)
                    {
                        var length = GetWindowTextLength(window);

                        if (length > 0)
                        {
                            var text = new StringBuilder(length + 1);
                            GetWindowText(window, text, text.Capacity);
                            var title = text.ToString().Trim();

                            if (title.Length > 0 && !titles.Contains(title))
                            {
                                titles.Add(title);
                            }
                        }
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception)
            {
                // no window station (service): no titles
            }

            return titles;
        }

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr window);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    }
}
