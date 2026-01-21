using System.Xml.Linq;
using TallySyncService.Models;

namespace TallySyncService.Services;

public interface ITallyService
{
    Task<bool> CheckConnectionAsync();
    Task<List<TallyTable>> GetAvailableTablesAsync();
    Task<string> FetchTableDataAsync(string tableName, DateTime? fromDate = null, DateTime? toDate = null);
    Task<List<TallyCompany>> GetCompanyListAsync();
    void SetActiveCompany(string companyName);
    string? GetActiveCompany();
}

public class TallyService : ITallyService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TallyService> _logger;
    private string? _activeCompany;

    public TallyService(IHttpClientFactory httpClientFactory, ILogger<TallyService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public void SetActiveCompany(string companyName)
    {
        _activeCompany = companyName;
        _logger.LogInformation("Active company set to: {CompanyName}", companyName);
    }

    public string? GetActiveCompany()
    {
        return _activeCompany;
    }

    public async Task<List<TallyCompany>> GetCompanyListAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("TallyClient");
            
            var xmlPayload = @"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Collection</TYPE>
    <ID>ListOfCompanies</ID>
  </HEADER>

  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
      </STATICVARIABLES>

      <TDL>
        <TDLMESSAGE>
          <COLLECTION NAME='ListOfCompanies'>
            <TYPE>Company</TYPE>
            <FETCH>NAME</FETCH>
          </COLLECTION>
        </TDLMESSAGE>
      </TDL>
    </DESC>
  </BODY>
</ENVELOPE>";

            var content = new StringContent(xmlPayload, System.Text.Encoding.UTF8, "text/xml");
            var response = await client.PostAsync("", content);
            
            response.EnsureSuccessStatusCode();
            
            var xmlData = await response.Content.ReadAsStringAsync();
            
            // Parse company list
            var doc = XDocument.Parse(xmlData);
            var companies = new List<TallyCompany>();
            
            foreach (var companyElement in doc.Descendants("COMPANY"))
            {
                var name = companyElement.Element("NAME")?.Value;
                var guid = companyElement.Element("GUID")?.Value;
                
                if (!string.IsNullOrEmpty(name))
                {
                    companies.Add(new TallyCompany
                    {
                        Name = name,
                        GUID = guid ?? ""
                    });
                }
            }
            
            _logger.LogInformation("Found {Count} companies in Tally", companies.Count);
            return companies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching company list from Tally");
            throw;
        }
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("TallyClient");
            
            // Simple ping request to Tally
            var pingXml = @"<ENVELOPE>
                            <HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST></HEADER>
                            <BODY><DESC><STATICVARIABLES><SVEXPORTFORMAT>$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES></DESC></BODY>
                          </ENVELOPE>";

            _logger.LogInformation("Attempting to connect to Tally at {Url}...", client.BaseAddress);
            
            var content = new StringContent(pingXml, System.Text.Encoding.UTF8, "text/xml");
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await client.PostAsync("", content, cts.Token);
            
            var isConnected = response.IsSuccessStatusCode;
            if (isConnected)
            {
                _logger.LogInformation("Tally connection successful");
            }
            else
            {
                _logger.LogWarning("Tally connection returned status {StatusCode}: {StatusDescription}", 
                    response.StatusCode, response.ReasonPhrase);
            }
            
            return isConnected;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Tally connection timeout - server at {Url} is not responding. Ensure Tally is running and accessible.", 
                _httpClientFactory.CreateClient("TallyClient").BaseAddress);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Tally connection failed - unable to reach {Url}. Check if Tally server is running.", 
                _httpClientFactory.CreateClient("TallyClient").BaseAddress);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking Tally connection");
            return false;
        }
    }

    public Task<List<TallyTable>> GetAvailableTablesAsync()
    {
        var tables = new List<TallyTable>
        {
            new() { Name = "Ledgers", Description = "Chart of Accounts - Ledgers", CollectionType = "Ledger" },
            new() { Name = "Groups", Description = "Ledger Groups", CollectionType = "Group" },
            new() { Name = "Vouchers", Description = "All Vouchers/Transactions", CollectionType = "Voucher" },
            new() { Name = "StockItems", Description = "Inventory Items", CollectionType = "StockItem" },
            new() { Name = "StockGroups", Description = "Stock Groups", CollectionType = "StockGroup" },
            new() { Name = "Units", Description = "Units of Measure", CollectionType = "Unit" },
            new() { Name = "CostCentres", Description = "Cost Centers", CollectionType = "CostCentre" },
            new() { Name = "Godowns", Description = "Warehouses/Godowns", CollectionType = "Godown" },
            new() { Name = "Currencies", Description = "Currency Masters", CollectionType = "Currency" },
            new() { Name = "VoucherTypes", Description = "Voucher Type Masters", CollectionType = "VoucherType" }
        };

        _logger.LogInformation("Retrieved {Count} available tables from Tally", tables.Count);
        return Task.FromResult(tables);
    }

    public async Task<string> FetchTableDataAsync(string tableName, DateTime? fromDate = null, DateTime? toDate = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("TallyClient");
            
            // Build proper TDL request based on table type with date filters and company
            var xmlPayload = GetTallyXmlRequest(tableName, fromDate, toDate);

            var content = new StringContent(xmlPayload, System.Text.Encoding.UTF8, "text/xml");
            var response = await client.PostAsync("", content);
            
            response.EnsureSuccessStatusCode();
            
            var xmlData = await response.Content.ReadAsStringAsync();
            
            var dateInfo = fromDate.HasValue || toDate.HasValue 
                ? $" (From: {fromDate:yyyy-MM-dd}, To: {toDate:yyyy-MM-dd})" 
                : "";
            
            var companyInfo = !string.IsNullOrEmpty(_activeCompany) ? $" [Company: {_activeCompany}]" : "";
            
            _logger.LogInformation("Successfully fetched data for table: {TableName}{DateInfo}{CompanyInfo}, Size: {Size} bytes", 
                tableName, dateInfo, companyInfo, xmlData.Length);
            
            return xmlData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching data for table: {TableName}", tableName);
            throw;
        }
    }

     private string GetTallyXmlRequest(string tableName, DateTime? fromDate, DateTime? toDate)
{
    var fromDateStr = fromDate?.ToString("yyyyMMdd") ?? "";
    var toDateStr = toDate?.ToString("yyyyMMdd") ?? "";
    
    var dateFilter = "";
    if (!string.IsNullOrEmpty(fromDateStr) && !string.IsNullOrEmpty(toDateStr))
    {
        dateFilter = $@"<SVFROMDATE>{fromDateStr}</SVFROMDATE>
                       <SVTODATE>{toDateStr}</SVTODATE>";
    }

    var companyFilter = "";
    if (!string.IsNullOrEmpty(_activeCompany))
    {
        companyFilter = $"<SVCURRENTCOMPANY>{_activeCompany}</SVCURRENTCOMPANY>";
    }

    return tableName switch
    {
        "Ledgers" => $@"<ENVELOPE>
                        <HEADER>
                            <VERSION>1</VERSION>
                            <TALLYREQUEST>Export</TALLYREQUEST>
                            <TYPE>Collection</TYPE>
                            <ID>Ledgers</ID>
                        </HEADER>
                        <BODY>
                            <DESC>
                                <STATICVARIABLES>
                                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                    {companyFilter}
                                    {dateFilter}
                                </STATICVARIABLES>
                                <TDL>
                                    <TDLMESSAGE>
                                        <COLLECTION NAME='Ledgers' ISMODIFY='No' ISFIXED='No' ISINITIALIZE='No' ISOPTION='No' ISINTERNAL='No'>
                                            <TYPE>Ledger</TYPE>
                                            <FETCH>NAME, PARENT, OPENINGBALANCE, CLOSINGBALANCE, GUID, ALTERID, LEDGERPHONE, LEDGEREMAIL, LEDGERCONTACT, COUNTRYNAME, STATENAME, PINCODE, GSTREGISTRATIONTYPE, PARTYGSTIN, ADDRESS.LIST</FETCH>
                                        </COLLECTION>
                                    </TDLMESSAGE>
                                </TDL>
                            </DESC>
                        </BODY>
                    </ENVELOPE>",

        "Groups" => $@"<ENVELOPE>
                        <HEADER>
                            <VERSION>1</VERSION>
                            <TALLYREQUEST>Export</TALLYREQUEST>
                            <TYPE>Collection</TYPE>
                            <ID>Groups</ID>
                        </HEADER>
                        <BODY>
                            <DESC>
                                <STATICVARIABLES>
                                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                    {companyFilter}
                                </STATICVARIABLES>
                                <TDL>
                                    <TDLMESSAGE>
                                        <COLLECTION NAME='Groups'>
                                            <TYPE>Group</TYPE>
                                            <FETCH>NAME, PARENT, PRIMARYGROUP, ISSUBLEDGER, ISADDABLE, GUID, ALTERID</FETCH>
                                        </COLLECTION>
                                    </TDLMESSAGE>
                                </TDL>
                            </DESC>
                        </BODY>
                    </ENVELOPE>",

        "Vouchers" => $@"<ENVELOPE>
                        <HEADER>
                            <VERSION>1</VERSION>
                            <TALLYREQUEST>Export</TALLYREQUEST>
                            <TYPE>Collection</TYPE>
                            <ID>Vouchers</ID>
                        </HEADER>
                        <BODY>
                            <DESC>
                                <STATICVARIABLES>
                                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                    {companyFilter}
                                    {dateFilter}
                                </STATICVARIABLES>
                                <TDL>
                                    <TDLMESSAGE>
                                        <COLLECTION NAME='Vouchers'>
                                            <TYPE>Voucher</TYPE>
                                            <FETCH>DATE, VOUCHERTYPENAME, VOUCHERNUMBER, REFERENCE, REFERENCEDATE, NARRATION, PARTYLEDGERNAME, AMOUNT, GUID, ALTERID, ALLLEDGERENTRIES.LIST</FETCH>
                                            <FILTER>DateFilter</FILTER>
                                        </COLLECTION>
                                        <SYSTEM TYPE='Formulae' NAME='DateFilter'>
                                            $$IsSysNameEqual:VoucherDate:##SVFROMDATE:##SVTODATE
                                        </SYSTEM>
                                    </TDLMESSAGE>
                                </TDL>
                            </DESC>
                        </BODY>
                    </ENVELOPE>",

        "StockItems" => $@"<ENVELOPE>
                        <HEADER>
                            <VERSION>1</VERSION>
                            <TALLYREQUEST>Export</TALLYREQUEST>
                            <TYPE>Collection</TYPE>
                            <ID>StockItems</ID>
                        </HEADER>
                        <BODY>
                            <DESC>
                                <STATICVARIABLES>
                                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                    {companyFilter}
                                </STATICVARIABLES>
                                <TDL>
                                    <TDLMESSAGE>
                                        <COLLECTION NAME='StockItems'>
                                            <TYPE>StockItem</TYPE>
                                            <FETCH>NAME, PARENT, CATEGORY, BASEUNITS, OPENINGBALANCE, CLOSINGBALANCE, OPENINGVALUE, CLOSINGVALUE, GUID, ALTERID, GSTAPPLICABLE, HSNCODE, GSTDETAILS.LIST</FETCH>
                                        </COLLECTION>
                                    </TDLMESSAGE>
                                </TDL>
                            </DESC>
                        </BODY>
                    </ENVELOPE>",

        "StockGroups" => $@"<ENVELOPE>
                        <HEADER>
                            <VERSION>1</VERSION>
                            <TALLYREQUEST>Export</TALLYREQUEST>
                            <TYPE>Collection</TYPE>
                            <ID>StockGroups</ID>
                        </HEADER>
                        <BODY>
                            <DESC>
                                <STATICVARIABLES>
                                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                    {companyFilter}
                                </STATICVARIABLES>
                                <TDL>
                                    <TDLMESSAGE>
                                        <COLLECTION NAME='StockGroups'>
                                            <TYPE>StockGroup</TYPE>
                                            <FETCH>NAME, PARENT, GUID, ALTERID</FETCH>
                                        </COLLECTION>
                                    </TDLMESSAGE>
                                </TDL>
                            </DESC>
                        </BODY>
                    </ENVELOPE>",

        "Units" => $@"<ENVELOPE>
                        <HEADER>
                            <VERSION>1</VERSION>
                            <TALLYREQUEST>Export</TALLYREQUEST>
                            <TYPE>Collection</TYPE>
                            <ID>Units</ID>
                        </HEADER>
                        <BODY>
                            <DESC>
                                <STATICVARIABLES>
                                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                    {companyFilter}
                                </STATICVARIABLES>
                                <TDL>
                                    <TDLMESSAGE>
                                        <COLLECTION NAME='Units'>
                                            <TYPE>Unit</TYPE>
                                            <FETCH>NAME, FORMALNAME, ISSIMPLEUNIT, GUID, ALTERID</FETCH>
                                        </COLLECTION>
                                    </TDLMESSAGE>
                                </TDL>
                            </DESC>
                        </BODY>
                    </ENVELOPE>",

        "VoucherTypes" => $@"<ENVELOPE>
                        <HEADER>
                            <VERSION>1</VERSION>
                            <TALLYREQUEST>Export</TALLYREQUEST>
                            <TYPE>Collection</TYPE>
                            <ID>VoucherTypes</ID>
                        </HEADER>
                        <BODY>
                            <DESC>
                                <STATICVARIABLES>
                                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                    {companyFilter}
                                </STATICVARIABLES>
                                <TDL>
                                    <TDLMESSAGE>
                                        <COLLECTION NAME='VoucherTypes'>
                                            <TYPE>VoucherType</TYPE>
                                            <FETCH>NAME, PARENT, NUMBERINGMETHOD, GUID, ALTERID</FETCH>
                                        </COLLECTION>
                                    </TDLMESSAGE>
                                </TDL>
                            </DESC>
                        </BODY>
                    </ENVELOPE>",

        "CostCentres" => $@"<ENVELOPE>
                        <HEADER>
                            <VERSION>1</VERSION>
                            <TALLYREQUEST>Export</TALLYREQUEST>
                            <TYPE>Collection</TYPE>
                            <ID>CostCentres</ID>
                        </HEADER>
                        <BODY>
                            <DESC>
                                <STATICVARIABLES>
                                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                    {companyFilter}
                                </STATICVARIABLES>
                                <TDL>
                                    <TDLMESSAGE>
                                        <COLLECTION NAME='CostCentres'>
                                            <TYPE>CostCentre</TYPE>
                                            <FETCH>NAME, PARENT, GUID, ALTERID</FETCH>
                                        </COLLECTION>
                                    </TDLMESSAGE>
                                </TDL>
                            </DESC>
                        </BODY>
                    </ENVELOPE>",

        "Godowns" => $@"<ENVELOPE>
                        <HEADER>
                            <VERSION>1</VERSION>
                            <TALLYREQUEST>Export</TALLYREQUEST>
                            <TYPE>Collection</TYPE>
                            <ID>Godowns</ID>
                        </HEADER>
                        <BODY>
                            <DESC>
                                <STATICVARIABLES>
                                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                    {companyFilter}
                                </STATICVARIABLES>
                                <TDL>
                                    <TDLMESSAGE>
                                        <COLLECTION NAME='Godowns'>
                                            <TYPE>Godown</TYPE>
                                            <FETCH>NAME, PARENT, GUID, ALTERID</FETCH>
                                        </COLLECTION>
                                    </TDLMESSAGE>
                                </TDL>
                            </DESC>
                        </BODY>
                    </ENVELOPE>",

        "Currencies" => $@"<ENVELOPE>
                        <HEADER>
                            <VERSION>1</VERSION>
                            <TALLYREQUEST>Export</TALLYREQUEST>
                            <TYPE>Collection</TYPE>
                            <ID>Currencies</ID>
                        </HEADER>
                        <BODY>
                            <DESC>
                                <STATICVARIABLES>
                                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                    {companyFilter}
                                </STATICVARIABLES>
                                <TDL>
                                    <TDLMESSAGE>
                                        <COLLECTION NAME='Currencies'>
                                            <TYPE>Currency</TYPE>
                                            <FETCH>NAME, SYMBOL, GUID, ALTERID</FETCH>
                                        </COLLECTION>
                                    </TDLMESSAGE>
                                </TDL>
                            </DESC>
                        </BODY>
                    </ENVELOPE>",

        _ => throw new ArgumentException($"Unknown table name: {tableName}")
    };
}

public async Task<string> TestFetchWithLogging(string tableName)
{
    try
    {
        var xmlData = await FetchTableDataAsync(tableName);
        
        _logger.LogInformation("=== Raw XML Response ===");
        _logger.LogInformation(xmlData);
        
        // Parse and count records
        var doc = XDocument.Parse(xmlData);
        var records = doc.Descendants(GetCollectionType(tableName)).Count();
        
        _logger.LogInformation("Found {Count} records for {TableName}", records, tableName);
        
        return xmlData;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Test fetch failed for {TableName}", tableName);
        throw;
    }
}

    private string GetCollectionType(string tableName)
    {
        return tableName switch
        {
            "Ledgers" => "Ledger",
            "Groups" => "Group",
            "Vouchers" => "Voucher",
            "StockItems" => "StockItem",
            "StockGroups" => "StockGroup",
            "Units" => "Unit",
            "CostCentres" => "CostCentre",
            "Godowns" => "Godown",
            "Currencies" => "Currency",
            "VoucherTypes" => "VoucherType",
            _ => tableName
        };
    }
}