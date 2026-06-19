namespace Pos.Application.Pricing;

/// <summary>Loại ưu đãi engine hỗ trợ (tập con phổ biến của B5; BOGO/Combo bổ sung sau).</summary>
public enum PromoKind
{
    LinePercent,    // giảm % trên dòng (theo VariantId)
    LineAmount,     // giảm số tiền trên dòng
    OrderPercent,   // giảm % trên tổng đơn
    OrderAmount,    // giảm số tiền trên tổng đơn
    QtyTierPercent, // mua >= MinQty thì giảm % cho SP đó
    MemberPercent,  // giảm % theo hạng thành viên
    VoucherPercent, // voucher giảm %
    VoucherAmount,  // voucher giảm số tiền
}

/// <summary>Định nghĩa 1 khuyến mãi (typed) cho engine đánh giá.</summary>
public sealed record PromotionDef
{
    public string Name { get; init; } = string.Empty;
    public PromoKind Kind { get; init; }

    /// <summary>Ưu tiên cao áp trước. KM non-stackable (loại trừ) dừng cộng dồn các KM sau.</summary>
    public int Priority { get; init; }
    public bool Stackable { get; init; }

    public DateTime FromUtc { get; init; } = DateTime.MinValue;
    public DateTime? ToUtc { get; init; }

    /// <summary>Giá trị đơn tối thiểu để áp (tính trên tổng gốc).</summary>
    public decimal MinOrderValue { get; init; }

    /// <summary>% (0..100) hoặc số tiền, tùy <see cref="Kind"/>.</summary>
    public decimal Value { get; init; }

    public Guid? TargetVariantId { get; init; } // Line*/QtyTier
    public decimal MinQty { get; init; }         // QtyTier
    public Guid? CustomerTierId { get; init; }    // MemberPercent
    public string? VoucherCode { get; init; }     // Voucher*
    public int? MaxUsage { get; init; }           // Voucher*
    public int UsedCount { get; init; }           // Voucher*
}

public sealed record PromoLine(Guid VariantId, decimal Qty, decimal UnitPrice);

public sealed record PromoContext(DateTime NowUtc, Guid? CustomerTierId = null, string? VoucherCode = null);

public sealed record PromotionResult(
    decimal[] LineDiscounts,
    decimal OrderDiscount,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> RejectedVouchers);

/// <summary>
/// Engine khuyến mãi (B5). Thuần (không I/O). Quy tắc gộp: duyệt theo Priority giảm dần,
/// dừng ngay sau KM <b>non-stackable</b> đầu tiên (KM loại trừ không cộng dồn thêm).
/// Mọi giảm giá clamp để dòng &amp; tổng đơn không âm (B5: tổng tiền ≥ 0). Làm tròn tới đồng (B13).
/// </summary>
public static class PromotionEngine
{
    public static PromotionResult Evaluate(
        IReadOnlyList<PromoLine> lines,
        IReadOnlyList<PromotionDef> promos,
        PromoContext ctx)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(promos);

        int n = lines.Count;
        var gross = new decimal[n];
        decimal orderGross = 0m;
        for (int i = 0; i < n; i++)
        {
            gross[i] = lines[i].Qty * lines[i].UnitPrice;
            orderGross += gross[i];
        }

        var lineDisc = new decimal[n];
        decimal orderDisc = 0m;
        var applied = new List<string>();
        var rejectedVouchers = new List<string>();

        var applicable = SelectApplicable(promos, ctx, lines, orderGross, rejectedVouchers);

        foreach (var p in applicable)
        {
            ApplyOne(p, lines, gross, lineDisc, ref orderDisc, orderGross);
            applied.Add(p.Name);
        }

        return new PromotionResult(lineDisc, orderDisc, applied, rejectedVouchers);
    }

    private static List<PromotionDef> SelectApplicable(
        IReadOnlyList<PromotionDef> promos, PromoContext ctx, IReadOnlyList<PromoLine> lines,
        decimal orderGross, List<string> rejectedVouchers)
    {
        var applicable = new List<PromotionDef>();
        foreach (var p in promos)
        {
            if (IsVoucher(p.Kind))
            {
                if (!VoucherApplies(p, ctx, orderGross, rejectedVouchers)) continue;
            }
            else if (!ConditionsMet(p, ctx, lines, orderGross))
            {
                continue;
            }
            applicable.Add(p);
        }

        // Thứ tự ưu tiên; dừng sau KM non-stackable đầu tiên (loại trừ).
        var ordered = applicable.OrderByDescending(p => p.Priority).ThenBy(p => p.Name).ToList();
        var chosen = new List<PromotionDef>();
        foreach (var p in ordered)
        {
            chosen.Add(p);
            if (!p.Stackable) break;
        }
        return chosen;
    }

    private static bool ConditionsMet(PromotionDef p, PromoContext ctx, IReadOnlyList<PromoLine> lines, decimal orderGross)
    {
        if (!InWindow(p, ctx.NowUtc)) return false;
        if (orderGross < p.MinOrderValue) return false;

        return p.Kind switch
        {
            PromoKind.MemberPercent => p.CustomerTierId is not null && p.CustomerTierId == ctx.CustomerTierId,
            PromoKind.LinePercent or PromoKind.LineAmount =>
                lines.Any(l => l.VariantId == p.TargetVariantId),
            PromoKind.QtyTierPercent =>
                lines.Where(l => l.VariantId == p.TargetVariantId).Sum(l => l.Qty) >= p.MinQty,
            _ => true, // Order*
        };
    }

    private static bool VoucherApplies(PromotionDef p, PromoContext ctx, decimal orderGross, List<string> rejected)
    {
        // Chỉ xét khi khách nhập đúng mã của KM này.
        if (string.IsNullOrWhiteSpace(ctx.VoucherCode)) return false;
        if (!string.Equals(p.VoucherCode, ctx.VoucherCode, StringComparison.OrdinalIgnoreCase)) return false;

        if (!InWindow(p, ctx.NowUtc)) { rejected.Add($"{p.VoucherCode}: hết hạn / chưa hiệu lực"); return false; }
        if (p.MaxUsage is { } max && p.UsedCount >= max) { rejected.Add($"{p.VoucherCode}: hết lượt sử dụng"); return false; }
        if (orderGross < p.MinOrderValue) { rejected.Add($"{p.VoucherCode}: chưa đạt giá trị tối thiểu"); return false; }
        return true;
    }

    private static bool InWindow(PromotionDef p, DateTime now) =>
        now >= p.FromUtc && (p.ToUtc is null || now <= p.ToUtc);

    private static bool IsVoucher(PromoKind k) => k is PromoKind.VoucherPercent or PromoKind.VoucherAmount;

    private static void ApplyOne(
        PromotionDef p, IReadOnlyList<PromoLine> lines, decimal[] gross, decimal[] lineDisc,
        ref decimal orderDisc, decimal orderGross)
    {
        switch (p.Kind)
        {
            case PromoKind.LinePercent:
            case PromoKind.QtyTierPercent:
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i].VariantId == p.TargetVariantId)
                        AddLine(lineDisc, gross, i, RoundDong(gross[i] * p.Value / 100m));
                break;

            case PromoKind.LineAmount:
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i].VariantId == p.TargetVariantId)
                        AddLine(lineDisc, gross, i, p.Value);
                break;

            case PromoKind.OrderPercent:
            case PromoKind.MemberPercent:
            case PromoKind.VoucherPercent:
            {
                decimal net = CurrentNet(gross, lineDisc, orderDisc, orderGross);
                orderDisc += RoundDong(net * p.Value / 100m);
                break;
            }

            case PromoKind.OrderAmount:
            case PromoKind.VoucherAmount:
            {
                decimal net = CurrentNet(gross, lineDisc, orderDisc, orderGross);
                orderDisc += Math.Min(p.Value, net); // không để tổng âm
                break;
            }
        }
    }

    private static void AddLine(decimal[] lineDisc, decimal[] gross, int i, decimal add)
    {
        // Không vượt quá giá trị dòng (dòng không âm).
        lineDisc[i] = Math.Min(gross[i], lineDisc[i] + Math.Max(0m, add));
    }

    private static decimal CurrentNet(decimal[] gross, decimal[] lineDisc, decimal orderDisc, decimal orderGross)
    {
        decimal net = orderGross - orderDisc;
        for (int i = 0; i < gross.Length; i++) net -= lineDisc[i];
        return net < 0m ? 0m : net;
    }

    private static decimal RoundDong(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);
}
