using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using Zenote.Core.Abstractions;
using Zenote.Core.Options;
using Zenote.Infrastructure.Ai;
using Zenote.Infrastructure.Pdf;

namespace Zenote.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddZenoteInfrastructure(this IServiceCollection services, ModelOptions options)
    {
        var url = new Uri(options.OllamaUrl);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.AddSingleton<IOllamaApiClient>(_ => new OllamaApiClient(url));
        services.AddSingleton<IChatClient>(_ => new OllamaApiClient(url, options.ChatModel));
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ => new OllamaApiClient(url, options.EmbeddingModel));
        services.AddSingleton<IAiHealthCheck, OllamaHealthCheck>();
        services.AddSingleton<IPageRenderer, PdfiumPageRenderer>();
        services.AddSingleton<ISystemCheck, SystemCheck>();

        return services;
    }
}
