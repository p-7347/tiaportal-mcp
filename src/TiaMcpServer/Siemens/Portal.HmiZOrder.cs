using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Bring to front / send to back for WinCC Unified screen items.
        //
        // V20 Openness has no z-order call for Unified items: the order is the order of the
        // screen's item composition, and a new item is created on top. So "to front" re-creates
        // the named items (they land on top), and "to back" re-creates every other item, which
        // leaves the named ones below. Re-creating uses the CopyHmiScreen snapshot of each item
        // (properties, texts, dynamizations, events, faceplate interface), deletes it and creates
        // it again under the same name, one transaction per item so a refused write rolls back
        // only that item. Items with anything the snapshot cannot read are refused up front.

        public HmiZOrderResult SetHmiItemZOrder(string softwarePath, string screenName, IReadOnlyList<string> itemNames, string position)
        {
            return Operation.Run(_logger, nameof(SetHmiItemZOrder), PortalErrorCode.InvalidParams,
                () =>
                {
                    var toFront = position.Trim().ToLowerInvariant() switch
                    {
                        "front" => true,
                        "back" => false,
                        _ => throw new PortalException(PortalErrorCode.InvalidParams, $"'position' must be 'front' or 'back', not '{position}'.")
                    };

                    if (itemNames.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "'items' names no item.");
                    }

                    var screen = RequireHmiScreen(RequireHmiSoftware(softwarePath), screenName);
                    var all = screen.ScreenItems.ToList();
                    var missing = itemNames.Where(n => all.All(i => i == null || !string.Equals(i.Name, n, StringComparison.Ordinal))).ToList();

                    if (missing.Count > 0)
                    {
                        throw new PortalException(PortalErrorCode.NotFound,
                            $"Not on screen '{screenName}': {string.Join(", ", missing)}. Use 'GetHmiScreenItems' to list the items.");
                    }

                    var named = new HashSet<string>(itemNames, StringComparer.Ordinal);

                    if (!toFront && all.Any(i => i == null))
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            "The screen holds an item Openness cannot read (e.g. a PLC code view); it cannot be re-created, " +
                            "so the named items cannot be moved behind it. Use 'front' for the other items, or arrange it in the TIA Portal editor.");
                    }

                    // in their current order, so the moved items keep their order among themselves
                    var moved = all.Where(i => i != null && named.Contains(i.Name) == toFront).ToList();

                    var skipped = new List<string>();
                    var snapshots = moved.Select(i => ReadItemSnapshot(i, skipped)).ToList();

                    if (skipped.Count > 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            $"Nothing was changed: re-creating these items would lose what Openness cannot read: {string.Join("; ", skipped)}.");
                    }

                    var writer = new CopyWriter(Array.Empty<KeyValuePair<string, string>>(), new Dictionary<string, object?>(StringComparer.Ordinal));
                    var done = new List<string>();

                    foreach (var snapshot in snapshots)
                    {
                        try
                        {
                            InTransaction($"MCP: z-order '{snapshot.Name}' on '{screenName}'", () =>
                            {
                                writer.Step = $"delete item '{snapshot.Name}'";
                                screen.ScreenItems.Find(snapshot.Name)?.Delete();

                                WriteItemCopy(screen, snapshot, writer);
                            });
                        }
                        catch (Exception ex) when (ex is not PortalException)
                        {
                            throw new PortalException(PortalErrorCode.CreateFailed,
                                $"TIA Portal refused re-creating '{snapshot.Name}' at '{writer.Step}': {Operation.DescribeChain(ex)}. " +
                                $"That item is unchanged; {(done.Count > 0 ? "already moved: " + string.Join(", ", done) : "nothing was moved")}.", null, ex);
                        }

                        done.Add(snapshot.Name);
                    }

                    return new HmiZOrderResult
                    {
                        Recreated = done,
                        Order = screen.ScreenItems.Select(i => i?.Name ?? "(unreadable item)").ToList(),
                        Warnings = writer.Warnings
                    };
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("position", position));
        }
    }

    public class HmiZOrderResult
    {
        /// <summary>Items deleted and created again, in that order.</summary>
        public List<string> Recreated { get; set; } = new List<string>();

        /// <summary>All items of the screen afterwards, back to front.</summary>
        public List<string> Order { get; set; } = new List<string>();

        public List<string> Warnings { get; set; } = new List<string>();
    }
}
