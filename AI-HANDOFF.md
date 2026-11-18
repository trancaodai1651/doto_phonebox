# Nhật ký bàn giao giữa các AI

Mục mới nhất ở **cuối** file. Mỗi phiên làm việc (Claude hoặc Codex) thêm một mục theo mẫu trong `AGENTS.md`.

## 2026-11-18 16:05 · Claude · Clone, quét mã độc, chỉ cho phép Tailscale, bản web iPhone/iPad
- **Đã làm:**
  - Clone ersinkoc/phonebox 0.9.20.
  - Quét mã độc: không thấy. Windows Defender sạch, đã đọc mã, và `gradle-wrapper.jar` khớp checksum trên gradle.org.
  - Thêm web panel (link `/web/<token>` và cookie), `TailscaleCert`, tắt tự động cập nhật.
- **Đã kiểm thử:** cài .NET SDK 10.0.404 qua winget; build và test đều đạt; đã triển khai vào `%LOCALAPPDATA%\PhoneBox\app`.

## 2026-11-18 16:45 · Claude · Mở thêm mạng Ethernet, gõ IP:port + PIN, bộ nhớ chung
- **Đã làm:**
  - Thay `lan.tailscaleOnly` bằng `lan.access` (mặc định `local`) và thêm `lan.allowedCidrs`.
  - Thêm chuyển `http` sang `https` trên cổng 8766, form PIN ở `/`, `POST /web/login` và `PinGate`.
  - `Lan.Addresses` nay có cả IP Ethernet nằm ngoài dải IP riêng.
  - Tạo `AGENTS.md`, `CLAUDE.md`, file này và `tools/test-web-access.ps1`.
- **Đã kiểm thử:**
  - `dotnet test`: 1112/1112 đạt.
  - Agent đã được triển khai lại. `tools/test-web-access.ps1` đạt 12/12 bước cho mỗi IP (Ethernet và Tailscale).
  - Khoá chống dò PIN: lần sai thứ 6 trả 429, IP khác vẫn đăng nhập được.
- **Còn dở / lỗi biết:**
  - Chưa thử trên iPhone/iPad thật.
  - Safari sẽ cảnh báo chứng chỉ tự ký khi mở bằng IP (chấp nhận một lần).
  - Tailnet chưa bật HTTPS Certificates.
  - Agent chưa tự khởi động cùng Windows.
  - Chưa commit.
- **Bước tiếp theo:** người dùng thử trên iPhone/iPad. Có thể làm thêm: autostart; một CA riêng kèm profile iOS để không còn cảnh báo chứng chỉ khi mở bằng IP.

## 2026-11-18 16:59 · Codex · Chuẩn bị phát hành PhoneBox 0.9.21
- Đã làm: chuẩn bị bộ cài Windows x64/ARM64 và macOS Intel/Apple Silicon, thiết lập ký APK bằng khóa của fork, đổi nguồn tải APK và cập nhật sang repo của fork. Đã bỏ thông tin mạng riêng khỏi lịch sử chưa công khai.
- Đã kiểm thử: dotnet test trước khi đóng gói đạt 1112/1112. Workflow release kiểm tra HĐH, kiến trúc, APK và các bộ cài trước khi tạo release.
- Còn dở / lỗi biết: đang kiểm chứng bộ cài trên GitHub Actions. Windows chưa ký Authenticode, macOS chưa công chứng.
- Bước tiếp theo: xác minh các tệp trên GitHub rồi thử bản Windows trên agent đang chạy.
