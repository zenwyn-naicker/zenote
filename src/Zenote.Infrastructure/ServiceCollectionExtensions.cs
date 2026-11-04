using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using OllamaSharp.Models;
using Zenote.Core.Abstractions;
using Zenote.Core.Answering;
using Zenote.Core.Chunking;
using Zenote.Core.Documents;
using Zenote.Core.Indexing;
using Zenote.Core.Options;
using Zenote.Core.Retrieval;
using Zenote.Infrastructure.Ai;
using Zenote.Infrastructure.Pdf;
using Zenote.Infrastructure.Persistence;
using Zenote.Infrastructure.Storage;
using Zenote.Infrastructure.Vectors;

namespace Zenote.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddZenoteInfrastructure(this IServiceCollection services, ModelOptions options, AppPaths paths)
    {
        var url = new Uri(options.OllamaUrl);

        Directory.CreateDirectory(paths.Root);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.AddSingleton<IOllamaApiClient>(_ => new OllamaApiClient(url));
        services.AddSingleton(_ => new SwitchableChatClient(model => CreateChatClient(url, options, model), options.ChatModel));
        services.AddSingleton<IChatClient>(provider => provider.GetRequiredService<SwitchableChatClient>());
        services.AddSingleton<IChatModels, OllamaChatModels>();
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ => new OllamaApiClient(url, options.EmbeddingModel));
        services.AddSingleton<IAiHealthCheck, OllamaHealthCheck>();
        services.AddSingleton(paths);
        services.AddSingleton(new SqliteConnectionFactory(paths.DatabasePath));
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<INotebookRepository, NotebookRepository>();
        services.AddSingleton<IDocumentRepository, DocumentRepository>();
        services.AddSingleton<IConversationRepository, ConversationRepository>();
        services.AddSingleton<IDocumentFileStore, DocumentFileStore>();
        services.AddSingleton<IDocumentLibrary, DocumentLibrary>();
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ChunkingOptions()));
        services.AddSingleton<IChunker, PageChunker>();
        services.AddSingleton<ITextEmbedder, OllamaTextEmbedder>();
        services.AddSingleton<IVectorStore, SqliteVecVectorStore>();
        services.AddSingleton<IIndexingQueue, IndexingQueue>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new RetrievalOptions()));
        services.AddSingleton<IRetriever, Retriever>();
        services.AddSingleton<IRagAnswerService, RagAnswerService>();
        services.AddSingleton<IModelWarmup, ModelWarmup>();
        services.AddSingleton<IPageRenderer, PdfiumPageRenderer>();
        services.AddSingleton<ISystemCheck, SystemCheck>();

        return services;
    }

    // Ollama silently cuts off prompts longer than its context window, so it's always set explicitly.
    // Keeping this here means Core never needs to know about Ollama.
    public static IChatClient CreateChatClient(Uri url, ModelOptions options, string model) =>
        new ChatClientBuilder(new OllamaApiClient(url, model))
            .ConfigureOptions(chat => chat
                .AddOllamaOption(OllamaOption.NumCtx, options.ChatContextTokens)
                .AddOllamaOption(OllamaOption.Think, false))
            .Build();
}
