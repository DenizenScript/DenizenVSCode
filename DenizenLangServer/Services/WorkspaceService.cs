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
        public class SettingsRoot
        {
            public DenizenSettings Denizenscript { get; set; }
        }

        public class DenizenSettings
        {
            public DenizenBehaviorSettings Behaviors { get; } = new DenizenBehaviorSettings();
        }

        public class DenizenBehaviorSettings
        {
            public bool Do_hover_docs { get; set; }

            public bool Do_tab_completes { get; set; }
        }

        [JsonRpcMethod(IsNotification = true, AllowExtensionData = true)]
        public void DidChangeConfiguration(SettingsRoot settings)
        {
            ClientConfiguration.DoHoverDocs = settings.Denizenscript.Behaviors.Do_hover_docs;
            ClientConfiguration.DoTabCompletes = settings.Denizenscript.Behaviors.Do_tab_completes;
        }
    }
}
