using AtmCash.Application.Abstractions.Persistence;
using AtmCash.Domain.Atms;

namespace AtmCash.Application.Features.Withdrawals;

internal static class QuoteHousekeeping
{
    /// <summary>Expires the ATM's pending quotes whose validity has passed. The ATM must be locked by the caller.</summary>
    public static async Task<int> ExpireStaleQuotesAsync(Atm atm, IQuoteRepository quotes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expired = 0;
        foreach (var quote in await quotes.ListPendingByAtmAsync(atm.Id, cancellationToken))
        {
            if (quote.IsExpiredAt(now))
            {
                quote.Expire(atm, now);
                expired++;
            }
        }

        return expired;
    }
}
