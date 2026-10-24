using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Event scripts (Tapped, Down, Up, Loaded, ...) on WinCC Unified screen items and screens:
        // creating or replacing them on one or many items, and deleting them.
        //
        // Callers: SetHmiItemEventScript and DeleteHmiItemEventScript in McpServer.HmiEvents.cs,
        // CreateHmiScreenItem's optional 'eventType'/'scriptCode'. Affected API: additive. Writes one
        // JSON backup per call with the previous scripts.
        //
        // Event handler compositions are typed per item kind (HmiButtonEventHandlerComposition with
        // HmiButtonEventType, ...) with no common base, so they are reached by reflection, as in
        // DescribeHmiEvents: the enum is the parameter type of the composition's Find/Create. As in
        // the other HMI write tools, every item and event is resolved before the first write.

        /// <summary>Common names for Unified events: Click is Tapped, Press is Down, Release is Up.</summary>
        private static readonly IReadOnlyDictionary<string, string> HmiEventAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Click"] = "Tapped",
            ["Clicked"] = "Tapped",
            ["Press"] = "Down",
            ["Pressed"] = "Down",
            ["Release"] = "Up",
            ["Released"] = "Up"
        };

        public sealed class HmiEventScriptChange
        {
            /// <summary>Item name, or the screen name for a screen event.</summary>
            public string Item { get; set; } = string.Empty;
            public string EventType { get; set; } = string.Empty;

            /// <summary>Created, Updated, Unchanged or Deleted.</summary>
            public string Action { get; set; } = string.Empty;
            public string? ScriptCode { get; set; }
        }

        public sealed class HmiEventScriptResult
        {
            public List<HmiEventScriptChange> Changes { get; set; } = new List<HmiEventScriptChange>();
            public string? Backup { get; set; }
            public string? BackupWarning { get; set; }
        }

        private sealed class EventTarget
        {
            public string Name { get; set; } = string.Empty;
            public object Handlers { get; set; } = null!;
            public object EventType { get; set; } = null!;
            public object? Existing { get; set; }
            public string? ScriptCode { get; set; }
        }

        /// <param name="itemNames">Item names; empty with 'itemRegex' empty and 'onScreen' true targets the screen itself.</param>
        /// <param name="itemValues">Per item text for '{value}' in the script; when given, '{item}' and '{value}' are replaced.</param>
        public HmiEventScriptResult SetHmiItemEventScript(string softwarePath, string screenName, IReadOnlyList<string> itemNames, string itemRegex,
            bool onScreen, string eventType, string scriptCode, string? globalDefinitionAreaScriptCode, bool? async,
            IReadOnlyDictionary<string, string>? itemValues, string toolName)
        {
            return Operation.Run(_logger, nameof(SetHmiItemEventScript), PortalErrorCode.InvalidParams,
                () =>
                {
                    if (string.IsNullOrWhiteSpace(scriptCode))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            "'scriptCode' is empty. To remove an event script use 'DeleteHmiItemEventScript'.");
                    }

                    var problems = new List<string>();
                    var targets = ResolveEventTargets(softwarePath, screenName, itemNames, itemRegex, onScreen, eventType, problems);

                    foreach (var target in targets)
                    {
                        target.ScriptCode = scriptCode;

                        if (itemValues != null && itemValues.Count > 0)
                        {
                            var code = scriptCode.Replace("{item}", target.Name);

                            if (code.Contains("{value}"))
                            {
                                if (itemValues.TryGetValue(target.Name, out var value))
                                {
                                    code = code.Replace("{value}", value);
                                }
                                else
                                {
                                    problems.Add($"'itemValues' has no value for '{target.Name}', but the script uses '{{value}}'.");
                                }
                            }

                            target.ScriptCode = code;
                        }
                    }

                    if (itemValues != null)
                    {
                        foreach (var name in itemValues.Keys.Where(k => targets.All(t => !string.Equals(t.Name, k, StringComparison.Ordinal))))
                        {
                            problems.Add($"'itemValues' names '{name}', which is not one of the selected items.");
                        }
                    }

                    ThrowIfProblems(problems, "Nothing was changed");

                    var result = new HmiEventScriptResult();
                    WriteEventBackup(targets, softwarePath, screenName, toolName, result);

                    foreach (var target in targets)
                    {
                        var before = target.Existing != null ? ScriptOf(target.Existing) : null;
                        var handler = target.Existing ?? Invoke(target.Handlers, "Create", target.EventType);
                        var script = ScriptOf(handler)
                            ?? throw new PortalException(PortalErrorCode.CreateFailed, $"{target.Name}: event {target.EventType} has no script object.");

                        var unchanged = before != null &&
                            string.Equals(before.ScriptCode, target.ScriptCode, StringComparison.Ordinal) &&
                            (globalDefinitionAreaScriptCode == null || string.Equals(before.GlobalDefinitionAreaScriptCode, globalDefinitionAreaScriptCode, StringComparison.Ordinal)) &&
                            (!async.HasValue || before.Async == async.Value);

                        if (!unchanged)
                        {
                            script.ScriptCode = target.ScriptCode;
                            if (globalDefinitionAreaScriptCode != null) script.GlobalDefinitionAreaScriptCode = globalDefinitionAreaScriptCode;
                            if (async.HasValue && script.Async != async.Value) script.Async = async.Value;
                        }

                        result.Changes.Add(new HmiEventScriptChange
                        {
                            Item = target.Name,
                            EventType = target.EventType.ToString(),
                            Action = unchanged ? "Unchanged" : target.Existing != null ? "Updated" : "Created",
                            ScriptCode = script.ScriptCode
                        });
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("eventType", eventType));
        }

        public HmiEventScriptResult DeleteHmiItemEventScript(string softwarePath, string screenName, IReadOnlyList<string> itemNames, string itemRegex,
            bool onScreen, string eventType, string toolName)
        {
            return Operation.Run(_logger, nameof(DeleteHmiItemEventScript), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var problems = new List<string>();
                    var targets = ResolveEventTargets(softwarePath, screenName, itemNames, itemRegex, onScreen, eventType, problems);
                    ThrowIfProblems(problems, "Nothing was deleted");

                    var existing = targets.Where(t => t.Existing != null).ToList();
                    var result = new HmiEventScriptResult();
                    WriteEventBackup(existing, softwarePath, screenName, toolName, result);

                    foreach (var target in existing)
                    {
                        var code = ScriptOf(target.Existing!)?.ScriptCode;
                        Invoke(target.Existing!, "Delete");

                        result.Changes.Add(new HmiEventScriptChange
                        {
                            Item = target.Name,
                            EventType = target.EventType.ToString(),
                            Action = "Deleted",
                            ScriptCode = code
                        });
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("eventType", eventType));
        }

        /// <summary>Creates or replaces one event script on an item just created (CreateHmiScreenItem).</summary>
        private static void SetEventScript(object owner, string ownerName, string eventType, string scriptCode)
        {
            var problems = new List<string>();
            var target = ResolveEventTarget(owner, ownerName, eventType, problems);

            if (target == null)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, string.Join(" ", problems));
            }

            var handler = target.Existing ?? Invoke(target.Handlers, "Create", target.EventType);
            var script = ScriptOf(handler)
                ?? throw new PortalException(PortalErrorCode.CreateFailed, $"{ownerName}: event {target.EventType} has no script object.");
            script.ScriptCode = scriptCode;
        }

        private List<EventTarget> ResolveEventTargets(string softwarePath, string screenName, IReadOnlyList<string> itemNames, string itemRegex,
            bool onScreen, string eventType, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    "'eventType' is required, e.g. 'Tapped' (click), 'Down' (press), 'Up' (release), or 'Loaded' for a screen.");
            }

            var screen = RequireHmiScreen(RequireHmiSoftware(softwarePath), screenName);
            var owners = new List<(string Name, object Owner)>();

            if (onScreen)
            {
                owners.Add((screenName, screen));
            }

            if (!onScreen || itemNames.Any(n => !string.IsNullOrWhiteSpace(n)) || !string.IsNullOrWhiteSpace(itemRegex))
            {
                owners.AddRange(SelectHmiItems(screen, screenName, itemNames, itemRegex).Select(i => (i.Name, (object)i)));
            }

            var targets = new List<EventTarget>();

            foreach (var (name, owner) in owners)
            {
                var target = ResolveEventTarget(owner, name, eventType, problems);
                if (target != null) targets.Add(target);
            }

            return targets;
        }

        private static EventTarget? ResolveEventTarget(object owner, string ownerName, string eventType, List<string> problems)
        {
            var handlers = owner.GetType().GetProperty("EventHandlers")?.GetValue(owner);
            var create = handlers?.GetType().GetMethod("Create");
            var enumType = create?.GetParameters().FirstOrDefault()?.ParameterType;

            if (handlers == null || enumType == null || !enumType.IsEnum)
            {
                problems.Add($"'{ownerName}' ({EventOwnerKind(owner)}) has no event scripts.");
                return null;
            }

            var name = HmiEventAliases.TryGetValue(eventType.Trim(), out var alias) ? alias : eventType.Trim();
            var valid = Enum.GetNames(enumType).Where(n => n != "None").ToList();
            var match = valid.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                problems.Add($"'{ownerName}' ({EventOwnerKind(owner)}) has no event '{eventType}'. Its events: {string.Join(", ", valid)}.");
                return null;
            }

            var value = Enum.Parse(enumType, match);

            return new EventTarget
            {
                Name = ownerName,
                Handlers = handlers,
                EventType = value,
                Existing = handlers.GetType().GetMethod("Find")?.Invoke(handlers, new[] { value })
            };
        }

        private static string EventOwnerKind(object owner) => owner is HmiScreenItemBase item ? KindOf(item) : "screen";

        private static IHmiScript? ScriptOf(object handler) => handler.GetType().GetProperty("Script")?.GetValue(handler) as IHmiScript;

        private static object Invoke(object target, string method, params object[] args)
        {
            try
            {
                return target.GetType().GetMethod(method)!.Invoke(target, args)!;
            }
            catch (System.Reflection.TargetInvocationException tex) when (tex.InnerException != null)
            {
                throw tex.InnerException;
            }
        }

        private void WriteEventBackup(List<EventTarget> targets, string softwarePath, string screenName, string toolName, HmiEventScriptResult result)
        {
            var before = targets.Where(t => t.Existing != null).Select(t =>
            {
                var script = ScriptOf(t.Existing!);
                return new
                {
                    item = t.Name,
                    eventType = t.EventType.ToString(),
                    scriptCode = script?.ScriptCode,
                    globalDefinitionAreaScriptCode = script?.GlobalDefinitionAreaScriptCode,
                    async = script?.Async
                };
            }).ToList();

            if (before.Count == 0)
            {
                return;
            }

            var json = JsonSerializer.Serialize(new { softwarePath, screenName, events = before }, new JsonSerializerOptions { WriteIndented = true });
            var backup = WriteBackup(toolName, $"HMI screen {screenName}", true, file => System.IO.File.WriteAllText(file.FullName, json), "json");
            result.Backup = backup.Path;
            result.BackupWarning = backup.Warning;
        }
    }
}
