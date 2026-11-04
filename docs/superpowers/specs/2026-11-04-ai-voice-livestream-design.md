# AI Voice trả lời comment livestream Facebook — Thiết kế MVP

- Ngày: 2026-11-04
- Trạng thái: chờ duyệt

## 1. Mục tiêu

Phiên live trên Fanpage luôn có "người dẫn chuyện": một playlist mp3 ngắn phát liên tục, và khi người xem comment thì hệ thống tự sinh câu trả lời bằng GPT, đọc bằng giọng ElevenLabs, chèn vào giữa hai clip nền rồi quay lại mạch nội dung.

**Tiêu chí thành công MVP**

- Comment hợp lệ trên live → câu trả lời được phát trong vòng vài giây sau khi clip nền đang phát kết thúc.
- Không bao giờ chồng tiếng, không bao giờ ngắt giữa câu.
- Playlist nền chạy đúng thứ tự, lặp vòng, không đứt kể cả khi GPT / ElevenLabs / Facebook lỗi.

## 2. Phạm vi

**Trong MVP**

- Một Fanpage, một phiên live tại một thời điểm, chạy local trên máy đang phát live bằng OBS.
- Lấy comment qua Facebook Graph API (Page Access Token).
- Lọc comment rác, hàng đợi có giới hạn, bỏ comment/câu trả lời quá cũ.
- Phát qua trang `player.html` gắn vào OBS làm Browser Source.

**Ngoài MVP (không làm)**

- Pause/resume giữa clip, ducking nhạc nền.
- Đăng câu trả lời dạng text lên Facebook.
- Giao diện quản trị, duyệt câu trả lời trước khi phát, đa người dùng, đa phiên live.
- Tự dọn file mp3 trả lời cũ.
- Overlay chữ lên video.

## 3. Kiến trúc

Một project ASP.NET Core (.NET 10), không dùng SDK bên thứ ba: OpenAI, ElevenLabs, Graph API đều gọi bằng `HttpClient`.

```
Facebook Live ──(poll /comments mỗi 2s)──▶ CommentPoller ──CommentFilter──▶ Channel (tối đa N, đầy thì bỏ cũ nhất)
                                                                                 │
                                                                           ReplyWorker (tuần tự)
                                                       GPT → "SKIP"? bỏ : ElevenLabs → audio/replies/{id}.mp3
                                                                                 │
                                                                      PlaybackQueue.AddReply
OBS Browser Source (player.html) ── clip phát xong ──▶ GET /api/next ──▶ PlaybackQueue.Next()
```

Player không giữ logic nào ngoài "phát xong thì hỏi tiếp". Mọi quyết định phát gì nằm ở server, trong `PlaybackQueue`.

## 4. Thành phần

| Thành phần | Trách nhiệm | Phụ thuộc |
|---|---|---|
| `PlaybackQueue` | Logic thuần, thread-safe (lock). Giữ danh sách clip nền, con trỏ playlist, hàng câu trả lời sẵn sàng (FIFO), số reply đã phát liền. Hàm `Next(now)` trả về clip kế tiếp hoặc `null`. | Không (nhận danh sách file và thời gian từ ngoài để test được) |
| `CommentFilter` | Hàm thuần quyết định một comment có đáng đưa vào hàng đợi không (luật mục 6). | Không |
| `CommentPoller` | `BackgroundService`. Tìm video đang LIVE, poll comment mới, khử trùng theo id, qua `CommentFilter`, ghi vào `Channel`. | Graph API, `CommentFilter` |
| `ReplyWorker` | `BackgroundService`. Đọc `Channel`, bỏ comment quá cũ, gọi GPT, gọi ElevenLabs, lưu mp3, `AddReply`. Giữ 10 lượt hỏi–đáp gần nhất làm ngữ cảnh hội thoại. | OpenAI, ElevenLabs, `PlaybackQueue` |
| `Program.cs` | Đăng ký DI, kiểm tra cấu hình lúc khởi động, map endpoint, phục vụ static file. | — |
| `wwwroot/player.html` | Một thẻ `<audio>`; `ended`/`error` → `GET /api/next` → phát URL trả về. | — |

## 5. Luật phát — `PlaybackQueue.Next(now)`

Trạng thái: `repliesInRow` (số reply vừa phát liền nhau).

1. Bỏ khỏi đầu hàng mọi reply có tuổi (tính từ lúc comment được tạo) > `ReplyMaxAgeSeconds`.
2. Nếu `repliesInRow < MaxRepliesInRow` và hàng reply còn phần tử → lấy reply đầu hàng, `repliesInRow++`, trả về reply.
3. Ngược lại, nếu playlist có clip → trả clip tại con trỏ, tăng con trỏ (về 0 khi hết danh sách), `repliesInRow = 0`.
4. Playlist trống: nếu còn reply thì trả reply (bỏ qua giới hạn liền nhau vì không có nội dung nền để chen); không còn gì → `null`.

Hệ quả: tối đa `MaxRepliesInRow` (mặc định 2) câu trả lời liền nhau rồi luôn quay về một clip nền.

Playlist = các file `*.mp3` trong `audio/playlist/`, sắp theo tên file (người vận hành đặt tên `01-...mp3`, `02-...mp3`). Đọc một lần khi khởi động.

## 6. Lọc comment và hàng đợi

`CommentFilter` loại comment khi:

- comment của chính Page (`from.id == PageId`);
- có ít hơn 3 ký tự chữ hoặc số (`char.IsLetterOrDigit`; loại "hi", "❤️", "...", "👍👍");
- trùng nội dung (không phân biệt hoa thường, bỏ khoảng trắng thừa) với comment ngay trước đó của cùng người.

Lần poll đầu tiên của mỗi video: đánh dấu tất cả comment hiện có là đã thấy, **không trả lời** (tránh trả lời dồn comment cũ khi khởi động lại app giữa phiên).

Hàng đợi comment: `Channel.CreateBounded(MaxPendingComments)` với `BoundedChannelFullMode.DropOldest`. `ReplyWorker` bỏ comment có tuổi > `CommentMaxAgeSeconds` khi lấy ra.

GPT là lớp lọc thứ hai: comment không cần trả lời (chào hỏi chung chung, spam, không liên quan) → GPT trả đúng chuỗi `SKIP` → bỏ, không gọi ElevenLabs.

## 7. Sinh câu trả lời

**GPT** — `POST https://api.openai.com/v1/chat/completions`, model lấy từ config (mặc định `gpt-4.1-mini`), dùng `max_completion_tokens`, timeout 10 giây.

Message gồm:

1. System prompt cố định trong code: là người dẫn live bán hàng; văn nói tiếng Việt tự nhiên; 1–2 câu, dưới ~40 từ; gọi tên người hỏi nếu có; chỉ dùng thông tin trong phần "Thông tin sản phẩm", không bịa giá/chính sách, không biết thì mời để lại inbox; trả đúng `SKIP` nếu comment không cần trả lời; không dùng emoji, markdown, ký hiệu khó đọc thành tiếng.
2. Nội dung `product.md` (người vận hành tự soạn: sản phẩm, giá, khuyến mãi, ship, giọng điệu). Đọc một lần khi khởi động.
3. Tối đa 10 lượt hỏi–đáp gần nhất (cặp user/assistant) để giữ mạch hội thoại.
4. Comment hiện tại dạng `"{tên}: {nội dung}"`.

**ElevenLabs** — `POST https://api.elevenlabs.io/v1/text-to-speech/{VoiceId}?output_format=mp3_44100_128`, header `xi-api-key`, body `{ "text", "model_id" }` (mặc định `eleven_flash_v2_5`, có tiếng Việt, độ trễ thấp), timeout 10 giây. Lưu bytes trả về vào `audio/replies/{commentId}.mp3`.

## 8. Facebook Graph API

- Token: Page Access Token của Fanpage, cần quyền `pages_read_engagement`, `pages_read_user_content`.
- Tìm video đang live (khi `LiveVideoId` trống): `GET /{PageId}/live_videos?broadcast_status=["LIVE"]&fields=id`. Chưa thấy → thử lại sau 10 giây.
- Poll comment mỗi `PollIntervalSeconds` (mặc định 2): `GET /{liveVideoId}/comments?filter=stream&order=reverse_chronological&fields=id,message,created_time,from{id,name}&limit=50`. Bỏ comment có id đã thấy (lưu `HashSet` id trong bộ nhớ).
- Video được xác định một lần mỗi lần chạy app. Sang phiên live mới thì khởi động lại app (nhờ luật "lần poll đầu không trả lời" ở mục 6, khởi động lại giữa phiên cũng an toàn).
- Phiên bản Graph API đặt trong config (`GraphApiVersion`).

## 9. API nội bộ

| Endpoint | Mô tả |
|---|---|
| `GET /api/next` | `200 { "url": "/audio/playlist/01-intro.mp3" }` hoặc `204` khi không có gì để phát. |
| `POST /api/comments` | Body `{ "name": "...", "message": "..." }`. Đưa comment qua `CommentFilter` vào cùng `Channel` như comment Facebook. Dùng để test khi chưa live. Chỉ lắng nghe `localhost`. |
| `GET /audio/...` | Static file từ thư mục `audio/`. |
| `GET /player.html` | Trang player. |

## 10. Player

- Khi trang tải: gọi `/api/next` và phát ngay (OBS Browser Source cho autoplay). Nếu `play()` bị trình duyệt chặn (Chrome thường), hiện nút "Bắt đầu".
- `ended` → gọi `/api/next`. `204`, lỗi mạng, hoặc `error` của audio → đợi 1 giây rồi gọi lại.
- Trong OBS: thêm Browser Source trỏ tới `http://localhost:5000/player.html`, tích "Control audio via OBS".

## 11. Cấu hình

`appsettings.json` (commit được, không chứa bí mật):

```json
{
  "Urls": "http://localhost:5000",
  "Live": {
    "PageId": "",
    "LiveVideoId": "",
    "GraphApiVersion": "v24.0",
    "PollIntervalSeconds": 2,
    "MaxPendingComments": 5,
    "CommentMaxAgeSeconds": 60,
    "ReplyMaxAgeSeconds": 90,
    "MaxRepliesInRow": 2,
    "OpenAIModel": "gpt-4.1-mini",
    "ElevenLabsVoiceId": "",
    "ElevenLabsModelId": "eleven_flash_v2_5"
  }
}
```

Bí mật đặt bằng `dotnet user-secrets` hoặc biến môi trường, không bao giờ vào git:

- `Live:OpenAIApiKey`
- `Live:ElevenLabsApiKey`
- `Live:PageAccessToken`

Thiếu key nào hoặc thiếu `PageId` / `ElevenLabsVoiceId` → app dừng ngay khi khởi động, log rõ tên key.

## 12. Xử lý lỗi

Nguyên tắc: **không lỗi nào được làm im tiếng live.**

| Lỗi | Xử lý |
|---|---|
| GPT / ElevenLabs lỗi hoặc quá 10 giây | Log, bỏ comment, xử lý comment tiếp. Không retry. |
| Graph API lỗi mạng / 5xx | Log, thử lại ở lần poll sau. |
| Token hết hạn (Graph error code `190`) | Log `Page Access Token hết hạn`, tiếp tục thử lại (playlist vẫn phát). |
| Không thấy video LIVE | Log, dò lại mỗi 10 giây. |
| Player lỗi mạng / file hỏng | Bỏ file, 1 giây sau hỏi `/api/next`. |
| Exception bất kỳ trong vòng lặp nền | Bắt, log, vòng lặp chạy tiếp (không để `BackgroundService` chết). |

Log ra console: mỗi comment nhận (đã lọc hay bị loại, lý do), mỗi câu trả lời sinh ra, mỗi file được phát.

## 13. Test

- **Unit test (xUnit)** cho `PlaybackQueue`: playlist lặp vòng; tối đa 2 reply liền rồi về clip nền; reply quá cũ bị bỏ; playlist trống + có reply; trống hoàn toàn → `null`.
- **Unit test** cho `CommentFilter`: comment của Page, toàn emoji, quá ngắn, trùng của cùng người, comment hợp lệ.
- **Không mock** GPT / ElevenLabs / Graph API. Kiểm tra tay:
  1. Chạy app, mở `http://localhost:5000/player.html` trong Chrome, nghe playlist.
  2. `POST /api/comments` → nghe câu trả lời chen vào sau clip đang phát.
  3. Gắn vào OBS, live thử trên Fanpage, comment thật.

## 14. Cấu trúc thư mục

```
AiAutoLive.slnx
src/AiAutoLive/
  Program.cs
  PlaybackQueue.cs
  CommentFilter.cs
  CommentPoller.cs
  ReplyWorker.cs
  LiveOptions.cs
  appsettings.json
  product.md
  wwwroot/player.html
  audio/playlist/      (người vận hành bỏ mp3 vào; không commit)
  audio/replies/       (app tự sinh; không commit)
tests/AiAutoLive.Tests/
  PlaybackQueueTests.cs
  CommentFilterTests.cs
```
