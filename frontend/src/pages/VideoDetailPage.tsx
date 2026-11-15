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
  Sparkles,
  Type,
  AlertCircle,
  Volume2,
  Download,
  Trash2,
  HardDrive,
  Edit3,
  AlertTriangle,
  RotateCcw,
  ExternalLink,
  Eye,
  Users,
  MessageSquare,
  ThumbsUp,
  BarChart3,
  Check
} from 'lucide-react';
import {
  api,
  VideoScriptDto,
  VideoScenePlanDto,
  VideoAssetDto,
  VideoReviewDto,
  FacebookPostDto,
  FacebookAccountDto
} from '../services/api';
import { VideoProject } from '../types';

interface VideoDetailPageProps {
  projectId: string;
  onBack: () => void;
}

export const VideoDetailPage: React.FC<VideoDetailPageProps> = ({ projectId, onBack }) => {
  const [activeTab, setActiveTab] = useState<'preview' | 'script' | 'scenes' | 'assets' | 'qc' | 'reels'>('preview');
  const [project, setProject] = useState<VideoProject | null>(null);
  const [script, setScript] = useState<VideoScriptDto | null>(null);
  const [scenes, setScenes] = useState<VideoScenePlanDto[]>([]);
  const [assets, setAssets] = useState<VideoAssetDto[]>([]);
  const [review, setReview] = useState<VideoReviewDto | null>(null);
  const [fbPosts, setFbPosts] = useState<FacebookPostDto[]>([]);
  const [pages, setPages] = useState<FacebookAccountDto[]>([]);

  const [loading, setLoading] = useState<boolean>(true);
  const [generating, setGenerating] = useState<boolean>(false);
  const [rendering, setRendering] = useState<boolean>(false);
  const [evaluating, setEvaluating] = useState<boolean>(false);
  const [publishing, setPublishing] = useState<boolean>(false);
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [metricsLoading, setMetricsLoading] = useState<string | null>(null);

  const [selectedProvider, setSelectedProvider] = useState<'Gemini' | 'Claude'>('Gemini');

  // Modals / forms
  const [showRejectModal, setShowRejectModal] = useState<boolean>(false);
  const [rejectNotes, setRejectNotes] = useState<string>('');
  const [showEditModal, setShowEditModal] = useState<boolean>(false);
  const [editTitle, setEditTitle] = useState<string>('');
  const [editCaption, setEditCaption] = useState<string>('');

  // Publish Modal
  const [showPublishModal, setShowPublishModal] = useState<boolean>(false);
  const [selectedPageId, setSelectedPageId] = useState<string>('');
  const [publishCaption, setPublishCaption] = useState<string>('');
  const [autoCleanup, setAutoCleanup] = useState<boolean>(true);

  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  useEffect(() => {
    loadData();
  }, [projectId]);

  const loadData = async () => {
    setLoading(true);
    setErrorMsg(null);
    try {
      const [projData, scriptData, scenesData, assetsData, reviewData, postsData, pagesData] = await Promise.all([
        api.getVideoProjectById(projectId),
        api.getScript(projectId),
        api.getScenes(projectId),
        api.getAssets(projectId),
        api.getReview(projectId),
        api.getProjectFacebookPosts(projectId),
        api.getFacebookPages()
      ]);
      setProject(projData);
      setScript(scriptData);
      setScenes(scenesData);
      setAssets(assetsData);
      setReview(reviewData);
      setFbPosts(postsData);
      setPages(pagesData);

      if (pagesData.length > 0 && !selectedPageId) {
        setSelectedPageId(pagesData[0].pageId);
      }

      if (projData) {
        setEditTitle(projData.title);
        var initialCaption = `${projData.title}\n\n${scriptData?.hook || ''}\n\n${scriptData?.sourceAttribution || ''}\n#News #Reels #TinTuc #AINewsStudio`;
        setPublishCaption(initialCaption);
      }
      if (scenesData.length > 0 && scenesData[0].onScreenText) {
        setEditCaption(scenesData[0].onScreenText);
      }
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
      await handleRunEvaluation();
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
      await handleRunEvaluation();
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
      await api.renderVideo(projectId, 'EdgeTts');
      setSuccessMsg('Render video 9:16 (1080×1920) hoàn tất! Video sẵn sàng để xem trước.');
      await loadData();
      setActiveTab('preview');
      await handleRunEvaluation();
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi render video');
    } finally {
      setRendering(false);
    }
  };

  const handleRunEvaluation = async () => {
    setEvaluating(true);
    try {
      const updatedReview = await api.evaluateQuality(projectId);
      setReview(updatedReview);
    } catch (err: any) {
      console.warn('Evaluation failed:', err);
    } finally {
      setEvaluating(false);
    }
  };

  const handleApprove = async () => {
    setErrorMsg(null);
    setSuccessMsg(null);
    try {
      const updated = await api.approveReview(projectId, 'Editor', 'Đạt chuẩn kiểm duyệt biên tập nội bộ.');
      setReview(updated);
      setSuccessMsg('✓ Video đã được phê duyệt! Sẵn sàng xuất bản lên Facebook Reels.');
    } catch (err: any) {
      setErrorMsg(err.message || 'Không thể phê duyệt video do vi phạm lỗi nghiêm trọng');
    }
  };

  const handleReject = async () => {
    if (!rejectNotes.trim()) {
      alert('Vui lòng nhập lý do từ chối để biên tập viên khắc phục.');
      return;
    }
    setErrorMsg(null);
    setSuccessMsg(null);
    try {
      const updated = await api.rejectReview(projectId, 'Editor', rejectNotes);
      setReview(updated);
      setShowRejectModal(false);
      setRejectNotes('');
      setSuccessMsg('Đã ghi nhận từ chối video kèm lý do phản hồi.');
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi khi từ chối video');
    }
  };

  const handleRequestAction = async (action: 'RegenerateScript' | 'RegenerateAudio' | 'ReRender') => {
    setActionLoading(action);
    setErrorMsg(null);
    try {
      await api.requestReviewAction(projectId, action);
      if (action === 'RegenerateScript') {
        await handleGenerateScript();
      } else if (action === 'RegenerateAudio' || action === 'ReRender') {
        await handleRenderVideo();
      }
      setSuccessMsg(`Đã yêu cầu thực hiện lại: ${action}`);
    } catch (err: any) {
      setErrorMsg(err.message || `Lỗi yêu cầu thực hiện ${action}`);
    } finally {
      setActionLoading(null);
    }
  };

  const handleUpdateMetadata = async () => {
    if (!editTitle.trim()) {
      alert('Tiêu đề video không được để trống');
      return;
    }
    try {
      await api.updateVideoMetadata(projectId, { title: editTitle, caption: editCaption });
      setShowEditModal(false);
      setSuccessMsg('Cập nhật tiêu đề & caption thành công.');
      await loadData();
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi cập nhật metadata');
    }
  };

  const handlePublishReel = async () => {
    setPublishing(true);
    setErrorMsg(null);
    setSuccessMsg(null);
    try {
      const result = await api.publishReel(projectId, {
        pageId: selectedPageId || undefined,
        customCaption: publishCaption,
        autoCleanupAssets: autoCleanup
      });
      setShowPublishModal(false);
      setSuccessMsg(`🎉 Xuất bản Facebook Reels thành công! Mã video: ${result.facebookVideoId}`);
      await loadData();
      setActiveTab('reels');
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi khi xuất bản Reels lên Meta Graph API');
    } finally {
      setPublishing(false);
    }
  };

  const handleFetchMetrics = async (postId: string) => {
    setMetricsLoading(postId);
    try {
      await api.fetchPostMetrics(postId);
      setSuccessMsg('Đã cập nhật số liệu tương tác mới nhất từ Meta Insights API.');
      await loadData();
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi cập nhật số liệu Insights');
    } finally {
      setMetricsLoading(null);
    }
  };

  const handleDeleteProject = async () => {
    if (!confirm('Bạn có chắc chắn muốn xóa vĩnh viễn dự án video này cùng tất cả file tạm thời không?')) return;
    try {
      const ok = await api.deleteVideoProject(projectId);
      if (ok) {
        alert('Đã xóa dự án thành công.');
        onBack();
      } else {
        setErrorMsg('Không thể xóa dự án');
      }
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi xóa dự án');
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
  const isApproved = review?.decision === 'Approved';
  const hasFatalViolations = review?.seriousViolationsFound || (review?.assessment?.hasFatalViolations ?? false);
  const publishedPost = fbPosts.find(p => p.uploadStatus === 'Published');

  return (
    <div className="space-y-6 max-w-7xl mx-auto">
      {/* Top Bar Navigation & Actions */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <button
          onClick={onBack}
          className="inline-flex items-center space-x-2 text-xs font-semibold text-gray-400 hover:text-white transition self-start"
        >
          <ArrowLeft className="w-4 h-4" />
          <span>Quay lại danh sách video</span>
        </button>

        <div className="flex flex-wrap items-center gap-3">
          {/* Status Decision Badge */}
          {review && (
            <div className="flex items-center gap-1.5 px-3 py-1 rounded-xl text-xs font-bold border border-gray-800 bg-[#0B0F19]">
              <span className="text-gray-400 font-normal">Trạng thái:</span>
              <span className={
                publishedPost ? 'text-indigo-400' :
                review.decision === 'Approved' ? 'text-emerald-400' :
                review.decision === 'Rejected' ? 'text-rose-400' : 'text-amber-400'
              }>
                {publishedPost ? 'Đã đăng Reels ✓' :
                 review.decision === 'Approved' ? 'Đã duyệt (Approved)' :
                 review.decision === 'Rejected' ? 'Từ chối (Rejected)' :
                 'Chờ duyệt (Pending Review)'}
              </span>
            </div>
          )}

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
            <span>{rendering ? 'FFmpeg đang dựng...' : 'Dựng video 9:16'}</span>
          </button>

          {/* Approve Button */}
          <button
            onClick={handleApprove}
            disabled={hasFatalViolations || !hasVideo}
            title={hasFatalViolations ? 'Chặn duyệt: Còn lỗi nghiêm trọng vi phạm kiểm định' : (!hasVideo ? 'Cần render video trước khi duyệt' : 'Phê duyệt video')}
            className={`px-4 py-2 rounded-xl text-xs font-semibold flex items-center gap-1.5 transition disabled:opacity-40 disabled:cursor-not-allowed ${
              isApproved
                ? 'bg-emerald-600 hover:bg-emerald-500 text-white'
                : 'bg-emerald-700 hover:bg-emerald-600 text-white shadow-lg shadow-emerald-700/20'
            }`}
          >
            <CheckCircle className="w-4 h-4" />
            <span>{isApproved ? 'Đã duyệt video ✓' : 'Phê duyệt video'}</span>
          </button>

          {/* Reject Button */}
          <button
            onClick={() => setShowRejectModal(true)}
            className="px-3 py-2 rounded-xl bg-rose-500/10 hover:bg-rose-500/20 text-rose-300 text-xs font-semibold flex items-center gap-1.5 transition border border-rose-500/30"
          >
            <XCircle className="w-3.5 h-3.5" />
            <span>Từ chối</span>
          </button>

          {/* Edit Metadata */}
          <button
            onClick={() => setShowEditModal(true)}
            className="px-3 py-2 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-semibold flex items-center gap-1.5 transition"
          >
            <Edit3 className="w-3.5 h-3.5" />
            <span>Sửa tiêu đề</span>
          </button>

          {/* Delete Project */}
          <button
            onClick={handleDeleteProject}
            className="px-3 py-2 rounded-xl bg-rose-950/40 hover:bg-rose-900/60 text-rose-400 text-xs font-semibold flex items-center gap-1.5 transition border border-rose-900/40"
            title="Xóa dự án và dọn sạch file tạm"
          >
            <Trash2 className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Error & Warning Banners */}
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

      {/* Fatal Violations Warning Banner */}
      {hasFatalViolations && review?.assessment?.fatalViolations && (
        <div className="p-4 rounded-2xl bg-rose-950/40 border border-rose-500/40 space-y-2">
          <div className="flex items-center gap-2 text-rose-300 text-xs font-bold">
            <AlertTriangle className="w-4 h-4 text-rose-400 animate-pulse" />
            <span>CẢNH BÁO KIỂM DUYỆT: PHÁT HIỆN LỖI NGHIÊM TRỌNG (CHẶN XUẤT BẢN)</span>
          </div>
          <ul className="list-disc list-inside text-xs text-rose-200/90 space-y-1">
            {review.assessment.fatalViolations.map((v, i) => (
              <li key={i}>{v}</li>
            ))}
          </ul>
        </div>
      )}

      {/* Main Review Workspace */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8">
        {/* Left Column: 9:16 Video Player Preview */}
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

        {/* Right Column: Editorial, Storyboard, Audio & QC Tabs */}
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
              <span>Kịch bản</span>
            </button>
            <button
              onClick={() => setActiveTab('scenes')}
              className={`px-3 py-2 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'scenes' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              <Film className="w-3.5 h-3.5" />
              <span>Phân cảnh ({scenes.length})</span>
            </button>
            <button
              onClick={() => setActiveTab('assets')}
              className={`px-3 py-2 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'assets' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-white'
              }`}
            >
              <HardDrive className="w-3.5 h-3.5" />
              <span>File tạm ({assets.length})</span>
            </button>
            <button
              onClick={() => setActiveTab('qc')}
              className={`px-3 py-2 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'qc' ? 'bg-emerald-600 text-white' : 'text-emerald-400 hover:text-emerald-300'
              }`}
            >
              <ShieldCheck className="w-3.5 h-3.5" />
              <span>Kiểm định QC ({review?.internalQualityScore ?? 100}/100)</span>
            </button>
            <button
              onClick={() => setActiveTab('reels')}
              className={`px-3 py-2 rounded-lg flex items-center gap-1.5 transition ${
                activeTab === 'reels' ? 'bg-indigo-600 text-white' : 'text-indigo-400 hover:text-indigo-300'
              }`}
            >
              <Share2 className="w-3.5 h-3.5" />
              <span>Facebook Reels {publishedPost ? '✓' : ''}</span>
            </button>
          </div>

          {/* PREVIEW / AUDIO & SUBTITLES TAB */}
          {activeTab === 'preview' && (
            <div className="space-y-4">
              <div className="glass-panel p-5 rounded-2xl border border-gray-800 space-y-4">
                <div className="flex items-center justify-between">
                  <h4 className="text-sm font-bold text-white flex items-center gap-2">
                    <Volume2 className="w-4 h-4 text-indigo-400" />
                    Thuyết minh giọng đọc tiếng Việt (TTS)
                  </h4>
                  <span className="text-xs text-gray-400 font-mono">Edge-TTS (vi-VN-HoaiMyNeural)</span>
                </div>

                {hasAudio ? (
                  <div className="space-y-3">
                    <audio controls className="w-full h-10">
                      <source src={`/api/video-projects/${projectId}/audio`} type="audio/wav" />
                      Trình duyệt không hỗ trợ phát audio HTML5.
                    </audio>
                    <div className="flex items-center justify-between text-xs text-gray-400">
                      <span>Định dạng: WAV PCM 24kHz 16-bit Mono</span>
                      <a
                        href={`/api/video-projects/${projectId}/audio`}
                        download="voiceover.wav"
                        className="text-indigo-400 hover:underline flex items-center gap-1"
                      >
                        <Download className="w-3 h-3" /> Tải file WAV
                      </a>
                    </div>
                  </div>
                ) : (
                  <div className="p-6 text-center text-xs text-gray-500 bg-gray-900/40 rounded-xl">
                    Chưa tạo âm thanh thuyết minh. Bấm "Dựng video 9:16" để hệ thống tổng hợp giọng đọc và dựng phim.
                  </div>
                )}
              </div>

              {/* Subtitles Download Card */}
              <div className="glass-panel p-5 rounded-2xl border border-gray-800 space-y-3">
                <div className="flex items-center justify-between">
                  <h4 className="text-sm font-bold text-white flex items-center gap-2">
                    <FileText className="w-4 h-4 text-purple-400" />
                    Phụ đề tự động (Synchronized Subtitles)
                  </h4>
                  <div className="flex items-center gap-2">
                    <a
                      href={`/api/video-projects/${projectId}/subtitles?format=srt`}
                      download="subtitles.srt"
                      className="px-2.5 py-1 rounded bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-semibold flex items-center gap-1"
                    >
                      <Download className="w-3 h-3" /> .SRT
                    </a>
                    <a
                      href={`/api/video-projects/${projectId}/subtitles?format=vtt`}
                      download="subtitles.vtt"
                      className="px-2.5 py-1 rounded bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-semibold flex items-center gap-1"
                    >
                      <Download className="w-3 h-3" /> .VTT
                    </a>
                  </div>
                </div>
                <p className="text-xs text-gray-400">
                  Phụ đề được tự động căn chỉnh timestamp theo từng mili-giây, hỗ trợ hiệu ứng karaoke highlight trên khung hình 9:16.
                </p>
              </div>
            </div>
          )}

          {/* SCRIPT TAB: FACT VS COMMENTARY */}
          {activeTab === 'script' && (
            <div className="space-y-4">
              {script ? (
                <div className="space-y-4">
                  <div className="glass-panel p-5 rounded-2xl border border-gray-800 space-y-3">
                    <div className="flex items-center justify-between">
                      <span className="text-xs font-bold text-indigo-400 uppercase tracking-wider">Tiêu đề video (45-60s)</span>
                      <span className="text-[11px] text-gray-400 font-mono">
                        ~{script.wordCount} từ • {script.estimatedReadingSeconds}s đọc
                      </span>
                    </div>
                    <h3 className="text-base font-bold text-white">{script.title}</h3>
                    <div className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/30 text-xs text-amber-200">
                      <span className="font-bold text-amber-400">HOOK (3-5s đầu): </span>
                      {script.hook}
                    </div>
                  </div>

                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <div className="glass-panel p-5 rounded-2xl border border-blue-500/30 bg-blue-950/10 space-y-2">
                      <div className="flex items-center justify-between">
                        <span className="text-xs font-bold text-blue-400 flex items-center gap-1.5">
                          <CheckCircle className="w-3.5 h-3.5" />
                          SỰ THẬT KHÁCH QUAN (KEY FACTS)
                        </span>
                        <span className="text-[10px] text-blue-300 bg-blue-500/20 px-2 py-0.5 rounded font-medium">Báo chí</span>
                      </div>
                      <div className="text-xs text-gray-300 whitespace-pre-wrap leading-relaxed">
                        {script.keyFacts}
                      </div>
                      <div className="pt-2 border-t border-blue-900/40 text-[11px] text-gray-400">
                        <span className="font-semibold text-blue-300">Bối cảnh: </span>
                        {script.context}
                      </div>
                    </div>

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

          {/* QC & VIDEO REVIEW TAB */}
          {activeTab === 'qc' && (
            <div className="space-y-4">
              <div className="glass-panel p-5 rounded-2xl border border-gray-800 space-y-4">
                <div className="flex flex-wrap items-center justify-between gap-4">
                  <div>
                    <h3 className="font-bold text-sm text-white flex items-center gap-2">
                      <ShieldCheck className="w-5 h-5 text-emerald-400" />
                      Báo cáo kiểm định chất lượng video (10 tiêu chí)
                    </h3>
                    <p className="text-xs text-gray-400 mt-1">
                      {review?.assessment?.summary || 'Hệ thống tự động rà soát nội dung, dữ kiện, bản quyền ảnh và kỹ thuật video.'}
                    </p>
                  </div>

                  <div className="flex items-center gap-3">
                    <button
                      onClick={handleRunEvaluation}
                      disabled={evaluating}
                      className="px-3 py-1.5 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-semibold flex items-center gap-1.5 transition"
                    >
                      <RefreshCw className={`w-3.5 h-3.5 ${evaluating ? 'animate-spin' : ''}`} />
                      <span>Kiểm định lại</span>
                    </button>

                    <div className="flex items-center gap-2 px-4 py-2 rounded-2xl bg-[#0B0F19] border border-gray-800">
                      <span className="text-xs text-gray-400">Điểm nội bộ:</span>
                      <span className={`font-mono text-lg font-extrabold ${
                        (review?.internalQualityScore ?? 0) >= 85 ? 'text-emerald-400' :
                        (review?.internalQualityScore ?? 0) >= 70 ? 'text-amber-400' : 'text-rose-400'
                      }`}>
                        {review?.internalQualityScore ?? 100}/100
                      </span>
                    </div>
                  </div>
                </div>

                <div className="p-3 rounded-xl bg-indigo-950/20 border border-indigo-500/20 text-[11px] text-indigo-300">
                  ℹ️ <strong>Lưu ý:</strong> {review?.assessment?.notice || 'Điểm kiểm định nội bộ là tiêu chuẩn biên tập của AI News Studio, không phản ánh thuật toán của Facebook.'}
                </div>

                <div className="space-y-2 text-xs">
                  {review?.assessment?.checkItems ? (
                    review.assessment.checkItems.map((item, idx) => (
                      <div
                        key={idx}
                        className={`p-3 rounded-xl border flex flex-col md:flex-row md:items-center justify-between gap-2 ${
                          item.isFatal && !item.passed
                            ? 'bg-rose-950/20 border-rose-500/40'
                            : item.passed
                            ? 'bg-[#0B0F19] border-gray-800/80'
                            : 'bg-amber-950/10 border-amber-500/30'
                        }`}
                      >
                        <div className="space-y-1">
                          <div className="flex items-center gap-2">
                            <span className="font-bold text-gray-200">
                              #{idx + 1}. {item.title}
                            </span>
                            <span className="px-1.5 py-0.5 rounded text-[9px] font-mono bg-gray-800 text-gray-400">
                              {item.category}
                            </span>
                            {item.isFatal && (
                              <span className="px-1.5 py-0.5 rounded text-[9px] font-bold bg-rose-500/20 text-rose-300">
                                Tiêu chí bắt buộc
                              </span>
                            )}
                          </div>
                          <p className="text-[11px] text-gray-400">{item.details}</p>
                          {item.recommendation && (
                            <p className="text-[10px] text-amber-300/90 font-medium">💡 Gợi ý: {item.recommendation}</p>
                          )}
                        </div>

                        <div className="flex items-center gap-2 shrink-0 self-end md:self-center">
                          <span className="font-mono text-[11px] text-gray-400">
                            {item.score}/10 đ
                          </span>
                          <span className={`px-2 py-0.5 rounded text-[10px] font-bold flex items-center gap-1 ${
                            item.passed
                              ? 'bg-emerald-500/20 text-emerald-300'
                              : item.isFatal
                              ? 'bg-rose-500/20 text-rose-300'
                              : 'bg-amber-500/20 text-amber-300'
                          }`}>
                            {item.passed ? <CheckCircle className="w-3 h-3" /> : <XCircle className="w-3 h-3" />}
                            {item.passed ? 'Đạt' : (item.isFatal ? 'Vi phạm' : 'Cần lưu ý')}
                          </span>
                        </div>
                      </div>
                    ))
                  ) : (
                    <div className="p-4 text-center text-gray-500 text-xs">
                      Bấm "Kiểm định lại" để chạy rà soát 10 tiêu chí chất lượng.
                    </div>
                  )}
                </div>
              </div>

              {/* Review Actions Toolbar */}
              <div className="glass-panel p-5 rounded-2xl border border-gray-800 space-y-3">
                <h4 className="text-xs font-bold text-gray-300 uppercase tracking-wider">
                  Thao tác kiểm duyệt & điều hướng quy trình
                </h4>
                <div className="flex flex-wrap items-center gap-3">
                  <button
                    onClick={() => handleRequestAction('RegenerateScript')}
                    disabled={actionLoading !== null}
                    className="px-3 py-2 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-semibold flex items-center gap-1.5 transition disabled:opacity-50"
                  >
                    <RotateCcw className={`w-3.5 h-3.5 ${actionLoading === 'RegenerateScript' ? 'animate-spin' : ''}`} />
                    <span>Yêu cầu tạo lại kịch bản</span>
                  </button>

                  <button
                    onClick={() => handleRequestAction('RegenerateAudio')}
                    disabled={actionLoading !== null}
                    className="px-3 py-2 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-semibold flex items-center gap-1.5 transition disabled:opacity-50"
                  >
                    <Volume2 className="w-3.5 h-3.5" />
                    <span>Yêu cầu tạo lại audio</span>
                  </button>

                  <button
                    onClick={() => handleRequestAction('ReRender')}
                    disabled={actionLoading !== null}
                    className="px-3 py-2 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-semibold flex items-center gap-1.5 transition disabled:opacity-50"
                  >
                    <Film className="w-3.5 h-3.5" />
                    <span>Render lại video</span>
                  </button>
                </div>
              </div>

              {/* Quick Publish Entry Box */}
              <div className="p-5 rounded-2xl bg-indigo-950/30 border border-indigo-500/30 flex flex-col md:flex-row md:items-center justify-between gap-4">
                <div>
                  <p className="font-bold text-sm text-white flex items-center gap-2">
                    <Share2 className="w-4 h-4 text-indigo-400" />
                    Xuất bản lên Facebook Reels (Meta Graph API)
                  </p>
                  <p className="text-xs text-indigo-300 mt-0.5">
                    {publishedPost
                      ? '✓ Video đã được xuất bản lên Reels. Xem phân tích số liệu tại tab Facebook Reels.'
                      : isApproved
                      ? '✓ Video đã được bạn phê duyệt, sẵn sàng đăng tải lên Meta Graph API.'
                      : hasFatalViolations
                      ? '⚠️ Không thể xuất bản: Video vi phạm tiêu chí kiểm định nghiêm trọng.'
                      : 'Cần bấm "Phê duyệt video" phía trên trước khi xuất bản.'}
                  </p>
                </div>
                <button
                  disabled={!isApproved || hasFatalViolations}
                  onClick={() => setShowPublishModal(true)}
                  className="px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 disabled:cursor-not-allowed text-white font-semibold text-xs flex items-center gap-2 transition shadow-lg shadow-indigo-600/30"
                >
                  <Share2 className="w-4 h-4" />
                  <span>{publishedPost ? 'Xem chi tiết bài đăng' : 'Đăng ngay lên Fanpage'}</span>
                </button>
              </div>
            </div>
          )}

          {/* REELS & INSIGHTS TAB (PHASE 7) */}
          {activeTab === 'reels' && (
            <div className="space-y-4">
              {publishedPost ? (
                <div className="space-y-4">
                  {/* Published Reel Status Card */}
                  <div className="glass-panel p-6 rounded-2xl border border-indigo-500/30 bg-gradient-to-br from-indigo-950/40 to-[#0B0F19] space-y-4">
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <div className="flex items-center gap-2.5">
                        <div className="w-10 h-10 rounded-2xl bg-indigo-600/20 text-indigo-400 flex items-center justify-center font-bold">
                          <Share2 className="w-5 h-5" />
                        </div>
                        <div>
                          <h4 className="text-sm font-bold text-white flex items-center gap-2">
                            <span>Facebook Reels đã xuất bản</span>
                            <span className="px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-300 text-[10px] font-bold">
                              Live on Meta ✓
                            </span>
                          </h4>
                          <p className="text-xs text-gray-400 mt-0.5 font-mono">
                            Post ID: {publishedPost.facebookPostId}
                          </p>
                        </div>
                      </div>

                      <div className="flex items-center gap-2">
                        {publishedPost.postUrl && (
                          <a
                            href={publishedPost.postUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="px-3.5 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold flex items-center gap-1.5 transition shadow-lg shadow-indigo-600/20"
                          >
                            <ExternalLink className="w-3.5 h-3.5" />
                            <span>Mở trên Facebook</span>
                          </a>
                        )}

                        <button
                          onClick={() => handleFetchMetrics(publishedPost.id)}
                          disabled={metricsLoading === publishedPost.id}
                          className="px-3 py-2 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-semibold flex items-center gap-1.5 transition"
                          title="Cập nhật số liệu từ Meta Page Insights API"
                        >
                          <RefreshCw className={`w-3.5 h-3.5 ${metricsLoading === publishedPost.id ? 'animate-spin text-indigo-400' : ''}`} />
                          <span>Cập nhật Insights</span>
                        </button>
                      </div>
                    </div>

                    <div className="p-3.5 rounded-xl bg-black/40 border border-gray-800 text-xs text-gray-300 space-y-1">
                      <p className="font-semibold text-white">Nội dung caption đã đăng:</p>
                      <p className="text-gray-400 whitespace-pre-wrap text-[11px] font-sans">
                        {project?.title} - {script?.sourceAttribution}
                      </p>
                    </div>

                    {/* Insights Metrics Grid */}
                    <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 pt-2">
                      <div className="p-3.5 rounded-xl bg-[#0B0F19] border border-gray-800 space-y-1">
                        <span className="text-[11px] text-gray-400 flex items-center gap-1.5">
                          <Eye className="w-3.5 h-3.5 text-blue-400" />
                          Tổng lượt xem (Views)
                        </span>
                        <p className="text-lg font-bold font-mono text-white">
                          {publishedPost.latestMetric?.views.toLocaleString() ?? '0'}
                        </p>
                      </div>

                      <div className="p-3.5 rounded-xl bg-[#0B0F19] border border-gray-800 space-y-1">
                        <span className="text-[11px] text-gray-400 flex items-center gap-1.5">
                          <Users className="w-3.5 h-3.5 text-emerald-400" />
                          Tiếp cận (Reach)
                        </span>
                        <p className="text-lg font-bold font-mono text-white">
                          {publishedPost.latestMetric?.reach.toLocaleString() ?? '0'}
                        </p>
                      </div>

                      <div className="p-3.5 rounded-xl bg-[#0B0F19] border border-gray-800 space-y-1">
                        <span className="text-[11px] text-gray-400 flex items-center gap-1.5">
                          <ThumbsUp className="w-3.5 h-3.5 text-amber-400" />
                          Tương tác (Engagement)
                        </span>
                        <p className="text-lg font-bold font-mono text-white">
                          {publishedPost.latestMetric?.engagement.toLocaleString() ?? '0'}
                        </p>
                      </div>

                      <div className="p-3.5 rounded-xl bg-[#0B0F19] border border-gray-800 space-y-1">
                        <span className="text-[11px] text-gray-400 flex items-center gap-1.5">
                          <Share2 className="w-3.5 h-3.5 text-purple-400" />
                          Lượt chia sẻ (Shares)
                        </span>
                        <p className="text-lg font-bold font-mono text-white">
                          {publishedPost.latestMetric?.shares.toLocaleString() ?? '0'}
                        </p>
                      </div>

                      <div className="p-3.5 rounded-xl bg-[#0B0F19] border border-gray-800 space-y-1">
                        <span className="text-[11px] text-gray-400 flex items-center gap-1.5">
                          <MessageSquare className="w-3.5 h-3.5 text-cyan-400" />
                          Bình luận (Comments)
                        </span>
                        <p className="text-lg font-bold font-mono text-white">
                          {publishedPost.latestMetric?.comments.toLocaleString() ?? '0'}
                        </p>
                      </div>

                      <div className="p-3.5 rounded-xl bg-[#0B0F19] border border-gray-800 space-y-1">
                        <span className="text-[11px] text-gray-400 flex items-center gap-1.5">
                          <BarChart3 className="w-3.5 h-3.5 text-rose-400" />
                          Thời lượng xem TB
                        </span>
                        <p className="text-lg font-bold font-mono text-white">
                          {publishedPost.latestMetric?.watchTimeSeconds.toFixed(1) ?? '0.0'}s
                        </p>
                      </div>
                    </div>
                  </div>
                </div>
              ) : (
                <div className="glass-panel p-8 text-center space-y-4 border border-gray-800">
                  <div className="w-12 h-12 mx-auto rounded-2xl bg-indigo-600/10 text-indigo-400 flex items-center justify-center">
                    <Share2 className="w-6 h-6" />
                  </div>
                  <div>
                    <h4 className="text-sm font-bold text-white">Video chưa được xuất bản lên Facebook Reels</h4>
                    <p className="text-xs text-gray-400 mt-1 max-w-md mx-auto">
                      Để xuất bản, video bắt buộc phải được render hoàn chỉnh và vượt qua kiểm định chất lượng (được biên tập viên duyệt).
                    </p>
                  </div>
                  <button
                    disabled={!isApproved || hasFatalViolations}
                    onClick={() => setShowPublishModal(true)}
                    className="px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 disabled:cursor-not-allowed text-white text-xs font-semibold inline-flex items-center gap-2 transition"
                  >
                    <Share2 className="w-4 h-4" />
                    <span>Mở hộp thoại xuất bản Reels</span>
                  </button>
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* PUBLISH REELS MODAL (PHASE 7) */}
      {showPublishModal && (
        <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="glass-panel p-6 rounded-2xl max-w-lg w-full border border-gray-800 space-y-5">
            <div className="flex items-center justify-between border-b border-gray-800 pb-3">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <Share2 className="w-5 h-5 text-indigo-400" />
                Xuất bản video lên Facebook Reels (Meta Graph API)
              </h3>
              <button
                onClick={() => setShowPublishModal(false)}
                className="text-gray-400 hover:text-white text-xs"
              >
                ✕
              </button>
            </div>

            <div className="space-y-4 text-xs">
              {/* Select Fanpage */}
              <div>
                <label className="block text-gray-400 font-semibold mb-1">Chọn Facebook Page xuất bản *</label>
                {pages.length > 0 ? (
                  <select
                    value={selectedPageId}
                    onChange={(e) => setSelectedPageId(e.target.value)}
                    className="w-full p-2.5 rounded-xl bg-[#0B0F19] border border-gray-800 text-gray-200 focus:outline-none focus:border-indigo-500"
                  >
                    {pages.map((p) => (
                      <option key={p.id} value={p.pageId}>
                        {p.pageName} (ID: {p.pageId})
                      </option>
                    ))}
                  </select>
                ) : (
                  <div className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-300">
                    Chưa có Fanpage nào được kết nối trong hệ thống. Hệ thống sẽ sử dụng Fanpage thử nghiệm mặc định của AI News Studio.
                  </div>
                )}
              </div>

              {/* Caption */}
              <div>
                <label className="block text-gray-400 font-semibold mb-1">Mô tả & Hashtags bài đăng</label>
                <textarea
                  value={publishCaption}
                  onChange={(e) => setPublishCaption(e.target.value)}
                  rows={4}
                  className="w-full p-3 rounded-xl bg-[#0B0F19] border border-gray-800 text-gray-200 focus:outline-none focus:border-indigo-500"
                />
              </div>

              {/* Ephemeral Auto-Cleanup Checkbox (Master Plan Requirement) */}
              <label className="flex items-start gap-2.5 p-3 rounded-xl bg-[#0B0F19] border border-gray-800 cursor-pointer">
                <input
                  type="checkbox"
                  checked={autoCleanup}
                  onChange={(e) => setAutoCleanup(e.target.checked)}
                  className="rounded border-gray-700 text-indigo-600 focus:ring-0 mt-0.5"
                />
                <div className="space-y-0.5">
                  <span className="font-semibold text-white">Tự động dọn dẹp file tạm sau khi đăng (Khuyến nghị)</span>
                  <p className="text-[11px] text-gray-400">
                    Tuân thủ Master Plan: Không lưu MP4/ảnh/audio lâu dài. File tạm trên ổ cứng sẽ được giải phóng ngay sau khi upload thành công lên Meta.
                  </p>
                </div>
              </label>
            </div>

            <div className="flex items-center justify-end gap-3 pt-2 border-t border-gray-800">
              <button
                type="button"
                onClick={() => setShowPublishModal(false)}
                className="px-4 py-2 rounded-xl text-xs text-gray-400 hover:text-white"
              >
                Hủy bỏ
              </button>
              <button
                type="button"
                onClick={handlePublishReel}
                disabled={publishing}
                className="px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-xs font-semibold flex items-center gap-2 shadow-lg shadow-indigo-600/30"
              >
                <Share2 className={`w-3.5 h-3.5 ${publishing ? 'animate-spin' : ''}`} />
                <span>{publishing ? 'Đang upload lên Meta Reels...' : 'Xác nhận đăng Reels'}</span>
              </button>
            </div>
          </div>
        </div>
      )}

      {/* REJECT MODAL */}
      {showRejectModal && (
        <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="glass-panel p-6 rounded-2xl max-w-md w-full border border-gray-800 space-y-4">
            <h3 className="text-base font-bold text-white flex items-center gap-2">
              <XCircle className="w-5 h-5 text-rose-400" />
              Từ chối duyệt video
            </h3>
            <p className="text-xs text-gray-400">
              Vui lòng nhập lý do từ chối để ghi lại lịch sử kiểm duyệt và hướng dẫn người biên tập chỉnh sửa.
            </p>
            <textarea
              value={rejectNotes}
              onChange={(e) => setRejectNotes(e.target.value)}
              placeholder="Ví dụ: Kịch bản chưa làm rõ số liệu, cần kiểm tra lại bản quyền ảnh phân cảnh số 3..."
              className="w-full h-24 p-3 rounded-xl bg-[#0B0F19] border border-gray-800 text-xs text-gray-200 focus:outline-none focus:border-rose-500"
            />
            <div className="flex items-center justify-end gap-3 pt-2">
              <button
                onClick={() => setShowRejectModal(false)}
                className="px-4 py-2 rounded-xl text-xs text-gray-400 hover:text-white"
              >
                Hủy bỏ
              </button>
              <button
                onClick={handleReject}
                className="px-4 py-2 rounded-xl bg-rose-600 hover:bg-rose-500 text-white text-xs font-semibold"
              >
                Xác nhận từ chối
              </button>
            </div>
          </div>
        </div>
      )}

      {/* EDIT METADATA MODAL */}
      {showEditModal && (
        <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="glass-panel p-6 rounded-2xl max-w-lg w-full border border-gray-800 space-y-4">
            <h3 className="text-base font-bold text-white flex items-center gap-2">
              <Edit3 className="w-5 h-5 text-indigo-400" />
              Chỉnh sửa tiêu đề & Caption video
            </h3>
            <div className="space-y-3 text-xs">
              <div>
                <label className="block text-gray-400 font-semibold mb-1">Tiêu đề video</label>
                <input
                  type="text"
                  value={editTitle}
                  onChange={(e) => setEditTitle(e.target.value)}
                  className="w-full p-2.5 rounded-xl bg-[#0B0F19] border border-gray-800 text-gray-200 focus:outline-none focus:border-indigo-500"
                />
              </div>
              <div>
                <label className="block text-gray-400 font-semibold mb-1">Caption / Chữ nổi bật phân cảnh</label>
                <input
                  type="text"
                  value={editCaption}
                  onChange={(e) => setEditCaption(e.target.value)}
                  placeholder="TIÊU ĐIỂM: ..."
                  className="w-full p-2.5 rounded-xl bg-[#0B0F19] border border-gray-800 text-gray-200 focus:outline-none focus:border-indigo-500"
                />
              </div>
            </div>
            <div className="flex items-center justify-end gap-3 pt-2">
              <button
                onClick={() => setShowEditModal(false)}
                className="px-4 py-2 rounded-xl text-xs text-gray-400 hover:text-white"
              >
                Hủy
              </button>
              <button
                onClick={handleUpdateMetadata}
                className="px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold"
              >
                Lưu thay đổi
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
