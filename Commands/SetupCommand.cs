using System.Text.Json;
using TallySyncService.Models;
using TallySyncService.Services;

namespace TallySyncService.Commands;

public class SetupCommand
{
    public static async Task ExecuteAsync()
    {
        Console.WriteLine("╔═══════════════════════════════════════════════╗");
        Console.WriteLine("║   Tally Sync Service - Setup                  ║");
        Console.WriteLine("╚═══════════════════════════════════════════════╝");
        Console.WriteLine();

        var config = new Dictionary<string, object>();

        // Tally Configuration
        Console.WriteLine("=== Tally Configuration ===");
        Console.Write("Tally Server [localhost]: ");
        var server = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(server)) server = "localhost";

        Console.Write("Tally Port [9000]: ");
        var portStr = Console.ReadLine();
        var port = string.IsNullOrWhiteSpace(portStr) ? 9000 : int.Parse(portStr);

        Console.Write("Company Name (leave empty to select at runtime): ");
        var company = Console.ReadLine() ?? "";

        config["tally"] = new Dictionary<string, object>
        {
            { "server", server },
            { "port", port },
            { "company", company }
        };

        // Sync Configuration
        Console.WriteLine("\n=== Sync Configuration ===");
        Console.Write("Sync Interval (minutes) [15]: ");
        var intervalStr = Console.ReadLine();
        var interval = string.IsNullOrWhiteSpace(intervalStr) ? 15 : int.Parse(intervalStr);

        Console.Write("Export Path [./exports]: ");
        var exportPath = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(exportPath)) exportPath = "./exports";

        config["sync"] = new Dictionary<string, object>
        {
            { "intervalMinutes", interval },
            { "exportPath", exportPath }
        };

        // Backend Configuration
        Console.WriteLine("\n=== Backend Configuration ===");
        Console.Write("Backend Base URL (e.g., http://localhost:3001): ");
        var backendBaseUrl = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(backendBaseUrl)) backendBaseUrl = "http://localhost:3001";
        
        // Remove trailing slash if present
        backendBaseUrl = backendBaseUrl.TrimEnd('/');

        config["backend"] = new Dictionary<string, object>
        {
            { "url", $"{backendBaseUrl}/api/data" }
        };

        // Save configuration
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);
        await File.WriteAllTextAsync("config.json", json);

        Console.WriteLine("\n✅ Configuration saved to config.json");
        Console.WriteLine();

        // Test Tally connection
        Console.WriteLine("Testing Tally connection...");
        var tallyConfig = new TallyConfig
        {
            Server = server,
            Port = port,
            Company = company
        };

        var tallyService = new TallyXmlService(tallyConfig);
        try
        {
            var connected = await tallyService.TestConnectionAsync();
            if (connected)
            {
                Console.WriteLine($"✓ Successfully connected to Tally at {server}:{port}");
                
                var companies = await tallyService.GetCompanyListAsync();
                Console.WriteLine($"✓ Found {companies.Count} company(ies):");
                foreach (var c in companies)
                {
                    Console.WriteLine($"  • {c.Name}");
                }
            }
            else
            {
                Console.WriteLine($"✗ Could not connect to Tally at {server}:{port}");
                Console.WriteLine("  Make sure Tally is running and XML interface is enabled");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Connection error: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("Setup complete! You can now:");
        Console.WriteLine($"  1. Login: dotnet run -- --login {backendBaseUrl}");
        Console.WriteLine("  2. Start sync: dotnet run");
        Console.WriteLine();
        Console.WriteLine($"Backend configured: {backendBaseUrl}");
    }
}
