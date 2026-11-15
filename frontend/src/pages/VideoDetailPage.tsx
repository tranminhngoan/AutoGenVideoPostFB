import React, { useState } from 'react';
import {
  ArrowLeft,
  Play,
  CheckCircle,
  XCircle,
  RefreshCw,
  Share2,
  ShieldCheck,
  Film,
  FileText,
  Sliders,
  CheckSquare
} from 'lucide-react';

interface VideoDetailPageProps {
  projectId: string;
  onBack: () => void;
}

export const VideoDetailPage: React.FC<VideoDetailPageProps> = ({ projectId, onBack }) => {
  const [activeTab, setActiveTab] = useState<'script' | 'scenes' | 'qc'>('qc');
  const [approved, setApproved] = useState(false);

  const scenes = [
    {
      num: 1,
      purpose: 'Hook',
      duration: '6s',
      voice: 'Sáng nay, tuyến metro số 1 Bến Thành - Suối Tiên chính thức mở cửa đón những vị khách đầu tiên sau hơn 10 năm chờ đợi.',
      text: 'METRO SỐ 1 CHÍNH THỨC VẬN HÀNH THƯƠNG MẠI',
      motion: 'ZoomIn',
      img: 'https://images.unsplash.com/photo-1544620347-c4fd4a3d5957?w=400'
    },
    {
      num: 2,
      purpose: 'Fact',
      duration: '8s',
      voice: 'Toàn tuyến dài gần 20 km với 14 ga, kết nối trung tâm Quận 1 tới cửa ngõ TP. Thủ Đức trong chỉ 30 phút.',
      text: 'CHIỀU DÀI 20 KM • 14 GA HIỆN ĐẠI',
      motion: 'PanLeft',
      img: 'https://images.unsplash.com/photo-1517649763962-0c623266ddc0?w=400'
    },
    {
      num: 3,
      purpose: 'Context',
      duration: '7s',
      voice: 'Trong tháng đầu tiên, hành khách được trải nghiệm hoàn toàn miễn phí, mang lại diện mạo giao thông công cộng hiện đại cho đô thị.',
      text: 'MIỄN PHÍ VÉ THÁNG ĐẦU TIÊN CHO TOÀN DÂN',
      motion: 'KenBurns',
      img: 'https://images.unsplash.com/photo-1518770660439-4636190af475?w=400'
    }
  ];

  return (
    <div className="space-y-6 max-w-7xl mx-auto">
      {/* Top Bar Navigation */}
      <div className="flex items-center justify-between">
        <button
          onClick={onBack}
          className="inline-flex items-center space-x-2 text-xs font-semibold text-gray-400 hover:text-white transition"
        >
          <ArrowLeft className="w-4 h-4" />
          <span>Quay lại danh sách video</span>
        </button>
        <div className="flex items-center space-x-3">
          <button
            onClick={() => alert('Yêu cầu render lại video đã được gửi đến Worker!')}
            className="px-3.5 py-2 rounded-xl bg-[#1F2937] hover:bg-[#374151] text-gray-300 text-xs font-medium flex items-center gap-1.5 transition"
          >
            <RefreshCw className="w-3.5 h-3.5" />
            <span>Render lại</span>
          </button>
          <button
            onClick={() => setApproved(!approved)}
            className={`px-4 py-2 rounded-xl text-xs font-semibold flex items-center gap-1.5 transition ${
              approved
                ? 'bg-emerald-600 hover:bg-emerald-500 text-white'
                : 'bg-indigo-600 hover:bg-indigo-500 text-white shadow-lg shadow-indigo-600/30'
            }`}
          >
            <CheckCircle className="w-4 h-4" />
            <span>{approved ? 'Đã duyệt video ✓' : 'Phê duyệt video'}</span>
          </button>
        </div>
      </div>

      {/* Main Review Workspace */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8">
        {/* Left Column: 9:16 Video Preview Mock */}
        <div className="lg:col-span-5 flex flex-col items-center">
          <div className="w-[320px] h-[568px] rounded-3xl bg-black border-4 border-gray-800 shadow-2xl relative overflow-hidden flex flex-col justify-between p-4 group">
            {/* Reel Header */}
            <div className="relative z-10 flex items-center justify-between">
              <span className="px-2 py-0.5 rounded bg-red-600 text-white font-extrabold text-[10px] uppercase tracking-wider">
                TIN NÓNG 24H
              </span>
              <span className="text-[10px] font-mono text-gray-400 bg-black/60 px-2 py-0.5 rounded">
                00:54 / 00:54
              </span>
            </div>

            {/* Video center action */}
            <div className="relative z-10 text-center space-y-2">
              <div className="w-14 h-14 mx-auto rounded-full bg-indigo-600/80 backdrop-blur text-white flex items-center justify-center cursor-pointer hover:scale-110 transition shadow-lg">
                <Play className="w-6 h-6 fill-white ml-0.5" />
              </div>
              <p className="text-[11px] text-gray-300 font-medium">Bấm để phát xem trước 9:16</p>
            </div>

            {/* Simulated Animated Subtitle & Attribution */}
            <div className="relative z-10 space-y-3">
              <div className="p-2.5 rounded-xl bg-black/80 backdrop-blur border border-amber-500/40 text-center">
                <span className="text-amber-300 font-extrabold text-xs tracking-wide">
                  METRO SỐ 1 CHÍNH THỨC VẬN HÀNH THƯƠNG MẠI
                </span>
              </div>
              <div className="flex items-center justify-between text-[10px] text-gray-400 px-1">
                <span>Nguồn: VnExpress</span>
                <span>FFmpeg 1080×1920</span>
              </div>
            </div>

            {/* Temporary background */}
            <div className="absolute inset-0 bg-gradient-to-t from-black via-gray-900 to-black opacity-90" />
          </div>

          <p className="text-xs text-gray-500 mt-3 text-center">
            * File video tạm sẽ tự động dọn dẹp sau khi đăng hoặc hết hạn xem trước.
          </p>
        </div>

        {/* Right Column: Editorial & QC Inspection */}
        <div className="lg:col-span-7 space-y-6">
          {/* Sub Navigation */}
          <div className="flex space-x-2 border-b border-[#1F2937] pb-3 text-xs font-semibold">
            <button
              onClick={() => setActiveTab('qc')}
              className={`px-3 py-1.5 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'qc' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              <CheckSquare className="w-3.5 h-3.5" />
              <span>Kiểm định chất lượng (QC)</span>
            </button>
            <button
              onClick={() => setActiveTab('scenes')}
              className={`px-3 py-1.5 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'scenes' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              <Film className="w-3.5 h-3.5" />
              <span>Phân cảnh (Storyboard)</span>
            </button>
            <button
              onClick={() => setActiveTab('script')}
              className={`px-3 py-1.5 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'script' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              <FileText className="w-3.5 h-3.5" />
              <span>Kịch bản tin tức</span>
            </button>
          </div>

          {/* QC Tab */}
          {activeTab === 'qc' && (
            <div className="space-y-4">
              <div className="glass-panel p-5 rounded-2xl border border-[#1F2937] space-y-3">
                <h3 className="font-bold text-sm text-white flex items-center gap-2">
                  <ShieldCheck className="w-4 h-4 text-emerald-400" />
                  Báo cáo kiểm soát chất lượng nội bộ (10 tiêu chí)
                </h3>
                <div className="space-y-2.5 text-xs">
                  {[
                    { label: 'Nội dung bám sát bài báo gốc', pass: true },
                    { label: 'Không bịa số liệu hoặc sự kiện', pass: true },
                    { label: 'Không sai tên hoặc danh tính nhân vật', pass: true },
                    { label: 'Chỉ sử dụng hình ảnh nằm trong chính bài báo', pass: true },
                    { label: 'Không có ảnh vi phạm quyền sử dụng', pass: true },
                    { label: 'Phụ đề tiếng Việt đồng bộ với giọng đọc', pass: true },
                    { label: 'Âm thanh tự nhiên, không bị rè hoặc mất tiếng', pass: true },
                    { label: 'Không có cảnh trống (blank scene)', pass: true },
                    { label: 'Video chuẩn tỷ lệ 9:16 (1080×1920) và thời lượng 45-60s', pass: true },
                    { label: 'Có giá trị biên tập, tóm tắt và phân tích riêng', pass: true },
                  ].map((item, idx) => (
                    <div key={idx} className="flex items-center justify-between p-2 rounded-lg bg-[#0B0F19]">
                      <span className="text-gray-300">{item.label}</span>
                      <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-emerald-500/20 text-emerald-300 flex items-center gap-1">
                        <CheckCircle className="w-3 h-3" /> Đạt
                      </span>
                    </div>
                  ))}
                </div>
              </div>

              {/* Publish Action Box */}
              <div className="p-4 rounded-xl bg-indigo-950/30 border border-indigo-500/30 flex items-center justify-between">
                <div>
                  <p className="font-bold text-sm text-white">Xuất bản lên Facebook Reels</p>
                  <p className="text-xs text-indigo-300">
                    {approved
                      ? 'Video đã được bạn phê duyệt, sẵn sàng đẩy lên Fanpage.'
                      : 'Cần bấm "Phê duyệt video" phía trên trước khi xuất bản.'}
                  </p>
                </div>
                <button
                  disabled={!approved}
                  onClick={() => alert('Bắt đầu quy trình upload video binary lên Meta Reels!')}
                  className="px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white font-semibold text-xs flex items-center gap-1.5 transition"
                >
                  <Share2 className="w-3.5 h-3.5" />
                  <span>Đăng ngay lên Fanpage</span>
                </button>
              </div>
            </div>
          )}

          {/* Scenes Tab */}
          {activeTab === 'scenes' && (
            <div className="space-y-4">
              {scenes.map((s) => (
                <div key={s.num} className="glass-panel p-4 rounded-xl border border-[#1F2937] space-y-3">
                  <div className="flex items-center justify-between text-xs">
                    <span className="font-mono font-bold text-indigo-400">CẢNH #{s.num} ({s.duration})</span>
                    <span className="px-2 py-0.5 rounded bg-gray-800 text-gray-300 font-semibold">{s.purpose}</span>
                  </div>
                  <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                    <div className="h-20 rounded-lg overflow-hidden bg-gray-900">
                      <img src={s.img} alt={`Scene ${s.num}`} className="w-full h-full object-cover" />
                    </div>
                    <div className="md:col-span-3 space-y-1 text-xs">
                      <p className="text-gray-300 font-medium">🗣️ Giọng đọc: {s.voice}</p>
                      <p className="text-amber-300 font-mono text-[11px]">📺 Chữ hiển thị: {s.text}</p>
                      <p className="text-gray-500 text-[10px]">Hiệu ứng: {s.motion} • Chuyển cảnh: Fade</p>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}

          {/* Script Tab */}
          {activeTab === 'script' && (
            <div className="glass-panel p-5 rounded-2xl border border-[#1F2937] space-y-4 text-xs">
              <div>
                <h4 className="font-bold text-white mb-2 text-sm">Tiêu đề video biên tập:</h4>
                <p className="p-3 rounded-xl bg-[#0B0F19] text-indigo-300 font-medium border border-gray-800">
                  Metro số 1 chính thức đón khách: Dấu mốc giao thông TP.HCM
                </p>
              </div>
              <div>
                <h4 className="font-bold text-white mb-2 text-sm">Toàn văn kịch bản lồng tiếng (Voice-over):</h4>
                <div className="p-4 rounded-xl bg-[#0B0F19] text-gray-300 leading-relaxed font-mono space-y-2 border border-gray-800">
                  <p>Sáng nay, tuyến metro số 1 Bến Thành - Suối Tiên chính thức mở cửa đón những vị khách đầu tiên sau hơn 10 năm chờ đợi.</p>
                  <p>Toàn tuyến dài gần 20 km với 14 ga hiện đại, kết nối trung tâm Quận 1 tới cửa ngõ TP. Thủ Đức trong chỉ 30 phút.</p>
                  <p>Trong tháng đầu tiên, hành khách được trải nghiệm hoàn toàn miễn phí, mang lại diện mạo giao thông công cộng hiện đại cho đô thị.</p>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
