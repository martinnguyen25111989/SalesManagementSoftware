using MediatR;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Common;
using Pos.Application.Pricing;
using Pos.Domain.Common;
using Pos.Domain.Sales;

namespace Pos.Application.Orders.CreateOrder;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    /// <summary>Ngưỡng giảm giá tay theo dòng cần Manager duyệt (B2/B5). Cấu hình được sau.</summary>
    private const decimal ManualLineDiscountThreshold = 0.10m;

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

        // Lấy về rồi gom theo biến thể ở bộ nhớ (tránh GroupBy+First không dịch được trên SQLite/offline).
        var prices = (await _db.PriceItems
                .Where(p => variantIds.Contains(p.VariantId))
                .ToListAsync(ct))
            .GroupBy(p => p.VariantId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedUtc).First().Price);

        // 5) Giải đơn giá + thuế suất từng dòng.
        int n = cmd.Lines.Count;
        var unitPrices = new decimal[n];
        var taxRates = new VatRate[n];
        var promoLines = new PromoLine[n];
        for (int i = 0; i < n; i++)
        {
            var l = cmd.Lines[i];
            if (!variants.TryGetValue(l.VariantId, out var variant))
                throw new NotFoundException($"Không tìm thấy biến thể {l.VariantId}.");

            decimal unitPrice = l.UnitPriceOverride
                ?? (prices.TryGetValue(l.VariantId, out var p) ? p
                    : throw new BusinessRuleException($"Biến thể {l.VariantId} chưa có giá bán."));

            unitPrices[i] = unitPrice;
            taxRates[i] = variant.Product?.TaxRate ?? VatRate.Ten;
            promoLines[i] = new PromoLine(l.VariantId, l.Qty, unitPrice);
        }

        // Khuyến mãi tự động (B5).
        var promo = PromotionEngine.Evaluate(promoLines, cmd.Promotions,
            new PromoContext(DateTime.UtcNow, cmd.CustomerTierId, cmd.VoucherCode));
        if (!string.IsNullOrWhiteSpace(cmd.VoucherCode) && promo.RejectedVouchers.Count > 0)
            throw new BusinessRuleException(string.Join("; ", promo.RejectedVouchers));

        // Gộp CK tay (theo dòng) + CK khuyến mãi; chặn CK tay vượt ngưỡng nếu chưa được duyệt (B2/B5).
        var calcLines = new List<OrderCalcLine>(n);
        for (int i = 0; i < n; i++)
        {
            var l = cmd.Lines[i];
            decimal gross = l.Qty * unitPrices[i];
            if (l.LineDiscount > ManualLineDiscountThreshold * gross && !cmd.ManagerApproved)
                throw new BusinessRuleException(
                    $"Giảm giá tay dòng {l.VariantId} vượt ngưỡng {ManualLineDiscountThreshold:P0} — cần Manager duyệt.");

            decimal lineDiscount = Math.Min(gross, l.LineDiscount + promo.LineDiscounts[i]);
            calcLines.Add(new OrderCalcLine(l.Qty, unitPrices[i], lineDiscount, taxRates[i]));
        }

        decimal orderDiscount = cmd.OrderDiscount + promo.OrderDiscount;
        var totals = OrderCalculator.Calculate(calcLines, orderDiscount, cmd.CashRoundingUnit);

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
                LineDiscount = calcLines[i].LineDiscount, // CK tay + KM, đã gộp
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
