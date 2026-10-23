using System;
using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseProjectStatus : ResponseMessage
    {
        public string Name { get; set; } = string.Empty;

        public string? Path { get; set; }

        /// <summary>True when the project has changes that are not saved yet.</summary>
        public bool IsModified { get; set; }

        public DateTime? LastModified { get; set; }

        public string? LastModifiedBy { get; set; }

        /// <summary>All history entries of the project; 'History' lists the newest of them.</summary>
        public int HistoryTotal { get; set; }

        public List<ResponseProjectHistoryEntry>? History { get; set; }

        public List<string>? Warnings { get; set; }
    }

    public class ResponseProjectHistoryEntry
    {
        public DateTime Time { get; set; }

        public string Text { get; set; } = string.Empty;
    }
}
