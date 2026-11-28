/**
 * ─── Cấu hình trang public /cheo-uy-tinh ─── (sửa ở ĐÂY, không sửa trong component)
 *
 * Để `null` / mảng rỗng thì trang tự lấy dữ liệu THẬT của Page từ Facebook
 * (qua API public của hệ thống, cache 10 phút).
 */

export interface FeaturedPost {
  /** Mã riêng của bài (dùng cho thống kê click). */
  id: string;
  title: string;
  description?: string;
  imageUrl?: string;
  /** true = bài video (hiện biểu tượng ▶). */
  isVideo?: boolean;
  /** Link bài viết trên Facebook. */
  url: string;
}

export interface LandingConfig {
  /** Đường dẫn trang + khoá thống kê. */
  slug: string;
  /** ID Facebook Page (đã kết nối trong Cài đặt → Facebook). */
  pageId: string;
  pageName: string;
  /** Link Facebook Page chính mà nút lớn sẽ mở. */
  pageUrl: string;
  /** null = ảnh đại diện thật của Page. */
  avatarUrl: string | null;
  /** null = ảnh bìa thật của Page. */
  coverUrl: string | null;
  /** null = phần "Giới thiệu" của Page trên Facebook. */
  description: string | null;
  greeting: string;
  /** Chữ trên nút mở Facebook Page (nằm cạnh số người theo dõi). */
  cta: { button: string };
  /** Bài nổi bật chọn tay (3–6 bài). Để trống = tự lấy các bài mới nhất của Page. */
  featuredPosts: FeaturedPost[];
  /** Số bài mới nhất lấy tự động khi featuredPosts trống. */
  livePostCount: number;
  thanks: string;
}

export const cheoUyTinhConfig: LandingConfig = {
  slug: 'cheo-uy-tinh',
  pageId: '1358397297367678',
  pageName: 'Trạm ký ức',
  pageUrl: 'https://www.facebook.com/1358397297367678',
  avatarUrl: null,
  coverUrl: null,
  description: null,
  greeting: 'Bên mình hứa chéo lại uy tính khi page nhận được thông báo ạ',
  cta: {
    button: 'MỞ FACEBOOK – ỦNG HỘ PAGE NGAY',
  },
  featuredPosts: [],
  livePostCount: 6,
  thanks: 'Cảm ơn bạn đã dành thời gian ghé thăm và ủng hộ Page. Chúc bạn một ngày thật vui! 💙',
};
