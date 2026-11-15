# AI NEWS STUDIO - NHẬT KÝ TRIỂN KHAI (IMPLEMENTATION LOG)

## Phase 3: Article Extractor và Image Extractor

**Thời gian thực hiện:** 15/11/2026

### 1. Công việc đã thực hiện
1. **Bổ sung thư viện bóc tách HTML**:
   - Tích hợp `HtmlAgilityPack` (1.11.71) vào `NewsStudio.Infrastructure`.
2. **Xây dựng DTOs & Interfaces**:
   - `NewsStudio.Application/DTOs/ArticleDtos.cs`: `ExtractArticleRequestDto`, `ArticleResponseDto`, `ArticleImageResponseDto`, `UpdateArticleImageDto`.
   - `NewsStudio.Application/Interfaces/IArticleServices.cs`: Định nghĩa `IArticleExtractorService`, `IArticleService`, cấu trúc trung gian `ExtractedArticleRaw`, `ExtractedImageRaw`.
   - `NewsStudio.Application/Interfaces/IImageClassifierService.cs`: Định nghĩa dịch vụ phân loại ảnh tự động.
3. **Phát triển Adapter bóc tách bài báo (`IWebsiteAdapter`)**:
   - `GenericArticleAdapter`: Phân tích JSON-LD `NewsArticle`, OpenGraph (`og:title`, `og:description`, `og:image`), semantic HTML (`<article>`, `<figure>`, `<figcaption>`). Tự động xử lý fallback title/author/description/body, loại bỏ quảng cáo (`/ads/`, banners), pixel theo dõi, icon SVG.
   - `VnExpressAdapter`: Tối ưu hóa bóc tách cho `vnexpress.net` (`h1.title-detail`, `p.description`, đoạn văn `p.Normal` trong `article.fck_detail`, khối ảnh `figure.tplCaption` kèm chú thích `figcaption p.Image`).
   - `TuoiTreAdapter`: Tối ưu hóa bóc tách cho `tuoitre.vn` (`h1.article-title`, `h2.article-sapo`, khối ảnh `div.VCSortable` / `PhotoCMS_Caption`).
   - `ThanhNienAdapter` & `DanTriAdapter`: Hỗ trợ chuyên sâu cấu trúc bài viết của báo Thanh Niên và Dân Trí.
4. **Phát triển dịch vụ trích xuất & chống trùng lặp (`ArticleExtractorService`)**:
   - Tự động phát hiện domain và ủy quyền adapter phù hợp.
   - Băm SHA-256 nội dung/URL ảnh để triệt tiêu trùng lặp và đánh thứ tự rõ ràng (`Order`).
   - Giữ nguyên vẹn thứ tự xuất hiện ảnh trong bài viết.
5. **Dịch vụ phân loại hình ảnh (`ImageClassifierService`)**:
   - Phân tích ngữ cảnh nội dung bài và caption để phân loại thành 6 nhóm: `Panorama`, `Character`, `Event`, `Document`, `Chart`, `Unsuitable`.
   - Sử dụng regex phân tách token chính xác, không tự suy đoán danh tính nhân vật nếu không có căn cứ.
6. **Dịch vụ lưu trữ nghiệp vụ (`ArticleService`)**:
   - Kiểm tra trùng lặp URL trước khi bóc tách mới.
   - Lưu trữ toàn bộ siêu dữ liệu bài báo và hình ảnh vào PostgreSQL.
   - **Thực thi nghiêm ngặt quy tắc bản quyền**: Mọi hình ảnh trích xuất luôn mặc định `RightsStatus = Unknown`, tuyệt đối không tự nâng cấp thành `Authorized`.
7. **Tích hợp vào Pipeline Worker (`VideoProcessingJobExecutor`)**:
   - Trong bước `Extracting`, nếu dự án chưa liên kết bài báo (`ArticleId == null`), worker tự động bóc tách bài báo từ `SourceUrl`, lưu vào DB và gắn `ArticleId`.
8. **Triển khai REST API (`ArticlesController`)**:
   - `POST /api/articles/extract`: Tiếp nhận URL, kiểm tra hợp lệ, tải và trích xuất bài báo + ảnh.
   - `GET /api/articles`: Lấy danh sách bài báo đã trích xuất phân trang.
   - `GET /api/articles/{id}`: Xem chi tiết bài báo kèm danh sách toàn bộ ảnh và metadata.
   - `PUT /api/articles/{id}/images/{imageId}`: Cập nhật caption, phân loại ảnh, trạng thái bản quyền (RightsStatus) và cờ chọn dùng cho video.
9. **Cập nhật giao diện Frontend (ReactJS + TS)**:
   - Cập nhật `frontend/src/services/api.ts` với đầy đủ API bài báo và cập nhật ảnh.
   - Nâng cấp màn hình `ArticlesPage.tsx`: Form dán link trích xuất bài báo trực tiếp, danh sách bài báo đã crawl, modal xem chi tiết nội dung, modal chỉnh sửa caption và phân quyền ảnh, thông báo cảnh báo bản quyền trực quan.
10. **Kiểm thử tự động**:
    - Xây dựng `Phase3ArticleAndImageExtractorTests.cs` (10 ca kiểm thử bao phủ bóc tách VnExpress, Tuổi Trẻ, Generic, lọc quảng cáo, xử lý bài không có ảnh, phân loại ảnh, lưu DB quyền Unknown, và các endpoint API).
    - Toàn bộ 23/23 tests xUnit đạt 100% PASS.

---

## Phase 2: Database, Hangfire và Job Processing

**Thời gian thực hiện:** 15/11/2026

*(Xem chi tiết trong tài liệu nghiệm thu Phase 2)*

---

## Phase 1: Khởi tạo dự án và kiến trúc

**Thời gian thực hiện:** 15/11/2026

*(Xem chi tiết trong lịch sử git commit ban đầu)*
