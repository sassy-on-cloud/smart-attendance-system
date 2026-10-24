using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SmartAttendance.API.Security;

// Bounded per-process account throttle. Reservations prevent simultaneous guesses bypassing the limit.
public sealed class LoginAttemptThrottle(IOptions<LoginSecurityOptions> options, TimeProvider clock) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AttemptState> _attempts = [];
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private const int MaximumAccounts = 5000;
    public sealed class AttemptState(DateTimeOffset until)
    {
        public DateTimeOffset Until { get; } = until;
        public int Count { get; set; }
    }
    public sealed record Reservation(string Key, AttemptState? State, int RetryAfterSeconds);

    public Reservation Reserve(string normalizedEmail)
    {
        var key = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(normalizedEmail)));
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (_attempts.TryGetValue(key, out var existing) && existing.Until <= now) _attempts.Remove(key);
            if (!_attempts.TryGetValue(key, out var state))
            {
                if (_attempts.Count >= MaximumAccounts)
                {
                    foreach (var expired in _attempts.Where(pair => pair.Value.Until <= now).Select(pair => pair.Key).ToArray()) _attempts.Remove(expired);
                    if (_attempts.Count >= MaximumAccounts) return new(key, null, 1);
                }
                state = new(now.AddSeconds(options.Value.AccountWindowSeconds)); _attempts[key] = state;
            }
            if (state.Count >= options.Value.AccountAttemptLimit)
                return new(key, null, Math.Max(1, (int)Math.Ceiling((state.Until - now).TotalSeconds)));
            state.Count++;
            return new(key, state, 0);
        }
    }
    public void Succeeded(Reservation reservation)
    {
        lock (_gate)
            if (_attempts.TryGetValue(reservation.Key, out var state) && ReferenceEquals(state, reservation.State)) _attempts.Remove(reservation.Key);
    }
    public void Dispose() { lock (_gate) { _attempts.Clear(); CryptographicOperations.ZeroMemory(_key); } }
}
