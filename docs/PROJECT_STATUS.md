# AI NEWS STUDIO - TRẠNG THÁI DỰ ÁN (PROJECT STATUS)

**Cập nhật lần cuối:** 15/11/2026  
**Giai đoạn hiện tại:** **Phase 4 – AI Editor và Script Planner** (HOÀN THÀNH)

---

## 1. Tổng quan tiến độ các Phase

| Phase | Nội dung | Trạng thái | Ghi chú |
|---|---|:---:|---|
| **Phase 1** | Khởi tạo dự án và kiến trúc | **HOÀN THÀNH** | .NET 8 Clean Architecture, Serilog, Swagger, HealthCheck, React Frontend, Dockerfile API & Worker |
| **Phase 2** | Database, Hangfire và Job Processing | **HOÀN THÀNH** | PostgreSQL (13 bảng), EF Core Migrations, Hangfire Server trong Worker, Pipeline Executor, Job API |
| **Phase 3** | Article Extractor và Image Extractor | **HOÀN THÀNH** | Bóc tách nội dung báo, trích xuất nghiêm ngặt ảnh nội bộ bài báo, tính hash SHA-256 deduplication, phân loại 6 nhóm, kiểm soát RightsStatus chặt chẽ |
| **Phase 4** | **AI Editor và Script Planner** | **HOÀN THÀNH** | Tích hợp linh hoạt Gemini & Claude, kịch bản 45-60s, tách bạch Fact vs Commentary, phân cảnh 6-10 scenes chỉ dùng ảnh bài báo hoặc typography |
| **Phase 5** | TTS và Video Renderer | Chưa bắt đầu | Sắp tới: Edge-TTS/ElevenLabs, FFmpeg 9:16 (1080×1920) |
| **Phase 6** | Video Review và Quality Control | Chưa bắt đầu | Giao diện kiểm duyệt 10 tiêu chí, cơ chế duyệt/từ chối |
| **Phase 7** | Facebook Graph API | Chưa bắt đầu | Upload video binary Reels, Page Access Token bảo mật |
| **Phase 8** | Railway Deployment và Production Hardening | Chưa bắt đầu | Tối ưu hóa bộ nhớ tạm, graceful shutdown, backup |

---

## 2. Tiêu chí nghiệm thu Phase 4

- [x] **AI Editor Engine (Gemini & Claude)**:
  - Thiết kế `IAIEditorService` và `IAiEditorFactory` cho phép chuyển đổi mượt mà giữa Google Gemini (`gemini-1.5-flash`, `gemini-1.5-pro`) và Anthropic Claude (`claude-3-5-haiku`, `claude-3-5-sonnet`).
  - Hỗ trợ cấu hình qua API Key, timeout, retry, tính toán lượng token tiêu thụ và chi phí ước tính (USD).
  - Có động cơ deterministic editorial synthesis thông minh hoạt động ổn định cả khi offline hoặc chưa cấu hình API key.
- [x] **Yêu cầu kịch bản tin tức ngắn (45–60 giây)**:
  - Dung lượng kịch bản chuẩn 110–150 từ tiếng Việt, câu ngắn dưới 18 từ, văn phong tin tức truyền hình dứt khoát.
  - Cấu trúc đầy đủ: `VideoTitle`, `Hook` (3-5s gây tò mò, giữ chân người xem), `VoiceOverScript`, `KeyFacts`, `Context`, `Analysis`, `Conclusion`, `SourceAttribution`.
  - **Tách bạch nghiêm ngặt Sự thật (Key Facts) và Bình luận biên tập (Analysis)** giúp người duyệt dễ dàng thẩm định tính xác thực.
  - Tuyệt đối không sao chép nguyên văn, không bịa số liệu/sự kiện, không giật tít sai sự thật.
- [x] **Script Planner (6–10 Scenes)**:
  - Phân chia câu chuyện thành 6–10 scenes liên tiếp theo luồng tự nhiên: `Hook` -> `Fact` -> `Context` -> `Analysis` -> `Conclusion`.
  - Tính toán chính xác `StartTimeSeconds`, `EndTimeSeconds`, `DurationSeconds` với tổng thời lượng nằm trọn vẹn trong 45–60 giây.
  - Gợi ý câu phụ đề ngắn gọn, điểm nhấn (`OnScreenText`) cho từng phân cảnh.
- [x] **Tuân thủ quy tắc hình ảnh bài báo**:
  - **Chỉ sử dụng `ArticleImageId` thuộc chính bài báo hiện tại**.
  - Nếu thiếu ảnh hoặc bài báo không có ảnh: Tự động chuyển `VisualType` sang **Typography** hoặc **MetricChart** / **Timeline**, hiển thị chữ nổi bật trên nền chuyển động.
  - Tuyệt đối không tạo ảnh AI, không tìm ảnh bên ngoài bài báo.
  - Phân bổ hiệu ứng `MotionType` (`ZoomIn`, `ZoomOut`, `PanLeft`, `PanRight`, `KenBurns`) và `TransitionType` (`Fade`, `Slide`, `Cut`).
- [x] **REST APIs hoàn chỉnh**:
  - `POST /api/video-projects/{id}/script/generate`
  - `GET /api/video-projects/{id}/script`
  - `PUT /api/video-projects/{id}/script`
  - `POST /api/video-projects/{id}/scenes/plan`
  - `GET /api/video-projects/{id}/scenes`
  - `PUT /api/video-projects/{id}/scenes`
- [x] **Giao diện Review Kịch bản & Phân cảnh**:
  - Tab Kịch bản: Hiển thị tiêu đề, hook, lời thoại lồng tiếng, và bảng phân chia Fact vs Commentary trực quan.
  - Tab Phân cảnh: Hiển thị 6-10 cards phân cảnh với mốc thời gian, loại hình ảnh (Ảnh bài viết vs Nền Typography), motion, transition, giọng đọc và chữ nổi bật.
  - Switcher chọn nhà cung cấp AI: Gemini / Claude trực tiếp trên UI.
- [x] **Kiểm thử tự động**:
  - 30/30 test cases xUnit đạt 100% PASS.
