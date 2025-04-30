using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FreneticUtilities.FreneticExtensions;
using LanguageServer.VsCode.Contracts;
using Newtonsoft.Json.Linq;

namespace DenizenLangServer.Services
{
    /// <summary>Helper class to provide script container snippets.</summary>
    public static class SnippetHelper
    {
        /// <summary>Reference data for a snippet.</summary>
        public class Snippet
        {
            public string Name;

            public string Description;

            public string[] Prefixes;

            public string Body;

            public MarkupContent DescriptionMarkup;
        }

        /// <summary>All available snippets, as a map of first character to list.</summary>
        public static Dictionary<char, List<Snippet>> Snippets = [];

        public static void RegisterSnippet(string name, string description, string[] prefixes, string body)
        {
            foreach (char letter in prefixes.Select(s => s[0]).Distinct())
            {
                List<Snippet> list = Snippets.GetOrCreate(letter, () => []);
                list.Add(new Snippet() { Name = name, Description = description, Prefixes = prefixes, Body = body, DescriptionMarkup = MarkupContent.Markdown($"{description}\n```\n{body}\n```\n") });
            }
        }

        static SnippetHelper()
        {
            RegisterSnippet("Book Script Container", "A reference basic book script container.", ["book"], "${1:my_book}:\n\ttype: book\n\ttitle: ${2:Title}\n\tauthor: ${3:Author}\n\ttext:\n\t- ${4:Once upon a time...}");
            RegisterSnippet("Command Script Container", "A reference basic command script container.", ["command", "cmd"], "${1:my_command}:\n\ttype: command\n\tname: ${2:mycmd}\n\tdescription: ${3:Does something}\n\tusage: /mycmd <&lt>${4:arg}<&gt>\n\tpermission: dscript.${2:mycmd}\n\tscript:\n\t- ${5:narrate Hello!}");
            RegisterSnippet("Data Script Container", "A reference basic data script container.", ["data", "config"], "${1:my_data}:\n\ttype: data\n\t${2:key}: ${3:value}");
            RegisterSnippet("Economy Script Container", "A reference basic economy script container.", ["economy", "vault-economy", "currency", "money"], "${1:my_economy}:\n\ttype: economy\n\tpriority: ${2|lowest,low,normal,high,highest|}\n\tname single: ${3:Dollar}\n\tname plural: ${4:Dollars}\n\tdigits: ${5:2}\n\tformat: ${6:$}<[amount]>\n\tbalance: <player.flag[balance]>\n\thas: <player.flag[balance].is[or_more].than[<[amount[>]>\n\twithdraw:\n\t- flag <player> balance:-:<[amount]>\n\tdeposit:\n\t- flag <player> balance:+:<[amount]>");
            RegisterSnippet("Enchantment Script Container", "A reference basic enchantment script container.", ["enchantment"], "${1:my_enchantment}:\n\ttype: enchantment\n\tid: ${2:enchantment_id}\n\tslots:\n\t- ${3:mainhand}\n\trarity: ${4|common,uncommon,rare,very_rare|}\n\tcategory: ${5|weapon,armor,armor_feet,armor_legs,armor_chest,armor_head,digger,fishing_rod,trident,breakable,bow,wearable,crossbow,vanishable|}\n\tfull_name: ${6:Enchantment Name} <context.level>\n\tmax_level: ${7:3}\n\tmin_cost: <context.level>\n\tmax_cost: <context.level.mul[1.5].round>");
            RegisterSnippet("Entity Script Container", "A reference basic entity script container.", ["entity", "custom-entity"], "${1:my_entity}:\n\ttype: entity\n\tentity_type: ${2:type}\n\tmechanisms:\n\t\t${3:mech}: ${4:value}");
            RegisterSnippet("Format Script Container", "A reference basic format script container.", ["format", "chat-format", "text-format"], "${1:my_format}:\n\ttype: format\n\tformat: $0<[text]>");
            RegisterSnippet("Item Script Container", "A reference basic item script container.", ["item", "custom-item"], "${1:my_item}:\n\ttype: item\n\tmaterial: ${2:stick}\n\tdisplay name: ${3:Stick}");
            RegisterSnippet("Item Script With Shape Recipe", "An item script with an empty shaped recipe.", ["item-craft", "item-recipe", "craftable-item", "recipe-item"], "${1:my_item}:\n\ttype: item\n\tmaterial: ${2:stick}\n\tdisplay name: ${3:Stick}\n\trecipes:\n\t\t1:\n\t\t\ttype: shaped\n\t\t\tinput:\n\t\t\t- ${4:air}|${5:air}|${6:air}\n\t\t\t- ${7:air}|${8:air}|${9:air}\n\t\t\t- ${10:air}|${11:air}|${12:air}");
            RegisterSnippet("Inventory Script Container", "A reference basic inventory script container.", ["inventory", "chest"], "${1:my_inventory}:\n\ttype: inventory\n\tinventory: ${2|chest,brewing,dispenser,enchanting,ender_chest,hopper,workbench|}\n\tslots:\n\t- [] [] [] [] [] [] [] [] []\n\t- [] [] [] [] [] [] [] [] []\n\t- [] [] [] [] [] [] [] [] []");
            RegisterSnippet("Inventory Script GUI Menu", "An inventory script to be used as a GUI/Menu.", ["inventory-gui", "gui", "inventory-menu", "menu"], "${1:my_inventory}:\n\ttype: inventory\n\tinventory: ${2|chest,brewing,dispenser,enchanting,ender_chest,hopper,workbench|}\n\tgui: true\n\tdefinitions:\n\t  ${3:name}: ${4:item}\n\tprocedural items:\n\t- define result <list>\n\t# Add some logic!\n\t- determine <[result]>\n\tslots:\n\t- [] [] [] [] [] [] [] [] []\n\t- [] [] [] [] [] [] [] [] []\n\t- [] [] [] [] [] [] [] [] []");
            RegisterSnippet("Map Script Container", "A reference basic map script container with a sample image and cursor.", ["map"], "${1:my_map}:\n\ttype: map\n\tobjects:\n\t\t1:\n\t\t\ttype: image\n\t\t\timage: ${2:image.png}\n\t\t2:\n\t\t\ttype: cursor\n\t\t\tcursor: red_marker\n\t\t\tdirection: 180\n\t\t\tx: 10\n\t\t\ty: 10");
            RegisterSnippet("Procedure Script Container", "A reference basic procedure script container.", ["procedure"], "${1:my_proc}:\n\ttype: procedure\n\tscript:\n\t- ${2:determine true}");
            RegisterSnippet("Procedure Script With Definitions", "A reference procedure script container with definitions.", ["procedure-def", "definitions-task"], "${1:my_proc}:\n\ttype: procedure\n\tdefinitions: ${2:def}\n\tscript:\n\t- ${3:determine true}");
            RegisterSnippet("Task Script Container", "A reference basic task script container.", ["task", "script"], "${1:my_task}:\n\ttype: task\n\tscript:\n\t- ${2:narrate \"Hello, world!\"}");
            RegisterSnippet("Task Script With Definitions", "A reference task script container with definitions.", ["task-def", "definitions-task"], "${1:my_task}:\n\ttype: task\n\tdefinitions: ${2:def}\n\tscript:\n\t- ${3:narrate \"Hello, world!\"}");
            RegisterSnippet("World Script Container", "A reference basic world script container.", ["world", "events", "listener"], "${1:my_world}:\n\ttype: world\n\tevents:\n\t\tafter ${2:player breaks block}:\n\t\t- ${3:narrate \"Nice <context.material>\"}");
        }

        public static CompletionItem[] GetSnippetsFor(string prefix, JToken token)
        {
            if (string.IsNullOrWhiteSpace(prefix) || !Snippets.TryGetValue(prefix[0], out List<Snippet> list))
            {
                return null;
            }
            List<CompletionItem> results = [];
            prefix = prefix.ToLowerFast();
            foreach (Snippet snippet in list)
            {
                string prefixMatch = snippet.Prefixes.FirstOrDefault(s => s.StartsWith(prefix));
                if (prefixMatch is not null)
                {
                    results.Add(new CompletionItem(prefixMatch, CompletionItemKind.Snippet, snippet.Name, snippet.DescriptionMarkup, token)
                    {
                        InsertText = snippet.Body,
                        InsertTextFormat = InsertTextFormat.Snippet
                    });
                }
            }
            return [.. results];
        }
    }
}
