---
name: refactor
description: Refactor code C#/.NET trong dự án POS mà không đổi hành vi — tách lớp Domain/Application/Infrastructure, giữ HAL & MVVM sạch, gom logic tính tiền/thuế. Dùng khi dọn nợ kỹ thuật.
---

# Skill: Refactor (POS .NET)

## Khi nào dùng
Khi code chạy đúng nhưng cần dọn cấu trúc, giảm trùng lặp, tăng dễ test.

## Nguyên tắc
- **Không đổi hành vi**: có test phủ trước khi refactor; chạy `dotnet test` sau mỗi bước.
- **Tôn trọng ranh giới**: Domain thuần (không phụ thuộc OS/EF); logic nghiệp vụ không nằm ở UI/Controller.
- **HAL**: phần cứng/OS chỉ ở project Hardware.* sau interface; ViewModel không gọi trực tiếp.
- **MVVM**: tách logic khỏi View (XAML).
- **Tính tiền/thuế**: gom về một nơi (vd domain service) theo thứ tự B5/B13, tránh rải rác.

## Quy trình
1. Xác định "code smell" + phạm vi an toàn.
2. Đảm bảo có test; nếu thiếu → thêm test đặc tả hành vi hiện tại trước.
3. Refactor từng bước nhỏ, build + test xanh sau mỗi bước.
4. Giữ `dotnet format` sạch.
