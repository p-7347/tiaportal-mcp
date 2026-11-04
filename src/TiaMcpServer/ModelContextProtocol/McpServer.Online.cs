using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        #region online connection (engineering station)

        [McpServerTool(Name = "GetOnlineState", Title = "Get online state", ReadOnly = true, OpenWorld = false), Description("Get the engineering station's online connection state (Offline/Connecting/Online/...) for a device or device item. This is just the connection used for diagnostics/monitoring - it never starts, stops, or otherwise commands the PLC.")]
        public static ResponseOnlineState GetOnlineState(
            [Description("path: defines the path in the project structure to the device or device item")] string path)
        {
            try
            {
                var state = Portal.GetOnlineState(path);
                if (state == null)
                {
                    throw new McpException($"Device or device item not found, or has no online connection, at '{path}'");
                }

                return new ResponseOnlineState
                {
                    Message = $"Online state retrieved for '{path}'",
                    State = state.ToString(),
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving online state for '{path}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GoOnline", Title = "Go online", Destructive = false, Idempotent = true, OpenWorld = false), Description("Establish the engineering station's online connection to a device or device item for diagnostics/monitoring. Does not start, stop, or otherwise command the PLC - use TIA Portal itself for Run/Stop control. Confirm with the user before calling this against a real (non-PLCSIM) PLC.")]
        public static ResponseOnlineState GoOnline(
            [Description("path: defines the path in the project structure to the device or device item")] string path)
        {
            try
            {
                var state = Portal.GoOnline(path);

                return new ResponseOnlineState
                {
                    Message = $"Went online with '{path}'",
                    State = state.ToString(),
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error going online with '{path}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GoOffline", Title = "Go offline", Destructive = false, Idempotent = true, OpenWorld = false), Description("Disconnect the engineering station's online connection to a device or device item. Does not stop the PLC - the PLC keeps running regardless of this connection.")]
        public static ResponseGoOffline GoOffline(
            [Description("path: defines the path in the project structure to the device or device item")] string path)
        {
            try
            {
                Portal.GoOffline(path);

                return new ResponseGoOffline
                {
                    Message = $"Went offline from '{path}'",
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error going offline from '{path}': {ex.Message}", ex);
            }
        }

        #endregion
    }
}
