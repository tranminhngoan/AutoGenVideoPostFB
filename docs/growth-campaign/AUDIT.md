# Meta Growth Campaign — Phase 0 Audit

Ngày khảo sát: 2026-11-26 · Commit nền: `434543a`

Tài liệu này ghi lại **hiện trạng thực tế** của codebase trước khi thêm module "Chiến dịch tăng trưởng", và chỉ rõ thành phần nào được tái sử dụng.

## 1. Cấu trúc repository

| Thư mục | Nội dung |
|---|---|
| `backend/src/NewsStudio.Domain` | Entity (EF Core), `BaseEntity` (Guid Id, CreatedAtUtc, UpdatedAtUtc) |
| `backend/src/NewsStudio.Application` | DTO, interface (`IFacebookPublishingService`, `IApplicationDbContext`, …) |
| `backend/src/NewsStudio.Infrastructure` | Triển khai service, EF `ApplicationDbContext`, migrations, Hangfire, Facebook, AI, Studio |
| `backend/src/NewsStudio.Api` | ASP.NET Core 8 controllers, `Program.cs` (embedded Hangfire server khi `Hangfire:EmbeddedServer=true`) |
| `backend/src/NewsStudio.Worker` | Worker Hangfire chạy riêng (local) |
| `backend/tests/NewsStudio.Tests` | xUnit + FluentAssertions + `WebApplicationFactory`, DB test riêng `newsstudio_test`, `Simulation:Enabled=true` |
| `frontend/` | React 18 + TypeScript + Vite + Tailwind 3 + lucide-react. **Không có** thư viện chart, form, router, test |

## 2. Frontend routing

- Router tự viết, hash-based: `frontend/src/lib/router.ts` (`useRoute`, `navigate`, `href`).
- `frontend/src/App.tsx` chuyển trang bằng `switch (first segment)`; trang con nhận `segments`.
- Module mới dùng route `#/growth/<sub-page>` — chỉ thêm 1 `case` + 1 key trong `TITLES`.

## 3. Sidebar / menu

- `frontend/src/components/Layout.tsx` → `SidebarContent` có các nhóm `main`, `studio`, `local`, `secondary`.
- Thêm một nhóm mới "Chiến dịch tăng trưởng" (icon `TrendingUp`) **giữa nhóm video và "Hệ thống"**; không sửa item cũ.

## 4. UI component library

`frontend/src/components/ui.tsx` (tự viết, Tailwind, token màu `fg/canvas/muted/line/brand/accent/success/warning/danger`):
`Button, ButtonLink, IconButton, PageHeader, Card, CardHeader, CardBody, Badge, Dot, Alert, Input, Textarea, Select, Field, Checkbox, Segmented, RadioCard, Tabs, Spinner, Skeleton, EmptyState, ProgressBar, Thumb, Modal, Drawer, Menu`.
`components/feedback.tsx`: `useToast`, `useConfirm`. `lib/format.ts`: `formatDateTime`, `formatNumber`, `formatUsd`, `errorText`…
Không có thư viện chart → biểu đồ vẽ bằng SVG thuần trong module mới.

## 5. Backend architecture

.NET 8 Clean Architecture. Service lớn đặt ở Infrastructure (ví dụ `StudioService`, `AiStudioService` là class cụ thể, đăng ký `AddScoped`). Controller mỏng, bắt `KeyNotFoundException → 404`, `ArgumentException/InvalidOperationException → 400`. Global rate-limit 120 req/phút.

## 6. Database schema

PostgreSQL + EF Core 8 (Npgsql). Migrations trong `Infrastructure/Persistence/Migrations`, tự chạy lúc khởi động (`Database:AutoMigrate`). Bảng liên quan:

| Bảng | Dùng cho module mới |
|---|---|
| `FacebookAccounts` | **Danh sách Page đã kết nối + token mã hoá** → tái sử dụng (chỉ đọc) |
| `FacebookPosts`, `VideoMetrics` | Bài Reels đã đăng + số liệu theo ngày → đọc để phân tích Reels |
| `PageFollowerSnapshots` | Follower Facebook/Instagram theo ngày (VN) → **tái sử dụng**, không tạo bảng follower thứ hai |
| `StudioVideos` | Kho video "Studio Video AI" (Seedance) — `FinalBlobId`, `Status`, `FacebookAccountId` |
| `AiStudioProjects` | Kho "AI Video Studio" — `FinalBlobId`, `FinalStatus = Done`, `Caption`, `FacebookAccountId` |
| `VideoProjects` | Video tin tức (đăng qua `PublishReelAsync`, cần duyệt biên tập) |
| `StudioCostRecords` | Nhật ký chi phí AI của studio → ghi chi phí AI của module mới vào đây (cùng ngân sách tháng) |
| `AuditLogs` | Audit chung — module mới có log riêng `GrowthActivityLogs` (gắn chiến dịch / nội dung) |

## 7. Tenant model

**Không có tenant/shop.** Ứng dụng có một chủ sở hữu duy nhất (ghi chú trong `AiStudioProject`: "The app has a single owner (no tenants)"). Đơn vị phân tách dữ liệu thực tế là **Facebook Page** (`FacebookAccountId`).
→ Module mới cô lập dữ liệu theo **chiến dịch → Page**: mọi tài nguyên con (nội dung, asset, đối thủ, số liệu) được kiểm tra thuộc đúng chiến dịch/Page trên URL trước khi đọc/ghi (chống IDOR).

## 8. Authentication / Authorization

- Không có đăng nhập người dùng.
- `Security:AdminApiKey` (middleware tuỳ chọn trong `Program.cs`) cho toàn API.
- `StudioAccess.Denied(...)` (`Api/Controllers/StudioAccess.cs`): thao tác của Studio cần header `X-Studio-Key` khớp khoá Vercel AI Gateway / Gemini trả phí / AdminApiKey. Frontend hỏi khoá 1 lần (`components/studio/StudioUnlock.tsx`, `services/studio.ts`).
- Module mới: **mọi endpoint (đọc và ghi)** đều qua `StudioAccess.Denied` — chặt hơn Studio (Studio để đọc mở).

## 9. Facebook Page connection

`IFacebookPublishingService` / `FacebookPublishingService` (`Infrastructure/Facebook`):
- `GetConnectedPagesAsync`, `ConnectPageAsync`, `ConnectFromUserTokenAsync` (Kết nối nhanh), `DisconnectPageAsync`, `DeletePageAsync`.
- Token lưu mã hoá (`IFacebookTokenProtectionService`, Data Protection), không bao giờ trả về client (DTO chỉ có `MaskedToken`).
- Graph API version: `Facebook:GraphApiVersion`, mặc định **v21.0**. Module mới dùng cùng cấu hình, **không nâng version**.
- Quyền app yêu cầu (`RequiredPermissions`): `pages_show_list, pages_read_engagement, pages_manage_posts, pages_manage_engagement, read_insights, instagram_basic, instagram_content_publish, instagram_manage_insights`.

## 10. Facebook publishing (đã có)

| Hàm | Graph API | Dùng bởi |
|---|---|---|
| `PublishReelAsync(videoProjectId, req)` | `/{page}/video_reels` (3 pha) | Video tin tức; có kiểm tra duyệt + chống đăng trùng |
| `PublishVideoFileAsync(page, path, caption, dur)` | Reels ≤ 90 s, ngược lại `/{page}/videos` | Studio Video AI |
| `StudioService.RunPublishAsync(id)` | như trên + Instagram + bản ghi `FacebookPosts` | Nút "Đăng" của Studio Video AI |
| `PublishPhotoPostAsync(page, paths, message)` | `/{page}/photos` (unpublished) + `/{page}/feed` `attached_media` | Điểm tin hằng ngày |
| `PublishVideoStoryAsync(page, path)` | `/{page}/video_stories` (start/upload/finish) | Điểm tin hằng ngày |
| `ReadFollowersAsync(page)` | `/{page}?fields=followers_count,fan_count` | PageStatsService |
| `FetchMetricsAsync(fbPostRecordId)` | `/{video}/video_insights` (`fb_reels_total_plays`, `post_impressions_unique`…), likes/comments/shares | PageStatsService |

**Chưa có:** đăng Story ảnh (`/{page}/photo_stories`), bài chỉ có chữ, đọc Page Insights cấp trang, số liệu bài ảnh, AI Video Studio chưa có nút đăng (chỉ copy caption).

## 11. AI provider

- `FilmGeminiClient.GenerateJsonAsync(system, user, maxTokens, temperature, ct, model, lightThinking)` — Gemini, ghi usage vào `AiUsageMeter`.
- Ngân sách tháng: `StudioService.EnsureMonthlyBudgetAsync(cost)`; chi phí ghi vào `StudioCostRecords`.
- Ví dụ caption: `AiStudioService.WriteCaptionAsync` (prompt chống bịa, chống giật tít).
- Không có khoá → lỗi rõ ràng (không có mock). Test chạy `Simulation:Enabled` và khoá rỗng.

## 12. Media storage

`StudioFileStore` (`Infrastructure/Studio`): đĩa cục bộ + bucket S3-compatible (Cloudflare R2) dùng chung giữa PC và Railway. `SaveAsync`, `LocalPathAsync`, `PresignedUrlAsync`, `Delete`. Module mới lưu ảnh/video tải lên qua cùng store (key `studio/<guid>.bin`), metadata ở bảng riêng.

## 13. Background jobs

Hangfire + PostgreSQL storage. Recurring jobs đăng ký ở **hai nơi**: `Api/Program.cs` (embedded) và `Worker/Worker.cs`. `WorkerSwitchService` chọn máy chạy (PC hoặc Railway) — cả hai có thể cùng lấy job ⇒ cần khoá ở mức DB để chống đăng trùng.

## 14. Analytics hiện có

- `PageStatsService` (job `facebook-stats-sync` 2 lần/giờ): đọc số liệu Reels, phát hiện bài đăng tay, chụp follower theo ngày vào `PageFollowerSnapshots`.
- `PageInsightsService`: giờ đăng tốt theo lượt xem Reels (tối thiểu 12 mẫu).
- Không có số liệu Page cấp trang (page views, follows/unfollows theo ngày).

## 15. Kiểm tra Graph API (tài liệu Meta, 2025–2026)

| Chức năng | API | Quyền cần có | Khả dụng |
|---|---|---|---|
| Danh sách Page | `/me/accounts` | `pages_show_list` | Có — module cũ |
| Đăng Reels | `/{page}/video_reels` | `pages_manage_posts`, `pages_read_engagement`, `publish_video` | Có — module cũ |
| Đăng ảnh / album | `/{page}/photos` + `/{page}/feed` | `pages_manage_posts`, `pages_read_engagement` | Có — module cũ |
| Đăng bài chữ | `/{page}/feed` | `pages_manage_posts` | Có (thêm ở adapter mới) |
| Story video | `/{page}/video_stories` | `pages_manage_posts` (+`business_management` nếu dùng system user) | Có — module cũ; ≤ 60 s |
| Story ảnh | `/{page}/photos?published=false` → `/{page}/photo_stories` | như trên | Có trong tài liệu; **kiểm tra lúc chạy**, lỗi → hướng dẫn thủ công |
| Follower | `/{page}?fields=followers_count` | `pages_read_engagement` | Có — module cũ |
| Page Insights theo ngày | `/{page}/insights?metric=…&period=day` | `read_insights` | **Tuỳ Page/metric** — Meta đã khai tử `page_fans`, `page_impressions*` (11/2025, 06/2026). Module **dò từng metric** (`page_daily_follows_unique`, `page_daily_unfollows_unique`, `page_views_total`, `page_media_view`, `page_total_media_view_unique`, `page_post_engagements`) và ghi lại metric nào bị từ chối + lý do |
| Số liệu bài ảnh/chữ | `/{post}?fields=shares,reactions.summary,comments.summary` + `/{post}/insights` | `pages_read_engagement`, `read_insights` | Một phần — metric insight dò lúc chạy |
| Số liệu Reels | `/{video}/video_insights` | `read_insights` | Có — module cũ |
| Follower theo bài Reels | `post_video_followers` | `read_insights` | Dò lúc chạy; không có thì không hiển thị |
| Số liệu Story | — | — | **Không có API ổn định** cho Page Story → không hiển thị số liệu Story, ghi rõ |
| Meta One (nút Follow nổi bật, tự mời follow) | — | — | **Không có API** → checklist + nhập tay |
| Insights của Page đối thủ | — | — | **Không được phép** → chỉ nhập tay / URL công khai |

Nguồn: [Page Stories API](https://developers.facebook.com/docs/page-stories-api), [Deprecated Page Insights metrics](https://developers.facebook.com/documentation/pages-api/platforminsights/page/deprecated-metrics), [Page Insights API updates (08/2025)](https://developers.facebook.com/blog/post/2025/08/15/page-insights-api-updates/).

## 16. Lỗi / rủi ro phát hiện ở code cũ (báo cáo, không sửa)

1. `FacebookPublishingService.PublishPhotoPostAsync` luôn gửi `Content-Type: image/jpeg` kể cả với PNG (Meta thường tự nhận dạng, nhưng nên dò theo magic bytes). Module mới chuyển ảnh về JPEG trước khi đăng khi xuất từ template.
2. `VerifyTokenAsync` trả `Scopes` cố định (không đọc thật từ `debug_token`).
3. `Worker/Worker.cs` không đăng ký `ai-studio-cleanup` (chỉ có trong `Program.cs`).
4. Token Facebook được đưa vào query string của URL Graph (chuẩn của Meta). Log `HttpClient` hiện an toàn vì Serilog đặt `System: Warning`; nếu ai hạ mức log xuống Information thì URL có token sẽ bị ghi. Module mới không log URL.
5. **Bảo mật (phát hiện khi kiểm thử giao diện):** với token hỏng, Meta lặp lại **nguyên token** trong thông báo lỗi (`(#190) Malformed access token EAAB…`). `FacebookPublishingService.ReadGraphErrorAsync` giữ nguyên văn bản này và các màn hình cũ có thể hiển thị nó (ví dụ `StudioVideo.ErrorMessage`). Module mới che token ở mọi lỗi (`GrowthGraphClient.Redact`); đề xuất áp dụng cùng cách cho service cũ trong một thay đổi riêng.
6. Test `ApiIntegrationTests.Studio_WithoutTheKey_IsRefused` (DELETE) thất bại ở lần chạy nền đầu tiên nhưng PASS ở 3 lần chạy sau — test chập chờn (dùng chung cấu hình khoá trên host test), không liên quan module mới.
