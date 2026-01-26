using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TallySyncService.Models;

namespace TallySyncService.Services;

public class AuthService
{
    private readonly string _backendUrl;
    private readonly HttpClient _httpClient;
    private RSA? _rsa;

    public AuthService(string backendUrl)
    {
        _backendUrl = backendUrl;
        _httpClient = new HttpClient();
    }

    public async Task<bool> SendOtpEmailAsync(string email)
    {
        try
        {
            // Get public key from backend
            await LoadPublicKeyAsync();

            // Encrypt email
            var encryptedEmail = EncryptData(email);

            // Send OTP request
            var request = new Dictionary<string, string>
            {
                { "email", encryptedEmail }
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_backendUrl}/sendotpmail", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"✓ {responseText}");
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"✗ Failed to send OTP: {error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error sending OTP: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> ValidateOtpAsync(string email, string code)
    {
        try
        {
            // Encrypt email and code
            var encryptedEmail = EncryptData(email);
            var encryptedCode = EncryptData(code);

            // Send validation request
            var request = new Dictionary<string, string>
            {
                { "email", encryptedEmail },
                { "code", encryptedCode }
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_backendUrl}/validateotp", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loginResponse = JsonSerializer.Deserialize<LoginResponse>(responseText, options);
                
                if (loginResponse != null && !string.IsNullOrEmpty(loginResponse.Token))
                {
                    Console.WriteLine("✓ Login successful!");
                    return loginResponse.Token;
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"✗ Invalid OTP: {error}");
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error validating OTP: {ex.Message}");
            return null;
        }
    }

    private async Task LoadPublicKeyAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_backendUrl}/key");
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync();
            var keyResponse = JsonSerializer.Deserialize<Dictionary<string, string>>(responseText);

            if (keyResponse != null && keyResponse.ContainsKey("key"))
            {
                var publicKeyPem = keyResponse["key"];
                _rsa = RSA.Create();
                _rsa.ImportFromPem(publicKeyPem);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to load public key: {ex.Message}", ex);
        }
    }

    public async Task<List<UserOrganisation>?> GetUserOrganisationsAsync(string token)
    {
        try
        {
            // Send token directly without "Bearer" prefix
            if (_httpClient.DefaultRequestHeaders.Contains("Authorization"))
            {
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
            }
            _httpClient.DefaultRequestHeaders.Add("Authorization", token);

            var response = await _httpClient.GetAsync($"{_backendUrl}/users/orgs");
            
            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var orgs = JsonSerializer.Deserialize<List<UserOrganisation>>(responseText, options);
                return orgs;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"✗ Failed to fetch organizations: {error}");
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error fetching organizations: {ex.Message}");
            return null;
        }
    }

    private string EncryptData(string data)
    {
        if (_rsa == null)
            throw new InvalidOperationException("Public key not loaded");

        var dataBytes = Encoding.UTF8.GetBytes(data);
        // Use OAEP with SHA256 to match Go backend's rsa.DecryptOAEP(sha256.New(), ...)
        var encryptedBytes = _rsa.Encrypt(dataBytes, RSAEncryptionPadding.OaepSHA256);
        return Convert.ToBase64String(encryptedBytes);
    }

    public static void SaveToken(string token, uint? organisationId = null)
    {
        var tokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tally_token");
        var orgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tally_org");
        
        File.WriteAllText(tokenPath, token);
        
        if (organisationId.HasValue)
        {
            File.WriteAllText(orgPath, organisationId.Value.ToString());
        }
    }

    public static string? LoadToken()
    {
        var tokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tally_token");
        
        if (File.Exists(tokenPath))
        {
            return File.ReadAllText(tokenPath);
        }

        return null;
    }

    public static uint? LoadOrganisationId()
    {
        var orgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tally_org");
        
        if (File.Exists(orgPath))
        {
            var content = File.ReadAllText(orgPath);
            if (uint.TryParse(content, out var orgId))
            {
                return orgId;
            }
        }

        return null;
    }

    public static void ClearToken()
    {
        var tokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tally_token");
        var orgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tally_org");
        
        if (File.Exists(tokenPath))
        {
            File.Delete(tokenPath);
        }
        
        if (File.Exists(orgPath))
        {
            File.Delete(orgPath);
        }
    }
}
