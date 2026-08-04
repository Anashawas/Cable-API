using System.Text.Json;
using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints;

/// <summary>One changed field in an update request: old value (snapshotted at submit time) vs requested new value.</summary>
public record UpdateRequestChangeDto(string Field, object? OldValue, object? NewValue);

/// <summary>
/// Builds the field-by-field diff (changes / attachments / riskFlags) for
/// station update requests. Old values come from the OldValuesJson snapshot
/// taken at submit time — never from the live station.
/// </summary>
public static class UpdateRequestDiffHelper
{
    /// <summary>Snapshot the station's current values for exactly the fields the request changes.</summary>
    public static string? BuildOldValuesSnapshot(ChargingPointUpdateRequest req, ChargingPoint cp)
    {
        var old = new Dictionary<string, object?>();

        if (req.Name != null) old["name"] = cp.Name;
        if (req.Note != null) old["note"] = cp.Note;
        if (req.CountryName != null) old["countryName"] = cp.CountryName;
        if (req.CityName != null) old["cityName"] = cp.CityName;
        if (req.Phone != null) old["phone"] = cp.Phone;
        if (req.MethodPayment != null) old["methodPayment"] = cp.MethodPayment;
        if (req.Price.HasValue) old["price"] = cp.Price;
        if (req.FromTime != null) old["fromTime"] = cp.FromTime;
        if (req.ToTime != null) old["toTime"] = cp.ToTime;
        if (req.ChargerSpeed.HasValue) old["chargerSpeed"] = cp.ChargerSpeed;
        if (req.ChargersCount.HasValue) old["chargersCount"] = cp.ChargersCount;
        // store BOTH coordinates when either moves, so the diff can compute distance
        if (req.Latitude.HasValue || req.Longitude.HasValue)
        {
            old["latitude"] = cp.Latitude;
            old["longitude"] = cp.Longitude;
        }
        if (req.StatusId.HasValue) old["statusId"] = cp.StatusId;
        if (req.OwnerPhone != null) old["ownerPhone"] = cp.OwnerPhone;
        if (req.Service != null) old["service"] = cp.Service;
        if (req.OfferDescription != null) old["offerDescription"] = cp.OfferDescription;
        if (req.Address != null) old["address"] = cp.Address;

        return old.Count == 0 ? null : JsonSerializer.Serialize(old);
    }

    public sealed record Diff(
        List<UpdateRequestChangeDto> Changes,
        List<string> Attachments,
        List<string> RiskFlags);

    /// <summary>
    /// Lookup names needed to render diffs (batched by the caller so list
    /// endpoints do 2 queries total, not 2 per row).
    /// </summary>
    public sealed record NameLookups(
        IReadOnlyDictionary<int, string> PlugTypeNames,
        IReadOnlyDictionary<int, string> StatusNames);

    /// <summary>Loads the plug-type / status names referenced by any of the given requests.</summary>
    public static async Task<NameLookups> LoadNamesAsync(
        IApplicationDbContext db,
        IEnumerable<ChargingPointUpdateRequest> requests,
        CancellationToken ct)
    {
        var plugIds = new HashSet<int>();
        var statusIds = new HashSet<int>();

        foreach (var r in requests)
        {
            foreach (var id in ParseIds(r.OldPlugTypeIds)) plugIds.Add(id);
            foreach (var id in ParseIds(r.NewPlugTypeIds)) plugIds.Add(id);
            if (r.StatusId.HasValue) statusIds.Add(r.StatusId.Value);
            var oldStatus = ReadSnapshotInt(r.OldValuesJson, "statusId");
            if (oldStatus.HasValue) statusIds.Add(oldStatus.Value);
        }

        var plugNames = plugIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.PlugTypes.Where(p => plugIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var statusNames = statusIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Statuses.Where(s => statusIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return new NameLookups(plugNames, statusNames);
    }

    /// <summary>Builds changes[] + attachments + riskFlags for one request.</summary>
    public static Diff Build(
        ChargingPointUpdateRequest req,
        NameLookups names,
        IUploadFileService files)
    {
        var snapshot = string.IsNullOrEmpty(req.OldValuesJson)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(req.OldValuesJson)
              ?? new Dictionary<string, JsonElement>();

        var changes = new List<UpdateRequestChangeDto>();
        var riskFlags = new List<string>();

        object? Old(string field) => snapshot.TryGetValue(field, out var v) ? v : null;
        void Add(string field, object? newValue) => changes.Add(new UpdateRequestChangeDto(field, Old(field), newValue));

        if (req.Name != null) { Add("name", req.Name); riskFlags.Add("name_changed"); }
        if (req.Note != null) Add("note", req.Note);
        if (req.CountryName != null) Add("countryName", req.CountryName);
        if (req.CityName != null) Add("cityName", req.CityName);
        if (req.Phone != null) { Add("phone", req.Phone); riskFlags.Add("contact_phone_changed"); }
        if (req.MethodPayment != null) Add("methodPayment", req.MethodPayment);
        if (req.Price.HasValue) Add("price", req.Price);
        if (req.FromTime != null) Add("fromTime", req.FromTime);
        if (req.ToTime != null) Add("toTime", req.ToTime);
        if (req.ChargerSpeed.HasValue) Add("chargerSpeed", req.ChargerSpeed);
        if (req.ChargersCount.HasValue) Add("chargersCount", req.ChargersCount);

        if (req.Latitude.HasValue || req.Longitude.HasValue)
        {
            var oldLat = ReadSnapshotDouble(snapshot, "latitude");
            var oldLng = ReadSnapshotDouble(snapshot, "longitude");
            var newLat = req.Latitude ?? oldLat;
            var newLng = req.Longitude ?? oldLng;

            if (req.Latitude.HasValue) Add("latitude", req.Latitude);
            if (req.Longitude.HasValue) Add("longitude", req.Longitude);

            if (oldLat.HasValue && oldLng.HasValue && newLat.HasValue && newLng.HasValue)
            {
                var meters = HaversineMeters(oldLat.Value, oldLng.Value, newLat.Value, newLng.Value);
                riskFlags.Add($"location_moved ({Math.Round(meters)} m)");
            }
            else
            {
                riskFlags.Add("location_moved");
            }
        }

        if (req.StatusId.HasValue)
        {
            var oldStatusId = ReadSnapshotInt(req.OldValuesJson, "statusId");
            changes.Add(new UpdateRequestChangeDto("status",
                oldStatusId.HasValue ? names.StatusNames.GetValueOrDefault(oldStatusId.Value, oldStatusId.Value.ToString()) : null,
                names.StatusNames.GetValueOrDefault(req.StatusId.Value, req.StatusId.Value.ToString())));
        }

        if (req.OwnerPhone != null) { Add("ownerPhone", req.OwnerPhone); riskFlags.Add("owner_contact_changed"); }
        if (req.Service != null) Add("service", req.Service);
        if (req.OfferDescription != null) Add("offerDescription", req.OfferDescription);
        if (req.Address != null) Add("address", req.Address);

        if (!string.IsNullOrEmpty(req.NewPlugTypeIds))
        {
            changes.Add(new UpdateRequestChangeDto("plugTypes",
                ParseIds(req.OldPlugTypeIds).Select(id => names.PlugTypeNames.GetValueOrDefault(id, id.ToString())).ToList(),
                ParseIds(req.NewPlugTypeIds).Select(id => names.PlugTypeNames.GetValueOrDefault(id, id.ToString())).ToList()));
        }

        if (!string.IsNullOrEmpty(req.NewIcon))
        {
            changes.Add(new UpdateRequestChangeDto("icon",
                !string.IsNullOrEmpty(req.OldIcon) ? files.GetFilePath(UploadFileFolders.CableChargingPoint, req.OldIcon) : null,
                files.GetFilePath(UploadFileFolders.CableChargingPoint, req.NewIcon)));
        }

        var deletions = req.AttachmentChanges
            .Where(a => a.AttachmentAction == AttachmentAction.Delete && a.ExistingAttachment != null)
            .Select(a => files.GetFilePath(UploadFileFolders.CableAttachments, a.ExistingAttachment!.FileName))
            .ToList();
        if (deletions.Count > 0)
            changes.Add(new UpdateRequestChangeDto("attachmentsToDelete", deletions, null));

        // newly uploaded photos attached to this request
        var attachments = req.AttachmentChanges
            .Where(a => a.AttachmentAction == AttachmentAction.Add && !string.IsNullOrEmpty(a.FileName))
            .Select(a => files.GetFilePath(UploadFileFolders.CableAttachments, a.FileName!))
            .ToList();

        return new Diff(changes, attachments, riskFlags);
    }

    private static List<int> ParseIds(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<int>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static int? ReadSnapshotInt(string? json, string field)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (dict != null && dict.TryGetValue(field, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt32();
        }
        catch (JsonException) { }
        return null;
    }

    private static double? ReadSnapshotDouble(Dictionary<string, JsonElement> snapshot, string field)
        => snapshot.TryGetValue(field, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
