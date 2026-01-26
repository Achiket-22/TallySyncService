namespace TallySyncService.Models;

public class AuthConfig
{
    public string BackendUrl { get; set; } = "http://localhost:8080";
    public string? JwtToken { get; set; }
    public uint? OrganisationId { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ValidateOtpRequest
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
}

public class UserOrganisation
{
    public uint UserId { get; set; }
    public uint OrganisationId { get; set; }
    public string OrganisationCode { get; set; } = string.Empty;
}
