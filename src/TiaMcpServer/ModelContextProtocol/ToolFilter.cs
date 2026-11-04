using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Optional '--tools' command line filter: lists only the tools whose names match, so a client
    /// that loads every tool into each request (no tool search) carries a fraction of the ~200 tool
    /// definitions. Comma-separated name patterns, '*' as wildcard, case-insensitive; a leading '-'
    /// excludes. Only exclusions means "everything except": '--tools "-*Hmi*,-*Comfort*"'.
    /// The session tools in <see cref="AlwaysListed"/> stay in any case, so the server remains usable.
    ///
    /// Callers: Program.Main (Configure from CliOptions), Program.BuildTools (Includes), the server
    /// instructions (Summary). Affected API: none by default - without '--tools' every tool is listed
    /// as before. A filtered-out tool is not registered at all, like a write tool without
    /// '--allow-write'.
    /// </summary>
    public static class ToolFilter
    {
        public static readonly IReadOnlyCollection<string> AlwaysListed = new[]
        {
            "Connect", "GetPortals", "GetState", "Doctor", "GetProject", "GetProjectTree"
        };

        private static List<Regex> _include = new();
        private static List<Regex> _exclude = new();

        /// <summary>The '--tools' text as given; null when every tool is listed.</summary>
        public static string? Spec { get; private set; }

        public static void Configure(string? spec)
        {
            _include = new List<Regex>();
            _exclude = new List<Regex>();
            Spec = string.IsNullOrWhiteSpace(spec) ? null : spec!.Trim();

            if (Spec == null)
            {
                return;
            }

            foreach (var part in Spec.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var pattern = part.Trim();
                var exclude = pattern.StartsWith("-", StringComparison.Ordinal);
                if (exclude)
                {
                    pattern = pattern.Substring(1).Trim();
                }

                if (pattern.Length == 0)
                {
                    continue;
                }

                var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                (exclude ? _exclude : _include).Add(regex);
            }
        }

        public static bool Includes(string toolName)
        {
            if (Spec == null || AlwaysListed.Contains(toolName, StringComparer.Ordinal))
            {
                return true;
            }

            var included = _include.Count == 0 || _include.Any(r => r.IsMatch(toolName));
            return included && !_exclude.Any(r => r.IsMatch(toolName));
        }

        /// <summary>One sentence for the server instructions; empty without a filter.</summary>
        public static string Summary(int listed) =>
            Spec == null
                ? string.Empty
                : $" Only {listed} tools are listed (the server was started with '--tools {Spec}'). If the user needs a " +
                  "tool that is not listed, tell them to change or remove \"--tools\" in this server's args and restart the client.";
    }
}
