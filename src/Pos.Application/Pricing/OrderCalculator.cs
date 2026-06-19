using Pos.Domain.Common;

namespace Pos.Application.Pricing;

/// <summary>Một dòng đầu vào để tính tiền.</summary>
public sealed record OrderCalcLine(decimal Qty, decimal UnitPrice, decimal LineDiscount, VatRate TaxRate);

/// <summary>Kết quả tính cho 1 dòng. <see cref="LineTotal"/> đã gồm thuế &amp; phần CK tổng phân bổ.</summary>
public sealed record OrderCalcLineResult(decimal Gross, decimal Taxable, decimal Tax, decimal LineTotal);

/// <summary>Tổng hợp tiền của đơn (theo B4/Order).</summary>
public sealed record OrderTotals(
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal RoundingAdj,
    decimal GrandTotal,
    IReadOnlyList<OrderCalcLineResult> Lines);

/// <summary>
/// Tính tiền đơn theo đúng thứ tự B5 và quy tắc B13:
/// giá gốc → CK dòng → CK tổng (phân bổ về dòng) → thuế VAT theo từng thuế suất → làm tròn cuối.
/// Mọi phép tính dùng <c>decimal</c>; làm tròn half-up tới đồng (VND không có phần lẻ).
/// </summary>
public static class OrderCalculator
{
    public static decimal VatPercent(VatRate rate) => rate switch
    {
        VatRate.Five => 5m,
        VatRate.Eight => 8m,
        VatRate.Ten => 10m,
        _ => 0m, // Zero / Exempt / NotDeclared
    };

    /// <param name="lines">Các dòng đã có đơn giá &amp; CK dòng.</param>
    /// <param name="orderDiscount">CK/KM trên tổng đơn (B5 bước 3), phân bổ về dòng.</param>
    /// <param name="cashRoundingUnit">Đơn vị làm tròn tiền mặt (vd 500/1000); 0 = không làm tròn.</param>
    public static OrderTotals Calculate(
        IReadOnlyList<OrderCalcLine> lines,
        decimal orderDiscount = 0m,
        decimal cashRoundingUnit = 0m)
    {
        ArgumentNullException.ThrowIfNull(lines);

        int n = lines.Count;
        var gross = new decimal[n];
        var lineNet = new decimal[n]; // sau CK dòng
        decimal subtotal = 0m, lineDiscountTotal = 0m, subtotalNet = 0m;

        for (int i = 0; i < n; i++)
        {
            gross[i] = lines[i].Qty * lines[i].UnitPrice;
            lineNet[i] = gross[i] - lines[i].LineDiscount;
            subtotal += gross[i];
            lineDiscountTotal += lines[i].LineDiscount;
            subtotalNet += lineNet[i];
        }

        // CK tổng đơn: phân bổ theo tỷ lệ lineNet; phần dư dồn vào dòng cuối (B13).
        var alloc = AllocateProportional(orderDiscount, lineNet, subtotalNet);

        var results = new OrderCalcLineResult[n];
        decimal taxTotal = 0m, taxableTotal = 0m;
        for (int i = 0; i < n; i++)
        {
            decimal taxable = lineNet[i] - alloc[i];
            decimal tax = RoundDong(taxable * VatPercent(lines[i].TaxRate) / 100m);
            taxableTotal += taxable;
            taxTotal += tax;
            results[i] = new OrderCalcLineResult(gross[i], taxable, tax, taxable + tax);
        }

        decimal preRound = taxableTotal + taxTotal;
        decimal grand = preRound;
        decimal roundingAdj = 0m;
        if (cashRoundingUnit > 0m)
        {
            grand = Math.Round(preRound / cashRoundingUnit, MidpointRounding.AwayFromZero) * cashRoundingUnit;
            roundingAdj = grand - preRound;
        }

        return new OrderTotals(
            Subtotal: subtotal,
            DiscountTotal: lineDiscountTotal + orderDiscount,
            TaxTotal: taxTotal,
            RoundingAdj: roundingAdj,
            GrandTotal: grand,
            Lines: results);
    }

    /// <summary>Phân bổ <paramref name="amount"/> theo tỷ lệ weight; chốt dư ở phần tử cuối để tổng khớp tuyệt đối.</summary>
    private static decimal[] AllocateProportional(decimal amount, decimal[] weights, decimal weightSum)
    {
        var result = new decimal[weights.Length];
        if (amount <= 0m || weightSum <= 0m) return result;

        decimal running = 0m;
        for (int i = 0; i < weights.Length; i++)
        {
            decimal share = RoundDong(amount * weights[i] / weightSum);
            result[i] = share;
            running += share;
        }
        // Dồn chênh lệch làm tròn vào dòng cuối → tổng phân bổ == amount.
        result[^1] += amount - running;
        return result;
    }

    /// <summary>Làm tròn half-up tới đồng (VND).</summary>
    private static decimal RoundDong(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);
}
