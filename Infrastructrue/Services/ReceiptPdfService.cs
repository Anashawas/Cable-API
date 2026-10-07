using Application.Common.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructrue.Services;

/// <summary>
/// Cable-branded A5 receipt, bilingual: English label on the left, value in the
/// middle, Arabic label on the right. Arial is used because it ships with
/// Arabic glyphs on every Windows host this API runs on; QuestPDF's bundled
/// default font does not, and would print the Arabic column as boxes.
/// </summary>
public class ReceiptPdfService : IReceiptPdfService
{
    private const string Brand = "#1565C0";
    private const string Font = "Arial";

    static ReceiptPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // QuestPDF only knows its bundled Lato unless told to look at the host's
        // fonts. The receipt needs Arabic glyphs, which Lato lacks and Arial has.
        QuestPDF.Settings.UseEnvironmentFonts = true;
    }

    public byte[] Generate(ReceiptData d)
    {
        try
        {
            return Render(d, Font);
        }
        catch (Exception ex) when (ex.Message.Contains("font", StringComparison.OrdinalIgnoreCase))
        {
            // A host without Arial still issues a receipt: the Latin text renders
            // and only the Arabic column degrades, rather than the payment losing
            // its receipt entirely.
            return Render(d, "Lato");
        }
    }

    private static byte[] Render(ReceiptData d, string font)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily(font));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Cable EV").FontSize(20).Bold().FontColor(Brand);
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("Payment Receipt").FontSize(12).SemiBold();
                            c.Item().AlignRight().Text("إيصال دفع").FontSize(12).SemiBold();
                        });
                    });
                    col.Item().PaddingTop(2).Text($"Ref: {d.ReferenceNo}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Brand);
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(4);
                            c.RelativeColumn(3);
                        });

                        void Row(string en, string value, string ar)
                        {
                            table.Cell().PaddingVertical(5).PaddingRight(6).Text(en).FontColor(Colors.Grey.Darken2);
                            table.Cell().PaddingVertical(5).Text(value).SemiBold();
                            table.Cell().PaddingVertical(5).PaddingLeft(6).AlignRight().Text(ar).FontColor(Colors.Grey.Darken2);
                        }

                        var plan = d.PlanMonths is > 0 ? $"{d.PlanMonths} month(s)" : "—";
                        Row("Item", d.EntityLabelEn, d.EntityLabelAr);
                        Row("Name", d.EntityName, "الاسم");
                        Row("Amount", $"{d.Amount:N3} {d.Currency}", "المبلغ");
                        Row("Method", d.MethodEn, d.MethodAr);
                        Row("Paid on", d.PaidDateLocal.ToString("yyyy-MM-dd"), "تاريخ الدفع");
                        Row("Plan", plan, "الخطة");
                        Row("Period from", d.PeriodStartLocal.ToString("yyyy-MM-dd"), "بداية الاشتراك");
                        Row("Period to", d.PeriodEndLocal.ToString("yyyy-MM-dd"), "نهاية الاشتراك");
                        Row("Paid by", d.PayerName, "الدافع");
                        if (!string.IsNullOrWhiteSpace(d.PayerPhone))
                            Row("Phone", d.PayerPhone!, "الهاتف");
                        if (!string.IsNullOrWhiteSpace(d.Note))
                            Row("Note", d.Note!, "ملاحظة");
                    });

                    col.Item().PaddingTop(18).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                    col.Item().PaddingTop(8).Text("Thank you for being a Cable partner.").FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().AlignRight().Text("شكراً لكونك شريكاً في Cable.").FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Medium));
                    t.Span("Generated by Cable EV · ");
                    t.Span(d.IssuedAtLocal.ToString("yyyy-MM-dd HH:mm"));
                    t.Span(" · support@cable-app.com");
                });
            });
        });

        return document.GeneratePdf();
    }
}
