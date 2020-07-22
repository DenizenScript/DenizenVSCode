using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Net.Http;
using FreneticUtilities.FreneticToolkit;
using JsonRpc.Client;
using JsonRpc.Contracts;
using JsonRpc.Server;
using JsonRpc.Streams;
using LanguageServer.VsCode;
using SharpDenizenTools.MetaHandlers;
using SharpDenizenTools.ScriptAnalysis;
using System.Threading.Tasks;
using DenizenLangServer.Services;
using LanguageServer.VsCode.Contracts.Client;
using System.Collections.Generic;

namespace DenizenLangServer
{
    class Program
    {
        static void Main(string[] args)
        {
            InitMetaHelper();
            InitExtensionServer();
        }

        public static AsciiMatcher URL_ACCEPTED = new AsciiMatcher(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'));

        static void InitMetaHelper()
        {
            ScriptChecker.LogInternalMessage = Console.Error.WriteLine;
            MetaDocs.CurrentMeta = new MetaDocs();
            string cachePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            bool needsRedownload = false;
            bool shouldIgnoreCache = false;
            if (Directory.Exists(cachePath)) // Safety with weird OS's.
            {
                string cacheFolder = cachePath + "/DenizenVSCodeExtension/cache/";
                Directory.CreateDirectory(cacheFolder);
                MetaDocs.AlternateZipSourcer = url =>
                {
                    char[] urlChars = url.ToCharArray();
                    for (int i = 0; i < urlChars.Length; i++)
                    {
                        if (!URL_ACCEPTED.IsMatch(urlChars[i]))
                        {
                            urlChars[i] = '_';
                        }
                    }
                    string cachableName = new string(urlChars);
                    string cacheFileName = cacheFolder + cachableName + ".zip";
                    if (!shouldIgnoreCache)
                    {
                        if (File.Exists(cacheFileName))
                        {
                            if (Math.Abs(DateTime.UtcNow.Subtract(File.GetLastWriteTimeUtc(cacheFileName)).TotalHours) > 12)
                            {
                                needsRedownload = true;
                            }
                            return File.ReadAllBytes(cacheFileName);
                        }
                    }
                    HttpClient client = new HttpClient
                    {
                        Timeout = new TimeSpan(0, 2, 0)
                    };
                    byte[] output = client.GetByteArrayAsync(url).Result;
                    File.WriteAllBytes(cacheFileName, output);
                    return output;
                };
            }
            Console.Error.WriteLine();
            MetaDocs.CurrentMeta.DownloadAll();
            if (needsRedownload)
            {
                shouldIgnoreCache = true;
                Task.Factory.StartNew(() =>
                {
                    MetaDocs newDocs = new MetaDocs();
                    newDocs.DownloadAll();
                    MetaDocs.CurrentMeta = newDocs;
                });
            }
        }

        static void InitExtensionServer()
        {
            Console.Error.WriteLine("Extension starting...");
            using Stream cin = Console.OpenStandardInput();
            using BufferedStream bcin = new BufferedStream(cin);
            using Stream cout = Console.OpenStandardOutput();
            using PartwiseStreamMessageReader reader = new PartwiseStreamMessageReader(bcin);
            using PartwiseStreamMessageWriter writer = new PartwiseStreamMessageWriter(cout);
            JsonRpcContractResolver contractResolver = new JsonRpcContractResolver
            {
                NamingStrategy = new CamelCaseJsonRpcNamingStrategy(),
                ParameterValueConverter = new CamelCaseJsonValueConverter(),
            };
            StreamRpcClientHandler clientHandler = new StreamRpcClientHandler();
            JsonRpcClient client = new JsonRpcClient(clientHandler);
            LanguageServerSession session = new LanguageServerSession(client, contractResolver);
            JsonRpcServiceHostBuilder builder = new JsonRpcServiceHostBuilder { ContractResolver = contractResolver };
            builder.UseCancellationHandling();
            builder.Register(typeof(Program).GetTypeInfo().Assembly);
            IJsonRpcServiceHost host = builder.Build();
            StreamRpcServerHandler serverHandler = new StreamRpcServerHandler(host,
                StreamRpcServerHandlerOptions.ConsistentResponseSequence | StreamRpcServerHandlerOptions.SupportsRequestCancellation);
            serverHandler.DefaultFeatures.Set(session);
            using (serverHandler.Attach(reader, writer))
            using (clientHandler.Attach(reader, writer))
            {
                // Wait for the "stop" request.
                session.CancellationToken.WaitHandle.WaitOne();
            }
        }
    }
}
