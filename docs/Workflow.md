# Workflow.md — Sơ đồ luồng nghiệp vụ POS

Tổng hợp các luồng chính của hệ thống POS bán lẻ (offline-first, HĐĐT kết nối thuế).
Nguồn: [`BusinessRules.md`](./BusinessRules.md) (B1, B4, B11-A) và [`Technical.md`](./Technical.md) (§9 Sync).

---

## 1. Luồng bán lẻ tổng quát (happy path — B1)

```mermaid
flowchart TD
    A[Mở ca\nĐếm quỹ đầu ca] --> B[Quét mã / chọn sản phẩm]
    B --> C[Giỏ hàng]
    C --> D[Áp giá + chiết khấu/KM + thuế]
    D --> E[Thanh toán\ntiền mặt / thẻ / QR / hỗn hợp]
    E --> F[Phát hành HĐĐT\nmã CQT + in HĐ kèm QR tra cứu]
    F --> G[Trừ tồn kho\nStockTransaction append-only]
    G -->|đơn tiếp theo| B
    G --> H[Đóng ca\nĐếm quỹ cuối ca + đối soát X/Z report]
```

---

## 2. Máy trạng thái đơn hàng (Order state machine — B4)

```mermaid
stateDiagram-v2
    [*] --> Draft: tạo giỏ
    Draft --> OnHold: hold / park
    OnHold --> Draft: resume
    Draft --> Voided: hủy (cần quyền)
    Draft --> Completed: checkout + thanh toán
    Completed --> Returned: trả toàn phần
    Completed --> PartiallyReturned: trả một phần
    Voided --> [*]
    Returned --> [*]
    PartiallyReturned --> [*]
```

> Mỗi đơn phải gắn **1 Shift đang mở**. Đơn đã thanh toán/đã phát hành HĐĐT **không xóa cứng** —
> chỉ điều chỉnh/thay thế/hủy qua nghiệp vụ HĐĐT (B11) + AuditLog. Định danh nội bộ luôn là **GUID**.

---

## 3. Phát hành hóa đơn điện tử (EasyInvoice — B11-A.5)

```mermaid
flowchart TD
    A[Checkout đơn đã Paid] --> B{Có token?}
    B -- Không --> B1[login/refresh] --> C
    B -- Có --> C[Build request từ Order\ntransactionId = Order.Id]
    C --> D[POST CreateInvoice\nloại máy tính tiền]
    D --> E{Kết quả?}
    E -- 2xx + mã CQT --> F[Lưu EInvoice\nIssued + tải PDF/XML]
    F --> G[In hóa đơn kèm QR tra cứu]
    E -- 4xx sai field/thuế --> H[Rejected + log\ncảnh báo Manager]
    E -- 5xx / timeout --> I[EInvoicePending\nretry có backoff]
    I -.->|background job khi online| D
```

> **Idempotency:** luôn gửi `transactionId = Order.Id`. Nghi đã gửi → `QueryAsync` lấy lại mã CQT,
> **không bao giờ tạo 2 HĐ cho 1 Order**. Offline: in **phiếu tạm tính** ("chưa phải hóa đơn"),
> đẩy vào hàng đợi `EInvoicePending`; chỉ hợp lệ khi có **mã CQT**.

---

## 4. Đồng bộ offline-first (Sync — Technical §9)

```mermaid
sequenceDiagram
    participant C as Client (SQLite)
    participant API as API (PostgreSQL)
    Note over C: Offline — bán hàng<br/>ghi SQLite (SyncStatus=Pending)
    C->>API: Push record Pending (Idempotency-Key)
    API-->>C: Accepted / Conflict (per record)
    C->>C: Cập nhật SyncStatus = Synced / Conflict
    C->>API: Pull thay đổi kể từ LastSyncToken
    API-->>C: Master data + Version
    Note over C,API: Conflict master: Last-Write-Wins theo Version<br/>Tồn kho: cộng dồn transaction (không LWW)
```

> Conflict không tự giải được → đánh dấu `Conflict`, hiển thị cho Manager xử lý tay.
```
