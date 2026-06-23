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

### 2026-06-19 — Seed RBAC + HAL NetworkPrinter + CQRS tạo Order
- **Seed (B2):** `Pos.Infrastructure/Persistence/Seed/DbSeeder.cs` + `SeedIds.cs` — 5 role
  (Owner/Manager/Cashier/Warehouse/Accountant), 13 permission theo hành động, mapping role→permission,
  và dữ liệu mẫu (Tenant/Store HCM01/Register/2 User/2 sản phẩm + bảng giá). Idempotent, GUID cố định.
  Gọi tự động khi API chạy ở Development (`Program.cs`, sau `MigrateAsync`).
- **HAL (Technical §10):** `Pos.Hardware.MacOS` — `NetworkReceiptPrinter` (IReceiptPrinter, ESC/POS qua
  TCP 9100), `NetworkCashDrawer` (ICashDrawer, kick qua máy in), `EscPosWriter` (fluent, gồm QR & cắt giấy),
  `ReceiptRenderer`. *Chưa map dấu tiếng Việt* (mặc định Latin1) — việc làm tiếp.
- **CQRS (MediatR):** `Pos.Application` — `IPosDbContext` (PosDbContext hiện thực), `OrderCalculator`
  (B5/B13: giá → CK dòng → CK tổng phân bổ → VAT theo thuế suất → làm tròn), `CreateOrderCommand/Handler`
  (Draft, idempotency = Order.Id, validate ca mở, chưa trừ tồn). `OrdersController` POST /api/orders.
  5 unit test cho OrderCalculator (đa thuế suất, CK tổng không lệch 1đ, làm tròn tiền mặt) — pass.
- **Lưu ý va chạm:** thêm type vào namespace `Pos.Application` làm `App : Application` (Avalonia) trong
  `Pos.Client.UI` bị nhập nhằng → đã qualify `Avalonia.Application`.
- **Bước tiếp theo:** checkout (thanh toán + trừ tồn StockTransaction) · phát hành HĐĐT EasyInvoice ·
  đăng ký DI máy in theo runtime ở Client.UI · map code page tiếng Việt cho máy in.

---

### 2026-06-19 (tiếp) — Checkout flow: thanh toán + trừ tồn (B6/B8/B9)
- **CQRS:** `Pos.Application/Orders/CheckoutOrder/` — `CheckoutOrderCommand/Handler`: Draft → Completed,
  ghi `Payment` (đa phương thức, tổng = GrandTotal), trừ tồn append-only `StockTransaction` (Sale, QtyChange âm)
  + cập nhật snapshot `StockBalance`, cộng tiền mặt vào `Shift.ExpectedCash` (B9). Idempotent theo OrderId
  (đơn đã Completed → trả kết quả cũ, không trừ tồn/thu tiền lần hai). Tính tiền thối + cờ mở két (B6).
- **API:** `POST /api/orders/{id}/checkout`. `IPosDbContext` thêm `StockTransactions`, `StockBalances`.
- **Bug bắt được nhờ test:** PK GUID client-gen (non-default) → thêm child chỉ qua navigation bị EF coi là
  Modified (lỗi khi lưu, cả PostgreSQL). Sửa: `Add` tường minh cho `Payment`. Lưu ý chung cho mọi entity con.
- **Test:** thêm `Microsoft.EntityFrameworkCore.InMemory` + `TestPosDbContext`/`TestData` (giữ tách tầng,
  không cần Infrastructure). 9 handler test mới (CreateOrder idempotency/ca đóng/override giá; Checkout trừ tồn,
  lệch tiền, hỗn hợp, idempotency không trừ tồn 2 lần). Tổng 16 test pass.
- **Bước tiếp theo:** OpenShift/CloseShift (X/Z report) · Hold/Resume · phát hành HĐĐT EasyInvoice (B11-A) ·
  trả hàng (B7) · DI máy in theo runtime ở Client.UI.

---

### 2026-06-19 (tiếp 2) — B9: Ca làm việc & Quỹ tiền (Shift / Cash)
- **CQRS (MediatR):** `Pos.Application/Shifts/`
  - `OpenShift` — mở ca, ExpectedCash = OpeningFloat; chặn mở 2 ca cùng 1 Register; idempotent theo ShiftId.
  - `CloseShift` — Variance = CountedCash − ExpectedCash; chặn nếu còn đơn Hold; lệch > ngưỡng 50k cần
    ManagerApproved (B2); idempotent; trả **Z-report**.
  - `RecordCashMovement` — thu/chi tiền mặt, điều chỉnh ExpectedCash (In +, Out −); Add tường minh (GUID PK).
  - `GetShiftReport` (query) + `ShiftReportBuilder` — **X-report** (đang mở) / **Z-report** (đã đóng):
    cash sales, cash in/out, ExpectedCash, OrderCount, GrandTotalSales, payments theo phương thức.
- **API:** `ShiftsController` — POST `/api/shifts/open`, `/{id}/close`, `/{id}/cash-movements`; GET `/{id}/report`.
- **IPosDbContext** thêm `CashMovements`, `Registers`.
- **Lưu ý:** namespace `Pos.Application.Shifts.CashMovement` trùng tên entity `CashMovement` → đổi namespace
  thành `CashMovements` (plural) để khỏi nhập nhằng alias.
- **Test:** +11 (open/cash-movement/close/X-report). Tổng **27 test pass**.
- **Bước tiếp theo:** Hold/Resume đơn (B4) · phát hành HĐĐT EasyInvoice (B11-A) · trả hàng (B7) ·
  DI máy in theo runtime ở Client.UI · CashRefund (B7) vào ExpectedCash của ca.

---

### 2026-06-19 (tiếp 3) — B4: Vòng đời đơn (Hold / Resume / Void)
- **CQRS (MediatR):** `Pos.Application/Orders/`
  - `HoldOrder` — Draft → OnHold; idempotent; chặn nếu không phải Draft.
  - `ResumeOrder` — OnHold → Draft; idempotent.
  - `VoidOrder` — Draft/OnHold → Voided; cần `ManagerApproved` + `Reason` (B2); CHẶN đơn đã
    Completed/Returned (phải hủy qua HĐĐT B11); idempotent. (AuditLog: bổ sung khi có module Audit.)
  - Hoàn tất B4 state machine (Draft/OnHold/Completed/Voided/Returned).
- **Chung:** thêm `EntityExtensions.MarkModified()` (cập nhật LastModifiedUtc + Version + SyncStatus=Pending)
  gom logic đánh dấu sửa cho offline-sync.
- **API:** `OrdersController` thêm POST `/{id}/hold`, `/{id}/resume`, `/{id}/void`; gom try/catch vào helper `Run`.
- **Test:** +12 (hold/resume/void: happy, idempotent, chặn completed, thiếu quyền/lý do). Tổng **39 test pass**.
- **Bước tiếp theo:** AuditLog (cross-cutting cho void/return/giảm giá/lệch quỹ) · trả hàng (B7) ·
  phát hành HĐĐT EasyInvoice (B11-A) · DI máy in theo runtime ở Client.UI.

---

### 2026-06-19 (tiếp 4) — B5 Khuyến mãi + B6 Thanh toán (hardening)
- **B5 — `PromotionEngine`** (`Pos.Application/Pricing/PromotionEngine.cs`, thuần, không I/O):
  hỗ trợ LinePercent/LineAmount, OrderPercent/OrderAmount, QtyTierPercent, MemberPercent,
  VoucherPercent/Amount. Điều kiện: time window, MinOrderValue, hạng KH, voucher (hạn/min/lượt dùng —
  trả lý do từ chối). Gộp KM: duyệt theo Priority giảm dần, **dừng sau KM non-stackable** (loại trừ).
  Clamp dòng & tổng không âm; làm tròn đồng. (BOGO/Combo để sau — cần thêm dòng quà tặng.)
- **Tích hợp:** `CreateOrderCommand` thêm `Promotions`, `VoucherCode`, `CustomerTierId`, `ManagerApproved`.
  Handler chạy engine → gộp CK tay + KM theo dòng, áp CK tổng; **chặn CK tay dòng > 10% nếu chưa duyệt**
  (B2/B5); từ chối khi voucher không hợp lệ. OrderLine lưu CK đã gộp.
- **B6 — Checkout hardening:** thẻ/QR/ví phải có `ExternalRef` (xác nhận đã nhận tiền — không tự "Paid",
  B6 edge "QR chưa về"); chặn payment ≤ 0. (Phần lõi B6: hỗn hợp/khớp GrandTotal/tiền thối/mở két đã có.)
- **Test:** +16 (engine: line/order/qtytier/member/voucher/exclusive/clamp; tích hợp CreateOrder + ngưỡng
  CK tay; B6 QR có/không ref). Tổng **55 test pass**.
- **Bước tiếp theo:** BOGO/Combo (thêm dòng quà) · seed Promotion mẫu + map JSON→PromotionDef ·
  trả hàng (B7) · HĐĐT EasyInvoice (B11-A) · AuditLog.

---

### 2026-06-19 (tiếp 5) — B7: Trả hàng / Hoàn tiền (Return / Refund)
- **CQRS:** `Pos.Application/Returns/CreateReturn/` — `CreateReturnCommand/Handler`:
  - Tham chiếu hóa đơn gốc (phải Completed/PartiallyReturned); **không trả quá số đã mua** (cộng dồn
    các phiếu trả trước); cần `ManagerApproved` + lý do (B7); idempotent theo ReturnId.
  - **Hoàn tiền theo tỷ lệ** trên `OrderLine.LineTotal` (đã gồm CK tổng phân bổ + VAT) → đúng cả khi đơn
    có KM tổng (B7 edge).
  - **B8:** nhập lại tồn append-only (`StockTransaction` Type=Return, +qty) + cập nhật `StockBalance`;
    `RestockToInventory=false` cho hàng lỗi → không nhập lại.
  - **B9:** hoàn tiền mặt giảm `Shift.ExpectedCash`.
  - **B4:** cập nhật hóa đơn gốc → Returned (toàn phần) / PartiallyReturned; PaymentStatus=Refunded.
- **API:** `ReturnsController` POST `/api/returns`. `IPosDbContext` thêm `ReturnOrders`, `ReturnLines`.
- **Chưa làm (TODO trong code):** HĐĐT điều chỉnh/thay thế (B11), AuditLog, tính lại điểm/công nợ (B10),
  đổi hàng (compose: trả + tạo đơn mới), ReturnOrder chưa có ShiftId → report CashRefunds vẫn 0
  (ExpectedCash đã phản ánh đúng cho variance).
- **Test:** +8 (toàn phần/một phần/prorate, quá số, cộng dồn, hàng lỗi không nhập kho, chưa duyệt/thiếu lý do,
  đơn chưa hoàn tất, idempotent). Tổng **63 test pass**.
- **Bước tiếp theo:** HĐĐT EasyInvoice (B11-A) + chứng từ điều chỉnh khi trả hàng · AuditLog · B10 loyalty/công nợ.

---

### 2026-06-19 (tiếp 6) — B8: Tồn kho & Kho (Inventory)
- **`StockLedger`** (`Pos.Application/Inventory/StockLedger.cs`): helper lõi B8 — append 1 StockTransaction
  + cập nhật snapshot StockBalance (Local-first để nhiều dòng cùng variant không tạo snapshot trùng).
  Dùng chung cho bán/trả/nhập/kiểm kê/chuyển kho. Đã refactor Checkout & Return dùng helper này.
- **CQRS:** `Pos.Application/Inventory/`
  - `ReceiveStock` (GRN) — PurchaseReceipt + GrnLines + StockTransaction(Purchase,+) có giá vốn; total; idempotent.
  - `AdjustStock` (kiểm kê) — đếm thực tế → biến động chênh lệch (StockTake, ±); cần Manager + lý do (B2); idempotent.
  - `TransferStock` — 2 vế (TransferOut −, TransferIn +) trong cùng SaveChanges (không mất hàng giữa 2 kho); idempotent.
  - `GetStockOnHand` (query) — snapshot StockBalance, hoặc `FromLedger=true` cộng dồn StockTransaction để đối soát.
- **API:** `InventoryController` — POST receive/adjust/transfer, GET on-hand. `IPosDbContext` thêm Suppliers,
  PurchaseReceipts, GrnLines.
- **Test:** +11 (nhập tăng tồn/giá vốn/idempotent/multi-line 1 balance; kiểm kê chênh lệch/quyền/idempotent;
  chuyển kho 2 vế/cùng kho/idempotent; on-hand snapshot==ledger). Tổng **74 test pass**.
- **Chưa làm (TODO):** giá vốn bình quân gia quyền/FIFO + COGS khi bán (cần field cost trên balance → migration) ·
  reorder point / cảnh báo hết hàng / hạn dùng (cần field) · chặn bán âm kho theo cấu hình (ở checkout) ·
  UnitConversion (bán/nhập theo đơn vị quy đổi).
- **Bước tiếp theo:** HĐĐT EasyInvoice (B11-A) · AuditLog · B10 loyalty/công nợ · giá vốn & lãi gộp.

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
Chưa làm / cần quyết định (5 mục): git init + bật hook · Avalonia 11 vs 12 · trả Postgres về 5432 · tài khoản test EasyInvoice · tách IEntityTypeConfiguration.
Đã xong (2026-06-19): seed Role/Permission · NetworkPrinter (HAL) · CQRS tạo Order.

- Dùng EF 8 pinned (Npgsql.EFCore.PostgreSQL 8.0.10, dotnet-ef 8.0.10) cho khớp .NET 8.
- Lệnh dotnet ef cần export PATH="$PATH:$HOME/.dotnet/tools" (đã ghi vào docs/Remember.md).
- EF config hiện đặt gọn trong OnModelCreating; khi domain lớn lên nên tách IEntityTypeConfiguration<T> riêng từng entity.
