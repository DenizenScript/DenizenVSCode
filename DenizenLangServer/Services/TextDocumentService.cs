using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using JsonRpc.Contracts;
using JsonRpc.Server;
using LanguageServer.VsCode;
using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Contracts.Client;
using LanguageServer.VsCode.Server;
using Newtonsoft.Json.Linq;
using SharpDenizenTools.MetaHandlers;
using SharpDenizenTools.MetaObjects;
using SharpDenizenTools.ScriptAnalysis;
using YamlDotNet.Core.Tokens;

namespace DenizenLangServer.Services
{
    [JsonRpcScope(MethodPrefix = "textDocument/")]
    public class TextDocumentService : DenizenLanguageServiceBase
    {
        [JsonRpcMethod]
        public Hover Hover(TextDocumentIdentifier textDocument, Position position, CancellationToken ct)
        {
            if (!ClientConfiguration.DoHoverDocs)
            {
                return null;
            }
            // TODO: All this code is a dirty "it works" vertical slice mess that needs to be cleaned up
            TextDocument doc = GetDocument(textDocument);
            if (doc == null || !textDocument.Uri.AbsolutePath.EndsWith(".dsc"))
            {
                return null;
            }
            int offset = doc.OffsetAt(position);
            string content = doc.Content;
            if (offset == content.Length + 1)
            {
                offset -= 2;
            }
            if (offset <= 0 || offset >= content.Length)
            {
                return null;
            }
            int startOfLine = content.LastIndexOf('\n', offset - 1) + 1;
            if (startOfLine == 0 || (offset - 1) < startOfLine)
            {
                return null;
            }
            int endOfLine = content.IndexOf('\n', offset);
            if (endOfLine == -1)
            {
                endOfLine = content.Length;
            }
            else
            {
                endOfLine--;
            }
            string relevantLine = content[startOfLine..endOfLine];
            if (position.Character < 0 || position.Character >= relevantLine.Length)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(relevantLine) || relevantLine[position.Character] == ' ')
            {
                return null;
            }
            try
            {
                return GetHoverAt(doc, offset, relevantLine, position);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Exception processing hover input for '{relevantLine}' at {position.Character}: {ex}");
                return null;
            }
        }

        public static AsciiMatcher SafeAdvanceForward = new(AsciiMatcher.BothCaseLetters + AsciiMatcher.Digits + "_-");

        public Hover GetHoverAt(TextDocument doc, int offset, string relevantLine, Position position)
        {
            LanguageServer.VsCode.Contracts.Range range(int start, int end)
            {
                return new LanguageServer.VsCode.Contracts.Range(position.Line, start, position.Line, end);
            }
            string trimmed = relevantLine.TrimStart();
            int spaces = relevantLine.Length - trimmed.Length;
            string subPiece = relevantLine[position.Character..];
            int canGoForwardBy = SafeAdvanceForward.FirstNonMatchingIndex(subPiece);
            if (canGoForwardBy == -1)
            {
                canGoForwardBy = subPiece.Length;
            }
            CompletionList completions = GetCompletionsFor(doc, new Position(position.Line, position.Character + canGoForwardBy), null);
            if (completions is not null && completions.Items.Any())
            {
                foreach (CompletionItem possible in completions.Items)
                {
                    if (possible.Documentation is null)
                    {
                        continue;
                    }
                    for (int i = position.Character - 1; i >= 0; i--)
                    {
                        if (relevantLine[i..].StartsWith(possible.Label))
                        {
                            return new Hover(possible.Documentation, range(i, i + possible.Label.Length));
                        }
                    }
                }
            }
            if (trimmed.EndsWith(":") && (trimmed.StartsWith("on ") || trimmed.StartsWith("after ")))
            {
                bool isAction = false;
                if (trimmed.StartsWith("on "))
                {
                    int assignIndex = doc.Content.LastIndexOf("type: assignment", offset);
                    if (assignIndex != -1)
                    {
                        int worldIndex = doc.Content.LastIndexOf("type: world", offset);
                        isAction = assignIndex > worldIndex;
                    }
                }
                if (isAction)
                {
                    string actionName = trimmed.BeforeLast(":");
                    if (!MetaDocs.CurrentMeta.Actions.TryGetValue(actionName, out MetaAction action))
                    {
                        foreach (MetaAction possibleAction in MetaDocs.CurrentMeta.Actions.Values)
                        {
                            if (possibleAction.RegexMatcher.IsMatch(actionName))
                            {
                                action = possibleAction;
                                break;
                            }
                        }
                    }
                    if (action != null)
                    {
                        return new Hover(CommandTabCompletions.DescribeAction(action), range(spaces, spaces + actionName.Length + 1));
                    }
                }
                else // is World
                {
                    string eventName = trimmed.BeforeLast(":");
                    if (eventName.StartsWith("after "))
                    {
                        eventName = eventName["after ".Length..];
                    }
                    else if (eventName.StartsWith("on "))
                    {
                        eventName = eventName["on ".Length..];
                    }
                    eventName = EventTools.SeparateSwitches(MetaDocs.CurrentMeta, eventName, out List<KeyValuePair<string, string>> switches);
                    if (!MetaDocs.CurrentMeta.Events.TryGetValue(eventName, out MetaEvent realEvt))
                    {
                        string[] parts = eventName.Split(' ');
                        int matchQuality = 0;
                        foreach (MetaEvent evt in MetaDocs.CurrentMeta.Events.Values)
                        {
                            int potentialMatch = evt.CouldMatchers.Select(c => c.TryMatch(parts, true, false)).Max();
                            if (potentialMatch > 0)
                            {
                                potentialMatch = potentialMatch == 10 ? 2 : 1;
                                if (evt.CouldMatchers.Any(c => c.TryMatch(parts, false, true) > 0))
                                {
                                    potentialMatch++;
                                }
                                if (evt.MultiNames.Any(name => ScriptChecker.AlphabetMatcher.TrimToMatches(name).Contains(eventName)))
                                {
                                    potentialMatch++;
                                }
                                if (switches.All(s => evt.IsValidSwitch(s.Key)))
                                {
                                    potentialMatch++;
                                }
                            }
                            if (potentialMatch > matchQuality)
                            {
                                matchQuality = potentialMatch;
                                realEvt = evt;
                                if (matchQuality == 5)
                                {
                                    break;
                                }
                            }
                        }
                    }
                    if (realEvt != null)
                    {
                        return new Hover(CommandTabCompletions.DescribeEvent(realEvt), range(spaces, spaces + trimmed.Length));
                    }
                }
            }
            if (trimmed.StartsWith("type: "))
            {
                string containerType = trimmed["type: ".Length..].ToLowerFast() + " script containers";
                if (MetaDocs.CurrentMeta.Languages.TryGetValue(containerType, out MetaLanguage lang))
                {
                    return new Hover(CommandTabCompletions.DescribeLang(lang), range(spaces, spaces + trimmed.Length));
                }
            }
            return null;
        }

        [JsonRpcMethod]
        public SignatureHelp SignatureHelp(TextDocumentIdentifier textDocument, Position position)
        {
            return new SignatureHelp(new List<SignatureInformation>
            {
            });
        }

        [JsonRpcMethod(IsNotification = true)]
        public void DidOpen(TextDocumentItem textDocument)
        {
            HandleOpen(Session, new SessionDocument(textDocument));
        }

        public static void HandleOpen(LanguageServerSession session, SessionDocument doc)
        {
            doc.DocumentChanged += (sender, args) =>
            {
                try
                {
                    session.DiagnosticProvider.DiagSession = session;
                    session.DiagnosticProvider.InformNeedsUpdate(((SessionDocument)sender).Document);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }
            };
            session.Documents.TryAdd(doc.Document.Uri, doc);
            session.DiagnosticProvider.DiagSession = session;
            session.DiagnosticProvider.InformNeedsUpdate(doc.Document);
        }

        [JsonRpcMethod(IsNotification = true)]
        public void DidChange(TextDocumentIdentifier textDocument, ICollection<TextDocumentContentChangeEvent> contentChanges)
        {
            Session.Documents[textDocument.Uri].NotifyChanges(contentChanges);
        }

        [JsonRpcMethod(IsNotification = true)]
        public void WillSave(TextDocumentIdentifier textDocument, TextDocumentSaveReason reason)
        {
        }

        [JsonRpcMethod(IsNotification = true)]
        public void DidClose(TextDocumentIdentifier textDocument)
        {
            Session.Documents.TryRemove(textDocument.Uri, out _);
        }

        private static readonly CompletionItem[] EmptyCompletionItems = Array.Empty<CompletionItem>();

        private static readonly JToken Token = JToken.FromObject("Data"); // TODO: ???

        [JsonRpcMethod]
        public CompletionList Completion(TextDocumentIdentifier textDocument, Position position, Dictionary<string, object> context)
        {
            try
            {
                if (!ClientConfiguration.DoTabCompletes)
                {
                    return null;
                }
                TextDocument doc = GetDocument(textDocument);
                if (doc == null || !textDocument.Uri.AbsolutePath.EndsWith(".dsc"))
                {
                    return new CompletionList(EmptyCompletionItems);
                }
                return GetCompletionsFor(doc, position, context);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Internal completion handling error: {ex}");
                return new CompletionList(EmptyCompletionItems);
            }
        }

        public static Dictionary<string, Func<IEnumerable<string>>> LinePrefixCompleters = new()
        {
            { "material", () => ExtraData.Data.Items },
            { "entity_type", () => ExtraData.Data.Entities }
        };

        public CompletionList GetCompletionsFor(TextDocument doc, Position position, Dictionary<string, object> context)
        {
            int offset = doc.OffsetAt(position);
            string content = doc.Content;
            if (offset == content.Length + 1)
            {
                offset -= 2;
            }
            if (offset < 0 || offset > content.Length)
            {
                return new CompletionList(EmptyCompletionItems);
            }
            int startOfLine = content.LastIndexOf('\n', offset - 1) + 1;
            if ((offset - 1) < startOfLine)
            {
                return new CompletionList(EmptyCompletionItems);
            }
            string relevantLine = content[startOfLine..offset];
            if (relevantLine.EndsWithFast('\n') || relevantLine.EndsWithFast('\r'))
            {
                relevantLine = relevantLine[..^1];
            }
            string trimmed = relevantLine.TrimStart();
            if (!relevantLine.Contains(' '))
            {
                CompletionItem[] results = SnippetHelper.GetSnippetsFor(trimmed.TrimEnd(), Token);
                if (results is not null && results.Any())
                {
                    return new CompletionList(results);
                }
            }
            if (trimmed.StartsWith("- "))
            {
                string afterDash = trimmed[2..];
                if (afterDash.StartsWith("~"))
                {
                    afterDash = afterDash[1..];
                }
                if (!afterDash.Contains(' '))
                {
                    string possibleCmd = afterDash.ToLowerFast();
                    CompletionItem[] results = MetaDocs.CurrentMeta.Commands.Where(c => c.Key.StartsWith(possibleCmd)).Select(c => new CompletionItem(c.Key, CompletionItemKind.Method, c.Value.Short, CommandTabCompletions.DescribeCommand(c.Value), Token)).ToArray();
                    return new CompletionList(results);
                }
                else
                {
                    if (MetaDocs.CurrentMeta.Commands.TryGetValue(afterDash.Before(' ').ToLowerFast(), out MetaCommand cmd))
                    {
                        string argThusFar = afterDash.AfterLast(' ').ToLowerFast();
                        int colon = argThusFar.IndexOf(':');
                        string prefix = colon == -1 ? "" : argThusFar[..colon];
                        string argValue = colon == -1 ? argThusFar : argThusFar[(colon + 1)..];
                        CompletionItem[] results = cmd.FlatArguments.Where(arg => arg.Item1.StartsWith(argThusFar)).JoinWith(
                            cmd.ArgPrefixes.Where(arg => arg.Item1.StartsWith(argThusFar)).Select(a => new Tuple<string, string>(a.Item1 + ":", a.Item2)))
                            .Select(a => new CompletionItem(a.Item1, CompletionItemKind.Field, a.Item2, Token)).ToArray();
                        if (CommandTabCompletions.ByCommand.TryGetValue(cmd.CleanName, out CommandTabCompletions completer) && completer.ByPrefix.TryGetValue(prefix, out Func<string, JToken, IEnumerable<CompletionItem>> completeFunc))
                        {
                            results = results.Concat(completeFunc(argValue, Token)).ToArray();
                        }
                        if (cmd.CleanName == "adjust" || cmd.CleanName == "inventory" || cmd.CleanName == "adjustblock")
                        {
                            IEnumerable<MetaMechanism> mechs = MetaDocs.CurrentMeta.Mechanisms.Values;
                            if (cmd.CleanName == "inventory")
                            {
                                if (!afterDash.ToLowerFast().Contains("adjust"))
                                {
                                    mechs = Array.Empty<MetaMechanism>();
                                }
                                else
                                {
                                    mechs = mechs.Where(mech => mech.MechObject == "ItemTag");
                                }
                            }
                            else if (cmd.CleanName == "adjustblock")
                            {
                                mechs = mechs.Where(mech => mech.MechObject == "MaterialTag");
                            }
                            results = results.Concat(mechs.Where(mech => mech.MechName.StartsWith(argValue)).Select(mech => new CompletionItem(mech.MechName, CompletionItemKind.Property, mech.FullName, CommandTabCompletions.DescribeMech(mech), Token))).ToArray();
                        }
                        if (results.Length > 0)
                        {
                            return new CompletionList(results);
                        }
                    }
                }
            }
            if (trimmed.Contains(':') && !trimmed.Contains('<'))
            {
                string prefix = trimmed.BeforeAndAfter(':', out string val);
                if (LinePrefixCompleters.TryGetValue(prefix.ToLowerFast(), out Func<IEnumerable<string>> completer))
                {
                    val = val.Trim();
                    CompletionItem[] results = completer().Where(text => text.StartsWith(val))
                        .Select(text => new CompletionItem(text, CompletionItemKind.Enum, Token)).ToArray();
                    if (results.Length > 0)
                    {
                        return new CompletionList(results);
                    }
                }
            }
            if (trimmed.StartsWithFast('-') || trimmed.Contains(':'))
            {
                if (trimmed.Contains('<'))
                {
                    int argStart = 0;
                    int argInTag = 0;
                    for (int i = 0; i < trimmed.Length; i++)
                    {
                        if (trimmed[i] == '"' || trimmed[i] == '\'')
                        {
                            char quote = trimmed[i++];
                            while (i < trimmed.Length && trimmed[i] != quote)
                            {
                                i++;
                            }
                        }
                        else if (trimmed[i] == '<' && i + 1 < trimmed.Length && ScriptChecker.VALID_TAG_FIRST_CHAR.IsMatch(trimmed[i + 1]))
                        {
                            argInTag++;
                        }
                        else if (trimmed[i] == '>')
                        {
                            argInTag--;
                        }
                        else if (trimmed[i] == ' ' && argInTag == 0)
                        {
                            argStart = i + 1;
                        }
                    }
                    string arg = trimmed[argStart..];
                    if (arg.Contains('<'))
                    {
                        int tagBits = 0;
                        int relevantTagStart = -1;
                        for (int i = arg.Length - 1; i >= 0; i--)
                        {
                            if (arg[i] == '>')
                            {
                                tagBits++;
                            }
                            else if (arg[i] == '<')
                            {
                                if (tagBits == 0)
                                {
                                    relevantTagStart = i + 1;
                                    break;
                                }
                                tagBits--;
                            }
                        }
                        if (relevantTagStart != -1)
                        {
                            string fullTag = arg[relevantTagStart..].ToLowerFast();
                            int components = 0;
                            int subTags = 0;
                            int squareBrackets = 0;
                            int lastDot = 0;
                            for (int i = 0; i < fullTag.Length; i++)
                            {
                                if (fullTag[i] == '<')
                                {
                                    subTags++;
                                }
                                else if (fullTag[i] == '>')
                                {
                                    subTags--;
                                }
                                else if (fullTag[i] == '[' && subTags == 0)
                                {
                                    squareBrackets++;
                                }
                                else if (fullTag[i] == ']' && subTags == 0)
                                {
                                    squareBrackets--;
                                }
                                else if (fullTag[i] == '.' && subTags == 0 && squareBrackets == 0)
                                {
                                    components++;
                                    lastDot = i + 1;
                                }
                            }
                            if (components == 0)
                            {
                                if (!fullTag.Contains('['))
                                {
                                    CompletionItem[] results = MetaDocs.CurrentMeta.TagBases.Where(tag => tag.StartsWith(fullTag))
                                        .Select(tag => MetaDocs.CurrentMeta.Tags.TryGetValue(tag, out MetaTag tagDoc) ?
                                            new CompletionItem(tag, CompletionItemKind.Property, tagDoc.Name, CommandTabCompletions.DescribeTag(tagDoc), Token) :
                                            new CompletionItem(tag, CompletionItemKind.Property, Token)).ToArray();
                                    return new CompletionList(results);
                                }
                                if (fullTag.Contains(']'))
                                {
                                    return new CompletionList(Array.Empty<CompletionItem>());
                                }
                                string baseTag = fullTag.Before('[');
                                if (MetaDocs.CurrentMeta.Tags.TryGetValue(baseTag, out MetaTag actualBase) && actualBase.AllowsParam)
                                {
                                    return new CompletionList(CommandTabCompletions.CompleteGenericTagParam(actualBase.ParsedFormat.Parts[0].Parameter, "", fullTag.AfterLast('['), actualBase, Token));
                                }
                                return new CompletionList(Array.Empty<CompletionItem>());
                            }
                            SingleTag tag = TagHelper.Parse(fullTag[..(lastDot - 1)], (_) => { /* ignore */ });
                            TagTracer tracer = new() { Tag = tag, Docs = MetaDocs.CurrentMeta };
                            tracer.Trace();
                            SingleTag.Part lastPart = tag.Parts[^1];
                            string subComponent = fullTag[lastDot..];
                            if (!subComponent.Contains('['))
                            {
                                if (lastPart.PossibleSubTypes.Any())
                                {
                                    return new CompletionList(MetaDocs.CurrentMeta.Tags.Values.Where(tag => lastPart.PossibleSubTypes.Contains(tag.BaseType) || lastPart.Text == tag.BeforeDot).Where(tag => tag.AfterDotCleaned.StartsWith(subComponent))
                                       .Select(tag => new CompletionItem(tag.AfterDotCleaned, CompletionItemKind.Property, tag.Name, CommandTabCompletions.DescribeTag(tag), Token)).ToArray());
                                }
                                CompletionItem[] results = MetaDocs.CurrentMeta.TagParts.Where(tag => tag.StartsWith(subComponent))
                                    .Select(tag => TryFindLikelyTagForPart(tag, out MetaTag tagDoc) ?
                                        new CompletionItem(tag, CompletionItemKind.Property, tagDoc.Name, CommandTabCompletions.DescribeTag(tagDoc), Token) :
                                        new CompletionItem(tag, CompletionItemKind.Property, Token)).ToArray();
                                return new CompletionList(results);
                            }
                            else if (!subComponent.Contains(']'))
                            {
                                SingleTag parsedFullTag = TagHelper.Parse(fullTag.BeforeLast('[') + "[]", (_) => { /* ignore */ });
                                TagTracer fullTracer = new() { Tag = parsedFullTag, Docs = MetaDocs.CurrentMeta };
                                fullTracer.Trace();
                                SingleTag.Part currentPart = parsedFullTag.Parts[^1];
                                MetaTag actualTag = currentPart.PossibleTags.FirstOrDefault(t => t.AllowsParam);
                                if (actualTag is not null && actualTag.ParsedFormat.Parts[^1].Parameter is not null)
                                {
                                    return new CompletionList(CommandTabCompletions.CompleteGenericTagParam(actualTag.ParsedFormat.Parts[^1].Parameter, "", fullTag.AfterLast('['), actualTag, Token));
                                }
                            }
                        }
                    }
                }
            }
            if (trimmed.StartsWith("on ") || trimmed.StartsWith("after "))
            {
                bool isAction = false;
                if (trimmed.StartsWith("on "))
                {
                    int assignIndex = doc.Content.LastIndexOf("type: assignment", offset);
                    if (assignIndex != -1)
                    {
                        int worldIndex = doc.Content.LastIndexOf("type: world", offset);
                        isAction = assignIndex > worldIndex;
                    }
                }
                if (isAction)
                {
                    string actionName = trimmed;
                    // TODO
                }
                else // is World
                {
                    string eventName = trimmed;
                    if (eventName.StartsWith("after "))
                    {
                        eventName = eventName["after ".Length..];
                    }
                    else if (eventName.StartsWith("on "))
                    {
                        eventName = eventName["on ".Length..];
                    }
                    eventName = EventTools.SeparateSwitches(MetaDocs.CurrentMeta, eventName, out List<KeyValuePair<string, string>> switches);
                    string[] parts = eventName.Split(' ');
                    List<CompletionItem> completions = new();
                    foreach (MetaEvent evt in MetaDocs.CurrentMeta.Events.Values)
                    {
                        foreach (ScriptEventCouldMatcher matcher in evt.CouldMatchers.Where(c => c.TryMatch(parts, true, false) > 0))
                        {
                            string switchText = evt.Switches.IsEmpty() ? "" : $"\n\nSwitches:\n{string.Join('\n', evt.Switches)}";
                            string warnText = evt.Warnings.IsEmpty() ? "" : $"\nWARNING: {string.Join('\n', evt.Warnings)}\n\n";
                            completions.Add(new CompletionItem(matcher.Format.Replace("'", ""), CompletionItemKind.Snippet, trimmed, $"{warnText}Triggers: {evt.Triggers}{switchText}", Token)
                            {
                                InsertText = EventHelper.Snippetify(matcher.Format, string.IsNullOrWhiteSpace(eventName) ? 0 : parts.Length),
                                InsertTextFormat = InsertTextFormat.Snippet
                            });
                        }
                    }
                    return new CompletionList(completions);
                }
            }
            // TODO: Actual completion logic for other cases (Type keys, tags, etc)
            return new CompletionList(EmptyCompletionItems);
        }

        /// <summary>Tries to find the tag for the given part.</summary>
        /// <param name="tagText">The tag part text to search for.</param>
        /// <param name="tagOut">The tag object, if the return is true. Otherwise null.</param>
        /// <returns>True if the tag is found, otherwise false.</returns>
        public static bool TryFindLikelyTagForPart(string tagText, out MetaTag tagOut)
        {
            string dottedText = "." + tagText;
            KeyValuePair<string, MetaTag> res = MetaDocs.CurrentMeta.Tags.FirstOrDefault(t => t.Key.EndsWith(dottedText));
            tagOut = res.Value;
            return tagOut != null;
        }
    }
}
