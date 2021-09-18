using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Contracts.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DenizenLangServer
{
    public static class ClientConfiguration
    {
        public static bool DoHoverDocs = true;

        public static bool DoTabCompletes = true;
    }
}
