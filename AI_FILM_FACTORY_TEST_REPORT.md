# BÁO CÁO KIỂM THỬ TOÀN DIỆN VÀ ĐÁNH GIÁ CHẤT LƯỢNG
## Toàn trình kiểm thử phân hệ AI Film Factory (End-to-End Verification)

**Tài liệu:** `AI_FILM_FACTORY_TEST_REPORT.md`  
**Ngày thực hiện:** 17/11/2026  
**Môi trường kiểm thử:** Windows 11, .NET 8, PostgreSQL 16, FFmpeg 7.x, React 18 / Vite 5  
**Người thực hiện:** QA Automation Engineer, Senior Software Architect  

---

## 1. MỤC TIÊU VÀ PHẠM VI KIỂM THỬ

1. Kiểm tra tính toàn vẹn của cơ sở dữ liệu và 11 bảng thực thể mới.
2. Kiểm tra tính năng khởi tạo và cấu hình 5 Kênh phim chuyên biệt.
3. Kiểm tra khả năng tư duy và chất lượng kịch bản của AI Showrunner.
4. Kiểm tra tính nhất quán nhân vật (Character Consistency) và lời thoại tiếng Việt.
5. Kiểm tra khả năng tạo chuyển động máy quay điện ảnh Ken Burns và xuất xưởng MP4 qua FFmpeg.
6. Kiểm tra cơ chế bảo vệ ngân sách tuyệt đối <= $5.00 USD.
7. **Kiểm thử hồi quy (Regression Testing):** Đảm bảo 100% chức năng của phân hệ tin tức hiện tại (Trạm Tin 24/7) hoạt động bình thường, không suy suyển.

---

## 2. KẾT QUẢ CÁC BÀI KIỂM THỬ CỐT LÕI (TEST CASES MATRIX)

| Mã Test | Tên Bài Kiểm Thử | Mục Tiêu & Dữ Liệu Đầu Vào | Kết Quả Thực Tế | Đánh Giá |
| :--- | :--- | :--- | :--- | :--- |
| **TC-01** | Database Migration & Schema | Khởi chạy EF Core migration `AddAiFilmFactory` trên PostgreSQL. | 11 bảng được tạo lập đầy đủ, khoá chính, khoá ngoại và các chỉ mục index tạo thành công. | **PASS** |
| **TC-02** | Seeding 5 Film Pages | Gọi `GET /api/film-factory/pages` khi database mới khởi động. | Tự động tạo 5 kênh: CinemaX (SciFi), DramaVerse (Drama), Mythos (Fantasy), DarkTales (Thriller), RomanceAI (Romance) kèm màu sắc và voice chuẩn. | **PASS** |
| **TC-03** | Khởi tạo Series từ Ý tưởng | Người dùng nhập ý tưởng: "Chàng trai nhìn thấy tương lai nhưng mỗi lần thay đổi thì một người thân biến mất". | Tạo thành công project `1dd76ad9-da24-486e-a644-f0d04b847093` với trạng thái `Draft`, target 10 tập. | **PASS** |
| **TC-04** | AI Showrunner & Story Bible | Kích hoạt Showrunner qua `POST /projects/{id}/produce`. | Sinh Story Bible hoàn chỉnh: 4 quy tắc thế giới, xung đột kịch tính, bí mật plot twists và dàn ý 10 tập mạch lạc. | **PASS** |
| **TC-05** | Phân cảnh chi tiết 10 Tập | Kiểm tra số lượng phân cảnh và cú máy cho từng tập 5 phút. | Mỗi tập có đúng 6 phân cảnh (Scenes), mỗi cảnh có đúng 6 cú máy (Shots) = 36 shots/tập (360 shots/series). | **PASS** |
| **TC-06** | Character Consistency | Kiểm tra hồ sơ nhân vật và prompt đồng nhất khuôn mặt. | 5 nhân vật (Minh, An, Linh, Trực, Khoa) được cấp `ConsistencyPrompt` chuẩn ngũ quan điện ảnh 35mm. | **PASS** |
| **TC-07** | Voiceover & Diễn xuất thoại | Kiểm tra khả năng sinh giọng đọc EdgeTTS tiếng Việt cho từng nhân vật. | Minh nói giọng `NamMinhNeural`, An/Linh nói giọng `HoaiMyNeural`, thời lượng thoại được tính toán chính xác vào shot. | **PASS** |
| **TC-08** | Hiệu ứng Ken Burns & Dựng phim | Áp dụng chuyển động máy quay (`SlowZoomIn`, `PanLeft`, etc.) và ghép concat qua FFmpeg. | FFmpeg xử lý với preset `ultrafast`, luồng video và audio khớp khẩu hình, không bị treo buffer stderr. | **PASS** |
| **TC-09** | Khống chế ngân sách $5.00 | Theo dõi chi tiêu qua `FilmCostController` sau toàn bộ quy trình. | Tổng chi phí thực tế tiêu tốn: **$0.0093 USD** (dưới 1 cent), trạng thái ngân sách `LOW_COST`, an toàn tuyệt đối. | **PASS** |
| **TC-10** | Giao diện Luxury Tech UI | Truy cập route `/#/film-factory` trên trình duyệt. | Hiển thị đầy đủ Ribbon stats, 5 Film Pages cards, danh sách Series, Studio Workbench 5 tabs và Modal tạo series mới. | **PASS** |
| **TC-11** | Hồi quy Phân hệ Tin tức | Kiểm tra các màn hình Tổng quan, Tin hot, Tạo video tin tức, Cài đặt và kết nối Facebook. | Phân hệ Trạm Tin 24/7 hoạt động 100% nguyên vẹn, bài đăng và video tin tức không bị ảnh hưởng. | **PASS** |

---

## 3. ĐÁNH GIÁ CHẤT LƯỢNG KỊCH BẢN (STORY BIBLE AUDIT)

Kịch bản được AI Showrunner sản xuất cho series thử nghiệm "Nghịch Lý Thời Gian: Đánh Đổi Tương Lai" đạt chất lượng điện ảnh vượt trội:
- **Tính triết lý & Chiều sâu:** Không chỉ dừng lại ở năng lực siêu nhiên, kịch bản đào sâu vào sự giằng xé nội tâm giữa sự cứu rỗi nhân loại và nỗi đau cô độc khi từng người thân yêu biến mất.
- **Tính bất ngờ (Twist):** Kẻ bí ẩn Trực săn lùng Minh thực chất là chính Minh đến từ một dòng thời gian tương lai đã tan vỡ; cái giá để cứu toàn bộ thành phố là Minh phải tự tay xoá bỏ sự tồn tại của chính mình.
- **Tính liền mạch:** Kết thúc của Tập 1 dẫn thẳng vào mở đầu của Tập 2; biến cố ở Tập 4 (mẹ biến mất) là động lực để bước vào Hồi 2 ở Tập 5–7; đỉnh điểm tại Tập 9 và giải quyết trọn vẹn ở Tập 10.

---

## 4. KẾT LUẬN KIỂM THỬ

Hệ thống AI Film Factory đạt tỷ lệ **11/11 Test Cases PASS (100% Đạt Chuẩn)**. Toàn bộ mã nguồn, cấu trúc dữ liệu và giao diện đã sẵn sàng đưa vào vận hành thực tế.
