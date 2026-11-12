# AGENTS.md — DSH Remote (dsh-web-control)

Hướng dẫn chuẩn cho agent làm việc trong repo này. Đọc hết trước khi sửa file.
Ngôn ngữ: trao đổi với user bằng tiếng Việt; code/UI text/comment trong code bằng
tiếng Anh; tài liệu (README, AGENTS) bằng tiếng Việt.

---

## 0. QUY TẮC BẮT BUỘC — ĐỌC TRƯỚC

### 0.1. Dừng debug sau 5 lần lặp lại cùng một lỗi

Một "lần" = một chu kỳ **sửa → build/chạy → quan sát** cho **cùng một lỗi hoặc
cùng một triệu chứng**.

- Đếm đến **lần thứ 5** mà lỗi vẫn còn, hoặc 5 lần thử liên tiếp không có tiến
  triển thực chất → **DỪNG NGAY**.
- Khi dừng: **không sửa thêm, không build thêm, không đổi ExecutionPolicy, không
  xoá/khôi phục file, không thử ngẫu nhiên**. Không được "thử thêm một lần nữa".
- Báo cho user, tối thiểu gồm:
  1. Triệu chứng/lỗi chính xác (copy nguyên văn output).
  2. Danh sách lệnh đã chạy và file đã sửa (kèm thời điểm).
  3. 5 giả thuyết đã thử và kết quả từng cái.
  4. Giả thuyết còn lại + bằng chứng thu thập được.
  5. 2–3 hướng đề xuất, nêu rõ hướng nào cần user quyết định/quyền mà agent không có.
- Chỉ tiếp tục sau khi user trả lời. Nếu user bảo tiếp tục, ghi rõ đang vượt rule
  này theo yêu cầu của user và tiếp tục đếm lại từ 0.
- Lỗi **mới và khác** (không cùng triệu chứng) không tính vào bộ đếm, nhưng vẫn
  phải báo ngắn gọn.

Mục đích: tránh vòng lặp debug vô tận, tránh phá môi trường bằng các thử nghiệm
liên tiếp. Thà dừng và hỏi còn hơn đoán mò.

### 0.2. Không đổi cấu hình hệ thống khi chưa được phép

- Không `Set-ExecutionPolicy` ở scope máy/user, không sửa registry, không chạy
  elevated/UAC, không cài phần mềm mới.
- Chỉ dùng `-ExecutionPolicy Bypass` **cho từng tiến trình** (xem §3.1).
- Không xoá `.bak-*`, không xoá `DSH Remote.new.exe` hay file runtime của user khi
  chưa hỏi.

### 0.3. Token/URL = quyền điều khiển máy

Không in, không log, không dán URL có `?token=` vào chat/commit/tài liệu. Khi cần
trích log, che token (`?token=***`).

---

## 1. Dự án là gì

**DSH Remote** là app WinForms nhỏ (1 file `.exe`) để bật/tắt DSH web host và gửi
link ghép qua Tailscale/Taildrop. Toàn bộ UI tự vẽ (custom-drawn), không dùng
designer, không có asset ngoài.

- Runtime: **.NET Framework 4.8**, biên dịch bằng `csc.exe` có sẵn của Windows.
- Ngôn ngữ: **C# 5** (KHÔNG phải C# 6+).
- Không Visual Studio, không `dotnet SDK`, không NuGet, không file `.csproj`.
- App chạy local trên PC; điện thoại chỉ là màn hình điều khiển qua Tailscale.

## 2. Bản đồ file

| Đường dẫn | Vai trò |
|---|---|
| `winforms/build.ps1` | Build bằng `csc`, xuất `..\DSH Remote.exe` |
| `winforms/Program.cs` | Entry point, single-instance mutex, assembly metadata |
| `winforms/MainForm.cs` | Toàn bộ layout + event handler + trạng thái UI |
| `winforms/UiKit.cs` | `UiTheme` (màu, scale, font) + control tự vẽ |
| `winforms/HostController.cs` | Gọi start/stop script, `netstat`, timeout |
| `winforms/TailscaleService.cs` | Đọc `tailscale status --json`, chọn thiết bị |
| `winforms/DshLocator.cs` | Dò `DeepSeek Harness.exe` / `dsh.cmd` |
| `winforms/SettingsStore.cs` | INI `gui-settings.ini` (atomic write + backup) |
| `winforms/Models.cs` | POCO settings/kết quả, không logic nặng |
| `winforms/Logger.cs` | Ghi `gui.log`, xoay file > 1 MB |
| `winforms/SecretMask.cs` | Che `?token=…` (và param bí mật khác) trước khi hiện lên UI/ghi log |
| `winforms/app.manifest` | DPI awareness / manifest |
| `dsh-web-start.ps1` | Bật host, sinh URL + QR, gửi Taildrop |
| `dsh-web-stop.ps1` | Tắt host, xoay token, xoá QR |
| `start-dsh-web.bat`, `stop-dsh-web.bat` | Vỏ gọi PowerShell cho user |
| `connection-1day.patch.yml` | Overlay cookie web 1 ngày |
| `docs/ui-preview.png` | Ảnh thiết kế chuẩn để đối chiếu UI (đã che dữ liệu cá nhân) |
| `README.md` | Hướng dẫn sử dụng cho user (tiếng Anh, mặc định) |
| `README.vi.md` | Hướng dẫn sử dụng tiếng Việt |
| `.gitignore` | Loại build output, backup, log, token, settings cá nhân (có `!/winforms/*.cs` để không loại nhầm source) |
| `.gitattributes` | Giữ CRLF cho `.bat`/`.cmd`, đánh dấu file nhị phân |
| `LICENSE` | MIT |
| `gui-settings.ini.example` | Mẫu `gui-settings.ini`, không chứa dữ liệu thật |

Runtime (KHÔNG commit, không sửa tay): mặc định `%USERPROFILE%\.dsh\remote\`
- `gui-settings.ini`, `gui.log`
- `dsh-web.log`, `dsh-web.err.log`, `dsh-web.url.txt`, `dsh-web-qr-*.png`
- Thứ tự chọn thư mục (giống nhau ở `AppPaths.cs`, `dsh-web-start.ps1`,
  `dsh-web-stop.ps1` — sửa một chỗ phải sửa cả ba): `-RuntimeDir` >
  `%DSH_REMOTE_DIR%` > `data\` cạnh exe/script (portable) > mặc định. GUI luôn
  truyền `-RuntimeDir` cho script. `/data/` đã nằm trong `.gitignore`.

File của DSH (không phải của tool, không chuyển chỗ được):
`<DSH home>\.credentials.yaml`, với DSH home = `%DSH_HOME%` hoặc `~\.dsh`. `stop`
sửa file này để xoay token; backup chỉ là tạm, xoá ngay khi thành công.

Repo đã có `.gitignore` nhưng chưa init git khi bàn giao. Trước khi sửa lớn, tạo bản
sao theo convention có sẵn: `<tên-file>.bak-<yyyyMMdd-HHmmss>` (các file này bị
`.gitignore` bỏ qua, không public).

---

## 3. Build

### 3.1. Lệnh chuẩn (dùng cái này)

```powershell
cd <repo>\winforms
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Lý do: Windows PowerShell 5.1 trên máy đang để ExecutionPolicy chặn script, nên
gõ `.\build.ps1` sẽ lỗi `running scripts is disabled on this system`. `Bypass`
chỉ có tác dụng cho tiến trình đó — **không** đổi policy của máy.

Nếu user muốn chạy trực tiếp `.\build.ps1` mỗi ngày, đưa họ lệnh (chỉ khi họ đồng ý):

```powershell
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
```

Không tự chạy lệnh này.

### 3.2. Yêu cầu & output

- `csc.exe`: `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (có fallback x86).
- Output mặc định: `..\DSH Remote.exe`; có thể `-OutFile <path>`.
- Build thành công: exit code `0` + dòng `Built: <path> (<n> bytes)`.
- Nếu exe đang chạy, Windows khoá file → build ghi ra `DSH Remote.new.exe` và in
  cảnh báo vàng. Cách xử lý: đóng cửa sổ DSH Remote rồi build lại; **không** tự
  kill process của user nếu chưa hỏi.
- Không sửa tay file `.exe`; luôn build lại từ source.

### 3.3. Kiểm tra sau build

1. `$LASTEXITCODE -eq 0`.
2. `Get-Item '..\DSH Remote.exe'` — `LastWriteTime` mới, size > 0.
3. Chạy app, xem UI khớp `docs/ui-preview.png` (xem §4).

---

## 4. Chạy & xác minh UI

```powershell
Start-Process -FilePath '.\DSH Remote.exe'
```

Checklist UI (đối chiếu `docs/ui-preview.png`):

- Header tối, logo `D`, tiêu đề `DSH Remote`, nút minimize/close hoạt động.
- Card trạng thái: `Stopped`/`Running`, chip `TAILSCALE ONLINE/OFFLINE`, URL + PID.
- Card `TAILDROP DEVICE`: ô nhập, nút `Refresh`, hint `Online/Offline · OS`.
- Hai nút `Start DSH` (xanh) / `Stop DSH` (viền đỏ).
- Card `ACTIVITY`: console tối, nút `Clear`, log cuộn được.
- Footer note + `v1.0.0`; cửa sổ bo góc, kéo được bằng header.
- Không có control nào bị cắt/đè chữ; resize/DPI khác nhau vẫn gọn.

Xác minh nhanh bằng screenshot desktop, không chỉ tin vào build exit code.
Sau khi xác minh xong, đóng app nếu không cần để tránh khoá file cho lần build sau
(`Stop-Process -Name 'DSH Remote'` chỉ khi app do agent mở).

---

## 5. Quy ước code C# (C# 5 — rất quan trọng)

`build.ps1` dùng `csc` của .NET Framework 4.0 → chỉ hỗ trợ **C# 5**. Các cú pháp
sau sẽ làm build fail, tuyệt đối không dùng:

- string interpolation `$"..."` → dùng `string.Format` hoặc `+`.
- `?.`, `?[]`, expression-bodied member (`=>` cho method/property).
- `nameof`, `using static`, `out var`, tuple, local function.
- auto-property initializer, getter-only auto-property.
- `async` trong `Main`, `Task.Run` với lambda async không được `await` đúng cách.
- pattern matching (`is X x`), `throw` expression.

Được dùng: `async/await`, `Task`, LINQ, `JavaScriptSerializer`, `dynamic` (hạn chế).

### 5.1. UI & scale

- Layout viết theo **đơn vị 96 dpi**, constants `DesignWidth = 680`,
  `DesignHeight = 700` trong `MainForm`.
- Luôn dùng `B(x,y,w,h)` / `UiTheme.S(...)` / `UiTheme.Px(control, ...)` để quy
  đổi; không hard-code pixel vật lý.
- KHÔNG bật `AutoScaleMode.Font/Dpi`; app tự tính `UiTheme.Factor` (có cap để
  vừa work area; sàn 0.75).
- Font: `UiTheme.MakeFont/MakeMonoFont` (Segoe UI / Consolas). Font tạo trong
  field của control phải `Dispose` (xem `HeaderPanel`).
- Màu: chỉ lấy từ `UiTheme`; không rải mã màu rải rác. Màu mới → thêm vào
  `UiTheme` kèm tên ngữ nghĩa.
- Control tự vẽ: bật `UserPaint | AllPaintingInWmPaint | OptimizedDoubleBuffer |
  ResizeRedraw`; dùng `using` cho `GraphicsPath/Brush/Pen`; luôn `base.OnPaint`.
- Bo góc: `UiTheme.Rounded(bounds, radius)`; nhớ trừ 1px (`Width-1`, `Height-1`)
  khi vẽ viền để không bị cắt.
- Text: `TextRenderer.DrawText` + `TextFormatFlags.NoPrefix` (tránh `&` thành
  accelerator). UI text tiếng Anh, ngắn gọn.

### 5.2. Threading & lỗi

- Không chặn UI thread: việc I/O/process chạy trong `Task.Run`, handler `async`.
- Cập nhật control chỉ trên UI thread sau `await` (WinForms SynchronizationContext).
- Mọi thao tác Start/Stop phải qua `SetBusy(true/false)` và tôn trọng cờ `busy`.
- Bắt exception ở biên (handler, `Load`) và log qua `AppendLog`; không nuốt lỗi
  im lặng ở logic chính.
- `MessageBox` chỉ cho lỗi user cần biết; lỗi kỹ thuật đưa vào Activity/log.

### 5.3. Process & argument

- Luôn `UseShellExecute=false`, `CreateNoWindow=true`, redirect stdout/stderr,
  encoding UTF-8, có timeout và kill khi timeout.
- Quote mọi đường dẫn/giá trị bằng `Quote(...)` (xử lý dấu `"`).
- Gọi script qua: `-NoProfile -ExecutionPolicy Bypass -File <script> ...`.
- Không đổi tên/tham số script tuỳ tiện: `HostController.BuildStartArguments`
  và các `.bat` đang phụ thuộc (`-ListenHost`, `-Port`, `-Profile`, `-DshExe`,
  `-DshCli`, `-Taildrop`, `-RuntimeDir`).

### 5.4. Settings & tương thích

- Key INI là contract: `Device, LastAction, LastUpdated, DshExe, DshCli,
  ListenHost, Port, Profile`. Thêm key mới phải có default an toàn và đọc
  case-insensitive.
- File hỏng không được làm app chết: `IniSettingsStore.Load` bọc try/catch.
- Ghi file phải atomic (tmp + `File.Replace`, fallback copy) và giữ `.bak`.

---

## 6. Quy ước PowerShell / script

- Tương thích **Windows PowerShell 5.1** (không dùng cú pháp chỉ có ở pwsh 7).
- Mọi output (`Write-Host`, `Note`, `echo` trong `.bat`), comment và trang HTML
  ghép đều bằng **tiếng Anh**. File `.ps1` lưu UTF-8 **có BOM** (PS 5.1 đọc file
  không BOM theo ANSI); `.bat` giữ ASCII + CRLF.
- Đầu script: `#requires -Version 5.1`, `$ErrorActionPreference = 'Stop'`,
  `[Console]::OutputEncoding = UTF8` (trong try/catch).
- Tham số giữ backward-compatible; thêm tham số mới phải có default.
- Không yêu cầu admin. Không đổi ExecutionPolicy trong script.
- Dùng `netstat.exe` để tìm PID đang listen (không dùng `Get-NetTCPConnection`,
  vì trong sandbox DSH trả rỗng). Nhận diện socket listen bằng địa chỉ đích
  `0.0.0.0:0` / `[::]:0`, **không** so chữ `LISTENING` (bị dịch theo ngôn ngữ Windows).
- Không truyền mảng có đường dẫn chứa dấu cách thẳng vào `Start-Process
  -ArgumentList` (nó không tự quote): dùng `ConvertTo-ProcessArgument`.
- Ngày giờ truyền cho công cụ ngoài (`schtasks /ST`) phải format bằng
  `InvariantCulture`.
- Mọi thao tác phải **idempotent**: chạy lại không tạo host trùng, không xoá nhầm.
- Không log token/URL đầy đủ. Không gửi token lên dịch vụ online (QR sinh local
  bằng `segno`).
- `stop` mặc định xoay token + xoá QR; `keep` giữ đăng nhập. Không đổi mặc định
  này khi chưa hỏi user.
- Giữ contract của `.bat` (`start-dsh-web.bat`, `stop-dsh-web.bat`) vì user
  double-click và Task Scheduler đang dùng.

---

## 7. Bảo mật

- URL `?token=…` = quyền chạy lệnh trên PC. Không chia sẻ, không chụp màn hình
  có token, không lưu vào OneDrive/cloud.
- `~\.dsh\remote\` (mặc định) cố ý nằm ngoài OneDrive; start cảnh báo nếu thư
  mục runtime nằm trong thư mục đồng bộ cloud.
- Sau `stop`, mọi cookie cũ vô hiệu; đừng "sửa" hành vi này.
- Khi chẩn đoán, che token trong log/ảnh trước khi gửi user.
- Không thêm telemetry, không gọi mạng ngoài Tailscale/DSH.

---

## 8. Skill chuẩn của project (làm theo đúng thứ tự)

### Skill A — `build-and-verify-ui`
1. Kiểm tra app có đang chạy không (`Get-Process 'DSH Remote'`).
2. Nếu có và không cần thiết, hỏi user hoặc đóng đúng cách.
3. Chạy lệnh build ở §3.1; kiểm tra exit code + output.
4. Chạy app, chụp màn hình, đối chiếu checklist §4 và `docs/ui-preview.png`.
5. Báo: đường dẫn exe, size, thời gian, ảnh xác minh.
6. Đóng app vừa mở (nếu chỉ để test).

### Skill B — `change-ui`
1. Đọc `MainForm.cs` phần liên quan + `UiKit.cs` trước khi sửa.
2. Tính lại layout theo design units; kiểm tra không overlap khi `Factor` thay
   đổi (thử 100% và 150% DPI nếu được).
3. Không đổi public API của control nếu không cần.
4. Build + verify như Skill A; so sánh ảnh trước/sau.
5. Cập nhật `docs/ui-preview.png` chỉ khi user đồng ý (đây là ảnh chuẩn).

### Skill C — `change-host-behavior`
1. Đọc `HostController.cs` + tham số trong `dsh-web-start.ps1`/`stop.ps1`.
2. Giữ tương thích tham số; thêm default.
3. Test đường không phá host: ưu tiên `-DryRun`, kiểm tra `GetPortOwner`.
4. Không tự bật host thật nếu user không yêu cầu; nếu bật, phải tắt đúng cách
   và không để lại process.
5. Kiểm tra log `gui.log` + `dsh-web.err.log` sau khi chạy.

### Skill D — `diagnose-start-stop`
1. Thu thập: `netstat -ano | findstr :8443`, `~\.dsh\remote\*.log`,
   `dsh-web.url.txt`, `tailscale status`.
2. Xác định host có đang listen không trước khi đổ lỗi script.
3. Đọc log từ cuối lên; tìm dòng lỗi đầu tiên, không chỉ dòng cuối.
4. Chỉ sửa nguyên nhân gốc; không thêm retry mù.
5. Nếu chạm rule 5 lần → dừng theo §0.1.

### Skill E — `release`
1. Tăng `AppVersion` trong `MainForm.cs` + `AssemblyVersion` trong `Program.cs`.
2. Build, verify UI, kiểm tra footer hiển thị version mới.
3. Cập nhật `README.md` nếu hành vi user-facing thay đổi.
4. Liệt kê file thay đổi; nhắc user backup nếu cần.

---

## 9. Definition of Done

Một thay đổi chỉ được coi là xong khi:

- [ ] Build exit code 0, không warning mới đáng kể.
- [ ] App khởi động được, UI không vỡ; ảnh xác minh đã xem.
- [ ] Không hồi quy Start/Stop/Refresh (test mức tối thiểu, không phá host).
- [ ] Không có token/secret trong output, log, file mới.
- [ ] Không để lại process/temp file do agent tạo.
- [ ] Tài liệu (README/AGENTS) cập nhật nếu hành vi thay đổi.
- [ ] Báo user ngắn gọn: đã làm gì, file nào, cách chạy, điều gì cần họ quyết.

---

## 10. Những điều KHÔNG được làm

- Không chạy `.\build.ps1` trực tiếp khi policy đang chặn rồi sửa lung tung;
  dùng `-ExecutionPolicy Bypass`.
- Không đổi ExecutionPolicy/registry/UAC.
- Không dùng C# 6+ syntax.
- Không thêm NuGet/package/asset nhị phân.
- Không hard-code màu/pixel ngoài `UiTheme` và design units.
- Không log/in token hoặc URL đầy đủ.
- Không xoá `.bak-*`, `*.new.exe`, file runtime của user khi chưa hỏi.
- Không "fix" bằng cách tắt tính năng bảo mật (cookie 1 ngày, xoay token,
  bind Tailscale-only).
- Không tiếp tục debug quá 5 lần cho cùng một lỗi — dừng và báo user (§0.1).
