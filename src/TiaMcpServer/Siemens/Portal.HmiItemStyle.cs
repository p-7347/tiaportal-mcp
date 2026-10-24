using Siemens.Engineering;
using Siemens.Engineering.HmiUnified.UI.Base;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Static properties of WinCC Unified screen items (colors, borders, fonts, formats, ...):
        // listing them, setting them by name on one or many items, and copying one item's style to
        // others.
        //
        // Callers: GetHmiScreenItemProperties, SetHmiScreenItemProperties and CopyHmiItemStyle in
        // McpServer.HmiItemStyle.cs. Affected API: additive. Writes one JSON backup per write call
        // with the previous values.
        //
        // Openness exposes every property generically (GetAttributeInfos/GetAttribute/SetAttribute),
        // so properties are named as TIA Portal's property list names them ('BackColor',
        // 'OutputFormat'); a property of a part object is reached with a dot ('Font.Size',
        // 'Border.Color'). Values are given as text and converted to the property's own type, read
        // from its current value. As in the other HMI write tools, everything is resolved and
        // converted before the first write, because a failed write inside the tool's transaction
        // makes TIA Portal refuse the whole commit.

        private const int ItemPropertyDepth = 2;

        /// <summary>Properties a style copy leaves alone unless they are named: identity, place and content.</summary>
        private static readonly HashSet<string> StyleExcluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name", "Left", "Top", "Width", "Height", "TabIndex", "ContainedType", "ProcessValue",
            "Text", "ToolTipText", "Visible", "Enabled", "Authorization", "ScreenName", "Screen"
        };

        public sealed class HmiItemPropertyInfo
        {
            /// <summary>'BackColor', or 'Font.Size' for a property of a part object.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Colors as '#RRGGBB' ('#AARRGGBB' when not opaque), texts as plain text in the first language.</summary>
            public string? Value { get; set; }

            /// <summary>Color, Boolean, Int32, Single, String, Text, or the enum's name.</summary>
            public string Type { get; set; } = string.Empty;

            /// <summary>Allowed values of an enum property.</summary>
            public List<string>? Values { get; set; }
        }

        public sealed class HmiItemPropertyChange
        {
            public string Item { get; set; } = string.Empty;
            public string Property { get; set; } = string.Empty;
            public string? Before { get; set; }
            public string? After { get; set; }
        }

        public sealed class HmiItemPropertyResult
        {
            public List<HmiItemPropertyChange> Changes { get; set; } = new List<HmiItemPropertyChange>();

            /// <summary>Items the call touched (also those already having every value).</summary>
            public List<string> Items { get; set; } = new List<string>();

            /// <summary>Properties not copied to an item, with the reason (style copy only).</summary>
            public List<string> Skipped { get; set; } = new List<string>();

            public string? Backup { get; set; }
            public string? BackupWarning { get; set; }
        }

        /// <summary>One planned write: resolved and converted, not yet applied.</summary>
        private sealed class ItemPropertyWrite
        {
            public string Item { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public IEngineeringObject Owner { get; set; } = null!;
            public string Attribute { get; set; } = string.Empty;
            public object? Before { get; set; }
            public object? Value { get; set; }

            /// <summary>Plain text for a multilingual text property (written to every language).</summary>
            public string? Text { get; set; }
        }

        #region read

        public List<HmiItemPropertyInfo> GetHmiScreenItemProperties(string softwarePath, string screenName, string itemName, string regexName = "")
        {
            return Operation.Run(_logger, nameof(GetHmiScreenItemProperties), PortalErrorCode.NotFound,
                () =>
                {
                    var item = RequireHmiScreenItem(softwarePath, screenName, itemName);
                    var regex = ItemPropertyRegex(regexName);
                    var list = new List<HmiItemPropertyInfo>();

                    foreach (var (path, value) in ReadItemProperties(item, string.Empty, ItemPropertyDepth))
                    {
                        if (regex != null && !regex.IsMatch(path))
                        {
                            continue;
                        }

                        list.Add(new HmiItemPropertyInfo
                        {
                            Name = path,
                            Value = PropertyText(value),
                            Type = PropertyTypeName(value),
                            Values = value != null && value.GetType().IsEnum ? Enum.GetNames(value.GetType()).ToList() : null
                        });
                    }

                    return list;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName));
        }

        /// <summary>Every writable plain value, text and part-object property, as (path, value).</summary>
        private static IEnumerable<(string Path, object? Value)> ReadItemProperties(IEngineeringObject source, string prefix, int depth)
        {
            IList<EngineeringAttributeInfo> infos;

            try
            {
                infos = source.GetAttributeInfos();
            }
            catch (Exception)
            {
                yield break;
            }

            foreach (var info in infos.OrderBy(i => i.Name, StringComparer.Ordinal))
            {
                if (info.AccessMode == EngineeringAttributeAccessMode.None || info.AccessMode == EngineeringAttributeAccessMode.Write)
                {
                    continue;
                }

                object? value;

                try
                {
                    value = source.GetAttribute(info.Name);
                }
                catch (Exception)
                {
                    continue;
                }

                var path = prefix + info.Name;

                if (value is MultilingualText)
                {
                    yield return (path, value);
                }
                else if (value != null && IsPlainValue(value))
                {
                    if (info.AccessMode == EngineeringAttributeAccessMode.ReadWrite)
                    {
                        yield return (path, value);
                    }
                }
                else if (value is IEngineeringObject part && value is not IEnumerable && depth > 0)
                {
                    foreach (var nested in ReadItemProperties(part, path + ".", depth - 1))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static string? PropertyText(object? value) => value switch
        {
            MultilingualText text => PlainText(text.Items.Select(i => i.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t))),
            _ => ValueText(value)
        };

        private static string PropertyTypeName(object? value) => value switch
        {
            null => "Unknown",
            System.Drawing.Color => "Color",
            MultilingualText => "Text",
            _ => value.GetType().Name
        };

        private static Regex? ItemPropertyRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return null;
            }

            try
            {
                return new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"'{pattern}' is not a valid regular expression: {ex.Message}", null, ex);
            }
        }

        #endregion

        #region write

        /// <param name="itemNames">Item names; with 'itemRegex' empty these are the items changed.</param>
        /// <param name="itemRegex">Regular expression over the item names, added to 'itemNames'.</param>
        /// <param name="properties">Property path to value text, e.g. 'BackColor' = '#FFFFFF', 'Font.Size' = '20'.</param>
        public HmiItemPropertyResult SetHmiScreenItemProperties(string softwarePath, string screenName, IReadOnlyList<string> itemNames,
            string itemRegex, IReadOnlyList<KeyValuePair<string, string>> properties, string toolName)
        {
            return Operation.Run(_logger, nameof(SetHmiScreenItemProperties), PortalErrorCode.InvalidParams,
                () =>
                {
                    if (properties.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            "'properties' is empty. Give a JSON object such as {\"BackColor\":\"#FFFFFF\",\"Font.Size\":\"20\"}.");
                    }

                    var screen = RequireHmiScreen(RequireHmiSoftware(softwarePath), screenName);
                    var items = SelectHmiItems(screen, screenName, itemNames, itemRegex);
                    var problems = new List<string>();
                    var writes = new List<ItemPropertyWrite>();

                    foreach (var item in items)
                    {
                        foreach (var pair in properties)
                        {
                            var write = ResolveItemProperty(item, pair.Key, out var problem);

                            if (write == null)
                            {
                                problems.Add(problem!);
                                continue;
                            }

                            try
                            {
                                if (write.Before is MultilingualText)
                                {
                                    write.Text = pair.Value;
                                }
                                else
                                {
                                    write.Value = ConvertPropertyValue(NormalizeColorText(pair.Value, write.Before), write.Before!.GetType(), $"{item.Name}.{pair.Key}");
                                }
                            }
                            catch (PortalException pex)
                            {
                                problems.Add(pex.Message);
                                continue;
                            }

                            writes.Add(write);
                        }
                    }

                    ThrowIfProblems(problems, "Nothing was changed");

                    return ApplyItemPropertyWrites(writes, items.Select(i => i.Name).ToList(), new List<string>(), softwarePath, screenName, toolName);
                },
                ("softwarePath", softwarePath), ("screenName", screenName));
        }

        /// <param name="properties">Property paths to copy; empty copies every style property (not position, size, name, texts or bindings).</param>
        /// <param name="targetScreenName">Screen of the targets; empty means the source item's screen.</param>
        public HmiItemPropertyResult CopyHmiItemStyle(string softwarePath, string screenName, string sourceItem, IReadOnlyList<string> targetNames,
            string targetRegex, IReadOnlyList<string> properties, string targetScreenName, string toolName)
        {
            return Operation.Run(_logger, nameof(CopyHmiItemStyle), PortalErrorCode.InvalidParams,
                () =>
                {
                    var hmi = RequireHmiSoftware(softwarePath);
                    var source = RequireHmiScreen(hmi, screenName).ScreenItems.Find(sourceItem)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Screen '{screenName}' has no item named '{sourceItem}'. Use 'GetHmiScreenItems' to list them.");

                    var targetScreen = string.IsNullOrWhiteSpace(targetScreenName) ? screenName : targetScreenName.Trim();
                    var targets = SelectHmiItems(RequireHmiScreen(hmi, targetScreen), targetScreen, targetNames, targetRegex)
                        .Where(t => !(targetScreen == screenName && t.Name == source.Name))
                        .ToList();

                    if (targets.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No target item other than the source itself.");
                    }

                    // The source values: every style property, or exactly the named ones.
                    var sourceValues = ReadItemProperties(source, string.Empty, ItemPropertyDepth)
                        .Where(p => p.Value != null && p.Value is not MultilingualText)
                        .ToDictionary(p => p.Path, p => p.Value!, StringComparer.OrdinalIgnoreCase);

                    List<string> paths;

                    if (properties.Count > 0)
                    {
                        var unknown = properties.Where(p => !sourceValues.ContainsKey(p)).ToList();

                        if (unknown.Count > 0)
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams,
                                $"'{sourceItem}' has no writable property {string.Join(", ", unknown.Select(u => $"'{u}'"))}. " +
                                "'GetHmiScreenItemProperties' lists the names.");
                        }

                        paths = properties.Select(p => sourceValues.Keys.First(k => string.Equals(k, p, StringComparison.OrdinalIgnoreCase))).ToList();
                    }
                    else
                    {
                        paths = sourceValues.Keys.Where(k => !StyleExcluded.Contains(k.Split('.')[0])).ToList();
                    }

                    var writes = new List<ItemPropertyWrite>();
                    var skipped = new List<string>();

                    foreach (var target in targets)
                    {
                        var missing = new List<string>();

                        foreach (var path in paths)
                        {
                            var write = ResolveItemProperty(target, path, out _);

                            if (write == null || write.Before == null || write.Before.GetType() != sourceValues[path].GetType())
                            {
                                // A different item kind simply lacks some properties.
                                missing.Add(path);
                                continue;
                            }

                            write.Value = sourceValues[path];
                            writes.Add(write);
                        }

                        if (missing.Count > 0)
                        {
                            skipped.Add($"{target.Name}: {string.Join(", ", missing)}");
                        }
                    }

                    return ApplyItemPropertyWrites(writes, targets.Select(t => t.Name).ToList(), skipped, softwarePath, targetScreen, toolName);
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("sourceItem", sourceItem));
        }

        private List<HmiScreenItemBase> SelectHmiItems(global::Siemens.Engineering.HmiUnified.UI.Screens.HmiScreen screen, string screenName,
            IReadOnlyList<string> names, string regexPattern)
        {
            var regex = ItemPropertyRegex(regexPattern);
            var result = new List<HmiScreenItemBase>();
            var missing = new List<string>();

            foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.Ordinal))
            {
                var item = screen.ScreenItems.Find(name);

                if (item == null)
                {
                    missing.Add(name);
                }
                else
                {
                    result.Add(item);
                }
            }

            if (missing.Count > 0)
            {
                throw new PortalException(PortalErrorCode.NotFound,
                    $"Screen '{screenName}' has no item {string.Join(", ", missing.Select(m => $"'{m}'"))}. Use 'GetHmiScreenItems' to list them.");
            }

            if (regex != null)
            {
                foreach (var item in screen.ScreenItems)
                {
                    if (item != null && regex.IsMatch(item.Name) && !result.Contains(item))
                    {
                        result.Add(item);
                    }
                }
            }

            if (result.Count == 0)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    regex == null
                        ? "No item given. Name the items, or give a regular expression over their names."
                        : $"No item on screen '{screenName}' matches '{regexPattern}'.");
            }

            return result;
        }

        /// <summary>Walks 'Font.Size' down to its owner; null with the reason when a step does not exist or is not writable.</summary>
        private static ItemPropertyWrite? ResolveItemProperty(HmiScreenItemBase item, string path, out string? problem)
        {
            problem = null;
            var parts = path.Split('.').Select(p => p.Trim()).ToArray();

            if (parts.Any(string.IsNullOrEmpty))
            {
                problem = $"'{path}' is not a property name.";
                return null;
            }

            IEngineeringObject owner = item;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                object? next;

                try
                {
                    next = owner.GetAttribute(parts[i]);
                }
                catch (Exception)
                {
                    next = null;
                }

                if (next is not IEngineeringObject part || next is IEnumerable)
                {
                    problem = $"{item.Name}: '{string.Join(".", parts.Take(i + 1))}' is not a part object of a {KindOf(item)}. 'GetHmiScreenItemProperties' lists the names.";
                    return null;
                }

                owner = part;
            }

            var attribute = parts[parts.Length - 1];
            EngineeringAttributeInfo? info;

            try
            {
                info = owner.GetAttributeInfos().FirstOrDefault(a => string.Equals(a.Name, attribute, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                info = null;
            }

            if (info == null)
            {
                problem = $"{item.Name}: a {KindOf(item)} has no property '{path}'. 'GetHmiScreenItemProperties' lists the names.";
                return null;
            }

            object? before;

            try
            {
                before = owner.GetAttribute(info.Name);
            }
            catch (Exception ex)
            {
                problem = $"{item.Name}.{path}: not readable ({Operation.DescribeChain(ex)}).";
                return null;
            }

            if (before is MultilingualText)
            {
                // Texts are changed through their items, so the attribute itself may be read-only.
            }
            else if (info.AccessMode != EngineeringAttributeAccessMode.ReadWrite)
            {
                problem = $"{item.Name}.{path} is read-only.";
                return null;
            }
            else if (before == null || !IsPlainValue(before))
            {
                problem = $"{item.Name}.{path} holds {(before == null ? "no value" : before.GetType().Name)}, which this tool cannot set.";
                return null;
            }

            return new ItemPropertyWrite { Item = item.Name, Path = string.Join(".", parts), Owner = owner, Attribute = info.Name, Before = before };
        }

        /// <summary>Accepts 'R, G, B' and 'A, R, G, B' for colors as well (the classic WinCC notation).</summary>
        private static string NormalizeColorText(string text, object? current)
        {
            if (current is not System.Drawing.Color)
            {
                return text;
            }

            var parts = text.Split(',').Select(p => p.Trim()).ToArray();

            if ((parts.Length == 3 || parts.Length == 4) && parts.All(p => byte.TryParse(p, out _)))
            {
                var bytes = parts.Select(byte.Parse).ToArray();
                return parts.Length == 3
                    ? $"#{bytes[0]:X2}{bytes[1]:X2}{bytes[2]:X2}"
                    : $"#{bytes[0]:X2}{bytes[1]:X2}{bytes[2]:X2}{bytes[3]:X2}";
            }

            return text;
        }

        private HmiItemPropertyResult ApplyItemPropertyWrites(List<ItemPropertyWrite> writes, List<string> items, List<string> skipped,
            string softwarePath, string screenName, string toolName)
        {
            var result = new HmiItemPropertyResult { Items = items, Skipped = skipped };

            var changing = writes.Where(w => w.Text != null
                    ? !string.Equals(PropertyText(w.Before), PlainText(w.Text) ?? string.Empty, StringComparison.Ordinal)
                    : !Equals(w.Before, w.Value))
                .ToList();

            if (changing.Count == 0)
            {
                return result;
            }

            var before = changing.Select(w => new { item = w.Item, property = w.Path, value = PropertyText(w.Before) }).ToList();
            var json = JsonSerializer.Serialize(new { softwarePath, screenName, properties = before }, new JsonSerializerOptions { WriteIndented = true });
            var backup = WriteBackup(toolName, $"HMI screen {screenName}", true, file => System.IO.File.WriteAllText(file.FullName, json), "json");
            result.Backup = backup.Path;
            result.BackupWarning = backup.Warning;

            foreach (var write in changing)
            {
                if (write.Text != null)
                {
                    SetHmiText((MultilingualText)write.Before!, write.Text);
                }
                else
                {
                    write.Owner.SetAttribute(write.Attribute, write.Value);
                }

                object? after;

                try
                {
                    after = write.Owner.GetAttribute(write.Attribute);
                }
                catch (Exception)
                {
                    after = write.Value;
                }

                result.Changes.Add(new HmiItemPropertyChange
                {
                    Item = write.Item,
                    Property = write.Path,
                    Before = write.Text != null ? before[changing.IndexOf(write)].value : PropertyText(write.Before),
                    After = PropertyText(after)
                });
            }

            return result;
        }

        #endregion
    }
}
