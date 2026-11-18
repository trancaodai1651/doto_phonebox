# Bộ nhớ dự án dùng chung cho mọi AI (Codex, Claude Code, …)

> Codex tự đọc file này. Claude Code đọc qua `CLAUDE.md` (file đó import `@AGENTS.md`).
> **Bắt đầu phiên:** đọc file này, rồi đọc phần cuối của [AI-HANDOFF.md](AI-HANDOFF.md).
> **Kết thúc phiên:** thêm một mục vào `AI-HANDOFF.md` (mẫu ở cuối file này). Kiến thức lâu dài thì sửa thẳng vào đây.

## Người dùng và cách làm việc
- Trả lời bằng **tiếng Việt**.
- Máy chạy Windows 11. Shell mặc định là PowerShell 5.1: không dùng `&&`, nối lệnh bằng `;` hoặc `if ($?) {}`.
- Không có Python. Có Node 26, curl.exe và Tailscale CLI.
- Người dùng muốn được kiểm thử kỹ: build, chạy toàn bộ test, triển khai rồi thử thật trên agent đang chạy trước khi báo xong.
- Chỉ commit hoặc push khi người dùng yêu cầu.

## Dự án
Đây là fork của [ersinkoc/phonebox](https://github.com/ersinkoc/phonebox) (gốc 0.9.20): biến điện thoại hay máy tính bảng thành bảng điều khiển cho PC.
- `agent/PhoneBox.Agent`: agent .NET 10 chạy trên PC (ASP.NET Kestrel).
- `agent/PhoneBox.Agent.Tests`: bộ test xUnit.
- `android/`: ứng dụng Android, là WebView của `android/app/src/main/assets/www`. Thư mục `www` này cũng được nhúng vào agent và phục vụ tại `/panel/`.

## Tuỳ chỉnh của fork (đừng làm mất)
1. **Cổng mạng `lan.port` (8766) và quyền truy cập theo `lan.access`.** Kiểm tra theo từng kết nối TCP, trước bước TLS, trong `NetAccess.cs`.
   - `tailscale`: chỉ Tailscale.
   - `local` (mặc định): Tailscale, cùng subnet với card mạng của PC, các dải IP riêng, và `lan.allowedCidrs`.
   - `any`: mọi mạng.

   Cổng này cũng tự chuyển `http://` sang `https://` (`NetAccess.RedirectPlainHttp`), nên chỉ cần gõ `IP:8766`.
2. **Bản web cho iPhone/iPad.**
   - Gõ `https://IP:8766/`, nhập PIN 6 số (`lan.webPin`). Agent đặt cookie `pb_web` (Secure, HttpOnly, SameSite=Strict, giá trị là `lan.webToken`).
   - Cách khác: link riêng `/web/<webToken>`.
   - Cookie chỉ mở `/panel/*` và các API nằm trong `Lan.AllowedOverLan`, và chỉ khi request đến từ chính trang panel (`WebPanel.FromPanel`).
   - `PinGate` chặn dò PIN: sai 5 lần thì khoá IP đó 5 phút; sai tổng 30 lần trong 10 phút thì khoá tất cả.
   - Trang thiết lập (địa chỉ, PIN, QR) ở `http://127.0.0.1:8765/web-setup`. Trang này chỉ mở được trên máy tính.
3. **Chứng chỉ Tailscale.** Khi client dùng tên MagicDNS (SNI), agent trả chứng chỉ từ `tailscale cert` (`TailscaleCert`). Khi kết nối bằng IP, agent dùng chứng chỉ tự ký mà ứng dụng Android đã ghim.
   - Lỗi hiện tại: tailnet chưa bật HTTPS Certificates.
4. **Tự động cập nhật tắt mặc định** (`updates.auto = false`). Từ 0.9.21, agent dùng GitHub Releases của `trancaodai1651/doto_phonebox` làm nguồn cập nhật mặc định.

File chính: `NetAccess.cs`, `TailscaleAccess.cs`, `Program.cs` (Kestrel và middleware cổng Wi‑Fi, tìm chữ `Fork`), `Config.cs` (`LanConfig`, `Normalize`), `Lan.cs`.

## Quy tắc bảo mật bắt buộc
- `/api/settings`, `/api/pair`, `/api/shortcuts/test`, `/web-setup` và `/api/web` **chỉ** được mở ở cổng 127.0.0.1:8765. Không thêm chúng vào `Lan.AllowedOverLan`.
- Không nới lỏng `WebPanel.FromPanel`, kiểm tra Origin, hay `PinGate`.
- Không đưa PIN hay token vào log, URL công khai, hoặc câu trả lời chat.
- Cổng 8765 không có xác thực: mọi tiến trình trên PC và mọi ứng dụng trên điện thoại cắm USB (qua adb reverse) đều gọi được. Đây là rủi ro sẵn có từ bản gốc.

## Lệnh thường dùng (PowerShell)
```powershell
$env:Path = "C:\Program Files\dotnet;" + $env:Path; $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
```
```powershell
dotnet test agent/PhoneBox.Agent.Tests --nologo
```
```powershell
dotnet publish agent/PhoneBox.Agent -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish --nologo -v q
```
Triển khai (thay agent đang chạy):
```powershell
Stop-Process -Name PhoneBoxAgent -Force -ErrorAction SilentlyContinue; Start-Sleep 2; Copy-Item publish\* "$env:LOCALAPPDATA\PhoneBox\app" -Force; Start-Process "$env:LOCALAPPDATA\PhoneBox\app\PhoneBoxAgent.exe" -WindowStyle Minimized
```
Kiểm thử thật trên agent đang chạy (12 bước cho mỗi IP):
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-web-access.ps1
```

## Môi trường của người dùng
- Agent cài ở `%LOCALAPPDATA%\PhoneBox\app`. Config ở `%APPDATA%\PhoneBox\config.json`, bản sao lưu trước khi fork là `config.before-fork.json`. Log ở `%LOCALAPPDATA%\PhoneBox\agent.log`.
- Agent chưa tự khởi động cùng Windows.
- Địa chỉ Ethernet và Tailscale có thể xem trên trang /web-setup.
- Có một điện thoại Android từng kết nối qua Wi‑Fi bằng token Bearer.

## Mẫu ghi vào AI-HANDOFF.md
```
## YYYY-MM-DD HH:MM · <Claude|Codex> · <tóm tắt 1 dòng>
- Đã làm: …
- Đã kiểm thử: … (lệnh + kết quả)
- Còn dở / lỗi biết: …
- Bước tiếp theo: …
```
