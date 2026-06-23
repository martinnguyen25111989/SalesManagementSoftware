using MediatR;

namespace Pos.Application.Invoicing.ProcessPending;

/// <summary>
/// Drain hàng đợi HĐĐT chưa phát hành (B11-A.6) — chạy khi có mạng (job nền). Phát hành lần lượt theo
/// thứ tự thời gian, idempotent theo Order.Id (không trùng, không sót). Trả thống kê lượt xử lý.
/// </summary>
public sealed record ProcessPendingEInvoicesCommand(int MaxBatch = 50) : IRequest<ProcessPendingResult>;

public sealed record ProcessPendingResult(int Processed, int Issued, int Rejected, int StillPending);
