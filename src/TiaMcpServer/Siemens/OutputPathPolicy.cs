using System;
using System.IO;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    // Centralizes where exports are allowed to land on disk. Callers (MCP clients/agents) may
    // only request a relative subfolder name under this managed root - never an absolute path,
    // a different drive, a UNC share, or '..' traversal. This closes the path-traversal/
    // accidental-overwrite/data-leak risk of accepting caller-supplied full paths directly
    // (see upstream tiaportal-mcp issue #18).
    public static class OutputPathPolicy
    {
        private static readonly Lazy<string> _root = new Lazy<string>(ResolveRoot);

        public static string Root => _root.Value;

        private static string ResolveRoot()
        {
            var configured = Environment.GetEnvironmentVariable("TiaMcpExportRoot");
            var root = !string.IsNullOrWhiteSpace(configured)
                ? configured
                : Path.Combine(Path.GetTempPath(), "tiaportal-mcp-exports");

            root = Path.GetFullPath(root);
            Directory.CreateDirectory(root);
            return root;
        }

        // Resolves a caller-supplied relative subfolder (or null/empty for the root itself)
        // into an absolute directory guaranteed to be inside Root, creating it if needed.
        public static string ResolveDirectory(string? requestedPath)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                return Root;
            }

            if (Path.IsPathRooted(requestedPath) || requestedPath.StartsWith("\\\\"))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"exportPath must be a relative subfolder name, not a full path - exports are always written under the server-managed folder ('{Root}'). Pass e.g. 'myexport' instead of a drive or absolute path.");
            }

            var segments = requestedPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => s == ".."))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    "exportPath must not contain '..' path traversal segments.");
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = segments.Select(s => new string(s.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray()));

            var resolved = Path.GetFullPath(Path.Combine(new[] { Root }.Concat(sanitized).ToArray()));

            if (!resolved.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "Resolved export path escapes the managed export folder.");
            }

            Directory.CreateDirectory(resolved);
            return resolved;
        }
    }
}
