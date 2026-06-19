---
name: test-writer
description: Viết unit/integration test cho dự án POS (.NET, xUnit + Testcontainers). Dùng khi cần test cho domain logic, sync engine, tính tiền/thuế, idempotency, hoặc luồng HĐĐT.
---

# Skill: Test Writer (POS .NET)

## Khi nào dùng
Khi cần bổ sung test cho một tính năng/sửa lỗi, đặc biệt vùng rủi ro cao.

## Nguyên tắc
- **Pyramid**: Unit (domain, tính toán) → Integration (EF + DB thật qua Testcontainers) → E2E (luồng bán hàng).
- Framework: xUnit; assertion rõ ràng; tên test mô tả hành vi.

## Trọng tâm bắt buộc (BusinessRules.md)
- **Sync**: 2+ máy offline → sync → tồn đúng, không trùng đơn, conflict phát hiện.
- **Idempotency**: cùng 1 order gửi 2 lần → 1 đơn.
- **Tính tiền (B5/B13)**: làm tròn, chiết khấu dòng/tổng phân bổ, VAT theo từng thuế suất, khử lệch 1đ.
- **Trả hàng (B7)**: hoàn đúng tỷ lệ CK/thuế, nhập lại tồn, thu hồi điểm.
- **HĐĐT (B11-A)**: phát hành offline→online đúng thứ tự, không trùng mã CQT.

## Output
Viết test vào `tests/<project>.Tests` tương ứng; chạy `dotnet test` để xác nhận pass.
