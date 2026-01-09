namespace TallySyncService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly HttpClient _tallyClient;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _tallyClient = new HttpClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

            try {
                await SyncTallyData();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error during Tally Sync");
            }

            // Wait for 15 minutes before next sync
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task SyncTallyData()
    {
        string tallyUrl = "http://localhost:9000"; // Tally's default port
        string xmlPayload = @"<ENVELOPE>
                                <HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Collection</TYPE><ID>List of Ledgers</ID></HEADER>
                                <BODY><DESC><STATICVARIABLES><SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES></DESC></BODY>
                              </ENVELOPE>";

        var content = new StringContent(xmlPayload, System.Text.Encoding.UTF8, "text/xml");
        var response = await _tallyClient.PostAsync(tallyUrl, content);
        
        if (response.IsSuccessStatusCode)
        {
            string xmlData = await response.Content.ReadAsStringAsync();
            await File.WriteAllTextAsync("output.txt", xmlData);
            _logger.LogInformation("Successfully fetched data from Tally and wrote to output.txt.");
        }
    }
}
