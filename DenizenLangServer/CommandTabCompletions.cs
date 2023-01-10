using FreneticUtilities.FreneticExtensions;
using LanguageServer.VsCode.Contracts;
using Newtonsoft.Json.Linq;
using SharpDenizenTools.MetaHandlers;
using SharpDenizenTools.ScriptAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core.Tokens;

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
        }

        public static IEnumerable<CompletionItem> CompleteEnum(IEnumerable<string> enumSet, string key, string arg, JToken Token)
        {
            return enumSet.Where(i => i.StartsWith(arg)).Select(i => new CompletionItem(i, CompletionItemKind.Enum, i, key == null ? null : new MarkupContent(MarkupKind.Markdown, $"Vanilla **{key}**: {i}"), Token));
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
    }
}
