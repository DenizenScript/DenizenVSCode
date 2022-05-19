using SharpDenizenTools.MetaHandlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DenizenLangServer
{
    public class CommandTabCompletions
    {
        public static Dictionary<string, CommandTabCompletions> TabCompletions = new();

        public Dictionary<string, Func<IEnumerable<string>>> ByPrefix = new();

        public static void Register(string command, string prefix, Func<IEnumerable<string>> options)
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
        }
    }
}
