import React from 'react';
import { Newspaper, Image as ImageIcon, ExternalLink, ShieldCheck, Clock, ArrowRight } from 'lucide-react';
import { Article } from '../types';

interface ArticlesPageProps {
  onSelectArticle: (articleId: string) => void;
}

export const ArticlesPage: React.FC<ArticlesPageProps> = ({ onSelectArticle }) => {
  const mockArticles: Article[] = [
    {
      id: 'art-1',
      title: 'Tuyến metro số 1 Bến Thành - Suối Tiên chính thức vận hành thương mại',
      url: 'https://vnexpress.net/tuyen-metro-so-1-ben-thanh-suoi-tien-van-hanh-4829102.html',
      publisher: 'VnExpress',
      author: 'Gia Minh',
      publishedDate: '15/11/2026 08:30',
      bodySnippet: 'Hàng nghìn người dân TP HCM hào hứng trải nghiệm tuyến metro đầu tiên trong ngày đầu mở cửa đón khách miễn phí...',
      imageCount: 4,
      extractedAt: '15/11/2026 09:15',
      images: [
        {
          id: 'img-1',
          originalImageUrl: 'https://images.unsplash.com/photo-1544620347-c4fd4a3d5957?w=400',
          caption: 'Đoàn tàu metro số 1 tại ga Bến Thành sáng nay',
          order: 1,
          rightsStatus: 'Authorized',
          selectedForVideo: true
        },
        {
          id: 'img-2',
          originalImageUrl: 'https://images.unsplash.com/photo-1517649763962-0c623266ddc0?w=400',
          caption: 'Người dân xếp hàng nhận vé trải nghiệm',
          order: 2,
          rightsStatus: 'Authorized',
          selectedForVideo: true
        }
      ]
    },
    {
      id: 'art-2',
      title: 'Việt Nam phát triển thành trung tâm công nghệ bán dẫn khu vực Đông Nam Á',
      url: 'https://tuoitre.vn/viet-nam-phat-trien-cong-nghe-ban-dan-20261114.htm',
      publisher: 'Tuổi Trẻ Online',
      author: 'Minh Khôi',
      publishedDate: '14/11/2026 15:45',
      bodySnippet: 'Các tập đoàn công nghệ lớn tiếp tục cam kết mở rộng đầu tư nhà máy đóng gói vi mạch và trung tâm R&D tại Hà Nội và Đà Nẵng...',
      imageCount: 3,
      extractedAt: '14/11/2026 16:00',
      images: [
        {
          id: 'img-3',
          originalImageUrl: 'https://images.unsplash.com/photo-1518770660439-4636190af475?w=400',
          caption: 'Dây chuyền thử nghiệm vi mạch bán dẫn',
          order: 1,
          rightsStatus: 'Authorized',
          selectedForVideo: true
        }
      ]
    }
  ];

  return (
    <div className="space-y-6 max-w-7xl mx-auto">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">Danh sách bài báo đã trích xuất</h1>
          <p className="text-sm text-gray-400 mt-1">
            Quản lý các bài báo đã được crawl nội dung và hình ảnh gốc sẵn sàng để biên tập video.
          </p>
        </div>
      </div>

      <div className="space-y-4">
        {mockArticles.map((art) => (
          <div
            key={art.id}
            className="glass-panel p-6 rounded-2xl border border-[#1F2937] hover:border-indigo-500/30 transition-all space-y-4"
          >
            <div className="flex items-start justify-between gap-4">
              <div className="space-y-1.5 flex-1">
                <div className="flex items-center space-x-2 text-xs">
                  <span className="px-2 py-0.5 rounded bg-indigo-500/20 text-indigo-300 font-semibold">
                    {art.publisher}
                  </span>
                  <span className="text-gray-500">•</span>
                  <span className="text-gray-400 flex items-center gap-1">
                    <Clock className="w-3 h-3" /> {art.publishedDate}
                  </span>
                  {art.author && (
                    <>
                      <span className="text-gray-500">•</span>
                      <span className="text-gray-400">Tác giả: {art.author}</span>
                    </>
                  )}
                </div>
                <h3 className="text-lg font-bold text-white hover:text-indigo-300 transition">
                  {art.title}
                </h3>
                <p className="text-xs text-gray-400 line-clamp-2 leading-relaxed">
                  {art.bodySnippet}
                </p>
              </div>

              <a
                href={art.url}
                target="_blank"
                rel="noreferrer"
                className="p-2 rounded-lg bg-[#1F2937] text-gray-400 hover:text-white hover:bg-gray-700 transition"
                title="Mở link gốc"
              >
                <ExternalLink className="w-4 h-4" />
              </a>
            </div>

            {/* Extracted Images Preview */}
            <div className="pt-3 border-t border-gray-800/80">
              <div className="flex items-center justify-between text-xs mb-3">
                <span className="text-gray-400 flex items-center gap-1.5 font-medium">
                  <ImageIcon className="w-3.5 h-3.5 text-indigo-400" />
                  Ảnh bài báo trích xuất ({art.images.length}/{art.imageCount} ảnh hợp lệ)
                </span>
                <span className="text-[11px] text-emerald-400 flex items-center gap-1">
                  <ShieldCheck className="w-3.5 h-3.5" /> Nguồn từ chính bài viết
                </span>
              </div>

              <div className="flex gap-4 overflow-x-auto pb-2">
                {art.images.map((img) => (
                  <div
                    key={img.id}
                    className="w-48 shrink-0 rounded-xl overflow-hidden bg-[#0B0F19] border border-gray-800 space-y-1.5 p-1.5"
                  >
                    <div className="h-28 rounded-lg overflow-hidden bg-gray-900 relative">
                      <img
                        src={img.originalImageUrl}
                        alt={img.caption}
                        className="w-full h-full object-cover"
                      />
                      <span className="absolute top-1 left-1 px-1.5 py-0.5 rounded text-[10px] font-mono bg-black/70 text-white">
                        #{img.order}
                      </span>
                    </div>
                    <p className="text-[11px] text-gray-300 line-clamp-1 px-1 font-medium">
                      {img.caption}
                    </p>
                    <div className="px-1 flex items-center justify-between text-[10px]">
                      <span className="text-emerald-400 font-semibold">{img.rightsStatus}</span>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Actions */}
            <div className="pt-2 flex justify-end">
              <button
                onClick={() => onSelectArticle(art.id)}
                className="px-4 py-2 rounded-xl bg-indigo-600/20 hover:bg-indigo-600 border border-indigo-500/40 text-indigo-300 hover:text-white text-xs font-semibold flex items-center space-x-1.5 transition"
              >
                <span>Tạo kịch bản video từ bài này</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};
