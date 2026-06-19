---
name: code-review
description: Review code C#/.NET trong dự án POS theo quy ước bắt buộc (PK GUID, decimal cho tiền, tồn kho append-only, idempotency, HAL, MVVM, tuân thủ HĐĐT). Dùng khi review PR/diff hoặc kiểm tra một file/module.
---

# Skill: Code Review (POS .NET)

## Khi nào dùng
Khi cần review thay đổi code, một class/module, hoặc trước khi merge.

## Quy trình
1. Lấy phạm vi: `git diff` hoặc file được chỉ định.
2. Đối chiếu **quy ước BẮT BUỘC** trong `CLAUDE.md` và nghiệp vụ trong `BusinessRules.md`.
3. Kiểm tra các điểm rủi ro cao:
   - PK GUID sinh ở client; không INT auto-increment.
   - Tiền: `decimal`; làm tròn/thuế theo B13; không lệch 1đ.
   - Tồn kho: cộng dồn `StockTransaction`, không update số tồn trực tiếp.
   - Idempotency-Key trên API tạo dữ liệu.
   - HAL: ViewModel không gọi OS/phần cứng trực tiếp.
   - HĐĐT: không sửa/xóa hóa đơn đã phát hành.
   - Không hardcode path; không commit secret; MVVM sạch.
4. Báo cáo theo mức độ: **Chặn release / Nên sửa / Gợi ý**, kèm `file:line`.

## Tài nguyên
- `references/` — checklist & ví dụ anti-pattern (bổ sung dần).
- `scripts/` — script hỗ trợ (vd grep nhanh các vi phạm phổ biến).
