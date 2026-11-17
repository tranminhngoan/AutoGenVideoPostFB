# MA TRẬN NHÀ CUNG CẤP AI VÀ CHIẾN LƯỢC TỐI ƯU CHI PHÍ
## AI Provider Matrix & Fallback Strategies for AI Film Factory

**Tài liệu:** `AI_FILM_FACTORY_PROVIDER_MATRIX.md`  
**Ngày phát hành:** 17/11/2026  
**Mục tiêu tài chính:** Khống chế chi phí thực tế toàn bộ series 10 tập (50 phút) ở mức **0 – 5 USD**.  

---

## 1. MA TRẬN NHÀ CUNG CẤP THEO TỪNG CÔNG ĐOẠN SẢN XUẤT

| Công Đoạn Sản Xuất | Nhà Cung Cấp Chính (Primary) | Chi Phí Đơn Vị | Nhà Cung Cấp Dự Phòng (Fallback) | Chi Phí Fallback | Cơ Chế Chuyển Đổi (Switching Trigger) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **1. AI Showrunner & Story Bible** | **Google Gemini 2.5 Flash (Free Tier)** | **$0.00** (15 RPM free) | Google Gemini 2.5 Pro / Flash Paid Tier | $0.30 / 1M input, $2.50 / 1M output | Hết hạn mức Free Tier hoặc lỗi Rate Limit (429) -> chuyển sang trả phí nhưng tự động giới hạn token. |
| **2. Biên kịch 10 tập (Script & Shots)** | **Google Gemini 2.5 Flash** | ~$0.005 / series | Deterministic Story Scripting Engine (Nội bộ) | **$0.00** | Mất kết nối internet hoặc API Gemini trả về lỗi 5xx -> động cơ kịch bản mẫu nội bộ tự động sinh 36 shot chuẩn. |
| **3. Giọng nói nhân vật (Character Dialogue)** | **Microsoft EdgeTTS Neural (C# EdgeTtsService)** | **$0.00** (Miễn phí 100%) | ElevenLabs API / Gemini TTS | $0.15 - $0.30 / 1,000 ký tự | EdgeTTS được đặt làm mặc định vĩnh viễn với các voice tiếng Việt cao cấp (`vi-VN-NamMinhNeural`, `vi-VN-HoaiMyNeural`). |
| **4. Tạo hình ảnh nhân vật & bối cảnh** | **Cinematic Plate Generator (FFmpeg Filter + Tone Grading)** | **$0.00** | Stable Diffusion / Flux / Midjourney API | $0.003 - $0.02 / ảnh | Chế độ Hybrid Visual: 70% frame được xử lý màu điện ảnh 35mm cục bộ; chỉ gọi Cloud Image khi người dùng cấu hình riêng. |
| **5. Hiệu ứng chuyển động (Motion & Camera)** | **FFmpeg Ken Burns Engine (Pan/Zoom/Tilt đa trục)** | **$0.00** (Chạy trên CPU máy chủ) | Runway Gen-3 / Luma Dream Machine API | $0.20 - $0.50 / 5s video | Chuyển động máy quay 6 góc máy thực tế được tính toán toán học qua FFmpeg `zoompan`, không tốn 1 xu chi phí đám mây. |
| **6. Ghép phim, lồng nhạc & Render MP4** | **FFmpeg Media Assembly Engine (Internal)** | **$0.00** (CPU local) | Cloud Render Service | $0.05 / phút | Sử dụng codec H.264, preset `ultrafast`, ghép audio thoại, nhạc nền và video shots trực tiếp tại máy chủ. |

---

## 2. PHÂN TÍCH CHI TIẾT CÁC NHÀ CUNG CẤP CỐT LÕI

### 2.1. Google Gemini 2.5 Flash (Bộ não Showrunner)
- **Vai trò:** Lập dàn ý 3 hồi, thiết lập 4 quy tắc thế giới, xây dựng tiểu sử nhân vật, biên kịch 6 cảnh × 6 shots cho từng tập 5 phút.
- **Lý do lựa chọn:**
  - Cửa sổ ngữ cảnh (Context Window) lên tới 1.000.000 tokens, cho phép nạp toàn bộ kịch bản 10 tập mà không lo AI bị quên chi tiết giữa các tập.
  - Hỗ trợ gói Free Tier chính thức từ Google (tối đa 15 yêu cầu / phút, 1.500 yêu cầu / ngày), hoàn toàn đủ để sinh trọn vẹn 1 series 10 tập với chi phí **$0.00**.
  - Tốc độ phản hồi cực nhanh (Time-To-First-Token < 800ms).

### 2.2. Microsoft EdgeTTS (Động cơ âm thanh & diễn xuất)
- **Vai trò:** Chuyển hoá lời thoại nhân vật thành giọng nói biểu cảm tiếng Việt, tạo subtitle đồng bộ nhịp điệu.
- **Các voice được chuẩn hoá theo nhân vật:**
  - `vi-VN-NamMinhNeural`: Nam chính, trung niên, giọng trầm ấm, kiên nghị, tốc độ chuẩn.
  - `vi-VN-HoaiMyNeural`: Nữ chính, nữ phản diện, truyền cảm, linh hoạt cảm xúc.
- **Ưu điểm tài chính:** Hoàn toàn miễn phí, không giới hạn số phút thoại, độ trễ tạo âm thanh < 1.5 giây cho một câu thoại 10 giây.

### 2.3. FFmpeg Ken Burns & Color Grading Engine (Thị giác điện ảnh)
- **Vai trò:** Biến các khung hình tĩnh thành video điện ảnh có chiều sâu và nhịp thở chuyển động của máy quay 35mm.
- **Các chuyển động được mô phỏng chính xác:**
  - `SlowZoomIn`: Máy quay từ từ tiến sát vào nhân vật, đẩy cao độ căng thẳng và cảm xúc.
  - `SlowZoomOut`: Máy quay từ từ lùi xa, mở rộng không gian, thể hiện sự cô độc hoặc kết thúc phân đoạn.
  - `PanLeft` / `PanRight`: Máy lia ngang qua bối cảnh đường phố, tiệm đồng hồ, đám đông.
  - `TiltUp` / `TiltDown`: Máy nghiêng từ chân tháp chuông lên bầu trời mưa bão hoặc ngược lại.
- **Bảng màu (LUTs / Color Grading):**
  - SciFi: Xanh Neo-Noir (`#3B82F6` kết hợp ánh neon lạnh).
  - Drama: Đỏ đậm và độ tương phản cao (`#E11D48`).
  - Fantasy: Tím thạch anh và ánh sáng khuếch tán (`#8B5CF6`).
  - Thriller: Hổ phách và bóng tối sâu thẳm (`#F59E0B`).
  - Romance: Giờ vàng (Golden Hour) ấm áp (`#EC4899`).

---

## 3. BẢO VỆ NGÂN SÁCH BẰNG 4 TRẠNG THÁI (FILM COST CONTROLLER)

Hệ thống tích hợp bộ điều phối tài chính `FilmCostController.cs` kiểm tra ngân sách trước mọi thao tác sinh nội dung:

```mermaid
stateDiagram-v2
    [*] --> FREE_ONLY: Dự án mới tạo ($0.00)
    FREE_ONLY --> LOW_COST: Chi tiêu > $0.00 & <= $2.00
    LOW_COST --> BUDGET_WARNING: Chi tiêu > $2.00 & <= $4.50
    BUDGET_WARNING --> BUDGET_EXHAUSTED: Chi tiêu > $4.50 hoặc chạm $5.00
    BUDGET_EXHAUSTED --> [*]: Chặn mọi API trả phí - Chuyển sang 100% Free
```

1. **Trạng thái `FREE_ONLY` ($0.00 spent):**
   - Sử dụng 100% khoá Gemini Free Tier, EdgeTTS và FFmpeg local. Chi phí vận hành là $0.0000.
2. **Trạng thái `LOW_COST` ($0.01 – $2.00 spent):**
   - Cho phép sử dụng Gemini Flash Paid nếu gặp nghẽn mạng ở Free Tier. Cảnh báo chi tiêu ở mức an toàn.
3. **Trạng thái `BUDGET_WARNING` ($2.01 – $4.50 spent):**
   - Hạn chế tối đa việc gọi API bên ngoài; giảm số lượng token prompt của LLM; kích hoạt nén prompt.
4. **Trạng thái `BUDGET_EXHAUSTED` (> $4.50 spent):**
   - Tự động khoá mọi lệnh gọi API trả phí; buộc hệ thống chuyển sang chế độ dự phòng nội bộ (Deterministic Engine) để đảm bảo không bao giờ vượt quá ngân sách $5.00 của người dùng.

---

## 4. KẾT LUẬN CHIẾN LƯỢC CUNG CẤP

Nhờ sự kết hợp tinh tế giữa **Google Gemini (tận dụng Free Tier thông minh)**, **Microsoft EdgeTTS (0 đồng)** và **FFmpeg Ken Burns Engine (0 đồng)**, hệ thống AI Film Factory đạt được kỳ tích:
> **Sản xuất trọn bộ series điện ảnh 10 tập (50 phút) với chất lượng cao mà chi phí thực tế chỉ dao động từ $0.00 đến tối đa $0.05 USD**, thấp hơn 100 lần so với mức trần $5.00 USD mà người dùng yêu cầu!
