using System.Globalization;
using Application.Ocpp.LiveCharging;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructrue.Services;

/// <summary>
/// Live-charging PDFs in the style of <see cref="ReceiptPdfService"/> (Cable blue, Arial for the Arabic glyphs,
/// English label · value · Arabic label): the session receipt (A5, §13.2) and the plug sticker sheet (A4, §10.5).
/// QR codes are drawn with QRCoder's PNG writer (no System.Drawing).
/// </summary>
public class LiveChargingPdfService : ILiveChargingPdfService
{
    private const string Brand = "#1565C0";
    private const string Font = "Arial";

    static LiveChargingPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;
    }

    public byte[] RenderSessionReceipt(SessionReceiptData data) => WithFontFallback(font => Receipt(data, font));

    public byte[] RenderStickerSheet(StickerSheetData data) => WithFontFallback(font => Stickers(data, font));

    private static byte[] WithFontFallback(Func<string, byte[]> render)
    {
        try
        {
            return render(Font);
        }
        catch (Exception ex) when (ex.Message.Contains("font", StringComparison.OrdinalIgnoreCase))
        {
            // A host without Arial still gets the document; only the Arabic degrades.
            return render("Lato");
        }
    }

    internal static byte[] Qr(string text, int pixelsPerModule = 12)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }

    private static string Num(decimal value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    internal static string Duration(int seconds, bool arabic)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var h = (int)t.TotalHours;
        return arabic
            ? (h > 0 ? $"{h} س {t.Minutes} د" : $"{t.Minutes} د {t.Seconds} ث")
            : (h > 0 ? $"{h} h {t.Minutes:00} min" : $"{t.Minutes} min {t.Seconds:00} s");
    }

    // -----------------------------------------------------------------------
    // Session receipt
    // -----------------------------------------------------------------------

    private static byte[] Receipt(SessionReceiptData d, string font)
    {
        var qr = Qr(d.VerifyUrl, 8);
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(26);
                page.DefaultTextStyle(t => t.FontSize(9.5f).FontFamily(font));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Cable EV").FontSize(20).Bold().FontColor(Brand);
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("Charging receipt").FontSize(12).SemiBold();
                            c.Item().AlignRight().Text("إيصال شحن").FontSize(12).SemiBold();
                        });
                    });
                    col.Item().PaddingTop(2).Row(row =>
                    {
                        row.RelativeItem().Text($"No. {d.ReceiptNo}").FontSize(9).FontColor(Colors.Grey.Darken1);
                        row.RelativeItem().AlignRight().Text(d.IssuedAtLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(Brand);
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(5);
                            c.RelativeColumn(3);
                        });

                        void Row(string en, string value, string ar)
                        {
                            table.Cell().PaddingVertical(3).PaddingRight(6).Text(en).FontColor(Colors.Grey.Darken2);
                            table.Cell().PaddingVertical(3).Text(value).SemiBold();
                            table.Cell().PaddingVertical(3).PaddingLeft(6).AlignRight().Text(ar).FontColor(Colors.Grey.Darken2);
                        }

                        var plug = $"{d.ConnectorId}" + (d.PlugTypeName is null ? "" : " · " + d.PlugTypeName)
                                   + (d.PowerKw is decimal kw ? " · " + Num(kw, "0.#") + " kW" : "");
                        Row("Station", d.StationName, "المحطة");
                        if (!string.IsNullOrWhiteSpace(d.StationAddress)) Row("Address", d.StationAddress!, "العنوان");
                        Row("Charger", d.ChargerName, "الشاحن");
                        Row("Plug", plug, "المقبس");
                        if (!string.IsNullOrWhiteSpace(d.DriverName)) Row("Driver", d.DriverName!, "السائق");
                        if (!string.IsNullOrWhiteSpace(d.CarName)) Row("Car", d.CarName!, "السيارة");
                        Row("Start", d.StartedAtLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "البداية");
                        if (d.StoppedAtLocal is DateTime end) Row("End", end.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "النهاية");
                        Row("Duration", Duration(d.DurationSec, false), "المدة");
                        if (d.SocStartPercent is int from && d.SocEndPercent is int to)
                            Row("Battery", $"{from}% → {to}% (+{to - from}%)", "البطارية");
                        Row("Energy", d.EnergyKwh is decimal e ? Num(e, "0.000") + " kWh" : "—", "الطاقة");
                        if (!string.IsNullOrWhiteSpace(d.StopReasonEn))
                            Row("Stopped", d.StopReasonEn!, d.StopReasonAr ?? "سبب التوقف");
                    });

                    // Price lines: "Off-peak 10.200 kWh × 183 fils = 1.867 JOD".
                    col.Item().PaddingTop(10).Text("Energy and cost · الطاقة والكلفة").SemiBold().FontColor(Brand);
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(4);
                            c.RelativeColumn(4);
                            c.RelativeColumn(3);
                        });
                        foreach (var line in d.Lines)
                        {
                            table.Cell().PaddingVertical(2).Text($"{line.NameEn} · {line.NameAr}");
                            table.Cell().PaddingVertical(2).Text($"{Num(line.Kwh, "0.000")} kWh × {line.PriceFils} fils");
                            table.Cell().PaddingVertical(2).AlignRight().Text($"{Num(line.Fils / 1000m, "0.000")} JOD");
                        }
                        table.Cell().ColumnSpan(2).PaddingTop(4).BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1).Text("Total · المجموع").Bold();
                        table.Cell().PaddingTop(4).BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1).AlignRight()
                            .Text(d.TotalFils is int fils ? Num(fils / 1000m, "0.000") + " JOD" : "—").Bold();
                    });

                    col.Item().PaddingTop(10).Background(Colors.Grey.Lighten4).Padding(6).Column(note =>
                    {
                        note.Item().Text("Estimated cost — pay the station directly.").FontSize(9);
                        note.Item().AlignRight().Text("كلفة تقديرية — الدفع للمحطة مباشرة").FontSize(9);
                    });

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.ConstantItem(72).Image(qr);
                        row.RelativeItem().PaddingLeft(8).AlignMiddle().Column(c =>
                        {
                            c.Item().Text("Scan to verify this receipt · امسح للتحقق من الإيصال").FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(d.VerifyUrl).FontSize(8).FontColor(Brand);
                        });
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Medium));
                    t.Span("Generated by Cable EV · support@cable-app.com");
                });
            });
        });
        return document.GeneratePdf();
    }

    // -----------------------------------------------------------------------
    // Sticker sheet: 2 × 3 stickers per A4 page
    // -----------------------------------------------------------------------

    private static byte[] Stickers(StickerSheetData d, string font)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily(font));

                page.Header().PaddingBottom(6).Row(row =>
                {
                    row.RelativeItem().Text(d.StationName).SemiBold().FontColor(Brand);
                    row.RelativeItem().AlignRight().Text(d.GeneratedAtLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).FontSize(8).FontColor(Colors.Grey.Darken1);
                });

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.RelativeColumn();
                    });

                    foreach (var s in d.Stickers)
                    {
                        var qr = Qr(s.Link, 12);
                        var plug = $"Plug {s.ConnectorId}" + (s.PlugTypeName is null ? "" : " · " + s.PlugTypeName)
                                   + (s.PowerKw is decimal kw ? " · " + Num(kw, "0.#") + " kW" : "");
                        table.Cell().Padding(6).Border(1).BorderColor(Brand).Column(card =>
                        {
                            card.Item().Background(Brand).PaddingVertical(4).PaddingHorizontal(8).Row(r =>
                            {
                                r.RelativeItem().Text("Cable").FontSize(14).Bold().FontColor(Colors.White);
                                r.RelativeItem().AlignRight().AlignMiddle().Text("امسح واشحن · Scan to charge").FontSize(8.5f).FontColor(Colors.White);
                            });
                            card.Item().PaddingTop(8).AlignCenter().Width(120).Image(qr);
                            card.Item().PaddingTop(4).AlignCenter().Text(s.Code).FontSize(20).Bold().LetterSpacing(0.08f);
                            card.Item().PaddingTop(4).PaddingHorizontal(8).AlignCenter().Text(s.StationName).FontSize(9.5f).SemiBold();
                            card.Item().PaddingHorizontal(8).AlignCenter().Text(s.ChargerNameEn).FontSize(9);
                            if (s.ChargerNameAr != s.ChargerNameEn)
                                card.Item().PaddingHorizontal(8).AlignCenter().Text(s.ChargerNameAr).FontSize(9);
                            card.Item().PaddingHorizontal(8).AlignCenter().Text(plug).FontSize(9.5f).SemiBold();
                            card.Item().PaddingHorizontal(8).AlignCenter().Text("المقبس " + s.ConnectorId).FontSize(9);
                            card.Item().PaddingTop(4).PaddingBottom(8).PaddingHorizontal(8).AlignCenter()
                                .Text("Or type the code in the Cable app · أو اكتب الكود بتطبيق كيبل").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Colors.Grey.Medium));
                    t.Span("Cable EV · cable-app.com · ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });
        return document.GeneratePdf();
    }
}
