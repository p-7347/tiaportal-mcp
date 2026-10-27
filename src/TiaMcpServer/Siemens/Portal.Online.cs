using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.Online;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region online connection (engineering station)

        // Openness's Online.OnlineProvider service exposes the engineering station's online
        // connection independently of the PLC's own Run/Stop state - GetService<OnlineProvider>()
        // on a Device or DeviceItem gives a plain public .State property and GoOnline()/
        // GoOffline() methods. None of GetOnlineState/GoOnline/GoOffline start, stop, or
        // otherwise command the PLC - only the engineering station's own online/diagnostic
        // connection changes. Deliberately does not add Run/Stop control - real PLC state
        // control stays out of scope as a safety call (see TODO.md).

        // Tries a Device first (e.g. a whole station like 'S7-1500/ET200MP station_1'), then a
        // DeviceItem (e.g. the CPU module within it, like 'PLC_1'), mirroring how GetDevice/
        // GetDeviceItem are both supported.
        private OnlineProvider? GetOnlineProvider(string path)
        {
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            var device = GetDevice(path);
            var provider = device?.GetService<OnlineProvider>();
            if (provider != null)
            {
                return provider;
            }

            var deviceItem = GetDeviceItem(path);
            return deviceItem?.GetService<OnlineProvider>();
        }

        /// <summary>
        /// 'PLC_1 is online ...' when a device or its CPU is online, else null. Reads only the
        /// engineering station's connection state; used as a hint on failed imports and exports.
        /// </summary>
        public string? DescribeOnlineDevices()
        {
            if (IsProjectNull())
            {
                return null;
            }

            var online = new List<string>();

            foreach (var device in GetDevices())
            {
                try
                {
                    if (device.GetService<OnlineProvider>()?.State == OnlineState.Online)
                    {
                        online.Add(device.Name);
                        continue;
                    }

                    foreach (DeviceItem item in device.DeviceItems)
                    {
                        if (item.GetService<OnlineProvider>()?.State == OnlineState.Online)
                        {
                            online.Add(item.Name);
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    // A device without an online service says nothing about the others.
                }
            }

            return online.Count == 0
                ? null
                : $"TIA Portal is online with {string.Join(", ", online.Select(n => $"'{n}'"))}, which blocks imports, exports and block changes. " +
                  $"Tell the user, or call 'GoOffline' with '{online[0]}' and retry.";
        }

        public OnlineState? GetOnlineState(string path)
        {
            _logger?.LogInformation($"Getting online state: {path}");
            return GetOnlineProvider(path)?.State;
        }

        public OnlineState GoOnline(string path)
        {
            _logger?.LogInformation($"Going online: {path}");

            var provider = GetOnlineProvider(path)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Device or device item not found, or has no online connection, at '{path}'");

            return provider.GoOnline();
        }

        public void GoOffline(string path)
        {
            _logger?.LogInformation($"Going offline: {path}");

            var provider = GetOnlineProvider(path)
                ?? throw new PortalException(PortalErrorCode.NotFound, $"Device or device item not found, or has no online connection, at '{path}'");

            provider.GoOffline();
        }

        #endregion
    }
}
