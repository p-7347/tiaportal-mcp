using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // The change log written by AuditLog (BACKLOG B4).

        [McpServerTool(Name = "GetChangeLog", Title = "Get change log", ReadOnly = true, Idempotent = true, OpenWorld = false),
         Description("Use to list what this server changed in the project on a day, e.g. to write a change summary or find a backup. Every write tool call is logged with time, tool, arguments, ok or error, its reply and the backup file. Works without a TIA connection")]
        public static ResponseChangeLog GetChangeLog(
            [Description("day: optional date as 'yyyy-MM-dd', e.g. '2026-10-22'. Empty (default) is today")] string day = "",
            [Description("toolRegex: optional tool name or regular expression, e.g. 'EditBlock'. Empty (default) lists every write tool")] string toolRegex = "")
        {
            try
            {
                var date = DateTime.Today;
                if (!string.IsNullOrWhiteSpace(day)
                    && !DateTime.TryParseExact(day.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    throw new McpException($"day '{day}' is not a date like '2026-10-22'.");
                }

                var entries = AuditLog.Read(date, toolRegex);
                var file = Path.Combine(AuditLog.Directory, date.ToString("yyyyMMdd") + ".jsonl");
                var errors = entries.Count(e => (string?)e["outcome"] == "error");

                return new ResponseChangeLog
                {
                    Message = entries.Count == 0
                        ? $"No write tool calls logged on {date:yyyy-MM-dd} ({file})."
                        : $"{entries.Count} write tool call(s) on {date:yyyy-MM-dd}, {errors} failed. Log: {file}",
                    File = file,
                    Entries = entries,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["count"] = entries.Count
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the change log: {ex.Message}", ex);
            }
        }
    }
}
