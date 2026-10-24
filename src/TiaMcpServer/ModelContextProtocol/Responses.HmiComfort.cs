using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseComfortScreens : ResponseMessage
    {
        public IEnumerable<TiaMcpServer.Siemens.Portal.ComfortScreenInfo>? Items { get; set; }
    }

    /// <summary>The items of one classic WinCC screen, template or pop-up, in the shape of ResponseHmiScreenItems.</summary>
    public class ResponseComfortScreenItems : ResponseMessage
    {
        public string? Kind { get; set; }

        /// <summary>The template the screen is based on (screens only).</summary>
        public string? Template { get; set; }

        public int? Width { get; set; }
        public int? Height { get; set; }

        /// <summary>The screen's own plain attributes (includeAttributes only).</summary>
        public Dictionary<string, string>? Attributes { get; set; }

        public IEnumerable<TiaMcpServer.Siemens.Portal.ComfortScreenItemDescription>? Items { get; set; }

        /// <summary>Events of the screen itself (Loaded, Cleared, ...).</summary>
        public IEnumerable<TiaMcpServer.Siemens.Portal.ComfortEventDescription>? ScreenEvents { get; set; }

        /// <summary>The kept export file, when keepExportPath was given.</summary>
        public string? ExportFile { get; set; }
    }
}
