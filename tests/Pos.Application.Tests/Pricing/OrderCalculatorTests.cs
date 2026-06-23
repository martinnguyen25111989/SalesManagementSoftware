using Pos.Application.Pricing;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Pricing;

public class OrderCalculatorTests
{
    [Fact]
    public void SingleLine_AppliesVat_AndTotals()
    {
        // 2 x 12.000 = 24.000, VAT 8% = 1.920 → tổng 25.920
        var totals = OrderCalculator.Calculate(new[]
        {
            new OrderCalcLine(Qty: 2m, UnitPrice: 12000m, LineDiscount: 0m, TaxRate: VatRate.Eight),
        });

        Assert.Equal(24000m, totals.Subtotal);
        Assert.Equal(0m, totals.DiscountTotal);
        Assert.Equal(1920m, totals.TaxTotal);
        Assert.Equal(25920m, totals.GrandTotal);
        Assert.Equal(25920m, totals.Lines[0].LineTotal);
    }

    [Fact]
    public void LineDiscount_ReducesTaxableBase()
    {
        // (10.000 − 2.000) = 8.000 ; VAT 10% = 800 → 8.800
        var totals = OrderCalculator.Calculate(new[]
        {
            new OrderCalcLine(1m, 10000m, 2000m, VatRate.Ten),
        });

        Assert.Equal(10000m, totals.Subtotal);
        Assert.Equal(2000m, totals.DiscountTotal);
        Assert.Equal(800m, totals.TaxTotal);
        Assert.Equal(8800m, totals.GrandTotal);
    }

    [Fact]
    public void MultipleRates_TaxAccumulatedPerLine_MatchesToTheDong()
    {
        // Dòng A: 8% trên 50.000 = 4.000 ; Dòng B: 10% trên 30.000 = 3.000 ; KCT: 0
        var totals = OrderCalculator.Calculate(new[]
        {
            new OrderCalcLine(1m, 50000m, 0m, VatRate.Eight),
            new OrderCalcLine(1m, 30000m, 0m, VatRate.Ten),
            new OrderCalcLine(1m, 20000m, 0m, VatRate.Exempt),
        });

        Assert.Equal(100000m, totals.Subtotal);
        Assert.Equal(7000m, totals.TaxTotal);
        Assert.Equal(107000m, totals.GrandTotal);
    }

    [Fact]
    public void OrderDiscount_AllocatedProportionally_NoOneDongDrift()
    {
        // CK tổng 7.000 trên 3 dòng (net 30/20/10 = 60.000). Phân bổ: 3.500 / 2.333→ remainder
        var lines = new[]
        {
            new OrderCalcLine(1m, 30000m, 0m, VatRate.Ten),
            new OrderCalcLine(1m, 20000m, 0m, VatRate.Ten),
            new OrderCalcLine(1m, 10000m, 0m, VatRate.Ten),
        };
        var totals = OrderCalculator.Calculate(lines, orderDiscount: 7000m);

        // Tổng net sau CK tổng = 60.000 − 7.000 = 53.000
        decimal taxableSum = totals.Lines.Sum(l => l.Taxable);
        Assert.Equal(53000m, taxableSum);

        // GrandTotal = taxable + tax, khớp tuyệt đối (không lệch 1đ)
        decimal expectedGrand = taxableSum + totals.TaxTotal;
        Assert.Equal(expectedGrand, totals.GrandTotal);
        Assert.Equal(60000m, totals.Subtotal);
        Assert.Equal(7000m, totals.DiscountTotal);
    }

    [Fact]
    public void CashRounding_SetsRoundingAdj_AndRoundsGrandTotal()
    {
        // VAT 8% trên 12.345 = 987,6 → 988 ; tổng 13.333 ; làm tròn 1.000 → 13.000, adj = −333
        var totals = OrderCalculator.Calculate(
            new[] { new OrderCalcLine(1m, 12345m, 0m, VatRate.Eight) },
            orderDiscount: 0m,
            cashRoundingUnit: 1000m);

        Assert.Equal(13000m, totals.GrandTotal);
        Assert.Equal(13333m - 13000m, -totals.RoundingAdj);
        Assert.Equal(0m, totals.GrandTotal % 1000m);
    }

    [Fact]
    public void Rounding_IsHalfUp_AtTheHalfDongBoundary()
    {
        // B13: làm tròn half-up (AwayFromZero). 5% trên 1.250 = 62,5 → 63 (không phải 62).
        var totals = OrderCalculator.Calculate(new[] { new OrderCalcLine(1m, 1250m, 0m, VatRate.Five) });

        Assert.Equal(63m, totals.TaxTotal);
        Assert.Equal(1313m, totals.GrandTotal);
    }

    [Fact]
    public void CashRounding_RoundsUp_AtHalfUnit()
    {
        // Tổng 13.500, làm tròn 1.000 half-up → 14.000 (adj +500).
        var totals = OrderCalculator.Calculate(
            new[] { new OrderCalcLine(1m, 13500m, 0m, VatRate.Zero) },
            cashRoundingUnit: 1000m);

        Assert.Equal(14000m, totals.GrandTotal);
        Assert.Equal(500m, totals.RoundingAdj);
    }

    [Fact]
    public void MultiRate_WithOrderDiscount_ReconcilesToTheDong()
    {
        // 3 dòng khác thuế suất + CK tổng 7% → tổng dòng (taxable+tax) == GrandTotal, không lệch 1đ.
        var lines = new[]
        {
            new OrderCalcLine(1m, 50000m, 0m, VatRate.Eight),
            new OrderCalcLine(1m, 30000m, 0m, VatRate.Ten),
            new OrderCalcLine(1m, 20000m, 0m, VatRate.Exempt),
        };
        var totals = OrderCalculator.Calculate(lines, orderDiscount: 7000m);

        decimal sumLines = totals.Lines.Sum(l => l.LineTotal);
        Assert.Equal(totals.GrandTotal, sumLines);                       // khớp tuyệt đối
        Assert.Equal(totals.TaxTotal, totals.Lines.Sum(l => l.Tax));     // thuế cộng dồn theo dòng
        Assert.Equal(100000m - 7000m, totals.Lines.Sum(l => l.Taxable)); // CK tổng phân bổ đủ
    }

    [Fact]
    public void OrderDiscount_Remainder_GoesToLastLine()
    {
        // CK tổng 10đ trên 3 dòng net bằng nhau (10.000 mỗi dòng) → 3,33→3 mỗi dòng, dư dồn dòng cuối.
        var lines = new[]
        {
            new OrderCalcLine(1m, 10000m, 0m, VatRate.Zero),
            new OrderCalcLine(1m, 10000m, 0m, VatRate.Zero),
            new OrderCalcLine(1m, 10000m, 0m, VatRate.Zero),
        };
        var totals = OrderCalculator.Calculate(lines, orderDiscount: 10m);

        // Tổng phần giảm phân bổ đúng 10đ (dòng cuối gánh phần dư).
        decimal allocated = 30000m - totals.Lines.Sum(l => l.Taxable);
        Assert.Equal(10m, allocated);
        Assert.True(totals.Lines[2].Taxable <= totals.Lines[0].Taxable); // dòng cuối bị trừ nhiều hơn/bằng
    }
}
