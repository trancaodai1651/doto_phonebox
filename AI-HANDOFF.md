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

## 2026-11-18 17:17 · Codex · Phát hành PhoneBox 0.9.21
- Đã làm: đẩy mã nguồn lên `origin/main`, giữ repo gốc ở remote `upstream` và bỏ thông tin mạng riêng khỏi lịch sử chưa công khai. [Release v0.9.21](https://github.com/trancaodai1651/doto_phonebox/releases/tag/v0.9.21) gồm bộ cài Windows x64/ARM64, macOS Intel/Apple Silicon và APK ký bằng khóa của fork.
- Đã kiểm thử: `dotnet test` đạt 1112/1112, Playwright đạt 179/179. [GitHub Actions](https://github.com/trancaodai1651/doto_phonebox/actions/runs/44282671613) cài và chạy agent trên cả bốn kiến trúc, kiểm tra chữ ký APK. Đã đối chiếu SHA-256 và xác nhận ba link tải công khai trả HTTP 200.
- Đã triển khai bản dựng từ commit phát hành lên Windows cục bộ. Agent trả phiên bản 0.9.21 và `tools/test-web-access.ps1` đạt 24/24 bước trên hai địa chỉ.
- Còn dở / lỗi biết: trình chạy lệnh cục bộ chặn lệnh thử bộ cài Windows tải về. Bộ cài Windows chưa ký Authenticode, macOS chưa công chứng Apple. Chưa có điện thoại Android kết nối để cài thử. GitHub Pages của fork chưa bật.
- Bước tiếp theo: dùng lại khóa ký Android trong GitHub Actions Secrets khi phát hành APK mới. Bản sao cục bộ nằm trong thư mục `%APPDATA%\PhoneBox\release-signing`.
