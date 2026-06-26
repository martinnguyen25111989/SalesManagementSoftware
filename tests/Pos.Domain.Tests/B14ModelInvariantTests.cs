using System.Reflection;
using Pos.Domain.Common;
using Pos.Domain.Customers;
using Pos.Domain.Inventory;
using Pos.Domain.Invoicing;
using Pos.Domain.Returns;
using Pos.Domain.Sales;

namespace Pos.Domain.Tests;

/// <summary>
/// B14 — bất biến mô hình dữ liệu (ERD &amp; ghi chú thiết kế B14.6) được khóa bằng test để không trôi.
/// </summary>
public class B14ModelInvariantTests
{
    private static readonly Assembly DomainAssembly = typeof(EntityBase).Assembly;

    [Fact]
    public void B14_6_AllEntities_HaveClientGenerated_GuidPrimaryKey()
    {
        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(EntityBase).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(entityTypes);
        foreach (var t in entityTypes)
        {
            var id = t.GetProperty(nameof(EntityBase.Id))!;
            Assert.Equal(typeof(Guid), id.PropertyType);

            // PK sinh ở client: khởi tạo mới đã có Guid khác rỗng (không chờ DB cấp).
            var instance = (EntityBase)Activator.CreateInstance(t)!;
            Assert.NotEqual(Guid.Empty, instance.Id);
        }
    }

    [Fact]
    public void B14_6_EInvoice_IsSeparatedFromOrder_AndChained()
    {
        // Không nhồi trạng thái/loại HĐĐT vào Order.
        var orderProps = typeof(Order).GetProperties().Select(p => p.PropertyType).ToList();
        Assert.DoesNotContain(typeof(EInvoiceStatus), orderProps);
        Assert.DoesNotContain(typeof(EInvoiceType), orderProps);
        Assert.DoesNotContain(typeof(EInvoice), orderProps);

        // Chuỗi chứng từ: gốc → điều chỉnh/thay thế/hủy qua OriginalInvoiceId (nullable).
        Assert.Equal(typeof(Guid), typeof(EInvoice).GetProperty(nameof(EInvoice.OrderId))!.PropertyType);
        Assert.Equal(typeof(Guid?), typeof(EInvoice).GetProperty(nameof(EInvoice.OriginalInvoiceId))!.PropertyType);
    }

    [Fact]
    public void B14_6_ReturnLine_ReferencesOriginalOrderLine()
    {
        // Trả hàng tham chiếu dòng gốc để chặn trả vượt & hoàn đúng tỷ lệ.
        Assert.Equal(typeof(Guid), typeof(ReturnLine).GetProperty(nameof(ReturnLine.OrderLineId))!.PropertyType);
        Assert.Equal(typeof(Guid), typeof(ReturnLine).GetProperty(nameof(ReturnLine.ReturnOrderId))!.PropertyType);
    }

    [Fact]
    public void B14_6_OrderDiscount_AllocatedOnLine_NoSeparateTable()
    {
        // CK tổng phân bổ ghi trên OrderLine.LineDiscount; không có bảng phân bổ riêng.
        Assert.Equal(typeof(decimal), typeof(OrderLine).GetProperty(nameof(OrderLine.LineDiscount))!.PropertyType);
        Assert.DoesNotContain(DomainAssembly.GetTypes(),
            t => t.Name.Contains("DiscountAllocation", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(ErdColumns))]
    public void B14_Erd_Entities_ExposeSpecifiedColumns(Type type, string property, Type clrType)
    {
        var prop = type.GetProperty(property);
        Assert.True(prop is not null, $"{type.Name} thiếu cột {property} (B14 ERD).");
        Assert.Equal(clrType, prop!.PropertyType);
    }

    public static IEnumerable<object[]> ErdColumns() => new[]
    {
        // B14.2 — Bán hàng & thanh toán
        new object[] { typeof(Order), nameof(Order.ShiftId), typeof(Guid) },
        new object[] { typeof(Order), nameof(Order.CashierId), typeof(Guid) },
        new object[] { typeof(Order), nameof(Order.CustomerId), typeof(Guid?) },
        new object[] { typeof(Order), nameof(Order.GrandTotal), typeof(decimal) },
        new object[] { typeof(Order), nameof(Order.RoundingAdj), typeof(decimal) },
        new object[] { typeof(OrderLine), nameof(OrderLine.VariantId), typeof(Guid) },
        new object[] { typeof(OrderLine), nameof(OrderLine.TaxRate), typeof(VatRate) },
        new object[] { typeof(Payment), nameof(Payment.Method), typeof(PaymentMethod) },
        new object[] { typeof(Payment), nameof(Payment.Amount), typeof(decimal) },
        // B14.4 — Tồn kho, mua hàng, trả hàng
        new object[] { typeof(StockTransaction), nameof(StockTransaction.QtyChange), typeof(decimal) },
        new object[] { typeof(StockTransaction), nameof(StockTransaction.UnitCost), typeof(decimal) },
        new object[] { typeof(StockTransaction), nameof(StockTransaction.RefId), typeof(Guid?) },
        new object[] { typeof(GrnLine), nameof(GrnLine.VariantId), typeof(Guid) },
        new object[] { typeof(ReturnLine), nameof(ReturnLine.RestockToInventory), typeof(bool) },
        // B14.5 — Khách hàng & HĐĐT
        new object[] { typeof(Customer), nameof(Customer.CreditLimit), typeof(decimal) },
        new object[] { typeof(Customer), nameof(Customer.PointBalance), typeof(decimal) },
        new object[] { typeof(Receivable), nameof(Receivable.Outstanding), typeof(decimal) },
        new object[] { typeof(LoyaltyTxn), nameof(LoyaltyTxn.PointChange), typeof(int) },
    };
}
