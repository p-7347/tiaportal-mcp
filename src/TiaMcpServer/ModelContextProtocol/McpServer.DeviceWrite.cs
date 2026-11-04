using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        #region device write CRUD

        [WriteTool]
        [McpServerTool(Name = "CreateDevice", Title = "Create device", Destructive = true, Idempotent = false, OpenWorld = false),
         Description("Create a single-item hardware device. Most devices (CPU stations, drives, HMI panels, maybe switches) are station+item pairs and fail here with 'the object cannot be created, it is of the wrong type': use 'CreateDeviceWithItem' for those. typeIdentifier must come from 'FindHardwareCatalogEntries' ('OrderNumber:<article>/<version>'); the 'System:Device.X' identifiers of placed devices are rejected")]
        public static ResponseCreated CreateDevice(
            [Description("typeIdentifier: a creation-ready catalog TypeIdentifier from FindHardwareCatalogEntries, e.g. 'OrderNumber:6SL3210-1KE11-8AF2/4.7.14' - NOT the TypeIdentifier attribute off an existing device")] string typeIdentifier,
            [Description("name: name for the new device")] string name,
            [Description("groupPath: path to the device group to create it under, e.g. 'Group/Subgroup' (empty for the project root)")] string groupPath = "")
        {
            return Guarded(nameof(CreateDevice), () =>
            {
                var device = Portal.CreateDevice(typeIdentifier, name, groupPath);
                return Created("Device", device.Name, JoinPath(groupPath, device.Name));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "CreateDeviceWithItem", Title = "Create device with item", Destructive = true, Idempotent = false, OpenWorld = false),
         Description("Create a hardware device with a station and its first item in one call, e.g. a CPU station or drive (Device 'S7-1500/ET200MP station_1' containing 'PLC_1'); a CPU comes with its real interfaces. typeIdentifier must come from 'FindHardwareCatalogEntries' ('OrderNumber:<article>/<version>'); 'System:Device.X' identifiers of placed devices are rejected. The resulting device name follows deviceItemName, not name")]
        public static ResponseCreated CreateDeviceWithItem(
            [Description("typeIdentifier: a creation-ready catalog TypeIdentifier from FindHardwareCatalogEntries, e.g. 'OrderNumber:6ES7 516-3AN01-0AB0/V2.1' - NOT the TypeIdentifier attribute off an existing device")] string typeIdentifier,
            [Description("name: name for the new device (station) - note this does not end up as the queryable Device.Name; use deviceItemName for the path you'll query/reference afterward")] string name,
            [Description("deviceItemName: name for the first sub-item (e.g. the CPU module) - this is what the resulting device is actually named/found under")] string deviceItemName,
            [Description("groupPath: path to the device group to create it under, e.g. 'Group/Subgroup' (empty for the project root)")] string groupPath = "")
        {
            return Guarded(nameof(CreateDeviceWithItem), () =>
            {
                var device = Portal.CreateDeviceWithItem(typeIdentifier, name, deviceItemName, groupPath);
                return Created("Device", device.Name, JoinPath(groupPath, device.Name));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteDevice", Title = "Delete device", Destructive = true, Idempotent = true, OpenWorld = false),
         Description("Delete a hardware device. Dry run unless confirm=true: first call previews, second with confirm=true deletes. Hardware is tied to wiring and GSD matching and TIA's undo ends at the next save, so ask the user before confirm=true on a real project")]
        public static ResponseDeviceDelete DeleteDevice(
            [Description("devicePath: full path to the device")] string devicePath,
            [Description("confirm: must be explicitly true to actually delete - false (default) only previews what would be deleted")] bool confirm = false)
        {
            return Guarded(nameof(DeleteDevice), () =>
            {
                var (executed, deviceName, typeName) = Portal.DeleteDevice(devicePath, confirm);

                return new ResponseDeviceDelete
                {
                    Message = executed
                        ? $"Device '{deviceName}' ({typeName}) deleted. {SaveHint}"
                        : $"Preview only (confirm=false, nothing changed) - would delete device '{deviceName}' ({typeName})",
                    Executed = executed,
                    DevicePath = devicePath,
                    DeviceName = deviceName,
                    TypeName = typeName,
                    Meta = OkMeta()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "CreateDeviceGroup", Title = "Create device group", Destructive = true, Idempotent = false, OpenWorld = false),
         Description("Create a new device group (folder) in the project, for organizing devices.")]
        public static ResponseCreated CreateDeviceGroup(
            [Description("parentGroupPath: path to the parent group to create the new group under, e.g. 'Group/Subgroup' (empty for the project root)")] string parentGroupPath,
            [Description("name: name for the new group")] string name)
        {
            return Guarded(nameof(CreateDeviceGroup), () =>
            {
                var group = Portal.CreateDeviceGroup(parentGroupPath, name);
                return Created("Device group", group.Name, JoinPath(parentGroupPath, group.Name));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteDeviceGroup", Title = "Delete device group", Destructive = true, Idempotent = true, OpenWorld = false),
         Description("Delete a device group (folder) from the project - also deletes every device inside it. Confirm with the user before calling this against a real (non-disposable) project.")]
        public static ResponseDeleted DeleteDeviceGroup(
            [Description("groupPath: full path to the group, e.g. 'Group/Subgroup'")] string groupPath)
        {
            return Guarded(nameof(DeleteDeviceGroup), () =>
            {
                Portal.DeleteDeviceGroup(groupPath);
                return Deleted("Device group", groupPath);
            });
        }

        #endregion
    }
}
