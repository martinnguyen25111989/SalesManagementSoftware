using Pos.Application.Invoicing.Abstractions;
using Pos.Application.Pricing;
using Pos.Domain.Common;
using Pos.Domain.Organization;
using Pos.Domain.Sales;

namespace Pos.Application.Invoicing;

/// <summary>
/// Dựng <see cref="EInvoiceRequest"/> từ Order (mapping POS→HĐĐT, B11-A.4). Số tiền từng dòng và
/// tổng tách theo thuế suất tính lại bằng <see cref="OrderCalculator"/> để khớp tuyệt đối B5/B13
/// (lệch 1đ là CQT từ chối). TransactionId = Order.Id làm idempotency key.
/// </summary>
public static class EInvoiceRequestBuilder
{
    /// <param name="items">VariantId → (tên hàng, đơn vị tính) để điền items[].</param>
    public static EInvoiceRequest Build(
        Order order,
        IReadOnlyDictionary<Guid, (string Name, string Unit)> items,
        Store store,
        EInvoiceType type,
        string buyerName,
        string? buyerTaxCode = null,
        string? buyerPhone = null,
        string? buyerAddress = null,
        string? paymentMethod = null,
        string? note = null)
    {
        var lines = order.Lines.ToList();

        // CK tổng suy ra = DiscountTotal − tổng CK dòng (giống CreateOrderHandler.ToResult).
        decimal orderDiscount = order.DiscountTotal - lines.Sum(l => l.LineDiscount);
        var calc = OrderCalculator.Calculate(
            lines.Select(l => new OrderCalcLine(l.Qty, l.UnitPrice, l.LineDiscount, l.TaxRate)).ToList(),
            orderDiscount);

        var invoiceItems = new List<EInvoiceItem>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var r = calc.Lines[i];
            var meta = items.TryGetValue(l.VariantId, out var m) ? m : (Name: "Hàng hóa", Unit: "Cái");
            invoiceItems.Add(new EInvoiceItem(
                ItemName: meta.Name,
                UnitName: meta.Unit,
                Quantity: l.Qty,
                UnitPrice: l.UnitPrice,
                VatPercent: EInvoiceTaxMap.Percent(l.TaxRate),
                VatRateLabel: EInvoiceTaxMap.Label(l.TaxRate),
                AmountWithoutVat: r.Taxable,
                VatAmount: r.Tax));
        }

        // Tổng thuế tách theo từng thuế suất.
        var vatByRate = lines
            .Select((l, i) => (l.TaxRate, calc.Lines[i]))
            .GroupBy(x => x.TaxRate)
            .Select(g => new EInvoiceVatLine(
                EInvoiceTaxMap.Percent(g.Key),
                EInvoiceTaxMap.Label(g.Key),
                g.Sum(x => x.Item2.Taxable),
                g.Sum(x => x.Item2.Tax)))
            .OrderBy(v => v.VatPercent)
            .ToList();

        decimal totalWithoutVat = calc.Lines.Sum(l => l.Taxable);
        decimal totalVat = calc.Lines.Sum(l => l.Tax);
        decimal totalAmount = totalWithoutVat + totalVat; // tổng HĐ = tiền hàng + thuế (B13)

        return new EInvoiceRequest(
            TransactionId: order.Id,
            SellerTaxCode: store.TaxCode ?? string.Empty,
            TemplateCode: store.InvoiceTemplateCode,
            Serial: store.InvoiceSerial,
            Type: type,
            BuyerName: string.IsNullOrWhiteSpace(buyerName) ? "Khách lẻ" : buyerName,
            BuyerTaxCode: buyerTaxCode,
            BuyerPhone: buyerPhone,
            BuyerAddress: buyerAddress,
            Items: invoiceItems,
            VatByRate: vatByRate,
            TotalAmountWithoutVat: totalWithoutVat,
            TotalVat: totalVat,
            TotalAmount: totalAmount,
            AmountInWords: VietnameseMoney.ToWords(totalAmount),
            PaymentMethod: paymentMethod,
            Note: note);
    }
}
