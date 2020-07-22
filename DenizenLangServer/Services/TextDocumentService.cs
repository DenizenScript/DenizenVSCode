using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JsonRpc.Contracts;
using JsonRpc.Server;
using LanguageServer.VsCode;
using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Contracts.Client;
using LanguageServer.VsCode.Server;

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

        private static readonly CompletionItem[] PredefinedCompletionItems =
        {
        };

        [JsonRpcMethod]
        public CompletionList Completion(TextDocumentIdentifier textDocument, Position position, Dictionary<string, object> context)
        {
            // TODO: Actual completion logic
            return new CompletionList(PredefinedCompletionItems);
        }
    }
}
