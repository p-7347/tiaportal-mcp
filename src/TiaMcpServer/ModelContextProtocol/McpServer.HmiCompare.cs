using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        [McpServerTool(Name = "CompareHmiScreens", Title = "Compare HMI screens", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("Compare two WinCC Unified screens item by item: items on one side only, and every differing property, text, dynamization, event script and faceplate interface value. 'replace' is applied to the first screen first, so 'S{1}=>S{2}' lists only what breaks the pattern, e.g. to check 'CopyHmiScreens' results or find hand edits")]
        public static ResponseHmiScreenCompare CompareHmiScreens(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: the first screen (the reference), as 'GetHmiScreens' lists it")] string screenName,
            [Description("otherScreenName: the second screen")] string otherScreenName,
            [Description("replace: optional 'from=>to' pairs separated by ';' applied to every string of the first screen before comparing, e.g. 'S{1}=>S{2};Servo_1=>Servo_2'")] string replace = "",
            [Description("otherSoftwarePath: optional HMI software of the second screen when it is on another HMI; empty uses 'softwarePath'")] string otherSoftwarePath = "",
            [Description("maxDifferences: maximum number of differences to list (default 200); 'differenceCount' is always the full count")] int maxDifferences = 200)
        {
            var replacements = ParseReplacements(replace);
            var otherPath = string.IsNullOrWhiteSpace(otherSoftwarePath) ? softwarePath : otherSoftwarePath;

            HmiScreenSnapshot first, second;

            try
            {
                first = Portal.ReadHmiScreenSnapshot(softwarePath, screenName);
                second = Portal.ReadHmiScreenSnapshot(otherPath, otherScreenName);
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }

            var left = Flatten(first, text => replacements.Aggregate(text, (s, p) => s.Replace(p.Key, p.Value)));
            var right = Flatten(second, text => text);

            var leftItems = new HashSet<string>(first.Items.Select(i => i.Name), StringComparer.Ordinal);
            var rightItems = new HashSet<string>(second.Items.Select(i => i.Name), StringComparer.Ordinal);
            var onlyFirst = first.Items.Select(i => i.Name).Where(n => !rightItems.Contains(n)).ToList();
            var onlySecond = second.Items.Select(i => i.Name).Where(n => !leftItems.Contains(n)).ToList();

            // Paths below items that exist on one side only are covered by the item lists
            bool OnBothSides(string path)
            {
                var item = path.Split('|')[0];
                return item == "screen" || (leftItems.Contains(item) && rightItems.Contains(item));
            }

            var differences = new List<HmiScreenDifference>();

            foreach (var path in left.Keys.Union(right.Keys).Where(OnBothSides).OrderBy(p => p, StringComparer.Ordinal))
            {
                left.TryGetValue(path, out var a);
                right.TryGetValue(path, out var b);

                if (!string.Equals(a, b, StringComparison.Ordinal))
                {
                    differences.Add(new HmiScreenDifference { Path = path.Replace('|', '.'), First = a, Second = b });
                }
            }

            var skipped = first.Skipped.Select(s => $"{screenName}: {s}").Concat(second.Skipped.Select(s => $"{otherScreenName}: {s}")).ToList();
            var limit = Math.Max(0, maxDifferences);
            var same = onlyFirst.Count == 0 && onlySecond.Count == 0 && differences.Count == 0;

            return new ResponseHmiScreenCompare
            {
                Message = same
                    ? $"'{screenName}' and '{otherScreenName}' are the same" + (replacements.Count > 0 ? " after 'replace'" : string.Empty) +
                      $" ({first.Items.Count} item(s), {left.Count} value(s) compared)."
                    : $"'{screenName}' vs '{otherScreenName}'" + (replacements.Count > 0 ? " after 'replace'" : string.Empty) +
                      $": {onlyFirst.Count} item(s) only in the first, {onlySecond.Count} only in the second, {differences.Count} differing value(s)" +
                      (differences.Count > limit ? $" (first {limit} listed)" : string.Empty) + ".",
                Same = same,
                OnlyInFirst = onlyFirst.Count > 0 ? onlyFirst : null,
                OnlyInSecond = onlySecond.Count > 0 ? onlySecond : null,
                DifferenceCount = differences.Count,
                Differences = differences.Count > 0 ? differences.Take(limit).ToList() : null,
                Skipped = skipped.Count > 0 ? skipped : null,
                Meta = OkMeta()
            };
        }

        /// <summary>Every value of a snapshot keyed by a stable path ('item|attribute|...'), strings passed through 'map'.</summary>
        private static Dictionary<string, string?> Flatten(HmiScreenSnapshot snapshot, Func<string, string> map)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);

            FlattenUi(values, "screen", snapshot.Screen, map);

            foreach (var item in snapshot.Items)
            {
                values[$"{item.Name}|kind"] = item.Type.Name;

                if (item.ContainedType != null)
                {
                    values[$"{item.Name}|ContainedType"] = item.ContainedType;
                }

                FlattenUi(values, item.Name, item.Ui, map);

                foreach (var part in item.Interface)
                {
                    var key = $"{item.Name}|Interface[{part.PropertyName}]";
                    values[key] = part.Value is string text ? map(text) : Show(part.Value);
                    FlattenDynamizations(values, key, part.Dynamizations, map);
                }
            }

            return values;
        }

        private static void FlattenUi(Dictionary<string, string?> values, string prefix, HmiUiSnapshot ui, Func<string, string> map)
        {
            foreach (var pair in ui.Values)
            {
                values[$"{prefix}|{pair.Key}"] = pair.Value is string text ? map(text) : Show(pair.Value);
            }

            foreach (var text in ui.Texts)
            {
                foreach (var culture in text.Value)
                {
                    values[$"{prefix}|{text.Key}[{culture.Key}]"] = map(culture.Value);
                }
            }

            foreach (var nested in ui.Nested)
            {
                FlattenUi(values, $"{prefix}|{nested.Key}", nested.Value, map);
            }

            FlattenDynamizations(values, prefix, ui.Dynamizations, map);

            foreach (var handler in ui.Events)
            {
                var key = $"{prefix}|Event[{handler.EventType}]";
                values[$"{key}|ScriptCode"] = handler.ScriptCode == null ? null : map(handler.ScriptCode);
                values[$"{key}|GlobalDefinitionAreaScriptCode"] = handler.GlobalDefinitionAreaScriptCode == null ? null : map(handler.GlobalDefinitionAreaScriptCode);
                values[$"{key}|Async"] = Show(handler.Async);
            }
        }

        private static void FlattenDynamizations(Dictionary<string, string?> values, string prefix, List<HmiDynamizationSnapshot> dynamizations, Func<string, string> map)
        {
            foreach (var dynamization in dynamizations)
            {
                var key = $"{prefix}|Dynamization[{dynamization.PropertyName}]";
                values[key] = dynamization.Type.Name;
                FlattenUi(values, key, dynamization.Ui, map);
            }
        }

        private static string? Show(object? value) => value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
