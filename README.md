# Sales Management Software (POS)

Phần mềm **POS bán lẻ đa nền tảng** (Windows / macOS / Linux), chạy **offline-first**, đồng bộ khi có mạng. Thị trường **Việt Nam** — hỗ trợ **hóa đơn điện tử khởi tạo từ máy tính tiền kết nối thuế** (EasyInvoice / SoftDreams).

> Stack: .NET 8 · Avalonia UI 11 · ASP.NET Core 8 · EF Core 8 · PostgreSQL 16 · SQLite · SignalR · Hangfire.

## Tài liệu

| File | Nội dung |
|---|---|
| [`CLAUDE.md`](./CLAUDE.md) | Ngữ cảnh & quy ước BẮT BUỘC cho AI/dev |
| [`docs/Technical.md`](./docs/Technical.md) | Kiến trúc, build/đóng gói đa nền tảng, sync, phần cứng, CI/CD, bảo mật |
| [`docs/BusinessRules.md`](./docs/BusinessRules.md) | Nghiệp vụ bán lẻ (B1–B14) + ERD + tích hợp HĐĐT |
| [`docs/Remember.md`](./docs/Remember.md) | Nhật ký cài đặt & môi trường |

## Getting Started

```bash
# 1. Sao chép biến môi trường
cp .env.example .env        # rồi điền secret (DB, JWT, EasyInvoice)

# 2. Hạ tầng local (PostgreSQL + Redis)
cd deploy && docker compose up -d && cd ..

# 3. Restore & build
dotnet restore
dotnet build

# 4. Chạy
dotnet run --project src/Pos.Api          # API  → Swagger https://localhost:5001/swagger
dotnet run --project src/Pos.Client.UI    # POS client (Avalonia)

# 5. Test
dotnet test
```

> Solution & cấu trúc project chưa được khởi tạo — chạy slash command `/bootstrap` (xem `.claude/commands/bootstrap.md`).

## Yêu cầu môi trường

.NET SDK 8.0 · Docker Desktop · Avalonia templates (`dotnet new install Avalonia.Templates`). Chi tiết & trạng thái máy: [`docs/Technical.md`](./docs/Technical.md) mục 3 và [`docs/Remember.md`](./docs/Remember.md).

## Bảo mật

KHÔNG commit secret. Dùng `.env` / User Secrets / biến môi trường. Hook quét secret trước commit: `.claude/hooks/pre-commit` (bật bằng `git config core.hooksPath .claude/hooks`).

## Quy ước Git

Branch: `main` (release) · `develop` · `feature/*` · `hotfix/*`. Commit theo **Conventional Commits**.