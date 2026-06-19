using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Pricing;
using Pos.Domain.Common;
using Pos.Domain.Sales;

namespace Pos.Application.Orders.CreateOrder;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    private readonly IPosDbContext _db;

    public CreateOrderHandler(IPosDbContext db) => _db = db;

    public async Task<OrderResult> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        // 1) Idempotency (CLAUDE.md): cùng OrderId đã có → trả về đơn cũ, không tạo trùng.
        var existing = await _db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == cmd.OrderId, ct);
        if (existing is not null)
            return ToResult(existing);

        if (cmd.Lines.Count == 0)
            throw new BusinessRuleException("Đơn phải có ít nhất 1 dòng hàng.");

        // 2) Chi nhánh (lấy prefix mã đơn).
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == cmd.StoreId, ct)
            ?? throw new NotFoundException($"Không tìm thấy chi nhánh {cmd.StoreId}.");

        // 3) Ca phải đang mở (B4: mỗi đơn gắn 1 ca mở).
        var shift = await _db.Shifts.FirstOrDefaultAsync(s => s.Id == cmd.ShiftId, ct)
            ?? throw new NotFoundException($"Không tìm thấy ca {cmd.ShiftId}.");
        if (shift.CloseAt is not null)
            throw new BusinessRuleException("Ca đã đóng — không thể tạo đơn.");

        // 4) Nạp biến thể (kèm Product để lấy thuế suất) + bảng giá.
        var variantIds = cmd.Lines.Select(l => l.VariantId).Distinct().ToList();
        var variants = await _db.Variants
            .Include(v => v.Product)
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        var prices = await _db.PriceItems
            .Where(p => variantIds.Contains(p.VariantId))
            .GroupBy(p => p.VariantId)
            .Select(g => g.OrderByDescending(p => p.CreatedUtc).First())
            .ToDictionaryAsync(p => p.VariantId, p => p.Price, ct);

        // 5) Dựng dòng tính tiền (B5: giá → CK dòng → …).
        var calcLines = new List<OrderCalcLine>(cmd.Lines.Count);
        var unitPrices = new decimal[cmd.Lines.Count];
        for (int i = 0; i < cmd.Lines.Count; i++)
        {
            var l = cmd.Lines[i];
            if (!variants.TryGetValue(l.VariantId, out var variant))
                throw new NotFoundException($"Không tìm thấy biến thể {l.VariantId}.");

            decimal unitPrice = l.UnitPriceOverride
                ?? (prices.TryGetValue(l.VariantId, out var p) ? p
                    : throw new BusinessRuleException($"Biến thể {l.VariantId} chưa có giá bán."));

            unitPrices[i] = unitPrice;
            var taxRate = variant.Product?.TaxRate ?? VatRate.Ten;
            calcLines.Add(new OrderCalcLine(l.Qty, unitPrice, l.LineDiscount, taxRate));
        }

        var totals = OrderCalculator.Calculate(calcLines, cmd.OrderDiscount, cmd.CashRoundingUnit);

        // 6) Tạo Order + OrderLine (Draft). Số đơn hiển thị có prefix chi nhánh;
        //    định danh nội bộ là GUID. Số chính thức có thể cấp khi phát hành HĐĐT.
        int seq = await _db.Orders.CountAsync(o => o.StoreId == cmd.StoreId, ct) + 1;
        var order = new Order
        {
            Id = cmd.OrderId,
            OrderNumber = $"{store.OrderPrefix}-{seq:D6}",
            StoreId = cmd.StoreId,
            DeviceId = cmd.DeviceId,
            ShiftId = cmd.ShiftId,
            CashierId = cmd.CashierId,
            CustomerId = cmd.CustomerId,
            Status = OrderStatus.Draft,
            PaymentStatus = PaymentStatus.Unpaid,
            Subtotal = totals.Subtotal,
            DiscountTotal = totals.DiscountTotal,
            TaxTotal = totals.TaxTotal,
            RoundingAdj = totals.RoundingAdj,
            GrandTotal = totals.GrandTotal,
        };

        for (int i = 0; i < cmd.Lines.Count; i++)
        {
            var l = cmd.Lines[i];
            var r = totals.Lines[i];
            order.Lines.Add(new OrderLine
            {
                OrderId = order.Id,
                VariantId = l.VariantId,
                Qty = l.Qty,
                UnitPrice = unitPrices[i],
                LineDiscount = l.LineDiscount,
                TaxRate = calcLines[i].TaxRate,
                LineTotal = r.LineTotal,
                Note = l.Note,
            });
        }

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        return ToResult(order);
    }

    private static OrderResult ToResult(Order o)
    {
        // Thuế từng dòng không lưu riêng trên OrderLine → tính lại (deterministic) để hiển thị.
        // Tổng đơn dùng giá trị đã lưu; CK tổng suy ra = DiscountTotal − tổng CK dòng.
        var lines = o.Lines.ToList();
        decimal orderDiscount = o.DiscountTotal - lines.Sum(l => l.LineDiscount);
        var calc = OrderCalculator.Calculate(
            lines.Select(l => new OrderCalcLine(l.Qty, l.UnitPrice, l.LineDiscount, l.TaxRate)).ToList(),
            orderDiscount);

        var lineResults = lines.Select((l, i) => new OrderLineResult(
            l.VariantId, l.Qty, l.UnitPrice, l.LineDiscount, l.TaxRate, calc.Lines[i].Tax, l.LineTotal)).ToList();

        return new OrderResult(
            o.Id, o.OrderNumber, o.Status, o.Subtotal, o.DiscountTotal, o.TaxTotal, o.RoundingAdj, o.GrandTotal,
            lineResults);
    }
}
