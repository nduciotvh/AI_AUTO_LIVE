# AI Auto Live

Phát playlist mp3 nền cho livestream Fanpage. Khi người xem comment, app nhờ GPT viết câu trả lời, ElevenLabs đọc thành giọng nói, rồi phát câu trả lời đó vào giữa hai clip nền.

Thiết kế: [docs/superpowers/specs/2026-11-04-ai-voice-livestream-design.md](docs/superpowers/specs/2026-11-04-ai-voice-livestream-design.md)

## Chuẩn bị

1. Bỏ các file mp3 nền vào `src/AiAutoLive/audio/playlist/`. Đặt tên theo thứ tự muốn phát: `01-chao.mp3`, `02-gioi-thieu.mp3`, … Playlist phát lặp vòng.
2. Sửa `src/AiAutoLive/product.md`: thông tin sản phẩm, giá, giao hàng, giọng điệu. GPT chỉ trả lời dựa trên file này. **Xoá hết các dòng "(Ví dụ …)"**: app sẽ không chạy khi file còn dòng mẫu, để tránh đọc giá giả lên live.
3. Trong `src/AiAutoLive/appsettings.json`, điền `Live:PageId` và `Live:ElevenLabsVoiceId`.

## Bí mật

Chạy trong thư mục `src/AiAutoLive`:

```bash
dotnet user-secrets set "Live:OpenAIApiKey" "sk-..."
dotnet user-secrets set "Live:ElevenLabsApiKey" "..."
dotnet user-secrets set "Live:PageAccessToken" "..."
```

Page Access Token phải có quyền `pages_read_engagement` và `pages_read_user_content`.

User-secrets chỉ được nạp khi chạy bằng `dotnet run` (môi trường Development). Nếu chạy bản publish, đặt biến môi trường `Live__OpenAIApiKey`, `Live__ElevenLabsApiKey`, `Live__PageAccessToken`.

## Chạy

```bash
cd src/AiAutoLive
dotnet run
```

App tự tìm video đang LIVE của Page (hoặc dùng `Live:LiveVideoId` nếu điền sẵn). **Sang phiên live mới thì khởi động lại app.** Khởi động lại giữa phiên vẫn an toàn: app không trả lời các comment đã có từ trước.

## Gắn vào OBS

Thêm **Browser Source** với URL `http://localhost:5000/player.html`, tích **Control audio via OBS**. Âm lượng chỉnh trong Audio Mixer của OBS.

Có thể mở cùng URL đó bằng Chrome để nghe thử. Chrome chặn tự phát tiếng, nên bấm nút "Bắt đầu" nếu thấy nút này hiện ra.

**Chỉ để một player chạy tại một thời điểm.** Mỗi player lấy mục kế tiếp ra khỏi hàng đợi, nên nếu tab Chrome nghe thử vẫn mở khi đang live, Chrome và OBS sẽ chia nhau clip và câu trả lời, người xem live chỉ nghe được khoảng một nửa. Đóng tab Chrome trước khi live, và trong OBS chỉ dùng một Browser Source này.

## Test không cần live

Gửi comment giả (PowerShell, gửi body dạng UTF-8 để giữ dấu tiếng Việt):

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/comments -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes('{"name":"Lan","message":"Áo còn size M không?"}'))
```

Câu trả lời sẽ phát ngay sau khi clip nền đang phát kết thúc. File mp3 của các câu trả lời được giữ lại trong `src/AiAutoLive/audio/replies/`.

## Đổi model

Sửa `Live:OpenAIModel` hoặc `Live:ElevenLabsModelId` trong `appsettings.json`. Có hiệu lực từ comment kế tiếp, không cần khởi động lại.

- Với model suy luận của OpenAI (ví dụ dòng gpt-5), đặt `Live:OpenAIReasoningEffort` là `minimal` hoặc `low` để mỗi câu trả lời không vượt giới hạn 10 giây.
- Với `gpt-4.1-mini`, để trống trường này.
- Model ElevenLabs phải hỗ trợ tiếng Việt.

## Tinh chỉnh

Các giá trị dưới đây nằm trong `appsettings.json`, mục `Live`:

| Key | Mặc định | Ý nghĩa |
|---|---|---|
| `MaxRepliesInRow` | 2 | Số câu trả lời được phát liền nhau trước khi quay về clip nền |
| `MaxPendingComments` | 5 | Số comment tối đa đang chờ; đầy thì bỏ comment cũ nhất |
| `CommentMaxAgeSeconds` | 60 | Comment chờ quá lâu thì bỏ, không trả lời |
| `ReplyMaxAgeSeconds` | 90 | Câu trả lời quá lâu chưa được phát thì bỏ |
| `PollIntervalSeconds` | 2 | Bao lâu đọc comment từ Facebook một lần |

Đổi các giá trị này thì phải khởi động lại app.

## Test

```bash
dotnet test
```
