---
description: Build, đóng gói & checklist phát hành đa nền tảng
---

Chạy quy trình đóng gói theo `Technical.md` mục 11–12 cho RID yêu cầu (mặc định `osx-x64` — máy Intel hiện tại):

```bash
dotnet publish src/Pos.Client.UI -c Release -r <rid> --self-contained true -o dist/<rid>
```

Sau đó kiểm **Checklist trước khi release** (cuối Technical.md):
- [ ] Build & test pass trên các nền tảng đích
- [ ] macOS: signed + notarized + stapled
- [ ] Windows: ký chứng chỉ
- [ ] Sync offline ≥ 2 thiết bị, tồn kho khớp
- [ ] In hóa đơn + mở két (in qua IP) OK
- [ ] **HĐĐT EasyInvoice phát hành & lấy mã CQT OK** (test trên môi trường demo trước)
- [ ] Không có secret trong source
- [ ] Checksum SHA256 cho mọi gói

⚠️ Đóng gói/ký/notarize là thao tác phát hành ra ngoài — xác nhận với người dùng trước khi chạy.
