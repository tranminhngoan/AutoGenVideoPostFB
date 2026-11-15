# AI NEWS STUDIO - TRẠNG THÁI DỰ ÁN (PROJECT STATUS)

**Cập nhật lần cuối:** 15/11/2026  
**Giai đoạn hiện tại:** **Phase 3 – Article Extractor và Image Extractor** (HOÀN THÀNH)

---

## 1. Tổng quan tiến độ các Phase

| Phase | Nội dung | Trạng thái | Ghi chú |
|---|---|:---:|---|
| **Phase 1** | Khởi tạo dự án và kiến trúc | **HOÀN THÀNH** | .NET 8 Clean Architecture, Serilog, Swagger, HealthCheck, React Frontend, Dockerfile API & Worker |
| **Phase 2** | Database, Hangfire và Job Processing | **HOÀN THÀNH** | PostgreSQL (13 bảng), EF Core Migrations, Hangfire Server trong Worker, Pipeline Executor, Job API |
| **Phase 3** | **Article Extractor và Image Extractor** | **HOÀN THÀNH** | Bóc tách nội dung báo, trích xuất nghiêm ngặt ảnh nội bộ bài báo, tính hash SHA-256 deduplication, phân loại 6 nhóm, kiểm soát RightsStatus chặt chẽ |
| **Phase 4** | AI Editor và Script Planner | Chưa bắt đầu | Sắp tới: Tích hợp Gemini/Claude API, kịch bản 45-60s, phân cảnh 6-10 scenes |
| **Phase 5** | TTS và Video Renderer | Chưa bắt đầu | Edge-TTS/ElevenLabs, FFmpeg 9:16 (1080×1920) |
| **Phase 6** | Video Review và Quality Control | Chưa bắt đầu | Giao diện kiểm duyệt 10 tiêu chí, cơ chế duyệt/từ chối |
| **Phase 7** | Facebook Graph API | Chưa bắt đầu | Upload video binary Reels, Page Access Token bảo mật |
| **Phase 8** | Railway Deployment và Production Hardening | Chưa bắt đầu | Tối ưu hóa bộ nhớ tạm, graceful shutdown, backup |

---

## 2. Tiêu chí nghiệm thu Phase 3

- [x] **Adapter-based Article Extractor**:
  - `IWebsiteAdapter` và chuỗi xử lý thích ứng: `VnExpressAdapter`, `TuoiTreAdapter`, `ThanhNienAdapter`, `DanTriAdapter` cùng `GenericArticleAdapter` (hỗ trợ Schema.org `NewsArticle` JSON-LD & OpenGraph).
  - Trích xuất toàn diện: `Title`, `Description`, `ContentBody`, `Author`, `Publisher`, `CanonicalUrl`, `PublishedAtUtc`.
  - Tuân thủ nguyên tắc không paywall/bypass, User-Agent chuẩn.
- [x] **Nghiêm ngặt trích xuất hình ảnh gốc (Original Article Images Only)**:
  - Chỉ lấy ảnh từ thẻ `figure`, khối ảnh nội dung (`fck_detail`, `detail-content`, `VCSortableInLine`, `PhotoCMS_Caption`), hoặc metadata chính thức của bài.
  - Lọc bỏ triệt để quảng cáo (`/ads/`, banners), pixel theo dõi, SVG biểu tượng mạng xã hội (Facebook, Zalo).
  - Nghiêm cấm ảnh AI, ảnh Google Images, ảnh nhân vật giả định.
- [x] **Deduplication & Metadata**:
  - Băm SHA-256 toàn bộ URL/ảnh để chống trùng lặp.
  - Lưu trữ thứ tự `ArticleImageOrder`, `Width`, `Height`, `Caption`, `AltText`, `ImageHash`.
- [x] **Quản lý bản quyền (RightsStatus Enforcement)**:
  - Giá trị mặc định luôn là `Unknown` hoặc `PermissionRequired`.
  - Nghiêm cấm hệ thống tự ý nâng cấp trạng thái thành `Authorized` mà không qua người dùng kiểm duyệt.
- [x] **Phân loại hình ảnh (Image Classification)**:
  - Phân loại chuẩn xác 6 nhóm nghiệp vụ: `Panorama` (Toàn cảnh), `Character` (Nhân vật), `Event` (Sự kiện), `Document` (Tài liệu), `Chart` (Biểu đồ), `Unsuitable` (Không phù hợp).
  - Không tự suy đoán danh tính nhân vật nếu không có căn cứ.
- [x] **REST APIs**:
  - `POST /api/articles/extract`
  - `GET /api/articles`
  - `GET /api/articles/{id}`
  - `PUT /api/articles/{id}/images/{imageId}`
- [x] **Giao diện Article Preview & Image Inspector**:
  - Form bóc tách link bài báo trực quan.
  - Modal xem chi tiết nội dung trích xuất và danh sách ảnh gốc.
  - Bộ điều khiển thay đổi caption, phân loại ảnh, gán quyền sử dụng và bật/tắt lựa chọn ảnh cho video.
- [x] **Kiểm thử tự động**:
  - Toàn bộ 23/23 tests xUnit đạt 100% PASS.
