using Pos.Application.Common;
using Pos.Application.Orders.CreateOrder;
using Pos.Application.Pricing;
using Pos.Application.Tests.Support;
using Pos.Domain.Common;

namespace Pos.Application.Tests.Orders;

public class CreateOrderPromotionTests
{
    private static CreateOrderCommand Cmd(Guid variantId, decimal qty, decimal manualLineDisc = 0m) => new()
    {
        StoreId = TestData.StoreId,
        ShiftId = TestData.ShiftId,
        CashierId = TestData.CashierId,
        Lines = new[] { new CreateOrderLine(variantId, qty, manualLineDisc) },
    };

    [Fact]
    public async Task Applies_LinePercentPromotion_EndToEnd()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var variantId = await TestData.AddProductAsync(db, 10_000m, VatRate.Zero); // thuế 0 cho dễ kiểm
        var cmd = Cmd(variantId, 2m) with
        {
            Promotions = new[]
            {
                new PromotionDef { Name = "-10%", Kind = PromoKind.LinePercent, Stackable = true, TargetVariantId = variantId, Value = 10m },
            },
        };

        var r = await new CreateOrderHandler(db).Handle(cmd, default);

        Assert.Equal(2_000m, r.Lines[0].LineDiscount); // 10% của 20.000
        Assert.Equal(18_000m, r.GrandTotal);
    }

    [Fact]
    public async Task ManualLineDiscount_OverThreshold_RequiresApproval()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var variantId = await TestData.AddProductAsync(db, 10_000m, VatRate.Zero);
        var over = Cmd(variantId, 1m, manualLineDisc: 5_000m); // 50% > ngưỡng 10%

        await Assert.ThrowsAsync<BusinessRuleException>(() => new CreateOrderHandler(db).Handle(over, default));

        var approved = over with { ManagerApproved = true };
        var r = await new CreateOrderHandler(db).Handle(approved, default);
        Assert.Equal(5_000m, r.Lines[0].LineDiscount);
        Assert.Equal(5_000m, r.GrandTotal);
    }

    [Fact]
    public async Task Rejects_When_VoucherInvalid()
    {
        using var db = TestPosDbContext.Create();
        await TestData.SeedAsync(db);
        var variantId = await TestData.AddProductAsync(db, 10_000m, VatRate.Ten);
        var cmd = Cmd(variantId, 1m) with
        {
            VoucherCode = "SALE",
            Promotions = new[]
            {
                new PromotionDef
                {
                    Name = "SALE", Kind = PromoKind.VoucherAmount, Stackable = true, VoucherCode = "SALE",
                    Value = 5_000m, FromUtc = DateTime.UtcNow.AddDays(-10), ToUtc = DateTime.UtcNow.AddDays(-1),
                },
            },
        };

        await Assert.ThrowsAsync<BusinessRuleException>(() => new CreateOrderHandler(db).Handle(cmd, default));
    }
}
