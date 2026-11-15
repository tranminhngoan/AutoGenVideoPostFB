# AI News Studio

> **Hệ thống chuyển đổi bài báo thành video tin tức ngắn 9:16 (1080 × 1920) tự động đăng Facebook Reels.**

---

## 🌟 Tổng quan dự án

AI News Studio là giải pháp AI-First hiện đại cho phép biên tập viên dán một liên kết bài báo (VnExpress, Tuổi Trẻ, Thanh Niên...), trích xuất nội dung và hình ảnh từ chính bài báo, sử dụng AI (Google Gemini / Anthropic Claude) viết kịch bản tin tức 45–60 giây, lồng tiếng tiếng Việt tự nhiên (TTS), tự động dựng video định dạng Reels dọc 9:16 bằng FFmpeg, kiểm định chất lượng (QC) và xuất bản lên Facebook Fanpage thông qua Meta Graph API.

### 🛡️ Nguyên tắc nghiệp vụ cố định
1. **Chỉ trích xuất ảnh trong bài báo gốc**: Tuyệt đối không lấy ảnh ngoài Internet, không tạo ảnh AI, không tạo khuôn mặt giả.
2. **Kịch bản biên tập độc lập**: AI viết lại tin tức ngắn gọn, súc tích (45-60s), phân biệt rõ giữa Fact và bình luận biên tập, không bịa đặt số liệu.
3. **Lưu trữ tạm (Ephemerality)**: MP4, audio và ảnh chỉ lưu tạm trong quá trình dựng (`/tmp/newsstudio`) và tự động dọn dẹp sau khi đăng hoặc hết hạn xem trước. Không lưu file media lớn trong PostgreSQL.
4. **Phê duyệt bắt buộc**: Không tự ý đăng tải lên Facebook nếu người dùng chưa bấm duyệt trên giao diện Admin.

---

## 🏗️ Kiến trúc hệ thống (.NET 8 Clean Architecture)

```
AutoGenVideoPostFB/
├── backend/
│   ├── NewsStudio.sln
│   ├── src/
│   │   ├── NewsStudio.Domain/            # Thực thể nghiệp vụ, Enums, Aggregate Roots
│   │   ├── NewsStudio.Application/       # DTOs, Interfaces (IAiEditor, ITts, IFacebook)
│   │   ├── NewsStudio.Infrastructure/    # Triển khai dịch vụ hệ thống, Logging, Health
│   │   ├── NewsStudio.Api/               # ASP.NET Core Web API, Swagger, CORS, HealthChecks
│   │   └── NewsStudio.Worker/            # Background Worker Service (FFmpeg, Hangfire)
│   └── tests/
│       └── NewsStudio.Tests/             # Integration tests & Architecture tests
├── frontend/                             # React 18 + TypeScript + Vite + Tailwind CSS
├── docker/
│   ├── Dockerfile.api                    # Container API cho Railway
│   ├── Dockerfile.worker                 # Container Worker + FFmpeg + Vietnamese Fonts
│   └── docker-compose.yml                # Cụm dịch vụ Docker local
├── docs/                                 # Tài liệu kỹ thuật dự án
├── railway.json                          # Cấu hình triển khai Railway
└── .env.example                          # Danh mục biến môi trường chuẩn
```

---

## 🚀 Hướng dẫn chạy máy Local

### Yêu cầu cài đặt
- **.NET 8 SDK** (hoặc .NET 9 SDK tương thích net8.0)
- **Node.js** v18+ & npm
- *(Tùy chọn cho Docker)*: Docker Desktop

### 1. Khởi chạy Backend API & Worker
Mở terminal tại thư mục gốc của dự án:
```bash
cd backend
dotnet restore
dotnet build

# Chạy Backend API (Port 8080)
dotnet run --project src/NewsStudio.Api

# Mở một terminal khác để chạy Worker Service:
dotnet run --project src/NewsStudio.Worker
```

- **Swagger Documentation**: [http://localhost:8080/swagger](http://localhost:8080/swagger)
- **Health Check API**: [http://localhost:8080/health](http://localhost:8080/health)

### 2. Khởi chạy Frontend React
Mở terminal tại thư mục `frontend`:
```bash
cd frontend
npm install
npm run dev
```

- **Admin Studio UI**: [http://localhost:5173](http://localhost:5173)

### 3. Chạy toàn bộ bài kiểm thử (Tests)
```bash
dotnet test backend/NewsStudio.sln
```

---

## 🐳 Khởi chạy bằng Docker Compose

```bash
docker-compose -f docker/docker-compose.yml up --build
```
Hệ thống sẽ tự động khởi động:
- `newsstudio-postgres`: PostgreSQL 16 (Port 5432)
- `newsstudio-api`: API Service (Port 8080)
- `newsstudio-worker`: Worker Service với FFmpeg cài đặt sẵn

---

## ☁️ Triển khai lên Railway

1. Dự án đã cấu hình sẵn file [railway.json](file:///D:/Project/AutoGenVideoPostFB/railway.json).
2. Tạo 3 services trên Railway:
   - **Service 1**: PostgreSQL Database
   - **Service 2**: `news-api` trỏ vào `docker/Dockerfile.api`
   - **Service 3**: `news-worker` trỏ vào `docker/Dockerfile.worker`
3. Cấu hình biến môi trường theo mẫu [.env.example](file:///D:/Project/AutoGenVideoPostFB/.env.example).
