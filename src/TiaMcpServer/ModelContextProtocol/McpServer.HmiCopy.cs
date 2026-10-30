using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        [WriteTool]
        [McpServerTool(Name = "CopyHmiScreen", Title = "Copy HMI screen", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Copy a WinCC Unified screen to a new screen in the same folder, item by item: every writable property (position, size, colors, fonts, ...), texts, tag and script dynamizations, event scripts and faceplate interfaces. 'replace' rewrites text in every copied string (tag names, texts, scripts, interface values), e.g. 'Servo01=>Servo02' to make the screen of the next axis. Openness has no screen copy of its own, so anything it cannot read is listed under 'skipped'. A refused write aborts the whole copy")]
        public static ResponseHmiScreenCopy CopyHmiScreen(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: the screen to copy, as 'GetHmiScreens' lists it")] string screenName,
            [Description("newScreenName: name of the new screen")] string newScreenName,
            [Description("replace: optional text replacements applied in order to every copied string, as 'from=>to' pairs separated by ';', e.g. 'Servo01=>Servo02;SM118=>SM119'. Matching is case-sensitive")] string replace = "",
            [Description("screenNumber: optional screen number of the new screen")] ushort? screenNumber = null)
        {
            var replacements = ParseReplacements(replace);

            // Read outside the write transaction: a failed read is skipped and reported there, while
            // any exception inside the transaction would make TIA Portal refuse the commit.
            HmiScreenSnapshot snapshot;

            try
            {
                snapshot = Portal.ReadHmiScreenSnapshot(softwarePath, screenName);
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }

            return Guarded(nameof(CopyHmiScreen), () =>
            {
                var result = Portal.WriteHmiScreenCopy(softwarePath, snapshot, newScreenName, replacements, screenNumber);

                return new ResponseHmiScreenCopy
                {
                    Message = $"Screen '{screenName}' copied to '{newScreenName}': {result.Items} item(s), {result.Attributes} propert(ies), " +
                              $"{result.Texts} text(s), {result.Dynamizations} dynamization(s), {result.Events} event script(s); " +
                              $"{result.Replaced} string(s) changed by 'replace'." +
                              (snapshot.Skipped.Count + result.Warnings.Count > 0
                                  ? $" WARNING: {snapshot.Skipped.Count + result.Warnings.Count} thing(s) not copied - see 'skipped' and 'warnings'."
                                  : string.Empty) +
                              $" {SaveHint}",
                    ScreenName = result.Screen?.Name,
                    ScreenNumber = result.Screen?.ScreenNumber,
                    Items = result.Items,
                    Properties = result.Attributes,
                    Texts = result.Texts,
                    Dynamizations = result.Dynamizations,
                    Events = result.Events,
                    Replaced = result.Replaced,
                    Skipped = snapshot.Skipped.Count > 0 ? snapshot.Skipped : null,
                    Warnings = result.Warnings.Count > 0 ? result.Warnings : null,
                    Meta = OkMeta()
                };
            }, oneTransaction: false);
        }

        [WriteTool]
        [McpServerTool(Name = "SetHmiItemZOrder", Title = "Bring HMI items to front or send to back", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to put WinCC Unified screen items in front of or behind the others (bring to front / send to back). Openness has no z-order call, so the items are deleted and created again on top, with everything 'CopyHmiScreen' copies (properties, texts, dynamizations, event scripts, faceplate interfaces) and the same names: 'front' re-creates the named items, 'back' re-creates all the other items. Refused when an affected item has something Openness cannot read. Ask the user to close the screen in the TIA Portal editor first")]
        public static ResponseHmiZOrder SetHmiItemZOrder(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("items: names of the items to move, separated by commas or line breaks; they keep their order among themselves")] string items,
            [Description("position: 'front' (on top of all other items) or 'back' (behind all other items)")] string position)
        {
            return Guarded(nameof(SetHmiItemZOrder), () =>
            {
                var result = Portal.SetHmiItemZOrder(softwarePath, screenName, SplitNames(items), position);

                return new ResponseHmiZOrder
                {
                    Message = $"{result.Recreated.Count} item(s) re-created on '{screenName}' to move '{items}' to the {position.Trim().ToLowerInvariant()}. " +
                              "Order back to front: " + string.Join(", ", result.Order) + "." +
                              (result.Warnings.Count > 0 ? $" WARNING: {result.Warnings.Count} thing(s) not re-created - see 'warnings'." : string.Empty) +
                              $" {SaveHint}",
                    Recreated = result.Recreated,
                    Order = result.Order,
                    Warnings = result.Warnings.Count > 0 ? result.Warnings : null,
                    Meta = OkMeta()
                };
            }, oneTransaction: false);
        }

        private static readonly TimeSpan CopyBudget = TimeSpan.FromSeconds(40);

        // The snapshot of the last series source, so a continued call (from=nextNumber) skips re-reading it
        private static readonly TimeSpan SnapshotReuse = TimeSpan.FromMinutes(10);
        private static readonly object SeriesLock = new object();
        private static (string Key, DateTime ReadAt, HmiScreenSnapshot Snapshot)? _seriesSnapshot;

        [WriteTool]
        [McpServerTool(Name = "CopyHmiScreens", Title = "Copy HMI screen to a series", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Make a numbered series of WinCC Unified screens from one screen, e.g. the screens of servo 2..60 from the screen of servo 1. Each copy works like 'CopyHmiScreen'; in 'newScreenName' and 'replace' the placeholder %n% is the number, %n2%/%n3% the number with leading zeros. Each screen and each of its items is its own transaction. Stops before a copy that would end after about 40 s, to stay inside the client timeout and returns 'nextNumber' - call again with from=nextNumber to continue. Screens that already exist are skipped, so a repeated call is safe. A continued call within 10 minutes reuses the source as the previous call read it")]
        public static ResponseHmiScreenCopies CopyHmiScreens(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: the screen to copy, as 'GetHmiScreens' lists it")] string screenName,
            [Description("newScreenName: name pattern of the new screens, e.g. '31%n2% SV1%n2%'")] string newScreenName,
            [Description("from: first number of the series")] int from,
            [Description("to: last number of the series (inclusive)")] int to,
            [Description("replace: optional 'from=>to' pairs separated by ';' applied to every copied string, with %n%/%n2%/%n3% placeholders, e.g. 'S{1}=>S{%n%};Axis{1}=>Axis{%n%};FP{1}=>FP{%n%};Servo_1=>Servo_%n%'")] string replace = "",
            [Description("screenNumberStart: optional screen number of the first new screen; the next ones count up by one. Empty leaves the numbers as TIA Portal gives them")] int? screenNumberStart = null)
        {
            if (to < from)
            {
                throw new McpException("'to' must not be smaller than 'from'.");
            }

            if (!newScreenName.Contains("%n"))
            {
                throw new McpException("'newScreenName' needs a placeholder %n%, %n2% or %n3%, or every copy would get the same name.");
            }

            ParseReplacements(Number(replace, from)); // validate the format once up front

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var key = $"{Portal.GetHashCode()}|{softwarePath}|{screenName}";
            HmiScreenSnapshot snapshot;
            bool reused;

            lock (SeriesLock)
            {
                var cached = _seriesSnapshot;
                reused = cached.HasValue && cached.Value.Key == key && DateTime.UtcNow - cached.Value.ReadAt < SnapshotReuse;
                snapshot = reused ? cached!.Value.Snapshot : null!;
            }

            if (!reused)
            {
                try
                {
                    snapshot = Portal.ReadHmiScreenSnapshot(softwarePath, screenName);
                }
                catch (PortalException pex)
                {
                    throw new McpException(pex.Message, pex);
                }

                lock (SeriesLock)
                {
                    _seriesSnapshot = (key, DateTime.UtcNow, snapshot);
                }
            }

            var snapshotSeconds = clock.Elapsed.TotalSeconds;

            var longest = TimeSpan.Zero;
            var created = new List<string>();
            var existing = new List<string>();
            var warnings = new List<string>();
            int? next = null;

            for (var n = from; n <= to; n++)
            {
                // stop before a copy that would likely run past the budget (a full servo screen takes ~25 s)
                if (created.Count > 0 && clock.Elapsed + longest > CopyBudget)
                {
                    next = n;
                    break;
                }

                var name = Number(newScreenName, n);

                if (Portal.HmiScreenExists(softwarePath, name))
                {
                    existing.Add(name);
                    continue;
                }

                var number = screenNumberStart.HasValue ? (ushort?)(screenNumberStart.Value + (n - from)) : null;
                var replacements = ParseReplacements(Number(replace, n));

                var started = clock.Elapsed;
                var result = Guarded(nameof(CopyHmiScreens),
                    () => Portal.WriteHmiScreenCopy(softwarePath, snapshot, name, replacements, number), oneTransaction: false);

                longest = TimeSpan.FromTicks(Math.Max(longest.Ticks, (clock.Elapsed - started).Ticks));
                created.Add(name);

                if (warnings.Count == 0 && result.Warnings.Count > 0)
                {
                    warnings.AddRange(result.Warnings);
                }
            }

            return new ResponseHmiScreenCopies
            {
                Message = $"{created.Count} screen(s) created from '{screenName}'" +
                          (existing.Count > 0 ? $", {existing.Count} already existed and were skipped" : string.Empty) +
                          (next.HasValue
                              ? $". Stopped after {clock.Elapsed.TotalSeconds:0} s to stay inside the client timeout - call again with from={next.Value} to continue."
                              : ".") +
                          $" {SaveHint}",
                Created = created,
                SnapshotReused = reused ? true : (bool?)null,
                SnapshotSeconds = Math.Round(snapshotSeconds, 1),
                SecondsPerScreen = created.Count > 0 ? Math.Round((clock.Elapsed.TotalSeconds - snapshotSeconds) / created.Count, 1) : (double?)null,
                AlreadyExisted = existing.Count > 0 ? existing : null,
                NextNumber = next,
                Skipped = snapshot.Skipped.Count > 0 ? snapshot.Skipped : null,
                Warnings = warnings.Count > 0 ? warnings : null,
                Meta = OkMeta()
            };
        }

        /// <summary>Fills %n%, %n2% and %n3% with the number, the latter two zero-padded.</summary>
        private static string Number(string pattern, int n) =>
            (pattern ?? string.Empty)
                .Replace("%n3%", n.ToString("000"))
                .Replace("%n2%", n.ToString("00"))
                .Replace("%n%", n.ToString());

        private static List<KeyValuePair<string, string>> ParseReplacements(string replace)
        {
            var pairs = new List<KeyValuePair<string, string>>();

            foreach (var entry in (replace ?? string.Empty).Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var text = entry.Trim('\r', ' ');

                if (text.Length == 0)
                {
                    continue;
                }

                var arrow = text.IndexOf("=>", StringComparison.Ordinal);

                if (arrow <= 0)
                {
                    throw new McpException($"'replace' entry '{text}' is not 'from=>to'. Example: 'Servo01=>Servo02;SM118=>SM119'.");
                }

                pairs.Add(new KeyValuePair<string, string>(text.Substring(0, arrow), text.Substring(arrow + 2)));
            }

            return pairs;
        }
    }
}
