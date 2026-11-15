# AI NEWS STUDIO - HƯỚNG DẪN TRIỂN KHAI RAILWAY (DEPLOYMENT GUIDE)

Tài liệu hướng dẫn triển khai toàn diện hệ thống AI News Studio lên nền tảng đám mây **Railway** theo kiến trúc Microservices phân tách: **PostgreSQL**, **news-api**, **news-worker** và **Frontend Web**.

---

## 1. Mô hình Kiến trúc Triển khai trên Railway

```
                                 [ Người dùng / Ban biên tập ]
                                               │
                                               ▼
                              ┌───────────────────────────────────┐
                              │     ReactJS + Vite Frontend       │
                              │ (Railway Web / Static Hosting)    │
                              └─────────────────┬─────────────────┘
                                                │ HTTPS / REST API
                                                ▼
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                                   RAILWAY PROJECT                                      │
│                                                                                        │
│  ┌───────────────────────────────┐                  ┌───────────────────────────────┐  │
│  │     Service: news-api         │                  │    Service: news-worker       │  │
│  │  - ASP.NET Core 8 Web API     │                  │  - .NET 8 Worker Service      │  │
│  │  - Swagger & Healthchecks     │                  │  - FFmpeg 9:16 Video Engine   │  │
│  │  - Rate Limiting & Auth Guard │                  │  - Edge-TTS / ElevenLabs      │  │
│  │  - Automatic DB Migration     │                  │  - Hangfire Background Server │  │
│  │  - Dockerfile.api             │                  │  - Dockerfile.worker          │  │
│  └──────────────┬────────────────┘                  └───────────────┬───────────────┘  │
│                 │                                                   │                  │
│                 │               ┌───────────────────┐               │                  │
│                 └──────────────►│    PostgreSQL     │◄──────────────┘                  │
│                                 │ (Railway Managed) │                                  │
│                                 │ Schema + Hangfire │                                  │
│                                 └───────────────────┘                                  │
│                                                                                        │
│                                Ổ đĩa tạm (Ephemeral)                                   │
│                        /tmp/newsstudio (Zero long-term media)                          │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Chuẩn bị trước khi triển khai

1. **Tài khoản Railway**: Đăng ký và kết nối tài khoản GitHub tại [railway.app](https://railway.app).
2. **Khóa API bên ngoài**:
   - Google Gemini API Key: Tạo tại [Google AI Studio](https://aistudio.google.com/).
   - (Tùy chọn) Anthropic Claude API Key: Tạo tại [Anthropic Console](https://console.anthropic.com/).
   - (Tùy chọn) ElevenLabs API Key: Nếu sử dụng giọng đọc ngoài EdgeTTS.
   - Meta App & Facebook Fanpage: Tạo Meta App trên [developers.facebook.com](https://developers.facebook.com) và lấy Page Access Token dài hạn (`pages_manage_posts`, `pages_read_engagement`).

---

## 3. Các bước triển khai chi tiết trên Railway

### Bước 1: Khởi tạo Project & Database PostgreSQL trên Railway
1. Trên giao diện Railway Dashboard, nhấn **New Project** → chọn **Provision PostgreSQL**.
2. Railway sẽ tự động tạo một database PostgreSQL phiên bản 16 và cấp phát biến môi trường nội bộ:
   - `DATABASE_URL` (dạng `postgresql://postgres:password@host:port/railway`)
   - `PGHOST`, `PGPORT`, `PGUSER`, `PGPASSWORD`, `PGDATABASE`.

---

### Bước 2: Triển khai Service `news-api`
1. Nhấn **+ New Service** → chọn **GitHub Repo** → chọn repository `AutoGenVideoPostFB`.
2. Đổi tên service thành: `news-api`.
3. Vào tab **Settings**:
   - **Build**: Chọn `Dockerfile`.
   - **Dockerfile Path**: Điền `docker/Dockerfile.api`.
   - **Deploy**:
     - **Healthcheck Path**: `/health`
     - **Healthcheck Timeout**: `30`
     - **Restart Policy**: `ON_FAILURE` (Max retries: 5).
4. Vào tab **Variables** và cấu hình:
   ```env
   ASPNETCORE_ENVIRONMENT=Production
   PORT=8080
   DATABASE_URL=${{Postgres.DATABASE_URL}}
   Database__AutoMigrate=true
   AI_PROVIDER=Gemini
   AI_API_KEY=AIzaSy_YOUR_GEMINI_KEY_HERE
   Ai__Provider=Gemini
   Ai__ApiKey=AIzaSy_YOUR_GEMINI_KEY_HERE
   TTS_PROVIDER=EdgeTts
   Tts__Provider=EdgeTts
   Facebook__EncryptionSecretKey=vN8xL9pQ2wE4rT7yU1iO3aS6dF8gH0jK
   Storage__TempStoragePath=/tmp/newsstudio
   Storage__MaxTempStorageMb=5120
   Storage__TempFileTtlMinutes=120
   FRONTEND_URL=${{frontend.RAILWAY_STATIC_URL}}
   ```
5. Vào tab **Settings** → **Networking** → nhấn **Generate Domain** để nhận domain công khai (ví dụ: `https://news-api-production.up.railway.app`).

---

### Bước 3: Triển khai Service `news-worker`
1. Nhấn **+ New Service** → chọn **GitHub Repo** → chọn repository `AutoGenVideoPostFB`.
2. Đổi tên service thành: `news-worker`.
3. Vào tab **Settings**:
   - **Build**: Chọn `Dockerfile`.
   - **Dockerfile Path**: Điền `docker/Dockerfile.worker`.
   - **Deploy**:
     - **Restart Policy**: `ON_FAILURE` (Max retries: 5).
4. Vào tab **Variables** và cấu hình:
   ```env
   DOTNET_ENVIRONMENT=Production
   DATABASE_URL=${{Postgres.DATABASE_URL}}
   AI_PROVIDER=Gemini
   AI_API_KEY=AIzaSy_YOUR_GEMINI_KEY_HERE
   Ai__Provider=Gemini
   Ai__ApiKey=AIzaSy_YOUR_GEMINI_KEY_HERE
   TTS_PROVIDER=EdgeTts
   Tts__Provider=EdgeTts
   Facebook__EncryptionSecretKey=vN8xL9pQ2wE4rT7yU1iO3aS6dF8gH0jK
   Storage__TempStoragePath=/tmp/newsstudio
   Storage__MaxConcurrentRenderJobs=2
   Storage__MaxTempStorageMb=5120
   Storage__TempFileTtlMinutes=120
   Storage__CleanupIntervalMinutes=15
   ```
5. **Cấp phát Volume cho thư mục tạm**:
   - Vào tab **Settings** → **Volumes** → nhấn **Add Volume**.
   - Mount Path: `/tmp/newsstudio`.

---

### Bước 4: Triển khai Giao diện Người dùng (Frontend)
1. Thư mục `frontend` là ứng dụng SPA React 18 + Vite + Tailwind CSS.
2. Trên Railway: Nhấn **+ New Service** → chọn GitHub repo:
   - Root Directory: `frontend`
   - Build Command: `npm run build`
   - Start Command: `npx serve -s dist -l $PORT` (hoặc cấu hình Nginx).
3. Biến môi trường:
   ```env
   VITE_API_BASE_URL=https://news-api-production.up.railway.app
   ```
4. Generate domain công khai cho Frontend.

---

## 4. Kiểm tra và Xác thực sau khi Triển khai

1. **Kiểm tra API Healthcheck**:
   ```bash
   curl -i https://news-api-production.up.railway.app/health
   # Kỳ vọng: HTTP/1.1 200 OK, Healthy
   ```
2. **Kiểm tra Swagger Documentation**:
   Truy cập: `https://news-api-production.up.railway.app/swagger`
3. **Kiểm tra Hangfire Dashboard**:
   Truy cập: `https://news-api-production.up.railway.app/hangfire` (Xem số lượng Worker hoạt động, queue `default`, `render`).
4. **Kiểm tra Production Telemetry**:
   ```bash
   curl -s https://news-api-production.up.railway.app/api/system/metrics | jq .
   ```
