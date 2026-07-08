using Indexarr.Web.Data;
using Indexarr.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Indexarr.Web.Services;

public sealed class AuthService
{
    private readonly IndexarrDbContext _dbContext;
    private readonly PasswordHasher<AdminUserEntity> _passwordHasher = new();

    public AuthService(IndexarrDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> HasUsersAsync(CancellationToken cancellationToken = default)
        => _dbContext.AdminUsers.AnyAsync(cancellationToken);

    public async Task<AdminUserEntity?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        => await _dbContext.AdminUsers.SingleOrDefaultAsync(x => x.Username == username, cancellationToken);

    public async Task<AdminUserEntity> RegisterFirstUserAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (await HasUsersAsync(cancellationToken))
        {
            throw new InvalidOperationException("Registration is disabled. Users already exist.");
        }

        var user = new AdminUserEntity
        {
            Username = username.Trim(),
            Role = "admin",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        _dbContext.AdminUsers.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<AdminUserEntity?> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await GetByUsernameAsync(username.Trim(), cancellationToken);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return null;
        }

        user.LastLoginAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.AdminUsers.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            return false;
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
