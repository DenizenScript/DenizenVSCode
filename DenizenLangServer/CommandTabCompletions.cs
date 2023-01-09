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

        public static void Register(Dictionary<string, CommandTabCompletions> set, string command, string prefix, Func<IEnumerable<string>> options)
        {
            if (!set.TryGetValue(command, out CommandTabCompletions completer))
            {
                set[command] = completer = new CommandTabCompletions();
            }
            completer.ByPrefix[prefix] = (arg, Token) => options().Where(s => s.StartsWith(arg)).Select(s => new CompletionItem(s, CompletionItemKind.Enum, Token));
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
                Register(ByCommand, cmd, "", () => Data.Blocks);
            }
            foreach (string cmd in new[] { "create", "spawn", "fakespawn", "disguise"})
            {
                Register(ByCommand, cmd, "", () => Data.Entities);
            }
            Register(ByCommand, "playeffect", "effect", () => Data.Particles.Concat(Data.Effects));
            Register(ByCommand, "playsound", "sound", () => Data.Sounds);
            foreach (string cmd in new[] { "give", "fakeitem", "displayitem", "drop", "itemcooldown" })
            {
                Register(ByCommand, "give", "", () => Data.Items);
            }
            Register(ByCommand, "take", "item", () => Data.Items);
            Register(ByCommand, "cast", "", () => Data.PotionEffects);
            Register(ByCommand, "statistic", "", () => Data.Statistics);
            HashSet<string> determineCompletions = new() { "cancelled", "cancelled:false" };
            Register(ByCommand, "determine", "", () => determineCompletions);
            IEnumerable<CompletionItem> scriptsByType(string t, string arg, JToken Token)
            {
                if (!ClientConfiguration.TrackFullWorkspace || WorkspaceTracker.WorkspaceData is null)
                {
                    return Array.Empty<CompletionItem>();
                }
                return WorkspaceTracker.WorkspaceData.Scripts.Values.Where(s => t is null || s.Type == t).Where(s => s.Name.StartsWith(arg)).Select(s => new CompletionItem(s.Name, CompletionItemKind.Method, s.Name, DescribeScript(s), Token));
            }
            foreach (string runner in new[] { "run", "runlater", "clickable", "inject", "modifyblock" })
            {
                Register(ByCommand, runner, "", (a, t) => scriptsByType("task", a, t));
            }
            Register(ByCommand, "shoot", "script", (a, t) => scriptsByType("task", a, t));
            Register(ByCommand, "narrate", "format", (a, t) => scriptsByType("format", a, t));
            Register(ByCommand, "zap", "", (a, t) => scriptsByType("interact", a, t));
            Register(ByCommand, "map", "script", (a, t) => scriptsByType("map", a, t));
            /////////////////////////
            Register(ByTag, "<procedure_script_name>", "", (a, t) => scriptsByType("procedure", a, t));
            Register(ByTag, "<script>", "", (a, t) => scriptsByType(null, a, t));
        }

        public static MarkupContent DescribeScript(ScriptContainerData script)
        {
            string defInfo = "";
            if (script.Keys.TryGetValue(new ScriptChecker.LineTrackedString(0, "definitions", 0), out object definitions))
            {
                defInfo = "### Definitions:\n";
                foreach (string def in definitions.ToString().Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string name = def;
                    string info = "";
                    if (def.Contains('[') && def.EndsWithFast(']'))
                    {
                        name = def.Before('[').Trim();
                        info = def.After('[').BeforeLast(']').Trim();
                    }
                    defInfo += $"- **{name}:** {info}  \n";
                }
            }
            string formatScr = "";
            if (script.Keys.TryGetValue(new ScriptChecker.LineTrackedString(0, "format", 0), out object format))
            {
                formatScr = $"\n**Format:** `{format}`\n";
            }
            return new MarkupContent(MarkupKind.Markdown, $"{script.Type} script '{script.Name}'  \n{defInfo}{formatScr}\nIn `{script.FileName}` at line `{script.LineNumber}`");
        }
    }
}
