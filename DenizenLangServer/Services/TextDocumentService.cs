using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FreneticUtilities.FreneticExtensions;
using JsonRpc.Contracts;
using JsonRpc.Server;
using LanguageServer.VsCode;
using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Contracts.Client;
using LanguageServer.VsCode.Server;
using Newtonsoft.Json.Linq;
using SharpDenizenTools.MetaHandlers;
using SharpDenizenTools.MetaObjects;

namespace DenizenLangServer.Services
{
    [JsonRpcScope(MethodPrefix = "textDocument/")]
    public class TextDocumentService : DenizenLanguageServiceBase
    {
        [JsonRpcMethod]
        public Hover Hover(TextDocumentIdentifier textDocument, Position position, CancellationToken ct)
        {
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
        public async Task DidClose(TextDocumentIdentifier textDocument)
        {
            await Client.Document.PublishDiagnostics(textDocument.Uri, Array.Empty<Diagnostic>());
            Session.Documents.TryRemove(textDocument.Uri, out _);
        }

        private static readonly CompletionItem[] EmptyCompletionItems = Array.Empty<CompletionItem>();

        private static readonly JToken Token = JToken.FromObject("Data"); // TODO: ???

        [JsonRpcMethod]
        public CompletionList Completion(TextDocumentIdentifier textDocument, Position position, Dictionary<string, object> context)
        {
            try
            {
                return GetCompletionsFor(textDocument, position, context);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Internal completion handling error: {ex}");
                return new CompletionList(EmptyCompletionItems);
            }
        }

        public static Dictionary<string, Func<IEnumerable<string>>> LinePrefixCompleters = new Dictionary<string, Func<IEnumerable<string>>>()
        {
            { "material", () => ExtraData.Data.Items },
            { "entity_type", () => ExtraData.Data.Entities }
        };

        public CompletionList GetCompletionsFor(TextDocumentIdentifier textDocument, Position position, Dictionary<string, object> context)
        {
            TextDocument doc = GetDocument(textDocument);
            if (doc == null || !textDocument.Uri.AbsolutePath.EndsWith(".dsc"))
            {
                return new CompletionList(EmptyCompletionItems);
            }
            int offset = doc.OffsetAt(position);
            string content = doc.Content;
            if (offset == content.Length + 1)
            {
                offset -= 2;
            }
            if (offset < 0 || offset >= content.Length)
            {
                return new CompletionList(EmptyCompletionItems);
            }
            int startOfLine = content.LastIndexOf('\n', offset - 1) + 1;
            if (startOfLine == 0 || (offset - 1) < startOfLine)
            {
                return new CompletionList(EmptyCompletionItems);
            }
            string relevantLine = content[startOfLine..(offset - 1)];
            string trimmed = relevantLine.TrimStart();
            if (trimmed.StartsWith("- "))
            {
                string afterDash = trimmed[2..];
                if (!afterDash.Contains(" "))
                {
                    string possibleCmd = afterDash.ToLowerFast();
                    CompletionItem[] results = MetaDocs.CurrentMeta.Commands.Where(c => c.Key.StartsWith(possibleCmd))
                        .Select(c => new CompletionItem(c.Key, CompletionItemKind.Method, c.Value.Short, c.Value.Syntax + "\n\n" + c.Value.Description, Token)).ToArray();
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
                        if (CommandTabCompletions.TabCompletions.TryGetValue(cmd.CleanName, out CommandTabCompletions completer) && completer.ByPrefix.TryGetValue(prefix, out Func<IEnumerable<string>> completeFunc))
                        {
                            results = results.Concat(completeFunc().Where(text => text.StartsWith(argValue)).Select(text => new CompletionItem(text, CompletionItemKind.Enum, Token))).ToArray();
                        }
                        if (results.Length > 0)
                        {
                            return new CompletionList(results);
                        }
                    }
                }
            }
            if (trimmed.Contains(":") && !trimmed.Contains("<"))
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
            if (trimmed.StartsWith("-") || trimmed.Contains(":"))
            {
                if (trimmed.Contains("<"))
                {
                    int argStart = 0;
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
                        else if (trimmed[i] == ' ')
                        {
                            argStart = i + 1;
                        }
                    }
                    string arg = trimmed[argStart..];
                    if (arg.Contains("<"))
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
                            if (components == 0 && !fullTag.Contains('['))
                            {
                                CompletionItem[] results = MetaDocs.CurrentMeta.TagBases.Where(tag => tag.StartsWith(fullTag))
                                    .Select(tag => MetaDocs.CurrentMeta.Tags.TryGetValue(tag, out MetaTag tagDoc) ?
                                        new CompletionItem(tag, CompletionItemKind.Property, tagDoc.Name, tagDoc.Description, Token) :
                                        new CompletionItem(tag, CompletionItemKind.Property, Token)).ToArray();
                                return new CompletionList(results);
                            }
                            string subComponent = fullTag[lastDot..];
                            if (!subComponent.Contains('['))
                            {
                                CompletionItem[] results = MetaDocs.CurrentMeta.TagParts.Where(tag => tag.StartsWith(subComponent))
                                    .Select(tag => TryFindLikelyTagForPart(tag, out MetaTag tagDoc) ?
                                        new CompletionItem(tag, CompletionItemKind.Property, tagDoc.Name, tagDoc.Description, Token) :
                                        new CompletionItem(tag, CompletionItemKind.Property, Token)).ToArray();
                                return new CompletionList(results);
                            }
                        }
                    }
                }
            }
            // TODO: Actual completion logic for other cases (Type keys, tags, etc)
            return new CompletionList(EmptyCompletionItems);
        }

        /// <summary>
        /// Tries to find the tag for the given part.
        /// </summary>
        /// <param name="tagText">The tag part text tosearch for.</param>
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
