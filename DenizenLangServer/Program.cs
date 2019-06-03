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

namespace DenizenLangServer
{
    class Program
    {
        static void Main(string[] args)
        {
            using (Stream cin = Console.OpenStandardInput())
            using (BufferedStream bcin = new BufferedStream(cin))
            using (Stream cout = Console.OpenStandardOutput())
            using (PartwiseStreamMessageReader reader = new PartwiseStreamMessageReader(bcin))
            using (PartwiseStreamMessageWriter writer = new PartwiseStreamMessageWriter(cout))
            {
                JsonRpcContractResolver contractResolver = new JsonRpcContractResolver
                {
                    NamingStrategy = new CamelCaseJsonRpcNamingStrategy(),
                    ParameterValueConverter = new CamelCaseJsonValueConverter(),
                };
                StreamRpcClientHandler clientHandler = new StreamRpcClientHandler();
                JsonRpcClient client = new JsonRpcClient(clientHandler);
                clientHandler.MessageSending += (_, e) =>
                {
                    Console.Error.WriteLine("Sending: " + e.Message);
                };
                clientHandler.MessageReceiving += (_, e) =>
                {
                    Console.Error.WriteLine("Receiving: " + e.Message);
                };
                LanguageServerSession session = new LanguageServerSession(client, contractResolver);
                JsonRpcServiceHostBuilder builder = new JsonRpcServiceHostBuilder { ContractResolver = contractResolver };
                builder.UseCancellationHandling();
                builder.Register(typeof(Program).GetTypeInfo().Assembly);
                builder.Intercept(async (context, next) =>
                {
                    Console.Error.WriteLine("Request: " + context.Request);
                    await next();
                    Console.Error.WriteLine("Response: " + context.Response);
                });
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
}
