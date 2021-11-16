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

namespace DenizenLangServer
{
    class Program
    {
        static void Main(string[] args)
        {
            GrossWorkaround();
            InitMetaHelper();
            InitExtensionServer();
        }

        /// <summary>
        /// The language server will fail to run if there has never been a usage of an HttpClient instance to download something.
        /// How, why, what - I don't know.
        /// But always intentionally hitting nothing suffices to guarantee the language server receives JsonRpc data.
        /// </summary>
        static void GrossWorkaround()
        {
            try
            {
                using HttpClient webClient = new()
                {
                    Timeout = new TimeSpan(0, 0, 1)
                };
                // Random weird download just to wake the internals or something idk.
                webClient.GetStringAsync("https://www.cloudflare.com/").Wait();
            }
            catch (Exception)
            {
            }
        }

        public static AsciiMatcher URL_ACCEPTED = new(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'));

        static void InitMetaHelper()
        {
            Console.Error.WriteLine("Loading meta-documentation...");
            ScriptChecker.LogInternalMessage = Console.Error.WriteLine;
            MetaDocs.CurrentMeta = new MetaDocs();
            string cachePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            bool needsRedownload = false;
            bool shouldIgnoreCache = false;
            string extraDataCache = null;
            if (Directory.Exists(cachePath)) // Safety with weird OS's.
            {
                string cacheFolder = cachePath + "/DenizenVSCodeExtension/cache/";
                Directory.CreateDirectory(cacheFolder);
                extraDataCache = cacheFolder + "extradata_minecraft.fds";
                MetaDocsLoader.AlternateZipSourcer = (url, client) =>
                {
                    char[] urlChars = url.ToCharArray();
                    for (int i = 0; i < urlChars.Length; i++)
                    {
                        if (!URL_ACCEPTED.IsMatch(urlChars[i]))
                        {
                            urlChars[i] = '_';
                        }
                    }
                    string cachableName = new(urlChars);
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
                    try
                    {
                        byte[] output = client.GetByteArrayAsync(url).Result;
                        if (output != null && output.Length > 0)
                        {
                            File.WriteAllBytes(cacheFileName, output);
                            return output;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Meta update download failed: {ex}");
                    }
                    if (File.Exists(cacheFileName))
                    {
                        return File.ReadAllBytes(cacheFileName);
                    }
                    return null;
                };
            }
            ExtraData.CachePath = extraDataCache;
            MetaDocs.CurrentMeta = MetaDocsLoader.DownloadAll();
            Console.Error.WriteLine($"Base meta and extra data loaded");
            if (needsRedownload)
            {
                shouldIgnoreCache = true;
                Task.Factory.StartNew(() =>
                {
                    MetaDocs.CurrentMeta = MetaDocsLoader.DownloadAll();
                    Console.Error.WriteLine($"Meta cache refreshed from source");
                });
            }
        }

        static void InitExtensionServer()
        {
            Console.Error.WriteLine("Extension starting...");
            using Stream cin = Console.OpenStandardInput();
            using BufferedStream bcin = new(cin);
            using Stream cout = Console.OpenStandardOutput();
            using PartwiseStreamMessageReader reader = new(bcin);
            using PartwiseStreamMessageWriter writer = new(cout);
            JsonRpcContractResolver contractResolver = new()
            {
                NamingStrategy = new CamelCaseJsonRpcNamingStrategy(),
                ParameterValueConverter = new CamelCaseJsonValueConverter()
            };
            StreamRpcClientHandler clientHandler = new();
            JsonRpcClient client = new(clientHandler);
            LanguageServerSession session = new(client, contractResolver);
            JsonRpcServiceHostBuilder builder = new() { ContractResolver = contractResolver };
            builder.UseCancellationHandling();
            builder.Register(typeof(Program).GetTypeInfo().Assembly);
            IJsonRpcServiceHost host = builder.Build();
            StreamRpcServerHandler serverHandler = new(host, StreamRpcServerHandlerOptions.ConsistentResponseSequence | StreamRpcServerHandlerOptions.SupportsRequestCancellation);
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
