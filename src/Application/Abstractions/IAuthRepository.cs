namespace Application.Abstractions;

public interface IAuthRepository
{
    Task<object?> LoginAsync(string email, string password, CancellationToken ct);
}
