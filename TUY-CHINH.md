# Bản tuỳ chỉnh PhoneBox

Fork của [ersinkoc/phonebox](https://github.com/ersinkoc/phonebox) (0.9.20). Chi tiết kỹ thuật cho AI và người phát triển
nằm trong [AGENTS.md](AGENTS.md); nhật ký các phiên làm việc nằm trong [AI-HANDOFF.md](AI-HANDOFF.md).

## Cài đặt

Tải bộ cài và mã kiểm tra SHA-256 tại [GitHub Releases của bản tuỳ chỉnh](https://github.com/trancaodai1651/doto_phonebox/releases/latest).

| Thiết bị | Cách cài |
|---|---|
| Windows x64 hoặc ARM64 | Mở `PhoneBox-…-windows-universal.exe`. Bộ cài chọn bản phù hợp với máy và tạo lối tắt PhoneBox trong Start Menu. |
| macOS 14 trở lên, Intel hoặc Apple Silicon | Mở `PhoneBox-…-macos-universal.pkg`, rồi mở PhoneBox trong Applications. Trên máy Mac đã chạy agent từ bộ cài trước đó, hãy thoát agent cũ trước khi mở PhoneBox. |
| Android 5.0 trở lên | Mở `PhoneBox-….apk` trên điện thoại và cài. Android chỉ cài đè nếu bản đang dùng có cùng khóa ký. Hãy lưu các cài đặt cần giữ trước khi gỡ bản dùng khóa khác. |

Windows và macOS đã kèm .NET. Bộ cài Windows chưa ký Authenticode và bộ cài macOS chưa được Apple công chứng, nên hệ điều hành có thể hiện cảnh báo. Để mở cài đặt agent sau khi chạy, vào `http://127.0.0.1:8765/` trên máy tính. Nếu dùng Android qua USB, máy tính cần có ADB và điện thoại cần bật USB debugging.

## Dùng trên iPhone / iPad
1. Mở `http://127.0.0.1:8765/web-setup` trên máy tính. Trang này hiện địa chỉ cần gõ và mã PIN.
2. Trên iPhone/iPad, mở Safari và gõ `192.0.2.45:8766` (Ethernet) hoặc `100.100.10.20:8766` (Tailscale).
3. Lần đầu, Safari báo chứng chỉ tự ký. Chọn **Hiển thị chi tiết → truy cập trang web này**.
4. Nhập PIN 6 số. Thiết bị sẽ được ghi nhớ khoảng 400 ngày.
5. Chọn **Chia sẻ → Thêm vào MH chính** để dùng toàn màn hình. Lần đầu mở từ màn hình chính có thể phải nhập PIN thêm một lần.

## Mạng được phép (`lan.access` trong `%APPDATA%\PhoneBox\config.json`)
| Giá trị | Ai kết nối được |
|---|---|
| `local` (mặc định) | Tailscale, cùng subnet với card mạng của máy tính (Ethernet / Wi‑Fi), dải IP riêng (`10.x`, `172.16–31.x`, `192.168.x`) và các mạng trong `lan.allowedCidrs` |
| `tailscale` | Chỉ thiết bị trong tailnet |
| `any` | Mọi mạng (vẫn cần PIN, token hoặc link riêng) |

Nếu iPhone dùng Wi‑Fi ở một subnet khác máy tính, thêm subnet đó vào config, ví dụ
`"allowedCidrs": ["10.20.0.0/16"]`. Sau khi sửa, khởi động lại agent.

## Bảo mật
- Thiết bị nằm ngoài các mạng được phép bị ngắt kết nối trước cả bước TLS.
- PIN có khoá chống dò: sai 5 lần thì khoá IP đó 5 phút; sai tổng 30 lần trong 10 phút thì khoá tất cả.
- Đổi PIN: xoá `lan.webPin`. Đăng xuất mọi trình duyệt: xoá `lan.webToken`. Sau đó khởi động lại agent.
- Trang cài đặt chỉ mở được trên chính máy tính.
- Tự động cập nhật đã tắt, để bản gốc không ghi đè bản tuỳ chỉnh này.

## Build, triển khai, kiểm thử
Xem mục "Lệnh thường dùng" trong [AGENTS.md](AGENTS.md). Kiểm thử thật trên agent đang chạy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-web-access.ps1
```
