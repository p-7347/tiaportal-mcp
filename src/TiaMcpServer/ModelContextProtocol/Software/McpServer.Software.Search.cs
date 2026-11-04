using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        #region insight

        [McpServerTool(Name = "FindInCode", Title = "Find in code", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to find where code mentions a name or text: a regular expression over the source text of blocks and PLC data types (all other filters match names only). Returns object path, line number and line. Objects are exported the first time (slow on a big PLC: narrow with 'nameFilter'), unchanged ones are then searched from memory. LAD and mixed blocks are searched as compact network text plus interface member names (format 'text'); uncompiled objects are listed in 'unsearchable'. Stops after about 40 s with 'nextStartAt': call again with startAt=nextStartAt")]
        public static ResponseCodeSearch FindInCode(
            [Description("softwarePath: PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("pattern: regular expression matched against each line, case-insensitive. Plain text works too")] string pattern,
            [Description("nameFilter: optional regular expression on object names, to limit which objects are searched. Empty (default) searches all of them")] string nameFilter = "",
            [Description("maxResults: stop after this many matching lines (default 200)")] int maxResults = 200,
            [Description("startAt: continue a search that stopped on time: the 'nextStartAt' of the previous call with the same pattern and nameFilter (default 0)")] int startAt = 0)
        {
            try
            {
                var result = Portal.FindInCode(softwarePath, pattern, nameFilter, maxResults, startAt);

                return new ResponseCodeSearch
                {
                    // Skipped objects go first: "0 matches" over a search that could not read half
                    // the candidates was read as "not used anywhere" (REQUESTS.md P11-1).
                    Message = (result.Unsearchable.Count > 0
                                  ? $"WARNING: {result.Unsearchable.Count} object(s) could not be searched, so the result is incomplete - see 'unsearchable'. "
                                  : string.Empty) +
                              $"'{pattern}' matched {result.Items.Count} line(s) across {result.ObjectsSearched} object(s)" +
                              (result.Truncated ? $", stopped at {maxResults}" : string.Empty) +
                              (result.NextStartAt.HasValue
                                  ? $". Stopped after about 40 s at object {result.NextStartAt.Value} of {result.ObjectsTotal} to stay inside the client timeout - " +
                                    $"call again with startAt={result.NextStartAt.Value} to search the rest"
                                  : string.Empty),
                    Pattern = result.Pattern,
                    Items = result.Items.Select(m => new ResponseCodeMatch
                    {
                        ObjectPath = m.ObjectPath,
                        Format = m.Format,
                        Line = m.Line,
                        Text = m.Text
                    }).ToList(),
                    ObjectsSearched = result.ObjectsSearched,
                    Unsearchable = result.Unsearchable,
                    Truncated = result.Truncated,
                    NextStartAt = result.NextStartAt,
                    ObjectsTotal = result.ObjectsTotal,
                    Meta = Ok(new JsonObject
                    {
                        ["matches"] = result.Items.Count,
                        ["objectsSearched"] = result.ObjectsSearched,
                        ["truncated"] = result.Truncated
                    })
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error searching '{softwarePath}' for '{pattern}': {ex.Message}", ex);
            }
        }

        #endregion
    }
}
