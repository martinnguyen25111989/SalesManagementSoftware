namespace Pos.Application.Common;

/// <summary>Không tìm thấy entity cần thiết (map sang HTTP 404 ở API).</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>Vi phạm quy tắc nghiệp vụ (B-rules) — map sang HTTP 422/400 ở API.</summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
