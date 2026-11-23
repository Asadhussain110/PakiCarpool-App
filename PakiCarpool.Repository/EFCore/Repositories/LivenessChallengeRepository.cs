using Microsoft.EntityFrameworkCore;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Repository.EFCore;

namespace PakiCarpool.Repository.EFCore.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ILivenessChallengeRepository"/>.
/// </summary>
public class LivenessChallengeRepository : ILivenessChallengeRepository
{
    private readonly PakiCarpoolDbContext _context;

    public LivenessChallengeRepository(PakiCarpoolDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task InsertChallengeAsync(LivenessChallenge challenge, CancellationToken cancellationToken = default)
    {
        _context.LivenessChallenges.Add(challenge);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LivenessChallenge?> GetChallengeByNonceAsync(
        string nonce,
        CancellationToken cancellationToken = default)
    {
        return await _context.LivenessChallenges
            .AsNoTracking()
            .FirstOrDefaultAsync(lc => lc.Nonce == nonce, cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkChallengeUsedAsync(LivenessChallenge challenge)
    {
        challenge.IsUsed = true;
        _context.LivenessChallenges.Update(challenge);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> TryConsumeChallengeAsync(
        string nonce,
        Guid driverId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var affectedRows = await _context.LivenessChallenges
            .Where(challenge => challenge.Nonce == nonce
                                && challenge.DriverId == driverId
                                && !challenge.IsUsed
                                && challenge.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(challenge => challenge.IsUsed, true),
                cancellationToken);

        return affectedRows == 1;
    }

    /// <inheritdoc />
    public async Task CleanupChallengesAsync()
    {
        var now = DateTime.UtcNow;
        var stale = await _context.LivenessChallenges
            .Where(lc => lc.IsUsed || lc.ExpiresAt < now)
            .ToListAsync();

        _context.LivenessChallenges.RemoveRange(stale);
        await _context.SaveChangesAsync();
    }
}
