using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseHmiTagTableInfo
    {
        public string? Name { get; set; }
    }

    public class ResponseHmiTagTables : ResponseMessage
    {
        public IEnumerable<ResponseHmiTagTableInfo>? Items { get; set; }
    }

    public class ResponseHmiTagInfo
    {
        public string? Name { get; set; }
        public string? DataType { get; set; }
        public string? HmiDataType { get; set; }
        public string? Address { get; set; }
        public string? Connection { get; set; }
        public string? PlcName { get; set; }
        public string? PlcTag { get; set; }
        public string? AccessMode { get; set; }
        public string? AcquisitionMode { get; set; }
        public string? Scope { get; set; }
        public string? TagType { get; set; }
        public string? Comment { get; set; }
    }

    public class ResponseHmiTags : ResponseMessage
    {
        public IEnumerable<ResponseHmiTagInfo>? Items { get; set; }
    }

    public class ResponseHmiScreenInfo
    {
        public string? Name { get; set; }
        public string? DisplayName { get; set; }

        /// <summary>DisplayName as plain text without the rich text markup; null when empty.</summary>
        public string? DisplayText { get; set; }

        public int? ScreenNumber { get; set; }
        public uint? Width { get; set; }
        public uint? Height { get; set; }
    }

    public class ResponseHmiScreens : ResponseMessage
    {
        public IEnumerable<ResponseHmiScreenInfo>? Items { get; set; }
    }

    public class ResponseHmiAlarmInfo
    {
        public string? Name { get; set; }
        public string? EventText { get; set; }
        public string? InfoText { get; set; }
        public string? AlarmClass { get; set; }
        public string? Area { get; set; }
        public byte? Priority { get; set; }
        public string? TriggerAddress { get; set; }
        public string? Condition { get; set; }
    }

    public class ResponseHmiAlarms : ResponseMessage
    {
        public IEnumerable<ResponseHmiAlarmInfo>? Items { get; set; }
    }

    public class ResponseHmiTextListInfo
    {
        public string? Name { get; set; }
    }

    public class ResponseHmiTextLists : ResponseMessage
    {
        public IEnumerable<ResponseHmiTextListInfo>? Items { get; set; }
    }

    public class ResponseHmiScreenItems : ResponseMessage
    {
        public IEnumerable<TiaMcpServer.Siemens.HmiScreenItemDescription>? Items { get; set; }

        /// <summary>Event handler scripts of the screen itself (includeScripts only).</summary>
        public IEnumerable<TiaMcpServer.Siemens.HmiEventDescription>? ScreenEvents { get; set; }
    }

    /// <summary>Result of an HMI screen item write: the item as read back after the change.</summary>
    public class ResponseHmiScreenItemWrite : ResponseMessage
    {
        public TiaMcpServer.Siemens.HmiScreenItemDescription? Item { get; set; }
    }

    /// <summary>Result of an HMI tag write: the tag as read back after the change.</summary>
    public class ResponseHmiTagBatch : ResponseMessage
    {
        /// <summary>CreateHmiTags: the tags as read back after creating them.</summary>
        public List<TiaMcpServer.Siemens.Portal.HmiTagState>? Created { get; set; }

        /// <summary>CreateHmiTags with skipExisting: names that already existed.</summary>
        public List<string>? Skipped { get; set; }

        /// <summary>UpdateHmiTags: each tag before and after.</summary>
        public List<TiaMcpServer.Siemens.Portal.HmiTagUpdate>? Updated { get; set; }

        public string? Backup { get; set; }
        public string? BackupWarning { get; set; }
    }

    public class ResponseHmiTagWrite : ResponseMessage
    {
        public ResponseHmiTagInfo? Tag { get; set; }
    }
}
