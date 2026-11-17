# BÁO CÁO KHẢO SÁT HỆ THỐNG VÀ KIẾN TRÚC HIỆN TẠI
## Phục vụ triển khai phân hệ: AI FILM FACTORY
**Tài liệu:** `AI_FILM_FACTORY_SOURCE_AUDIT.md`  
**Ngày lập:** 17/11/2026  
**Nhóm khảo sát:** Senior Software Architect, Database Architect, Full-stack Lead, AI Cost Engineer  

---

## MỤC LỤC
1. [Tổng quan hệ thống hiện tại](#1-tổng-quan-hệ-thống-hiện-tại)
2. [Chi tiết 15 thành phần cốt lõi của hệ thống](#2-chi-tiết-15-thành-phần-cốt-lõi-của-hệ-thống)
3. [Phân loại thành phần: Tái sử dụng - Xây mới - Tuyệt đối không sửa](#3-phân-loại-thành-phần)
4. [Các API hiện tại có thể dùng lại](#4-các-api-hiện-tại-có-thể-dùng-lại)
5. [Các vấn đề kiến trúc và thách thức kỹ thuật](#5-các-vấn-đề-kiến-trúc-và-thách-thức-kỹ-thuật)
6. [Đánh giá rủi ro: Database, Bảo mật và Ngân sách 0–5 USD](#6-đánh-giá-rủi-ro)
7. [Kết luận và Đề xuất lộ trình triển khai](#7-kết-luận-và-đề-xuất-lộ-trình-triển-khai)

---

## 1. TỔNG QUAN HỆ THỐNG HIỆN TẠI

Hệ thống hiện tại là **Trạm Tin 24/7 (AI News Studio)** – một nền tảng sản xuất video tin tức ngắn (Facebook Reels & Instagram Reels) hoàn chỉnh và đang vận hành thực tế. 
- **Workspace:** `D:\Project\AutoGenVideoPostFB`
- **Mô hình tổng thể:** Clean Architecture (.NET 8 C# Backend + React 18 TypeScript Frontend + PostgreSQL Database + Hangfire Distributed Jobs + FFmpeg Media Engine).
- **Trạng thái vận hành:** Rất ổn định. Hệ thống vừa xuất bản thành công video Reels thực tế lên Facebook Page `Trạm Tin 24/7` và đồng bộ số liệu thời gian thực từ Meta Graph API v20.0.

---

## 2. CHI TIẾT 15 THÀNH PHẦN CỐT LÕI CỦA HỆ THỐNG

### 2.1. Công nghệ Frontend
- **Framework & Runtime:** React 18.2.0, TypeScript 5.2.2, Vite 5.1.6 (chạy tại cổng dev `5173`).
- **Styling & Theme:** Tailwind CSS 3.4.1, PostCSS, hỗ trợ Theme chuyển đổi tức thời (`Light`, `Dark`, `System`) lưu qua `localStorage` (`frontend/src/lib/theme.ts`).
- **Hệ thống Routing:** Hash-based SPA Router siêu nhẹ (`frontend/src/lib/router.ts`), quản lý qua `window.location.hash` (`#/...`), điều hướng mượt mà không reload trang qua các hàm `navigate()` và `href()`.
- **Hệ thống UI Component:** Bộ thư viện UI nội bộ chuẩn chỉnh tại `frontend/src/components/ui.tsx` (Card, Button, Badge, Modal, Input, Textarea, Select, Tabs, Table, Tooltip, Dot, Skeleton) kết hợp icon từ `lucide-react` (0.344.0).
- **Hệ thống phản hồi:** Toast notifications và Confirm modal tương tác async (`frontend/src/components/feedback.tsx`).
- **Quản lý thiết lập người dùng:** `frontend/src/lib/prefs.ts` quản lý cấu hình mặc định (AI provider, giọng đọc, tốc độ, hashtags).

### 2.2. Công nghệ Backend
- **Nền tảng:** .NET 8.0 (C# 12) Web API (`NewsStudio.Api`) và Background Worker Service (`NewsStudio.Worker`).
- **Kiến trúc mã nguồn:** Clean Architecture phân tách rõ ràng 5 project:
  1. `NewsStudio.Domain`: Entity, BaseEntity, Enums, Value Objects.
  2. `NewsStudio.Application`: Interfaces, DTOs, Service Contracts.
  3. `NewsStudio.Infrastructure`: EF Core PostgreSQL, Facebook Graph API, AI Services (Gemini, Claude), Audio/TTS, FFmpeg Rendering, Storage, Hangfire Jobs.
  4. `NewsStudio.Api`: REST Controllers, Swagger, Rate Limiter, Health Checks, Middleware.
  5. `NewsStudio.Worker`: Host chạy Hangfire Server xử lý background queues (`critical`, `render`, `default`), tự động dọn dẹp file tạm, phục hồi job đứt đoạn.
- **Logging & Giám sát:** Serilog ghi log đa kênh (Console + Rolling file logs tại `logs/api-.log` và `logs/worker-.log`).
- **Cơ chế xử lý lỗi:** RFC 7807 ProblemDetails middleware trả về thông báo lỗi chuẩn hóa.

### 2.3. Kiến trúc API
- RESTful HTTP API chuẩn mực, port mặc định `http://localhost:5000`.
- Rate Limiting: Fixed Window Limiter tích hợp sẵn (120 requests/phút).
- CORS: Cấu hình linh hoạt cho `localhost:5173`, `localhost:3000` và biến môi trường `FRONTEND_URL`, `RAILWAY_PUBLIC_DOMAIN`.
- Tài liệu hóa: Swagger UI đầy đủ tại `/swagger`.
- Endpoint giám sát sức khỏe: `/health` (HealthChecks tích hợp sẵn kiểm tra kết nối PostgreSQL).

### 2.4. Database đang sử dụng
- **Hệ quản trị CSDL:** PostgreSQL (kết nối mặc định: `Host=localhost;Port=5432;Database=newsstudio_db;Username=postgres;Password=postgres`).
- **ORM:** Entity Framework Core 8.0 với driver `Npgsql.EntityFrameworkCore.PostgreSQL`.
- **Quản lý Schema:** EF Core Code-First Migrations (`NewsStudio.Infrastructure/Persistence/Migrations`), đã có hơn 10 migration chạy tự động khi khởi động.
- **Bảng dữ liệu hiện hữu:**
  - Nghiệp vụ tin tức: `Articles`, `ArticleImages`, `VideoProjects`, `VideoScenes`, `VideoScripts`, `VideoAssets`, `ProcessingJobs`, `VideoReviews`.
  - Phân phối mạng xã hội: `FacebookAccounts`, `FacebookPosts`, `VideoMetrics`, `InstagramMetrics`.
  - Hệ thống: `SystemSettings`, `AuditLogs`, `TrendingArticles`, `AutoPostSettings`, `AutoPostRuns`, `DailyDigestRuns`, `AiUsages`.

### 2.5. Hệ thống Đăng nhập và Phân quyền
- **Hiện trạng:** Hệ thống đang chạy theo mô hình Studio Đơn nhiệm (Single-Tenant Studio / Local Operator).
- **Bảo vệ API:** Có sẵn cơ chế Middleware chặn bằng `X-Admin-Api-Key` (`Security:AdminApiKey` trong cấu hình) dùng khi deploy lên môi trường Production/Cloud.
- **Hangfire Dashboard:** Bảo vệ bằng `LocalRequestsOnlyAuthorizationFilter` tại `/hangfire`.
- Không có bảng `Users` hay phân quyền Role phức tạp; phù hợp trực tiếp cho người điều hành studio.

### 2.6. Cách quản lý các Facebook Page
- Thực thể `FacebookAccount` lưu trữ:
  - `PageId`, `PageName`, `EncryptedAccessToken`, `TokenExpiresAtUtc`, `IsActive`, `Status`.
- **Mã hóa an toàn:** Token được mã hóa AES-256 đối xứng (`FacebookTokenProtectionService`) trước khi ghi vào database; khi trả về UI được mask an toàn (`EABE••••••••rl61`).
- Hỗ trợ kết nối nhiều Page (đã có Page `Trạm Tin 24/7` và các Page khác).
- Tích hợp Meta Graph API v20.0/v21.0: Đăng video Reels thông qua giao thức Resumable Upload Session (`rupload.facebook.com` với HTTP Header `Offset` và `file_size`).
- Tự động lấy số liệu tương tác: Views, Reach, Likes, Comments, Shares, Watch Time.

### 2.7. Cách quản lý Instagram
- Tích hợp trực tiếp trên thực thể `FacebookAccount`:
  - `InstagramAccountId`: ID tài khoản Instagram Professional/Creator được liên kết với Page.
  - `InstagramUsername`: Username Instagram.
- Bảng `InstagramMetric`: Lưu trữ lịch sử tăng trưởng tương tác theo từng ngày tại Việt Nam (Views, Reach, Likes, Comments, Shares, Saved).
- Cơ chế xuất bản: Đăng đồng thời Reel lên Instagram thông qua Instagram Content Publishing API khi xuất bản video lên Facebook.

### 2.8. Hệ thống lưu trữ file
- **Lưu trữ cục bộ:** Thư mục `Storage:TempStoragePath` (mặc định `./tmp/newsstudio`).
- **Quản lý vòng đời tài sản:** 
  - `VideoAssetService` tạo và quản lý đường dẫn file theo từng dự án (`VideoAssets`).
  - Hạn mức lưu trữ: `MaxTempStorageMb: 5120` (5 GB).
  - Thời gian tồn tại: `TempFileTtlMinutes: 120` (2 giờ).
  - `StorageCleanupService` chạy định kỳ trong Worker để dọn dẹp các file rác và file tạm đã quá hạn, bảo vệ ổ cứng không bị đầy.
- **Streaming:** `VideoMediaController` hỗ trợ HTTP Range Requests (Status 206 Partial Content) để xem video tức thì trên trình duyệt không cần tải toàn bộ file.

### 2.9. Các AI Provider hiện có
- **AI Text / Scriptwriting:**
  - Google Gemini: `GeminiAiEditorService.cs` (hỗ trợ `gemini-flash-latest`, `gemini-1.5-flash`, `gemini-1.5-pro` qua Google AI REST API).
  - Anthropic Claude: `ClaudeAiEditorService.cs` (`claude-3-5-sonnet-20241022` qua Anthropic Messages API).
  - Cơ chế dự phòng `SimulationMode`: Tạo kịch bản mẫu nội bộ khi không có mạng/key để kiểm thử test suite mà không tốn credit.
- **AI Voice / Audio:**
  - `EdgeTtsService`: Sử dụng Microsoft Edge Neural TTS (gói WebSocket mã nguồn mở, hỗ trợ tiếng Việt cực kỳ tự nhiên `vi-VN-HoaiMyNeural`, `vi-VN-NamMinhNeural` hoàn toàn miễn phí, 0 đồng, không cần API key).
  - `GeminiTtsService`: Gọi TTS từ Gemini nếu có cấu hình.
  - `ElevenLabsTtsService`: Stub mẫu tích hợp ElevenLabs.
- **AI Image & Video Generation:**
  - Hiện tại hệ thống tin tức sử dụng ảnh trích xuất từ bài báo gốc (`ArticleImage`) + `CommonsImageFinder`. **Chưa có module Text-to-Image / Image-to-Video bằng AI sinh hình.**

### 2.10. Cách lưu API Key và cấu hình AI
- Cấu hình qua: `appsettings.json`, `appsettings.Development.json`, Environment Variables và .NET User Secrets.
- **Cơ chế "Free First, Paid After" độc đáo:** Lớp `GeminiKeys.cs` hỗ trợ quản lý 2 tầng khóa:
  - `Ai:Gemini:FreeApiKey` / `GEMINI_FREE_API_KEY`: Key thuộc Google Cloud Project không gắn thẻ visa (Free Tier).
  - `Ai:Gemini:ApiKey` / `GEMINI_API_KEY`: Key có gắn thanh toán (Paid Tier).
  - Hệ thống luôn ưu tiên dùng Key miễn phí trước; khi gặp lỗi hết quota (HTTP 429/ResourceExhausted), tự động đưa key miễn phí vào trạng thái nghỉ ("resting") và chuyển sang key có phí hoặc chờ đợi, tránh lãng phí chi phí.

### 2.11. Hệ thống hàng đợi và Background Jobs
- Sử dụng **Hangfire** với bộ lưu trữ PostgreSQL (`Hangfire.PostgreSql`, schema `hangfire`).
- Quản lý 3 queue: `critical`, `render`, `default`.
- Giới hạn tải: `MaxConcurrentRenderJobs: 2` (ngăn nghẽn CPU/GPU khi chạy FFmpeg).
- **Phục hồi tiến trình:** `JobRecoveryService.cs` tự động quét các tác vụ bị treo (Running/Enqueued) khi máy chủ restart đột ngột, phục hồi về trạng thái an toàn, đảm bảo tính bất biến (Idempotency).
- Lập lịch tự động: `IRecurringJobManager` quản lý chu kỳ kiểm tra đăng tự động mỗi 2 phút, quét tin tức hot mỗi 30 phút.

### 2.12. Hệ thống tạo hình ảnh, video, âm thanh hiện có
- `FFmpegRendererService.cs`: Động cơ dựng phim mạnh mẽ xây dựng trên FFmpeg CLI.
  - Tạo chuyển động điện ảnh Ken Burns (pan, tilt, zoom in, zoom out, smooth easing).
  - Tạo dải màu nền cinematic, hiệu ứng viền mờ (blur background) cho ảnh không chuẩn tỷ lệ.
  - Tạo phụ đề Karaoke / Vietsub động theo thời gian khớp từng mili-giây với file audio thoại (drawtext filter / ASS subtitle styling).
  - Xuất bản chuẩn định dạng: MP4, H.264, AAC, 30 FPS, độ phân giải 1080x1920 (9:16) hoặc 1920x1080 (16:9).
- Bộ trộn âm thanh (Audio Mixer): Trộn giọng đọc thuyết minh (Voiceover) + Nhạc nền (BGM) với kỹ thuật Auto-ducking (nhạc tự nhỏ khi có giọng đọc).

### 2.13. Hệ thống quản lý lịch đăng
- `AutoPostSettings`: Cấu hình khung giờ đăng trong ngày theo giờ Việt Nam (ví dụ: `07:00,11:30,17:30,20:00,21:30`), thời gian chuẩn bị trước (`LeadMinutes: 40`), bộ lọc chủ đề.
- `AutoPostRuns`: Lưu vết nhật ký từng lần kích hoạt xuất bản tự động.
- Cổng kiểm định biên tập (Review Gate): `VideoReview` bắt buộc trạng thái `Approved` trước khi được phép xuất bản, trừ khi bật chế độ Auto-approve có kiểm định fact-check.

### 2.14. Hệ thống thống kê chi phí API (AI Cost Meter)
- Thực thể `AiUsage` ghi nhận chi tiết: `Kind`, `Model`, `FreeKey`, `InputTokens`, `OutputTokens`, `CostUsd`, `CreatedAtUtc`.
- Dịch vụ `AiUsageMeter.cs` tự động phân tích trường `usageMetadata` trả về từ Google Gemini API, tính toán chi phí thực tế theo USD/1 triệu token.
- `AiUsageController` & Dashboard: Hiển thị biểu đồ chi phí theo ngày, chi phí theo loại tác vụ, ước tính chi phí cả tháng, cảnh báo vượt ngưỡng ngân sách (`ai-budget`).

### 2.15. Các module và quy trình đang hoạt động
1. **Module Tin đang hot (Trending News):** Quét RSS từ các báo lớn (VnExpress, Tuổi Trẻ, Thanh Niên,...), phân loại chủ đề.
2. **Module Thu thập bài báo (Article Extractor):** Tải nội dung, bóc tách tiêu đề, tóm tắt, hình ảnh chất lượng cao.
3. **Module Biên tập kịch bản (AI Editor):** Tạo kịch bản 6 cảnh (Hook, Context, Key Facts, Deep Dive, Impact, Outro).
4. **Module Sản xuất âm thanh & video (Renderer):** Gọi EdgeTTS tạo audio -> FFmpeg ghép cảnh, hiệu ứng Ken Burns, phụ đề -> Xuất file MP4.
5. **Module Phê duyệt (Review):** Biên tập viên xem trước video, sửa kịch bản, đổi giọng đọc, duyệt xuất bản.
6. **Module Xuất bản (Publisher):** Đăng tải video lên Facebook Reels và Instagram qua Graph API.

---

## 3. PHÂN LOẠI THÀNH PHẦN

### 3.1. Những thành phần CÓ THỂ TÁI SỬ DỤNG (Không viết lại)
1. **Cơ sở hạ tầng Backend:** Cấu trúc dự án Clean Architecture (.NET 8), Service Collection DI, Serilog, ProblemDetails.
2. **Cơ sở hạ tầng Frontend:** React 18 + Vite + Tailwind CSS, hệ thống Theme (Dark/Light), Router hash-based, toàn bộ bộ UI components (`ui.tsx`, `feedback.tsx`).
3. **Database & ORM:** PostgreSQL kết nối qua EF Core, cơ chế Auto Migration và Snapshot.
4. **Hệ thống Job & Worker:** Hangfire Distributed Queue, PostgreSQL Storage, cơ chế phục hồi lỗi `JobRecoveryService`.
5. **Động cơ Dựng phim FFmpeg:** `FFmpegRendererService` có thể tái sử dụng gần như toàn bộ logic xử lý Ken Burns, ghép audio/video, auto-ducking nhạc nền, burn phụ đề ASS/SRT.
6. **Bộ đo lường chi phí:** `AiUsageMeter` và bảng `AiUsages` có thể mở rộng để ghi nhận thêm chi phí cho các provider mới của Film Factory.
7. **Hệ thống Quản lý Token & Meta API:** `FacebookTokenProtectionService`, `FacebookPublishingService` để tái sử dụng đăng Reels lên Facebook/Instagram khi hoàn tất tập phim.
8. **Hệ thống TTS miễn phí:** `EdgeTtsService` với các giọng đọc tiếng Việt mượt mà hoàn toàn không mất phí.

### 3.2. Những thành phần CẦN XÂY MỚI HOÀN TOÀN (Cho AI Film Factory)
1. **Domain Entities mới (Film Factory):**
   - `FilmPage`: Quản lý 5 Film Page độc lập (avatar, thể loại, phong cách hình ảnh, giọng đọc mặc định, audience).
   - `FilmProject` & `FilmSeries`: Quản lý series phim (10 tập, tổng 50 phút).
   - `FilmEpisode`: Quản lý từng tập phim 5 phút, tiến trình sản xuất, timeline, trạng thái.
   - `FilmStoryBible`: Lưu trữ thế giới phim, timeline, quy tắc thế giới, bí mật.
   - `FilmCharacter` & `FilmCharacterReference`: Hồ sơ nhân vật (ID, ảnh chính diện, góc nghiêng, toàn thân, tính cách, giọng nói).
   - `FilmLocation`: Hồ sơ bối cảnh (kiến trúc, ánh sáng, ảnh tham chiếu).
   - `FilmContinuityRecord`: Nhật ký tính liên tục giữa các cảnh và tập (trang phục, vết thương, đồ vật cầm tay).
   - `FilmScene` & `FilmShot`: Phân rã kịch bản chi tiết đến từng cú máy (Shot 3–8s, prompt, camera motion, provider, chi phí).
   - `FilmVoiceProfile`: Bản đồ phân vai giọng đọc cho từng nhân vật.
   - `FilmCostRecord`: Nhật ký chi tiêu cho từng shot/tập phim để đảm bảo ngân sách cứng ≤ 5 USD.
2. **Hệ thống AI Showrunner & Screenwriter:**
   - Prompt engine chuyên biệt cho điện ảnh: Nhận ý tưởng cốt lõi -> Xây dựng cốt truyện 3 hồi -> Phân rã 10 tập nối tiếp -> Viết kịch bản chi tiết 5 phút/tập kèm lời thoại, chỉ dẫn diễn xuất, chuyển cảnh.
   - Continuity Checker: Phân tích mâu thuẫn kịch bản giữa tập $N$ và tập $N-1$.
3. **Visual Production Engine & Provider Adapters:**
   - Bộ điều phối hình ảnh thông minh: Tự động phân bổ tỷ lệ (10–15% AI Video cho cảnh cao trào, 70–75% Cinematic Motion Image cho cảnh tự sự/đối thoại, 10–20% Motion Graphics/Cutaways).
   - Lớp Provider Adapter thống nhất: Hỗ trợ linh hoạt các Cloud Provider (Google Veo/Flow, Kling, PixVerse, Cloud GPU) và fallback tự động sang Cinematic Motion Image khi hết quota miễn phí.
4. **Hệ thống Cost Controller (Ngân sách cứng 0–5 USD):**
   - Quản lý 4 trạng thái: `FREE_ONLY`, `LOW_COST`, `BUDGET_WARNING`, `BUDGET_EXHAUSTED`.
   - Dự toán chi phí chi tiết (Pre-production Budget Estimate) trước khi cho phép render bất kỳ shot nào.
   - Khóa cứng toàn bộ tác vụ trả phí khi chi phí chạm ngưỡng ngân sách.
5. **Giao diện người dùng AI Film Factory:**
   - Menu riêng "AI FILM FACTORY" trên Sidebar với phong cách **AI First, Luxury Tech**.
   - Bộ 20 màn hình chức năng: Dashboard tổng quan, Quản lý 5 Film Page, Tạo dự án phim mới, Kho ý tưởng, Showrunner, Series Bible, Character Studio, Location Studio, Storyboard, Phòng dựng phim, Quản lý 10 tập, Theo dõi chi phí & ngân sách.

### 3.3. Những thành phần TUYỆT ĐỐI KHÔNG ĐƯỢC SỬA
1. **Toàn bộ Domain Entities của phân hệ tin tức:** `Article`, `ArticleImage`, `VideoProject`, `VideoScene`, `VideoScript`, `VideoReview`, `TrendingArticle`, `AutoPostSettings`, `AutoPostRuns`, `DailyDigestRuns`.
2. **Các Controller và API tin tức hiện hữu:** `ArticlesController`, `VideoProjectsController`, `VideoProjectScriptsController`, `VideoReviewController`, `TrendingController`, `AutoPostController`, `DailyDigestController`.
3. **Quy trình sản xuất tin tức đang chạy:** Logic cào bài báo, phân cảnh 6 scene tin tức, quy tắc chọn ảnh bài báo A-E, cơ chế fact-check tin tức.
4. **Tài khoản và liên kết Facebook hiện có:** Dữ liệu trong bảng `FacebookAccounts`, cấu hình `Trạm Tin 24/7` đang hoạt động xuất bản Reels.

---

## 4. CÁC API HIỆN TẠI CÓ THỂ DÙNG LẠI

| API Endpoint | Mục đích tái sử dụng cho Film Factory |
| :--- | :--- |
| `GET /health` | Kiểm tra tình trạng sẵn sàng của hệ thống và PostgreSQL |
| `GET /api/system/config-summary` | Đọc thông số phần cứng, dung lượng lưu trữ, cấu hình temp disk |
| `GET /api/system/storage-metrics` | Theo dõi dung lượng lưu trữ file video phim để tránh đầy đĩa |
| `GET /api/facebook/pages` | Lấy danh sách Facebook / Instagram Pages để gán vào 5 Film Pages |
| `POST /api/facebook/verify` | Kiểm tra tính hợp lệ của Token mạng xã hội |
| `GET /api/video-media/stream/{id}` | Cơ chế stream video theo byte-range cho trình phát video phim trên UI |
| `GET /api/ai-usage` | Đọc báo cáo chi phí tổng thể của hệ sinh thái AI |

---

## 5. CÁC VẤN ĐỀ KIẾN TRÚC VÀ THÁCH THỨC KỸ THUẬT

1. **Thách thức về độ dài video (5 phút/tập × 10 tập = 50 phút):**
   - Video tin tức trước đây chỉ dài 60–90 giây (~10–20MB). Phim 5 phút (1080p) sẽ nặng khoảng 100–250MB mỗi tập; 10 tập là 1–2.5GB.
   - *Giải pháp:* Phân rã bắt buộc theo cấu trúc `Series -> Episode -> Scene -> Shot`. Mỗi shot chỉ dài từ 3 đến 8 giây. Chỉ render từng shot thành file tạm, sau đó dùng FFmpeg concat demuxer ghép lại. Nếu một shot bị lỗi, hệ thống chỉ tạo lại shot đó, không bao giờ render lại toàn bộ tập.
2. **Thách thức về tính nhất quán nhân vật (Character Consistency):**
   - Các model AI hình ảnh thông thường sẽ tạo khuôn mặt khác nhau ở mỗi câu lệnh prompt.
   - *Giải pháp:* Xây dựng `Character Bible` lưu trữ `Seed`, bảng mô tả nhân vật chi tiết (đặc điểm khuôn mặt, vóc dáng, màu mắt, kiểu tóc, trang phục đặc trưng) và ảnh tham chiếu đa góc (Front, Side, Full-body). Áp dụng kỹ thuật Face Reference / Image-to-Image / IP-Adapter tương thích với API của provider.
3. **Thách thức về phần cứng máy tính người dùng (GPU yếu):**
   - Máy tính cá nhân không thể chạy cục bộ các model video nặng (như CogVideoX, HunyuanVideo).
   - *Giải pháp:* Tận dụng các Cloud API có gói miễn phí / chi phí thấp; kết hợp động cơ dựng phim cục bộ bằng CPU thông qua FFmpeg (Ken Burns cinematic motion, camera panning/zooming trên ảnh độ phân giải cao).
4. **Tính liên tục của kịch bản xuyên suốt 10 tập:**
   - Context window của LLM nếu bị phân mảnh sẽ làm kịch bản tập 8 mâu thuẫn với tập 2.
   - *Giải pháp:* `Series Bible` đóng vai trò bộ nhớ dài hạn, lưu lại tóm tắt sự kiện (State tracking) sau mỗi tập. Khi viết tập $N$, chỉ nạp bản tóm tắt thế giới phim + hồ sơ nhân vật + sự kiện tập $N-1$.

---

## 6. ĐÁNH GIÁ RỦI RO

### 6.1. Rủi ro về Ngân sách và Chi phí (Giới hạn cứng 0–5 USD)
- **Rủi ro:** Gọi API sinh video AI thương mại bừa bãi (Runway, Luma, Sora) có giá từ \$0.20 – \$0.50 cho mỗi 5 giây video. Với 50 phút (600 shot), chi phí có thể lên tới \$120 – \$300, vi phạm hoàn toàn giới hạn 5 USD!
- **Chiến lược kiểm soát chi phí tuyệt đối:**
  1. **Tỷ lệ kết hợp thông minh:** 10–15% video AI cho cảnh chuyển động phức tạp/cao trào + 70–75% ảnh điện ảnh độ phân giải cao kết hợp camera motion của FFmpeg (chi phí 0 đồng hoặc cực thấp) + 10–20% Motion graphics/cảnh chuyển tiếp.
  2. **Thoại tiếng Việt hoàn toàn miễn phí:** Sử dụng EdgeTTS (`vi-VN-HoaiMyNeural`, `vi-VN-NamMinhNeural`) cho toàn bộ hội thoại và lồng tiếng, chi phí \$0.00.
  3. **Kịch bản bằng Gemini Free Tier:** Tận dụng tối đa `GeminiKeys.Free` để lập kịch bản và phân cảnh, chi phí \$0.00.
  4. **Cost Controller cứng:** Kiểm tra số dư ngân sách khả dụng trước mỗi lượt gọi API. Khi đạt \$4.00 (80%), phát cảnh báo; khi chạm \$5.00 (100%), tự động khóa toàn bộ API có phí và chuyển 100% sang chế độ Cinematic Motion Image + EdgeTTS.

### 6.2. Rủi ro về Database và Hiệu năng
- **Rủi ro:** Tạo quá nhiều bảng hoặc quan hệ vòng tròn (circular dependency) làm chậm DbContext.
- **Biện pháp:** Tạo các bảng Film Factory hoàn toàn độc lập với tiền tố `Film*`, thiết lập Foreign Key và Index rõ ràng trên các cột thường xuyên truy vấn (`FilmPageId`, `SeriesId`, `EpisodeId`, `Status`). Không nhồi nhét file nhị phân vào DB; chỉ lưu đường dẫn file.

### 6.3. Rủi ro về Bảo mật và Bản quyền
- Không lưu API Key dạng plaintext trong database; tiếp tục sử dụng kiến trúc bảo vệ bí mật hiện tại.
- Nhân vật AI phải là tạo hình hư cấu nguyên bản (Original AI Character), không mô phỏng người nổi tiếng hoặc nhân vật có bản quyền của bên thứ ba.
- Âm thanh và nhạc nền: Sử dụng kho nhạc miễn phí bản quyền (Royalty-free ambient tracks) lưu trong thư viện hệ thống.

---

## 7. KẾT LUẬN VÀ ĐỀ XUẤT LỘ TRÌNH TRIỂN KHAI

Hệ thống hiện tại có nền tảng kiến trúc **Clean Architecture cực kỳ vững chắc, module hóa cao, và đã kiểm chứng thành công khả năng render video cũng như kết nối mạng xã hội**. Việc xây dựng phân hệ **AI FILM FACTORY** hoàn toàn khả thi và có thể tích hợp mượt mà mà **không gây ra bất kỳ xung đột nào đối với phân hệ tin tức đang chạy**.

### Lộ trình triển khai 7 Phase tiếp theo:
- **Phase 1 – Foundation:** Thiết kế Database Entities cho Film Factory, EF Core Migrations, Quản lý 5 Film Page, Khung dự án phim & UI Navigation.
- **Phase 2 – AI Showrunner & Screenwriter:** Xây dựng Story Bible, Character Bible, Continuity Bible và AI viết kịch bản 10 tập.
- **Phase 3 – Asset Generation & Provider Adapters:** Xây dựng Character Engine, Image Engine, Video Provider Adapter với chiến lược phân bổ tỷ lệ cảnh.
- **Phase 4 – Cost Optimization Controller:** Xây dựng bộ điều khiển ngân sách cứng 0–5 USD, cảnh báo và cơ chế fallback.
- **Phase 5 – Voice & Film Renderer:** Tích hợp đa giọng đọc theo vai diễn, ghép nối shot, nhạc nền, cân bằng âm lượng và render tập phim 5 phút bằng FFmpeg.
- **Phase 6 – Production Automation:** Tích hợp Hangfire Job Queue tự động hóa toàn bộ quy trình từ ý tưởng đến 10 tập hoàn chỉnh.
- **Phase 7 – QA, Acceptance & Regression Test:** Kiểm thử toàn diện pipeline, xác thực video và đảm bảo phân hệ tin tức an toàn 100%.

---
*Báo cáo khảo sát đã được xác thực trên source code thực tế. Hệ thống sẵn sàng bước vào Phase 1.*
