# AI NEWS STUDIO - BẢNG BIẾN MÔI TRƯỜNG (ENVIRONMENT VARIABLES REFERENCE)

Tài liệu chi tiết toàn bộ các biến môi trường được sử dụng trong hệ thống AI News Studio khi triển khai trên **Railway**, **Docker Compose** và **Local Development**.

---

## 1. Cấu hình Môi trường Hosting & Mạng (Core & Network)

| Tên biến | Mặc định | Môi trường Railway | Bắt buộc | Mô tả |
|---|---|---|:---:|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Production` | Có | Chế độ môi trường ASP.NET Core (`Production` bật bảo mật, tắt Swagger dev banner) |
| `PORT` | `8080` | `${{PORT}}` (Railway tự cấp) | Có | Cổng mạng HTTP lắng nghe của dịch vụ `news-api` |
| `FRONTEND_URL` | `http://localhost:5173` | `https://newsstudio-web.railway.app` | Có | URL domain của ứng dụng React frontend phục vụ cấu hình CORS an toàn |
| `RAILWAY_PUBLIC_DOMAIN` | *(Trống)* | `${{RAILWAY_PUBLIC_DOMAIN}}` | Không | Domain công khai của API để tự động cấu hình CORS |

---

## 2. Cơ sở dữ liệu PostgreSQL (`newsstudio_db`)

| Tên biến | Mặc định | Môi trường Railway | Bắt buộc | Mô tả |
|---|---|---|:---:|---|
| `DATABASE_URL` | *(Local connection)* | `${{Postgres.DATABASE_URL}}` | Có | Chuỗi kết nối URI chuẩn Railway: `postgresql://user:pass@host:port/database`. Hệ thống tự động chuyển đổi sang định dạng Npgsql |
| `ConnectionStrings__DefaultConnection` | `Host=localhost;...` | `${{DATABASE_URL}}` | Không | Định dạng connection string chuẩn của .NET |
| `Database__AutoMigrate` | `true` | `true` | Không | Tự động chạy Entity Framework Core migrations khi API khởi động |

---

## 3. Trí tuệ Nhân tạo Biên tập Kịch bản (AI Editor)

| Tên biến | Mặc định | Môi trường Railway | Bắt buộc | Mô tả |
|---|---|---|:---:|---|
| `AI_PROVIDER` / `Ai__Provider` | `Gemini` | `Gemini` hoặc `Claude` | Có | Nhà cung cấp AI chính (`Gemini` hoặc `Claude`) |
| `AI_API_KEY` / `Ai__ApiKey` | *(Trống)* | `AIzaSy...` hoặc `sk-ant-...` | Có | API Key bí mật cho Gemini API hoặc Anthropic Claude API |
| `Ai__GeminiModel` | `gemini-1.5-flash` | `gemini-1.5-flash` | Không | Model Gemini tối ưu tốc độ và chi phí |
| `Ai__ClaudeModel` | `claude-3-5-haiku` | `claude-3-5-haiku` | Không | Model Claude tối ưu tốc độ và chi phí |
| `Ai__Temperature` | `0.7` | `0.7` | Không | Độ sáng tạo kịch bản biên tập |
| `Ai__TimeoutSeconds` | `60` | `60` | Không | Thời gian timeout tối đa cho mỗi cuộc gọi AI sinh kịch bản |

---

## 4. Giọng đọc Tiếng Việt (Vietnamese TTS Engine)

| Tên biến | Mặc định | Môi trường Railway | Bắt buộc | Mô tả |
|---|---|---|:---:|---|
| `TTS_PROVIDER` / `Tts__Provider` | `EdgeTts` | `EdgeTts` hoặc `ElevenLabs` | Có | Nhà cung cấp giọng đọc (`EdgeTts` miễn phí, chất lượng cao; `ElevenLabs` cao cấp) |
| `TTS_API_KEY` / `Tts__ApiKey` | *(Trống)* | `xi-...` (nếu dùng ElevenLabs) | Không | API Key cho ElevenLabs |
| `Tts__Voice` | `vi-VN-HoaiMyNeural` | `vi-VN-HoaiMyNeural` | Không | Giọng đọc mặc định (`vi-VN-HoaiMyNeural` nữ, `vi-VN-NamMinhNeural` nam) |
| `Tts__Speed` | `+0%` | `+0%` | Không | Tốc độ đọc (chuẩn thời lượng 45–60 giây) |
| `Tts__Pitch` | `+0Hz` | `+0Hz` | Không | Cao độ giọng đọc |

---

## 5. Xuất bản Facebook Reels (Meta Graph API)

| Tên biến | Mặc định | Môi trường Railway | Bắt buộc | Mô tả |
|---|---|---|:---:|---|
| `FACEBOOK_APP_ID` / `Facebook__AppId` | *(Trống)* | ID ứng dụng Meta Developers | Không | Mã định danh Meta App |
| `FACEBOOK_APP_SECRET` / `Facebook__AppSecret` | *(Trống)* | Khóa bí mật App Secret | Không | Khóa bảo mật Meta App |
| `Facebook__GraphApiVersion` | `v21.0` | `v21.0` | Không | Phiên bản Graph API sử dụng để đăng Reels |
| `Facebook__EncryptionSecretKey` | *(Internal Salt)* | Chuỗi ngẫu nhiên 32+ ký tự | Có | Khóa dẫn xuất SHA-256 mã hóa AES-256 bảo vệ Access Token trong PostgreSQL |

---

## 6. Chính sách Lưu trữ Tạm thời (Ephemeral Storage Policy)

> [!IMPORTANT]
> **Quy tắc bất biến:** Không lưu trữ MP4, ảnh hay audio lâu dài. Mọi tệp tin chỉ tồn tại tạm thời trong container Worker và tự động dọn dẹp sau khi đăng hoặc khi hết hạn TTL.

| Tên biến | Mặc định | Môi trường Railway | Bắt buộc | Mô tả |
|---|---|---|:---:|---|
| `TEMP_STORAGE_PATH` / `Storage__TempStoragePath` | `/tmp/newsstudio` | `/tmp/newsstudio` | Có | Thư mục lưu trữ tạm thời các file render trong Worker |
| `MAX_CONCURRENT_RENDER_JOBS` / `Storage__MaxConcurrentRenderJobs` | `2` | `2` | Có | Số lượng tác vụ render FFmpeg chạy đồng thời tối đa |
| `MAX_TEMP_STORAGE_MB` / `Storage__MaxTempStorageMb` | `5120` (5 GB) | `5120` | Có | Dung lượng trần ổ đĩa tạm. Vượt quá ngưỡng sẽ tự động xóa file cũ nhất |
| `TEMP_FILE_TTL_MINUTES` / `Storage__TempFileTtlMinutes` | `120` (2 giờ) | `120` | Có | Thời gian sống tối đa của tệp tin tạm xem trước |
| `Storage__CleanupIntervalMinutes` | `15` | `15` | Không | Chu kỳ chạy ngầm dọn dẹp ổ đĩa tạm của Worker (phút) |

---

## 7. Bảo mật & Quản trị (Admin Security)

| Tên biến | Mặc định | Môi trường Railway | Bắt buộc | Mô tả |
|---|---|---|:---:|---|
| `Security__AdminApiKey` | *(Trống)* | Khóa bí mật API Key (tùy chọn) | Không | Nếu được thiết lập, các endpoint quản trị yêu cầu header `X-Admin-Api-Key` |
| `Serilog__MinimumLevel__Default` | `Information` | `Information` | Không | Mức độ log tối thiểu cho Serilog |
