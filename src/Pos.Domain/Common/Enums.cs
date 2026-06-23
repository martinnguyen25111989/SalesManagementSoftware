namespace Pos.Domain.Common;

/// <summary>Trạng thái đồng bộ offline-first (B14.6).</summary>
public enum SyncStatus { Pending = 0, Synced = 1, Conflict = 2 }

public enum OrderStatus { Draft, OnHold, Completed, Voided, Returned, PartiallyReturned }

public enum PaymentStatus { Unpaid, Partial, Paid, Refunded }

public enum PaymentMethod { Cash, Card, VietQR, Wallet, Debt, Point }

public enum CashMovementType { In, Out }

public enum ProductStatus { Active, Discontinued }

public enum BarcodeType { Ean13, Weighed, Internal }

/// <summary>Thuế suất VAT theo HĐĐT VN: 0/5/8/10/KCT (không chịu thuế)/KKKNT (không kê khai).</summary>
public enum VatRate { Zero, Five, Eight, Ten, Exempt, NotDeclared }

public enum PromotionType { PercentLine, AmountLine, OrderDiscount, Bogo, QtyTier, Combo, Voucher, MemberTier }

public enum StockTransactionType { Sale, Purchase, Return, Adjust, StockTake, TransferIn, TransferOut, Void }

/// <summary>
/// B8 — chính sách bán khi tồn về âm theo từng chi nhánh:
/// Allow = cho qua (offline-first, mặc định); Warn = cần Manager duyệt; Block = chặn cứng.
/// </summary>
public enum NegativeStockPolicy { Allow, Warn, Block }

public enum EInvoiceType { Original, Adjust, Replace, Cancel }

public enum EInvoiceStatus { Pending, Issued, Sent, Rejected, Canceled }
