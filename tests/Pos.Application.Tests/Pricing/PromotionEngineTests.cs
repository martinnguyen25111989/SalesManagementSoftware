using Pos.Application.Pricing;

namespace Pos.Application.Tests.Pricing;

public class PromotionEngineTests
{
    private static readonly Guid VarA = Guid.NewGuid();
    private static readonly Guid VarB = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

    private static PromoLine[] TwoLines() => new[]
    {
        new PromoLine(VarA, 2m, 30000m), // gross 60.000
        new PromoLine(VarB, 1m, 40000m), // gross 40.000  → tổng 100.000
    };

    private static PromoContext Ctx(string? voucher = null, Guid? tier = null) => new(Now, tier, voucher);

    [Fact]
    public void LinePercent_DiscountsOnlyTargetVariant()
    {
        var promo = new PromotionDef { Name = "-10% A", Kind = PromoKind.LinePercent, Stackable = true, TargetVariantId = VarA, Value = 10m };

        var r = PromotionEngine.Evaluate(TwoLines(), new[] { promo }, Ctx());

        Assert.Equal(6000m, r.LineDiscounts[0]); // 10% của 60.000
        Assert.Equal(0m, r.LineDiscounts[1]);
        Assert.Equal(0m, r.OrderDiscount);
    }

    [Fact]
    public void OrderAmount_RequiresMinOrderValue()
    {
        var promo = new PromotionDef { Name = "Đơn>=500k -50k", Kind = PromoKind.OrderAmount, Stackable = true, MinOrderValue = 500000m, Value = 50000m };

        var r = PromotionEngine.Evaluate(TwoLines(), new[] { promo }, Ctx());

        Assert.Empty(r.Applied); // tổng 100k < 500k
        Assert.Equal(0m, r.OrderDiscount);
    }

    [Fact]
    public void QtyTier_AppliesWhenQuantityReached()
    {
        var lines = new[] { new PromoLine(VarA, 5m, 10000m) }; // 5 x 10.000
        var promo = new PromotionDef { Name = "Mua>=5 -20%", Kind = PromoKind.QtyTierPercent, Stackable = true, TargetVariantId = VarA, MinQty = 5m, Value = 20m };

        var r = PromotionEngine.Evaluate(lines, new[] { promo }, Ctx());

        Assert.Equal(10000m, r.LineDiscounts[0]); // 20% của 50.000
    }

    [Fact]
    public void MemberPercent_AppliesOnlyForMatchingTier()
    {
        var tier = Guid.NewGuid();
        var promo = new PromotionDef { Name = "VIP -5%", Kind = PromoKind.MemberPercent, Stackable = true, CustomerTierId = tier, Value = 5m };

        var noTier = PromotionEngine.Evaluate(TwoLines(), new[] { promo }, Ctx());
        Assert.Equal(0m, noTier.OrderDiscount);

        var withTier = PromotionEngine.Evaluate(TwoLines(), new[] { promo }, Ctx(tier: tier));
        Assert.Equal(5000m, withTier.OrderDiscount); // 5% của 100.000
    }

    [Fact]
    public void LineAndOrderPromo_DoNotDoubleDiscountSameAmount()
    {
        // KM dòng -10% A (6.000) + KM tổng -10% trên phần net còn lại (94.000 → 9.400)
        var line = new PromotionDef { Name = "-10% A", Kind = PromoKind.LinePercent, Stackable = true, Priority = 10, TargetVariantId = VarA, Value = 10m };
        var order = new PromotionDef { Name = "-10% đơn", Kind = PromoKind.OrderPercent, Stackable = true, Priority = 5, Value = 10m };

        var r = PromotionEngine.Evaluate(TwoLines(), new[] { line, order }, Ctx());

        Assert.Equal(6000m, r.LineDiscounts[0]);
        Assert.Equal(9400m, r.OrderDiscount); // 10% của (100.000 − 6.000), không nhân đôi
    }

    [Fact]
    public void NonStackable_IsExclusive_StopsFurtherStacking()
    {
        var exclusive = new PromotionDef { Name = "Độc quyền -20% đơn", Kind = PromoKind.OrderPercent, Stackable = false, Priority = 100, Value = 20m };
        var other = new PromotionDef { Name = "-10% A", Kind = PromoKind.LinePercent, Stackable = true, Priority = 1, TargetVariantId = VarA, Value = 10m };

        var r = PromotionEngine.Evaluate(TwoLines(), new[] { exclusive, other }, Ctx());

        Assert.Single(r.Applied);
        Assert.Equal("Độc quyền -20% đơn", r.Applied[0]);
        Assert.Equal(20000m, r.OrderDiscount);
        Assert.Equal(0m, r.LineDiscounts[0]); // KM dòng bị loại trừ
    }

    [Fact]
    public void Voucher_Rejected_WhenExpired()
    {
        var promo = new PromotionDef
        {
            Name = "SALE", Kind = PromoKind.VoucherAmount, Stackable = true, VoucherCode = "SALE",
            Value = 20000m, FromUtc = Now.AddDays(-10), ToUtc = Now.AddDays(-1), // đã hết hạn
        };

        var r = PromotionEngine.Evaluate(TwoLines(), new[] { promo }, Ctx(voucher: "SALE"));

        Assert.Empty(r.Applied);
        Assert.Single(r.RejectedVouchers);
        Assert.Contains("hết hạn", r.RejectedVouchers[0]);
    }

    [Fact]
    public void Voucher_Rejected_WhenUsageExhausted()
    {
        var promo = new PromotionDef
        {
            Name = "ONE", Kind = PromoKind.VoucherPercent, Stackable = true, VoucherCode = "ONE",
            Value = 10m, MaxUsage = 1, UsedCount = 1, FromUtc = Now.AddDays(-1),
        };

        var r = PromotionEngine.Evaluate(TwoLines(), new[] { promo }, Ctx(voucher: "ONE"));

        Assert.Contains("hết lượt", r.RejectedVouchers[0]);
    }

    [Fact]
    public void Voucher_Applies_WhenValid()
    {
        var promo = new PromotionDef
        {
            Name = "TET", Kind = PromoKind.VoucherAmount, Stackable = true, VoucherCode = "tet",
            Value = 20000m, MinOrderValue = 50000m, FromUtc = Now.AddDays(-1), ToUtc = Now.AddDays(1),
        };

        var r = PromotionEngine.Evaluate(TwoLines(), new[] { promo }, Ctx(voucher: "TET")); // không phân biệt hoa thường

        Assert.Single(r.Applied);
        Assert.Equal(20000m, r.OrderDiscount);
        Assert.Empty(r.RejectedVouchers);
    }

    [Fact]
    public void Discounts_NeverExceedValue_TotalNotNegative()
    {
        var line = new PromotionDef { Name = "-999k A", Kind = PromoKind.LineAmount, Stackable = true, Priority = 10, TargetVariantId = VarA, Value = 999000m };
        var order = new PromotionDef { Name = "-999k đơn", Kind = PromoKind.OrderAmount, Stackable = true, Priority = 5, Value = 999000m };

        var r = PromotionEngine.Evaluate(TwoLines(), new[] { line, order }, Ctx());

        Assert.Equal(60000m, r.LineDiscounts[0]);       // không vượt gross dòng A
        Assert.Equal(40000m, r.OrderDiscount);          // phần net còn lại (40.000), không âm
    }
}
