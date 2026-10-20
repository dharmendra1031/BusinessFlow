using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Services;

public static class ConcurrencyTokens
{
    public static bool TryParse(string? token, out byte[] version)
    {
        version = new byte[8];
        return token is not null && token.Length == 12 &&
            Convert.TryFromBase64String(token, version, out var written) && written == 8;
    }

    public static bool Apply<TEntity>(AppDbContext db, TEntity entity, string? token) where TEntity : class
    {
        if (!TryParse(token, out var version)) return false;
        var property = db.Entry(entity).Property<byte[]>("RowVersion");
        if (!property.CurrentValue!.SequenceEqual(version)) throw new DbUpdateConcurrencyException();
        property.OriginalValue = version;
        return true;
    }
}
