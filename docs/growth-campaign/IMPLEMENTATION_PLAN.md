# Meta Growth Campaign — Implementation Plan

## Nguyên tắc

- Module **độc lập về nghiệp vụ**: bảng, service, controller, trang riêng (tiền tố `Growth`).
- **Tái sử dụng, không sửa**: `IFacebookPublishingService` (Page, token, đăng Reels/ảnh/Story video, follower, số liệu Reels), `StudioService.RunPublishAsync` (đăng video Studio Video AI), `StudioFileStore` (lưu file), `FilmGeminiClient` + ngân sách AI của studio, `StudioAccess` (phân quyền), `PageFollowerSnapshots`.
- Thứ cũ chỉ bị chạm ở **điểm đăng ký**: DbContext (DbSet + cấu hình), DI, recurring jobs, `App.tsx` (1 route), `Layout.tsx` (1 nhóm menu).
- Không số liệu giả: giá trị `null` = "Chưa có dữ liệu" kèm lý do từ Meta.

## Kiến trúc backend

```
Domain/Entities/GrowthEntities.cs
Application/DTOs/GrowthDtos.cs
Infrastructure/Growth/
  GrowthTime.cs              — múi giờ Asia/Ho_Chi_Minh, ngày VN, khung giờ
  GrowthGraphClient.cs       — adapter Graph API MỚI (chỉ những gì module cũ chưa có): photo_stories, bài chữ,
                               Page Insights dò từng metric, số liệu bài ảnh; token lấy từ FacebookAccounts (giải mã
                               bằng IFacebookTokenProtectionService), không log URL chứa token
  GrowthCampaignService.cs   — CRUD chiến dịch, vòng đời (start/pause/resume/complete/cancel), kế hoạch 7 ngày,
                               checklist Meta One, số liệu nhập tay
  GrowthContentService.cs    — lịch đăng (CRUD, duyệt, huỷ, đăng ngay, đánh dấu đã đăng thủ công), asset upload
  GrowthPublisher.cs         — job mỗi phút: claim nguyên tử (UPDATE … WHERE Status='Scheduled'), đăng qua service cũ,
                               retry lỗi tạm thời (tối đa 3, backoff), không retry lỗi quyền/token, kẹt >20' → Failed
  GrowthInsightsService.cs   — job mỗi giờ: follower, Page Insights, số liệu từng bài; tổng quan/biểu đồ/xếp hạng/giờ đăng
  GrowthAiService.cs         — caption nhiều phong cách, repurpose, đề xuất, phân tích đối thủ (Gemini, ngân sách)
  GrowthReportService.cs     — báo cáo, đánh giá Meta One, CSV / Excel (SpreadsheetML) 
  GrowthRegistration.cs      — DI
Api/Controllers/GrowthController.cs — /api/growth/...
```

## Bảng (migration `AddGrowthCampaign`, chỉ CREATE TABLE — không đụng bảng cũ)

| Bảng | Mục đích | Ghi chú |
|---|---|---|
| `GrowthCampaigns` | Chiến dịch: Page, ngày, mục tiêu, tần suất, khung giờ, chế độ duyệt, ngân sách AI, follower đầu/cuối, checklist Meta One, chi phí Meta One | FK `FacebookAccountId` **ON DELETE SET NULL** (xoá Page ở module cũ không bị chặn) + lưu `PageName` |
| `GrowthScheduledContents` | Nội dung trong lịch (Reels/Ảnh/Album/Story/Tương tác), trạng thái, idempotency key (unique), retry, kết quả đăng | FK campaign cascade |
| `GrowthContentAssets` | Ảnh/video tải lên hoặc xuất từ template (file ở StudioFileStore) | kiểm tra magic bytes + kích thước |
| `GrowthDailyMetrics` | Số liệu Page theo ngày VN (follows, unfollows, page views, media views, engagement) + số nhập tay (lời mời Meta One) | unique (Page, Date) |
| `GrowthPostMetrics` | Số liệu từng nội dung theo ngày | unique (Content, Date) |
| `GrowthCompetitors` | ≤ 5 đối thủ / Page | |
| `GrowthCompetitorSnapshots` | Quan sát nhập tay (follower, tần suất, bài nổi bật…) | |
| `GrowthCampaignReports` | Báo cáo đã tạo (JSON) | |
| `GrowthActivityLogs` | Audit log của module | |

Không tạo `GrowthFollowerSnapshots` (đã có `PageFollowerSnapshots`) và `GrowthCampaignPages` (một chiến dịch = một Page).

## API (`/api/growth`, mọi endpoint yêu cầu `X-Studio-Key`)

```
GET    /pages                                   Page đã kết nối (từ module cũ) + chiến dịch đang chạy
GET    /pages/{pageId}/capabilities             Bảng quyền/khả dụng Graph API dò thật bằng token của Page
GET    /campaigns?pageId=                        POST /campaigns (mẫu mặc định "Meta One 7-Day Growth Challenge")
GET    /campaigns/{id}                           PUT /campaigns/{id}   DELETE (chỉ khi chưa chạy)
POST   /campaigns/{id}/start|pause|resume|complete|cancel
PUT    /campaigns/{id}/checklist                 PUT /campaigns/{id}/manual-metrics
GET    /campaigns/{id}/overview                  KPI + biểu đồ + hôm nay/sắp đăng/thất bại + tiến độ
POST   /campaigns/{id}/sync                      Đọc lại số liệu từ Meta ngay
GET    /campaigns/{id}/analytics/posts|ranking|hours|followers
GET    /campaigns/{id}/contents?from&to&kind     POST /campaigns/{id}/contents
PUT    /campaigns/{id}/contents/{cid}            POST …/{cid}/approve|cancel|publish-now|retry|mark-manual
GET    /campaigns/{id}/sources                   Video đã hoàn thành (Studio Video AI, AI Video Studio, tin tức)
POST   /campaigns/{id}/assets (multipart)        GET /assets/{assetId}/file (ảnh preview, id ngẫu nhiên)
POST   /campaigns/{id}/ai/captions|repurpose|suggestions
GET    /campaigns/{id}/report                    POST /campaigns/{id}/report (tạo + AI nhận xét)
GET    /campaigns/{id}/report.csv|report.xls
GET    /campaigns/{id}/logs
GET/POST/PUT/DELETE /competitors?pageId=…   POST /competitors/{id}/snapshots   POST /competitors/analyze
```

## Frontend

```
src/services/growth.ts               — client (dùng chung khoá Studio)
src/lib/growthMath.ts                — công thức thuần (growth %, conversion, tiến độ) — có test
src/pages/growth/GrowthPage.tsx      — khung: chọn Page + chiến dịch, tab trang con, mở khoá
src/pages/growth/OverviewView.tsx    src/pages/growth/CalendarView.tsx    src/pages/growth/ContentStudioView.tsx
src/pages/growth/CampaignsView.tsx   src/pages/growth/AnalyticsView.tsx   src/pages/growth/CompetitorsView.tsx
src/pages/growth/SettingsView.tsx    src/pages/growth/charts.tsx (SVG)    src/pages/growth/ImageTemplateStudio.tsx (Canvas)
```

Ảnh template dựng bằng **Canvas API của trình duyệt** (không thêm thư viện, không gọi AI ảnh), xuất JPEG rồi tải lên như asset.
PDF: trang báo cáo có CSS in → "In / Lưu PDF" của trình duyệt (dự án không có thư viện PDF).

## Đăng nội dung (Phase 2)

| Loại | Đường đăng |
|---|---|
| Reels từ Studio Video AI | `StudioService.RunPublishAsync` (hàm cũ, không đổi) sau khi đặt caption; kiểm tra video chưa từng đăng |
| Reels từ AI Video Studio | `IFacebookPublishingService.PublishVideoFileAsync` + bản ghi `FacebookPosts` (Source `Growth`) để thống kê cũ theo dõi |
| Reels tin tức | `IFacebookPublishingService.PublishReelAsync` (giữ nguyên kiểm tra duyệt/chống trùng) |
| Ảnh / album | `IFacebookPublishingService.PublishPhotoPostAsync` |
| Bài tương tác | có ảnh → như trên; chỉ chữ → `GrowthGraphClient.PublishTextPostAsync` |
| Story video | `IFacebookPublishingService.PublishVideoStoryAsync` (kiểm tra ≤ 60 s) |
| Story ảnh | `GrowthGraphClient.PublishPhotoStoryAsync`; lỗi quyền/không hỗ trợ → `Failed` + hướng dẫn đăng thủ công, nút "Đã đăng thủ công" |

Chống trùng: idempotency key unique, claim bằng `UPDATE … WHERE Status = 'Scheduled'`, đã có `ExternalPostId` thì không bao giờ đăng lại, bài kẹt "Publishing" không tự đăng lại (đánh dấu cần kiểm tra Page).
Dừng ngay: pause/complete/cancel → bài Scheduled không được claim; complete/cancel chuyển chúng thành Cancelled. Không đăng sau `EndDate`.

## Test (Phase 5)

- Unit: thời gian VN, công thức KPI, phân loại lỗi Graph (tạm thời / token / quyền), giờ đăng đề xuất, verdict Meta One, parser AI, CSV.
- API/Integration (DB test riêng): tạo chiến dịch, vòng đời, validation, IDOR (nội dung/asset/đối thủ của chiến dịch khác → 404), idempotency, giới hạn 5 đối thủ, upload sai loại → 400, thiếu khoá → 401, claim chỉ một lần, không đăng khi chiến dịch tạm dừng/kết thúc, lỗi token → Failed không retry, tạo báo cáo/CSV.
- Frontend: `tsc` + build, test thuần cho `growthMath`.
- Regression: chạy toàn bộ suite cũ.

---

# Kết quả triển khai (2026-11-26)

## File thêm mới
- Backend: `Domain/Entities/GrowthEntities.cs`, `Application/DTOs/GrowthDtos.cs`, `Infrastructure/Growth/*` (GrowthTime, GrowthGraphClient, GrowthCampaignService, GrowthContentService, GrowthPublisher + GrowthPublishGateway, GrowthInsightsService, GrowthAiService, GrowthReportService, GrowthCompetitorService, GrowthRegistration + GrowthCapabilityService, GrowthModelConfiguration), `Api/Controllers/GrowthController.cs`, migration `20261126133924_AddGrowthCampaign` (chỉ CREATE TABLE/INDEX), `tests/GrowthCampaignTests.cs`.
- Frontend: `services/growth.ts`, `lib/growthMath.ts`, `pages/growth/*` (GrowthPage, OverviewView, CalendarView, ContentStudioView, ImageTemplateStudio, CampaignsView, AnalyticsView, CompetitorsView, SettingsView, ContentEditor, charts, shared), `tests/growthMath.test.ts`.

## File cũ bị sửa (chỉ điểm đăng ký, không đổi logic)
| File | Thay đổi |
|---|---|
| `IApplicationDbContext.cs`, `ApplicationDbContext.cs` | +9 DbSet, gọi `GrowthModelConfiguration.Configure` |
| `ApplicationDbContextModelSnapshot.cs` | sinh bởi EF, chỉ thêm dòng |
| `Infrastructure/DependencyInjection.cs` | +1 dòng đăng ký module |
| `Api/Program.cs`, `Worker/Worker.cs` | +2 recurring job: `growth-publish-tick` (mỗi phút), `growth-insights-sync` (mỗi giờ, phút 41) |
| `frontend/src/App.tsx` | +1 route `#/growth/*` (lazy-load, chunk riêng) |
| `frontend/src/components/Layout.tsx` | +1 nhóm menu "Chiến dịch tăng trưởng" (7 mục) |
| `frontend/src/index.css` | +2 biến màu biểu đồ |
| `frontend/package.json` | +script `test` |

## Kiểm thử
- Backend: **319/319 PASS** (273 test cũ + 46 test mới: logic, API, quyền truy cập, IDOR, idempotency, chống đăng trùng khi 4 worker cùng claim, tạm dừng/hết hạn không đăng, retry lỗi tạm thời, không retry lỗi token, bài kẹt không tự đăng lại, xoá Page cũ vẫn chạy, che token).
- Frontend: `tsc` + `vite build` OK; `npm test` 5/5 PASS; chụp headless (Chrome) 7 trang desktop + mobile 390px, sáng/tối, không lỗi console; luồng Template → lưu ảnh → mở editor, Tạo báo cáo, Kiểm tra quyền chạy thật trên DB test.

## Giới hạn đã biết (không giả lập)
- Story: chỉ biết Page có được đăng qua API hay không khi đăng thử; bị từ chối → `Failed/Unsupported` + hướng dẫn đăng thủ công + nút "Đã đăng thủ công".
- Không có số liệu Story, lượt xem từ người chưa theo dõi, số lời mời Meta One qua API → hiển thị "Chưa có dữ liệu" kèm lý do / cho nhập tay.
- Page Insights theo ngày phụ thuộc metric Meta còn hỗ trợ cho từng Page (dò lúc chạy, lưu lý do từ chối).
- Excel = SpreadsheetML (.xls), PDF = In/Lưu PDF của trình duyệt (dự án không có thư viện xlsx/pdf).
- Đăng Reels từ Studio Video AI dùng nguyên chức năng đăng của Studio, nên cũng đăng lên Instagram liên kết (giống nút "Đăng" của Studio).

---

# Chế độ tự động (Autopilot) — bổ sung 2026-11-26

Một công tắc trên **Tổng quan chiến dịch**. Job `growth-autopilot-tick` (10 phút/lần, từ 05:00 giờ VN) với mỗi Page bật tự động:

| Việc | Cách làm | Chống trùng |
|---|---|---|
| Bài viết (1/ngày) | Kịch bản chưa dùng (xoay vòng chủ đề) → Gemini vẽ 1 ảnh 4:5 từ cảnh giữa kịch bản (không chữ) → watermark Page (watermark có sẵn của Studio) → AI viết bài 120–220 chữ + câu hỏi + dòng "Ảnh minh hoạ được tạo bằng AI" → đăng ảnh lên tường | `GrowthScriptUsages` unique (Page, kịch bản); huỷ bài chưa đăng → trả kịch bản lại |
| Reels (1/ngày) | Video đã dựng xong mới nhất, chưa đăng (Studio Video AI, rồi AI Video Studio) — **không tự tạo video** (~2 USD/video) | Video đã có Reels trong lịch không bị chọn lại |
| Tin (mặc định 2/ngày) | Tin 1: đoạn ~45 s ồn/sôi động nhất của video Page (FFmpeg ebur128), cắt ngay trước khi đăng; Tin 2: ảnh kịch bản của ngày | Đoạn đã dùng của mỗi video không cắt lại |
| Hết kịch bản | Thông báo đỏ trên bảng điều khiển, dừng bài viết hằng ngày; cảnh báo khi còn ≤ 7 | — |

- Kịch bản: trang tự gửi 300 kịch bản của "Lên kịch bản" (và kịch bản bạn tự lưu trong trình duyệt) lên server khi mở Chiến dịch tăng trưởng (chỉ gửi kịch bản mới).
- Nội dung vào chiến dịch đang chạy của Page; nếu không có, tự tạo chiến dịch "Tự động dd/MM–dd/MM" 7 ngày (chế độ đăng tự động, ngân sách AI 2 USD/tuần) → mỗi tuần có báo cáo riêng. Tạm dừng chiến dịch = tạm dừng tự động.
- Khoá idempotency cố định theo (Page, ngày, ô) → hai worker không bao giờ tạo trùng; mỗi bước tối đa 3 lần thử/ngày.
- Giờ: bài viết 11:30, Reels 19:30, Tin 07:30 / 21:00 — Reels tự dời sang giờ có lượt xem cao nhất khi đủ dữ liệu thật.
- Chi phí ước tính: ~0,05 USD/ngày (ảnh + chữ) ≈ 1,5 USD/tháng.
- Test: 330/330 backend PASS (11 test autopilot, gồm cắt clip thật bằng FFmpeg).
