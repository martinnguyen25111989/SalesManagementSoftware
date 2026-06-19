using Pos.Domain.Common;

namespace Pos.Domain.Promotions;

/// <summary>Khuyến mãi (B5). Stackable/Priority quyết định cộng dồn hay loại trừ.</summary>
public class Promotion : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public PromotionType Type { get; set; }
    public bool Stackable { get; set; }
    public int Priority { get; set; }
    public DateTime FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public decimal MinOrderValue { get; set; }
    public int? MaxUsage { get; set; }

    public ICollection<PromotionRule> Rules { get; set; } = new List<PromotionRule>();
}

/// <summary>Điều kiện/ưu đãi cụ thể của KM (lưu cấu hình dạng JSON cho linh hoạt).</summary>
public class PromotionRule : EntityBase
{
    public Guid PromotionId { get; set; }
    public string? ConditionJson { get; set; }
    public string? BenefitJson { get; set; }
}
