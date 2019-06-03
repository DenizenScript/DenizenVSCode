using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using JsonRpc.Client;
using JsonRpc.Contracts;
using JsonRpc.Server;
using JsonRpc.Streams;
using LanguageServer.VsCode;
using LanguageServer.VsCode.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace DenizenLangServer
{
    public static class Utilities
    {
        public static readonly JsonSerializer CamelCaseJsonSerializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };
    }
}
