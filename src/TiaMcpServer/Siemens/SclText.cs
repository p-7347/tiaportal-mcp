using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Renders the StructuredText XML of a SimaticML export (Token/Blank/NewLine/Access/Parameter/
    /// LineComment/Comment/Text) back into SCL source text. Namespace is ignored throughout
    /// (LocalName only). Pure text work on the XML: no Siemens types, so it is testable without
    /// TIA Portal.
    ///
    /// Calls come in two shapes. Built-in instructions and instance calls are
    /// Access Scope="Call" with an Instruction child (LIMIT(...), CONCAT(...), INT_TO_DINT(...), and
    /// '#timer(IN := ...)' where the Instruction has no name and follows the instance's own Access);
    /// until 2026-11-17 these were dropped from the text altogether, argument list included, so
    /// '#x := LIMIT(MN := 0, IN := #a, MX := 9);' read as '#x := ;' and FindInCode could not find
    /// anything inside them (306 such calls in 158 exported blocks of one project). Block calls can
    /// also be a CallInfo child.
    /// </summary>
    public static class SclText
    {
        private static readonly Regex PlainSclName = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        public static string Render(XElement container)
        {
            var sb = new StringBuilder();
            foreach (var child in container.Elements())
            {
                RenderNode(child, sb);
            }
            return sb.ToString();
        }

        private static void RenderNode(XElement c, StringBuilder sb)
        {
            switch (c.Name.LocalName)
            {
                case "Token":
                    sb.Append((string?)c.Attribute("Text") ?? "");
                    break;
                case "Blank":
                    sb.Append(' ', ParseNum(c));
                    break;
                case "NewLine":
                    sb.Append('\n', ParseNum(c));
                    break;
                case "Access":
                    RenderAccess(c, sb);
                    break;
                case "LineComment":
                    sb.Append("//").Append(ConcatText(c));
                    break;
                case "Comment":
                    sb.Append("(*").Append(ConcatText(c)).Append("*)");
                    break;
                case "Text":
                    sb.Append(c.Value);
                    break;
                case "Parameter":
                    sb.Append((string?)c.Attribute("Name") ?? "");
                    RenderChildren(c, sb);
                    break;
                case "NamelessParameter":
                    RenderChildren(c, sb);
                    break;
            }
        }

        private static void RenderChildren(XElement parent, StringBuilder sb)
        {
            foreach (var child in parent.Elements())
            {
                RenderNode(child, sb);
            }
        }

        private static int ParseNum(XElement e)
        {
            var attr = (string?)e.Attribute("Num");
            return attr != null && int.TryParse(attr, out var n) ? n : 1;
        }

        private static string ConcatText(XElement e) =>
            string.Concat(e.Descendants().Where(d => d.Name.LocalName == "Text").Select(d => d.Value));

        private static string Quoted(string name) =>
            PlainSclName.IsMatch(name) ? name : $"\"{name}\"";

        private static void RenderAccess(XElement c, StringBuilder sb)
        {
            var scope = (string?)c.Attribute("Scope");

            if (scope == "LiteralConstant" || scope == "TypedConstant")
            {
                var value = c.Descendants().FirstOrDefault(d => d.Name.LocalName == "ConstantValue");
                var type = c.Descendants().FirstOrDefault(d => d.Name.LocalName == "ConstantType");
                if (type != null && scope == "TypedConstant")
                {
                    sb.Append(type.Value).Append('#');
                }
                sb.Append(value != null ? value.Value : "?");
                return;
            }

            if (scope == "GlobalConstant" || scope == "LocalConstant")
            {
                // a user constant: only its name is in the XML ("MAX_AXIS", #cLimit)
                var name = (string?)c.Elements().FirstOrDefault(e => e.Name.LocalName == "Constant")?.Attribute("Name") ?? "?";
                sb.Append(scope == "LocalConstant" ? "#" + Quoted(name) : $"\"{name}\"");
                return;
            }

            if (scope == "Address")
            {
                sb.Append(LadText.AddressText(c.Elements().FirstOrDefault(e => e.Name.LocalName == "Address")));
                return;
            }

            var callInfo = c.Elements().FirstOrDefault(e => e.Name.LocalName == "CallInfo");
            if (scope == "Call" && callInfo != null)
            {
                RenderCallInfo(callInfo, sb);
                return;
            }

            var instruction = c.Elements().FirstOrDefault(e => e.Name.LocalName == "Instruction");
            if (scope == "Call" && instruction != null)
            {
                // No name: an instance call, the instance was written by the Access before this one.
                var name = (string?)instruction.Attribute("Name");
                if (!string.IsNullOrEmpty(name))
                {
                    sb.Append(Quoted(name!));
                }

                RenderChildren(instruction, sb);
                return;
            }

            if (scope == "LocalVariable" || scope == "GlobalVariable" || scope == "Call")
            {
                RenderSymbolAccess(c, scope, sb);
                return;
            }

            sb.Append('<').Append(scope).Append('>');
        }

        // CallInfo's children are rendered with their own NewLine/Blank handling (a NewLine here
        // is always a single '\n' regardless of its Num attribute) - matches how TIA lays out
        // multi-line call argument lists, confirmed against real exported SCL call networks.
        private static void RenderCallInfo(XElement ci, StringBuilder sb)
        {
            var instance = ci.Elements().FirstOrDefault(e => e.Name.LocalName == "Instance");
            var instanceName = (string?)instance?.Elements().FirstOrDefault(e => e.Name.LocalName == "Component")?.Attribute("Name");

            if (instanceName == null)
            {
                // an FC has no instance: the call is written with the block's own name
                sb.Append('"').Append((string?)ci.Attribute("Name") ?? "").Append('"');
            }
            else if ((string?)instance!.Attribute("Scope") == "LocalVariable")
            {
                sb.Append('#').Append(Quoted(instanceName));
            }
            else
            {
                sb.Append('"').Append(instanceName).Append('"');
            }

            foreach (var k in ci.Elements())
            {
                switch (k.Name.LocalName)
                {
                    case "Token":
                        sb.Append((string?)k.Attribute("Text") ?? "");
                        break;
                    case "Parameter":
                        sb.Append((string?)k.Attribute("Name") ?? "");
                        foreach (var pc in k.Elements())
                        {
                            RenderNode(pc, sb);
                        }
                        break;
                    case "NamelessParameter":
                        RenderChildren(k, sb);
                        break;
                    case "NewLine":
                        sb.Append('\n');
                        break;
                    case "Blank":
                        sb.Append(' ', ParseNum(k));
                        break;
                }
            }
        }

        // LocalVariable/GlobalVariable (and bare Call) access: a dotted Symbol/Component chain.
        // The first Component is quoted ("Db") for GlobalVariable or '#'-prefixed for
        // LocalVariable; later Components (after a '.' Token) are plain. Array index brackets
        // are NOT added here - the exported XML already contains literal "[" / "]" Token
        // children inside the indexed Component, so rendering them in place is correct.
        private static void RenderSymbolAccess(XElement c, string scope, StringBuilder sb)
        {
            var symbol = c.Elements().FirstOrDefault(e => e.Name.LocalName == "Symbol");
            if (symbol == null)
            {
                return;
            }

            var any = false;

            foreach (var cc in symbol.Elements())
            {
                if (cc.Name.LocalName == "Component")
                {
                    var name = (string?)cc.Attribute("Name") ?? "";

                    // names that are not plain identifiers ("Home[0]", "S->P") need quotes in SCL,
                    // or the text reads as an array access and does not compile
                    var quoted = PlainSclName.IsMatch(name) || name.StartsWith("%", StringComparison.Ordinal) ? name : $"\"{name}\"";

                    if (!any && scope == "GlobalVariable")
                    {
                        sb.Append(name.StartsWith("%", StringComparison.Ordinal) ? name : $"\"{name}\"");
                    }
                    else if (!any && scope == "LocalVariable")
                    {
                        sb.Append('#').Append(quoted);
                    }
                    else
                    {
                        sb.Append(quoted);
                    }
                    any = true;

                    foreach (var x in cc.Elements())
                    {
                        if (x.Name.LocalName == "Token" || x.Name.LocalName == "Access")
                        {
                            RenderNode(x, sb);
                        }
                    }
                }
                else if (cc.Name.LocalName == "Token")
                {
                    sb.Append((string?)cc.Attribute("Text") ?? "");
                    any = true;
                }
            }
        }
    }
}
