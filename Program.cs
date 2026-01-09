using TallySyncService;

var builder = Host.CreateApplicationBuilder(args);

// Enable Windows Service support
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Tally Data Sync Service";
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
