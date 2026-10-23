using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseHmiTagUsage : ResponseMessage
    {
        /// <summary>HMI tags whose name or PLC tag matches 'tag'.</summary>
        public List<ResponseHmiTagUsageTag>? Tags { get; set; }

        public List<ResponseHmiTagUse>? Uses { get; set; }

        public int ScreensTotal { get; set; }

        public int ScreensSearched { get; set; }

        /// <summary>Screens read from TIA Portal in this call; the others came from the server's index.</summary>
        public int ScreensReadNow { get; set; }

        public int AlarmsSearched { get; set; }

        public string? StoppedReason { get; set; }

        public List<string>? Warnings { get; set; }
    }

    public class ResponseHmiTagUsageTag
    {
        public string Name { get; set; } = string.Empty;
        public string? TagTable { get; set; }
        public string? PlcTag { get; set; }
        public string? Connection { get; set; }
    }

    public class ResponseHmiTagUse
    {
        /// <summary>Dynamization, FaceplateInterface, ItemEvent, ScreenEvent, ScriptDynamization or Alarm.</summary>
        public string Kind { get; set; } = string.Empty;

        public string? Screen { get; set; }

        /// <summary>Screen item, or the alarm name for Kind 'Alarm'.</summary>
        public string? Item { get; set; }

        /// <summary>Property, event or alarm field, e.g. 'BackColor', 'Tapped', 'RaisedStateTag'.</summary>
        public string? Where { get; set; }

        /// <summary>The reference as written, e.g. 'Motor.Speed'.</summary>
        public string Reference { get; set; } = string.Empty;

        public string HmiTag { get; set; } = string.Empty;

        /// <summary>Scripts only: the first line that names the tag.</summary>
        public string? Line { get; set; }
    }
}
