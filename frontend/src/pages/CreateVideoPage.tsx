import React, { useState } from 'react';
import {
  Link as LinkIcon,
  Bot,
  Volume2,
  Film,
  Sparkles,
  ShieldAlert,
  ArrowRight,
  CheckCircle,
  Loader2,
  AlertCircle,
  Image as ImageIcon
} from 'lucide-react';
import { api } from '../services/api';

interface CreateVideoPageProps {
  onCreated: (projectId: string) => void;
}

export const CreateVideoPage: React.FC<CreateVideoPageProps> = ({ onCreated }) => {
  const [url, setUrl] = useState('');
  const [title, setTitle] = useState('');
  const [aiProvider, setAiProvider] = useState<'Gemini' | 'Claude'>('Gemini');
  const [targetDuration, setTargetDuration] = useState(60); // 55-65s, target 60s
  const [voice, setVoice] = useState('vi-VN-HoaiMyNeural');
  const [subtitleStyle, setSubtitleStyle] = useState('breaking-yellow');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [createdProjectId, setCreatedProjectId] = useState<string | null>(null);
  const [jobId, setJobId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!url) return;

    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      // 1. Create Video Project in PostgreSQL
      const project = await api.createVideoProject({
        title: title || 'Bản tin tự động',
        sourceUrl: url,
        publisher: url.includes('vnexpress') ? 'VnExpress' : url.includes('tuoitre') ? 'Tuổi Trẻ' : 'Báo điện tử',
        targetDurationSeconds: targetDuration,
        aiProvider,
        voice
      });

      // 2. Enqueue processing job into Hangfire via Worker
      const job = await api.processVideoProject(project.id);

      setCreatedProjectId(project.id);
      setJobId(job.id);
    } catch (err: any) {
      setErrorMessage(err.message || 'Không thể kết nối API hoặc cơ sở dữ liệu.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="max-w-4xl mx-auto space-y-8">
      {/* Page Title & Mode Badge */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-emerald-500/20 border border-emerald-500/30 text-emerald-300 text-xs font-semibold mb-2">
            <ImageIcon className="w-3.5 h-3.5" />
            <span>Chế độ A: ArticleImageOnly (Chỉ dùng ảnh bài báo • 60s Reels)</span>
          </div>
          <h1 className="text-2xl font-bold text-white tracking-tight">Tạo video tin tức 60 giây từ URL bài báo</h1>
          <p className="text-sm text-gray-400 mt-1">
            Hệ thống bóc tách nội dung, trích xuất hình ảnh thực tế trong bài, chia 8–10 phân cảnh và render video dọc 9:16 bằng FFmpeg.
          </p>
        </div>
      </div>

      {createdProjectId ? (
        <div className="glass-panel p-8 rounded-2xl border border-emerald-500/30 text-center space-y-4">
          <div className="w-14 h-14 mx-auto rounded-full bg-emerald-500/20 text-emerald-400 flex items-center justify-center">
            <CheckCircle className="w-8 h-8" />
          </div>
          <h2 className="text-xl font-bold text-white">Yêu cầu đã được lưu vào PostgreSQL & chuyển tới Hangfire!</h2>
          <p className="text-sm text-gray-300 max-w-lg mx-auto">
            Mã dự án: <span className="font-mono text-indigo-400 font-semibold">{createdProjectId}</span><br />
            Mã tác vụ: <span className="font-mono text-emerald-400">{jobId}</span><br />
            Chế độ: <span className="text-emerald-300 font-semibold">ArticleImageOnly (55–65s, 8–10 cảnh)</span>.<br />
            Worker đang xử lý các bước trong pipeline. Bạn có thể xem trực tiếp tiến độ tại màn hình chi tiết.
          </p>
          <div className="pt-2 flex justify-center space-x-3">
            <button
              onClick={() => onCreated(createdProjectId)}
              className="px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-sm transition shadow-lg shadow-indigo-600/30"
            >
              Xem chi tiết & Theo dõi tiến độ
            </button>
            <button
              onClick={() => {
                setCreatedProjectId(null);
                setJobId(null);
                setUrl('');
                setTitle('');
              }}
              className="px-5 py-2.5 rounded-xl bg-[#1F2937] hover:bg-[#374151] text-gray-300 font-semibold text-sm transition"
            >
              Tạo video khác
            </button>
          </div>
        </div>
      ) : (
        <form onSubmit={handleSubmit} className="space-y-6">
          {errorMessage && (
            <div className="p-4 rounded-xl bg-rose-950/30 border border-rose-500/40 text-rose-300 text-xs flex items-center gap-2">
              <AlertCircle className="w-4 h-4 shrink-0 text-rose-400" />
              <span>{errorMessage}</span>
            </div>
          )}

          {/* URL & Title Input */}
          <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
            <div>
              <label className="block text-sm font-semibold text-white flex items-center gap-2 mb-2">
                <LinkIcon className="w-4 h-4 text-indigo-400" />
                <span>URL bài báo gốc</span>
                <span className="text-rose-400">*</span>
              </label>
              <input
                type="url"
                required
                value={url}
                onChange={(e) => setUrl(e.target.value)}
                placeholder="https://vnexpress.net/... hoặc https://tuoitre.vn/..."
                className="w-full px-4 py-3.5 rounded-xl bg-[#0B0F19] border border-[#1F2937] text-white placeholder-gray-500 text-sm focus:outline-none focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 transition"
              />
            </div>

            <div>
              <label className="block text-xs text-gray-400 mb-1">
                Tiêu đề video mong muốn (Tùy chọn, để trống AI sẽ tự đặt chuẩn báo chí)
              </label>
              <input
                type="text"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="Ví dụ: Khánh thành cầu Mỹ Thuận 2 kết nối đôi bờ Tiền Giang - Vĩnh Long..."
                className="w-full px-3 py-2.5 rounded-xl bg-[#0B0F19] border border-[#1F2937] text-white text-xs focus:outline-none focus:border-indigo-500"
              />
            </div>
          </div>

          {/* Configuration Grid */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {/* AI Editor Options */}
            <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
              <div className="flex items-center space-x-2 text-white font-semibold text-sm">
                <Bot className="w-4 h-4 text-purple-400" />
                <span>AI Biên tập & Phân cảnh (8–10 cảnh)</span>
              </div>
              <div className="space-y-3">
                <div>
                  <label className="text-xs text-gray-400 block mb-1.5">Mô hình AI tóm tắt</label>
                  <div className="grid grid-cols-2 gap-2">
                    <button
                      type="button"
                      onClick={() => setAiProvider('Gemini')}
                      className={`p-2.5 rounded-lg text-xs font-semibold border text-center transition ${
                        aiProvider === 'Gemini'
                          ? 'bg-indigo-600/20 border-indigo-500 text-indigo-300'
                          : 'bg-[#0B0F19] border-[#1F2937] text-gray-400 hover:text-white'
                      }`}
                    >
                      Google Gemini
                    </button>
                    <button
                      type="button"
                      onClick={() => setAiProvider('Claude')}
                      className={`p-2.5 rounded-lg text-xs font-semibold border text-center transition ${
                        aiProvider === 'Claude'
                          ? 'bg-purple-600/20 border-purple-500 text-purple-300'
                          : 'bg-[#0B0F19] border-[#1F2937] text-gray-400 hover:text-white'
                      }`}
                    >
                      Anthropic Claude
                    </button>
                  </div>
                </div>

                <div>
                  <div className="flex justify-between text-xs text-gray-400 mb-1">
                    <span>Thời lượng video mục tiêu (55–65s)</span>
                    <span className="font-mono text-indigo-400 font-bold">{targetDuration} giây</span>
                  </div>
                  <input
                    type="range"
                    min={55}
                    max={65}
                    value={targetDuration}
                    onChange={(e) => setTargetDuration(Number(e.target.value))}
                    className="w-full accent-indigo-600 bg-gray-700 h-1.5 rounded-lg appearance-none cursor-pointer"
                  />
                  <div className="flex justify-between text-[10px] text-gray-500 mt-0.5">
                    <span>55s (Ngắn gọn)</span>
                    <span className="text-indigo-400 font-semibold">60s (Chuẩn Reels)</span>
                    <span>65s (Chi tiết)</span>
                  </div>
                </div>
              </div>
            </div>

            {/* TTS & Voice Options */}
            <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
              <div className="flex items-center space-x-2 text-white font-semibold text-sm">
                <Volume2 className="w-4 h-4 text-pink-400" />
                <span>Giọng đọc bản tin & Phụ đề</span>
              </div>
              <div className="space-y-3">
                <div>
                  <label className="text-xs text-gray-400 block mb-1.5">Giọng đọc tiếng Việt (TTS)</label>
                  <select
                    value={voice}
                    onChange={(e) => setVoice(e.target.value)}
                    className="w-full px-3 py-2 rounded-lg bg-[#0B0F19] border border-[#1F2937] text-white text-xs focus:outline-none focus:border-indigo-500"
                  >
                    <option value="vi-VN-HoaiMyNeural">Hoài My (Nữ, giọng Bắc tự nhiên, truyền cảm)</option>
                    <option value="vi-VN-NamMinhNeural">Nam Minh (Nam, giọng Bắc chuẩn tin tức)</option>
                  </select>
                </div>

                <div>
                  <label className="text-xs text-gray-400 block mb-1.5">Phong cách chữ phụ đề</label>
                  <select
                    value={subtitleStyle}
                    onChange={(e) => setSubtitleStyle(e.target.value)}
                    className="w-full px-3 py-2 rounded-lg bg-[#0B0F19] border border-[#1F2937] text-white text-xs focus:outline-none focus:border-indigo-500"
                  >
                    <option value="breaking-yellow">Breaking News Vàng / Đen (An toàn, dễ đọc trên điện thoại)</option>
                    <option value="modern-white">Modern Clean Trắng / Viền tối</option>
                    <option value="high-contrast">Tương phản cao (Đặc biệt cho màn hình dọc)</option>
                  </select>
                </div>
              </div>
            </div>
          </div>

          {/* Strict Policy Reminder */}
          <div className="p-4 rounded-xl bg-amber-950/20 border border-amber-500/30 flex items-start space-x-3 text-xs text-amber-200">
            <ShieldAlert className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
            <div className="space-y-1">
              <p className="font-semibold text-amber-300">Cam kết Chế độ ArticleImageOnly:</p>
              <p className="text-amber-200/80 leading-relaxed">
                • Không sử dụng Veo hoặc API tạo video AI. Không tạo ảnh nhân vật/khuôn mặt giả.<br />
                • Chỉ sử dụng hình ảnh thực tế được trích xuất từ chính bài báo gốc. Không tìm ảnh bên ngoài.<br />
                • Nếu bài báo không có đủ hình ảnh phù hợp, hệ thống sẽ cảnh báo để bạn quyết định.<br />
                • Dựng video 9:16 (1080×1920) chuẩn Reels khoảng 60 giây bằng FFmpeg, có kiểm duyệt trước khi đăng.
              </p>
            </div>
          </div>

          {/* Submit Button */}
          <div className="flex justify-end">
            <button
              type="submit"
              disabled={isSubmitting || !url}
              className="px-6 py-3 rounded-xl bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white font-semibold text-sm flex items-center space-x-2 shadow-lg shadow-indigo-600/30 transition-all hover:scale-[1.02]"
            >
              {isSubmitting ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  <span>Đang bóc tách & tạo Hangfire Job...</span>
                </>
              ) : (
                <>
                  <Sparkles className="w-4 h-4" />
                  <span>Bắt đầu sản xuất video 60s</span>
                  <ArrowRight className="w-4 h-4" />
                </>
              )}
            </button>
          </div>
        </form>
      )}
    </div>
  );
};
