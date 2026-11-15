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
  Loader2
} from 'lucide-react';

interface CreateVideoPageProps {
  onCreated: (projectId: string) => void;
}

export const CreateVideoPage: React.FC<CreateVideoPageProps> = ({ onCreated }) => {
  const [url, setUrl] = useState('');
  const [aiProvider, setAiProvider] = useState<'Gemini' | 'Claude'>('Gemini');
  const [targetDuration, setTargetDuration] = useState(50);
  const [voice, setVoice] = useState('vi-VN-HoaiMyNeural');
  const [subtitleStyle, setSubtitleStyle] = useState('breaking-yellow');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!url) return;

    setIsSubmitting(true);
    // Simulate job dispatch to Hangfire queue (Full processing logic in Phase 2 & 3)
    setTimeout(() => {
      setIsSubmitting(false);
      setSubmitted(true);
    }, 1200);
  };

  return (
    <div className="max-w-4xl mx-auto space-y-8">
      <div>
        <h1 className="text-2xl font-bold text-white tracking-tight">Tạo video tin tức mới từ URL bài báo</h1>
        <p className="text-sm text-gray-400 mt-1">
          Dán URL bài báo để hệ thống tự động đọc nội dung, trích xuất hình ảnh gốc và lên kịch bản video Reels.
        </p>
      </div>

      {submitted ? (
        <div className="glass-panel p-8 rounded-2xl border border-emerald-500/30 text-center space-y-4">
          <div className="w-14 h-14 mx-auto rounded-full bg-emerald-500/20 text-emerald-400 flex items-center justify-center">
            <CheckCircle className="w-8 h-8" />
          </div>
          <h2 className="text-xl font-bold text-white">Yêu cầu tạo video đã được tiếp nhận!</h2>
          <p className="text-sm text-gray-300 max-w-lg mx-auto">
            Hệ thống đang chuẩn bị tiến trình trích xuất bài báo và các hình ảnh gốc. Bạn có thể theo dõi tiến độ trong danh sách video.
          </p>
          <div className="pt-2 flex justify-center space-x-3">
            <button
              onClick={() => onCreated('demo-project-id')}
              className="px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-sm transition"
            >
              Xem chi tiết video
            </button>
            <button
              onClick={() => {
                setSubmitted(false);
                setUrl('');
              }}
              className="px-5 py-2.5 rounded-xl bg-[#1F2937] hover:bg-[#374151] text-gray-300 font-semibold text-sm transition"
            >
              Tạo video khác
            </button>
          </div>
        </div>
      ) : (
        <form onSubmit={handleSubmit} className="space-y-6">
          {/* URL Input */}
          <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-3">
            <label className="block text-sm font-semibold text-white flex items-center gap-2">
              <LinkIcon className="w-4 h-4 text-indigo-400" />
              <span>URL bài báo gốc</span>
              <span className="text-rose-400">*</span>
            </label>
            <div className="relative">
              <input
                type="url"
                required
                value={url}
                onChange={(e) => setUrl(e.target.value)}
                placeholder="https://vnexpress.net/... hoặc https://tuoitre.vn/..."
                className="w-full px-4 py-3.5 rounded-xl bg-[#0B0F19] border border-[#1F2937] text-white placeholder-gray-500 text-sm focus:outline-none focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 transition"
              />
            </div>
            <p className="text-xs text-gray-400">
              Hệ thống hỗ trợ các trang báo uy tín tại Việt Nam (VnExpress, Tuổi Trẻ, Thanh Niên, Dân Trí, VietnamNet, v.v.).
            </p>
          </div>

          {/* Configuration Grid */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {/* AI Editor Options */}
            <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
              <div className="flex items-center space-x-2 text-white font-semibold text-sm">
                <Bot className="w-4 h-4 text-purple-400" />
                <span>AI Biên tập kịch bản</span>
              </div>
              <div className="space-y-3">
                <div>
                  <label className="text-xs text-gray-400 block mb-1.5">Mô hình AI</label>
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
                    <span>Thời lượng video mục tiêu</span>
                    <span className="font-mono text-indigo-400">{targetDuration} giây</span>
                  </div>
                  <input
                    type="range"
                    min={45}
                    max={60}
                    value={targetDuration}
                    onChange={(e) => setTargetDuration(Number(e.target.value))}
                    className="w-full accent-indigo-600 bg-gray-700 h-1.5 rounded-lg appearance-none cursor-pointer"
                  />
                  <div className="flex justify-between text-[10px] text-gray-500 mt-0.5">
                    <span>45s</span>
                    <span>50s</span>
                    <span>60s</span>
                  </div>
                </div>
              </div>
            </div>

            {/* TTS & Voice Options */}
            <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
              <div className="flex items-center space-x-2 text-white font-semibold text-sm">
                <Volume2 className="w-4 h-4 text-pink-400" />
                <span>Giọng đọc & Phụ đề</span>
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
                    <option value="breaking-yellow">Breaking News Vàng / Đen (Nổi bật, dễ đọc)</option>
                    <option value="modern-white">Modern Clean Trắng / Viền tối</option>
                    <option value="high-contrast">Tương phản cao (Đặc biệt cho điện thoại)</option>
                  </select>
                </div>
              </div>
            </div>
          </div>

          {/* Strict Policy Reminder */}
          <div className="p-4 rounded-xl bg-[#111827] border border-gray-800 flex items-start space-x-3 text-xs text-gray-300">
            <ShieldAlert className="w-4 h-4 text-indigo-400 shrink-0 mt-0.5" />
            <p>
              Quy tắc trích xuất: Hệ thống chỉ quét ảnh có trong bài báo tại URL này. Không tìm kiếm thêm ảnh ngoài Internet, không tạo ảnh nhân vật giả bằng AI.
            </p>
          </div>

          {/* Submit Button */}
          <div className="flex justify-end">
            <button
              type="submit"
              disabled={isSubmitting || !url}
              className="px-6 py-3 rounded-xl bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white font-semibold text-sm flex items-center space-x-2 shadow-lg shadow-indigo-600/30 transition-all"
            >
              {isSubmitting ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  <span>Đang khởi tạo job trích xuất...</span>
                </>
              ) : (
                <>
                  <Sparkles className="w-4 h-4" />
                  <span>Bắt đầu trích xuất & tạo video</span>
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
