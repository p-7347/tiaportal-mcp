using Siemens.Engineering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Unsaved-change flag and project history (BACKLOG D4): ProjectBase.IsModified,
        // LastModified, LastModifiedBy and HistoryEntries (date and text of events such as
        // "project created", "compiled", "upgraded"). Read-only.

        public sealed class ProjectHistoryItem
        {
            public DateTime Time { get; set; }
            public string Text { get; set; } = string.Empty;
        }

        public sealed class ProjectStatus
        {
            public string Name { get; set; } = string.Empty;
            public string? Path { get; set; }
            public bool IsModified { get; set; }
            public DateTime? LastModified { get; set; }
            public string? LastModifiedBy { get; set; }
            public int HistoryTotal { get; set; }
            public List<ProjectHistoryItem> History { get; } = new List<ProjectHistoryItem>();
            public List<string> Warnings { get; } = new List<string>();
        }

        public ProjectStatus GetProjectStatus(int historyCount)
        {
            return Operation.Run(_logger, nameof(GetProjectStatus), PortalErrorCode.InvalidState,
                () =>
                {
                    if (IsProjectNull())
                    {
                        throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal.");
                    }

                    var project = _project!;
                    var status = new ProjectStatus
                    {
                        Name = project.Name,
                        Path = project.Path?.FullName,
                        IsModified = project.IsModified
                    };

                    status.LastModified = ReadPart<DateTime?>(() => project.LastModified, "LastModified", status.Warnings);
                    status.LastModifiedBy = ReadPart(() => project.LastModifiedBy, "LastModifiedBy", status.Warnings);

                    var entries = ReadPart(() => project.HistoryEntries.ToList(), "HistoryEntries", status.Warnings);
                    if (entries != null)
                    {
                        status.HistoryTotal = entries.Count;
                        status.History.AddRange(entries
                            .Select(e => new ProjectHistoryItem { Time = e.DateTime, Text = e.Text ?? string.Empty })
                            .OrderByDescending(e => e.Time)
                            .Take(Math.Max(0, historyCount)));
                    }

                    return status;
                });
        }
    }
}
