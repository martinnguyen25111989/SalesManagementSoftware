---
description: Khởi tạo solution & cấu trúc project POS từ đầu
---

Khởi tạo bộ khung dự án theo `Technical.md` mục 4 (nếu chưa có):

1. Tạo `PosSystem.sln` và `global.json` pin SDK 8.0 (`rollForward: latestFeature`).
2. Tạo `Directory.Build.props` (cấu hình chung: `LangVersion`, `Nullable=enable`, `TreatWarningsAsErrors`).
3. Tạo các project trong `src/`: `Pos.Domain`, `Pos.Application`, `Pos.Infrastructure`, `Pos.Client.Core`, `Pos.Client.UI` (Avalonia), `Pos.Hardware.Abstractions`, `Pos.Api`, `Pos.Sync`.
4. Tạo `tests/`: `Pos.Domain.Tests`, `Pos.Application.Tests`, `Pos.Sync.Tests`.
5. Tạo `deploy/docker-compose.yml` (PostgreSQL 16 + Redis 7) theo Technical.md mục 5.3.
6. Sau mỗi bước, ghi 1 entry vào `Remember.md`.

Tuân thủ các quy ước BẮT BUỘC trong `CLAUDE.md`. Hỏi trước khi chạy lệnh tạo file hàng loạt.
