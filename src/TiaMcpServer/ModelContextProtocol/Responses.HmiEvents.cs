using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>SetHmiItemEventScript and DeleteHmiItemEventScript: one entry per item and event.</summary>
    public class ResponseHmiEventScriptWrite : ResponseMessage
    {
        public List<TiaMcpServer.Siemens.Portal.HmiEventScriptChange>? Changes { get; set; }
        public string? Backup { get; set; }
        public string? BackupWarning { get; set; }
    }
}
