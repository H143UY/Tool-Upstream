# GitUpstreamLocal - One-way A -> B

Ứng dụng WinForms cục bộ: chọn repo A (nguồn) và B (đích), nhấn một nút để đồng bộ **một chiều A → B**.
Nếu B chưa có remote, app tự tạo remote `B` (và `git init` nếu B là thư mục rỗng).

## Yêu cầu
- Windows, Git for Windows (lệnh `git` chạy được trong CMD/PowerShell)
- .NET 8 SDK hoặc Visual Studio 2022 (workload ".NET desktop development")
- Repo A đã clone, có remote `origin` (ưu tiên) hoặc `upstream`, đã checkout branch cần đồng bộ
- Nếu dùng URL remote B mới: repo trống tương ứng đã tồn tại trên GitLab/GitHub
  (GitLab còn cho phép "push to create" project mới nếu bạn có quyền trong namespace đó)

## Lịch sử commit A và B
- Giữa cửa sổ có hai bảng cạnh nhau: **lịch sử A** (trái) và **lịch sử B** (phải), mỗi bảng hiện tối đa 300 commit mới nhất của branch đang checkout (mã, ngày, tác giả, nội dung).
- Màu **cam** = commit của A chưa có ở B (sẽ được đưa sang khi đồng bộ). Màu **xanh** = commit chỉ có ở B.
- Dòng tiêu đề mỗi bảng cho biết branch, tổng số commit và số commit chênh lệch.
- **Nhấp đôi** vào commit để xem chi tiết (`git show --stat`).
- Tự tải khi đổi đường dẫn, sau mỗi lần đồng bộ, hoặc bấm **Tải lịch sử**.
- Lịch sử A là của repo local (HEAD). Khi đồng bộ, app fetch từ URL remote của A, nên commit local chưa push lên remote của A sẽ không được đưa sang B.

## Các trường hợp B được xử lý
| Tình huống của B | App làm gì |
|---|---|
| Thư mục chưa tồn tại | Hỏi xác nhận → tạo thư mục → `git init -b <branch của A>` |
| Thư mục rỗng, chưa phải repo | Hỏi xác nhận → `git init -b <branch của A>` |
| Thư mục có file nhưng chưa phải repo | Dừng (không tự init để tránh ảnh hưởng file) |
| Là repo nhưng chưa có remote `B` | Dùng ô "URL remote B" → `git remote add B <url>` |
| Đã có remote `B`, ô URL khác URL hiện tại | Hỏi có cập nhật remote `B` không |
| Đã có remote `B`, ô URL để trống | Dùng remote `B` hiện có |

## Luồng xử lý
1. Đọc branch và URL remote của A (A chỉ đọc, không ghi).
2. Chuẩn bị B (init / tạo remote `B` nếu cần).
3. Chặn nếu URL remote B trùng URL A.
4. Kiểm tra B sạch (không có thay đổi chưa commit).
5. `git fetch` branch A vào B → `git merge FETCH_HEAD` (lỗi/conflict thì tự `merge --abort`).
6. `git push -u B <branch>:<branch>`.

Không có lệnh push tới A, không force push, không tự commit/stash/xóa file.
Token/mật khẩu nhúng trong URL được ẩn (`***@`) trong nhật ký.

## Chạy
1. Giải nén ZIP, mở `GitUpstreamLocal.csproj` bằng Visual Studio 2022, F5.
2. Hoặc publish bản độc lập:
```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```
File chạy: `bin\Release\net8.0-windows\win-x64\publish\GitUpstreamLocal.exe`

## Nếu báo lỗi
- `Repo B chưa có remote 'B'`: nhập URL vào ô "URL remote B".
- `Authentication failed`: đăng nhập GitLab một lần bằng Git Credential Manager (`git ls-remote <url>` trong CMD) hoặc dùng SSH URL.
- `working tree ... not clean`: commit hoặc stash thay đổi trong B.
- `merge conflict`: xử lý trong B rồi chạy lại.
- `refusing to merge unrelated histories`: B và A không chung lịch sử. App không tự bật `--allow-unrelated-histories`.
