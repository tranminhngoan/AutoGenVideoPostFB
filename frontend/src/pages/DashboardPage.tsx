import React from 'react';
import {
  Film,
  PlusCircle,
  Play,
  CheckCircle2,
  AlertCircle,
  TrendingUp,
  Cpu,
  Layers,
  Sparkles,
  ArrowRight
} from 'lucide-react';
import { SystemConfig } from '../types';

interface DashboardPageProps {
  onNavigate: (tab: string) => void;
  config: SystemConfig | null;
  apiHealthy: boolean;
}

export const DashboardPage: React.FC<DashboardPageProps> = ({ onNavigate, config, apiHealthy }) => {
  return (
    <div className="space-y-8 max-w-7xl mx-auto">
      {/* Welcome Banner */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-r from-indigo-900/60 via-purple-900/40 to-[#111827] border border-indigo-500/20 p-8 shadow-2xl">
        <div className="relative z-10 max-w-2xl space-y-4">
          <div className="inline-flex items-center space-x-2 px-3 py-1 rounded-full bg-indigo-500/20 border border-indigo-400/30 text-indigo-300 text-xs font-semibold">
            <Sparkles className="w-3.5 h-3.5" />
            <span>AI News Video Studio Engine</span>
          </div>
          <h1 className="text-3xl font-extrabold text-white tracking-tight leading-tight">
            Chuyển bài báo thành video Reels chuyên nghiệp trong 60 giây
          </h1>
          <p className="text-sm text-gray-300 leading-relaxed">
            Hệ thống tự động đọc báo, trích xuất ảnh thực tế, viết kịch bản biên tập tin tức,
            lồng tiếng tiếng Việt và render video dọc 9:16 (1080×1920) sẵn sàng đăng Facebook Reels.
          </p>
          <div className="pt-2 flex items-center space-x-4">
            <button
              onClick={() => onNavigate('create')}
              className="inline-flex items-center space-x-2 px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-sm shadow-lg shadow-indigo-600/30 transition-all hover:scale-[1.02]"
            >
              <PlusCircle className="w-4 h-4" />
              <span>Tạo video mới ngay</span>
              <ArrowRight className="w-4 h-4" />
            </button>
            <button
              onClick={() => onNavigate('videos')}
              className="inline-flex items-center space-x-2 px-4 py-2.5 rounded-xl bg-[#1F2937] hover:bg-[#374151] text-gray-200 font-medium text-sm border border-gray-700 transition"
            >
              <Film className="w-4 h-4" />
              <span>Xem danh sách video</span>
            </button>
          </div>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-5">
        <div className="glass-panel p-5 rounded-xl border border-[#1F2937]">
          <div className="flex items-center justify-between text-gray-400 mb-3">
            <span className="text-xs font-semibold uppercase tracking-wider">Video đã tạo</span>
            <Film className="w-4 h-4 text-indigo-400" />
          </div>
          <div className="text-2xl font-bold text-white">24</div>
          <p className="text-xs text-emerald-400 mt-2 flex items-center gap-1">
            <TrendingUp className="w-3.5 h-3.5" />
            <span>+8 trong tuần này</span>
          </p>
        </div>

        <div className="glass-panel p-5 rounded-xl border border-[#1F2937]">
          <div className="flex items-center justify-between text-gray-400 mb-3">
            <span className="text-xs font-semibold uppercase tracking-wider">Thời lượng TB</span>
            <Play className="w-4 h-4 text-purple-400" />
          </div>
          <div className="text-2xl font-bold text-white">52s</div>
          <p className="text-xs text-gray-400 mt-2 font-mono">
            Chuẩn mục tiêu 45–60s
          </p>
        </div>

        <div className="glass-panel p-5 rounded-xl border border-[#1F2937]">
          <div className="flex items-center justify-between text-gray-400 mb-3">
            <span className="text-xs font-semibold uppercase tracking-wider">Đã duyệt & Đăng</span>
            <CheckCircle2 className="w-4 h-4 text-emerald-400" />
          </div>
          <div className="text-2xl font-bold text-white">19</div>
          <p className="text-xs text-indigo-300 mt-2">
            100% duyệt bởi biên tập viên
          </p>
        </div>

        <div className="glass-panel p-5 rounded-xl border border-[#1F2937]">
          <div className="flex items-center justify-between text-gray-400 mb-3">
            <span className="text-xs font-semibold uppercase tracking-wider">Hạ tầng Pipeline</span>
            <Cpu className="w-4 h-4 text-amber-400" />
          </div>
          <div className="text-2xl font-bold text-white">
            {apiHealthy ? 'Ready' : 'Checking'}
          </div>
          <p className="text-xs text-gray-400 mt-2 font-mono truncate">
            {config?.environment || 'Development'} mode
          </p>
        </div>
      </div>

      {/* Visual Pipeline Flow */}
      <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
        <h3 className="text-sm font-bold uppercase tracking-wider text-gray-400 flex items-center gap-2">
          <Layers className="w-4 h-4 text-indigo-400" />
          Quy trình sản xuất tự động 9:16 Reels
        </h3>
        <div className="grid grid-cols-2 md:grid-cols-7 gap-3 text-center">
          {[
            { step: '1', title: 'URL Báo', desc: 'Dán link bài báo', color: 'border-blue-500/40 text-blue-400' },
            { step: '2', title: 'Ảnh Trong Bài', desc: 'Chỉ ảnh bài báo', color: 'border-indigo-500/40 text-indigo-400' },
            { step: '3', title: 'AI Biên tập', desc: 'Kịch bản 45-60s', color: 'border-purple-500/40 text-purple-400' },
            { step: '4', title: 'TTS Tiếng Việt', desc: 'Hoài My / Nam Minh', color: 'border-pink-500/40 text-pink-400' },
            { step: '5', title: 'FFmpeg 9:16', desc: 'Dựng dọc 1080x1920', color: 'border-amber-500/40 text-amber-400' },
            { step: '6', title: 'Kiểm duyệt QC', desc: 'Xem trước & duyệt', color: 'border-emerald-500/40 text-emerald-400' },
            { step: '7', title: 'Meta Reels', desc: 'Đăng Page tự động', color: 'border-cyan-500/40 text-cyan-400' },
          ].map((s) => (
            <div key={s.step} className={`p-3 rounded-xl bg-[#111827] border ${s.color} space-y-1`}>
              <div className="w-5 h-5 mx-auto rounded-full bg-gray-800 text-xs font-mono font-bold flex items-center justify-center">
                {s.step}
              </div>
              <p className="text-xs font-bold text-white">{s.title}</p>
              <p className="text-[11px] text-gray-400">{s.desc}</p>
            </div>
          ))}
        </div>
      </div>

      {/* Strict Policy Box */}
      <div className="p-4 rounded-xl bg-amber-950/20 border border-amber-500/30 flex items-start space-x-3 text-amber-200 text-xs">
        <AlertCircle className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
        <div className="space-y-1">
          <p className="font-semibold text-amber-300">Quy tắc nghiệp vụ cố định:</p>
          <p className="text-amber-200/80 leading-relaxed">
            Hệ thống chỉ trích xuất hình ảnh nằm trong chính bài báo. Tuyệt đối không tìm ảnh ngoài Internet,
            không dùng ảnh AI, không tạo khuôn mặt giả. Không lưu MP4/ảnh/audio lâu dài (tự động xóa sau khi xử lý).
            Tất cả video phải được người dùng duyệt trước khi xuất bản lên Facebook Page.
          </p>
        </div>
      </div>
    </div>
  );
};
