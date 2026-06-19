# CLAUDE.md — Sales Management Software (POS)

Hướng dẫn ngữ cảnh cho Claude khi làm việc trong repo này. Đọc kỹ trước khi sửa/đề xuất code.

## Dự án là gì

Phần mềm **POS bán lẻ đa nền tảng** (Windows/macOS/Linux), chạy **offline-first**, đồng bộ khi có mạng. Thị trường: **Việt Nam** — bắt buộc **hóa đơn điện tử khởi tạo từ máy tính tiền kết nối thuế**.

## Tài liệu nguồn (đọc trước khi code)

| File | Nội dung |
|---|---|
| [`docs/Technical.md`](./docs/Technical.md) | Kiến trúc, stack, build/đóng gói đa nền tảng, sync, phần cứng, CI/CD, bảo mật |
| [`docs/BusinessRules.md`](./docs/BusinessRules.md) | Nghiệp vụ bán lẻ (B1–B14): sản phẩm, đơn, giá–KM, thanh toán, tồn kho, HĐĐT, ERD |
| [`docs/Remember.md`](./docs/Remember.md) | Nhật ký cài đặt & trạng thái môi trường máy dev |

## Stack

.NET 8 · Avalonia UI 11 (client) · ASP.NET Core 8 (API) · EF Core 8 · PostgreSQL 16 (cloud) · SQLite (local) · SignalR · Hangfire · Serilog · JWT. NCC hóa đơn điện tử: **EasyInvoice (SoftDreams)**.

## Quy ước BẮT BUỘC (vi phạm = bug nghiêm trọng)

- **Khóa chính = GUID sinh ở client** cho mọi bảng giao dịch — KHÔNG dùng INT auto-increment (vỡ khi sync offline).
- **Tiền tệ dùng `decimal`**, KHÔNG `double/float`. Làm tròn theo BusinessRules.md **B13**.
- **Tồn kho = cộng dồn `StockTransaction` (append-only)**, không update 1 dòng tồn.
- **Idempotency-Key** trên mọi API tạo dữ liệu (order, payment, phát hành HĐĐT). Dùng `Order.Id`.
- **HAL**: mọi truy cập phần cứng (máy in, két, scanner) qua interface (`IReceiptPrinter`...), nạp theo OS runtime. KHÔNG gọi OS trực tiếp từ ViewModel.
- **Không hardcode đường dẫn** (`C:\`, `/Users/`): dùng `Path.Combine` + `Environment.SpecialFolder`.
- **MVVM**: View (XAML) không chứa logic nghiệp vụ.
- **HĐĐT là tuân thủ pháp lý**: không sửa/xóa hóa đơn đã phát hành — chỉ điều chỉnh/thay thế/hủy (BusinessRules.md B11/B11-A).
- **Không commit secret**: dùng User Secrets / biến môi trường.

## Lệnh hay dùng

```bash
dotnet build                              # build solution
dotnet test                               # chạy toàn bộ test
dotnet run --project src/Pos.Api          # chạy API (Swagger :5001)
dotnet run --project src/Pos.Client.UI    # chạy POS client
dotnet format                             # format trước khi commit
cd deploy && docker compose up -d         # PostgreSQL + Redis local
```

## Môi trường máy hiện tại

Intel Mac (x86_64), macOS 15.7.4 → publish macOS dùng RID **`osx-x64`**. Chi tiết: `docs/Remember.md`.

## Cấu trúc `.claude/`

- `commands/` — slash command nội bộ dự án (`/bootstrap`, `/test-all`, `/review`, `/deploy`).
- `skills/` — kỹ năng chuyên biệt (code-review, test-writer, security-audit, refactor).
- `agents/` — định nghĩa sub-agent.
- `settings.json` — cấu hình chia sẻ cả team (commit). `settings.local.json` — cấu hình máy cá nhân (KHÔNG commit).

## Git

Branch: `main` (release) · `develop` · `feature/*` · `hotfix/*`. Commit theo **Conventional Commits** (`feat:`, `fix:`, `refactor:`, `docs:`, `test:`).
