using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseHmiItemProperties : ResponseMessage
    {
        public string? Item { get; set; }
        public List<TiaMcpServer.Siemens.Portal.HmiItemPropertyInfo>? Properties { get; set; }
    }

    /// <summary>SetHmiScreenItemProperties and CopyHmiItemStyle: every changed value before and after.</summary>
    public class ResponseHmiItemPropertyWrite : ResponseMessage
    {
        public List<string>? Items { get; set; }
        public List<TiaMcpServer.Siemens.Portal.HmiItemPropertyChange>? Changes { get; set; }

        /// <summary>CopyHmiItemStyle: properties a target does not have, per target.</summary>
        public List<string>? Skipped { get; set; }

        public string? Backup { get; set; }
        public string? BackupWarning { get; set; }
    }
}
