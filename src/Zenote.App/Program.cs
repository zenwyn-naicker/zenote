using Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zenote.App.Services;
using Zenote.App.ViewModels;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;
using Zenote.Core.Options;
using Zenote.Infrastructure;
using Zenote.Infrastructure.Persistence;
using Zenote.Infrastructure.Storage;

namespace Zenote.App;

public static class Program
{
    public static IServiceProvider Services { get; private set; } = null!;

    [STAThread]
    public static int Main(string[] args)
    {
        Services = BuildServices();
        Services.GetRequiredService<DatabaseInitializer>().Initialize();

        if (args.Contains("--self-check"))
        {
            return RunSelfCheck();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Used by the Avalonia previewer as well as Main.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static string UserSettingsPath => Path.Combine(AppPaths.Default.Root, "settings.json");

    private static ServiceProvider BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile(UserSettingsPath, optional: true)
            .Build();

        var models = configuration.GetSection("Models").Get<ModelOptions>() ?? new ModelOptions();

        return new ServiceCollection()
            .AddZenoteInfrastructure(models, AppPaths.Default)
            .AddZenoteViewModels()
            .AddSingleton(new UserSettings(UserSettingsPath))
            .BuildServiceProvider();
    }

    public static IServiceCollection AddZenoteViewModels(this IServiceCollection services) => services
        .AddSingleton<IFilePicker, AvaloniaFilePicker>()
        .AddSingleton<NotebooksViewModel>()
        .AddSingleton<SettingsViewModel>()
        .AddSingleton<ModelPickerViewModel>()
        .AddTransient<MainWindowViewModel>()
        .AddSingleton<Func<Notebook, NotebookViewModel>>(provider => notebook => new NotebookViewModel(
            notebook,
            provider.GetRequiredService<IDocumentRepository>(),
            provider.GetRequiredService<IDocumentLibrary>(),
            provider.GetRequiredService<IFilePicker>(),
            provider.GetRequiredService<IIndexingQueue>(),
            provider.GetRequiredService<IRagAnswerService>(),
            provider.GetRequiredService<IPageRenderer>(),
            provider.GetRequiredService<IConversationRepository>(),
            provider.GetRequiredService<INotebookRepository>()));

    // Prints the same checks as the status bar, so a published build can be tested without the UI.
    private static int RunSelfCheck()
    {
        var results = Services.GetRequiredService<ISystemCheck>().RunAsync(CancellationToken.None).GetAwaiter().GetResult();

        foreach (var result in results)
        {
            Console.WriteLine($"{(result.Ok ? "ok  " : "FAIL")} {result.Name} {result.Detail}".TrimEnd());
        }

        return results.All(r => r.Ok) ? 0 : 1;
    }
}
