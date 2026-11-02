using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace ITSResFix.Staff.Services;

/// <summary>One sign-in encryption key, stored as XML in the database (table data_protection_key).</summary>
[Table("data_protection_key")]
public class DataProtectionKeyRow : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("friendly_name")]
    public string FriendlyName { get; set; } = string.Empty;

    [Column("xml")]
    public string Xml { get; set; } = string.Empty;

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Keeps ASP.NET's data-protection keys (they encrypt the sign-in cookie and form tokens) in the database instead of on disk.
/// Free hosting has no persistent disk and wipes the file system on every restart, which would sign everyone out each time.
/// The table is only reachable with the server's service-role key (row level security, no public access).
/// </summary>
public class SupabaseXmlRepository : IXmlRepository
{
    private readonly Supabase.Client _db;

    public SupabaseXmlRepository(Supabase.Client db) => _db = db;

    // The interface is synchronous; the key ring is read at start-up and re-read rarely, so blocking here is fine.
    public IReadOnlyCollection<XElement> GetAllElements() =>
        _db.From<DataProtectionKeyRow>().Get().GetAwaiter().GetResult().Models
            .Select(r => XElement.Parse(r.Xml)).ToList();

    public void StoreElement(XElement element, string friendlyName) =>
        _db.From<DataProtectionKeyRow>().Insert(new DataProtectionKeyRow
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting)
        }).GetAwaiter().GetResult();
}
