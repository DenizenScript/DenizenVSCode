using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using JsonRpc.DynamicProxy.Client;
using JsonRpc.Client;
using JsonRpc.Contracts;
using JsonRpc.Server;
using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Contracts.Client;
using LanguageServer.VsCode.Server;
using FreneticUtilities.FreneticToolkit;

namespace DenizenLangServer
{
    public class LanguageServerSession
    {
        private readonly CancellationTokenSource cts = new();

        public LanguageServerSession(JsonRpcClient rpcClient, IJsonRpcContractResolver contractResolver)
        {
            RpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));
            var builder = new JsonRpcProxyBuilder {ContractResolver = contractResolver};
            Client = new ClientProxy(builder, rpcClient);
            Documents = new ConcurrentDictionary<Uri, SessionDocument>();
            DiagnosticProvider = new DiagnosticProvider
            {
                CancelToken = CancellationToken
            };
            Console.Error.WriteLine("Linter thread starting...");
            Task.Factory.StartNew(DiagnosticProvider.LintCheckLoopThread);
        }

        public CancellationToken CancellationToken => cts.Token;

        public JsonRpcClient RpcClient { get; set; }

        public ClientProxy Client { get; set; }

        public ConcurrentDictionary<Uri, SessionDocument> Documents { get; set; }

        public DiagnosticProvider DiagnosticProvider { get; set; }

        public void StopServer()
        {
            cts.Cancel();
        }

    }

    public class SessionDocument
    {
        public SessionDocument(TextDocumentItem doc)
        {
            Document = TextDocument.Load<FullTextDocument>(doc);
        }

        private readonly LockObject SyncLock = new();

        public event EventHandler DocumentChanged;

        public TextDocument Document { get; set; }

        public void NotifyChanges(IEnumerable<TextDocumentContentChangeEvent> changes)
        {
            lock (SyncLock)
            {
                Document = Document.ApplyChanges(changes.ToList());
            }
            OnDocumentChanged();
        }

        protected virtual void OnDocumentChanged()
        {
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
