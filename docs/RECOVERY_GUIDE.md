# AI NEWS STUDIO - SỔ TAY XỬ LÝ SỰ CỐ VÀ PHỤC HỒI (RECOVERY GUIDE)

Tài liệu quy trình chuẩn (SOP) để chẩn đoán, khắc phục sự cố và phục hồi hệ thống AI News Studio khi vận hành trên môi trường Production.

---

## 1. Nguyên tắc An toàn Cốt lõi (Safety Guarantees)

1. **Tuyệt đối không đăng trùng lặp (Zero Duplicate Publish)**:
   - Dù Worker hoặc API bị crash/restart giữa chừng trong bước `Publishing`, hệ thống kiểm tra bảng `FacebookPosts`. Nếu đã tồn tại bài đăng với `UploadStatus == "Published"`, hệ thống lập tức hòa giải trạng thái về `Published`, **tuyệt đối không gọi Meta Graph API lần thứ hai**.
2. **Không lưu trữ file MP4 lâu dài (Ephemeral Storage Guarantee)**:
   - File nhị phân chỉ là tài nguyên tạm thời. Khi container khởi động lại, thư mục tạm `/tmp/newsstudio` được làm mới. Nếu tệp video bị mất trong quá trình dựng, dự án sẽ được chuyển về trạng thái `Failed` hoặc `Pending` kèm thông báo rõ ràng để người dùng bấm dựng lại, tránh tình trạng treo job vĩnh viễn.

---

## 2. Các Kịch bản Sự cố và Quy trình Xử lý (Incident Playbooks)

### Kịch bản 1: Worker bị Crash hoặc Khởi động lại khi đang Dựng Video (Rendering / Audio)
- **Hiện tượng**: Job đang ở trạng thái `Rendering` hoặc `GeneratingAudio` thì container bị Restart do thiếu RAM (OOM) hoặc cập nhật deployment.
- **Cơ chế tự động**:
  - Khi Worker khởi động lại, `IJobRecoveryService` tự động chạy quét:
    - Nếu file `FinalVideo.mp4` vẫn còn nguyên vẹn trên volume đĩa tạm: Chuyển trạng thái sang `AwaitingApproval` để sẵn sàng duyệt.
    - Nếu file bị mất do đĩa tạm bị xóa: Đánh dấu trạng thái `Failed` với ghi chú: *"Quá trình dựng video bị ngắt quãng do Worker khởi động lại. Tệp tin tạm đã được làm mới; vui lòng bấm Tạo lại video."*
- **Hành động thủ công (nếu cần)**:
  - Vào màn hình **Chi tiết Video** → Bấm **Yêu cầu làm lại** → Chọn **Render lại video**.

---

### Kịch bản 2: Sự cố Mất Kết nối Cơ sở Dữ liệu PostgreSQL
- **Hiện tượng**: API trả về HTTP 503 hoặc healthcheck `/health` báo `Unhealthy` do mạng Railway chập chờn.
- **Cơ chế tự động**:
  - EF Core được cấu hình `EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: 5s)`.
  - Npgsql kết nối lại tự động với keep-alive 30s.
- **Hành động khắc phục**:
  - Kiểm tra trạng thái service `Postgres` trên Railway Dashboard.
  - Nếu database bị đầy RAM hoặc disk, nâng cấp dung lượng trên Railway và khởi động lại container PostgreSQL.

---

### Kịch bản 3: Token Facebook Page Hết hạn hoặc Bị Thu hồi Quyền
- **Hiện tượng**: Xuất bản Reels báo lỗi: `Invalid OAuth 2.0 Access Token` hoặc mã lỗi `190`.
- **Quy trình xử lý**:
  1. Truy cập [Meta Graph API Explorer](https://developers.facebook.com/tools/explorer/).
  2. Chọn App của bạn → Chọn Fanpage đích → Cấp các quyền: `pages_manage_posts`, `pages_read_engagement`, `publish_video`.
  3. Sinh **Page Access Token** dài hạn.
  4. Mở giao diện Admin Studio → Menu **Cấu hình Facebook** → Bấm **Kết nối Fanpage** và dán token mới.
  5. Hệ thống tự động mã hóa AES-256 và cập nhật vào PostgreSQL.
  6. Mở lại dự án video và thực hiện xuất bản.

---

### Kịch bản 4: Trí tuệ Nhân tạo (Gemini / Claude) Bị Quá Tải hoặc Timeout
- **Hiện tượng**: Bước biên tập kịch bản quay vòng quá 60 giây và báo timeout.
- **Cơ chế tự động**:
  - Bộ điều phối AI tích hợp engine phân tích nội dung dự phòng (Deterministic Fallback Engine) để đảm bảo không làm gián đoạn luồng sản xuất ngay cả khi API AI bên ngoài bị nghẽn mạng.
- **Hành động khắc phục**:
  - Vào màn hình **Cấu hình AI**:
    - Chuyển đổi nhà cung cấp từ Gemini sang Claude (hoặc ngược lại).
    - Kiểm tra hạn mức (Quota / Billing) của tài khoản Google Cloud / Anthropic.

---

### Kịch bản 5: Dung lượng Ổ đĩa Tạm Bị Đầy (Temp Storage Quota Exceeded)
- **Hiện tượng**: Báo động `TempStorageUsagePercent > 90%` hoặc FFmpeg báo lỗi `No space left on device`.
- **Cơ chế tự động**:
  - Background Service `StorageCleanupService` chạy định kỳ 15 phút sẽ tự động xóa các asset có `ExpiresAtUtc < DateTime.UtcNow`.
  - Nếu vẫn vượt quá trần `Storage:MaxTempStorageMb` (5120 MB), thuật toán tự động xóa tệp cũ nhất cho đến khi dung lượng hạ xuống dưới 80% hạn ngạch.
- **Hành động khẩn cấp**:
  - Gọi API dọn dẹp tức thì:
    ```bash
    curl -X POST https://news-api-production.up.railway.app/api/system/storage/cleanup
    ```
  - Hoặc bấm nút **Dọn dẹp đĩa** trực tiếp trên Dashboard Studio.

---

## 3. Quy trình Sao lưu và Phục hồi PostgreSQL (Backup & Disaster Recovery)

### A. Sao lưu Cơ sở dữ liệu (Database Backup)
Thực thi lệnh sao lưu từ máy quản trị hoặc terminal có kết nối tới PostgreSQL:

```bash
# Định dạng nén tùy biến (-Fc) an toàn nhất cho PostgreSQL
pg_dump -h <HOST_RAILWAY> -p <PORT> -U postgres -d newsstudio_db -Fc -f newsstudio_backup_$(date +%Y%m%d_%H%M%S).dump
```

*Ví dụ dùng chuỗi kết nối URI:*
```bash
pg_dump "$DATABASE_URL" -Fc -f newsstudio_backup_latest.dump
```

### B. Phục hồi Cơ sở dữ liệu (Database Restore)
Khi xảy ra sự cố mất dữ liệu hoặc cần chuyển sang cụm cơ sở dữ liệu mới:

```bash
# Phục hồi dữ liệu từ file dump
pg_restore -h <HOST_RAILWAY> -p <PORT> -U postgres -d newsstudio_db --clean --if-exists newsstudio_backup_latest.dump
```

*Lưu ý:* Bảng `VideoAssets` chỉ chứa siêu dữ liệu và đường dẫn tệp tạm. Sau khi restore, hệ thống vẫn hoạt động bình thường, các video cũ đã xuất bản lên Facebook vẫn giữ nguyên liên kết và chỉ số Insights từ Meta Graph API.
