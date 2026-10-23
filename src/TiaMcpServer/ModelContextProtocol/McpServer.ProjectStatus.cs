using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // Unsaved-change flag and project history (BACKLOG D4); see Portal.ProjectStatus.cs.

        [McpServerTool(Name = "GetProjectStatus", Title = "Get project status", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to check before saving or closing whether the open project has unsaved changes, and to read its project history (date and text of events such as created, upgraded, compiled), last modified time and user. Changes nothing")]
        public static ResponseProjectStatus GetProjectStatus(
            [Description("historyCount: number of most recent history entries to list (default 10; 0 lists none)")] int historyCount = 10)
        {
            try
            {
                var status = Portal.GetProjectStatus(historyCount);

                var message = $"Project '{status.Name}' {(status.IsModified ? "HAS unsaved changes" : "has no unsaved changes")}";
                if (status.LastModified != null)
                {
                    message += $", last modified {status.LastModified.Value.ToLocalTime():yyyy-MM-dd HH:mm} (local time)" +
                               (string.IsNullOrEmpty(status.LastModifiedBy) ? string.Empty : $" by {status.LastModifiedBy}");
                }

                message += $"; {status.History.Count} of {status.HistoryTotal} history entries listed.";

                return new ResponseProjectStatus
                {
                    Message = message,
                    Name = status.Name,
                    Path = status.Path,
                    IsModified = status.IsModified,
                    LastModified = status.LastModified,
                    LastModifiedBy = status.LastModifiedBy,
                    HistoryTotal = status.HistoryTotal,
                    History = status.History.Select(h => new ResponseProjectHistoryEntry { Time = h.Time, Text = h.Text }).ToList(),
                    Warnings = status.Warnings.Count > 0 ? status.Warnings : null,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["isModified"] = status.IsModified
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the project status: {ex.Message}", ex);
            }
        }
    }
}
