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
