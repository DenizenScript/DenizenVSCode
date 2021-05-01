using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using JsonRpc.Contracts;
using LanguageServer.VsCode.Contracts;

namespace DenizenLangServer.Services
{
    [JsonRpcScope(MethodPrefix = "completionItem/")]
    public class CompletionItemService : DenizenLanguageServiceBase
    {
        // The request is sent from the client to the server to resolve additional information
        // for a given completion item.
        [JsonRpcMethod(AllowExtensionData = true)]
        public CompletionItem Resolve()
        {
            CompletionItem item = RequestContext.Request.Parameters.ToObject<CompletionItem>(Utilities.CamelCaseJsonSerializer);
            return item;
        }
    }
}
