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
                      +--------------+--------------+
                                     |
                          Tạo Job & Truy vấn DB
                                     v
      +------------------------------+-------------------------------+
      |                                                              |
      v                                                              v
+-----------------------------+                      +-------------------------------+
|     NewsStudio.Worker       |                      |     PostgreSQL Database       |
|  - Hangfire Server          |                      |  - Dữ liệu nghiệp vụ          |
|  - Article / Image Extract  |<====================>|  - Metadata VideoProjects     |
|  - AI Scripting (Gemini)    |                      |  - Trạng thái Job Hangfire    |
|  - TTS Audio Synthesis      |                      |  (Không lưu binary media)     |
|  - FFmpeg Video Renderer    |                      +-------------------------------+
|  - Ephemeral Temp Storage   |
+--------------+--------------+
               |
               v
+-----------------------------+
|    Meta Graph API (Reels)   |
|  - Fanpage Reels Publishing |
+-----------------------------+
```

---

## 2. Chi tiết các tầng trong Clean Architecture

### 2.1. `NewsStudio.Domain` (Core Domain)
- Độc lập hoàn toàn, không phụ thuộc vào bất kỳ tầng nào khác.
- Chứa:
  - `BaseEntity`: Cung cấp `Id` (GUID), `CreatedAtUtc`, `UpdatedAtUtc`.
  - `Enums`: `VideoProjectStatus` (14 trạng thái vòng đời), `RightsStatus` (quyền sử dụng ảnh), `AiProviderType`, `TtsProviderType`, `ImageCategory`.

### 2.2. `NewsStudio.Application` (Business Logic Interfaces & DTOs)
- Phụ thuộc duy nhất vào `NewsStudio.Domain`.
- Chứa:
  - `DTOs`: `SystemHealthDto`, `ComponentHealthDto`.
  - Interfaces:
    - `IAiEditorService`: Giao diện dịch vụ AI biên tập kịch bản (Gemini, Claude).
    - `IScriptPlannerService`: Giao diện phân bổ phân cảnh.
    - `ITtsService`: Giao diện tổng hợp giọng đọc tiếng Việt.
    - `IFacebookPublishingService`: Giao diện kết nối và đăng Facebook Reels.
    - `ISystemHealthService`: Giao diện kiểm tra chẩn đoán hệ thống.

### 2.3. `NewsStudio.Infrastructure` (External Concerns & Implementations)
- Phụ thuộc vào `NewsStudio.Application` và `NewsStudio.Domain`.
- Chứa các triển khai dịch vụ ngoài, truy cập file tạm, kiểm tra phần cứng, và tích hợp bên thứ ba.

### 2.4. `NewsStudio.Api` (Presentation / Entry Point)
- Điểm tiếp nhận request từ người dùng và trình duyệt.
- Chứa:
  - Controllers: `HealthController`, `SystemController`.
  - Swagger/OpenAPI 3.0.
  - Serilog Request Logging.
  - CORS policy.

### 2.5. `NewsStudio.Worker` (Background Processing Service)
- Phục vụ các tác vụ nặng: crawl bài báo, trích xuất ảnh, gọi AI, gọi TTS, render FFmpeg.
- Tích hợp kiểm soát vòng đời ứng dụng (SIGTERM graceful shutdown).
- Quản lý thư mục file tạm `/tmp/newsstudio`.

---

## 3. Chính sách lưu trữ dữ liệu & File tạm (Ephemerality)

1. **PostgreSQL Database**:
   - Chỉ lưu thông tin metadata, kịch bản text, phân cảnh, chỉ số metrics và nhật ký job.
   - **Tuyệt đối không lưu binary ảnh, audio hoặc video MP4** trong Database.
2. **Thư mục tạm Worker**:
   - Mọi tệp ảnh gốc, audio lồng tiếng, phụ đề ass/srt và video MP4 được lưu trong `/tmp/newsstudio/{JobId}/`.
   - Cơ chế tự dọn dẹp (Cleanup Policy): Sau khi đăng Reels thành công, hoặc người dùng từ chối video, hoặc hết hạn xem trước (TTL 120 phút), toàn bộ thư mục của Job sẽ bị xóa sạch khỏi ổ đĩa.
