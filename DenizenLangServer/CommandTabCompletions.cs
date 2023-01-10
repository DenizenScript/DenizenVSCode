using FreneticUtilities.FreneticExtensions;
using LanguageServer.VsCode.Contracts;
using Newtonsoft.Json.Linq;
using SharpDenizenTools.MetaHandlers;
using SharpDenizenTools.MetaObjects;
using SharpDenizenTools.ScriptAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using YamlDotNet.Core.Tokens;
using static System.Collections.Specialized.BitVector32;

namespace DenizenLangServer
{
    public class CommandTabCompletions
    {
        public static Dictionary<string, CommandTabCompletions> ByCommand = new();

        public static Dictionary<string, CommandTabCompletions> ByTag = new();

        public Dictionary<string, Func<string, JToken, IEnumerable<CompletionItem>>> ByPrefix = new();

        public static bool TryGetCompleterForTagParam(string param, out CommandTabCompletions completer)
        {
            if (param.StartsWithFast('(') && param.EndsWithFast(')'))
            {
                param = param[1..^1];
            }
            if (param.EndsWith("|..."))
            {
                param = param[..^"|...".Length];
            }
            return ByTag.TryGetValue(param, out completer);
        }

        public static void Register(Dictionary<string, CommandTabCompletions> set, string command, string prefix, Func<IEnumerable<string>> options, string enumKey)
        {
            if (!set.TryGetValue(command, out CommandTabCompletions completer))
            {
                set[command] = completer = new CommandTabCompletions();
            }
            completer.ByPrefix[prefix] = (arg, Token) => CompleteEnum(options(), enumKey, arg, Token);
        }

        public static void Register(Dictionary<string, CommandTabCompletions> set, string command, string prefix, Func<string, JToken, IEnumerable<CompletionItem>> options)
        {
            if (!set.TryGetValue(command, out CommandTabCompletions completer))
            {
                set[command] = completer = new CommandTabCompletions();
            }
            completer.ByPrefix[prefix] = options;
        }

        public static ExtraData Data => ExtraData.Data;

        static CommandTabCompletions()
        {
            foreach (string cmd in new[] { "modifyblock", "showfake" })
            {
                Register(ByCommand, cmd, "", () => Data.Blocks, "Block Material");
            }
            foreach (string cmd in new[] { "create", "spawn", "fakespawn" })
            {
                Register(ByCommand, cmd, "", SuggestEntityType);
            }
            Register(ByCommand, "disguise", "as", SuggestEntityType);
            Register(ByCommand, "playeffect", "effect", () => Data.Particles.Concat(Data.Effects), "Particle Effect");
            Register(ByCommand, "playsound", "sound", () => Data.Sounds, "Sound Enum");
            foreach (string cmd in new[] { "give", "fakeitem", "displayitem", "drop", "itemcooldown" })
            {
                Register(ByCommand, cmd, "", SuggestItem);
            }
            Register(ByCommand, "take", "item", SuggestItem);
            Register(ByCommand, "cast", "", () => Data.PotionEffects, "Potion Effect Type");
            Register(ByCommand, "statistic", "", () => Data.Statistics, "Statistic");
            HashSet<string> determineCompletions = new() { "cancelled", "cancelled:false" };
            Register(ByCommand, "determine", "", () => determineCompletions, null);
            foreach (string runner in new[] { "run", "runlater", "clickable", "inject", "modifyblock" })
            {
                Register(ByCommand, runner, "", (a, t) => SuggestScriptByType("task", a, t));
            }
            Register(ByCommand, "shoot", "script", (a, t) => SuggestScriptByType("task", a, t));
            foreach (string cmd in new[] { "narrate", "announce", "actionbar" })
            {
                Register(ByCommand, cmd, "format", (a, t) => SuggestScriptByType("format", a, t));
            }
            Register(ByCommand, "zap", "", (a, t) => SuggestScriptByType("interact", a, t));
            Register(ByCommand, "map", "script", (a, t) => SuggestScriptByType("map", a, t));
            /////////////////////////
            Register(ByTag, "<procedure_script_name>", "", (a, t) => SuggestScriptByType("procedure", a, t));
            Register(ByTag, "<script>", "", (a, t) => SuggestScriptByType(null, a, t));
            Register(ByTag, "<material>", "", () => Data.Materials, "Material");
            Register(ByTag, "<item>", "", SuggestItem);
            Register(ByTag, "<statistic>", "", () => Data.Statistics, "Statistic");
            Register(ByTag, "<entity_type>", "", SuggestEntityType);
            Register(ByTag, "<effect>", "", () => Data.PotionEffects, "Potion Effect Type");
            Register(ByTag, "<format_script>", "", (a, t) => SuggestScriptByType("format", a, t));
            Register(ByTag, "<biome>", "", () => Data.Biomes, "Biome");
            Register(ByTag, "<enchantment>", "", SuggestEnchantmentType);
            Register(ByTag, "<inventory>", "", SuggestInventoryType);
            Register(ByTag, "<property-name>", "", (a, t) => SuggestMechanisms(null, a, t));
            Register(ByTag, "<mechanism>=<value>", "", (a, t) => SuggestMechPair(null, a, t));
            Register(ByTag, "<mechanism>=<value>;...", "", (a, t) => SuggestMechPairSet(null, a, t));
        }

        public static IEnumerable<CompletionItem> CompleteEnum(IEnumerable<string> enumSet, string key, string arg, JToken Token)
        {
            return enumSet.Where(i => i.StartsWith(arg)).Select(i => new CompletionItem(i, CompletionItemKind.Enum, i, key == null ? null : new MarkupContent(MarkupKind.Markdown, $"Vanilla **{key}**: {i}"), Token));
        }

        public static IEnumerable<CompletionItem> SuggestMechanisms(string objectType, string arg, JToken Token)
        {
            return MetaDocs.CurrentMeta.Mechanisms.Values.Where(m => m.MechName.StartsWith(arg) && (objectType is null || m.MechObject == objectType)).Select(m => new CompletionItem(m.MechName, CompletionItemKind.Property, m.MechName, DescribeMech(m), Token));
        }

        public static IEnumerable<CompletionItem> SuggestMechPair(string objectType, string arg, JToken Token)
        {
            if (arg.Contains('='))
            {
                return Array.Empty<CompletionItem>();
            }
            else
            {
                return SuggestMechanisms(objectType, arg, Token);
            }
        }

        public static IEnumerable<CompletionItem> SuggestMechPairSet(string objectType, string arg, JToken Token)
        {
            return SuggestMechPair(objectType, arg.AfterLast(';'), Token);
        }

        public static IEnumerable<CompletionItem> SuggestInventoryType(string arg, JToken Token)
        {
            List<CompletionItem> results = new();
            results.AddRange(CompleteEnum(ExtraData.InventoryMatchers, "Inventory Type", arg, Token));
            results.AddRange(SuggestScriptByType("inventory", arg, Token));
            return results;
        }

        public static IEnumerable<CompletionItem> SuggestEnchantmentType(string arg, JToken Token)
        {
            List<CompletionItem> results = new();
            results.AddRange(CompleteEnum(Data.Enchantments, "Enchantment Key", arg, Token));
            results.AddRange(SuggestScriptByType("enchantment", arg, Token));
            return results;
        }

        public static IEnumerable<CompletionItem> SuggestEntityType(string arg, JToken Token)
        {
            List<CompletionItem> results = new();
            results.AddRange(CompleteEnum(Data.EntityArray, "Entity Type", arg, Token));
            results.AddRange(SuggestScriptByType("entity", arg, Token));
            return results;
        }

        public static IEnumerable<CompletionItem> SuggestItem(string arg, JToken Token)
        {
            List<CompletionItem> results = new();
            results.AddRange(CompleteEnum(Data.ItemArray, "Item", arg, Token));
            results.AddRange(SuggestScriptByType("item", arg, Token));
            results.AddRange(SuggestScriptByType("book", arg, Token));
            return results;
        }

        public static IEnumerable<CompletionItem> SuggestScriptByType(string type, string arg, JToken Token)
        {
            if (!ClientConfiguration.TrackFullWorkspace || WorkspaceTracker.WorkspaceData is null)
            {
                return Array.Empty<CompletionItem>();
            }
            return WorkspaceTracker.WorkspaceData.Scripts.Values.Where(s => type is null || s.Type == type).Where(s => s.Name.StartsWith(arg)).Select(s => new CompletionItem(s.Name, CompletionItemKind.Method, s.Name, DescribeScript(s), Token));
        }

        public static MarkupContent DescribeScript(ScriptContainerData script)
        {
            string addedFirst = "";
            foreach (string key in new[] { "Description", "Display Name", "Title", "Name Single" })
            {
                if (script.Keys.TryGetValue(new ScriptChecker.LineTrackedString(0, key.ToLowerFast(), 0), out object val))
                {
                    addedFirst += $"\n**{key}:** {val}  ";
                }
            }
            string defInfo = "";
            if (script.Keys.TryGetValue(new ScriptChecker.LineTrackedString(0, "definitions", 0), out object definitions))
            {
                defInfo = "\n### Definitions:";
                foreach (string def in definitions.ToString().Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string name = def;
                    string info = "";
                    if (def.Contains('[') && def.EndsWithFast(']'))
                    {
                        name = def.Before('[').Trim();
                        info = def.After('[').BeforeLast(']').Trim();
                    }
                    defInfo += $"\n- **{name}:** {info}  ";
                }
            }
            string addedAfter = "";
            foreach (string key in new[] { "ID", "Entity_Type", "Inventory", "Size", "Material", "Book", "Format" })
            {
                if (script.Keys.TryGetValue(new ScriptChecker.LineTrackedString(0, key.ToLowerFast(), 0), out object val))
                {
                    addedAfter += $"\n**{key}:** `{val}`  ";
                }
            }
            return new MarkupContent(MarkupKind.Markdown, $"{script.Type} script '{script.Name}'  {addedFirst}{defInfo}\n{addedAfter}\nIn `{script.FileName}` at line `{(script.LineNumber + 1)}`");
        }

        public static string ObligatoryText(MetaObject obj)
        {
            string result = "\n\n";
            if (!string.IsNullOrWhiteSpace(obj.Plugin))
            {
                result += $"Required plugin(s) or platform(s): {DescriptionClean(obj.Plugin)}\n\n";
            }
            if (!string.IsNullOrWhiteSpace(obj.Deprecated))
            {
                result += $"Deprecation notice: {DescriptionClean(obj.Deprecated)}\n\n";
            }
            if (obj.Warnings != null && obj.Warnings.Any())
            {
                result += "### WARNING\n" + DescriptionClean(string.Join("\n- ", obj.Warnings)) + "\n\n";
            }
            return result;
        }

        public static string DescriptionClean(string input)
        {
            int codeStart = input.IndexOf("<code>");
            if (codeStart != -1)
            {
                int codeEnd = input.IndexOf("</code>", codeStart);
                if (codeEnd != -1)
                {
                    return DescriptionClean(input[..codeStart]) + "\n```yml\n" + input[(codeStart + "<code>".Length)..(codeEnd)].Replace('`', '\'') + "\n```\n" + DescriptionClean(input[(codeEnd + "</code>".Length)..]);
                }
            }
            input = input.Replace('`', '\'').Replace("&", "&amp;").Replace("#", "&#35;").Replace("<", "&lt;").Replace(">", "&gt;");
            return input;
        }

        public static string LinkMeta(MetaObject obj)
        {
            return $"[Meta Docs: {obj.Type.WebPath} {DescriptionClean(obj.Name)}](https://" + $"meta.denizenscript.com/Docs/{obj.Type.WebPath}/{HttpUtility.UrlEncode(obj.CleanName)})";
        }

        public static MarkupContent DescribeCommand(MetaCommand command)
        {
            return new MarkupContent(MarkupKind.Markdown, $"### Command {command.Name}\n{DescriptionClean(command.Short)}\n```xml\n- {command.Syntax}\n```\n{LinkMeta(command)}"
                            + $"\n\n{DescriptionClean(command.Description)}{ObligatoryText(command)}Related Tags:\n- {DescriptionClean(string.Join("\n- ", command.Tags))}");
        }

        public static MarkupContent DescribeMech(MetaMechanism mechanism)
        {
            return new MarkupContent(MarkupKind.Markdown, $"### {mechanism.MechObject} Mechanism {mechanism.MechName}\n{LinkMeta(mechanism)}\n\nInput: {mechanism.Input}"
                                + $"\n\n{DescriptionClean(mechanism.Description)}{ObligatoryText(mechanism)}Related Tags:\n- {DescriptionClean(string.Join("\n- ", mechanism.Tags))}");
        }

        public static MarkupContent DescribeTag(MetaTag tag)
        {
            return new MarkupContent(MarkupKind.Markdown, $"### Tag {DescriptionClean(tag.Name)}\n{LinkMeta(tag)}\n\nReturns: {tag.Returns}\n\n{DescriptionClean(tag.Description)}{ObligatoryText(tag)}");
        }

        public static MarkupContent DescribeLang(MetaLanguage lang)
        {
            return new MarkupContent(MarkupKind.Markdown, $"### {DescriptionClean(lang.Name)}\n{LinkMeta(lang)}\n\n{DescriptionClean(lang.Description)}{ObligatoryText(lang)}");
        }

        public static MarkupContent DescribeEvent(MetaEvent evt)
        {
            return new MarkupContent(MarkupKind.Markdown, $"### Event {DescriptionClean(evt.Name)}\n{LinkMeta(evt)}\n\nTriggers: {DescriptionClean(evt.Triggers)}\n\n"
                            + $"Contexts:\n- {DescriptionClean(string.Join("\n- ", evt.Context))}{ObligatoryText(evt)}");
        }

        public static MarkupContent DescribeAction(MetaAction action)
        {
            return new MarkupContent(MarkupKind.Markdown, $"### Action {DescriptionClean(action.Name)}\n\n{LinkMeta(action)}\n\nTriggers: {DescriptionClean(action.Triggers)}"
                            + $"\n\nContexts:\n- {DescriptionClean(string.Join("\n- ", action.Context))}{ObligatoryText(action)}");
        }
    }
}
