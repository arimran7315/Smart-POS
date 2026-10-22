using SmartPOS.Core.Entities;

namespace SmartPOS.UseCases.Authentication;

public interface IAuthenticationService
{
    Task<bool> IsFirstAdministratorRequiredAsync(CancellationToken ct = default);
    Task<AppUser?> LoginAsync(string userName, string password, CancellationToken ct = default);
    Task<AppUser> CreateFirstAdministratorAsync(string username, string displayName, string password, CancellationToken ct = default);
}
