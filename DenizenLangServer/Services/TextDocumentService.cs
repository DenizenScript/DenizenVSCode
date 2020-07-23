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
            await Client.Document.PublishDiagnostics(textDocument.Uri, new Diagnostic[0]);
            Session.Documents.TryRemove(textDocument.Uri, out _);
        }

        private static readonly CompletionItem[] EmptyCompletionItems =
        {
        };

        [JsonRpcMethod]
        public CompletionList Completion(TextDocumentIdentifier textDocument, Position position, Dictionary<string, object> context)
        {
            TextDocument doc = GetDocument(textDocument);
            if (doc == null || !textDocument.Uri.AbsolutePath.EndsWith(".dsc"))
            {
                return new CompletionList(EmptyCompletionItems);
            }
            int offset = doc.OffsetAt(position);
            string content = doc.Content;
            if (offset < 0 || offset >= content.Length)
            {
                return new CompletionList(EmptyCompletionItems);
            }
            int startOfLine = content.LastIndexOf('\n', offset - 1) + 1;
            string relevantLine = content[startOfLine..(offset - 1)];
            string trimmed = relevantLine.TrimStart();
            if (trimmed.StartsWith("- "))
            {
                string afterDash = trimmed[2..];
                if (!afterDash.Contains(" "))
                {
                    string possibleCmd = afterDash.ToLowerFast();
                    CompletionItem[] results = MetaDocs.CurrentMeta.Commands.Where(c => c.Key.StartsWith(possibleCmd))
                        .Select(c => new CompletionItem(c.Key, CompletionItemKind.Method, c.Value.Short, c.Value.Syntax + "\n\n" + c.Value.Description, JToken.FromObject("Data"))).ToArray();
                    return new CompletionList(results);
                }
            }
            // TODO: Actual completion logic for other cases (Type keys, tags, etc)
            return new CompletionList(EmptyCompletionItems);
        }
    }
}
