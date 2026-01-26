using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TallySyncService;
using TallySyncService.Commands;

// Check for command line arguments
if (args.Length > 0)
{
    switch (args[0])
    {
        case "--setup":
            await SetupCommand.ExecuteAsync();
            return;
        
        case "--login":
            var backendUrl = args.Length > 1 ? args[1] : "http://localhost:8080";
            await LoginCommand.ExecuteAsync(backendUrl);
            return;
    }
}

// Run background service
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<TallySyncWorker>();
    })
    .Build();

await host.RunAsync();