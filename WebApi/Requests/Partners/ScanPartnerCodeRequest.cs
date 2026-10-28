using System.Text.Json;

namespace Cable.Requests.Partners;

/// <summary>Optional JSON body of ScanPartnerCode (Sprint 5 promos): { "code": "PTR-8KQ2M4", "userPromoId": 31 }.</summary>
public record ScanPartnerCodeRequest(string? Code, int? UserPromoId)
{
    /// <summary>
    /// Reads the body only when it is JSON; anything else (the older ?code= calls with no body) gives null, so no
    /// existing client can get a 415 or a parse error from the new optional body.
    /// </summary>
    public static async Task<ScanPartnerCodeRequest?> TryReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
            return null;
        try
        {
            return await request.ReadFromJsonAsync<ScanPartnerCodeRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;   // empty or not an object
        }
    }
}
