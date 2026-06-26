using Microsoft.EntityFrameworkCore;
using Pos.Domain.Catalog;
using Pos.Domain.Customers;
using Pos.Domain.Inventory;
using Pos.Domain.Returns;
using Pos.Domain.Sales;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Tests.Persistence;

/// <summary>
/// B14 — kiểm chứng mô hình EF khớp ERD: mọi bảng PK = GUID "Id", các unique index &amp; quan hệ
/// chính tồn tại. Đọc <see cref="Microsoft.EntityFrameworkCore.Metadata.IModel"/> tĩnh — KHÔNG nối DB.
/// </summary>
public class B14ModelConformanceTests
{
    private static PosDbContext NewContext()
    {
        // Npgsql provider chỉ để dựng model; đọc metadata không mở kết nối.
        var options = new DbContextOptionsBuilder<PosDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=x;Password=x")
            .Options;
        return new PosDbContext(options);
    }

    [Fact]
    public void B14_6_EveryEntity_HasSingle_GuidId_PrimaryKey()
    {
        using var ctx = NewContext();

        foreach (var et in ctx.Model.GetEntityTypes())
        {
            var pk = et.FindPrimaryKey();
            Assert.True(pk is not null, $"{et.ClrType.Name} thiếu khóa chính.");
            var prop = Assert.Single(pk!.Properties);
            Assert.Equal("Id", prop.Name);
            Assert.Equal(typeof(Guid), prop.ClrType);
        }
    }

    [Theory]
    [InlineData(typeof(Barcode), nameof(Barcode.Code))]
    [InlineData(typeof(Customer), nameof(Customer.Phone))]
    public void B14_UniqueIndex_Exists(Type entity, string property)
    {
        using var ctx = NewContext();
        var et = ctx.Model.FindEntityType(entity)!;

        bool hasUnique = et.GetIndexes()
            .Any(i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == property);
        Assert.True(hasUnique, $"{entity.Name}.{property} cần unique index (B14).");
    }

    [Fact]
    public void B14_6_StockBalance_HasUniqueIndex_OnVariantAndStore()
    {
        using var ctx = NewContext();
        var et = ctx.Model.FindEntityType(typeof(StockBalance))!;

        bool ok = et.GetIndexes().Any(i => i.IsUnique
            && i.Properties.Select(p => p.Name).OrderBy(n => n)
                .SequenceEqual(new[] { nameof(StockBalance.StoreId), nameof(StockBalance.VariantId) }.OrderBy(n => n)));
        Assert.True(ok, "StockBalance cần unique index (VariantId, StoreId) — snapshot tồn theo kho (B14.6).");
    }

    [Theory]
    [InlineData(typeof(OrderLine), typeof(Order))]
    [InlineData(typeof(Payment), typeof(Order))]
    [InlineData(typeof(ReturnLine), typeof(ReturnOrder))]
    [InlineData(typeof(GrnLine), typeof(PurchaseReceipt))]
    public void B14_Relationship_ChildReferencesParent(Type child, Type parent)
    {
        using var ctx = NewContext();
        var et = ctx.Model.FindEntityType(child)!;

        bool hasFk = et.GetForeignKeys().Any(fk => fk.PrincipalEntityType.ClrType == parent);
        Assert.True(hasFk, $"{child.Name} cần FK tới {parent.Name} (B14 ERD).");
    }
}
