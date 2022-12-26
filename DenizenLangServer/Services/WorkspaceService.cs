using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FreneticUtilities.FreneticExtensions;
using JsonRpc.Contracts;
using LanguageServer.VsCode.Contracts;
using Newtonsoft.Json.Linq;
using SharpDenizenTools.MetaHandlers;

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

            public string Extra_sources { get; set; }
        }

        public class DenizenBehaviorSettings
        {
            public bool Do_hover_docs { get; set; }

            public bool Do_tab_completes { get; set; }

            public bool Track_full_workspace { get; set; }
        }

        [JsonRpcMethod(IsNotification = true, AllowExtensionData = true)]
        public void DidChangeConfiguration(SettingsRoot settings)
        {
            try
            {
                ClientConfiguration.DoHoverDocs = settings.Denizenscript.Behaviors.Do_hover_docs;
                ClientConfiguration.DoTabCompletes = settings.Denizenscript.Behaviors.Do_tab_completes;
                ClientConfiguration.TrackFullWorkspace = settings.Denizenscript.Behaviors.Track_full_workspace;
                if (ClientConfiguration.ExtraSources != settings.Denizenscript.Extra_sources)
                {
                    Console.Error.WriteLine($"Alternate meta sources detected, scanning...");
                    ClientConfiguration.ExtraSources = settings.Denizenscript.Extra_sources ?? "";
                    IEnumerable<string> newSources = ClientConfiguration.ExtraSources.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    MetaDocsLoader.SourcesToUse = MetaDocsLoader.DENIZEN_SOURCES.JoinWith(MetaDocsLoader.DENIZEN_ADDON_SOURCES).JoinWith(newSources).Distinct().ToArray();
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            Console.Error.WriteLine($"Reloading meta docs...");
                            MetaDocs.CurrentMeta = MetaDocsLoader.DownloadAll();
                            Console.Error.WriteLine($"Meta reloaded due to custom sources listed");
                        }
                        catch (Exception ex)
                        {
                            Console.Error.Write($"Alternate meta source loading error: {ex}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to scan config change: {ex}");
            }
        }
    }
}
