# AI NEWS STUDIO - SỔ TAY VẬN HÀNH HỆ THỐNG (OPERATIONS GUIDE)

Tài liệu hướng dẫn dành cho Quản trị viên (DevOps / SysAdmin) và Ban biên tập trong quá trình vận hành hệ thống AI News Studio trên môi trường Production.

---

## 1. Quy trình Kiểm tra Đầu ngày (Daily Operations Checklist)

Mỗi ngày làm việc, quản trị viên thực hiện 4 bước kiểm tra nhanh sau:

1. **Kiểm tra trạng thái Service & Health Probes**:
   - Truy cập: `https://news-api-production.up.railway.app/health` → Đảm bảo trạng thái `Healthy`.
   - Kiểm tra endpoint telemetry: `GET /api/system/metrics`.

2. **Kiểm tra Hàng đợi Tác vụ Hangfire Dashboard**:
   - Truy cập: `https://news-api-production.up.railway.app/hangfire`
   - Kiểm tra mục **Servers**: Worker Service phải đang hoạt động (`Heartbeat < 1m`).
   - Kiểm tra mục **Failed Jobs**: Nếu có job bị lỗi (do bài báo quá dài hoặc mạng chập chờn), xem xét nguyên nhân và bấm **Retry**.

3. **Kiểm tra Dung lượng Ổ đĩa Tạm (Ephemeral Storage)**:
   - Trên Dashboard Studio hoặc gọi API `GET /api/system/metrics`.
   - Đảm bảo `TempStorageUsagePercent < 70%`.
   - Nếu dung lượng tiệm cận 80%, quản trị viên có thể bấm **Dọn dẹp đĩa** trên giao diện Dashboard hoặc gọi `POST /api/system/storage/cleanup`.

4. **Kiểm tra Chi phí AI & TTS Tích lũy**:
   - Xem mục **Chi phí AI & TTS** trên Dashboard hoặc gọi `GET /api/system/costs`.
   - Theo dõi chi phí trung bình mỗi video (chuẩn mục tiêu: `< $0.005 / video` khi dùng Gemini 1.5 Flash + EdgeTTS).

---

## 2. Giám sát & Quản trị Chi phí (Cost Monitoring & Optimization)

### A. Chi phí AI (Biên tập Kịch bản)
| Nhà cung cấp | Model | Giá Input / 1M tokens | Giá Output / 1M tokens | Chi phí trung bình / video | Khuyến nghị |
|---|---|---|---|---|:---:|
| **Google Gemini** | `gemini-1.5-flash` | $0.075 | $0.30 | **~$0.0003** | **Khuyên dùng (Tối ưu nhất)** |
| **Google Gemini** | `gemini-1.5-pro` | $3.50 | $10.50 | ~$0.0075 | Dùng khi cần phân tích chuyên sâu |
| **Anthropic** | `claude-3-5-haiku` | $0.80 | $4.00 | ~$0.0028 | Thay thế chất lượng cao |
| **Anthropic** | `claude-3-5-sonnet`| $3.00 | $15.00 | ~$0.0120 | Chất lượng cao nhất |

### B. Chi phí Giọng đọc Tiếng Việt (TTS)
- **Microsoft EdgeTTS** (Giọng `vi-VN-HoaiMyNeural`, `vi-VN-NamMinhNeural`): **Hoàn toàn Miễn phí ($0.00)**, độ tự nhiên và phát âm chuẩn tiếng Việt rất cao.
- **ElevenLabs**: ~$0.15 / 1.000 ký tự (~$0.10 / video). Chỉ nên kích hoạt khi người dùng yêu cầu giọng thương hiệu riêng.

### C. Chi phí Tài nguyên Railway (Billing Optimization)
- **PostgreSQL Service**: Gói cơ bản (1GB RAM, 1 vCPU, 10GB Disk) ~ $5 - $7/tháng.
- **news-api Service**: 512MB RAM, 1 vCPU ~ $3 - $5/tháng.
- **news-worker Service**: 1GB RAM, 2 vCPU ~ $7 - $10/tháng (cần RAM cho FFmpeg encoding 1080×1920).
- **Tổng chi phí hạ tầng ước tính**: **~$15 - $22/tháng** cho toàn bộ hệ thống xử lý hàng trăm video mỗi tháng.

---

## 3. Quản lý Token & Tài khoản Facebook Page

1. **Thời hạn Token**:
   - Meta User Token ngắn hạn: 1–2 giờ.
   - Long-Lived Page Access Token: Thường có giá trị vĩnh viễn (hoặc 60 ngày nếu người dùng đổi mật khẩu Facebook).
2. **Kiểm tra & Gia hạn Token**:
   - Truy cập màn hình **Cấu hình Facebook** trên Admin Studio.
   - Bấm **Kiểm tra với Meta** để rà soát tính hợp lệ và danh sách quyền hạn.
   - Nếu token hết hạn hoặc bị thu hồi quyền, tạo Access Token mới trên Meta Developer Graph API Explorer và nhập lại. Hệ thống sẽ tự động mã hóa AES-256 trước khi lưu.

---

## 4. Bảo mật & Giới hạn Tần suất (Rate Limiting)

- Hệ thống trang bị ASP.NET Core RateLimiter:
  - Giới hạn toàn cầu: 120 yêu cầu / phút trên mỗi địa chỉ IP.
  - Vượt ngưỡng sẽ trả về HTTP `429 Too Many Requests`.
- Nếu cấu hình biến môi trường `Security__AdminApiKey`, tất cả các yêu cầu quản trị bắt buộc phải gửi kèm header:
  ```http
  X-Admin-Api-Key: your-secret-admin-key
  ```
