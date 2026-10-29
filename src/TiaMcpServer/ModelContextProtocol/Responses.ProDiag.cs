using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseProDiagInfo : ResponseMessage
    {
        /// <summary>Blocks looked at.</summary>
        public int Total { get; set; }

        /// <summary>FBs in the ProDiag language, with their instance DBs.</summary>
        public List<ResponseProDiagBlock> ProDiagBlocks { get; set; } = new List<ResponseProDiagBlock>();

        /// <summary>Other FBs that define supervisions on their own members.</summary>
        public List<ResponseSupervisedBlock> SupervisedBlocks { get; set; } = new List<ResponseSupervisedBlock>();

        /// <summary>Instance DBs that name a ProDiag FB in 'AssignedProDiagFB'.</summary>
        public List<ResponseProDiagAssignment> Assignments { get; set; } = new List<ResponseProDiagAssignment>();

        public string? StoppedReason { get; set; }
    }

    public class ResponseProDiagBlock
    {
        public string Path { get; set; } = string.Empty;
        public string? Version { get; set; }
        public bool? InitialValueAcquisition { get; set; }
        public bool? UseCentralTimeStamp { get; set; }
        public List<string> InstanceDbs { get; set; } = new List<string>();
        public string? Error { get; set; }
    }

    public class ResponseSupervisedBlock
    {
        public string Path { get; set; } = string.Empty;
        public int Count { get; set; }

        /// <summary>The first supervisions: number, type, operand, status, delay, conditions, category numbers, associated values.</summary>
        public List<Dictionary<string, string?>> Supervisions { get; set; } = new List<Dictionary<string, string?>>();
        public string? Error { get; set; }
    }

    public class ResponseProDiagAssignment
    {
        public string Path { get; set; } = string.Empty;
        public string? InstanceOf { get; set; }
        public string ProDiagFb { get; set; } = string.Empty;
    }

    public class ResponseProDiagSupervisionsXlsx : ResponseMessage
    {
        /// <summary>"global supervisions of PLC_1" or the ProDiag FB path.</summary>
        public string Scope { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;

        /// <summary>SupervisionXlsxResultState from TIA Portal: Success or Failure.</summary>
        public string? State { get; set; }

        /// <summary>Log file TIA Portal wrote for the export/import, when it names one.</summary>
        public string? LogFile { get; set; }

        public string? Backup { get; set; }
        public string? BackupWarning { get; set; }
    }

    public class ResponseProDiagAssignmentSet : ResponseMessage
    {
        public string BlockPath { get; set; } = string.Empty;
        public string? MemberPath { get; set; }
        public string? OldValue { get; set; }

        /// <summary>Read back from TIA Portal after the write.</summary>
        public string? NewValue { get; set; }

        public string? Backup { get; set; }
        public string? BackupWarning { get; set; }
    }
}
