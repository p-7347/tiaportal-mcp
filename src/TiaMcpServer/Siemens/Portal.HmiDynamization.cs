using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // WinCC Unified screen item dynamizations in full (BACKLOG C6): which tag, value range or bit
        // drives a property (the tag dynamization's mapping table), the script and its trigger, and
        // flashing. GetHmiScreenItems already lists property, type and tag; this reads the rest.
        //
        // Callers: GetHmiDynamizations in McpServer.HmiDynamization.cs. Affected API: additive.
        // Reads no data files.
        //
        // Openness constraints (V20, read from the installed Siemens.Engineering.dll):
        // - Every dynamization is a DynamizationBase on UIBase.Dynamizations, keyed by property name;
        //   the concrete type (TagDynamization, ScriptDynamization, FlashingDynamization,
        //   ResourceListDynamization, ExpressionDynamization, TagParameterDynamization) carries the rest.
        // - A tag or expression dynamization maps the value through a ValueConverter: either a
        //   Formula, or a MappingTable whose ConditionType says how its entries are read (None:
        //   direct, Range, Singlebit, Bitmask). Entry values are typed by the property
        //   (System.Drawing.Color for colors, bool, numbers) but exposed as object.
        // - Bit entries carry 'Relevant' (the bit mask) and 'Condition' (the bit values that must
        //   match), e.g. bit 3 set: Relevant 8, Condition 8.

        #region read

        public sealed class HmiDynamizationsResult
        {
            public List<HmiItemDynamizations> Items { get; } = new List<HmiItemDynamizations>();
            public int ItemsLookedAt { get; set; }
            public string? StoppedReason { get; set; }
        }

        public HmiDynamizationsResult GetHmiDynamizations(string softwarePath, string screenName, string regexName, string propertyName, TimeSpan budget)
        {
            return Operation.Run(_logger, nameof(GetHmiDynamizations), PortalErrorCode.NotFound,
                () =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    var result = new HmiDynamizationsResult();

                    foreach (var item in GetHmiScreenItems(softwarePath, screenName, regexName))
                    {
                        if (stopwatch.Elapsed > budget)
                        {
                            result.StoppedReason = $"time budget of {budget.TotalSeconds:0} s used up after {result.ItemsLookedAt} item(s); narrow 'regexName' to read the rest";
                            break;
                        }

                        result.ItemsLookedAt++;

                        var dynamizations = item.Dynamizations
                            .Where(d => d != null && (string.IsNullOrEmpty(propertyName) || string.Equals(d.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase)))
                            .Select(DescribeDynamization)
                            .ToList();

                        if (dynamizations.Count > 0)
                        {
                            result.Items.Add(new HmiItemDynamizations
                            {
                                ItemName = item.Name,
                                Kind = KindOf(item),
                                Dynamizations = dynamizations
                            });
                        }
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("screenName", screenName));
        }

        /// <summary>
        /// Full description of one dynamization. Each part is read on its own, so a property TIA
        /// Portal refuses to give out leaves an 'error' note instead of failing the whole screen.
        /// </summary>
        public static HmiDynamizationDetail DescribeDynamization(DynamizationBase dynamization)
        {
            var detail = new HmiDynamizationDetail
            {
                PropertyName = dynamization.PropertyName,
                Type = dynamization.DynamizationType.ToString()
            };

            try
            {
                switch (dynamization)
                {
                    case TagDynamization tag:
                        detail.Tag = tag.Tag;
                        detail.PlcTag = NullIfEmpty(tag.PlcTag);
                        detail.DataType = NullIfEmpty(tag.DataType);
                        detail.ReadOnly = tag.ReadOnly;
                        detail.UseIndirectAddressing = tag.UseIndirectAddressing ? true : null;
                        DescribeConverter(tag.ValueConverter, detail);
                        break;

                    case ExpressionDynamization expression:
                        DescribeConverter(expression.ValueConverter, detail);
                        break;

                    case ScriptDynamization script:
                        detail.ScriptCode = script.ScriptCode;
                        detail.GlobalDefinitionAreaScriptCode = NullIfEmpty(script.GlobalDefinitionAreaScriptCode?.Trim());
                        detail.Async = script.Async ? true : null;
                        DescribeTrigger(script.Trigger, detail);
                        break;

                    case FlashingDynamization flashing:
                        detail.FlashingCondition = flashing.FlashingCondition.ToString();
                        detail.FlashingRate = flashing.FlashingRate.ToString();
                        detail.Color = ColorText(flashing.Color);
                        detail.AlternateColor = ColorText(flashing.AlternateColor);
                        break;

                    case ResourceListDynamization resourceList:
                        detail.Tag = NullIfEmpty(resourceList.Tag);
                        detail.ResourceList = resourceList.ResourceList;
                        break;

                    case TagParameterDynamization tagParameter:
                        detail.Tag = tagParameter.DynamicTagName;
                        break;
                }
            }
            catch (Exception ex)
            {
                detail.Error = Operation.DescribeChain(ex);
            }

            return detail;
        }

        private static void DescribeConverter(ValueConverter? converter, HmiDynamizationDetail detail)
        {
            if (converter == null)
            {
                return;
            }

            if (converter.IsFormulaSelected)
            {
                detail.Formula = converter.Formula;
                return;
            }

            var table = converter.MappingTable;
            if (table == null || table.Entries.Count == 0)
            {
                return;
            }

            detail.ConditionType = table.ConditionType.ToString();
            detail.Mapping = table.Entries.Select(DescribeMappingEntry).ToList();
        }

        private static HmiMappingEntryDescription DescribeMappingEntry(MappingTableEntryBase entry)
        {
            var description = new HmiMappingEntryDescription
            {
                Value = ValueText(entry.Value),
                Flashing = entry.Flashing ? true : null
            };

            if (entry.Flashing)
            {
                description.AlternateValue = ValueText(entry.AlternateValue);
                description.FlashingRate = entry.FlashingRate.ToString();
            }

            switch (entry)
            {
                case MappingTableEntrySimple simple:
                    description.Condition = simple.Condition;
                    break;

                case MappingTableEntryRange range:
                    description.RangeType = range.RangeType.ToString();
                    description.From = range.RangeType == RangeType.To ? null : ValueText(range.From);
                    description.To = range.RangeType == RangeType.From ? null : ValueText(range.To);
                    break;

                case MappingTableEntryBitmask bits:
                    description.BitType = bits.BitDynamizationType.ToString();
                    description.Relevant = bits.Relevant;
                    description.BitCondition = bits.Condition;
                    description.Bits = BitNumbers(bits.Relevant);
                    break;
            }

            return description;
        }

        private static void DescribeTrigger(Trigger? trigger, HmiDynamizationDetail detail)
        {
            if (trigger == null)
            {
                return;
            }

            detail.TriggerType = trigger.Type.ToString();

            if (trigger.Type == TriggerType.CustomCycle)
            {
                detail.TriggerCycle = NullIfEmpty(trigger.CustomDuration);
            }

            // Trigger.Tags is typed object; it holds the tag names as a list.
            detail.TriggerTags = trigger.Tags switch
            {
                null => null,
                string single => string.IsNullOrEmpty(single) ? null : new List<string> { single },
                System.Collections.IEnumerable many => NullIfEmpty(many.Cast<object>().Select(t => t?.ToString() ?? string.Empty).Where(t => t.Length > 0).ToList()),
                var other => new List<string> { other.ToString() }
            };
        }

        /// <summary>Bit numbers set in a mask, e.g. 20 -> [2, 4].</summary>
        private static List<int> BitNumbers(ulong mask)
        {
            var bits = new List<int>();

            for (var bit = 0; bit < 64; bit++)
            {
                if ((mask & (1UL << bit)) != 0)
                {
                    bits.Add(bit);
                }
            }

            return bits;
        }

        /// <summary>Colors as '#RRGGBB' ('#AARRGGBB' when not opaque); other values as invariant text.</summary>
        private static string? ValueText(object? value) => value switch
        {
            null => null,
            System.Drawing.Color color => ColorText(color),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        private static string ColorText(System.Drawing.Color color) =>
            color.A == 255
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

        private static List<T>? NullIfEmpty<T>(List<T> list) => list.Count == 0 ? null : list;

        #endregion

        #region write

        // Every write follows the same order: find the item, parse and convert every argument, back
        // up the current dynamization of that property, and only then change anything. A failed
        // Openness call inside the tool's transaction makes TIA Portal refuse the whole commit, so
        // nothing is tried and retried here.

        public sealed class HmiDynamizationWriteResult
        {
            public string ItemName { get; set; } = string.Empty;
            public HmiDynamizationDetail? Dynamization { get; set; }
            public HmiDynamizationDetail? Previous { get; set; }
            public BackupResult Backup { get; set; } = new BackupResult();
        }

        /// <summary>
        /// Binds 'tagName' to the property and, when given, replaces the value mapping: 'mapping' is a
        /// JSON array of range entries ('[]' clears the table), or 'formula' a value converter formula.
        /// Neither given keeps the current mapping.
        /// </summary>
        public HmiDynamizationWriteResult SetHmiTagDynamization(
            string softwarePath, string screenName, string itemName, string propertyName, string tagName,
            string? mapping = null, string? formula = null, string? mappingMode = null)
        {
            return Operation.Run(_logger, nameof(SetHmiTagDynamization), PortalErrorCode.InvalidParams,
                () =>
                {
                    RequireText(propertyName, "propertyName");
                    RequireText(tagName, "tagName");

                    if (!string.IsNullOrEmpty(mapping) && !string.IsNullOrEmpty(formula))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "Pass 'mapping' or 'formula', not both.");
                    }

                    var mode = string.IsNullOrWhiteSpace(mappingMode) ? "replace" : mappingMode!.Trim().ToLowerInvariant();

                    if (mode != "replace" && mode != "add" && mode != "remove")
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"Unknown mappingMode '{mappingMode}': use 'replace' (default), 'add' or 'remove'.");
                    }

                    if (mode != "replace" && string.IsNullOrEmpty(mapping))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"mappingMode '{mode}' needs 'mapping' with the entries to {mode}.");
                    }

                    var item = RequireHmiScreenItem(softwarePath, screenName, itemName);
                    var existing = RequireReplaceable(item, propertyName, DynamizationType.Tag);

                    var entries = string.IsNullOrEmpty(mapping)
                        ? null
                        : ParseRangeMapping(mapping!, PropertyValueType(item, propertyName, existing), requireValue: mode != "remove");

                    if (mode == "remove")
                    {
                        // checked before any write: an exception inside the transaction makes TIA Portal refuse the commit
                        var current = (existing as TagDynamization)?.ValueConverter?.MappingTable?.Entries.OfType<MappingTableEntryRange>().ToList()
                                      ?? new List<MappingTableEntryRange>();
                        var missing = entries!.Where(e => !current.Any(c => SameBound(c.From, e.From) && SameBound(c.To, e.To))).ToList();

                        if (missing.Count > 0)
                        {
                            throw new PortalException(PortalErrorCode.NotFound,
                                $"Nothing was changed: no mapping entry with {string.Join(", ", missing.Select(m => $"from {m.From} to {m.To}"))} on '{itemName}.{propertyName}'. 'GetHmiDynamizations' lists the current entries.");
                        }
                    }

                    var result = Prepare(item, propertyName, existing, nameof(SetHmiTagDynamization), screenName);

                    var dynamization = existing as TagDynamization ?? item.Dynamizations.Create<TagDynamization>(propertyName);
                    dynamization.Tag = tagName;

                    if (entries != null)
                    {
                        // IsFormulaSelected is only written when it changes: a Unified Comfort panel
                        // refuses the setter even for 'false' ("Property is not supported on current
                        // device", live 2026-10-19), and that would abort the whole transaction.
                        var converter = dynamization.ValueConverter;
                        if (converter.IsFormulaSelected)
                        {
                            converter.IsFormulaSelected = false;
                        }

                        var table = converter.MappingTable;

                        // 'replace' clears the table; 'add' and 'remove' drop only the entries with the
                        // same from/to as a given one, so 'add' also changes an entry in place
                        foreach (var old in table.Entries.ToList())
                        {
                            if (mode == "replace" || (old is MappingTableEntryRange range && entries.Any(e => SameBound(range.From, e.From) && SameBound(range.To, e.To))))
                            {
                                old.Delete();
                            }
                        }

                        if (entries.Count > 0 && mode != "remove")
                        {
                            table.ConditionType = ConditionType.Range;

                            foreach (var spec in entries)
                            {
                                var entry = table.Entries.Create<MappingTableEntryRange>();
                                entry.From = spec.From!;
                                entry.To = spec.To!;
                                entry.Value = spec.Value;

                                if (spec.Flashing)
                                {
                                    entry.Flashing = true;
                                    if (spec.AlternateValue != null) entry.AlternateValue = spec.AlternateValue;
                                    if (spec.FlashingRate.HasValue) entry.FlashingRate = spec.FlashingRate.Value;
                                }
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(formula))
                    {
                        try
                        {
                            dynamization.ValueConverter.IsFormulaSelected = true;
                            dynamization.ValueConverter.Formula = formula;
                        }
                        catch (Exception ex) when (ex is not PortalException)
                        {
                            throw new PortalException(PortalErrorCode.NotSupported,
                                $"TIA Portal refused a formula for '{itemName}.{propertyName}': {Operation.DescribeChain(ex).TrimEnd('.')}. " +
                                "Some panels support no value converter formula; use 'mapping' or 'SetHmiScriptDynamization' instead.", null, ex);
                        }
                    }

                    return Finish(result, item, propertyName);
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName), ("propertyName", propertyName));
        }

        /// <summary>
        /// Sets the script of a script dynamization, creating it when the property has none.
        /// Arguments left null keep their current value.
        /// </summary>
        public HmiDynamizationWriteResult SetHmiScriptDynamization(
            string softwarePath, string screenName, string itemName, string propertyName, string scriptCode,
            string? triggerType = null, string? triggerTags = null, string? triggerCycle = null, string? globalDefinitionCode = null)
        {
            return Operation.Run(_logger, nameof(SetHmiScriptDynamization), PortalErrorCode.InvalidParams,
                () =>
                {
                    RequireText(propertyName, "propertyName");
                    RequireText(scriptCode, "scriptCode");

                    TriggerType? type = string.IsNullOrEmpty(triggerType) ? null : ParseEnum<TriggerType>(triggerType!, "triggerType");
                    var tags = string.IsNullOrEmpty(triggerTags)
                        ? null
                        : triggerTags!.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

                    if (tags != null && type.HasValue && type.Value != TriggerType.Tags)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"'triggerTags' go with triggerType 'Tags', not '{type.Value}'.");
                    }

                    var item = RequireHmiScreenItem(softwarePath, screenName, itemName);
                    var existing = RequireReplaceable(item, propertyName, DynamizationType.Script);

                    // Without 'triggerType' the current trigger stays, so a new cycle alone is fine on a
                    // script that already runs on a custom cycle.
                    var effectiveType = type ?? (existing as ScriptDynamization)?.Trigger?.Type;
                    if (!string.IsNullOrEmpty(triggerCycle) && effectiveType != TriggerType.CustomCycle)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            "'triggerCycle' goes with triggerType 'CustomCycle'.");
                    }

                    var result = Prepare(item, propertyName, existing, nameof(SetHmiScriptDynamization), screenName);

                    var dynamization = existing as ScriptDynamization ?? item.Dynamizations.Create<ScriptDynamization>(propertyName);
                    dynamization.ScriptCode = scriptCode;

                    if (globalDefinitionCode != null)
                    {
                        dynamization.GlobalDefinitionAreaScriptCode = globalDefinitionCode;
                    }

                    if (tags != null)
                    {
                        type ??= TriggerType.Tags;
                    }

                    if (type.HasValue)
                    {
                        dynamization.Trigger.Type = type.Value;
                    }

                    if (tags != null)
                    {
                        dynamization.Trigger.Tags = tags;
                    }

                    if (!string.IsNullOrEmpty(triggerCycle))
                    {
                        dynamization.Trigger.CustomDuration = triggerCycle;
                    }

                    return Finish(result, item, propertyName);
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName), ("propertyName", propertyName));
        }

        /// <summary>Sets flashing on a color property, creating the flashing dynamization when there is none.</summary>
        public HmiDynamizationWriteResult SetHmiFlashingDynamization(
            string softwarePath, string screenName, string itemName, string propertyName,
            string? flashingCondition = null, string? flashingRate = null, string? color = null, string? alternateColor = null)
        {
            return Operation.Run(_logger, nameof(SetHmiFlashingDynamization), PortalErrorCode.InvalidParams,
                () =>
                {
                    RequireText(propertyName, "propertyName");

                    FlashingCondition? condition = string.IsNullOrEmpty(flashingCondition) ? null : ParseEnum<FlashingCondition>(flashingCondition!, "flashingCondition");
                    FlashingRate? rate = string.IsNullOrEmpty(flashingRate) ? null : ParseEnum<FlashingRate>(flashingRate!, "flashingRate");
                    System.Drawing.Color? first = string.IsNullOrEmpty(color) ? null : ParseColor(color!, "color");
                    System.Drawing.Color? second = string.IsNullOrEmpty(alternateColor) ? null : ParseColor(alternateColor!, "alternateColor");

                    var item = RequireHmiScreenItem(softwarePath, screenName, itemName);
                    var existing = RequireReplaceable(item, propertyName, DynamizationType.Flashing);

                    var result = Prepare(item, propertyName, existing, nameof(SetHmiFlashingDynamization), screenName);

                    var dynamization = existing as FlashingDynamization ?? item.Dynamizations.Create<FlashingDynamization>(propertyName);

                    if (condition.HasValue) dynamization.FlashingCondition = condition.Value;
                    if (rate.HasValue) dynamization.FlashingRate = rate.Value;
                    if (first.HasValue) dynamization.Color = first.Value;
                    if (second.HasValue) dynamization.AlternateColor = second.Value;

                    return Finish(result, item, propertyName);
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName), ("propertyName", propertyName));
        }

        /// <summary>Removes the dynamization of one property; the property keeps its static value.</summary>
        public HmiDynamizationWriteResult DeleteHmiDynamization(string softwarePath, string screenName, string itemName, string propertyName)
        {
            return Operation.Run(_logger, nameof(DeleteHmiDynamization), PortalErrorCode.DeleteFailed,
                () =>
                {
                    RequireText(propertyName, "propertyName");

                    var item = RequireHmiScreenItem(softwarePath, screenName, itemName);
                    var existing = item.Dynamizations.Find(propertyName)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"'{itemName}.{propertyName}' on screen '{screenName}' has no dynamization. 'GetHmiDynamizations' lists them.");

                    var result = Prepare(item, propertyName, existing, nameof(DeleteHmiDynamization), screenName);

                    existing.Delete();

                    return result;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName), ("propertyName", propertyName));
        }

        /// <summary>
        /// The current dynamization of the property, when it may be changed in place: none, or one of
        /// the given type. Another type is refused - the property must be cleared with
        /// DeleteHmiDynamization first, so a tool never silently swaps e.g. a script for a tag.
        /// Not a generic method constrained to DynamizationBase: a Siemens type in a constraint makes
        /// loading the Portal class need Siemens.Engineering, and Program.Main touches Portal before
        /// the Openness assembly resolver is registered.
        /// </summary>
        private static DynamizationBase? RequireReplaceable(HmiScreenItemBase item, string propertyName, DynamizationType type)
        {
            var existing = item.Dynamizations.Find(propertyName);

            if (existing != null && existing.DynamizationType != type)
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    $"'{item.Name}.{propertyName}' already has a {existing.DynamizationType} dynamization. " +
                    "Remove it with 'DeleteHmiDynamization' first if it should be replaced.");
            }

            return existing;
        }

        /// <summary>Describes and backs up the current dynamization, before anything changes.</summary>
        private static HmiDynamizationWriteResult Prepare(HmiScreenItemBase item, string propertyName, DynamizationBase? existing, string toolName, string screenName)
        {
            var result = new HmiDynamizationWriteResult { ItemName = item.Name };

            if (existing == null)
            {
                return result;
            }

            result.Previous = DescribeDynamization(existing);

            var objectPath = $"{screenName}/{item.Name}/{propertyName}";
            var json = System.Text.Json.JsonSerializer.Serialize(
                new { Screen = screenName, Item = item.Name, Dynamization = result.Previous },
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

            result.Backup = WriteBackup(toolName, objectPath, true, file => System.IO.File.WriteAllText(file.FullName, json), "json");

            return result;
        }

        private static HmiDynamizationWriteResult Finish(HmiDynamizationWriteResult result, HmiScreenItemBase item, string propertyName)
        {
            var current = item.Dynamizations.Find(propertyName);
            result.Dynamization = current == null ? null : DescribeDynamization(current);
            return result;
        }

        private sealed class RangeEntrySpec
        {
            public object? From { get; set; }
            public object? To { get; set; }
            public object Value { get; set; } = string.Empty;
            public bool Flashing { get; set; }
            public object? AlternateValue { get; set; }
            public FlashingRate? FlashingRate { get; set; }
        }

        /// <summary>
        /// Parses '[{"from":0,"to":0,"value":"#00FF00"},{"from":1,"to":1,"value":"#FF0000"}]'.
        /// 'from'/'to' are tag values, both required; 'value' is converted to the property's type;
        /// 'flashing', 'alternateValue' and 'flashingRate' are optional.
        /// </summary>
        private static bool SameBound(object? a, object? b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            try
            {
                return Convert.ToDouble(a, System.Globalization.CultureInfo.InvariantCulture) == Convert.ToDouble(b, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                return string.Equals(Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture), Convert.ToString(b, System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            }
        }

        private static List<RangeEntrySpec> ParseRangeMapping(string mapping, Type valueType, bool requireValue = true)
        {
            System.Text.Json.JsonDocument document;

            try
            {
                document = System.Text.Json.JsonDocument.Parse(mapping);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"'mapping' is not valid JSON: {ex.Message}. Example: [{{\"from\":0,\"to\":0,\"value\":\"#00FF00\"}},{{\"from\":1,\"to\":1,\"value\":\"#FF0000\"}}]", null, ex);
            }

            using (document)
            {
                if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, "'mapping' must be a JSON array of entries, or '[]' to clear the table.");
                }

                var list = new List<RangeEntrySpec>();
                var index = 0;

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    index++;

                    if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"'mapping' entry {index} is not an object.");
                    }

                    var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "from", "to", "value", "flashing", "alternateValue", "flashingRate" };
                    var unknown = element.EnumerateObject().Select(p => p.Name).FirstOrDefault(n => !known.Contains(n));
                    if (unknown != null)
                    {
                        throw new PortalException(PortalErrorCode.NotSupported,
                            $"'mapping' entry {index}: '{unknown}' is not supported. Entries take from, to, value, flashing, alternateValue, flashingRate (range conditions only).");
                    }

                    var spec = new RangeEntrySpec
                    {
                        From = RangeBound(element, "from", index),
                        To = RangeBound(element, "to", index)
                    };

                    // Both bounds: MappingTableEntryRange.RangeType is read-only and stays 'Range', so an
                    // entry with one bound would silently get 0 as the other (live 2026-10-19).
                    if (spec.From == null || spec.To == null)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"'mapping' entry {index} needs both 'from' and 'to'; for a single value give it twice, e.g. \"from\":1,\"to\":1. Open ranges cannot be written.");
                    }

                    var value = JsonText(element, "value");

                    if (value != null)
                    {
                        spec.Value = ConvertPropertyValue(value, valueType, $"mapping entry {index} 'value'");
                    }
                    else if (requireValue)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"'mapping' entry {index} needs a 'value'.");
                    }

                    if (Property(element, "flashing") is { } flashing)
                    {
                        spec.Flashing = flashing.ValueKind == System.Text.Json.JsonValueKind.True
                            || (flashing.ValueKind == System.Text.Json.JsonValueKind.String && bool.TryParse(flashing.GetString(), out var on) && on);
                    }

                    if (JsonText(element, "alternateValue") is { } alternate)
                    {
                        spec.AlternateValue = ConvertPropertyValue(alternate, valueType, $"mapping entry {index} 'alternateValue'");
                    }

                    if (JsonText(element, "flashingRate") is { } rate)
                    {
                        spec.FlashingRate = ParseEnum<FlashingRate>(rate, $"mapping entry {index} 'flashingRate'");
                    }

                    list.Add(spec);
                }

                return list;
            }
        }

        private static System.Text.Json.JsonElement? Property(System.Text.Json.JsonElement element, string name)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value;
                }
            }

            return null;
        }

        private static string? JsonText(System.Text.Json.JsonElement element, string name)
        {
            var value = Property(element, name);

            return value?.ValueKind switch
            {
                null or System.Text.Json.JsonValueKind.Null => null,
                System.Text.Json.JsonValueKind.String => value.Value.GetString(),
                _ => value.Value.GetRawText()
            };
        }

        /// <summary>Range bounds are stored as Int32 for whole numbers (also for Bool tags), otherwise as Double.</summary>
        private static object? RangeBound(System.Text.Json.JsonElement element, string name, int index)
        {
            var text = JsonText(element, name);
            if (text == null)
            {
                return null;
            }

            if (bool.TryParse(text, out var flag))
            {
                return flag ? 1 : 0;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
            {
                return whole;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
            {
                return real;
            }

            throw new PortalException(PortalErrorCode.InvalidParams, $"'mapping' entry {index}: '{name}' value '{text}' is not a number.");
        }

        /// <summary>
        /// The .NET type the property's values have (System.Drawing.Color for colors, bool for
        /// Visible, ...), taken from the item's current value or, failing that, from an existing entry.
        /// </summary>
        private static Type PropertyValueType(HmiScreenItemBase item, string propertyName, DynamizationBase? existing)
        {
            object? current;

            try
            {
                current = item.GetAttribute(propertyName);
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"A {KindOf(item)} has no property '{propertyName}' (as named in TIA Portal's property list, e.g. 'BackColor', 'Visible'): {Operation.DescribeChain(ex)}", null, ex);
            }

            var type = current?.GetType()
                ?? (existing as TagDynamization)?.ValueConverter?.MappingTable?.Entries.Select(e => e.Value?.GetType()).FirstOrDefault(t => t != null);

            if (type == null)
            {
                throw new PortalException(PortalErrorCode.NotSupported,
                    $"Cannot tell the value type of '{item.Name}.{propertyName}' (it has no current value), so 'mapping' values cannot be converted.");
            }

            if (type != typeof(System.Drawing.Color) && type != typeof(string) && type != typeof(bool) && !type.IsEnum && !type.IsPrimitive)
            {
                throw new PortalException(PortalErrorCode.NotSupported,
                    $"'{item.Name}.{propertyName}' holds {type.Name} values, which a mapping cannot set. Texts change with a resource list in TIA Portal.");
            }

            return type;
        }

        private static object ConvertPropertyValue(string text, Type type, string what)
        {
            if (type == typeof(System.Drawing.Color))
            {
                return ParseColor(text, what);
            }

            try
            {
                if (type == typeof(string)) return text;
                if (type == typeof(bool)) return text.Trim() == "1" || bool.Parse(text.Trim());
                if (type.IsEnum) return Enum.Parse(type, text.Trim(), true);

                return Convert.ChangeType(text.Trim(), type, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException || ex is ArgumentException)
            {
                var hint = type.IsEnum ? $" One of: {string.Join(", ", Enum.GetNames(type))}." : string.Empty;
                throw new PortalException(PortalErrorCode.InvalidParams, $"{what}: '{text}' is not a valid {type.Name}.{hint}", null, ex);
            }
        }

        /// <summary>'#RRGGBB', '#AARRGGBB' or a color name such as 'Red'.</summary>
        private static System.Drawing.Color ParseColor(string text, string what)
        {
            var value = text.Trim();

            if (value.StartsWith("#", StringComparison.Ordinal)
                && (value.Length == 7 || value.Length == 9)
                && uint.TryParse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                return System.Drawing.Color.FromArgb(value.Length == 7 ? unchecked((int)(0xFF000000 | argb)) : unchecked((int)argb));
            }

            var named = System.Drawing.Color.FromName(value);
            if (named.IsKnownColor)
            {
                return System.Drawing.Color.FromArgb(named.ToArgb());
            }

            throw new PortalException(PortalErrorCode.InvalidParams, $"{what}: '{text}' is not a color. Use '#RRGGBB', '#AARRGGBB' or a name such as 'Red'.");
        }

        private static T ParseEnum<T>(string text, string what) where T : struct
        {
            if (Enum.TryParse<T>(text.Trim(), true, out var value) && Enum.IsDefined(typeof(T), value))
            {
                return value;
            }

            throw new PortalException(PortalErrorCode.InvalidParams,
                $"{what}: '{text}' is not valid. One of: {string.Join(", ", Enum.GetNames(typeof(T)))}.");
        }

        private static void RequireText(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"'{name}' is required.");
            }
        }

        #endregion
    }

    public class HmiItemDynamizations
    {
        public string ItemName { get; set; } = string.Empty;

        /// <summary>Button, IOField, Label, Text, Rectangle, or the Openness type name for other items.</summary>
        public string Kind { get; set; } = string.Empty;

        public List<HmiDynamizationDetail> Dynamizations { get; set; } = new List<HmiDynamizationDetail>();
    }

    public class HmiDynamizationDetail
    {
        public string PropertyName { get; set; } = string.Empty;

        /// <summary>Tag, Script, Flashing, ResourceList, Expression or TagParameter.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>The HMI tag: bound tag (Tag), index tag (ResourceList), dynamic tag name (TagParameter).</summary>
        public string? Tag { get; set; }

        public string? PlcTag { get; set; }
        public string? DataType { get; set; }
        public bool? ReadOnly { get; set; }
        public bool? UseIndirectAddressing { get; set; }

        /// <summary>Value converter formula, when a formula is selected instead of a mapping table.</summary>
        public string? Formula { get; set; }

        /// <summary>How the mapping entries are read: None (direct value), Range, Singlebit, Bitmask, Expression.</summary>
        public string? ConditionType { get; set; }

        /// <summary>Mapping table: which tag value gives which property value.</summary>
        public List<HmiMappingEntryDescription>? Mapping { get; set; }

        public string? ScriptCode { get; set; }
        public string? GlobalDefinitionAreaScriptCode { get; set; }
        public bool? Async { get; set; }

        /// <summary>Script trigger: Disabled, T100ms ... T10s, CustomCycle, Tags, AutomaticTags.</summary>
        public string? TriggerType { get; set; }

        public string? TriggerCycle { get; set; }
        public List<string>? TriggerTags { get; set; }

        /// <summary>Flashing: Never, Always or RangeViolation.</summary>
        public string? FlashingCondition { get; set; }

        /// <summary>Slow (0.5 Hz), Medium (1 Hz) or Fast (2 Hz).</summary>
        public string? FlashingRate { get; set; }

        public string? Color { get; set; }
        public string? AlternateColor { get; set; }

        public string? ResourceList { get; set; }

        /// <summary>Set when TIA Portal refused to give out part of this dynamization.</summary>
        public string? Error { get; set; }
    }

    public class HmiMappingEntryDescription
    {
        /// <summary>Simple entries: the tag value this entry matches.</summary>
        public string? Condition { get; set; }

        /// <summary>Range entries: Range, From (value and above) or To (value and below).</summary>
        public string? RangeType { get; set; }

        public string? From { get; set; }
        public string? To { get; set; }

        /// <summary>Bit entries: SingleBit or MultiBit.</summary>
        public string? BitType { get; set; }

        /// <summary>Bit entries: mask of the bits looked at, e.g. 8 for bit 3.</summary>
        public ulong? Relevant { get; set; }

        /// <summary>Bit entries: the value the relevant bits must have, e.g. 8 for bit 3 set, 0 for bit 3 clear.</summary>
        public ulong? BitCondition { get; set; }

        /// <summary>Bit entries: the bit numbers in 'Relevant'.</summary>
        public List<int>? Bits { get; set; }

        /// <summary>The property value for this entry, colors as '#RRGGBB'.</summary>
        public string? Value { get; set; }

        public bool? Flashing { get; set; }
        public string? AlternateValue { get; set; }
        public string? FlashingRate { get; set; }
    }
}
