# Technical.md — Phần mềm Quản lý Bán hàng (POS) Đa nền tảng

> Tài liệu hướng dẫn phát triển, build và vận hành phần mềm POS desktop chạy trên **Windows / macOS / Linux**.
> Stack tham chiếu: **.NET 8 + Avalonia UI + ASP.NET Core + PostgreSQL + SQLite (Offline-First)**.

---

> 📘 **Nghiệp vụ & Domain (bán lẻ, business rule, ERD)** được tách sang tài liệu riêng: [`BusinessRules.md`](./BusinessRules.md). File này chỉ chứa nội dung **kỹ thuật & vận hành**.

## Mục lục

1. [Tổng quan dự án](#1-tổng-quan-dự-án)
2. [Kiến trúc đa nền tảng](#2-kiến-trúc-đa-nền-tảng)
3. [Yêu cầu môi trường](#3-yêu-cầu-môi-trường)
4. [Cấu trúc thư mục dự án](#4-cấu-trúc-thư-mục-dự-án)
5. [Cài đặt môi trường phát triển](#5-cài-đặt-môi-trường-phát-triển)
6. [Cấu hình (Configuration)](#6-cấu-hình-configuration)
7. [Database & Migration](#7-database--migration)
8. [Chạy ứng dụng (Development)](#8-chạy-ứng-dụng-development)
9. [Offline-First & Đồng bộ dữ liệu](#9-offline-first--đồng-bộ-dữ-liệu)
10. [Tích hợp phần cứng POS theo nền tảng](#10-tích-hợp-phần-cứng-pos-theo-nền-tảng)
11. [Build & Đóng gói theo nền tảng](#11-build--đóng-gói-theo-nền-tảng)
12. [Code signing & Phân phối](#12-code-signing--phân-phối)
13. [Auto-update](#13-auto-update)
14. [Testing](#14-testing)
15. [CI/CD](#15-cicd)
16. [Quy ước code & Git](#16-quy-ước-code--git)
17. [Bảo mật](#17-bảo-mật)
18. [Troubleshooting đa nền tảng](#18-troubleshooting-đa-nền-tảng)

---

## 1. Tổng quan dự án

Phần mềm POS dành cho cửa hàng bán lẻ, cafe, nhà hàng, siêu thị mini và chuỗi cửa hàng. Hoạt động **cả Online lẫn Offline**, đồng bộ dữ liệu khi có mạng.

**Mục tiêu đa nền tảng:** một codebase duy nhất build ra app native cho Windows, macOS (Intel + Apple Silicon) và Linux.

| Thành phần | Công nghệ | Ghi chú đa nền tảng |
|---|---|---|
| POS Client (Desktop) | Avalonia UI 11 (.NET 8) | Win / macOS / Linux từ 1 codebase |
| Backend API | ASP.NET Core 8 | Chạy Docker, đa OS |
| DB Cloud | PostgreSQL 16 | — |
| DB Local | SQLite | Embedded, không cần cài |
| ORM | Entity Framework Core 8 | — |
| Auth | JWT | — |
| Realtime | SignalR | Đẩy thông báo, đồng bộ |
| Logging | Serilog | — |
| Background Job | Hangfire | Email, báo cáo, sync |

> **Vì sao Avalonia (không phải MAUI):** Avalonia hỗ trợ macOS và Linux desktop trưởng thành hơn .NET MAUI (MAUI desktop mạnh trên Windows nhưng yếu trên macOS, không có Linux). Với POS desktop đa nền tảng thực thụ, Avalonia là lựa chọn ổn định nhất trong hệ sinh thái .NET.

---

## 2. Kiến trúc đa nền tảng

```
┌───────────────────────────────────────────────┐
│   POS Client (Avalonia UI)                     │
│   Windows  |  macOS (x64/arm64)  |  Linux      │
│   ├── Presentation (Views / ViewModels - MVVM) │
│   ├── Local Domain Logic                       │
│   ├── SQLite Local DB                           │
│   └── Hardware Abstraction Layer (HAL) ◄──┐    │
└──────────────┬────────────────────────────┼────┘
               │ HTTPS / SignalR             │
               ▼                             │
┌───────────────────────────────────────┐   │  Platform-specific
│   ASP.NET Core API                    │   │  drivers nạp runtime
│   ├── API Layer (Controllers)         │   │  qua interface chung
│   ├── Application (CQRS + MediatR)    │   │
│   ├── Domain                          │   │
│   └── Infrastructure (EF Core)        │   │
└──────────────┬────────────────────────┘   │
               ▼                             │
   PostgreSQL  +  Redis  +  Hangfire ◄───────┘
```

**Nguyên tắc cốt lõi để đa nền tảng không vỡ:**

- **Hardware Abstraction Layer (HAL):** Mọi truy cập phần cứng (máy in, két tiền, scanner, cân) đi qua **interface chung** (`IReceiptPrinter`, `ICashDrawer`, `IBarcodeScanner`...). Mỗi nền tảng có một implementation riêng, được nạp runtime bằng DI dựa trên `RuntimeInformation.IsOSPlatform(...)`.
- **Không hardcode đường dẫn:** dùng `Path.Combine` và `Environment.SpecialFolder`, không bao giờ viết `C:\` hay `/Users/`.
- **Phân tách rõ Core (chia sẻ) và Platform (riêng):** logic nghiệp vụ nằm trong project độc lập nền tảng; chỉ phần phần cứng/OS mới tách theo nền tảng.

---

## 3. Yêu cầu môi trường

### Phần mềm bắt buộc (mọi nền tảng)

- **.NET SDK 8.0** (LTS) — https://dotnet.microsoft.com/download
- **Git**
- **Docker Desktop** (chạy PostgreSQL, Redis local)
- **IDE:** JetBrains Rider (khuyến nghị, tốt nhất cho Avalonia đa nền tảng) hoặc Visual Studio 2022 (Windows) / VS Code

### Yêu cầu riêng theo nền tảng build

| Nền tảng đích | Máy build cần | Ghi chú |
|---|---|---|
| Windows (.exe / .msi) | Windows hoặc cross-build từ Linux | MSI cần build trên Windows |
| macOS (.app / .dmg / .pkg) | **Bắt buộc máy macOS** | Code signing & notarization chỉ chạy trên macOS |
| Linux (.deb / .rpm / AppImage) | Linux hoặc Docker | — |

> **Lưu ý quan trọng:** Bạn **không thể** notarize app macOS từ máy Windows/Linux. Cần ít nhất 1 máy Mac (hoặc CI runner macOS như GitHub Actions `macos-latest`) trong pipeline.

### Tài khoản & chứng chỉ cần chuẩn bị

- **Apple Developer Program** ($99/năm) — để ký và notarize app macOS.
- **Code signing certificate cho Windows** (OV/EV từ DigiCert, Sectigo...) — tránh cảnh báo SmartScreen.
- (Linux không bắt buộc ký, nhưng nên cung cấp checksum SHA256.)

---

## 4. Cấu trúc thư mục dự án

```
PosSystem/
├── src/
│   ├── Pos.Domain/                  # Entities, value objects, domain logic (thuần, không phụ thuộc OS)
│   ├── Pos.Application/             # CQRS handlers, MediatR, DTO, validation
│   ├── Pos.Infrastructure/         # EF Core, repositories, external services
│   │
│   ├── Pos.Client.Core/            # ViewModels, services chia sẻ cho client (MVVM, không UI cụ thể)
│   ├── Pos.Client.UI/              # Avalonia Views, XAML, styles (chạy mọi nền tảng)
│   │   ├── App.axaml
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   └── Assets/
│   │
│   ├── Pos.Hardware.Abstractions/  # Interfaces: IReceiptPrinter, ICashDrawer, IBarcodeScanner...
│   ├── Pos.Hardware.Windows/       # Driver in/két tiền cho Windows
│   ├── Pos.Hardware.MacOS/         # Driver cho macOS (in qua socket/IP)
│   │
│   ├── Pos.Api/                    # ASP.NET Core Web API
│   └── Pos.Sync/                   # Sync engine (offline-first)
│
├── tests/
│   ├── Pos.Domain.Tests/
│   ├── Pos.Application.Tests/
│   └── Pos.Sync.Tests/
│
├── build/                          # Scripts đóng gói theo nền tảng
│   ├── build-windows.ps1
│   ├── build-macos.sh
│   
│
├── deploy/
│   ├── docker-compose.yml          # PostgreSQL + Redis cho dev
│   └── api.Dockerfile
│
├── docs/
├── .github/workflows/              # CI/CD
├── global.json                     # Pin .NET SDK version
├── Directory.Build.props           # Cấu hình chung mọi project
└── PosSystem.sln
```

---

## 5. Cài đặt môi trường phát triển

### 5.1 Clone & restore

```bash
git clone <repo-url>
cd PosSystem
dotnet restore
```

### 5.2 Cài Avalonia templates (nếu tạo project mới)

```bash
dotnet new install Avalonia.Templates
```

### 5.3 Khởi động hạ tầng local (PostgreSQL + Redis)

```bash
cd deploy
docker compose up -d
```

`docker-compose.yml` tối thiểu:

```yaml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: posdb
      POSTGRES_USER: posuser
      POSTGRES_PASSWORD: pospass
    ports: ["5432:5432"]
    volumes: ["pgdata:/var/lib/postgresql/data"]
  redis:
    image: redis:7
    ports: ["6379:6379"]
volumes:
  pgdata:
```

### 5.4 Pin phiên bản SDK (`global.json`)

```json
{
  "sdk": {
    "version": "8.0.0",
    "rollForward": "latestFeature"
  }
}
```

> Pin SDK đảm bảo mọi máy dev (Win/Mac/Linux) build bằng đúng phiên bản, tránh "máy tôi chạy được".

---

## 6. Cấu hình (Configuration)

### 6.1 API — `appsettings.json` / `appsettings.Development.json`

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=posdb;Username=posuser;Password=pospass",
    "Redis": "localhost:6379"
  },
  "Jwt": {
    "Issuer": "PosSystem",
    "Audience": "PosClient",
    "SecretKey": "<đặt qua biến môi trường, KHÔNG commit>",
    "AccessTokenMinutes": 30,
    "RefreshTokenDays": 14
  },
  "Serilog": { "MinimumLevel": "Information" }
}
```

### 6.2 Client — đường dẫn dữ liệu local theo nền tảng

**Không hardcode.** Dùng helper xác định thư mục dữ liệu chuẩn của từng OS:

```csharp
public static string GetLocalDataPath()
{
    var baseDir = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData);
    // Windows -> C:\Users\<user>\AppData\Local
    // macOS   -> /Users/<user>/.local/share (hoặc ~/Library/Application Support tùy cấu hình)
    // Linux   -> /home/<user>/.local/share
    var path = Path.Combine(baseDir, "PosSystem");
    Directory.CreateDirectory(path);
    return path;
}
// SQLite file: Path.Combine(GetLocalDataPath(), "pos_local.db")
```

> **Bí mật (secret):** Không commit `SecretKey`, mật khẩu DB. Dev dùng **User Secrets** (`dotnet user-secrets`), production dùng biến môi trường / Key Vault.

---

## 7. Database & Migration

### 7.1 Tạo migration (PostgreSQL — server)

```bash
dotnet ef migrations add InitialCreate \
  --project src/Pos.Infrastructure \
  --startup-project src/Pos.Api

dotnet ef database update \
  --project src/Pos.Infrastructure \
  --startup-project src/Pos.Api
```

### 7.2 SQLite local (client)

SQLite schema nên là **tập con** của schema server + thêm cột phục vụ sync:

- `SyncStatus` (Pending / Synced / Conflict)
- `LastModifiedUtc`
- `RowVersion` / `Version` (cho optimistic concurrency)
- `DeviceId` (máy nào tạo record)

> **Quan trọng:** Khóa chính dùng **UUID/GUID sinh ở client**, KHÔNG dùng auto-increment INT. Nếu dùng INT tự tăng, hai máy offline sẽ sinh trùng ID → vỡ khi sync.

---

## 8. Chạy ứng dụng (Development)

### 8.1 Chạy API

```bash
dotnet run --project src/Pos.Api
# Swagger: https://localhost:5001/swagger
```

### 8.2 Chạy POS Client (Avalonia)

```bash
dotnet run --project src/Pos.Client.UI
```

Avalonia tự chọn backend render phù hợp mỗi OS. Để debug render trên máy yếu/headless:

```bash
# Ép dùng software rendering nếu GPU lỗi
AVALONIA_RENDERING_MODE=Software dotnet run --project src/Pos.Client.UI
```

---

## 9. Offline-First & Đồng bộ dữ liệu

Đây là phần **rủi ro kỹ thuật cao nhất**, làm sai sẽ mất/sai tồn kho. Quy ước bắt buộc:

### 9.1 Mô hình dữ liệu cho sync

- **Đơn hàng (Order) = event append-only.** Mỗi giao dịch bán hàng là một bản ghi độc lập, **không update chung một dòng tồn kho**. Tồn kho được tính bằng cách cộng dồn các transaction. Điều này loại bỏ phần lớn xung đột.
- Mỗi record có: `Id (GUID)`, `DeviceId`, `CreatedUtc`, `Version`, `SyncStatus`.

### 9.2 Luồng sync

```
[Offline] Bán hàng → ghi SQLite (SyncStatus = Pending)
[Online]  Sync Service đẩy các record Pending lên API
          → API trả về kết quả từng record (Accepted / Conflict)
          → Client cập nhật SyncStatus = Synced / Conflict
          → Kéo về (pull) thay đổi từ server kể từ LastSyncToken
```

### 9.3 Chống lỗi do mạng chập chờn

- **Idempotency-Key** bắt buộc trên mọi API tạo dữ liệu (order, payment). Server lưu key đã xử lý → retry không tạo trùng.
- **Số hóa đơn:** mỗi máy/chi nhánh có **prefix riêng** (VD `HCM01-000123`). Số hiển thị chính thức có thể cấp khi sync; nội bộ luôn dùng GUID.

### 9.4 Xử lý xung đột (conflict)

- Mặc định **Last-Write-Wins theo `Version`** cho dữ liệu master (sản phẩm, giá).
- Với tồn kho: KHÔNG dùng LWW; dùng **cộng dồn transaction** như mục 9.1.
- Conflict không tự giải được → đánh dấu `Conflict`, hiển thị cho Manager xử lý tay.

---

## 10. Tích hợp phần cứng POS theo nền tảng

> Đây là phần **khác biệt lớn nhất** giữa các OS. Tất cả phải đi qua HAL (`Pos.Hardware.Abstractions`).

### 10.1 Interface chung

```csharp
public interface IReceiptPrinter
{
    Task PrintAsync(ReceiptDocument doc, CancellationToken ct = default);
    Task<bool> IsAvailableAsync();
}

public interface ICashDrawer
{
    Task OpenAsync(CancellationToken ct = default); // thường qua lệnh kick của máy in
}

public interface IBarcodeScanner
{
    event EventHandler<string> BarcodeScanned;
    void Start();
    void Stop();
}
```

### 10.2 Máy in hóa đơn (ESC/POS)

| Nền tảng | Cách triển khai khuyến nghị |
|---|---|
| **Windows** | In qua driver hệ thống hoặc gửi raw bytes ESC/POS qua USB/Serial. Thư viện: ESCPOS_NET. |
| **macOS** | **Khó hơn.** Driver ESC/POS hạn chế. **Khuyến nghị in qua LAN/IP**: mở TCP socket tới IP máy in (cổng 9100) và gửi raw ESC/POS bytes. Tránh phụ thuộc driver hệ thống. |
| **Linux** | Qua CUPS hoặc raw socket (9100) tương tự macOS. |

> **Khuyến nghị thiết kế:** Ưu tiên **máy in mạng (Ethernet/WiFi)** cho mọi nền tảng. Cách gửi raw bytes qua TCP 9100 hoạt động **giống hệt** trên Win/Mac/Linux → giảm code riêng theo OS xuống tối thiểu.

```csharp
// In ESC/POS qua mạng — chạy giống nhau mọi OS
using var client = new TcpClient();
await client.ConnectAsync(printerIp, 9100);
await client.GetStream().WriteAsync(escPosBytes);
```

### 10.3 Két tiền (Cash Drawer)

- Mở bằng lệnh **kick** gửi qua máy in (ESC/POS: `0x1B 0x70 0x00 ...`).
- Hoạt động ổn định trên cả 3 OS nếu in qua mạng.

### 10.4 Máy quét mã vạch (Barcode Scanner)

- Loại **HID keyboard wedge** (phổ biến nhất): hoạt động như bàn phím → **đa nền tảng tự nhiên**, chỉ cần bắt input. Khuyến nghị dùng loại này.
- Loại serial/USB-COM: cần đọc cổng serial, code riêng theo OS.

### 10.5 Máy thanh toán thẻ (Verifone / Pax / Ingenico)

- **Cảnh báo:** SDK kết nối trực tiếp thường **chỉ có cho Windows/Android**, **thiếu hoặc không hỗ trợ macOS**.
- Trên macOS, ưu tiên tích hợp **qua cổng thanh toán cloud** (cloud terminal API) thay vì kết nối trực tiếp thiết bị.
- Kiểm tra SDK của từng hãng cho từng OS **trước khi cam kết** với khách.

### 10.6 Đăng ký DI theo runtime

```csharp
if (OperatingSystem.IsWindows())
    services.AddSingleton<IReceiptPrinter, WindowsPrinter>();
else if (OperatingSystem.IsMacOS())
    services.AddSingleton<IReceiptPrinter, NetworkPrinter>(); // in qua IP
else
    services.AddSingleton<IReceiptPrinter, CupsPrinter>();
```

---

## 11. Build & Đóng gói theo nền tảng

### 11.1 Runtime Identifiers (RID)

| Nền tảng | RID |
|---|---|
| Windows 64-bit | `win-x64` |
| macOS Intel | `osx-x64` |
| macOS Apple Silicon | `osx-arm64` |
| Linux 64-bit | `linux-x64` |

### 11.2 Publish self-contained (không cần cài .NET trên máy khách)

```bash
# Windows
dotnet publish src/Pos.Client.UI -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -o dist/win-x64

# macOS Apple Silicon
dotnet publish src/Pos.Client.UI -c Release -r osx-arm64 \
  --self-contained true -o dist/osx-arm64

# macOS Intel
dotnet publish src/Pos.Client.UI -c Release -r osx-x64 \
  --self-contained true -o dist/osx-x64

# Linux
dotnet publish src/Pos.Client.UI -c Release -r linux-x64 \
  --self-contained true -o dist/linux-x64
```

### 11.3 Đóng gói installer

| Nền tảng | Định dạng | Công cụ |
|---|---|---|
| Windows | `.msi` / `.exe` | WiX Toolset, hoặc Inno Setup, hoặc MSIX |
| macOS | `.app` → `.dmg` / `.pkg` | `dotnet` publish ra `.app` bundle, đóng gói `.dmg` bằng `create-dmg` |
| Linux | `.deb` / `.rpm` / AppImage | `dotnet-deb`, hoặc AppImage |

### 11.4 Tạo `.app` bundle cho macOS

macOS yêu cầu cấu trúc bundle chuẩn:

```
PosSystem.app/
├── Contents/
│   ├── Info.plist          # Metadata: bundle id, version, icon
│   ├── MacOS/              # File thực thi
│   ├── Resources/          # Icon .icns, assets
│   └── _CodeSignature/     # Sau khi ký
```

`Info.plist` tối thiểu cần: `CFBundleIdentifier`, `CFBundleVersion`, `CFBundleExecutable`, `CFBundleIconFile`, và `NSHighResolutionCapable = true` (cho màn Retina).

> **Apple Silicon vs Intel:** Build riêng `osx-arm64` và `osx-x64`, hoặc tạo **Universal Binary** gộp 2 kiến trúc bằng `lipo`. Universal binary chạy native trên cả M-series và Intel Mac.

---

## 12. Code signing & Phân phối

### 12.1 macOS (bắt buộc, nếu không sẽ bị Gatekeeper chặn)

Quy trình 3 bước:

```bash
# 1. Ký app với Developer ID
codesign --deep --force --options runtime \
  --sign "Developer ID Application: Your Company (TEAMID)" \
  PosSystem.app

# 2. Đóng gói .dmg rồi gửi notarize lên Apple
xcrun notarytool submit PosSystem.dmg \
  --apple-id "you@email.com" \
  --team-id TEAMID \
  --password "<app-specific-password>" \
  --wait

# 3. Staple kết quả notarization vào app
xcrun stapler staple PosSystem.dmg
```

> Thiếu **notarization**, người dùng macOS sẽ thấy cảnh báo "không thể mở vì Apple không thể kiểm tra mã độc" và phải vào System Settings mở thủ công — trải nghiệm rất tệ cho khách hàng POS.

### 12.2 Windows

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /a PosSystemSetup.exe
```

> Chứng chỉ **EV** giúp qua SmartScreen ngay; chứng chỉ **OV** cần tích lũy uy tín (reputation) một thời gian.

### 12.3 Linux

Không bắt buộc ký. Cung cấp **SHA256 checksum** để người dùng kiểm tra tính toàn vẹn.

---

## 13. Auto-update

| Nền tảng | Giải pháp |
|---|---|
| Cross-platform .NET | **Velopack** (kế thừa Squirrel, hỗ trợ Win/macOS/Linux) — khuyến nghị cho stack này |
| Tự xây | API kiểm tra version, tải bản mới, verify chữ ký, thay thế binary |

Nguyên tắc: app **kiểm tra phiên bản lúc khởi động**, tải nền, thông báo và áp dụng khi khởi động lại. **Luôn verify chữ ký/checksum** trước khi cài để chống cập nhật giả mạo.

---

## 14. Testing

```bash
# Chạy toàn bộ test
dotnet test

# Một project
dotnet test tests/Pos.Sync.Tests
```

**Bắt buộc test kỹ:**

- **Sync engine:** mô phỏng 2+ máy bán offline cùng lúc → sync → kiểm tra tồn kho đúng, không trùng đơn, conflict được phát hiện.
- **Idempotency:** gửi cùng một request order 2 lần → chỉ tạo 1 đơn.
- **Tính tiền/khuyến mãi:** test các edge case làm tròn, chiết khấu, VAT.

**Pyramid:** Unit (domain, tính toán) → Integration (EF + DB thật qua Testcontainers) → E2E (luồng bán hàng).

---

## 15. CI/CD

GitHub Actions với **matrix đa nền tảng**:

```yaml
name: build
on: [push, pull_request]
jobs:
  build:
    strategy:
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
          - os: macos-latest
            rid: osx-arm64
          - os: ubuntu-latest
            rid: linux-x64
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore
      - run: dotnet test --no-restore
      - run: dotnet publish src/Pos.Client.UI -c Release -r ${{ matrix.rid }} --self-contained true
```

> **macOS notarization trong CI:** lưu certificate và app-specific password vào **GitHub Secrets**, chạy bước ký/notarize chỉ trên job `macos-latest`.

---

## 16. Quy ước code & Git

### Code style

- Tuân thủ `.editorconfig` chung trong repo.
- Format trước khi commit: `dotnet format`.
- MVVM bắt buộc cho client: View (XAML) **không chứa logic nghiệp vụ**, mọi logic ở ViewModel.
- Không gọi phần cứng/OS trực tiếp từ ViewModel — luôn qua interface HAL.

### Git branch

```
main        → bản release ổn định
develop     → tích hợp
feature/*   → tính năng
hotfix/*    → sửa khẩn cấp
```

Commit theo **Conventional Commits**: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`.

---

## 17. Bảo mật

- **JWT** access token ngắn hạn + refresh token. Lưu token an toàn theo OS:
  - Windows: DPAPI
  - macOS: Keychain
  - Linux: Secret Service / libsecret
  - (Avalonia không có API thống nhất → trừu tượng hóa qua interface `ISecureStorage` mỗi OS một impl.)
- **RBAC**: phân quyền Admin / Manager / Cashier / Warehouse / Accountant.
- **Thao tác nhạy cảm** (hủy đơn, sửa giá, mở két, hoàn tiền): yêu cầu **PIN/quyền Manager** + ghi **AuditLog**.
- **Mã hóa**: TLS cho mọi giao tiếp API; cân nhắc mã hóa file SQLite local (SQLCipher) nếu dữ liệu nhạy cảm.
- **Không commit secret**: dùng User Secrets / biến môi trường / Key Vault.

---

## 18. Troubleshooting đa nền tảng

| Vấn đề | Nền tảng | Cách xử lý |
|---|---|---|
| App macOS không mở, báo "unidentified developer" | macOS | Chưa notarize. Notarize hoặc tạm thời: System Settings → Privacy & Security → Open Anyway |
| Máy in ESC/POS không in trên macOS | macOS | Chuyển sang in qua IP (TCP 9100), bỏ phụ thuộc driver |
| Trùng ID đơn hàng sau khi sync | Mọi OS | Đảm bảo PK là GUID sinh ở client, không dùng INT auto-increment |
| Đơn hàng nhân đôi khi mạng chậm | Mọi OS | Thiếu Idempotency-Key trên API tạo order |
| Giao diện render lỗi / màn hình đen | Linux/VM | Đặt `AVALONIA_RENDERING_MODE=Software` |
| Barcode scanner không nhận | Mọi OS | Dùng scanner HID keyboard-wedge; kiểm tra focus đang ở ô nhập |
| Font tiếng Việt vỡ trên macOS/Linux | macOS/Linux | Nhúng font hỗ trợ Unicode vào app, không phụ thuộc font hệ thống |
| SmartScreen chặn cài đặt | Windows | Ký bằng chứng chỉ EV, hoặc tích lũy reputation với chứng chỉ OV |
| SQLite "database is locked" | Mọi OS | Bật WAL mode; tránh nhiều tiến trình ghi đồng thời |

---

## Checklist trước khi release đa nền tảng

- [ ] Build & test pass trên Windows, macOS (arm64 + x64), Linux
- [ ] App macOS đã **signed + notarized + stapled**
- [ ] App Windows đã ký chứng chỉ
- [ ] Test sync offline với ≥ 2 thiết bị, tồn kho khớp
- [ ] Test in hóa đơn + mở két trên từng nền tảng (in qua IP)
- [ ] Test font tiếng Việt hiển thị/in đúng trên cả 3 OS
- [ ] Auto-update verify chữ ký trước khi cài
- [ ] Không có secret nào trong source/commit
- [ ] Checksum SHA256 cho mọi gói cài đặt

---

*Tài liệu này là hướng dẫn kỹ thuật cho dự án POS desktop đa nền tảng (.NET 8 + Avalonia). Cập nhật khi thay đổi stack hoặc quy trình build.*