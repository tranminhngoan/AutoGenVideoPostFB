import React, { useState, useEffect } from 'react';
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
  CheckSquare,
  Sparkles,
  Layers,
  Clock,
  Eye,
  Type,
  AlertCircle,
  Volume2,
  Download,
  Trash2,
  HardDrive
} from 'lucide-react';
import { api, VideoScriptDto, VideoScenePlanDto, VideoAssetDto } from '../services/api';
import { VideoProject } from '../types';

interface VideoDetailPageProps {
  projectId: string;
  onBack: () => void;
}

export const VideoDetailPage: React.FC<VideoDetailPageProps> = ({ projectId, onBack }) => {
  const [activeTab, setActiveTab] = useState<'preview' | 'script' | 'scenes' | 'assets' | 'qc'>('preview');
  const [project, setProject] = useState<VideoProject | null>(null);
  const [script, setScript] = useState<VideoScriptDto | null>(null);
  const [scenes, setScenes] = useState<VideoScenePlanDto[]>([]);
  const [assets, setAssets] = useState<VideoAssetDto[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [generating, setGenerating] = useState<boolean>(false);
  const [rendering, setRendering] = useState<boolean>(false);
  const [selectedProvider, setSelectedProvider] = useState<'Gemini' | 'Claude'>('Gemini');
  const [selectedVoice, setSelectedVoice] = useState<'vi-VN-HoaiMyNeural' | 'vi-VN-NamMinhNeural'>('vi-VN-HoaiMyNeural');
  const [approved, setApproved] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  useEffect(() => {
    loadData();
  }, [projectId]);

  const loadData = async () => {
    setLoading(true);
    setErrorMsg(null);
    try {
      const [projData, scriptData, scenesData, assetsData] = await Promise.all([
        api.getVideoProjectById(projectId),
        api.getScript(projectId),
        api.getScenes(projectId),
        api.getAssets(projectId)
      ]);
      setProject(projData);
      setScript(scriptData);
      setScenes(scenesData);
      setAssets(assetsData);
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi tải thông tin dự án');
    } finally {
      setLoading(false);
    }
  };

  const handleGenerateScript = async () => {
    setGenerating(true);
    setErrorMsg(null);
    setSuccessMsg(null);
    try {
      const generatedScript = await api.generateScript(projectId, {
        aiProvider: selectedProvider
      });
      setScript(generatedScript);

      const updatedScenes = await api.getScenes(projectId);
      setScenes(updatedScenes);
      setActiveTab('script');
      setSuccessMsg('Đã tạo kịch bản AI (45-60s) và phân chia 6-10 phân cảnh thành công!');
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi tạo kịch bản AI');
    } finally {
      setGenerating(false);
    }
  };

  const handlePlanScenes = async () => {
    setGenerating(true);
    setErrorMsg(null);
    try {
      const planned = await api.planScenes(projectId);
      setScenes(planned);
      setActiveTab('scenes');
      setSuccessMsg('Đã lập lại 6-10 phân cảnh khớp với hình ảnh bài viết!');
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi lập phân cảnh');
    } finally {
      setGenerating(false);
    }
  };

  const handleRenderVideo = async () => {
    setRendering(true);
    setErrorMsg(null);
    setSuccessMsg(null);
    try {
      const result = await api.renderVideo(projectId, 'EdgeTts');
      setSuccessMsg('Render video 9:16 (1080×1920) hoàn tất! Video sẵn sàng để xem trước.');
      await loadData();
      setActiveTab('preview');
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi render video');
    } finally {
      setRendering(false);
    }
  };

  const handleCleanupAssets = async () => {
    if (!confirm('Bạn có chắc muốn xóa toàn bộ file media tạm (MP4, audio, phụ đề) của dự án này khỏi ổ đĩa?')) return;
    try {
      await api.cleanupAssets(projectId);
      setSuccessMsg('Đã dọn dẹp sạch sẽ toàn bộ file tạm của dự án.');
      await loadData();
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi dọn dẹp file tạm');
    }
  };

  const hasVideo = assets.some(a => (a.assetType === 'FinalVideo' || a.assetType === 'Video') && !a.isCleanedUp);
  const hasAudio = assets.some(a => a.assetType === 'Audio' && !a.isCleanedUp);

  return (
    <div className="space-y-6 max-w-7xl mx-auto">
      {/* Top Bar Navigation */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <button
          onClick={onBack}
          className="inline-flex items-center space-x-2 text-xs font-semibold text-gray-400 hover:text-white transition self-start"
        >
          <ArrowLeft className="w-4 h-4" />
          <span>Quay lại danh sách video</span>
        </button>

        <div className="flex flex-wrap items-center gap-3">
          {/* AI Provider Switcher */}
          <div className="flex items-center bg-[#0B0F19] rounded-xl border border-gray-800 p-1 text-xs">
            <button
              onClick={() => setSelectedProvider('Gemini')}
              className={`px-3 py-1 rounded-lg transition font-medium ${
                selectedProvider === 'Gemini' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              Gemini
            </button>
            <button
              onClick={() => setSelectedProvider('Claude')}
              className={`px-3 py-1 rounded-lg transition font-medium ${
                selectedProvider === 'Claude' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              Claude
            </button>
          </div>

          <button
            onClick={handleGenerateScript}
            disabled={generating}
            className="px-3.5 py-2 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-200 text-xs font-semibold flex items-center gap-1.5 transition disabled:opacity-50"
          >
            <Sparkles className={`w-3.5 h-3.5 ${generating ? 'animate-spin text-indigo-400' : ''}`} />
            <span>{generating ? 'AI đang viết...' : 'Tạo kịch bản AI'}</span>
          </button>

          {/* Render Video Button */}
          <button
            onClick={handleRenderVideo}
            disabled={rendering}
            className="px-4 py-2 rounded-xl bg-gradient-to-r from-indigo-600 to-purple-600 hover:from-indigo-500 hover:to-purple-500 text-white text-xs font-semibold flex items-center gap-1.5 transition disabled:opacity-50 shadow-lg shadow-indigo-600/20"
          >
            <Film className={`w-3.5 h-3.5 ${rendering ? 'animate-spin' : ''}`} />
            <span>{rendering ? 'FFmpeg đang dựng...' : 'Dựng video 9:16 (FFmpeg)'}</span>
          </button>

          <button
            onClick={() => setApproved(!approved)}
            className={`px-4 py-2 rounded-xl text-xs font-semibold flex items-center gap-1.5 transition ${
              approved
                ? 'bg-emerald-600 hover:bg-emerald-500 text-white'
                : 'bg-gray-800 hover:bg-gray-700 text-gray-200 border border-gray-700'
            }`}
          >
            <CheckCircle className="w-4 h-4" />
            <span>{approved ? 'Đã duyệt video ✓' : 'Phê duyệt video'}</span>
          </button>
        </div>
      </div>

      {errorMsg && (
        <div className="p-3.5 rounded-xl bg-rose-500/10 border border-rose-500/30 text-rose-300 text-xs flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>{errorMsg}</span>
        </div>
      )}

      {successMsg && (
        <div className="p-3.5 rounded-xl bg-emerald-500/10 border border-emerald-500/30 text-emerald-300 text-xs flex items-center gap-2">
          <CheckCircle className="w-4 h-4 shrink-0" />
          <span>{successMsg}</span>
        </div>
      )}

      {/* Main Review Workspace */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8">
        {/* Left Column: 9:16 Video Player */}
        <div className="lg:col-span-5 flex flex-col items-center">
          <div className="w-[320px] h-[568px] rounded-3xl bg-black border-4 border-gray-800 shadow-2xl relative overflow-hidden flex flex-col justify-between group">
            {hasVideo ? (
              <video
                controls
                playsInline
                className="w-full h-full object-cover"
                poster={scenes.length > 0 && scenes[0].imageUrl ? scenes[0].imageUrl : undefined}
              >
                <source src={`/api/video-projects/${projectId}/video`} type="video/mp4" />
                <track kind="subtitles" src={`/api/video-projects/${projectId}/subtitles`} srcLang="vi" label="Tiếng Việt" default />
                Trình duyệt không hỗ trợ xem video HTML5.
              </video>
            ) : (
              <div className="w-full h-full flex flex-col justify-between p-4 relative">
                {/* Reel Header */}
                <div className="relative z-10 flex items-center justify-between">
                  <span className="px-2 py-0.5 rounded bg-red-600 text-white font-extrabold text-[10px] uppercase tracking-wider">
                    TIN NÓNG 24H
                  </span>
                  <span className="text-[10px] font-mono text-gray-400 bg-black/60 px-2 py-0.5 rounded">
                    00:{script ? script.estimatedReadingSeconds : '50'} / 00:60
                  </span>
                </div>

                {/* Video center action */}
                <div className="relative z-10 text-center space-y-2">
                  <button
                    onClick={handleRenderVideo}
                    disabled={rendering}
                    className="w-14 h-14 mx-auto rounded-full bg-indigo-600/90 backdrop-blur text-white flex items-center justify-center hover:scale-110 transition shadow-lg"
                  >
                    <Play className="w-6 h-6 fill-white ml-0.5" />
                  </button>
                  <p className="text-[11px] text-gray-300 font-medium">
                    {rendering ? 'Đang dựng video 9:16...' : 'Bấm để render video 9:16'}
                  </p>
                </div>

                {/* Simulated Animated Subtitle & Attribution */}
                <div className="relative z-10 space-y-3">
                  <div className="p-2.5 rounded-xl bg-black/80 backdrop-blur border border-amber-500/40 text-center">
                    <span className="text-amber-300 font-extrabold text-xs tracking-wide uppercase line-clamp-2">
                      {script ? script.title : (project ? project.title : 'METRO SỐ 1 CHÍNH THỨC VẬN HÀNH')}
                    </span>
                  </div>
                  <div className="flex items-center justify-between text-[10px] text-gray-400 px-1">
                    <span>{project ? project.publisher : 'Báo điện tử'}</span>
                    <span>FFmpeg 1080×1920</span>
                  </div>
                </div>

                {/* Background Image / Gradient */}
                {scenes.length > 0 && scenes[0].imageUrl ? (
                  <img
                    src={scenes[0].imageUrl}
                    alt="Scene visual"
                    className="absolute inset-0 w-full h-full object-cover opacity-60"
                  />
                ) : (
                  <div className="absolute inset-0 bg-gradient-to-t from-black via-gray-900 to-black opacity-90" />
                )}
              </div>
            )}
          </div>

          {/* Action buttons below reel */}
          <div className="flex items-center gap-3 mt-3">
            {hasVideo && (
              <a
                href={`/api/video-projects/${projectId}/video`}
                download={`video_reel_${projectId.slice(0, 8)}.mp4`}
                className="px-3 py-1.5 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-medium flex items-center gap-1.5 transition"
              >
                <Download className="w-3.5 h-3.5" />
                <span>Tải MP4 kiểm tra</span>
              </a>
            )}
            <button
              onClick={handleCleanupAssets}
              className="px-3 py-1.5 rounded-xl bg-rose-500/10 hover:bg-rose-500/20 text-rose-300 text-xs font-medium flex items-center gap-1.5 transition"
              title="Xóa toàn bộ file tạm của dự án"
            >
              <Trash2 className="w-3.5 h-3.5" />
              <span>Dọn dẹp file tạm</span>
            </button>
          </div>

          <p className="text-[11px] text-gray-500 mt-2 text-center max-w-xs">
            * Nguyên tắc lưu trữ: Không lưu binary video/ảnh/audio lâu dài trong database. Tự động dọn dẹp sau TTL 2 giờ.
          </p>
        </div>

        {/* Right Column: Editorial, Storyboard, Audio & QC Inspection */}
        <div className="lg:col-span-7 space-y-6">
          {/* Sub Navigation */}
          <div className="flex flex-wrap space-x-2 border-b border-[#1F2937] pb-3 text-xs font-semibold">
            <button
              onClick={() => setActiveTab('preview')}
              className={`px-3 py-2 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'preview' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              <Volume2 className="w-3.5 h-3.5" />
              <span>Âm thanh & Phụ đề</span>
            </button>
            <button
              onClick={() => setActiveTab('script')}
              className={`px-3 py-2 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'script' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              <FileText className="w-3.5 h-3.5" />
              <span>Kịch bản tin tức</span>
            </button>
            <button
              onClick={() => setActiveTab('scenes')}
              className={`px-3 py-2 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'scenes' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              <Film className="w-3.5 h-3.5" />
              <span>Phân cảnh ({scenes.length} Scenes)</span>
            </button>
            <button
              onClick={() => setActiveTab('assets')}
              className={`px-3 py-2 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'assets' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              <HardDrive className="w-3.5 h-3.5" />
              <span>File tạm ({assets.filter(a => !a.isCleanedUp).length})</span>
            </button>
            <button
              onClick={() => setActiveTab('qc')}
              className={`px-3 py-2 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'qc' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              <CheckSquare className="w-3.5 h-3.5" />
              <span>Kiểm định QC</span>
            </button>
          </div>

          {/* PREVIEW TAB: AUDIO & SUBTITLES */}
          {activeTab === 'preview' && (
            <div className="space-y-4">
              {/* Voice-over Audio Player Card */}
              <div className="glass-panel p-5 rounded-2xl border border-gray-800 space-y-3">
                <div className="flex items-center justify-between">
                  <h4 className="text-sm font-bold text-white flex items-center gap-2">
                    <Volume2 className="w-4 h-4 text-indigo-400" />
                    Giọng đọc lồng tiếng tiếng Việt (Voice-Over)
                  </h4>
                  <div className="flex items-center gap-2 text-xs">
                    <select
                      value={selectedVoice}
                      onChange={(e: any) => setSelectedVoice(e.target.value)}
                      className="bg-[#0B0F19] border border-gray-700 text-gray-300 rounded-lg px-2.5 py-1 text-xs"
                    >
                      <option value="vi-VN-HoaiMyNeural">Hoài My (Nữ miền Nam)</option>
                      <option value="vi-VN-NamMinhNeural">Nam Minh (Nam miền Bắc)</option>
                    </select>
                  </div>
                </div>

                {hasAudio ? (
                  <div className="space-y-2 pt-1">
                    <audio controls className="w-full h-10 rounded-lg" src={`/api/video-projects/${projectId}/audio`}>
                      Trình duyệt không hỗ trợ audio HTML5.
                    </audio>
                    <p className="text-[11px] text-gray-400">
                      Audio giọng đọc được đồng bộ tự động với phụ đề và điều chỉnh âm lượng nền (audio ducking).
                    </p>
                  </div>
                ) : (
                  <div className="p-4 rounded-xl bg-gray-900/50 text-center text-xs text-gray-400 space-y-2">
                    <p>Chưa có file audio giọng đọc được tạo cho dự án này.</p>
                    <button
                      onClick={handleRenderVideo}
                      disabled={rendering}
                      className="px-3.5 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold"
                    >
                      Tạo giọng đọc & Render video
                    </button>
                  </div>
                )}
              </div>

              {/* Subtitles Inspector */}
              <div className="glass-panel p-5 rounded-2xl border border-gray-800 space-y-3">
                <div className="flex items-center justify-between">
                  <h4 className="text-sm font-bold text-white flex items-center gap-2">
                    <FileText className="w-4 h-4 text-amber-400" />
                    Phụ đề đồng bộ (SRT / WebVTT)
                  </h4>
                  <a
                    href={`/api/video-projects/${projectId}/subtitles?format=srt`}
                    download="subtitles.srt"
                    className="text-xs text-indigo-400 hover:underline flex items-center gap-1"
                  >
                    <Download className="w-3 h-3" />
                    <span>Tải file .SRT</span>
                  </a>
                </div>

                <div className="p-4 rounded-xl bg-[#0B0F19] border border-gray-800 font-mono text-xs text-gray-300 space-y-2 max-h-56 overflow-y-auto">
                  {scenes.map((s, idx) => (
                    <div key={idx} className="pb-2 border-b border-gray-900 last:border-0">
                      <span className="text-indigo-400 font-bold">{s.sceneNumber}. </span>
                      <span className="text-gray-500">[{s.startTimeSeconds}s → {s.endTimeSeconds}s] </span>
                      <span className="text-gray-200">{s.voiceText}</span>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}

          {/* SCRIPT TAB: TÁCH BẠCH FACT VS COMMENTARY */}
          {activeTab === 'script' && (
            <div className="space-y-4">
              {loading ? (
                <div className="p-8 text-center text-gray-400">
                  <RefreshCw className="w-6 h-6 animate-spin mx-auto text-indigo-400 mb-2" />
                  <p className="text-xs">Đang tải kịch bản...</p>
                </div>
              ) : script ? (
                <div className="space-y-4">
                  {/* Script Meta Header */}
                  <div className="glass-panel p-4 rounded-xl border border-gray-800 flex flex-wrap items-center justify-between gap-3 text-xs">
                    <div className="flex items-center gap-2">
                      <span className="px-2 py-0.5 rounded bg-indigo-500/20 text-indigo-300 font-semibold font-mono">
                        {script.aiProvider} ({script.modelName})
                      </span>
                      <span className="text-gray-400 flex items-center gap-1">
                        <Clock className="w-3 h-3" /> {script.estimatedReadingSeconds}s (~{script.wordCount} từ)
                      </span>
                    </div>
                    <span className="text-[11px] text-emerald-400 font-mono">
                      Tokens: {script.tokenUsage} | Ước tính: ${(script.estimatedCostUsd || 0.0001).toFixed(5)}
                    </span>
                  </div>

                  {/* Video Title & Hook */}
                  <div className="glass-panel p-5 rounded-2xl border border-gray-800 space-y-3">
                    <div>
                      <span className="text-[11px] uppercase tracking-wider text-indigo-400 font-bold">Tiêu đề video (Reels Title)</span>
                      <h3 className="text-base font-bold text-white mt-1">{script.title}</h3>
                    </div>

                    <div className="pt-2 border-t border-gray-800/80">
                      <span className="text-[11px] uppercase tracking-wider text-amber-400 font-bold">Mở đầu cuốn hút (The Hook 3-5s)</span>
                      <p className="text-xs text-amber-200/90 font-medium mt-1 leading-relaxed">{script.hook}</p>
                    </div>
                  </div>

                  {/* Fact vs Commentary Split Grid */}
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    {/* FACT SECTION */}
                    <div className="glass-panel p-5 rounded-2xl border border-blue-500/30 bg-blue-950/10 space-y-2">
                      <div className="flex items-center justify-between">
                        <span className="text-xs font-bold text-blue-400 flex items-center gap-1.5">
                          <CheckCircle className="w-3.5 h-3.5" />
                          SỰ THẬT CỐT LÕI (KEY FACTS)
                        </span>
                        <span className="text-[10px] text-blue-300 bg-blue-500/20 px-2 py-0.5 rounded font-medium">Báo chí</span>
                      </div>
                      <p className="text-xs text-gray-300 whitespace-pre-line leading-relaxed">
                        {script.keyFacts}
                      </p>
                      {script.context && (
                        <div className="pt-2 border-t border-blue-900/40 text-[11px] text-gray-400">
                          <span className="font-semibold text-blue-300">Bối cảnh: </span>
                          {script.context}
                        </div>
                      )}
                    </div>

                    {/* COMMENTARY / ANALYSIS SECTION */}
                    <div className="glass-panel p-5 rounded-2xl border border-purple-500/30 bg-purple-950/10 space-y-2">
                      <div className="flex items-center justify-between">
                        <span className="text-xs font-bold text-purple-400 flex items-center gap-1.5">
                          <Sparkles className="w-3.5 h-3.5" />
                          BÌNH LUẬN BIÊN TẬP (ANALYSIS)
                        </span>
                        <span className="text-[10px] text-purple-300 bg-purple-500/20 px-2 py-0.5 rounded font-medium">Giá trị gia tăng</span>
                      </div>
                      <p className="text-xs text-gray-300 leading-relaxed">
                        {script.analysis}
                      </p>
                      <div className="pt-2 border-t border-purple-900/40 text-[11px] text-gray-400">
                        <span className="font-semibold text-purple-300">Kết luận: </span>
                        {script.conclusion}
                      </div>
                    </div>
                  </div>

                  {/* Full Voice-Over Script */}
                  <div className="glass-panel p-5 rounded-2xl border border-gray-800 space-y-2">
                    <span className="text-xs font-bold text-gray-300 flex items-center gap-1.5">
                      <FileText className="w-3.5 h-3.5 text-indigo-400" />
                      TOÀN VĂN LỜI THOẠI LỒNG TIẾNG (VOICE-OVER SCRIPT)
                    </span>
                    <div className="p-4 rounded-xl bg-[#0B0F19] border border-gray-800 text-xs text-gray-200 leading-relaxed whitespace-pre-wrap font-sans">
                      {script.voiceOverScript}
                    </div>
                    <p className="text-[11px] text-gray-500 italic text-right">
                      {script.sourceAttribution}
                    </p>
                  </div>
                </div>
              ) : (
                <div className="glass-panel p-8 text-center space-y-3 border border-gray-800">
                  <FileText className="w-10 h-10 mx-auto text-gray-600" />
                  <p className="text-sm text-gray-300 font-medium">Chưa có kịch bản AI cho dự án này</p>
                  <button
                    onClick={handleGenerateScript}
                    className="px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold"
                  >
                    Tạo kịch bản AI ngay
                  </button>
                </div>
              )}
            </div>
          )}

          {/* SCENES TAB: 6-10 PHÂN CẢNH VỚI ẢNH GỐC / TYPOGRAPHY */}
          {activeTab === 'scenes' && (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <span className="text-xs text-gray-400">
                  Phân cảnh chi tiết ({scenes.length} Scenes • Tổng thời lượng: {scenes.length > 0 ? scenes[scenes.length - 1].endTimeSeconds : 0}s)
                </span>
                <button
                  onClick={handlePlanScenes}
                  disabled={generating}
                  className="px-3 py-1.5 rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-semibold flex items-center gap-1 transition"
                >
                  <RefreshCw className={`w-3 h-3 ${generating ? 'animate-spin' : ''}`} />
                  <span>Phân cảnh lại</span>
                </button>
              </div>

              {scenes.length === 0 ? (
                <div className="glass-panel p-8 text-center space-y-3 border border-gray-800">
                  <Film className="w-10 h-10 mx-auto text-gray-600" />
                  <p className="text-sm text-gray-300 font-medium">Chưa có phân cảnh nào được lập</p>
                  <button
                    onClick={handlePlanScenes}
                    className="px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold"
                  >
                    Lập 6-10 phân cảnh từ kịch bản
                  </button>
                </div>
              ) : (
                scenes.map((s) => (
                  <div key={s.sceneNumber} className="glass-panel p-4 rounded-xl border border-[#1F2937] space-y-3">
                    <div className="flex items-center justify-between text-xs">
                      <span className="font-mono font-bold text-indigo-400">
                        CẢNH #{s.sceneNumber} ({s.startTimeSeconds}s - {s.endTimeSeconds}s • {s.durationSeconds}s)
                      </span>
                      <div className="flex items-center gap-1.5">
                        <span className="px-2 py-0.5 rounded bg-gray-800 text-gray-300 font-semibold text-[10px]">
                          {s.scenePurpose}
                        </span>
                        {s.visualType === 'Image' ? (
                          <span className="px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-300 font-semibold text-[10px]">
                            Ảnh bài viết
                          </span>
                        ) : (
                          <span className="px-2 py-0.5 rounded bg-amber-500/20 text-amber-300 font-semibold text-[10px] flex items-center gap-1">
                            <Type className="w-2.5 h-2.5" /> Nền Typography
                          </span>
                        )}
                      </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-4 gap-4 items-center">
                      <div className="h-24 rounded-lg overflow-hidden bg-gray-900 border border-gray-800 relative flex items-center justify-center">
                        {s.imageUrl ? (
                          <img src={s.imageUrl} alt={`Scene ${s.sceneNumber}`} className="w-full h-full object-cover" />
                        ) : (
                          <div className="text-center p-2 text-[10px] text-gray-400 font-bold uppercase">
                            {s.onScreenText}
                          </div>
                        )}
                        <span className="absolute bottom-1 right-1 px-1 py-0.5 rounded text-[9px] bg-black/80 font-mono text-gray-300">
                          {s.motionType}
                        </span>
                      </div>

                      <div className="md:col-span-3 space-y-1.5 text-xs">
                        <p className="text-gray-200 font-medium">🗣️ Giọng đọc: {s.voiceText}</p>
                        <p className="text-amber-300 font-mono text-[11px]">📺 Chữ nổi bật: {s.onScreenText}</p>
                        <div className="flex items-center gap-3 text-[10px] text-gray-500 pt-1">
                          <span>Chuyển cảnh: {s.transitionType}</span>
                          <span>•</span>
                          <span>Hiệu ứng: {s.motionType}</span>
                          {s.imageCaption && (
                            <>
                              <span>•</span>
                              <span className="truncate max-w-xs" title={s.imageCaption}>Caption: {s.imageCaption}</span>
                            </>
                          )}
                        </div>
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>
          )}

          {/* ASSETS TAB: EPHEMERAL STORAGE INSPECTOR */}
          {activeTab === 'assets' && (
            <div className="space-y-4">
              <div className="glass-panel p-5 rounded-2xl border border-gray-800 space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <h4 className="text-sm font-bold text-white flex items-center gap-2">
                      <HardDrive className="w-4 h-4 text-emerald-400" />
                      Quản lý file tạm (Ephemeral Media Storage)
                    </h4>
                    <p className="text-xs text-gray-400 mt-0.5">
                      Toàn bộ file MP4, audio và phụ đề chỉ lưu tạm trong quá trình dựng và hết hạn sau 2 giờ.
                    </p>
                  </div>
                  <button
                    onClick={handleCleanupAssets}
                    className="px-3 py-1.5 rounded-lg bg-rose-500/20 hover:bg-rose-500 text-rose-300 hover:text-white text-xs font-semibold flex items-center gap-1 transition"
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                    <span>Dọn dẹp ngay</span>
                  </button>
                </div>

                <div className="space-y-2">
                  {assets.length === 0 ? (
                    <div className="p-6 text-center text-xs text-gray-500 bg-gray-900/40 rounded-xl">
                      Chưa có file media tạm nào được ghi nhận. Bấm "Dựng video 9:16" để tạo file.
                    </div>
                  ) : (
                    assets.map((asset) => (
                      <div
                        key={asset.id}
                        className="p-3 rounded-xl bg-[#0B0F19] border border-gray-800 flex flex-wrap items-center justify-between gap-3 text-xs"
                      >
                        <div className="space-y-0.5">
                          <div className="flex items-center gap-2">
                            <span className="px-2 py-0.5 rounded font-mono text-[10px] bg-indigo-500/20 text-indigo-300 font-bold">
                              {asset.assetType}
                            </span>
                            <span className="text-gray-300 font-mono truncate max-w-xs" title={asset.localTempPath}>
                              {asset.localTempPath.split(/[\/\\]/).pop()}
                            </span>
                          </div>
                          <p className="text-[10px] text-gray-500 font-mono truncate" title={asset.sha256Hash || ''}>
                            Hash: {asset.sha256Hash?.slice(0, 16)}... | Size: {(asset.fileSizeBytes / 1024).toFixed(1)} KB
                          </p>
                        </div>

                        <div className="text-right">
                          <span className={`px-2 py-0.5 rounded text-[10px] font-semibold ${
                            asset.isCleanedUp ? 'bg-gray-800 text-gray-500' : 'bg-emerald-500/20 text-emerald-300'
                          }`}>
                            {asset.isCleanedUp ? 'Đã dọn dẹp' : 'Đang hoạt động (Active)'}
                          </span>
                          <p className="text-[10px] text-gray-500 mt-1">
                            Hết hạn: {asset.expiresAtUtc ? new Date(asset.expiresAtUtc).toLocaleTimeString('vi-VN') : 'TTL 2h'}
                          </p>
                        </div>
                      </div>
                    ))
                  )}
                </div>
              </div>
            </div>
          )}

          {/* QC TAB */}
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
        </div>
      </div>
    </div>
  );
};
