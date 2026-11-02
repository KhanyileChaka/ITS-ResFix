using ITSResFix.Staff.Helpers;
using ITSResFix.Staff.Models.Db;
using Microsoft.Extensions.Caching.Memory;
using static Supabase.Postgrest.Constants;

namespace ITSResFix.Staff.Services;

public record ChatMessage(long Id, string Sender, string? Name, string Body, string? PhotoUrl, string Time, DateTime CreatedAt, bool Seen);
public record ChatSnapshot(List<ChatMessage> Messages, string? TypingName);

/// <summary>
/// The live side of the conversation between staff and a student: who is typing right now, and "seen" marks.
/// Typing lives in memory for a few seconds (it is gone on a restart, which is fine); seen marks are saved on the message.
/// </summary>
public class ChatService
{
    private static readonly TimeSpan TypingFor = TimeSpan.FromSeconds(6);
    private readonly IMemoryCache _typing;
    private readonly Supabase.Client _db;

    public ChatService(IMemoryCache cache, Supabase.Client db) { _typing = cache; _db = db; }

    public const string Staff = "staff";
    public const string Student = "student";

    /// <summary>Records that <paramref name="side"/> is typing on this request right now.</summary>
    public void Typing(int complaintId, string side, string name) =>
        _typing.Set(Key(complaintId, side), string.IsNullOrWhiteSpace(name) ? side : name, TypingFor);

    /// <summary>Who on the other side is typing, or null.</summary>
    public string? TypingOf(int complaintId, string side) =>
        _typing.TryGetValue(Key(complaintId, side), out string? name) ? name : null;

    /// <summary>True at most once every 2 seconds per conversation side, so typing pings cannot be used to hammer the database.</summary>
    public bool AllowPing(int complaintId, string side)
    {
        var key = $"typing-gate|{complaintId}|{side}";
        if (_typing.TryGetValue(key, out _)) return false;
        _typing.Set(key, true, TimeSpan.FromSeconds(2));
        return true;
    }

    public void StopTyping(int complaintId, string side) => _typing.Remove(Key(complaintId, side));

    /// <summary>
    /// The whole thread as <paramref name="viewer"/> sees it. When <paramref name="markSeen"/> is set (the conversation is open on
    /// screen) the other side's unread messages get their seen mark.
    /// </summary>
    public async Task<ChatSnapshot> ThreadAsync(int complaintId, string viewer, bool markSeen)
    {
        var other = viewer == Staff ? Student : Staff;
        var messages = (await _db.From<ComplaintMessage>().Filter("complaint_id", Operator.Equals, complaintId.ToString())
            .Order("created_at", Ordering.Ascending).Get()).Models;

        if (markSeen && messages.Any(m => m.Sender == other && m.ReadAt is null))
        {
            var now = DateTime.UtcNow;
            await _db.From<ComplaintMessage>()
                .Filter("complaint_id", Operator.Equals, complaintId.ToString())
                .Filter("sender", Operator.Equals, other)
                .Filter("read_at", Operator.Is, "null")
                .Set(m => m.ReadAt!, now).Update();
            foreach (var m in messages.Where(m => m.Sender == other && m.ReadAt is null)) m.ReadAt = now;
        }

        var list = messages.Select(m => new ChatMessage(m.Id, m.Sender, m.SenderName, m.Body,
            DisplayHelper.IsHttpsUrl(m.PhotoUrl) ? m.PhotoUrl : null,
            ((DateTime?)m.CreatedAt).ToSaTime("dd MMM HH:mm"), m.CreatedAt.AsUtc(), m.ReadAt is not null)).ToList();
        return new ChatSnapshot(list, TypingOf(complaintId, other));
    }

    private static string Key(int complaintId, string side) => $"typing|{complaintId}|{side}";
}
