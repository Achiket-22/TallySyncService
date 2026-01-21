namespace TallySyncService.Models;

public class AuthRequest
{
    public string Email { get; set; } = string.Empty;
}

public class OtpRequest
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
}

public class AuthState
{
    public string? JwtToken { get; set; }
    public DateTime? TokenExpiry { get; set; }
    public string? UserEmail { get; set; }
    public bool IsAuthenticated { get; set; }
}