# Remember.md — Nhật ký cài đặt & môi trường

> File ghi nhớ những gì đã cài / cấu hình cho dự án POS. **Ghi 1 entry sau mỗi lần cài đặt** để không phải dò lại.
> Liên quan: [`Technical.md`](./Technical.md) (mục 3 & 5 — yêu cầu môi trường) · [`BusinessRules.md`](./BusinessRules.md).

---

## Thông tin máy

| Mục | Giá trị |
|---|---|
| OS | macOS 15.7.4 (build 24G517) |
| Kiến trúc | **x86_64 (Intel Mac)** → RID build: `osx-x64` |
| Thư mục dự án | `/Users/martinguyen/SalesManagementSoftware` |

> Lưu ý: máy Intel → khi publish client dùng `-r osx-x64`. Nếu sau này build cho máy Apple Silicon, thêm `osx-arm64` (xem Technical.md mục 11).

---

## Trạng thái môi trường (cập nhật mới nhất: 2026-06-18)

| Công cụ | Yêu cầu | Đã có | Trạng thái |
|---|---|---|---|
| Homebrew | — | 5.1.14 | ✅ |
| Git | bất kỳ | 2.24.3 (Apple Git) | ✅ (cũ nhưng chạy được) |
| .NET SDK | 8.0 (LTS) | **8.0.123** + 10.0.202 | ✅ |
| Docker | Desktop | 29.2.1 (daemon đang chạy) | ✅ |
| Docker Compose | v2+ | v5.0.2 | ✅ |
| Avalonia templates | có | `avalonia.app/mvvm/xplat` | ✅ (cài 2026-06-18) |

➡️ **Môi trường dev đã đủ để khởi tạo solution.**

---

## ✅ Checklist hôm nay (2026-06-18)

**Đã xong:**
- [x] Kiểm tra & cài môi trường: Homebrew, Git, .NET 8.0.123, Docker 29.2.1 + Compose, Avalonia templates
- [x] Cấu trúc `.claude/` + `CLAUDE.md` (commands, skills, agents, plugins, settings, hooks)
- [x] Chuẩn hóa: agents `.yml`→`.md`, gộp `docs/`, `.env.example`, `README.md`, hook `pre-commit`, `.gitignore`
- [x] Bootstrap solution: `global.json`, `Directory.Build.props`, `PosSystem.sln` + **13 project** + references (clean architecture)
- [x] Hạ tầng local: `docker-compose` Postgres (**5433**) + Redis (6379) đang chạy; `api.Dockerfile`; build scripts; CI `build.yml`
- [x] **Domain 34 entity** theo ERD B14 (`Pos.Domain`)
- [x] `PosDbContext` (34 DbSet) + design-time factory (`Pos.Infrastructure`); `AddDbContext` ở `Pos.Api`
- [x] Migration `InitialCreate` → áp DB → **35 bảng** trong `posdb`
- [x] **HAL interfaces**: `IReceiptPrinter`, `ICashDrawer`, `IBarcodeScanner`
- [x] Build **13/13 project, 0 warning/error**; test **3/3 pass**

**Chưa làm / cần quyết định:**
- [ ] `git init` + bật hook: `git config core.hooksPath .claude/hooks`
- [ ] Quyết định **Avalonia 11 vs 12** và đồng bộ lại `Technical.md` (hiện thực tế là 12)
- [ ] Trả lại Postgres về cổng **5432** nếu giải phóng được (hiện dùng 5433)
- [ ] Lấy **tài khoản test EasyInvoice** + tài liệu API (B11-A.9)
- [ ] Seed `Role`/`Permission` (5 role ở B2) + dữ liệu mẫu
- [ ] HAL implementation đầu tiên: `NetworkPrinter` (ESC/POS qua TCP 9100)
- [ ] CQRS handlers ở `Pos.Application` (MediatR) cho luồng tạo Order
- [ ] Tách `IEntityTypeConfiguration<T>` khi domain lớn lên

---

## Nhật ký cài đặt (mới nhất ở trên)

### 2026-06-18 — EF Core domain + DbContext + migration đầu tiên + HAL
- **Domain (`Pos.Domain`)**: 34 entity theo ERD B14, gom theo module (Common/Catalog/Sales/Operations/Promotions/Inventory/Returns/Organization/Customers/Invoicing). Base: `EntityBase` (GUID PK + cột sync) và `TransactionEntity` (+StoreId/DeviceId). Tiền dùng `decimal`; enum thuế `VatRate` (0/5/8/10/KCT/KKKNT).
- **`Pos.Infrastructure/Persistence`**: `PosDbContext` (34 DbSet) + `PosDbContextFactory` (design-time, conn localhost:5433). Cấu hình: decimal mặc định (18,2), số lượng (18,3); unique index Barcode.Code / Customer.Phone / StockBalance(Variant,Store); **DeleteBehavior.Restrict toàn bộ** (tránh cascade vòng).
- **Packages (pin EF 8)**: `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.10` (Infrastructure), `Microsoft.EntityFrameworkCore.Design 8.0.10` (Api). Tool: `dotnet-ef 8.0.10` (global).
- **`Pos.Api/Program.cs`**: `AddDbContext<PosDbContext>(UseNpgsql)` — conn từ config, fallback localhost:5433.
- **HAL (`Pos.Hardware.Abstractions`)**: `IReceiptPrinter` (+`ReceiptDocument`/`ReceiptLine`), `ICashDrawer`, `IBarcodeScanner`.
- **Migration**: `InitialCreate` → `dotnet ef database update` → **35 bảng** trong `posdb` (34 entity + __EFMigrationsHistory). Build 13/13 OK.
- **Lệnh migration** (nhớ export PATH tool): `export PATH="$PATH:$HOME/.dotnet/tools"` rồi `dotnet ef migrations add <Name> --project src/Pos.Infrastructure --startup-project src/Pos.Api --output-dir Persistence/Migrations`.
- **Bước tiếp theo**: EF Core configurations tách riêng (IEntityTypeConfiguration) nếu cần; seed dữ liệu Role/Permission; HAL implementation (NetworkPrinter qua TCP 9100); bắt đầu CQRS handlers ở `Pos.Application`.

### 2026-06-18 — Khởi tạo solution PosSystem + hạ tầng local (bootstrap)
- **`global.json`** pin SDK `8.0.0` (rollForward latestFeature) → `dotnet` dùng **8.0.123** trong repo.
- **`Directory.Build.props`**: `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`.
- **`PosSystem.sln`** + **13 project** (10 src + 3 tests) theo Technical.md mục 4; wire reference theo clean architecture.
- **Sửa quan trọng:** template Avalonia tạo `Pos.Client.UI` nhắm **net10.0 + Avalonia 12** → đổi TFM về **net8.0** (Avalonia 12.0.4 vẫn hỗ trợ net8.0). *Lưu ý: Technical.md ghi "Avalonia 11" — thực tế template hiện tại là 12; cân nhắc cập nhật doc hoặc hạ về 11 nếu cần.*
- **Build:** `dotnet build` → **13/13 project, 0 warning, 0 error**. `dotnet test` → 3/3 project pass.
- **Hạ tầng:** `deploy/docker-compose.yml` (Postgres 16 + Redis 7) + `api.Dockerfile`. **Postgres host port đổi 5432 → 5433** (5432 đã bị chiếm); tham số hóa qua `POSTGRES_PORT`. Đã cập nhật `.env.example`.
  - Postgres: `localhost:5433` (posdb/posuser) — `pg_isready` OK. Redis: `localhost:6379` — PONG.
- **Thêm:** `build/build-macos.sh|build-linux.sh|build-windows.ps1`, `.github/workflows/build.yml` (matrix Win/macOS/Linux).
- **Bước tiếp theo:** thiết kế EF Core entity theo ERD (BusinessRules.md B14) → migration đầu tiên; viết HAL interfaces trong `Pos.Hardware.Abstractions`.

### 2026-06-18 — Chuẩn hóa cấu trúc theo "Claude Code Project Structure"
- **Agents `.yml` → `.md`** (đúng chuẩn Claude Code nhận diện): `code-reviewer`, `test-writer`, `security-auditor`, `devops-sre`; `model: opus`. Xóa 4 file `.yml` cũ.
- **Gộp doc vào `docs/`**: di chuyển `Technical.md`, `BusinessRules.md`, `Remember.md` (file này) vào `docs/`; cập nhật link trong `CLAUDE.md`.
- **Thêm:** `.env.example` (DB/JWT/EasyInvoice), `README.md` (getting started), `.claude/hooks/pre-commit` (quét secret, đã `chmod +x`).
- **`.gitignore`:** bổ sung `.env`.
- **Bật hook khi đã `git init`:** `git config core.hooksPath .claude/hooks`.
- **Bước tiếp theo:** `/bootstrap` để tạo `PosSystem.sln` + `src/`, `tests/`, `deploy/docker-compose.yml`.

### 2026-06-18 — Tạo cấu trúc cấu hình `.claude/` + CLAUDE.md
- **Tạo mới:**
  - `CLAUDE.md` (ngữ cảnh dự án + quy ước BẮT BUỘC), `.mcp.json`, `.gitignore`.
  - `.claude/settings.json` (allow lệnh dotnet/git/docker, deny đọc secret), `.claude/settings.local.json` (không commit).
  - `.claude/commands/`: `bootstrap`, `test-all`, `review`, `deploy`.
  - `.claude/skills/`: `code-review` (+ scripts/references/assets), `test-writer`, `security-audit`, `refactor`.
  - `.claude/agents/`: `code-reviewer`, `test-writer`, `security-auditor`, `devops-sre`.
  - `.claude/plugins/`: `manifest.json` + `my-plugin/plugin.json`.
- **Ghi chú:** nội dung điền theo nghiệp vụ POS (tham chiếu Technical.md & BusinessRules.md), không để file rỗng.
- **Bước tiếp theo:** chạy `/bootstrap` để khởi tạo solution `PosSystem.sln` + cấu trúc project.

### 2026-06-18 — Kiểm tra & hoàn thiện môi trường ban đầu
- **Kiểm tra:** đã có sẵn Homebrew, Git, .NET 8.0.123 (+10), Docker 29.2.1 + Compose, daemon chạy.
- **Cài mới:** Avalonia templates
  ```bash
  dotnet new install Avalonia.Templates
  ```
  → có `avalonia.app`, `avalonia.mvvm`, `avalonia.xplat`.
- **Ghi chú:** máy là **Intel (x86_64)** → publish macOS dùng `osx-x64`.
- **Chưa làm (bước tiếp theo gợi ý):**
  - [ ] Khởi tạo solution `PosSystem.sln` + cấu trúc project (Technical.md mục 4)
  - [ ] Tạo `global.json` pin SDK 8.0 (Technical.md mục 5.4)
  - [ ] Tạo `deploy/docker-compose.yml` (PostgreSQL + Redis) và `docker compose up -d`
  - [ ] Lấy tài khoản **test EasyInvoice** + tài liệu API (BusinessRules.md B11-A.9)

---

## Mẫu entry cho lần sau (copy xuống dưới phần Nhật ký)

```
### YYYY-MM-DD — <việc đã làm>
- Cài/cấu hình: <gì>
  ```bash
  <lệnh đã chạy>
  ```
- Phiên bản / kết quả: <...>
- Lỗi gặp & cách xử lý: <nếu có>
- Bước tiếp theo: <...>
```


Lưu ý
Chưa làm / cần quyết định (8 mục): git init + bật hook · Avalonia 11 vs 12 · trả Postgres về 5432 · tài khoản test EasyInvoice · seed Role/Permission · NetworkPrinter · CQRS handlers · tách IEntityTypeConfiguration.

- Dùng EF 8 pinned (Npgsql.EFCore.PostgreSQL 8.0.10, dotnet-ef 8.0.10) cho khớp .NET 8.
- Lệnh dotnet ef cần export PATH="$PATH:$HOME/.dotnet/tools" (đã ghi vào docs/Remember.md).
- EF config hiện đặt gọn trong OnModelCreating; khi domain lớn lên nên tách IEntityTypeConfiguration<T> riêng từng entity.

Bước tiếp theo gợi ý

- Seed Role/Permission mặc định (5 role trong B2) + dữ liệu mẫu.
- HAL implementation đầu tiên: NetworkPrinter (ESC/POS qua TCP 9100) trong Pos.Hardware.MacOS.
- Bắt đầu CQRS handlers ở Pos.Application (MediatR) cho luồng tạo Order.