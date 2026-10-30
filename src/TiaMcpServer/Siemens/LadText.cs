using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Renders a block's SimaticML export as compact text, one line per coil or box, for reading
    /// LAD/FBD code without its XML. A 8-network LAD FB is ~74 000 characters of XML and ~3 000 of
    /// this. SCL networks go through the caller's StructuredText renderer; STL and anything the
    /// renderer does not know are named, not dropped.
    ///
    /// Notation, per network:
    ///   operand := expr;          Coil          S operand IF expr;   R operand IF expr;
    ///   [expr] Move(in := x) => out1 -> y;      a box; [expr] is its EN, omitted when TRUE
    ///   #inst.Q / Name#uid.pin    a box output wired on into the logic (instance member, or box id)
    ///   [= x]                     EN is the same condition as the coil on x above
    /// expr uses AND / OR / NOT, P(x) / N(x) for edges (of an operand, or of the logic before a P_TRIG box), and (a >= b) for compares.
    /// Pure text work on the XML: no Siemens types, so it is testable without TIA Portal.
    /// </summary>
    public static class LadText
    {
        private static readonly HashSet<string> CompareNames = new HashSet<string>(StringComparer.Ordinal)
            { "Eq", "Ne", "Gt", "Ge", "Lt", "Le" };

        private static readonly Dictionary<string, string> CompareOps = new Dictionary<string, string>(StringComparer.Ordinal)
            { ["Eq"] = "=", ["Ne"] = "<>", ["Gt"] = ">", ["Ge"] = ">=", ["Lt"] = "<", ["Le"] = "<=" };

        private static readonly Regex PlainIdentifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        public static string RenderBlock(XDocument doc, Func<XElement, string> renderStructuredText)
        {
            var sb = new StringBuilder();
            var block = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.StartsWith("SW.Blocks.", StringComparison.Ordinal));
            var blockAttributes = Child(block, "AttributeList");

            if (block != null)
            {
                sb.Append("// ").Append(block.Name.LocalName.Substring("SW.Blocks.".Length))
                  .Append(' ').Append(Child(blockAttributes, "Name")?.Value)
                  .Append(", ").Append(Child(blockAttributes, "ProgrammingLanguage")?.Value)
                  .AppendLine(" - compact text rendering, not importable; use GetBlockInterface for the interface");
            }

            var units = doc.Descendants().Where(e => e.Name.LocalName == "SW.Blocks.CompileUnit").ToList();

            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                var attributes = Child(unit, "AttributeList");
                var language = Child(attributes, "ProgrammingLanguage")?.Value ?? "?";
                var title = MultilingualText(unit, "Title");
                var comment = MultilingualText(unit, "Comment");

                sb.AppendLine();
                sb.Append("NW").Append(i + 1).Append(" [").Append(language).Append(']');

                if (!string.IsNullOrWhiteSpace(title))
                {
                    sb.Append(' ').Append(OneLine(title!));
                }

                sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(comment))
                {
                    sb.Append("  // ").AppendLine(OneLine(comment!));
                }

                var source = Child(attributes, "NetworkSource");
                var flgNet = Child(source, "FlgNet");
                var structuredText = Child(source, "StructuredText");

                if (flgNet != null)
                {
                    foreach (var line in RenderFlgNet(flgNet))
                    {
                        sb.Append("  ").AppendLine(line);
                    }
                }
                else if (structuredText != null)
                {
                    foreach (var line in renderStructuredText(structuredText).Replace("\r", string.Empty).Split('\n'))
                    {
                        if (line.Trim().Length > 0)
                        {
                            sb.Append("  ").AppendLine(line.TrimEnd());
                        }
                    }
                }
                else if (source == null || !source.HasElements)
                {
                    sb.AppendLine("  (empty)");
                }
                else
                {
                    sb.Append("  (").Append(language).AppendLine(" network not rendered - read it with format 'xml')");
                }
            }

            return sb.ToString();
        }

        /// <summary>One LAD/FBD network as statements: coils first in wire order, then boxes.</summary>
        public static List<string> RenderFlgNet(XElement flgNet)
        {
            return new Network(flgNet).Render();
        }

        private sealed class Network
        {
            private readonly Dictionary<string, XElement> _parts = new Dictionary<string, XElement>(StringComparer.Ordinal);
            private readonly Dictionary<string, XElement> _accesses = new Dictionary<string, XElement>(StringComparer.Ordinal);

            // (part uid, input pin) -> the wire's source endpoint
            private readonly Dictionary<(string, string), XElement> _inputs = new Dictionary<(string, string), XElement>();

            // (part uid, output pin) -> targets fed by it
            private readonly Dictionary<(string, string), List<XElement>> _outputs = new Dictionary<(string, string), List<XElement>>();

            private readonly Dictionary<(string, string), string> _memo = new Dictionary<(string, string), string>();
            private readonly HashSet<string> _visiting = new HashSet<string>(StringComparer.Ordinal);

            // condition -> operand of the first coil driven by it, so a box on the same rung says [= operand]
            private readonly Dictionary<string, string> _coilConditions = new Dictionary<string, string>(StringComparer.Ordinal);

            public Network(XElement flgNet)
            {
                foreach (var element in Child(flgNet, "Parts")?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    var uid = (string?)element.Attribute("UId");

                    if (uid == null)
                    {
                        continue;
                    }

                    if (element.Name.LocalName == "Access")
                    {
                        _accesses[uid] = element;
                    }
                    else
                    {
                        _parts[uid] = element;
                    }
                }

                foreach (var wire in Child(flgNet, "Wires")?.Elements().Where(e => e.Name.LocalName == "Wire") ?? Enumerable.Empty<XElement>())
                {
                    var ends = wire.Elements().ToList();

                    if (ends.Count < 2)
                    {
                        continue;
                    }

                    var from = ends[0];

                    foreach (var to in ends.Skip(1))
                    {
                        if (to.Name.LocalName == "NameCon")
                        {
                            _inputs[(Uid(to), Pin(to))] = from;
                        }
                    }

                    if (from.Name.LocalName == "NameCon")
                    {
                        var key = (Uid(from), Pin(from));

                        if (!_outputs.TryGetValue(key, out var list))
                        {
                            _outputs[key] = list = new List<XElement>();
                        }

                        list.AddRange(ends.Skip(1));
                    }
                }
            }

            public List<string> Render()
            {
                var lines = new List<string>();

                foreach (var pair in _parts)
                {
                    var uid = pair.Key;
                    var part = pair.Value;
                    var name = PartName(part);

                    switch (name)
                    {
                        case "Coil":
                            lines.Add($"{Operand(uid)} := {Input(uid, "in")};");
                            Remember(uid);
                            break;
                        case "NCoil":
                            lines.Add($"{Operand(uid)} := NOT ({Input(uid, "in")});");
                            break;
                        case "SCoil":
                            lines.Add($"S {Operand(uid)} IF {Input(uid, "in")};");
                            Remember(uid);
                            break;
                        case "RCoil":
                            lines.Add($"R {Operand(uid)} IF {Input(uid, "in")};");
                            Remember(uid);
                            break;
                        case "Contact":
                        case "O":
                        case "A":
                        case "X":
                        case "Not":
                        case "PContact":
                        case "NContact":
                        case "PBox":
                        case "NBox":
                            // logic: rendered where it is used; only a dangling end becomes a line
                            if (!HasConsumer(uid))
                            {
                                lines.Add($"(unused) {Value(uid, "out")};");
                            }

                            break;
                        default:
                            if (CompareNames.Contains(name) && HasConsumer(uid))
                            {
                                break;
                            }

                            lines.Add(RenderBox(uid, part, name));
                            break;
                    }
                }

                return lines;
            }

            private void Remember(string uid)
            {
                var condition = Input(uid, "in");

                if (condition.Length > 40 && !_coilConditions.ContainsKey(condition))
                {
                    _coilConditions[condition] = Operand(uid);
                }
            }

            private bool HasConsumer(string uid) =>
                _outputs.Keys.Any(k => k.Item1 == uid);

            private string RenderBox(string uid, XElement part, string name)
            {
                var label = BoxLabel(uid, part, name);
                var en = _inputs.ContainsKey((uid, "en")) ? Input(uid, "en") : (_inputs.ContainsKey((uid, "pre")) ? Input(uid, "pre") : "TRUE");
                var ins = new List<string>();
                var outs = new List<string>();

                foreach (var key in _inputs.Keys.Where(k => k.Item1 == uid).OrderBy(k => k.Item2, StringComparer.Ordinal))
                {
                    if (key.Item2 == "en" || key.Item2 == "pre" || _inputs[key].Name.LocalName == "OpenCon")
                    {
                        continue;
                    }

                    ins.Add($"{key.Item2} := {Input(uid, key.Item2)}");
                }

                foreach (var pair in _outputs.Where(p => p.Key.Item1 == uid).OrderBy(p => p.Key.Item2, StringComparer.Ordinal))
                {
                    var variables = pair.Value.Where(t => t.Name.LocalName == "IdentCon").Select(t => AccessText((string?)t.Attribute("UId"))).ToList();

                    if (variables.Count > 0)
                    {
                        outs.Add($"{pair.Key.Item2} -> {string.Join(", ", variables)}");
                    }
                }

                var line = new StringBuilder();

                if (en != "TRUE")
                {
                    line.Append('[').Append(_coilConditions.TryGetValue(en, out var coil) ? "= " + coil : en).Append("] ");
                }

                line.Append(label).Append('(').Append(string.Join(", ", ins)).Append(')');

                if (outs.Count > 0)
                {
                    line.Append(" => ").Append(string.Join("; ", outs));
                }

                return line.Append(';').ToString();
            }

            private string BoxLabel(string uid, XElement part, string name)
            {
                var instance = Child(part, "Instance");
                var callInfo = Child(part, "CallInfo");

                if (callInfo != null)
                {
                    instance = Child(callInfo, "Instance");
                    name = (string?)callInfo.Attribute("Name") ?? name;
                }

                var label = name.Length > 0 && !PlainIdentifier.IsMatch(name) ? $"\"{name}\"" : name;

                if (instance != null)
                {
                    label += " " + SymbolText((string?)instance.Attribute("Scope") ?? string.Empty, instance);
                }
                else if (_outputs.Any(p => p.Key.Item1 == uid && p.Value.Any(t => t.Name.LocalName == "NameCon")))
                {
                    // other logic reads this box's ENO/out as Name#uid.pin
                    label += "#" + uid;
                }

                return label;
            }

            private string Operand(string uid) => Input(uid, "operand");

            /// <summary>What drives an input pin.</summary>
            private string Input(string uid, string pin)
            {
                if (!_inputs.TryGetValue((uid, pin), out var from))
                {
                    return "?";
                }

                switch (from.Name.LocalName)
                {
                    case "Powerrail":
                        return "TRUE";
                    case "IdentCon":
                        return AccessText((string?)from.Attribute("UId"));
                    case "NameCon":
                        return Value(Uid(from), Pin(from));
                    default:
                        return "?";
                }
            }

            /// <summary>The boolean (or box) value at a part's output pin.</summary>
            private string Value(string uid, string pin)
            {
                if (_memo.TryGetValue((uid, pin), out var known))
                {
                    return known;
                }

                if (!_parts.TryGetValue(uid, out var part) || !_visiting.Add(uid))
                {
                    return $"#{uid}.{pin}";
                }

                string text;
                var name = PartName(part);

                switch (name)
                {
                    case "Contact":
                    {
                        var operand = Operand(uid);
                        text = And(Input(uid, "in"), IsNegated(part, "operand") ? Not(operand) : operand);
                        break;
                    }
                    case "PContact":
                        text = And(Input(uid, "in"), $"P({Operand(uid)})");
                        break;
                    case "NContact":
                        text = And(Input(uid, "in"), $"N({Operand(uid)})");
                        break;
                    case "PBox":
                        // edge of the incoming logic itself (P_TRIG); 'bit' is its edge memory
                        text = $"P({Input(uid, "in")})";
                        break;
                    case "NBox":
                        text = $"N({Input(uid, "in")})";
                        break;
                    case "Not":
                        text = Not(Input(uid, "in"));
                        break;
                    case "O":
                        text = Or(uid);
                        break;
                    case "A":
                        text = Join(" AND ", InputPins(uid).Select(p => Input(uid, p)));
                        break;
                    case "X":
                        text = Join(" XOR ", InputPins(uid).Select(p => Input(uid, p)));
                        break;
                    case "Coil":
                    case "NCoil":
                    case "SCoil":
                    case "RCoil":
                        text = Input(uid, "in");
                        break;
                    default:
                        if (CompareNames.Contains(name))
                        {
                            var compare = $"({Input(uid, "in1")} {CompareOps[name]} {Input(uid, "in2")})";
                            text = _inputs.ContainsKey((uid, "pre")) ? And(Input(uid, "pre"), compare) : compare;
                        }
                        else
                        {
                            // a timer/counter/FB output reads as its instance member, as in TIA Portal
                            var instance = Child(part, "Instance") ?? Child(Child(part, "CallInfo"), "Instance");

                            text = instance != null
                                ? $"{SymbolText((string?)instance.Attribute("Scope") ?? string.Empty, instance)}.{pin.ToUpperInvariant()}"
                                : $"{name}#{uid}.{pin}";
                        }

                        break;
                }

                _visiting.Remove(uid);
                _memo[(uid, pin)] = text;

                return text;
            }

            /// <summary>
            /// A parallel branch. Each branch starts where the rung splits, so the logic before the
            /// split would be repeated in every branch ("a AND b OR a AND c"); when all branches start
            /// at the same split, it is written once: "a AND (b OR c)", as the editor draws it.
            /// </summary>
            private string Or(string uid)
            {
                var pins = InputPins(uid).ToList();
                var branches = pins.Select(p => (Pin: p, Branch: Branch(uid, p))).ToList();
                var parts = new List<string>();

                // branches that split off at the same point share what comes before it
                foreach (var group in branches.GroupBy(b => b.Branch.Start))
                {
                    var members = group.ToList();

                    if (group.Key == null || members.Count == 1)
                    {
                        parts.AddRange(members.Select(m => Input(uid, m.Pin)));
                        continue;
                    }

                    var alternatives = Join(" OR ", members.Select(m => m.Branch.Terms.Count == 0 ? "TRUE" : Join(" AND ", m.Branch.Terms)));
                    var before = Source(group.Key);

                    parts.Add(before == "TRUE" ? alternatives : And(before, alternatives));
                }

                return Join(" OR ", parts);
            }

            /// <summary>The series contacts of one branch back to the point where it splits off.</summary>
            private (XElement? Start, List<string> Terms) Branch(string uid, string pin)
            {
                var terms = new List<string>();

                if (!_inputs.TryGetValue((uid, pin), out var from))
                {
                    return (null, terms);
                }

                while (from.Name.LocalName == "NameCon" && Pin(from) == "out"
                       && _parts.TryGetValue(Uid(from), out var part)
                       && Fanout(Uid(from)) == 1
                       && SeriesTerm(Uid(from), part) is string term
                       && _inputs.TryGetValue((Uid(from), "in"), out var next))
                {
                    terms.Insert(0, term);
                    from = next;
                }

                return (from.Name.LocalName == "Powerrail" || from.Name.LocalName == "NameCon" ? from : null, terms);
            }

            // the condition a series element adds, or null for anything that is not a plain contact
            private string? SeriesTerm(string uid, XElement part)
            {
                switch (PartName(part))
                {
                    case "Contact":
                        var operand = Operand(uid);
                        return IsNegated(part, "operand") ? Not(operand) : operand;
                    case "PContact":
                        return $"P({Operand(uid)})";
                    case "NContact":
                        return $"N({Operand(uid)})";
                    default:
                        return null;
                }
            }

            private int Fanout(string uid) =>
                _outputs.TryGetValue((uid, "out"), out var targets) ? targets.Count : 0;

            private string Source(XElement from) =>
                from.Name.LocalName == "Powerrail" ? "TRUE" : Value(Uid(from), Pin(from));

            private IEnumerable<string> InputPins(string uid) =>
                _inputs.Keys.Where(k => k.Item1 == uid && k.Item2.StartsWith("in", StringComparison.Ordinal))
                    .Select(k => k.Item2)
                    .OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal);

            private string AccessText(string? uid)
            {
                if (uid == null || !_accesses.TryGetValue(uid, out var access))
                {
                    return "?";
                }

                return LadText.AccessText(access);
            }
        }

        /// <summary>An operand as TIA Portal writes it: "Global".member, #local, constants as they are.</summary>
        public static string AccessText(XElement access)
        {
            var scope = (string?)access.Attribute("Scope") ?? string.Empty;

            switch (scope)
            {
                case "LiteralConstant":
                case "TypedConstant":
                    return Child(Child(access, "Constant"), "ConstantValue")?.Value ?? "?";
                case "GlobalConstant":
                case "LocalConstant":
                {
                    var constant = Child(access, "Constant");
                    var name = (string?)constant?.Attribute("Name") ?? "?";
                    return scope == "LocalConstant" ? "#" + name : $"\"{name}\"";
                }
                case "Address":
                    return AddressText(Child(access, "Address"));
                case "GlobalVariable":
                case "LocalVariable":
                    return SymbolText(scope, Child(access, "Symbol"));
                default:
                    return $"({scope})";
            }
        }

        private static string SymbolText(string scope, XElement? symbol)
        {
            if (symbol == null)
            {
                return "?";
            }

            var parts = new List<string>();

            foreach (var component in symbol.Elements().Where(e => e.Name.LocalName == "Component"))
            {
                var name = (string?)component.Attribute("Name") ?? "?";
                var absolute = parts.Count == 0 && name.StartsWith("%", StringComparison.Ordinal);
                var text = PlainIdentifier.IsMatch(name) || absolute ? name : $"\"{name}\"";

                if (parts.Count == 0 && scope == "GlobalVariable" && !absolute)
                {
                    text = $"\"{name}\"";
                }

                if ((string?)component.Attribute("AccessModifier") == "Array")
                {
                    var indexes = component.Elements().Where(e => e.Name.LocalName == "Access").Select(AccessText);
                    text += "[" + string.Join(", ", indexes) + "]";
                }

                var slice = (string?)component.Attribute("SliceAccessModifier");

                if (!string.IsNullOrEmpty(slice))
                {
                    text += ".%" + slice!.ToUpperInvariant();
                }

                parts.Add(text);
            }

            var joined = string.Join(".", parts);

            return scope == "LocalVariable" ? "#" + joined : joined;
        }

        private static string AddressText(XElement? address)
        {
            if (address == null)
            {
                return "?";
            }

            var area = (string?)address.Attribute("Area") switch
            {
                "Input" => "I",
                "Output" => "Q",
                "Memory" => "M",
                "Timer" => "T",
                "Counter" => "C",
                var other => other ?? "?"
            };

            var type = (string?)address.Attribute("Type") ?? string.Empty;
            int.TryParse((string?)address.Attribute("BitOffset"), out var bits);

            if (type == "Bool")
            {
                return $"%{area}{bits / 8}.{bits % 8}";
            }

            var size = type switch { "Byte" => "B", "Word" => "W", "Int" => "W", "DWord" => "D", "DInt" => "D", "Real" => "D", _ => "" };

            return $"%{area}{size}{bits / 8}";
        }

        private static string PartName(XElement part)
        {
            if (part.Name.LocalName == "Call")
            {
                return (string?)Child(part, "CallInfo")?.Attribute("Name") ?? "Call";
            }

            return (string?)part.Attribute("Name") ?? part.Name.LocalName;
        }

        private static bool IsNegated(XElement part, string pin) =>
            part.Elements().Any(e => e.Name.LocalName == "Negated" && (string?)e.Attribute("Name") == pin);

        private static string And(string left, string right)
        {
            if (left == "TRUE")
            {
                return right;
            }

            return $"{Group(left, " OR ")} AND {Group(right, " OR ")}";
        }

        private static string Not(string value) =>
            PlainOperand(value) ? "NOT " + value : $"NOT ({value})";

        private static string Join(string op, IEnumerable<string> values)
        {
            var list = values.ToList();

            return list.Count switch
            {
                0 => "?",
                1 => list[0],
                _ => string.Join(op, list.Select(v => op == " OR " ? v : Group(v, " OR ")))
            };
        }

        // parenthesise an OR term inside AND
        private static string Group(string value, string looseOp) =>
            value.Contains(looseOp) && !(value.StartsWith("(", StringComparison.Ordinal) && value.EndsWith(")", StringComparison.Ordinal) && Balanced(value.Substring(1, value.Length - 2)))
                ? $"({value})"
                : value;

        private static bool Balanced(string s)
        {
            var depth = 0;

            foreach (var c in s)
            {
                depth += c == '(' ? 1 : c == ')' ? -1 : 0;

                if (depth < 0)
                {
                    return false;
                }
            }

            return depth == 0;
        }

        private static bool PlainOperand(string value) =>
            !value.Contains(' ');

        private static string Uid(XElement end) => (string?)end.Attribute("UId") ?? "?";

        private static string Pin(XElement end) => (string?)end.Attribute("Name") ?? "?";

        private static XElement? Child(XElement? parent, string localName) =>
            parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

        private static string? MultilingualText(XElement unit, string compositionName)
        {
            var text = unit.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "MultilingualText" && (string?)e.Attribute("CompositionName") == compositionName);

            if (text == null)
            {
                return null;
            }

            string? fallback = null;

            foreach (var item in text.Descendants().Where(e => e.Name.LocalName == "MultilingualTextItem"))
            {
                var culture = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "Culture")?.Value;
                var value = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "Text")?.Value;

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (culture == "en-US")
                {
                    return value;
                }

                fallback ??= value;
            }

            return fallback;
        }

        private static string OneLine(string text) =>
            Regex.Replace(text.Trim(), @"\s*\r?\n\s*", " / ");
    }
}
