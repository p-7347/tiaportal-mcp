using Siemens.Engineering;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseMessage
    {
        public string? Message { get; set; }
        public JsonObject? Meta { get; set; }
    }

    public class ResponseAttributes : ResponseMessage
    {
        public IEnumerable<Attribute>? Attributes { get; set; }
    }

    public class ResponseSoftwareInfo : ResponseAttributes
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    public class ResponseDeviceInfo : ResponseAttributes
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    public class ResponseDeviceItemInfo : ResponseAttributes
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    public class ResponseBlockInfo : ResponseAttributes
    {
        //public string? Path { get; set; }
        public string? TypeName { get; set; }
        public string? Name { get; set; }
        public string? Namespace { get; set; }
        public string? ProgrammingLanguage { get; set; }
        public string? MemoryLayout { get; set; }
        public bool? IsConsistent { get; set; }
        public string? HeaderName { get; set; }
        public DateTime? ModifiedDate { get; set; }
        public bool? IsKnowHowProtected { get; set; }
        public string? Description { get; set; }
    }
    public class ResponseBlocksWithHierarchy : ResponseMessage
    {
        public BlockGroupInfo? Root { get; set; }
    }

    public class ResponseTypeInfo : ResponseAttributes
    {
        //public string? Path { get; set; }
        public string? Name { get; set; }
        public string? TypeName { get; set; }
        public string? Namespace { get; set; }
        public bool? IsConsistent { get; set; }
        public DateTime? ModifiedDate { get; set; }
        public bool? IsKnowHowProtected { get; set; }
        public string? Description { get; set; }
    }

    public class ResponseProjectInfo : ResponseAttributes
    {
        //public string? Path { get; set; }
        public string? Name { get; set; }
    }

    public class ResponseConnect : ResponseMessage
    {
    }

    public class ResponseTiaPortalInstance
    {
        public int Id { get; set; }
        public string? ProjectPath { get; set; }
        public string? Mode { get; set; }
    }

    public class ResponseTiaPortalInstances : ResponseMessage
    {
        public IEnumerable<ResponseTiaPortalInstance>? Items { get; set; }
    }

    public class ResponseDisconnect : ResponseMessage
    {
    }

    public class ResponseGsdReference
    {
        public string? Path { get; set; }
        public string? Name { get; set; }
        public string? GsdId { get; set; }
        public string? GsdName { get; set; }
        public string? GsdType { get; set; }
        public bool IsProfibus { get; set; }
        public bool IsProfinet { get; set; }
    }

    public class ResponseGsdDependencies : ResponseMessage
    {
        public IEnumerable<ResponseGsdReference>? Items { get; set; }
    }

    public class ResponseState : ResponseMessage
    {
        public bool? IsConnected { get; set; }
        public string? Project { get; set; }
        public string? Session { get; set; }
    }

    public class ResponseTiaInstallation
    {
        public int MajorVersion { get; set; }
        public string? InstallPath { get; set; }
        public bool EngineeringExists { get; set; }
        public bool PortalExeExists { get; set; }
    }

    public class ResponseDoctor : ResponseMessage
    {
        public string? Report { get; set; }
        public bool? IsConnected { get; set; }
        public int? ActiveTiaMajorVersion { get; set; }
        public string? ProjectName { get; set; }
        public string? ProjectPath { get; set; }
        public bool? IsUserInGroup { get; set; }
        public IEnumerable<ResponseTiaInstallation>? Installations { get; set; }
        public string? ExportRoot { get; set; }
    }

    public class ResponseGetProjects : ResponseMessage
    {
        public IEnumerable<ResponseProjectInfo>? Items { get; set; }
    }

    public class ResponseOpenProject : ResponseMessage
    {
    }

    public class ResponseSaveProject : ResponseMessage
    {
    }

    public class ResponseSaveAsProject : ResponseMessage
    {
    }

    public class ResponseCloseProject : ResponseMessage
    {
    }

    public class ResponseTree : ResponseMessage
    {
        public string? Tree { get; set; }
    }

    public class ResponseProjectTree : ResponseMessage
    {
        public string? Tree { get; set; }
    }

    public class ResponseSoftwareTree : ResponseMessage
    {
        public string? Tree { get; set; }
    }

    public class ResponseDevices : ResponseMessage
    {
        public IEnumerable<ResponseDeviceInfo>? Items { get; set; }
    }

    public class ResponseOnlineState : ResponseMessage
    {
        public string? State { get; set; }
    }

    public class ResponseGoOffline : ResponseMessage
    {
    }

    public class ResponseCompileSoftware : ResponseMessage
    {
    }
    
    public class ResponseBlocks : ResponseMessage
    {
        public IEnumerable<ResponseBlockInfo>? Items { get; set; }
    }

    public class ResponseExportBlock : ResponseMessage
    {
    }

    public class ResponseImportBlock : ResponseMessage
    {
    }

    public class ResponseExportBlocks : ResponseMessage
    {
        public IEnumerable<ResponseBlockInfo>? Items { get; set; }
        public IEnumerable<ResponseBlockInfo>? Inconsistent { get; set; }
    }

    public class ResponseTypes : ResponseMessage
    {
        public IEnumerable<ResponseTypeInfo>? Items { get; set; }
    }

    public class ResponseExportType : ResponseMessage
    {
    }

    public class ResponseImportType : ResponseMessage
    {
    }

    public class ResponseExportTypes : ResponseMessage
    {
        public IEnumerable<ResponseTypeInfo>? Items { get; set; }
        public IEnumerable<ResponseTypeInfo>? Inconsistent { get; set; }
    }

    public class ResponseExportAsDocuments : ResponseMessage
    {
    }

    public class ResponseExportBlocksAsDocuments : ResponseMessage
    {
        public IEnumerable<ResponseBlockInfo>? Items { get; set; }
    }

    public class ResponseImportFromDocuments : ResponseMessage
    {
    }

    public class ResponseImportBlocksFromDocuments : ResponseMessage
    {
        public IEnumerable<ResponseBlockInfo>? Items { get; set; }
    }

    public class ResponseTagTableInfo : ResponseAttributes
    {
        public string? Name { get; set; }
        public string? TypeName { get; set; }
        public bool? IsDefault { get; set; }
        public string? Description { get; set; }
    }

    public class ResponseTagTables : ResponseMessage
    {
        public IEnumerable<ResponseTagTableInfo>? Items { get; set; }
    }

    public class ResponseTagInfo
    {
        public string? Name { get; set; }
        public string? DataTypeName { get; set; }
        public string? LogicalAddress { get; set; }
        public string? Comment { get; set; }
        public IEnumerable<Attribute>? Attributes { get; set; }
    }

    public class ResponseTags : ResponseMessage
    {
        public IEnumerable<ResponseTagInfo>? Items { get; set; }
    }

    public class ResponseExportTagTable : ResponseMessage
    {
    }

    public class ResponseExternalSourceInfo : ResponseAttributes
    {
        public string? Name { get; set; }
    }

    public class ResponseExternalSources : ResponseMessage
    {
        public IEnumerable<ResponseExternalSourceInfo>? Items { get; set; }
    }

    public class ResponseImportExternalSource : ResponseMessage
    {
        public string? Name { get; set; }
    }

    public class ResponseGenerateBlocksFromSource : ResponseMessage
    {
        public IEnumerable<string>? GeneratedObjectNames { get; set; }
    }

    public class ResponseDeleteExternalSource : ResponseMessage
    {
    }

    public class ResponseExportSourceFromBlocks : ResponseMessage
    {
    }

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

    public class ResponseCrossReferenceLocation
    {
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? TypeName { get; set; }
        public string? Access { get; set; }
        public string? ReferenceType { get; set; }
        public string? ReferenceLocation { get; set; }
        public string? ReferencedAsName { get; set; }
    }

    public class ResponseCrossReferenceReference
    {
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? Address { get; set; }
        public string? Device { get; set; }
        public string? TypeName { get; set; }
        public IEnumerable<ResponseCrossReferenceLocation>? Locations { get; set; }
    }

    public class ResponseCrossReferenceSource
    {
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? Address { get; set; }
        public string? Device { get; set; }
        public string? TypeName { get; set; }
        public IEnumerable<ResponseCrossReferenceReference>? References { get; set; }
        public IEnumerable<ResponseCrossReferenceSource>? Children { get; set; }
    }

    public class ResponseCrossReferences : ResponseMessage
    {
        public IEnumerable<ResponseCrossReferenceSource>? Sources { get; set; }
    }
}
