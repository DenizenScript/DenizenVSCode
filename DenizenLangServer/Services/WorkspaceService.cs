using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using JsonRpc.Contracts;
using LanguageServer.VsCode.Contracts;
using Newtonsoft.Json.Linq;

namespace DenizenLangServer.Services
{
    [JsonRpcScope(MethodPrefix = "workspace/")]
    public class WorkspaceService : DenizenLanguageServiceBase
    {
        [JsonRpcMethod(IsNotification = true)]
        public void DidChangeWatchedFiles(ICollection<FileEvent> changes)
        {
        }
    }
}
