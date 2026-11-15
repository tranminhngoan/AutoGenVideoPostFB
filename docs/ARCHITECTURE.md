# AI NEWS STUDIO - TÀI LIỆU KIẾN TRÚC HỆ THỐNG (ARCHITECTURE)

## 1. Mô hình kiến trúc tổng thể

Hệ thống được thiết kế theo nguyên lý **Clean Architecture** và hướng dịch vụ phân tách rõ rệt giữa lớp nhận yêu cầu (**NewsStudio.Api**) và lớp xử lý tác vụ nặng bất đồng bộ (**NewsStudio.Worker**).

```
                      +-----------------------------+
                      |      ReactJS + TS Admin     |
                      |   (Vite + Tailwind CSS)     |
                      +--------------+--------------+
                                     |
                               REST API / JSON
                                     v
                      +-----------------------------+
                      |       NewsStudio.Api        |
                      |  - Swagger / OpenAPI        |
                      |  - Serilog Request Logging  |
                      |  - HealthCheck Endpoints    |
                      |  - Hangfire Dashboard       |
                      |  - ArticlesController       |
                      +--------------+--------------+
                                     |
                          Tạo Job & Truy vấn DB
                                     v
      +------------------------------+-------------------------------+
      |                                                              |
      v                                                              v
+-----------------------------+                      +-------------------------------+
|     NewsStudio.Worker       |                      |     PostgreSQL Database       |
|  - Hangfire Server          |                      |  - Schema public: 13 tables   |
|  - Article / Image Extract  |<====================>|  - Schema hangfire: queue/jobs|
|  - AI Scripting (Gemini)    |                      |  - Dữ liệu nghiệp vụ          |
|  - TTS Audio Synthesis      |                      |  - Metadata VideoProjects     |
|  - FFmpeg Video Renderer    |                      |  (Không lưu binary media)     |
|  - Ephemeral Temp Storage   |                      +-------------------------------+
+--------------+--------------+
               |
               v
+-----------------------------+
|    Meta Graph API (Reels)   |
|  - Fanpage Reels Publishing |
+-----------------------------+
```

---

## 2. Chi tiết Database Schema (PostgreSQL 16)

Hệ thống quản lý 13 thực thể nghiệp vụ thông qua EF Core 8:
1. **Articles**: Lưu trữ thông tin bài báo (Title, NormalizedTitle, Url, CanonicalUrl, Publisher, ContentBody, PublishedAtUtc, Status).
2. **ArticleImages**: Quản lý hình ảnh gốc bài báo (OriginalImageUrl, LocalTempPath, Caption, AltText, Order, Width, Height, ImageHash, RightsStatus, Category, Status).
3. **VideoProjects**: Trung tâm dự án video (Title, SourceUrl, Publisher, TargetDurationSeconds, Status theo 14 vòng đời, Resolution 1080x1920, AspectRatio 9:16).
4. **VideoScenes**: Phân cảnh chi tiết (SceneNumber, StartTime, EndTime, VoiceText, OnScreenText, SelectedArticleImageId, MotionType, TransitionType, ScenePurpose).
5. **VideoScripts**: Kịch bản do AI biên soạn (Title, Hook, VoiceOverScript, KeyFacts, Context, Analysis, Conclusion, SourceAttribution, TokenUsage).
6. **VideoAssets**: Theo dõi các file tạm (AssetType, LocalTempPath, FileSizeBytes, MimeType, Sha256Hash, ExpiresAtUtc, IsCleanedUp).
7. **ProcessingJobs**: Theo dõi tiến trình Hangfire (HangfireJobId, Step, Status, RetryCount, ProgressPercentage, IdempotencyKey, WorkerHost).
8. **VideoReviews**: Báo cáo kiểm định chất lượng nội bộ (ReviewedBy, Decision, FeedbackNotes, InternalQualityScore, SeriousViolationsFound).
9. **FacebookAccounts**: Quản lý kết nối Page (PageId, PageName, EncryptedAccessToken, TokenExpiresAtUtc, IsActive).
10. **FacebookPosts**: Bản ghi xuất bản Reels (FacebookPostId, FacebookVideoId, UploadSessionId, UploadStatus, PublishedAtUtc).
11. **VideoMetrics**: Chỉ số đo lường sau xuất bản (Views, Reach, Engagement, Shares, Comments, WatchTimeSeconds, CollectedAtUtc).
12. **SystemSettings**: Bảng cấu hình động theo nhóm (AI, TTS, Storage, Facebook, Hangfire).
13. **AuditLogs**: Nhật ký kiểm toán thao tác hệ thống (EntityName, EntityId, Action, PerformedBy, DetailsJson).

---

## 3. Vòng đời tác vụ (Job Lifecycle)

Tác vụ xử lý video trải qua 14 trạng thái kế tiếp:
1. `Pending`: Dự án vừa được tạo từ URL.
2. `Extracting`: Worker đang tải và bóc tách HTML bài báo.
3. `Extracted`: Nội dung và danh sách ảnh gốc đã sẵn sàng.
4. `Analyzing`: AI đang phân tích dữ liệu, bối cảnh, sự kiện.
5. `Scripting`: AI biên tập kịch bản 45-60 giây.
6. `PlanningScenes`: Chia kịch bản thành 6-10 cảnh, ghép ảnh bài báo.
7. `GeneratingAudio`: Lồng tiếng tiếng Việt qua TTS.
8. `Rendering`: FFmpeg kết xuất video 9:16 (1080×1920).
9. `QualityChecking`: Kiểm định tự động 10 tiêu chí chất lượng.
10. `AwaitingApproval`: Video hoàn tất và **dừng lại chờ người dùng phê duyệt**.
11. `Publishing`: Đang upload video binary lên Facebook Reels.
12. `Published`: Đăng Reels thành công, lưu Post ID/Video ID.
13. `Failed`: Tác vụ gặp lỗi không thể phục hồi.
14. `Cancelled`: Tác vụ bị hủy bởi người dùng hoặc hệ thống.

---

## 4. Xử lý bất đồng bộ với Hangfire

- **Storage**: Lưu trữ job state trong PostgreSQL schema `hangfire`.
- **Worker Process**: `NewsStudio.Worker` khởi chạy `BackgroundJobServer` với số lượng worker xử lý đồng thời (`WorkerCount = 2`).
- **Idempotency & Retry**: Mỗi job được gắn `IdempotencyKey`. Sử dụng `[AutomaticRetry(Attempts = 3)]` có giới hạn và ghi log rõ ràng theo `JobId`.
- **API không chặn**: `NewsStudio.Api` chỉ tiếp nhận yêu cầu, sinh ID, lưu DB và chuyển giao cho Hangfire qua `IBackgroundJobClient.Enqueue`.

---

## 5. Kiến trúc trích xuất bài báo và kiểm soát hình ảnh (Phase 3)

### 5.1. Mô hình Adapter bóc tách bài báo (`IWebsiteAdapter`)
Áp dụng mẫu **Chain of Responsibility**:
- `VnExpressAdapter`: Nhận diện `vnexpress.net`, bóc tách `h1.title-detail`, `p.description`, đoạn văn `p.Normal` trong `article.fck_detail`, khối ảnh `figure.tplCaption` kèm chú thích `figcaption p.Image`.
- `TuoiTreAdapter`: Nhận diện `tuoitre.vn`, bóc tách `h1.article-title`, `h2.article-sapo`, khối ảnh `div.VCSortable` / `PhotoCMS_Caption`.
- `ThanhNienAdapter`: Nhận diện `thanhnien.vn`, bóc tách cấu trúc bài báo Thanh Niên.
- `DanTriAdapter`: Nhận diện `dantri.com.vn`, bóc tách cấu trúc bài báo Dân Trí.
- `GenericArticleAdapter`: Bộ thích ứng cơ sở và dự phòng cho mọi trang tin: ưu tiên JSON-LD `NewsArticle` (`schema.org`), OpenGraph (`og:title`, `og:description`, `og:image`), semantic HTML (`<article>`, `<figure>`, `<figcaption>`).

### 5.2. Nguyên tắc bất biến trích xuất hình ảnh
- **Chỉ trích xuất ảnh gốc trong chính bài báo**: Không lấy ảnh từ Google Images, không dùng ảnh AI, không tạo nhân vật giả mạo.
- **Lọc nhiễu triệt để**: Tự động loại bỏ ảnh banner quảng cáo (`/ads/`, `banner`), ảnh avatar, icon mạng xã hội, pixel theo dõi.
- **Deduplication bằng SHA-256**: Băm toàn bộ URL ảnh để triệt tiêu trùng lặp và đánh chỉ số thứ tự rõ ràng (`ArticleImageOrder`).
- **Xử lý bài báo không có ảnh**: Hệ thống bóc tách an toàn và cho phép kịch bản video sử dụng nền typography hoặc số liệu ở các phase sau.

### 5.3. Kiểm soát quyền sử dụng (`RightsStatus`)
- Các trạng thái: `Unknown`, `PermissionRequired`, `Licensed`, `Authorized`, `Rejected`.
- **Quy tắc cứng**: Quyền hình ảnh khi trích xuất **bắt buộc mặc định là `Unknown`**. Hệ thống tuyệt đối không tự động nâng cấp lên `Authorized`. Việc cấp quyền phải do biên tập viên xác nhận trên giao diện kiểm duyệt.

### 5.4. Phân loại hình ảnh tự động (`ImageClassifierService`)
- Phân tích ngữ cảnh nội dung và caption ảnh thành 6 nhóm:
  1. `Panorama` (Toàn cảnh / Không gian)
  2. `Character` (Nhân vật / Chuyên gia - không tự suy đoán danh tính nếu không có căn cứ)
  3. `Event` (Sự kiện / Hội nghị / Lễ ký kết)
  4. `Document` (Tài liệu / Văn bản / Quyết định)
  5. `Chart` (Biểu đồ / Đồ thị / Số liệu)
  6. `Unsuitable` (Không phù hợp / Quảng cáo)

---

## 6. Kiến trúc AI Editor và Script Planner (Phase 4)

### 6.1. Thiết kế đa nhà cung cấp AI (`IAIEditorService` & `IAiEditorFactory`)
Hệ thống áp dụng mẫu **Factory / Strategy** cho phép thay đổi nhà cung cấp AI linh hoạt giữa Google Gemini và Anthropic Claude:
- `GeminiAiEditorService`: Sử dụng REST API của Gemini (`gemini-1.5-flash`, `gemini-1.5-pro`), tính toán token usage và chi phí ước tính ($0.075 / 1M input, $0.30 / 1M output).
- `ClaudeAiEditorService`: Sử dụng REST API của Claude (`claude-3-5-haiku-20241022`, `claude-3-5-sonnet-20241022`), tính toán token usage và chi phí ước tính.
- `Deterministic Editorial Engine`: Động cơ tổng hợp tin tức ngữ nghĩa cục bộ đảm bảo hệ thống và các luồng kiểm thử chạy ổn định ngay cả khi chưa có API key ngoài hoặc mất kết nối mạng.

### 6.2. Cấu trúc kịch bản tin tức ngắn (45–60 giây)
- **Chuẩn thời lượng**: 110–150 từ tiếng Việt, tốc độ đọc 2.3 từ/giây.
- **Tách bạch Fact vs Commentary**:
  - `KeyFacts`: Dữ kiện khách quan (số liệu, thời gian, sự kiện, địa điểm).
  - `Context`: Bối cảnh cần biết.
  - `Analysis`: Đánh giá tác động, góc nhìn chuyên môn, tạo giá trị gia tăng riêng cho video.
  - `Hook`: 3-5 giây mở đầu cuốn hút, giữ chân người xem lướt Reels.
  - `Conclusion`: Lời kết súc tích hoặc câu hỏi mở.
  - `SourceAttribution`: Dẫn nguồn báo chí chính thức (ví dụ: 'Theo VnExpress').

### 6.3. Script Planner (6–10 Scenes) & Quy tắc gán hình ảnh
- `ScriptPlannerService` tự động phân tách kịch bản thành 6 đến 10 phân cảnh liên tục:
  - `StartTimeSeconds` và `EndTimeSeconds` phân bổ theo độ dài lời thoại, tổng thời lượng nằm trọn vẹn trong 45–60 giây.
  - `OnScreenText`: Dòng chữ nổi bật ngắn gọn (3-7 từ) viết hoa điểm nhấn.
  - **Quy tắc gán hình ảnh**:
    - **Chỉ sử dụng `ArticleImageId` của bài báo hiện tại**, ưu tiên ảnh hợp lệ (`RightsStatus != Rejected`, `Category != Unsuitable`).
    - Nếu bài báo thiếu ảnh hoặc không có ảnh: Tự động gán `VisualType = "Typography"` hoặc `"MetricChart"`, hiển thị typography chuyển động trên nền tối. Tuyệt đối không sinh ảnh AI, không lấy ảnh ngoài.
  - Cấu hình chuyển động: `MotionType` (`ZoomIn`, `ZoomOut`, `PanLeft`, `PanRight`, `KenBurns`) và `TransitionType` (`Fade`, `Slide`, `Cut`).
