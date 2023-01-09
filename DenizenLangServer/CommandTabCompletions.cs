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
        public static Dictionary<string, CommandTabCompletions> TabCompletions = new();

        public Dictionary<string, Func<string, JToken, IEnumerable<CompletionItem>>> ByPrefix = new();

        public static void Register(string command, string prefix, Func<IEnumerable<string>> options)
        {
            if (!TabCompletions.TryGetValue(command, out CommandTabCompletions completer))
            {
                TabCompletions[command] = completer = new CommandTabCompletions();
            }
            completer.ByPrefix[prefix] = (arg, Token) => options().Where(s => s.StartsWith(arg)).Select(s => new CompletionItem(s, CompletionItemKind.Enum, Token));
        }

        public static void Register(string command, string prefix, Func<string, JToken, IEnumerable<CompletionItem>> options)
        {
            if (!TabCompletions.TryGetValue(command, out CommandTabCompletions completer))
            {
                TabCompletions[command] = completer = new CommandTabCompletions();
            }
            completer.ByPrefix[prefix] = options;
        }

        public static ExtraData Data => ExtraData.Data;

        static CommandTabCompletions()
        {
            foreach (string cmd in new[] { "modifyblock", "showfake" })
            {
                Register(cmd, "", () => Data.Blocks);
            }
            foreach (string cmd in new[] { "create", "spawn", "fakespawn", "disguise"})
            {
                Register(cmd, "", () => Data.Entities);
            }
            Register("playeffect", "effect", () => Data.Particles.Concat(Data.Effects));
            Register("playsound", "sound", () => Data.Sounds);
            foreach (string cmd in new[] { "give", "fakeitem", "displayitem", "drop", "itemcooldown" })
            {
                Register("give", "", () => Data.Items);
            }
            Register("take", "item", () => Data.Items);
            Register("cast", "", () => Data.PotionEffects);
            Register("statistic", "", () => Data.Statistics);
            HashSet<string> determineCompletions = new() { "cancelled", "cancelled:false" };
            Register("determine", "", () => determineCompletions);
            Func<string, string, JToken, IEnumerable<CompletionItem>> scriptsByType = (t, arg, Token) => WorkspaceTracker.WorkspaceData.Scripts.Values.Where(s => s.Type == t).Select(s => new CompletionItem(s.Name, CompletionItemKind.Method, s.Name, DescribeScript(s), Token));
            foreach (string runner in new[] { "run", "runlater", "clickable", "inject", "modifyblock" })
            {
                Register(runner, "", (a, t) => scriptsByType("task", a, t));
            }
            Register("shoot", "script", (a, t) => scriptsByType("task", a, t));
            Register("narrate", "format", (a, t) => scriptsByType("format", a, t));
            Register("zap", "", (a, t) => scriptsByType("interact", a, t));
            Register("map", "script", (a, t) => scriptsByType("map", a, t));
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
