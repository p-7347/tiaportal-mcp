using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseTechnologyObjects : ResponseMessage
    {
        public List<ResponseTechnologyObject> Items { get; set; } = new List<ResponseTechnologyObject>();
    }

    public class ResponseTechnologyObject
    {
        /// <summary>Group path plus name, e.g. "Axes/SM110"; pass it as 'objectPath'.</summary>
        public string Path { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>System library element, e.g. TO_PositioningAxis, TO_ExternalEncoder, PID_Compact.</summary>
        public string? Type { get; set; }

        public string? Version { get; set; }
    }

    public class ResponseTechnologyObjectParameters : ResponseMessage
    {
        public ResponseTechnologyObject Object { get; set; } = new ResponseTechnologyObject();

        /// <summary>Parameters looked at (all of them unless the walk stopped early).</summary>
        public int Total { get; set; }

        /// <summary>Parameters matching 'regexName', before 'maxParameters' cuts the list.</summary>
        public int Matched { get; set; }

        public List<ResponseTechnologyObjectParameter> Parameters { get; set; } = new List<ResponseTechnologyObjectParameter>();

        public string? StoppedReason { get; set; }
    }

    public class ResponseTechnologyObjectParameter
    {
        public string Name { get; set; } = string.Empty;

        public string? Value { get; set; }

        /// <summary>Meaning of a coded value of an axis (e.g. "Negative direction"), when known.</summary>
        public string? ValueText { get; set; }

        public string? Error { get; set; }
    }

    public class ResponseTechnologyObjectParameterSet : ResponseMessage
    {
        public string ObjectPath { get; set; } = string.Empty;

        public string ParameterName { get; set; } = string.Empty;

        public string? OldValue { get; set; }

        /// <summary>Read back from TIA Portal after the write.</summary>
        public string? NewValue { get; set; }
    }
}
