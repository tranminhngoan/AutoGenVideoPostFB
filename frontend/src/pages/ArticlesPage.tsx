import React, { useState, useEffect } from 'react';
import { 
  Newspaper, Image as ImageIcon, ExternalLink, ShieldCheck, Clock, 
  ArrowRight, Plus, RefreshCw, AlertCircle, CheckCircle2, XCircle, 
  Eye, Edit2, ShieldAlert, Sparkles, Filter, Hash
} from 'lucide-react';
import { api, ArticleDto, ArticleImageDto } from '../services/api';

interface ArticlesPageProps {
  onSelectArticle: (articleId: string) => void;
}

export const ArticlesPage: React.FC<ArticlesPageProps> = ({ onSelectArticle }) => {
  const [articles, setArticles] = useState<ArticleDto[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [extracting, setExtracting] = useState<boolean>(false);
  const [inputUrl, setInputUrl] = useState<string>('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  // Selected article for full preview & image review modal
  const [selectedArticle, setSelectedArticle] = useState<ArticleDto | null>(null);
  const [editingImage, setEditingImage] = useState<ArticleImageDto | null>(null);
  const [savingImage, setSavingImage] = useState<boolean>(false);

  useEffect(() => {
    loadArticles();
  }, []);

  const loadArticles = async () => {
    setLoading(true);
    setErrorMessage(null);
    try {
      const data = await api.getArticles();
      setArticles(data);
      if (selectedArticle) {
        const updatedSelected = data.find(a => a.id === selectedArticle.id);
        if (updatedSelected) setSelectedArticle(updatedSelected);
      }
    } catch (err: any) {
      setErrorMessage(err.message || 'Không thể tải danh sách bài báo');
    } finally {
      setLoading(false);
    }
  };

  const handleExtract = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputUrl.trim()) return;

    setExtracting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      const extracted = await api.extractArticle(inputUrl.trim());
      setSuccessMessage(`Đã trích xuất thành công: "${extracted.title}" với ${extracted.imageCount} ảnh gốc.`);
      setInputUrl('');
      await loadArticles();
      setSelectedArticle(extracted);
    } catch (err: any) {
      setErrorMessage(err.message || 'Trích xuất bài báo thất bại');
    } finally {
      setExtracting(false);
    }
  };

  const handleUpdateImageRights = async (articleId: string, imageId: string, rightsStatus: string) => {
    try {
      const updated = await api.updateArticleImage(articleId, imageId, { rightsStatus });
      updateLocalImage(articleId, updated);
    } catch (err: any) {
      alert(err.message || 'Không thể cập nhật quyền hình ảnh');
    }
  };

  const handleUpdateImageCategory = async (articleId: string, imageId: string, category: string) => {
    try {
      const updated = await api.updateArticleImage(articleId, imageId, { category });
      updateLocalImage(articleId, updated);
    } catch (err: any) {
      alert(err.message || 'Không thể cập nhật phân loại hình ảnh');
    }
  };

  const handleToggleImageSelection = async (articleId: string, image: ArticleImageDto) => {
    try {
      const updated = await api.updateArticleImage(articleId, image.id, { 
        selectedForVideo: !image.selectedForVideo 
      });
      updateLocalImage(articleId, updated);
    } catch (err: any) {
      alert(err.message || 'Không thể đổi trạng thái chọn ảnh');
    }
  };

  const handleSaveImageModal = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingImage || !selectedArticle) return;

    setSavingImage(true);
    try {
      const updated = await api.updateArticleImage(selectedArticle.id, editingImage.id, {
        caption: editingImage.caption,
        rightsStatus: editingImage.rightsStatus,
        category: editingImage.category,
        selectedForVideo: editingImage.selectedForVideo
      });
      updateLocalImage(selectedArticle.id, updated);
      setEditingImage(null);
    } catch (err: any) {
      alert(err.message || 'Lỗi lưu thay đổi ảnh');
    } finally {
      setSavingImage(false);
    }
  };

  const updateLocalImage = (articleId: string, updatedImage: ArticleImageDto) => {
    setArticles(prev => prev.map(art => {
      if (art.id !== articleId) return art;
      return {
        ...art,
        images: art.images.map(img => img.id === updatedImage.id ? updatedImage : img)
      };
    }));

    if (selectedArticle && selectedArticle.id === articleId) {
      setSelectedArticle(prev => {
        if (!prev) return null;
        return {
          ...prev,
          images: prev.images.map(img => img.id === updatedImage.id ? updatedImage : img)
        };
      });
    }
  };

  const getRightsBadge = (status: string) => {
    switch (status) {
      case 'Authorized':
        return <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">Được phép (Authorized)</span>;
      case 'Licensed':
        return <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-blue-500/20 text-blue-300 border border-blue-500/30">Có bản quyền (Licensed)</span>;
      case 'Rejected':
        return <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-rose-500/20 text-rose-300 border border-rose-500/30">Từ chối (Rejected)</span>;
      case 'PermissionRequired':
        return <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-amber-500/20 text-amber-300 border border-amber-500/30">Cần xin phép (Required)</span>;
      default:
        return <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-gray-500/20 text-gray-300 border border-gray-500/30">Chưa rõ (Unknown)</span>;
    }
  };

  const getCategoryBadge = (category: string) => {
    const map: Record<string, { label: string; color: string }> = {
      Panorama: { label: 'Toàn cảnh', color: 'bg-indigo-500/20 text-indigo-300' },
      Character: { label: 'Nhân vật', color: 'bg-purple-500/20 text-purple-300' },
      Event: { label: 'Sự kiện', color: 'bg-cyan-500/20 text-cyan-300' },
      Document: { label: 'Tài liệu', color: 'bg-amber-500/20 text-amber-300' },
      Chart: { label: 'Biểu đồ', color: 'bg-emerald-500/20 text-emerald-300' },
      Unsuitable: { label: 'Không phù hợp', color: 'bg-rose-500/20 text-rose-400' }
    };
    const c = map[category] || { label: category, color: 'bg-gray-500/20 text-gray-400' };
    return <span className={`px-2 py-0.5 rounded text-[10px] font-medium ${c.color}`}>{c.label}</span>;
  };

  return (
    <div className="space-y-6 max-w-7xl mx-auto">
      {/* Header & Extractor Input */}
      <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold text-white tracking-tight flex items-center gap-2">
              <Newspaper className="w-6 h-6 text-indigo-400" />
              Article Extractor & Image Inspector
            </h1>
            <p className="text-xs text-gray-400 mt-1">
              Trích xuất nội dung bài báo và chỉ lấy hình ảnh gốc nằm trong chính bài viết. Không tìm ảnh ngoài, không dùng ảnh AI.
            </p>
          </div>
          <button
            onClick={loadArticles}
            disabled={loading}
            className="px-3.5 py-2 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-medium flex items-center space-x-1.5 transition self-start"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            <span>Làm mới</span>
          </button>
        </div>

        {/* Input Extract Form */}
        <form onSubmit={handleExtract} className="flex flex-col sm:flex-row gap-3 pt-2">
          <div className="relative flex-1">
            <input
              type="url"
              required
              value={inputUrl}
              onChange={(e) => setInputUrl(e.target.value)}
              placeholder="Dán link bài báo (VnExpress, Tuổi Trẻ, Thanh Niên, Dân Trí...)"
              className="w-full px-4 py-3 rounded-xl bg-[#0B0F19] border border-gray-700 text-white placeholder-gray-500 text-sm focus:outline-none focus:border-indigo-500 transition"
              disabled={extracting}
            />
          </div>
          <button
            type="submit"
            disabled={extracting || !inputUrl.trim()}
            className="px-6 py-3 rounded-xl bg-gradient-to-r from-indigo-600 to-purple-600 hover:from-indigo-500 hover:to-purple-500 text-white text-sm font-semibold flex items-center justify-center space-x-2 transition disabled:opacity-50 disabled:cursor-not-allowed shadow-lg shadow-indigo-600/20"
          >
            {extracting ? (
              <>
                <RefreshCw className="w-4 h-4 animate-spin" />
                <span>Đang trích xuất...</span>
              </>
            ) : (
              <>
                <Plus className="w-4 h-4" />
                <span>Trích xuất bài báo</span>
              </>
            )}
          </button>
        </form>

        {errorMessage && (
          <div className="p-3 rounded-xl bg-rose-500/10 border border-rose-500/30 text-rose-300 text-xs flex items-center gap-2">
            <AlertCircle className="w-4 h-4 shrink-0" />
            <span>{errorMessage}</span>
          </div>
        )}

        {successMessage && (
          <div className="p-3 rounded-xl bg-emerald-500/10 border border-emerald-500/30 text-emerald-300 text-xs flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4 shrink-0" />
            <span>{successMessage}</span>
          </div>
        )}
      </div>

      {/* Article List */}
      <div className="space-y-4">
        {loading && articles.length === 0 ? (
          <div className="glass-panel p-12 text-center text-gray-400 space-y-3">
            <RefreshCw className="w-8 h-8 animate-spin mx-auto text-indigo-400" />
            <p className="text-sm">Đang tải danh sách bài báo đã trích xuất...</p>
          </div>
        ) : articles.length === 0 ? (
          <div className="glass-panel p-12 text-center text-gray-400 space-y-4 border border-[#1F2937]">
            <Newspaper className="w-12 h-12 mx-auto text-gray-600" />
            <p className="text-base text-gray-300 font-medium">Chưa có bài báo nào được trích xuất trong database</p>
            <p className="text-xs text-gray-500 max-w-md mx-auto">
              Hãy dán URL bài báo từ VnExpress, Tuổi Trẻ, Thanh Niên hoặc Dân Trí ở trên để hệ thống tự động bóc tách nội dung và hình ảnh.
            </p>
          </div>
        ) : (
          articles.map((art) => (
            <div
              key={art.id}
              className="glass-panel p-6 rounded-2xl border border-[#1F2937] hover:border-indigo-500/40 transition-all space-y-4 shadow-sm"
            >
              <div className="flex items-start justify-between gap-4">
                <div className="space-y-1.5 flex-1">
                  <div className="flex flex-wrap items-center space-x-2 text-xs">
                    <span className="px-2 py-0.5 rounded bg-indigo-500/20 text-indigo-300 font-semibold">
                      {art.publisher}
                    </span>
                    <span className="text-gray-500">•</span>
                    <span className="text-gray-400 flex items-center gap-1">
                      <Clock className="w-3 h-3" /> {art.publishedAtUtc ? new Date(art.publishedAtUtc).toLocaleString('vi-VN') : 'Mới cập nhật'}
                    </span>
                    {art.author && (
                      <>
                        <span className="text-gray-500">•</span>
                        <span className="text-gray-400">Tác giả: {art.author}</span>
                      </>
                    )}
                  </div>
                  <h3 className="text-lg font-bold text-white hover:text-indigo-300 transition cursor-pointer"
                      onClick={() => setSelectedArticle(art)}>
                    {art.title}
                  </h3>
                  <p className="text-xs text-gray-400 line-clamp-2 leading-relaxed">
                    {art.description || art.contentBody?.slice(0, 200) + '...'}
                  </p>
                </div>

                <div className="flex items-center space-x-2 shrink-0">
                  <button
                    onClick={() => setSelectedArticle(art)}
                    className="p-2 rounded-lg bg-gray-800 text-gray-300 hover:text-white hover:bg-gray-700 transition"
                    title="Xem chi tiết & duyệt ảnh"
                  >
                    <Eye className="w-4 h-4" />
                  </button>
                  <a
                    href={art.url}
                    target="_blank"
                    rel="noreferrer"
                    className="p-2 rounded-lg bg-[#1F2937] text-gray-400 hover:text-white hover:bg-gray-700 transition"
                    title="Mở link bài gốc"
                  >
                    <ExternalLink className="w-4 h-4" />
                  </a>
                </div>
              </div>

              {/* Extracted Images Strip */}
              <div className="pt-3 border-t border-gray-800/80">
                <div className="flex items-center justify-between text-xs mb-3">
                  <span className="text-gray-400 flex items-center gap-1.5 font-medium">
                    <ImageIcon className="w-3.5 h-3.5 text-indigo-400" />
                    Hình ảnh bài viết trích xuất ({art.images.length} ảnh gốc)
                  </span>
                  <span className="text-[11px] text-emerald-400 flex items-center gap-1">
                    <ShieldCheck className="w-3.5 h-3.5" /> Nguồn từ chính bài viết (No AI)
                  </span>
                </div>

                {art.images.length === 0 ? (
                  <div className="p-4 rounded-xl bg-gray-900/40 text-xs text-gray-500 text-center">
                    Bài báo không có hình ảnh minh họa trong nội dung. Video sẽ sử dụng nền typography hoặc số liệu.
                  </div>
                ) : (
                  <div className="flex gap-4 overflow-x-auto pb-2">
                    {art.images.map((img) => (
                      <div
                        key={img.id}
                        className={`w-52 shrink-0 rounded-xl overflow-hidden bg-[#0B0F19] border ${
                          img.selectedForVideo ? 'border-gray-800' : 'border-rose-900/50 opacity-60'
                        } space-y-1.5 p-2 transition`}
                      >
                        <div className="h-32 rounded-lg overflow-hidden bg-gray-900 relative group">
                          <img
                            src={img.originalImageUrl}
                            alt={img.caption || 'Article visual'}
                            className="w-full h-full object-cover"
                            onError={(e) => {
                              // Fallback display if remote host blocks CORS image display
                              (e.target as HTMLImageElement).src = 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 24 24" fill="none" stroke="%23666" stroke-width="2"><rect width="18" height="18" x="3" y="3" rx="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"/></svg>';
                            }}
                          />
                          <span className="absolute top-1 left-1 px-1.5 py-0.5 rounded text-[10px] font-mono bg-black/80 text-white">
                            #{img.articleImageOrder}
                          </span>
                          <button
                            onClick={() => setEditingImage(img)}
                            className="absolute top-1 right-1 p-1 rounded bg-black/70 hover:bg-black text-gray-300 hover:text-white opacity-0 group-hover:opacity-100 transition"
                            title="Sửa caption / quyền"
                          >
                            <Edit2 className="w-3 h-3" />
                          </button>
                        </div>

                        <p className="text-[11px] text-gray-300 line-clamp-1 font-medium" title={img.caption || ''}>
                          {img.caption || '(Không có chú thích)'}
                        </p>

                        <div className="flex items-center justify-between text-[10px] pt-1 border-t border-gray-900">
                          {getCategoryBadge(img.category)}
                          {getRightsBadge(img.rightsStatus)}
                        </div>

                        <div className="flex items-center justify-between pt-1">
                          <label className="text-[10px] text-gray-400 flex items-center gap-1 cursor-pointer">
                            <input
                              type="checkbox"
                              checked={img.selectedForVideo}
                              onChange={() => handleToggleImageSelection(art.id, img)}
                              className="rounded bg-gray-800 border-gray-700 text-indigo-600 focus:ring-0 w-3 h-3"
                            />
                            <span>Dùng cho video</span>
                          </label>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              {/* Actions */}
              <div className="pt-2 flex justify-between items-center">
                <span className="text-[11px] text-gray-500 font-mono">
                  ID: {art.id.slice(0, 8)}... | Trạng thái: {art.status}
                </span>
                <div className="flex items-center space-x-3">
                  <button
                    onClick={() => setSelectedArticle(art)}
                    className="px-3.5 py-2 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 hover:text-white text-xs font-semibold flex items-center space-x-1.5 transition"
                  >
                    <Eye className="w-3.5 h-3.5" />
                    <span>Chi tiết bài báo</span>
                  </button>
                  <button
                    onClick={() => onSelectArticle(art.id)}
                    className="px-4 py-2 rounded-xl bg-indigo-600/20 hover:bg-indigo-600 border border-indigo-500/40 text-indigo-300 hover:text-white text-xs font-semibold flex items-center space-x-1.5 transition"
                  >
                    <span>Tạo dự án video từ bài này</span>
                    <ArrowRight className="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>
            </div>
          ))
        )}
      </div>

      {/* ARTICLE FULL PREVIEW MODAL */}
      {selectedArticle && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm overflow-y-auto">
          <div className="glass-panel w-full max-w-4xl max-h-[90vh] flex flex-col rounded-2xl border border-gray-700 bg-[#0F1422] shadow-2xl overflow-hidden my-auto">
            {/* Modal Header */}
            <div className="p-6 border-b border-gray-800 flex items-start justify-between gap-4">
              <div className="space-y-1">
                <div className="flex items-center gap-2 text-xs">
                  <span className="px-2 py-0.5 rounded bg-indigo-500/20 text-indigo-300 font-semibold">
                    {selectedArticle.publisher}
                  </span>
                  <span className="text-gray-500">•</span>
                  <span className="text-gray-400">
                    {selectedArticle.publishedAtUtc ? new Date(selectedArticle.publishedAtUtc).toLocaleString('vi-VN') : ''}
                  </span>
                </div>
                <h2 className="text-xl font-bold text-white leading-snug">
                  {selectedArticle.title}
                </h2>
                <a
                  href={selectedArticle.url}
                  target="_blank"
                  rel="noreferrer"
                  className="text-xs text-indigo-400 hover:underline flex items-center gap-1"
                >
                  <span>{selectedArticle.url}</span>
                  <ExternalLink className="w-3 h-3" />
                </a>
              </div>
              <button
                onClick={() => setSelectedArticle(null)}
                className="p-2 rounded-xl bg-gray-800 text-gray-400 hover:text-white transition"
              >
                <XCircle className="w-5 h-5" />
              </button>
            </div>

            {/* Modal Body */}
            <div className="p-6 overflow-y-auto space-y-6 text-sm text-gray-300 leading-relaxed">
              {/* Image Inspector section */}
              <div className="space-y-3">
                <h3 className="text-sm font-semibold text-white uppercase tracking-wider flex items-center gap-2">
                  <ImageIcon className="w-4 h-4 text-indigo-400" />
                  Danh sách ảnh trích xuất ({selectedArticle.images.length})
                </h3>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  {selectedArticle.images.map((img) => (
                    <div
                      key={img.id}
                      className="p-3 rounded-xl bg-[#0B0F19] border border-gray-800 space-y-2"
                    >
                      <div className="flex gap-3">
                        <div className="w-24 h-24 rounded-lg bg-gray-900 shrink-0 overflow-hidden relative">
                          <img
                            src={img.originalImageUrl}
                            alt={img.caption || ''}
                            className="w-full h-full object-cover"
                          />
                          <span className="absolute top-1 left-1 px-1 py-0.5 rounded text-[9px] bg-black/80 font-mono text-white">
                            #{img.articleImageOrder}
                          </span>
                        </div>
                        <div className="space-y-1 text-xs flex-1">
                          <p className="text-gray-200 font-medium line-clamp-2">
                            {img.caption || '(Không có caption)'}
                          </p>
                          <p className="text-[10px] text-gray-500 font-mono truncate" title={img.imageHash || ''}>
                            Hash: {img.imageHash?.slice(0, 16)}...
                          </p>
                          <div className="flex items-center gap-1.5 pt-1">
                            {getCategoryBadge(img.category)}
                            {getRightsBadge(img.rightsStatus)}
                          </div>
                        </div>
                      </div>

                      {/* Controls inside card */}
                      <div className="flex items-center justify-between pt-2 border-t border-gray-900 text-xs">
                        <select
                          value={img.rightsStatus}
                          onChange={(e) => handleUpdateImageRights(selectedArticle.id, img.id, e.target.value)}
                          className="bg-gray-800 border border-gray-700 text-gray-200 rounded px-2 py-1 text-[11px] focus:outline-none"
                        >
                          <option value="Unknown">Unknown (Chưa rõ)</option>
                          <option value="PermissionRequired">Permission Required</option>
                          <option value="Licensed">Licensed</option>
                          <option value="Authorized">Authorized (Được duyệt)</option>
                          <option value="Rejected">Rejected (Từ chối)</option>
                        </select>

                        <button
                          onClick={() => setEditingImage(img)}
                          className="text-[11px] text-indigo-400 hover:text-indigo-300 font-medium"
                        >
                          Chỉnh sửa
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              {/* Full Article Content */}
              <div className="space-y-3 pt-4 border-t border-gray-800">
                <h3 className="text-sm font-semibold text-white uppercase tracking-wider flex items-center gap-2">
                  <Newspaper className="w-4 h-4 text-purple-400" />
                  Nội dung bài viết
                </h3>
                <div className="p-4 rounded-xl bg-[#0B0F19] border border-gray-800/80 whitespace-pre-wrap text-xs text-gray-300 max-h-72 overflow-y-auto leading-relaxed">
                  {selectedArticle.contentBody || 'Không có nội dung bài viết.'}
                </div>
              </div>
            </div>

            {/* Modal Footer */}
            <div className="p-4 border-t border-gray-800 flex justify-end space-x-3 bg-gray-950/50">
              <button
                onClick={() => setSelectedArticle(null)}
                className="px-4 py-2 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-semibold transition"
              >
                Đóng
              </button>
              <button
                onClick={() => {
                  const id = selectedArticle.id;
                  setSelectedArticle(null);
                  onSelectArticle(id);
                }}
                className="px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold flex items-center space-x-1.5 transition"
              >
                <span>Dùng bài này tạo video</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </button>
            </div>
          </div>
        </div>
      )}

      {/* EDIT IMAGE METADATA MODAL */}
      {editingImage && selectedArticle && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm">
          <div className="glass-panel w-full max-w-md rounded-2xl border border-gray-700 bg-[#0F1422] p-6 shadow-2xl space-y-4">
            <h3 className="text-base font-bold text-white flex items-center gap-2">
              <Edit2 className="w-4 h-4 text-indigo-400" />
              Chỉnh sửa thông tin ảnh #{editingImage.articleImageOrder}
            </h3>

            <form onSubmit={handleSaveImageModal} className="space-y-4">
              <div>
                <label className="block text-xs font-medium text-gray-400 mb-1">Chú thích (Caption)</label>
                <textarea
                  rows={3}
                  value={editingImage.caption || ''}
                  onChange={(e) => setEditingImage({ ...editingImage, caption: e.target.value })}
                  className="w-full px-3 py-2 rounded-xl bg-[#0B0F19] border border-gray-700 text-white text-xs focus:outline-none focus:border-indigo-500"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-gray-400 mb-1">Phân loại ảnh</label>
                <select
                  value={editingImage.category}
                  onChange={(e) => setEditingImage({ ...editingImage, category: e.target.value })}
                  className="w-full px-3 py-2 rounded-xl bg-[#0B0F19] border border-gray-700 text-white text-xs focus:outline-none focus:border-indigo-500"
                >
                  <option value="Panorama">Toàn cảnh (Panorama)</option>
                  <option value="Character">Nhân vật (Character)</option>
                  <option value="Event">Sự kiện (Event)</option>
                  <option value="Document">Tài liệu (Document)</option>
                  <option value="Chart">Biểu đồ (Chart)</option>
                  <option value="Unsuitable">Không phù hợp (Unsuitable)</option>
                </select>
              </div>

              <div>
                <label className="block text-xs font-medium text-gray-400 mb-1">Quyền sử dụng (Rights Status)</label>
                <select
                  value={editingImage.rightsStatus}
                  onChange={(e) => setEditingImage({ ...editingImage, rightsStatus: e.target.value })}
                  className="w-full px-3 py-2 rounded-xl bg-[#0B0F19] border border-gray-700 text-white text-xs focus:outline-none focus:border-indigo-500"
                >
                  <option value="Unknown">Unknown (Chưa xác định)</option>
                  <option value="PermissionRequired">Permission Required (Cần xin phép)</option>
                  <option value="Licensed">Licensed (Có bản quyền cấp phép)</option>
                  <option value="Authorized">Authorized (Được duyệt xuất bản)</option>
                  <option value="Rejected">Rejected (Từ chối sử dụng)</option>
                </select>
                <p className="text-[10px] text-amber-400/90 mt-1 flex items-center gap-1">
                  <ShieldAlert className="w-3 h-3" />
                  Quy định: Không tự động chuyển Unknown sang Authorized mà không kiểm tra bản quyền.
                </p>
              </div>

              <div className="flex items-center gap-2 pt-1">
                <input
                  type="checkbox"
                  id="modalSelected"
                  checked={editingImage.selectedForVideo}
                  onChange={(e) => setEditingImage({ ...editingImage, selectedForVideo: e.target.checked })}
                  className="rounded bg-gray-800 border-gray-700 text-indigo-600 focus:ring-0 w-4 h-4"
                />
                <label htmlFor="modalSelected" className="text-xs text-gray-300">
                  Sử dụng hình ảnh này trong video dự án
                </label>
              </div>

              <div className="flex justify-end space-x-2 pt-3 border-t border-gray-800">
                <button
                  type="button"
                  onClick={() => setEditingImage(null)}
                  className="px-4 py-2 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs font-medium"
                >
                  Hủy
                </button>
                <button
                  type="submit"
                  disabled={savingImage}
                  className="px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold"
                >
                  {savingImage ? 'Đang lưu...' : 'Lưu thay đổi'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
