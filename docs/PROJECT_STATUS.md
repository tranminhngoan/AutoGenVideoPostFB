# AI NEWS STUDIO - TRẠNG THÁI DỰ ÁN (PROJECT STATUS)

**Cập nhật lần cuối:** 15/11/2026  
**Giai đoạn hiện tại:** **Phase 2 – Database, Hangfire và Job Processing** (HOÀN THÀNH)

---

## 1. Tổng quan tiến độ các Phase

| Phase | Nội dung | Trạng thái | Ghi chú |
|---|---|:---:|---|
| **Phase 1** | Khởi tạo dự án và kiến trúc | **HOÀN THÀNH** | .NET 8 Clean Architecture, Serilog, Swagger, HealthCheck, React Frontend, Dockerfile API & Worker |
| **Phase 2** | **Database, Hangfire và Job Processing** | **HOÀN THÀNH** | PostgreSQL (13 bảng), EF Core Migrations, Hangfire Server trong Worker, Pipeline Executor, Job API |
| **Phase 3** | Article Extractor và Image Extractor | Chưa bắt đầu | Sắp tới: Crawler nội dung bài báo, trích xuất hình ảnh gốc, phân loại & kiểm soát quyền |
| **Phase 4** | AI Editor và Script Planner | Chưa bắt đầu | Tích hợp Gemini/Claude API, kịch bản 45-60s, phân cảnh 6-10 scenes |
| **Phase 5** | TTS và Video Renderer | Chưa bắt đầu | Edge-TTS/ElevenLabs, FFmpeg 9:16 (1080×1920) |
| **Phase 6** | Video Review và Quality Control | Chưa bắt đầu | Giao diện kiểm duyệt 10 tiêu chí, cơ chế duyệt/từ chối |
| **Phase 7** | Facebook Graph API | Chưa bắt đầu | Upload video binary Reels, Page Access Token bảo mật |
| **Phase 8** | Railway Deployment và Production Hardening | Chưa bắt đầu | Tối ưu hóa bộ nhớ tạm, graceful shutdown, backup |

---

## 2. Tiêu chí nghiệm thu Phase 2

- [x] **PostgreSQL Database Schema**: Thiết kế và hoàn tất migration cho 13 bảng:
  - `Articles`, `ArticleImages`, `VideoProjects`, `VideoScenes`, `VideoScripts`, `VideoAssets`, `ProcessingJobs`, `VideoReviews`, `FacebookAccounts`, `FacebookPosts`, `VideoMetrics`, `SystemSettings`, `AuditLogs`.
- [x] **Toàn vẹn quan hệ & Khóa**: Đầy đủ Primary Key, Foreign Key (Cascade/SetNull), Indexes (Url, Status, CreatedAtUtc, IdempotencyKey).
- [x] **Nguyên tắc Ephemerality**: Không lưu binary ảnh, audio, MP4 vào PostgreSQL; chỉ lưu metadata và đường dẫn file tạm.
- [x] **Hangfire Integration**: Cấu hình PostgreSQL Storage với schema `hangfire`, thiết lập `HangfireServer` chạy trong `NewsStudio.Worker` (WorkerCount = 2).
- [x] **Job Processing Pipeline**: Xây dựng `VideoProcessingJobExecutor` mô phỏng đầy đủ 14 trạng thái vòng đời, tự động dừng ở trạng thái `AwaitingApproval` chờ người dùng duyệt.
- [x] **Cơ chế Idempotency & Retry**: Idempotency Key chống trùng lặp job, `[AutomaticRetry(Attempts = 3)]` có timeout và cancellation token.
- [x] **Bộ REST APIs đầy đủ**:
  - `POST /api/video-projects`
  - `GET /api/video-projects`
  - `GET /api/video-projects/{id}`
  - `POST /api/video-projects/{id}/process`
  - `POST /api/jobs/{id}/cancel`
  - `GET /api/jobs/{id}`
- [x] **Kiểm thử tự động**: 13/13 test cases xUnit đạt 100% PASS (bao gồm luồng ghi DB, enqueue job, truy vấn, hủy job và chạy pipeline).
