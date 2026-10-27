using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// The single exception-decoration point for Openness operations, as required by
    /// docs/error-model.md. Wrap every Portal operation body in <see cref="Run{T}"/> instead of
    /// repeating the try/catch/decorate/log/rethrow block per method.
    ///
    /// Callers: the Portal partial classes in this folder. Affected API: none existing - this
    /// adds a new internal helper and changes no public signature. Reads/writes no data files;
    /// its only output is via ILogger.
    /// </summary>
    internal static class Operation
    {
        /// <summary>
        /// Openness objects are not thread-safe and the MCP SDK may dispatch tool calls
        /// concurrently, so all Openness traffic is serialized here.
        /// Monitor - not SemaphoreSlim - because Portal methods call each other (ExportXmlBlock
        /// calls GetBlock, ExportXmlBlocks calls GetBlocks), and Monitor is reentrant on the
        /// same thread while SemaphoreSlim would self-deadlock.
        /// </summary>
        private static readonly object Gate = new object();

        /// <summary>
        /// Marker written into PortalException.Data so a nested Run does not log the same
        /// failure once per stack level.
        /// </summary>
        private const string LoggedKey = "__logged";

        /// <summary>
        /// Extra text for a failed Openness call, e.g. that a PLC is still online, which blocks
        /// imports, exports and block changes without TIA saying so (thread feedback 2-1). Set by
        /// Portal; null when there is nothing to add. Never throws into the failure path.
        /// </summary>
        internal static Func<string?>? FailureHint { get; set; }

        internal static T Run<T>(
            ILogger? logger,
            string operation,
            PortalErrorCode failCode,
            Func<T> body,
            params (string Key, object? Value)[] context)
        {
            lock (Gate)
            {
                try
                {
                    return body();
                }
                catch (Exception ex)
                {
                    throw Decorate(logger, operation, failCode, ex, context);
                }
            }
        }

        internal static void Run(
            ILogger? logger,
            string operation,
            PortalErrorCode failCode,
            Action body,
            params (string Key, object? Value)[] context)
        {
            lock (Gate)
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    throw Decorate(logger, operation, failCode, ex, context);
                }
            }
        }

        /// <summary>
        /// Bridge for the legacy bool-returning Portal methods that swallow their exceptions.
        /// Prefer <see cref="Run{T}"/> for new code; this exists so those methods can adopt the
        /// shared lock and logging without changing their signature.
        /// </summary>
        internal static bool TryRun(ILogger? logger, string operation, Action body)
        {
            lock (Gate)
            {
                try
                {
                    body();
                    return true;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "{Operation} failed", operation);
                    return false;
                }
            }
        }

        private static PortalException Decorate(
            ILogger? logger,
            string operation,
            PortalErrorCode failCode,
            Exception ex,
            (string Key, object? Value)[] context)
        {
            var pex = ex as PortalException
                      ?? new PortalException(failCode, $"{operation} failed: {DescribeChain(ex)}{Hint(failCode)}", null, ex);

            // Inner frames win: an inner Run already recorded the most specific context.
            foreach (var (key, value) in context)
            {
                if (!pex.Data.Contains(key))
                {
                    pex.Data[key] = value;
                }
            }

            if (!pex.Data.Contains("operation"))
            {
                pex.Data["operation"] = operation;
            }

            if (!pex.Data.Contains(LoggedKey))
            {
                pex.Data[LoggedKey] = true;
                logger?.LogError(pex, "{Operation} failed", operation);
            }

            return pex;
        }

        /// <summary>Only raw Openness failures of operations that change or export the project get the hint.</summary>
        private static string Hint(PortalErrorCode failCode)
        {
            if (failCode == PortalErrorCode.NotFound || failCode == PortalErrorCode.InvalidParams || failCode == PortalErrorCode.WriteDisabled)
            {
                return string.Empty;
            }

            try
            {
                return FailureHint?.Invoke() is { Length: > 0 } hint ? " " + hint : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Non-blocking check for whether some call, on any attached instance, is currently
        /// inside Run/TryRun. This lock is shared across every TIA Portal instance (see the
        /// remark on <see cref="Gate"/>), so a request stuck waiting on its own instance's
        /// PortalContext.Gate may actually be queued behind a different instance's call holding
        /// this one. Used only to add that possibility to a busy-timeout message - never to make
        /// a decision, since "stuck" vs. "just slow" still has to be judged by whoever reads it.
        /// </summary>
        internal static bool IsGloballyBusy()
        {
            if (Monitor.TryEnter(Gate, 0))
            {
                Monitor.Exit(Gate);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Every distinct message down the InnerException chain, joined. Openness wraps the
        /// actual reason for a refused import or compile one or two levels deep, so the outer
        /// message alone ("Error when calling method 'Import'") says nothing.
        /// </summary>
        internal static string DescribeChain(Exception ex)
        {
            var parts = new System.Collections.Generic.List<string>();

            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                var text = $"{e.GetType().Name}: {e.Message}".Trim();

                if (!parts.Contains(text))
                {
                    parts.Add(text);
                }
            }

            return string.Join(" -> ", parts);
        }

        /// <summary>
        /// Every export/output-path parameter in this codebase must pass through this check
        /// before being combined into a file path. A relative path resolves against the server
        /// process's current working directory, which the caller does not control and which may
        /// not be writable - Claude Desktop launches this server with its working directory set
        /// to a system folder (no write access for a non-admin user). Live-tested: an unchecked
        /// relative exportPath on a batch export silently exported 0 of N items, with every
        /// per-item UnauthorizedAccessException swallowed into an internal failures list nobody
        /// surfaced to the caller, reporting success=true. Reject upfront instead of failing open.
        /// </summary>
        internal static void RequireAbsolutePath(string path, string paramName = "exportPath")
        {
            if (!System.IO.Path.IsPathRooted(path))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"{paramName} must be an absolute path - relative paths resolve against the server process's working directory, which may not be writable. Got: '{path}'");
            }
        }
    }

    /// <summary>
    /// TIA Portal allows object names that are not valid Windows file names - the CPU01 project has
    /// blocks named 'T_Data->HMI' and 'GTS_EQ_I/FLower' - so every export that turns a name into a
    /// file or folder name goes through here. Lookups by name still use the real name.
    /// </summary>
    internal static class FileNames
    {
        private static readonly char[] Invalid = System.IO.Path.GetInvalidFileNameChars();

        /// <summary>The name with every character Windows does not allow in a file name replaced by '_'.</summary>
        internal static string Safe(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(Invalid) < 0)
            {
                return name;
            }

            var chars = name.ToCharArray();

            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(Invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        /// <summary>A '/'-separated group path as a relative Windows folder path, each segment made safe.</summary>
        internal static string SafeRelative(string groupPath)
        {
            return string.Join("\\", (groupPath ?? string.Empty)
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Safe));
        }
    }
}
