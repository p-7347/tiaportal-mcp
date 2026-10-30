using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Controls;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Copy of a WinCC Unified screen, with text replacement - "make screens 2..60 from screen 1".
        //
        // V20 Openness has no copy, export or master copy for Unified screens (checked against the
        // installed Siemens.Engineering.dll: HmiScreenComposition only has Create/Find). So the copy
        // is rebuilt item by item from what Openness exposes generically: every read-write
        // attribute (GetAttributeInfos), multilingual texts, nested part objects (fonts etc.),
        // dynamizations (tag, script, ...), event handler scripts and faceplate interfaces.
        //
        // Two phases, because any exception inside the tool's transaction makes TIA Portal refuse
        // the commit: ReadHmiScreenSnapshot reads everything outside the transaction (where a failing
        // read can safely be skipped and reported), and WriteHmiScreenCopy only writes what was read,
        // skipping values already equal on the new object. A write TIA Portal refuses aborts the
        // whole copy (nothing is kept) and the message names the item and attribute.

        private const int SnapshotDepth = 3;

        public HmiScreenSnapshot ReadHmiScreenSnapshot(string softwarePath, string screenName)
        {
            return Operation.Run(_logger, nameof(ReadHmiScreenSnapshot), PortalErrorCode.NotFound,
                () =>
                {
                    var screen = RequireHmiScreen(RequireHmiSoftware(softwarePath), screenName);
                    var snapshot = new HmiScreenSnapshot { Name = screen.Name };

                    ReadUi(screen, snapshot.Screen, "screen", snapshot.Skipped, SnapshotDepth, "Name", "ScreenNumber");
                    ReadEvents(screen, snapshot.Screen, "screen", snapshot.Skipped);

                    foreach (var item in screen.ScreenItems)
                    {
                        if (item == null)
                        {
                            // a kind V20 Openness has no type for (e.g. a PLC code view)
                            snapshot.Skipped.Add("an item of a kind Openness cannot read (e.g. PLC code view): not copied");
                            continue;
                        }

                        var itemSnapshot = new HmiItemSnapshot { Name = item.Name, Type = item.GetType() };

                        if (item is HmiContainerBase container)
                        {
                            itemSnapshot.ContainedType = container.ContainedType;
                        }

                        ReadUi(item, itemSnapshot.Ui, item.Name, snapshot.Skipped, SnapshotDepth, "Name", "ContainedType");
                        ReadEvents(item, itemSnapshot.Ui, item.Name, snapshot.Skipped);

                        if (item is HmiFaceplateContainer faceplate)
                        {
                            foreach (var property in faceplate.Interface)
                            {
                                var part = new HmiInterfaceSnapshot { PropertyName = property.PropertyName };

                                try
                                {
                                    part.Value = property.Value;
                                }
                                catch (Exception ex)
                                {
                                    snapshot.Skipped.Add($"{item.Name}.Interface.{property.PropertyName}: not readable ({ex.Message})");
                                }

                                ReadDynamizations(property, part.Dynamizations, $"{item.Name}.Interface.{property.PropertyName}", snapshot.Skipped);
                                itemSnapshot.Interface.Add(part);
                            }
                        }

                        snapshot.Items.Add(itemSnapshot);
                    }

                    return snapshot;
                },
                ("softwarePath", softwarePath), ("screenName", screenName));
        }

        public HmiScreenCopyResult WriteHmiScreenCopy(
            string softwarePath,
            HmiScreenSnapshot source,
            string newScreenName,
            IReadOnlyList<KeyValuePair<string, string>> replacements,
            ushort? screenNumber)
        {
            return Operation.Run(_logger, nameof(WriteHmiScreenCopy), PortalErrorCode.CreateFailed,
                () =>
                {
                    EnsureValidName(newScreenName);

                    var hmi = RequireHmiSoftware(softwarePath);

                    if (FindHmiScreen(hmi, newScreenName) != null)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"A screen named '{newScreenName}' already exists in '{softwarePath}'.");
                    }

                    var sourceScreen = RequireHmiScreen(hmi, source.Name);
                    var screens = ScreensHolding(hmi, sourceScreen);
                    var writer = new CopyWriter(replacements, source.TargetDefaults);

                    try
                    {
                        writer.Step = $"create screen '{newScreenName}'";
                        var screen = screens.Create(newScreenName);

                        writer.WriteUi(screen, source.Screen, "screen");

                        if (screenNumber.HasValue)
                        {
                            writer.Step = $"set screen number {screenNumber.Value}";
                            screen.ScreenNumber = screenNumber.Value;
                        }

                        foreach (var item in source.Items)
                        {
                            writer.Step = $"create item '{item.Name}' ({item.Type.Name})";
                            var created = CreateItemOfType(screen.ScreenItems, item);
                            writer.Items++;

                            writer.WriteUi(created, item.Ui, item.Name);

                            if (created is HmiFaceplateContainer faceplate)
                            {
                                foreach (var part in item.Interface)
                                {
                                    writer.Step = $"{item.Name}.Interface.{part.PropertyName}";
                                    var target = faceplate.Interface.Find(part.PropertyName);

                                    if (target == null)
                                    {
                                        writer.Warnings.Add($"{item.Name}: faceplate interface '{part.PropertyName}' not found on the copy");
                                        continue;
                                    }

                                    if (part.Value != null && !IsPlainValue(part.Value))
                                    {
                                        writer.Warnings.Add($"{item.Name}.Interface.{part.PropertyName}: value of type {part.Value.GetType().Name} not copied");
                                    }
                                    else if (part.Value != null)
                                    {
                                        var value = writer.Replace(part.Value);

                                        if (!Equals(target.Value, value))
                                        {
                                            target.Value = value;
                                            writer.Attributes++;
                                        }
                                    }

                                    writer.WriteDynamizations(target, part.Dynamizations, $"{item.Name}.Interface.{part.PropertyName}");
                                }
                            }
                        }

                        return new HmiScreenCopyResult
                        {
                            Screen = screen,
                            Items = writer.Items,
                            Attributes = writer.Attributes,
                            Texts = writer.Texts,
                            Dynamizations = writer.Dynamizations,
                            Events = writer.Events,
                            Replaced = writer.Replaced,
                            Warnings = writer.Warnings
                        };
                    }
                    catch (Exception ex) when (ex is not PortalException)
                    {
                        throw new PortalException(PortalErrorCode.CreateFailed,
                            $"TIA Portal refused the copy at '{writer.Step}': {Operation.DescribeChain(ex)}. Nothing was kept.", null, ex);
                    }
                },
                ("softwarePath", softwarePath), ("source", source.Name), ("newScreenName", newScreenName));
        }

        public bool HmiScreenExists(string softwarePath, string screenName)
        {
            return Operation.Run(_logger, nameof(HmiScreenExists), PortalErrorCode.NotFound,
                () => FindHmiScreen(RequireHmiSoftware(softwarePath), screenName) != null,
                ("softwarePath", softwarePath), ("screenName", screenName));
        }

        /// <summary>The screen composition (root or a screen group) that holds 'screen', so the copy lands next to it.</summary>
        private static HmiScreenComposition ScreensHolding(HmiSoftware hmi, HmiScreen screen)
        {
            return screen.Parent is global::Siemens.Engineering.HmiUnified.UI.ScreenGroup.HmiScreenGroup group
                ? group.Screens
                : hmi.Screens;
        }

        private static HmiScreenItemBase CreateItemOfType(HmiScreenItemBaseComposition items, HmiItemSnapshot item)
        {
            // Create<T> only exists as a generic method; T is the source item's own type.
            var create = typeof(HmiScreenItemBaseComposition).GetMethods()
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition &&
                            m.GetParameters().Length == (item.ContainedType != null ? 2 : 1))
                .MakeGenericMethod(item.Type);

            var arguments = item.ContainedType != null
                ? new object[] { item.Name, item.ContainedType }
                : new object[] { item.Name };

            try
            {
                return (HmiScreenItemBase)create.Invoke(items, arguments);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }

        #region read

        private static void ReadUi(IEngineeringObject source, HmiUiSnapshot target, string where, List<string> skipped, int depth, params string[] exclude)
        {
            ReadAttributes(source, target, where, skipped, depth, exclude);

            if (source is UIBase ui)
            {
                ReadDynamizations(ui, target.Dynamizations, where, skipped);
            }
        }

        private static void ReadAttributes(IEngineeringObject source, HmiUiSnapshot target, string where, List<string> skipped, int depth, params string[] exclude)
        {
            IList<EngineeringAttributeInfo> infos;

            try
            {
                infos = source.GetAttributeInfos();
            }
            catch (Exception ex)
            {
                skipped.Add($"{where}: attributes not readable ({ex.Message})");
                return;
            }

            foreach (var info in infos)
            {
                if (exclude.Contains(info.Name, StringComparer.Ordinal) || info.AccessMode == EngineeringAttributeAccessMode.None ||
                    info.AccessMode == EngineeringAttributeAccessMode.Write)
                {
                    continue;
                }

                object? value;

                try
                {
                    value = source.GetAttribute(info.Name);
                }
                catch (Exception ex)
                {
                    skipped.Add($"{where}.{info.Name}: not readable ({ex.Message})");
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                if (value is MultilingualText text)
                {
                    var byCulture = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var entry in text.Items)
                    {
                        byCulture[entry.Language.Culture.Name] = entry.Text ?? string.Empty;
                    }

                    target.Texts[info.Name] = byCulture;
                }
                else if (IsPlainValue(value))
                {
                    if (info.AccessMode == EngineeringAttributeAccessMode.ReadWrite)
                    {
                        target.Values.Add(new KeyValuePair<string, object>(info.Name, value));
                    }
                }
                else if (value is IEngineeringObject nested && value is not IEnumerable && depth > 0)
                {
                    var part = new HmiUiSnapshot();
                    ReadAttributes(nested, part, $"{where}.{info.Name}", skipped, depth - 1);

                    if (part.Values.Count > 0 || part.Texts.Count > 0 || part.Nested.Count > 0)
                    {
                        target.Nested[info.Name] = part;
                    }
                }
            }
        }

        private static bool IsPlainValue(object value) =>
            value is string || value.GetType().IsPrimitive || value.GetType().IsEnum || value is ValueType;

        private static void ReadDynamizations(UIBase source, List<HmiDynamizationSnapshot> target, string where, List<string> skipped)
        {
            try
            {
                foreach (var dynamization in source.Dynamizations)
                {
                    var snapshot = new HmiDynamizationSnapshot { PropertyName = dynamization.PropertyName, Type = dynamization.GetType() };
                    ReadAttributes(dynamization, snapshot.Ui, $"{where}.{dynamization.PropertyName}", skipped, SnapshotDepth,
                        "PropertyName", "DynamizationType");
                    target.Add(snapshot);
                }
            }
            catch (Exception ex)
            {
                skipped.Add($"{where}: dynamizations not readable ({ex.Message})");
            }
        }

        /// <summary>Event handlers exist per item type (HmiButtonEventHandlerComposition, ...); read them by reflection.</summary>
        private static void ReadEvents(object source, HmiUiSnapshot target, string where, List<string> skipped)
        {
            var handlers = source.GetType().GetProperty("EventHandlers")?.GetValue(source) as IEnumerable;

            if (handlers == null)
            {
                return;
            }

            try
            {
                foreach (var handler in handlers)
                {
                    var eventType = handler.GetType().GetProperty("EventType")?.GetValue(handler);

                    if (eventType == null || handler.GetType().GetProperty("Script")?.GetValue(handler) is not IHmiScript script)
                    {
                        continue;
                    }

                    target.Events.Add(new HmiEventSnapshot
                    {
                        EventType = eventType,
                        ScriptCode = script.ScriptCode,
                        GlobalDefinitionAreaScriptCode = script.GlobalDefinitionAreaScriptCode,
                        Async = script.Async
                    });
                }
            }
            catch (Exception ex)
            {
                skipped.Add($"{where}: event handlers not readable ({ex.Message})");
            }
        }

        #endregion

        /// <summary>Writes a snapshot onto new objects inside the transaction; counts what it did.</summary>
        private sealed class CopyWriter
        {
            private readonly IReadOnlyList<KeyValuePair<string, string>> _replacements;

            private readonly Dictionary<string, object?> _defaults;

            public CopyWriter(IReadOnlyList<KeyValuePair<string, string>> replacements, Dictionary<string, object?> defaults)
            {
                _replacements = replacements;
                _defaults = defaults;
            }

            public string Step { get; set; } = string.Empty;
            public int Items { get; set; }
            public int Attributes { get; set; }
            public int Texts { get; private set; }
            public int Dynamizations { get; private set; }
            public int Events { get; private set; }
            public int Replaced { get; private set; }
            public List<string> Warnings { get; } = new List<string>();

            public object Replace(object value) => value is string text ? Replace(text) : value;

            public string Replace(string text)
            {
                var result = text;

                foreach (var pair in _replacements)
                {
                    result = result.Replace(pair.Key, pair.Value);
                }

                if (!string.Equals(result, text, StringComparison.Ordinal))
                {
                    Replaced++;
                }

                return result;
            }

            public void WriteUi(IEngineeringObject target, HmiUiSnapshot source, string where)
            {
                WriteAttributes(target, source, where);

                if (target is UIBase ui)
                {
                    WriteDynamizations(ui, source.Dynamizations, where);
                }

                WriteEvents(target, source.Events, where);
            }

            private void WriteAttributes(IEngineeringObject target, HmiUiSnapshot source, string where)
            {
                foreach (var pair in source.Values)
                {
                    Step = $"{where}.{pair.Key}";
                    var value = Replace(pair.Value);

                    // Every copy of one snapshot creates the same objects and writes in the same order, so the value
                    // a fresh object has here is the same each time: read it on the first copy, reuse it after
                    if (!_defaults.TryGetValue(Step, out var current))
                    {
                        current = target.GetAttribute(pair.Key);
                        _defaults[Step] = current;
                    }

                    if (!Equals(current, value))
                    {
                        target.SetAttribute(pair.Key, value);
                        Attributes++;
                    }
                }

                foreach (var text in source.Texts)
                {
                    Step = $"{where}.{text.Key}";

                    if (target.GetAttribute(text.Key) is not MultilingualText targetText)
                    {
                        Warnings.Add($"{where}.{text.Key}: no text on the copy");
                        continue;
                    }

                    foreach (var entry in targetText.Items)
                    {
                        if (text.Value.TryGetValue(entry.Language.Culture.Name, out var value))
                        {
                            value = Replace(value);

                            if (!string.Equals(entry.Text, value, StringComparison.Ordinal))
                            {
                                entry.Text = value;
                                Texts++;
                            }
                        }
                    }
                }

                foreach (var nested in source.Nested)
                {
                    Step = $"{where}.{nested.Key}";

                    if (target.GetAttribute(nested.Key) is IEngineeringObject part)
                    {
                        WriteAttributes(part, nested.Value, $"{where}.{nested.Key}");
                    }
                    else
                    {
                        Warnings.Add($"{where}.{nested.Key}: no such part on the copy");
                    }
                }
            }

            public void WriteDynamizations(UIBase target, List<HmiDynamizationSnapshot> source, string where)
            {
                foreach (var dynamization in source)
                {
                    Step = $"{where}.{dynamization.PropertyName} ({dynamization.Type.Name})";

                    var existing = target.Dynamizations.Find(dynamization.PropertyName);
                    DynamizationBase created;

                    if (existing != null && existing.GetType() == dynamization.Type)
                    {
                        created = existing;
                    }
                    else if (existing != null)
                    {
                        Warnings.Add($"{where}.{dynamization.PropertyName}: the copy already has a {existing.DynamizationType} dynamization; left as is");
                        continue;
                    }
                    else
                    {
                        var create = typeof(DynamizationBaseComposition).GetMethods()
                            .First(m => m.Name == "Create" && m.IsGenericMethodDefinition)
                            .MakeGenericMethod(dynamization.Type);

                        try
                        {
                            created = (DynamizationBase)create.Invoke(target.Dynamizations, new object[] { dynamization.PropertyName });
                        }
                        catch (TargetInvocationException tie) when (tie.InnerException != null)
                        {
                            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                            throw;
                        }
                    }

                    WriteAttributes(created, dynamization.Ui, $"{where}.{dynamization.PropertyName}");
                    Dynamizations++;
                }
            }

            private void WriteEvents(object target, List<HmiEventSnapshot> source, string where)
            {
                if (source.Count == 0)
                {
                    return;
                }

                var handlers = target.GetType().GetProperty("EventHandlers")?.GetValue(target);

                if (handlers == null)
                {
                    Warnings.Add($"{where}: the copy has no event handlers; {source.Count} event script(s) not copied");
                    return;
                }

                var find = handlers.GetType().GetMethod("Find");
                var create = handlers.GetType().GetMethod("Create");

                foreach (var handler in source)
                {
                    Step = $"{where} event {handler.EventType}";

                    var targetHandler = find?.Invoke(handlers, new[] { handler.EventType }) ?? create?.Invoke(handlers, new[] { handler.EventType });

                    if (targetHandler?.GetType().GetProperty("Script")?.GetValue(targetHandler) is not IHmiScript script)
                    {
                        Warnings.Add($"{where}: event {handler.EventType} could not be created on the copy");
                        continue;
                    }

                    if (handler.ScriptCode != null) script.ScriptCode = Replace(handler.ScriptCode);
                    if (handler.GlobalDefinitionAreaScriptCode != null) script.GlobalDefinitionAreaScriptCode = Replace(handler.GlobalDefinitionAreaScriptCode);
                    if (script.Async != handler.Async) script.Async = handler.Async;

                    Events++;
                }
            }
        }
    }

    public class HmiScreenSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public HmiUiSnapshot Screen { get; } = new HmiUiSnapshot();
        public List<HmiItemSnapshot> Items { get; } = new List<HmiItemSnapshot>();

        /// <summary>What could not be read and is therefore not copied.</summary>
        public List<string> Skipped { get; } = new List<string>();

        /// <summary>Attribute values a freshly created copy has before they are written (see CopyWriter), filled by the first copy.</summary>
        public Dictionary<string, object?> TargetDefaults { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    public class HmiUiSnapshot
    {
        /// <summary>Read-write plain values (numbers, enums, strings, colors) in Openness order.</summary>
        public List<KeyValuePair<string, object>> Values { get; } = new List<KeyValuePair<string, object>>();

        /// <summary>Multilingual texts: attribute name to culture name to text.</summary>
        public Dictionary<string, Dictionary<string, string>> Texts { get; } = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Part objects (font, padding, ...) whose own attributes are copied.</summary>
        public Dictionary<string, HmiUiSnapshot> Nested { get; } = new Dictionary<string, HmiUiSnapshot>();

        public List<HmiDynamizationSnapshot> Dynamizations { get; } = new List<HmiDynamizationSnapshot>();
        public List<HmiEventSnapshot> Events { get; } = new List<HmiEventSnapshot>();
    }

    public class HmiItemSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public Type Type { get; set; } = typeof(object);
        public string? ContainedType { get; set; }
        public HmiUiSnapshot Ui { get; } = new HmiUiSnapshot();
        public List<HmiInterfaceSnapshot> Interface { get; } = new List<HmiInterfaceSnapshot>();
    }

    public class HmiInterfaceSnapshot
    {
        public string PropertyName { get; set; } = string.Empty;
        public object? Value { get; set; }
        public List<HmiDynamizationSnapshot> Dynamizations { get; } = new List<HmiDynamizationSnapshot>();
    }

    public class HmiDynamizationSnapshot
    {
        public string PropertyName { get; set; } = string.Empty;
        public Type Type { get; set; } = typeof(object);
        public HmiUiSnapshot Ui { get; } = new HmiUiSnapshot();
    }

    public class HmiEventSnapshot
    {
        public object EventType { get; set; } = string.Empty;
        public string? ScriptCode { get; set; }
        public string? GlobalDefinitionAreaScriptCode { get; set; }
        public bool Async { get; set; }
    }

    public class HmiScreenCopyResult
    {
        public HmiScreen? Screen { get; set; }
        public int Items { get; set; }
        public int Attributes { get; set; }
        public int Texts { get; set; }
        public int Dynamizations { get; set; }
        public int Events { get; set; }
        public int Replaced { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
