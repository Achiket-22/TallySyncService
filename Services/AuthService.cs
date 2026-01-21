using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TallySyncService.Models;

namespace TallySyncService.Services;

public interface IAuthService
{
    Task<bool> SendOtpAsync(string email);
    Task<bool> ValidateOtpAsync(string email, string otp);
    Task<string?> GetValidTokenAsync();
    bool IsAuthenticated();
    Task<bool> LoginAsync();
}

public class AuthService : IAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfigurationService _configService;
    private readonly ILogger<AuthService> _logger;
    private readonly TallySyncOptions _options;
    private AuthState? _authState;
    private string? _publicKeyPem;

    public AuthService(
        IHttpClientFactory httpClientFactory,
        IConfigurationService configService,
        ILogger<AuthService> logger,
        IOptions<TallySyncOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _configService = configService;
        _logger = logger;
        _options = options.Value;
        
        // Load saved auth state
        LoadAuthState();
    }

    private async Task<bool> FetchPublicKeyAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            
            _logger.LogInformation("Fetching RSA public key from /key endpoint");
            
            var response = await client.GetAsync("/key");
            
            if (response.IsSuccessStatusCode)
            {
                var keyData = await response.Content.ReadAsStringAsync();
                
                // Parse JSON response {"key": "..."}
                try
                {
                    var jsonDoc = JsonDocument.Parse(keyData);
                    if (jsonDoc.RootElement.TryGetProperty("key", out var keyElement))
                    {
                        _publicKeyPem = keyElement.GetString();
                    }
                    else
                    {
                        // Fallback: assume raw key
                        _publicKeyPem = keyData;
                    }
                }
                catch
                {
                    // Not JSON, use as-is
                    _publicKeyPem = keyData;
                }
                
                _logger.LogInformation("Successfully fetched RSA public key");
                return true;
            }
            else
            {
                _logger.LogError("Failed to fetch public key. Status: {Status}", response.StatusCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching public key");
            return false;
        }
    }

    public async Task<bool> SendOtpAsync(string email)
    {
        try
        {
            // Fetch public key if not already loaded
            if (string.IsNullOrEmpty(_publicKeyPem))
            {
                var keyFetched = await FetchPublicKeyAsync();
                if (!keyFetched)
                {
                    _logger.LogError("Cannot send OTP without public key");
                    return false;
                }
            }

            var client = _httpClientFactory.CreateClient("BackendClient");
            
            // Encrypt email with RSA public key
            var encryptedEmail = EncryptWithRsa(email);
            
            var payload = new Dictionary<string, string>
            {
                { "email", encryptedEmail }
            };

            _logger.LogInformation("Sending OTP to email: {Email}", email);
            
            var response = await client.PostAsJsonAsync("/sendotpmail", payload);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("OTP sent successfully to {Email}", email);
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send OTP. Status: {Status}, Error: {Error}", 
                    response.StatusCode, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending OTP");
            return false;
        }
    }

    public async Task<bool> ValidateOtpAsync(string email, string otp)
    {
        try
        {
            // Fetch public key if not already loaded
            if (string.IsNullOrEmpty(_publicKeyPem))
            {
                var keyFetched = await FetchPublicKeyAsync();
                if (!keyFetched)
                {
                    _logger.LogError("Cannot validate OTP without public key");
                    return false;
                }
            }

            var client = _httpClientFactory.CreateClient("BackendClient");
            
            // Encrypt email and OTP with RSA public key
            var encryptedEmail = EncryptWithRsa(email);
            var encryptedOtp = EncryptWithRsa(otp);
            
            var payload = new Dictionary<string, string>
            {
                { "email", encryptedEmail },
                { "code", encryptedOtp }
            };

            _logger.LogInformation("Validating OTP for email: {Email}", email);
            
            var response = await client.PostAsJsonAsync("/validateotp", payload);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
                
                if (result != null && !string.IsNullOrEmpty(result.Token))
                {
                    // Save auth state
                    _authState = new AuthState
                    {
                        JwtToken = result.Token,
                        TokenExpiry = DateTime.UtcNow.AddDays(30), // JWT is valid for 30 days
                        UserEmail = email,
                        IsAuthenticated = true
                    };
                    
                    SaveAuthState();
                    
                    _logger.LogInformation("Successfully authenticated for {Email}", email);
                    return true;
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("OTP validation failed. Status: {Status}, Error: {Error}", 
                    response.StatusCode, error);
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating OTP");
            return false;
        }
    }

    public Task<string?> GetValidTokenAsync()
    {
        if (_authState == null || !_authState.IsAuthenticated)
        {
            _logger.LogWarning("Not authenticated");
            return Task.FromResult<string?>(null);
        }

        // Check if token is expired
        if (_authState.TokenExpiry.HasValue && _authState.TokenExpiry.Value < DateTime.UtcNow)
        {
            _logger.LogWarning("Token expired. Need to re-authenticate");
            _authState.IsAuthenticated = false;
            SaveAuthState();
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(_authState.JwtToken);
    }

    public bool IsAuthenticated()
    {
        if (_authState == null)
            return false;

        if (!_authState.IsAuthenticated)
            return false;

        if (_authState.TokenExpiry.HasValue && _authState.TokenExpiry.Value < DateTime.UtcNow)
        {
            _authState.IsAuthenticated = false;
            SaveAuthState();
            return false;
        }

        return true;
    }

    public async Task<bool> LoginAsync()
    {
        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║   Tally Sync Service - Login              ║");
        Console.WriteLine("╚════════════════════════════════════════════╝");
        Console.WriteLine();

        // Check if already authenticated
        if (IsAuthenticated())
        {
            Console.WriteLine("Already authenticated as: " + _authState?.UserEmail);
            Console.Write("Do you want to login with a different account? (y/n): ");
            var answer = Console.ReadLine()?.Trim().ToLower();
            if (answer != "y" && answer != "yes")
            {
                return true;
            }
        }

        // Fetch public key first
        Console.WriteLine("Fetching encryption key from backend...");
        var keyFetched = await FetchPublicKeyAsync();
        if (!keyFetched)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Failed to fetch encryption key. Please ensure backend is running.");
            Console.ResetColor();
            return false;
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓ Encryption key fetched successfully");
        Console.ResetColor();
        Console.WriteLine();

        Console.Write("Enter your email: ");
        var email = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(email))
        {
            Console.WriteLine("Email cannot be empty");
            return false;
        }

        // Send OTP
        Console.WriteLine("Sending OTP to {0}...", email);
        var otpSent = await SendOtpAsync(email);

        if (!otpSent)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Failed to send OTP. Please try again.");
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("OTP sent successfully!");
        Console.ResetColor();
        Console.WriteLine();

        Console.Write("Enter the 6-digit OTP: ");
        var otp = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(otp))
        {
            Console.WriteLine("OTP cannot be empty");
            return false;
        }

        // Validate OTP
        Console.WriteLine("Validating OTP...");
        var validated = await ValidateOtpAsync(email, otp);

        if (validated)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Successfully authenticated!");
            Console.ResetColor();
            return true;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("✗ Invalid OTP. Authentication failed.");
            Console.ResetColor();
            return false;
        }
    }

    private string EncryptWithRsa(string plainText)
    {
        if (string.IsNullOrEmpty(_publicKeyPem))
        {
            _logger.LogError("No RSA public key available for encryption");
            throw new InvalidOperationException("RSA public key not available");
        }

        try
        {
            using var rsa = RSA.Create();
            
            // Handle both PKCS#1 (RSA PUBLIC KEY) and PKCS#8 (PUBLIC KEY) formats
            if (_publicKeyPem.Contains("BEGIN RSA PUBLIC KEY"))
            {
                // PKCS#1 format - need to use ImportRSAPublicKey
                var pemLines = _publicKeyPem
                    .Replace("-----BEGIN RSA PUBLIC KEY-----", "")
                    .Replace("-----END RSA PUBLIC KEY-----", "")
                    .Replace("\n", "")
                    .Replace("\r", "")
                    .Trim();
                
                var keyBytes = Convert.FromBase64String(pemLines);
                rsa.ImportRSAPublicKey(keyBytes, out _);
            }
            else if (_publicKeyPem.Contains("BEGIN PUBLIC KEY"))
            {
                // PKCS#8 format - standard PEM import
                rsa.ImportFromPem(_publicKeyPem);
            }
            else
            {
                // Try as base64 encoded DER
                var keyBytes = Convert.FromBase64String(_publicKeyPem.Trim());
                rsa.ImportSubjectPublicKeyInfo(keyBytes, out _);
            }

            var dataToEncrypt = Encoding.UTF8.GetBytes(plainText);
            var encryptedData = rsa.Encrypt(dataToEncrypt, RSAEncryptionPadding.OaepSHA256);
            
            return Convert.ToBase64String(encryptedData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting with RSA. Key preview: {KeyPreview}", 
                _publicKeyPem?.Substring(0, Math.Min(100, _publicKeyPem?.Length ?? 0)));
            throw;
        }
    }

    private void LoadAuthState()
    {
        try
        {
            var authFilePath = Path.Combine(_configService.GetDataDirectory(), "auth-state.json");
            
            if (File.Exists(authFilePath))
            {
                var json = File.ReadAllText(authFilePath);
                _authState = JsonSerializer.Deserialize<AuthState>(json);
                _logger.LogInformation("Auth state loaded");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load auth state");
        }
    }

    private void SaveAuthState()
    {
        try
        {
            var authFilePath = Path.Combine(_configService.GetDataDirectory(), "auth-state.json");
            var json = JsonSerializer.Serialize(_authState, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(authFilePath, json);
            _logger.LogInformation("Auth state saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save auth state");
        }
    }
}