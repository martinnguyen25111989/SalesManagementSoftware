---
description: Review diff hiện tại theo quy ước dự án POS
---

Review thay đổi đang có (`git diff`) theo các quy ước BẮT BUỘC trong `CLAUDE.md`. Tập trung:

- **PK GUID** (không INT auto-increment) cho bảng giao dịch.
- **Tiền dùng `decimal`**, làm tròn/thuế đúng B13.
- **Tồn kho append-only**, không update trực tiếp số tồn.
- **Idempotency-Key** trên API tạo dữ liệu.
- **HAL**: không gọi OS/phần cứng trực tiếp từ ViewModel.
- **HĐĐT**: không sửa/xóa hóa đơn đã phát hành.
- Không hardcode đường dẫn, không commit secret.
- MVVM: View không chứa logic nghiệp vụ.

Báo lỗi đúng/sai + vị trí `file:line`. Phân loại theo mức độ (chặn release / nên sửa / gợi ý).
