namespace Application.Ocpp.LiveCharging;

/// <summary>A generated file for a download endpoint (PDF / CSV).</summary>
public record LiveChargingFile(byte[] Content, string ContentType, string FileName);

/// <summary>One price line on the receipt: "Off-peak 10.200 kWh × 183 fils = 1.867 JOD".</summary>
public record SessionReceiptLine(string NameEn, string NameAr, decimal Kwh, int PriceFils, int Fils);

/// <summary>Everything printed on a session receipt (§13.2). Dates are already Jordan local time.</summary>
public record SessionReceiptData(
    string ReceiptNo,
    DateTime IssuedAtLocal,
    string StationName,
    string? StationAddress,
    string ChargerName,
    int ConnectorId,
    string? PlugTypeName,
    decimal? PowerKw,
    string? DriverName,
    string? CarName,
    DateTime StartedAtLocal,
    DateTime? StoppedAtLocal,
    int DurationSec,
    int? SocStartPercent,
    int? SocEndPercent,
    decimal? EnergyKwh,
    List<SessionReceiptLine> Lines,
    int? TotalFils,
    string? StopReasonEn,
    string? StopReasonAr,
    /// <summary>Encoded in the QR: the public verification page.</summary>
    string VerifyUrl);

/// <summary>One plug sticker (§10.5).</summary>
public record ConnectorSticker(string Code, string Link, string StationName, string ChargerNameEn, string ChargerNameAr, int ConnectorId, string? PlugTypeName, decimal? PowerKw);

public record StickerSheetData(string StationName, DateTime GeneratedAtLocal, List<ConnectorSticker> Stickers);

/// <summary>Renders the live-charging PDFs (Cable brand, bilingual, QR codes). Pure: no I/O, no storage.</summary>
public interface ILiveChargingPdfService
{
    byte[] RenderSessionReceipt(SessionReceiptData data);
    byte[] RenderStickerSheet(StickerSheetData data);
}
