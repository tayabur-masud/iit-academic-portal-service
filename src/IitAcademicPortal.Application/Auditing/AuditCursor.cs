using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// The opaque keyset cursor for newest-first paging. It carries the last returned event's time and ID, so
/// concurrent inserts never cause a duplicate or an omission across pages.
/// </summary>
public static class AuditCursor
{
    public static string Encode(DateTime occurredAtUtc, Guid eventId) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{occurredAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)}:{eventId:N}"));

    public static bool TryDecode(string? cursor, out DateTime occurredAtUtc, out Guid eventId)
    {
        occurredAtUtc = default;
        eventId = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 200)
        {
            return false;
        }

        try
        {
            var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            var parts = text.Split(':');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTime.MinValue.Ticks
                || ticks > DateTime.MaxValue.Ticks
                || !Guid.TryParseExact(parts[1], "N", out eventId))
            {
                return false;
            }

            occurredAtUtc = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
