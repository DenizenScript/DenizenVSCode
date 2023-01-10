using System;
using System.Threading.Tasks;
using JsonRpc;
using JsonRpc.Contracts;
using JsonRpc.Server;
using JsonRpc.Messages;
using LanguageServer.VsCode.Contracts;
using Newtonsoft.Json.Linq;
using FreneticUtilities.FreneticToolkit;
using FreneticUtilities.FreneticExtensions;
using System.IO;

namespace DenizenLangServer.Services
{
    public class InitializationService : DenizenLanguageServiceBase
    {

        [JsonRpcMethod(AllowExtensionData = true)]
        public InitializeResult Initialize(int processId, Uri rootUri, ClientCapabilities capabilities, JToken initializationOptions = null, string trace = null)
        {
            SpecialTools.Internationalize();
            WorkspaceTracker.WorkspacePath = WorkspaceTracker.FixPath(rootUri);
            return new InitializeResult(new ServerCapabilities
            {
                HoverProvider = new HoverOptions() { WorkDoneProgress = false },
                SignatureHelpProvider = new SignatureHelpOptions(""),
                CompletionProvider = new CompletionOptions(true, " .=<[;"),
                TextDocumentSync = new TextDocumentSyncOptions
                {
                    OpenClose = true,
                    WillSave = true,
                    Change = TextDocumentSyncKind.Incremental
                }
            });
        }

        [JsonRpcMethod(IsNotification = true)]
        public void Initialized()
        {
        }

        [JsonRpcMethod]
        public void Shutdown()
        {
        }

        [JsonRpcMethod(IsNotification = true)]
        public void Exit()
        {
            Session.StopServer();
        }

        [JsonRpcMethod("$/cancelRequest", IsNotification = true)]
        public void CancelRequest(MessageId id)
        {
            RequestContext.Features.Get<IRequestCancellationFeature>().TryCancel(id);
        }
    }
}
