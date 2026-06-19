---
name: security-audit
description: Rà soát bảo mật cho dự án POS (.NET) — JWT/secret, RBAC & thao tác nhạy cảm, mã hóa SQLite, TLS, AuditLog, an toàn dữ liệu HĐĐT. Dùng khi review bảo mật hoặc trước release.
---

# Skill: Security Audit (POS .NET)

## Khi nào dùng
Trước release, hoặc khi đụng tới auth, lưu trữ token, dữ liệu nhạy cảm, tích hợp HĐĐT.

## Checklist (Technical.md mục 17 + BusinessRules.md)
- **Secret**: không có secret/SecretKey/mật khẩu DB trong source/commit; dùng User Secrets / biến môi trường / Key Vault.
- **JWT**: access token ngắn hạn + refresh; lưu token theo OS qua `ISecureStorage` (DPAPI/Keychain/libsecret).
- **RBAC**: thao tác nhạy cảm (hủy đơn, sửa giá, mở két, hoàn tiền, giảm giá vượt ngưỡng) yêu cầu PIN/quyền Manager + **AuditLog** (BusinessRules.md B2).
- **TLS** cho mọi giao tiếp API; cân nhắc **SQLCipher** cho SQLite local.
- **HĐĐT/EasyInvoice**: credential để ngoài cấu hình; không log token/PII; idempotency theo `Order.Id`.
- **Input validation**: chống injection ở API; không tin dữ liệu client khi sync.

## Output
Liệt kê phát hiện theo mức độ rủi ro (Cao/Trung/Thấp) + vị trí + cách khắc phục.
