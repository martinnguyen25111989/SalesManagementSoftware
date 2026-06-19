---
description: Chạy toàn bộ test và tóm tắt kết quả
---

Chạy `dotnet test` cho toàn solution, ưu tiên kiểm các vùng rủi ro cao (BusinessRules.md):

- **Sync engine** (`Pos.Sync.Tests`): 2+ máy offline → sync → tồn kho đúng, không trùng đơn, conflict phát hiện được.
- **Idempotency**: gửi cùng 1 order 2 lần → chỉ 1 đơn.
- **Tính tiền** (B5/B13): làm tròn, chiết khấu dòng/tổng, VAT theo từng thuế suất, không lệch 1đ.

Tóm tắt: số test pass/fail, test nào fail kèm output. Nếu có fail, đề xuất nguyên nhân — KHÔNG tự sửa trừ khi được yêu cầu.
