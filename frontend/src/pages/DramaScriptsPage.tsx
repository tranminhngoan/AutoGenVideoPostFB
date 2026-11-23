import React, { useState, useEffect, useMemo } from 'react';
import {
  Search,
  Flame,
  Film,
  Sparkles,
  Copy,
  Check,
  Download,
  Upload,
  Trash2,
  Edit3,
  ArrowRight,
  ExternalLink,
  Clapperboard,
  Clock,
  Volume2,
  Video,
  Eye,
  RefreshCw,
  Wand2,
  Plus,
  ShieldCheck,
  Heart,
  FileText,
  ChevronRight,
  SlidersHorizontal,
  Layers,
} from 'lucide-react';
import {
  Badge,
  Button,
  Card,
  Field,
  Input,
  PageHeader,
  Select,
  Textarea,
  cn,
} from '../components/ui';
import { useToast } from '../components/feedback';
import { navigate } from '../lib/router';
import {
  CHINESE_DRAMAS,
  DRAMA_GENRES,
  ChineseDrama,
  DramaEpisode,
  DramaScene,
} from '../data/chineseDramaLibrary';
import {
  estimateReadingTime,
  extractVoiceoverText,
  formatProductionScript,
  extractVisualPrompts,
  exportAllEpisodes,
  generateEpisodesFromPlot,
  DramaStyleTone,
} from '../lib/dramaScriptEngine';
import {
  dramaStorage,
  SavedDramaProject,
} from '../lib/dramaStorage';

type ActiveTab = 'library' | 'studio' | 'saved';

export function DramaScriptsPage() {
  const toast = useToast();
  const [activeTab, setActiveTab] = useState<ActiveTab>('library');

  // Search & Filter state for Library
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedGenre, setSelectedGenre] = useState<string>('all');
  const [favorites, setFavorites] = useState<string[]>(() => dramaStorage.getFavorites());

  // Selected drama for preview modal or details
  const [previewDrama, setPreviewDrama] = useState<ChineseDrama | null>(null);

  // Rewriter & Episode Studio state
  const [studioTitle, setStudioTitle] = useState('');
  const [studioSourcePlot, setStudioSourcePlot] = useState('');
  const [studioNumEpisodes, setStudioNumEpisodes] = useState<number>(3);
  const [studioTone, setStudioTone] = useState<DramaStyleTone>('va_mat');
  const [studioEpisodes, setStudioEpisodes] = useState<DramaEpisode[]>([]);
  const [selectedEpIndex, setSelectedEpIndex] = useState<number>(0);
  const [isEditingScene, setIsEditingScene] = useState<number | null>(null);

  // Saved projects
  const [savedProjects, setSavedProjects] = useState<SavedDramaProject[]>([]);
  const [copiedKey, setCopiedKey] = useState<string | null>(null);

  // Load saved projects on mount
  useEffect(() => {
    setSavedProjects(dramaStorage.getSavedProjects());
  }, []);

  // Filtered dramas in Library
  const filteredDramas = useMemo(() => {
    return CHINESE_DRAMAS.filter((d) => {
      const matchGenre = selectedGenre === 'all' || d.genreKey === selectedGenre;
      const q = searchQuery.toLowerCase().trim();
      const matchQuery =
        !q ||
        d.title.toLowerCase().includes(q) ||
        d.originalTitle.toLowerCase().includes(q) ||
        d.synopsis.toLowerCase().includes(q) ||
        d.hook.toLowerCase().includes(q) ||
        d.tags.some((t) => t.toLowerCase().includes(q)) ||
        d.characters.some((c) => c.name.toLowerCase().includes(q));
      return matchGenre && matchQuery;
    });
  }, [searchQuery, selectedGenre]);

  // Copy helper with feedback
  const handleCopy = async (key: string, text: string, message: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopiedKey(key);
      toast.success(message);
      setTimeout(() => setCopiedKey(null), 2500);
    } catch {
      toast.error('Không thể sao chép vào bộ nhớ tạm. Hãy thử lại!');
    }
  };

  // Toggle favorite
  const handleToggleFavorite = (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    const isFav = dramaStorage.toggleFavorite(id);
    setFavorites(dramaStorage.getFavorites());
    toast.info(isFav ? 'Đã thêm vào yêu thích!' : 'Đã bỏ yêu thích!');
  };

  // Load a drama from library directly into Studio
  const handleLoadDramaToStudio = (drama: ChineseDrama) => {
    setStudioTitle(drama.title);
    setStudioSourcePlot(drama.synopsis);
    setStudioNumEpisodes(drama.episodes.length || 3);
    setStudioEpisodes(JSON.parse(JSON.stringify(drama.episodes)));
    setSelectedEpIndex(0);
    setActiveTab('studio');
    toast.success(`Đã tải kịch bản "${drama.title}" vào Studio!`);
  };

  // Generate / Rewrite episodes
  const handleRewriteEpisodes = () => {
    if (!studioTitle.trim() && !studioSourcePlot.trim()) {
      toast.error('Vui lòng nhập tiêu đề hoặc cốt truyện kịch bản cần viết lại!');
      return;
    }

    const title = studioTitle.trim() || 'Kịch Bản Phim Ngắn Gây Nghiện';
    const eps = generateEpisodesFromPlot({
      title,
      sourceText: studioSourcePlot,
      numEpisodes: studioNumEpisodes,
      targetDurationSeconds: 120,
      tone: studioTone,
    });

    setStudioEpisodes(eps);
    setSelectedEpIndex(0);
    toast.success(`Đã tạo thành công ${eps.length} tập phim chuẩn 2 phút!`);
  };

  // Save current studio project to LocalStorage
  const handleSaveCurrentProject = () => {
    if (studioEpisodes.length === 0) {
      toast.error('Chưa có tập kịch bản nào để lưu!');
      return;
    }
    const newProj: SavedDramaProject = {
      id: 'drama_' + Date.now(),
      title: studioTitle.trim() || 'Kịch bản chưa đặt tên',
      genre: 'Phim ngắn Trung Quốc',
      tone: studioTone,
      synopsis: studioSourcePlot,
      episodes: studioEpisodes,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };
    dramaStorage.saveProject(newProj);
    setSavedProjects(dramaStorage.getSavedProjects());
    toast.success('Đã lưu kịch bản an toàn vào trình duyệt (LocalStorage)!');
  };

  // Transfer episode to Video Studio (/studio/new)
  const handleTransferToStudio = (episode: DramaEpisode) => {
    const fullScript = formatProductionScript(episode);
    sessionStorage.setItem('studio_prefill_script', fullScript);
    sessionStorage.setItem('studio_prefill_title', `${studioTitle || 'Phim Ngắn TQ'} - ${episode.title}`);
    
    // Also copy to clipboard so they can paste in CapCut or external editor
    navigator.clipboard.writeText(fullScript).catch(() => {});
    
    toast.success('Đã nạp kịch bản vào Studio Dựng Phim & sao chép vào bộ nhớ tạm!');
    navigate('/studio/new');
  };

  // Edit scene in active episode
  const handleUpdateScene = (sceneIndex: number, field: keyof DramaScene, value: string) => {
    if (!studioEpisodes[selectedEpIndex]) return;
    const updatedEps = [...studioEpisodes];
    const currentEp = { ...updatedEps[selectedEpIndex] };
    const updatedScenes = [...currentEp.scenes];
    updatedScenes[sceneIndex] = {
      ...updatedScenes[sceneIndex],
      [field]: value,
    };
    currentEp.scenes = updatedScenes;
    updatedEps[selectedEpIndex] = currentEp;
    setStudioEpisodes(updatedEps);
  };

  const currentEpisode = studioEpisodes[selectedEpIndex];
  const currentEpReadingTime = useMemo(() => {
    if (!currentEpisode) return { words: 0, seconds: 0, formatted: '0s' };
    const allVoiceText = currentEpisode.scenes.map((s) => s.voiceover).join(' ');
    return estimateReadingTime(allVoiceText);
  }, [currentEpisode]);

  return (
    <div className="space-y-6 pb-16">
      {/* Page Header */}
      <PageHeader
        title={
          <div className="flex flex-wrap items-center gap-2.5">
            <span>Kịch Bản Phim Ngắn Gây Nghiện TQ</span>
            <Badge tone="info" className="flex items-center gap-1 font-medium text-xs">
              <Sparkles className="h-3 w-3" /> Douyin & Kuaishou Viral
            </Badge>
          </div>
        }
        description="Kho kịch bản phim ngắn Trung Quốc gây nghiện triệu view (Tổng tài, Chiến thần, Trùng sinh, Manh bảo, Vả mặt...). Tự động biên tập tách tập chuẩn 2 phút, 1 chạm sao chép bỏ qua trang dựng phim hoặc nạp sang CapCut."
        actions={
          <div className="flex items-center gap-2">
            <Button
              variant={activeTab === 'studio' ? 'primary' : 'secondary'}
              size="sm"
              icon={<Wand2 className="h-4 w-4" />}
              onClick={() => {
                if (studioEpisodes.length === 0 && CHINESE_DRAMAS[0]) {
                  handleLoadDramaToStudio(CHINESE_DRAMAS[0]);
                } else {
                  setActiveTab('studio');
                }
              }}
            >
              Studio Tách Tập 2P
            </Button>
            <Button
              variant={activeTab === 'library' ? 'primary' : 'secondary'}
              size="sm"
              icon={<Film className="h-4 w-4" />}
              onClick={() => setActiveTab('library')}
            >
              Kho Kịch Bản Hot ({CHINESE_DRAMAS.length})
            </Button>
            <Button
              variant={activeTab === 'saved' ? 'primary' : 'secondary'}
              size="sm"
              icon={<FileText className="h-4 w-4" />}
              onClick={() => setActiveTab('saved')}
            >
              Kịch Bản Của Tôi ({savedProjects.length})
            </Button>
          </div>
        }
      />

      {/* Railway Friendly Notice */}
      <div className="flex items-center justify-between rounded-lg border border-line bg-surface/50 p-3 text-xs text-fg-subtle">
        <div className="flex items-center gap-2">
          <ShieldCheck className="h-4 w-4 text-emerald-500 shrink-0" />
          <span>
            <strong>Tối ưu Railway Starter:</strong> Kịch bản lưu trữ 100% trong bộ nhớ trình duyệt (LocalStorage). Không dùng Database server, không tốn RAM hay dung lượng đĩa của Railway!
          </span>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => dramaStorage.exportBackupJson()}
            className="flex items-center gap-1 text-brand hover:underline font-medium"
            title="Tải file backup JSON về máy"
          >
            <Download className="h-3.5 w-3.5" /> Xuất JSON
          </button>
        </div>
      </div>

      {/* ─────────────────────────────────────────────────────────────
          TAB 1: KHO KỊCH BẢN HOT (LIBRARY & SEARCH)
      ───────────────────────────────────────────────────────────── */}
      {activeTab === 'library' && (
        <div className="space-y-5">
          {/* Search bar & Filter Pills */}
          <Card className="p-4 space-y-3">
            <div className="flex flex-col sm:flex-row gap-3">
              <div className="relative flex-1">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fg-subtle" />
                <Input
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  placeholder="Tìm theo từ khóa (chiến thần, ở rể, tổng tài, trùng sinh, mẹ kế, vả mặt, ADN...)..."
                  className="pl-9"
                />
              </div>
              {searchQuery && (
                <Button variant="ghost" size="sm" onClick={() => setSearchQuery('')}>
                  Xóa lọc
                </Button>
              )}
            </div>

            {/* Genre Pills */}
            <div className="flex items-center gap-1.5 overflow-x-auto pb-1 text-xs">
              <span className="text-fg-subtle font-medium flex items-center gap-1 mr-1">
                <SlidersHorizontal className="h-3.5 w-3.5" /> Thể loại:
              </span>
              {DRAMA_GENRES.map((g) => (
                <button
                  key={g.key}
                  type="button"
                  onClick={() => setSelectedGenre(g.key)}
                  className={cn(
                    'whitespace-nowrap px-2.5 py-1 rounded-full border transition-colors',
                    selectedGenre === g.key
                      ? 'border-brand bg-brand text-white font-medium'
                      : 'border-line bg-muted/50 text-fg-subtle hover:text-fg hover:bg-muted'
                  )}
                >
                  {g.label}
                </button>
              ))}
            </div>
          </Card>

          {/* Drama Cards Grid */}
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {filteredDramas.map((drama) => {
              const isFav = favorites.includes(drama.id);
              return (
                <Card
                  key={drama.id}
                  className="flex flex-col justify-between p-4 hover:border-brand/50 hover:shadow-card transition-all"
                >
                  <div className="space-y-3">
                    {/* Top row: Genre & Views */}
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-1.5 flex-wrap">
                        <Badge tone="info" className="text-[11px]">
                          {drama.genre}
                        </Badge>
                        {drama.isFullSeries && (
                          <Badge tone="success" className="text-[10px] font-semibold">
                            Full Trọn Bộ ({drama.episodes.length} tập)
                          </Badge>
                        )}
                        <span className="flex items-center gap-0.5 text-[11px] font-semibold text-amber-500">
                          ⭐ {drama.addictionRating.toFixed(1)}
                        </span>
                      </div>
                      <div className="flex items-center gap-1">
                        <span className="text-[11px] text-fg-subtle">{drama.viewsEstimate}</span>
                        <button
                          type="button"
                          onClick={(e) => handleToggleFavorite(drama.id, e)}
                          className="p-1 text-fg-subtle hover:text-rose-500 transition-colors"
                          title="Lưu yêu thích"
                        >
                          <Heart
                            className={cn('h-4 w-4', isFav && 'fill-rose-500 text-rose-500')}
                          />
                        </button>
                      </div>
                    </div>

                    {/* Title */}
                    <div>
                      <h3 className="font-semibold text-fg text-base line-clamp-1 group-hover:text-brand">
                        {drama.title}
                      </h3>
                      <p className="text-xs text-fg-subtle line-clamp-1 italic">
                        {drama.originalTitle}
                      </p>
                    </div>

                    {/* Viral Hook Highlight */}
                    <div className="rounded-md bg-amber-500/10 border border-amber-500/20 p-2 text-xs text-amber-700 dark:text-amber-400">
                      <p className="flex items-start gap-1 font-medium">
                        <Flame className="h-3.5 w-3.5 text-amber-500 shrink-0 mt-0.5" />
                        <span>{drama.hook}</span>
                      </p>
                    </div>

                    {/* Synopsis */}
                    <p className="text-xs text-fg-subtle line-clamp-3 leading-relaxed">
                      {drama.synopsis}
                    </p>

                    {/* Tags */}
                    <div className="flex flex-wrap gap-1">
                      {drama.tags.slice(0, 4).map((tag) => (
                        <span
                          key={tag}
                          className="rounded bg-muted px-1.5 py-0.5 text-[10px] text-fg-subtle font-medium"
                        >
                          #{tag}
                        </span>
                      ))}
                    </div>
                  </div>

                  {/* Card Bottom Actions */}
                  <div className="pt-4 mt-3 border-t border-line/60 flex items-center justify-between gap-2">
                    <Button
                      variant="ghost"
                      size="sm"
                      icon={<Eye className="h-3.5 w-3.5" />}
                      onClick={() => setPreviewDrama(drama)}
                      className="text-xs"
                    >
                      Chi tiết ({drama.episodes.length} tập)
                    </Button>
                    <Button
                      variant="primary"
                      size="sm"
                      icon={<Wand2 className="h-3.5 w-3.5" />}
                      onClick={() => handleLoadDramaToStudio(drama)}
                      className="text-xs"
                    >
                      Tách Tập 2P
                    </Button>
                  </div>
                </Card>
              );
            })}
          </div>

          {filteredDramas.length === 0 && (
            <Card className="p-8 text-center space-y-2">
              <Film className="h-8 w-8 text-fg-subtle mx-auto opacity-50" />
              <p className="text-sm font-medium text-fg">Không tìm thấy kịch bản phù hợp</p>
              <p className="text-xs text-fg-subtle">
                Hãy thử tìm với từ khóa khác hoặc chuyển sang Studio để tự viết lại kịch bản tùy ý!
              </p>
            </Card>
          )}
        </div>
      )}

      {/* ─────────────────────────────────────────────────────────────
          TAB 2: BIÊN TẬP & TÁCH TẬP 2 PHÚT (EPISODE STUDIO)
      ───────────────────────────────────────────────────────────── */}
      {activeTab === 'studio' && (
        <div className="space-y-6">
          {/* Input & Settings Card */}
          <Card className="p-5 space-y-4">
            <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 border-b border-line pb-3">
              <div>
                <h3 className="font-semibold text-fg text-base flex items-center gap-2">
                  <Wand2 className="h-4 w-4 text-brand" /> Bộ Tách Kịch Bản Chuẩn 2 Phút / Tập
                </h3>
                <p className="text-xs text-fg-subtle">
                  Phân rã cốt truyện thành từng tập ngắn 2 phút (~300 từ lời đọc), có đầy đủ 5 phân cảnh, hook mở đầu 5s và cliffhanger giữ chân khán giả.
                </p>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <Button
                  variant="secondary"
                  size="sm"
                  icon={<Save className="h-3.5 w-3.5" />}
                  onClick={handleSaveCurrentProject}
                >
                  Lưu kịch bản
                </Button>
                <Button
                  variant="primary"
                  size="sm"
                  icon={<Sparkles className="h-3.5 w-3.5" />}
                  onClick={handleRewriteEpisodes}
                >
                  ⚡ Tách Tập 2 Phút Ngay
                </Button>
              </div>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <div className="md:col-span-2 space-y-3">
                <Field label="Tiêu đề phim / Kịch bản:">
                  <Input
                    value={studioTitle}
                    onChange={(e) => setStudioTitle(e.target.value)}
                    placeholder="VD: Chiến Thần Ở Rể, Trùng Sinh Vả Mặt Trà Xanh, Giả Nghèo Đi Xem Mắt..."
                  />
                </Field>

                <Field label="Cốt truyện / Kịch bản thô (Dán tóm tắt hoặc ý tưởng bất kỳ từ Douyin/TikTok):">
                  <Textarea
                    value={studioSourcePlot}
                    onChange={(e) => setStudioSourcePlot(e.target.value)}
                    rows={4}
                    placeholder="Dán tóm tắt cốt truyện, các nút thắt, cao trào hoặc kịch bản thô của bạn vào đây. Engine sẽ tự động viết lại và tách thành từng tập dài 2 phút..."
                  />
                </Field>
              </div>

              {/* Controls */}
              <div className="space-y-3 border-t md:border-t-0 md:border-l border-line md:pl-4 pt-3 md:pt-0">
                <Field label="Số tập muốn tách:">
                  <Select
                    value={String(studioNumEpisodes)}
                    onChange={(e) => setStudioNumEpisodes(Number(e.target.value))}
                  >
                    <option value="1">1 tập (Video ngắn 2 phút)</option>
                    <option value="2">2 tập (4 phút)</option>
                    <option value="3">3 tập (6 phút - Khuyên dùng)</option>
                    <option value="5">5 tập (Mini series 10 phút)</option>
                    <option value="8">8 tập (Phim dài)</option>
                    <option value="10">10 tập (Đại kết cục)</option>
                  </Select>
                </Field>

                <Field label="Phong cách viết lại:">
                  <Select
                    value={studioTone}
                    onChange={(e) => setStudioTone(e.target.value as DramaStyleTone)}
                  >
                    <option value="va_mat">🔥 Vả mặt cực gắt (Douyin Viral)</option>
                    <option value="kich_tinh">⚡ Kịch tính giật gân, nghẹt thở</option>
                    <option value="nguoc_luyen">💔 Ngược luyến tàn tâm, hối hận</option>
                    <option value="hai_huoc">😂 Hài hước bá đạo, châm biếm</option>
                  </Select>
                </Field>

                <div className="rounded-md bg-muted/60 p-2.5 text-[11px] text-fg-subtle space-y-1">
                  <p className="font-semibold text-fg flex items-center gap-1">
                    <Clock className="h-3 w-3 text-brand" /> Tiêu chuẩn mỗi tập:
                  </p>
                  <p>• Thời lượng: ~2 phút (120 giây)</p>
                  <p>• Dung lượng thoại: 280 - 340 từ</p>
                  <p>• Pacing: 5 phân cảnh dồn dập</p>
                  <p>• Hook 5s đầu + Cliffhanger kết tập</p>
                </div>
              </div>
            </div>
          </Card>

          {/* Episode Viewer & Action Copy Section */}
          {studioEpisodes.length > 0 ? (
            <div className="space-y-4">
              {/* Episode Tabs Switcher */}
              <div className="flex flex-wrap items-center justify-between gap-2 border-b border-line pb-2">
                <div className="flex items-center gap-1.5 overflow-x-auto">
                  {studioEpisodes.map((ep, idx) => (
                    <button
                      key={ep.episodeNumber}
                      type="button"
                      onClick={() => {
                        setSelectedEpIndex(idx);
                        setIsEditingScene(null);
                      }}
                      className={cn(
                        'px-3 py-1.5 rounded-md text-xs font-medium transition-all flex items-center gap-1.5',
                        selectedEpIndex === idx
                          ? 'bg-brand text-white shadow-sm'
                          : 'bg-muted/70 text-fg hover:bg-muted'
                      )}
                    >
                      <Film className="h-3 w-3" />
                      <span>Tập {ep.episodeNumber}</span>
                    </button>
                  ))}
                </div>

                {/* Batch Export Options */}
                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() =>
                      handleCopy(
                        'all_voiceover',
                        exportAllEpisodes(studioEpisodes, 'voiceover'),
                        'Đã sao chép lời đọc toàn bộ các tập!'
                      )
                    }
                    className="text-xs text-fg-subtle hover:text-brand flex items-center gap-1 font-medium"
                  >
                    <Copy className="h-3.5 w-3.5" /> Copy toàn bộ các tập (Voiceover)
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      const allText = exportAllEpisodes(studioEpisodes, 'production');
                      dramaStorage.downloadFile(
                        `${studioTitle || 'kich_ban'}_toan_bo_cac_tap.txt`,
                        allText
                      );
                      toast.success('Đã tải file kịch bản toàn bộ các tập!');
                    }}
                    className="text-xs text-fg-subtle hover:text-brand flex items-center gap-1 font-medium"
                  >
                    <Download className="h-3.5 w-3.5" /> Xuất file .TXT
                  </button>
                </div>
              </div>

              {/* Active Episode Card & Action Bar */}
              {currentEpisode && (
                <div className="space-y-4">
                  {/* Episode Summary & Quick Copy Action Bar */}
                  <Card className="p-4 bg-surface border-line space-y-3">
                    <div className="flex flex-col lg:flex-row items-start lg:items-center justify-between gap-3 border-b border-line pb-3">
                      <div>
                        <div className="flex items-center gap-2">
                          <h4 className="font-semibold text-fg text-base">
                            {currentEpisode.title}
                          </h4>
                          <Badge tone="info" className="text-xs">
                            {currentEpReadingTime.formatted} (~{currentEpReadingTime.words} từ)
                          </Badge>
                        </div>
                        <p className="text-xs text-fg-subtle mt-0.5">
                          Tập được định dạng tối ưu cho video ngắn 2 phút. Bấm các nút sao chép bên dưới để sử dụng ngay cho dựng phim.
                        </p>
                      </div>

                      {/* PRIMARY ACTION BUTTONS: BỎ QUA TRANG DỰNG PHIM */}
                      <div className="flex flex-wrap items-center gap-2">
                        {/* 1. Copy Voiceover Only */}
                        <Button
                          variant="primary"
                          size="sm"
                          icon={
                            copiedKey === `vo_${currentEpisode.episodeNumber}` ? (
                              <Check className="h-3.5 w-3.5" />
                            ) : (
                              <Volume2 className="h-3.5 w-3.5" />
                            )
                          }
                          onClick={() =>
                            handleCopy(
                              `vo_${currentEpisode.episodeNumber}`,
                              extractVoiceoverText(currentEpisode),
                              `Đã sao chép lời đọc Tập ${currentEpisode.episodeNumber} (Sạch thoại để dán vào CapCut TTS)!`
                            )
                          }
                          className="bg-emerald-600 hover:bg-emerald-700 text-white"
                          title="Sao chép chỉ phần lời thoại & lời dẫn để ném vào CapCut / ElevenLabs lồng tiếng"
                        >
                          📋 Copy Lời Đọc (TTS)
                        </Button>

                        {/* 2. Copy Full Production Script */}
                        <Button
                          variant="secondary"
                          size="sm"
                          icon={
                            copiedKey === `prod_${currentEpisode.episodeNumber}` ? (
                              <Check className="h-3.5 w-3.5" />
                            ) : (
                              <Clapperboard className="h-3.5 w-3.5" />
                            )
                          }
                          onClick={() =>
                            handleCopy(
                              `prod_${currentEpisode.episodeNumber}`,
                              formatProductionScript(currentEpisode),
                              `Đã sao chép kịch bản phân cảnh dựng phim Tập ${currentEpisode.episodeNumber}!`
                            )
                          }
                          title="Sao chép đầy đủ phân cảnh (Visual, Thoại, SFX) để dán vào công cụ dựng phim"
                        >
                          🎬 Copy Kịch Bản Dựng Phim
                        </Button>

                        {/* 3. Copy Visual Prompts */}
                        <Button
                          variant="secondary"
                          size="sm"
                          icon={<Video className="h-3.5 w-3.5" />}
                          onClick={() =>
                            handleCopy(
                              `vis_${currentEpisode.episodeNumber}`,
                              extractVisualPrompts(currentEpisode),
                              `Đã sao chép prompt hình ảnh Tập ${currentEpisode.episodeNumber}!`
                            )
                          }
                          title="Sao chép các mô tả hình ảnh để tạo video/ảnh với AI (Grok Imagine, Midjourney, Kling)"
                        >
                          🖼️ Copy Prompt Visual (AI)
                        </Button>

                        {/* 4. One-Click Transfer to Built-in Video Studio */}
                        <Button
                          variant="secondary"
                          size="sm"
                          icon={<ExternalLink className="h-3.5 w-3.5" />}
                          onClick={() => handleTransferToStudio(currentEpisode)}
                          className="border-brand/40 text-brand hover:bg-brand/10"
                          title="Nạp thẳng kịch bản này vào Studio Video AI của hệ thống"
                        >
                          🚀 Mở Trong Studio Dựng Phim
                        </Button>
                      </div>
                    </div>

                    {/* Hook & Cliffhanger Pills */}
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-xs">
                      <div className="rounded bg-amber-500/10 border border-amber-500/20 p-2 text-amber-700 dark:text-amber-400">
                        <span className="font-semibold flex items-center gap-1">
                          <Flame className="h-3 w-3" /> Hook mở màn 5s:
                        </span>
                        <p className="mt-0.5">{currentEpisode.hook}</p>
                      </div>
                      <div className="rounded bg-rose-500/10 border border-rose-500/20 p-2 text-rose-700 dark:text-rose-400">
                        <span className="font-semibold flex items-center gap-1">
                          <Sparkles className="h-3 w-3" /> Cliffhanger kết tập:
                        </span>
                        <p className="mt-0.5">{currentEpisode.cliffhanger}</p>
                      </div>
                    </div>
                  </Card>

                  {/* Scene by Scene Breakdown */}
                  <div className="space-y-3">
                    <div className="flex items-center justify-between">
                      <h5 className="font-medium text-xs text-fg-subtle uppercase tracking-wider flex items-center gap-1.5">
                        <Layers className="h-3.5 w-3.5 text-brand" /> Chi Tiết 5 Phân Cảnh (Chuẩn 2 Phút)
                      </h5>
                      <span className="text-xs text-fg-subtle">
                        Nhấp đúp hoặc bấm nút chỉnh sửa để gọt giũa lời thoại
                      </span>
                    </div>

                    {currentEpisode.scenes.map((scene, sIdx) => {
                      const isEditing = isEditingScene === sIdx;
                      return (
                        <Card key={scene.sceneNumber} className="p-4 space-y-3">
                          <div className="flex items-center justify-between border-b border-line pb-2">
                            <div className="flex items-center gap-2">
                              <span className="flex h-5 w-5 items-center justify-center rounded-full bg-brand text-[11px] font-bold text-white">
                                {scene.sceneNumber}
                              </span>
                              <span className="font-semibold text-fg text-xs">
                                {scene.title}
                              </span>
                              <Badge tone="neutral" className="text-[10px]">
                                {scene.timecode}
                              </Badge>
                            </div>
                            <Button
                              variant="ghost"
                              size="sm"
                              icon={<Edit3 className="h-3 w-3" />}
                              onClick={() =>
                                setIsEditingScene(isEditing ? null : sIdx)
                              }
                              className="text-[11px] h-6 px-2"
                            >
                              {isEditing ? 'Xong' : 'Chỉnh sửa'}
                            </Button>
                          </div>

                          {isEditing ? (
                            <div className="space-y-3 pt-1">
                              <Field label="Tiêu đề cảnh & Thời gian:">
                                <div className="grid grid-cols-2 gap-2">
                                  <Input
                                    value={scene.title}
                                    onChange={(e) =>
                                      handleUpdateScene(sIdx, 'title', e.target.value)
                                    }
                                  />
                                  <Input
                                    value={scene.timecode}
                                    onChange={(e) =>
                                      handleUpdateScene(sIdx, 'timecode', e.target.value)
                                    }
                                  />
                                </div>
                              </Field>
                              <Field label="Lời thoại & Lời dẫn lồng tiếng:">
                                <Textarea
                                  value={scene.voiceover}
                                  onChange={(e) =>
                                    handleUpdateScene(sIdx, 'voiceover', e.target.value)
                                  }
                                  rows={3}
                                />
                              </Field>
                              <Field label="Mô tả hình ảnh (Visual Prompt):">
                                <Textarea
                                  value={scene.visual}
                                  onChange={(e) =>
                                    handleUpdateScene(sIdx, 'visual', e.target.value)
                                  }
                                  rows={2}
                                />
                              </Field>
                              <Field label="Âm thanh / BGM / SFX:">
                                <Input
                                  value={scene.sound}
                                  onChange={(e) =>
                                    handleUpdateScene(sIdx, 'sound', e.target.value)
                                  }
                                />
                              </Field>
                            </div>
                          ) : (
                            <div className="grid grid-cols-1 md:grid-cols-12 gap-3 text-xs">
                              {/* Visual Prompt */}
                              <div className="md:col-span-5 rounded-md bg-muted/40 p-2.5 space-y-1">
                                <p className="font-semibold text-fg flex items-center gap-1 text-[11px]">
                                  <Video className="h-3 w-3 text-blue-500" /> Hình ảnh / Góc máy:
                                </p>
                                <p className="text-fg-subtle leading-relaxed">
                                  {scene.visual}
                                </p>
                              </div>

                              {/* Voiceover / Dialogue */}
                              <div className="md:col-span-5 rounded-md bg-emerald-500/5 border border-emerald-500/20 p-2.5 space-y-1">
                                <p className="font-semibold text-emerald-600 dark:text-emerald-400 flex items-center gap-1 text-[11px]">
                                  <Volume2 className="h-3 w-3" /> Lời đọc & Thoại (Voiceover):
                                </p>
                                <p className="text-fg leading-relaxed font-medium">
                                  {scene.voiceover}
                                </p>
                              </div>

                              {/* Sound / SFX */}
                              <div className="md:col-span-2 rounded-md bg-muted/40 p-2.5 space-y-1">
                                <p className="font-semibold text-fg flex items-center gap-1 text-[11px]">
                                  <Sparkles className="h-3 w-3 text-amber-500" /> Âm thanh SFX:
                                </p>
                                <p className="text-fg-subtle text-[11px]">
                                  {scene.sound}
                                </p>
                              </div>
                            </div>
                          )}
                        </Card>
                      );
                    })}
                  </div>
                </div>
              )}
            </div>
          ) : (
            <Card className="p-8 text-center space-y-3">
              <Film className="h-8 w-8 text-fg-subtle mx-auto opacity-50" />
              <p className="text-sm font-medium text-fg">Chưa có tập kịch bản nào</p>
              <p className="text-xs text-fg-subtle max-w-md mx-auto">
                Nhập tiêu đề hoặc dán tóm tắt ở trên rồi bấm nút "⚡ Tách Tập 2 Phút Ngay", hoặc chọn một phim có sẵn từ Kho Kịch Bản!
              </p>
              <Button
                variant="secondary"
                size="sm"
                icon={<Film className="h-3.5 w-3.5" />}
                onClick={() => setActiveTab('library')}
              >
                Chọn Từ Kho Kịch Bản
              </Button>
            </Card>
          )}
        </div>
      )}

      {/* ─────────────────────────────────────────────────────────────
          TAB 3: KỊCH BẢN ĐÃ LƯU (MY SAVED SCRIPTS / LOCAL VAULT)
      ───────────────────────────────────────────────────────────── */}
      {activeTab === 'saved' && (
        <div className="space-y-4">
          <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
            <div>
              <h3 className="font-semibold text-fg text-base">
                Kịch Bản Đã Lưu ({savedProjects.length})
              </h3>
              <p className="text-xs text-fg-subtle">
                Toàn bộ dữ liệu nằm trong LocalStorage trình duyệt của bạn. Hoàn toàn an toàn, không tốn tài nguyên server Railway.
              </p>
            </div>
            <div className="flex items-center gap-2">
              <Button
                variant="secondary"
                size="sm"
                icon={<Download className="h-3.5 w-3.5" />}
                onClick={() => dramaStorage.exportBackupJson()}
              >
                Sao lưu JSON
              </Button>
              <label className="cursor-pointer">
                <input
                  type="file"
                  accept=".json"
                  className="hidden"
                  onChange={(e) => {
                    const file = e.target.files?.[0];
                    if (!file) return;
                    const reader = new FileReader();
                    reader.onload = (ev) => {
                      const res = dramaStorage.importBackupJson(String(ev.target?.result));
                      if (res.success) {
                        setSavedProjects(dramaStorage.getSavedProjects());
                        toast.success(`Đã khôi phục thành công ${res.count} kịch bản!`);
                      } else {
                        toast.error(res.error || 'Lỗi khi đọc file JSON');
                      }
                    };
                    reader.readAsText(file);
                  }}
                />
                <Button variant="secondary" size="sm" icon={<Upload className="h-3.5 w-3.5" />}>
                  Nhập file JSON
                </Button>
              </label>
            </div>
          </div>

          <div className="space-y-3">
            {savedProjects.map((proj) => (
              <Card key={proj.id} className="p-4 space-y-3">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-line pb-2">
                  <div>
                    <h4 className="font-semibold text-fg text-base">{proj.title}</h4>
                    <p className="text-xs text-fg-subtle">
                      {proj.episodes.length} tập • Lưu lúc: {new Date(proj.updatedAt).toLocaleString('vi-VN')}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <Button
                      variant="primary"
                      size="sm"
                      icon={<Edit3 className="h-3.5 w-3.5" />}
                      onClick={() => {
                        setStudioTitle(proj.title);
                        setStudioSourcePlot(proj.synopsis);
                        setStudioNumEpisodes(proj.episodes.length);
                        setStudioEpisodes(proj.episodes);
                        setSelectedEpIndex(0);
                        setActiveTab('studio');
                        toast.success(`Đã mở lại kịch bản "${proj.title}"!`);
                      }}
                      className="text-xs"
                    >
                      Mở lại Studio
                    </Button>
                    <Button
                      variant="secondary"
                      size="sm"
                      icon={<Copy className="h-3.5 w-3.5" />}
                      onClick={() =>
                        handleCopy(
                          `saved_${proj.id}`,
                          exportAllEpisodes(proj.episodes, 'voiceover'),
                          'Đã copy toàn bộ lời thoại kịch bản này!'
                        )
                      }
                      className="text-xs"
                    >
                      Copy Thoại
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      icon={<Trash2 className="h-3.5 w-3.5 text-rose-500" />}
                      onClick={() => {
                        if (confirm(`Bạn có chắc muốn xóa kịch bản "${proj.title}"?`)) {
                          dramaStorage.deleteProject(proj.id);
                          setSavedProjects(dramaStorage.getSavedProjects());
                          toast.info('Đã xóa kịch bản!');
                        }
                      }}
                      className="text-xs"
                    />
                  </div>
                </div>

                {proj.synopsis && (
                  <p className="text-xs text-fg-subtle line-clamp-2">{proj.synopsis}</p>
                )}

                <div className="flex flex-wrap gap-2 pt-1 text-xs">
                  {proj.episodes.map((ep) => (
                    <span
                      key={ep.episodeNumber}
                      className="rounded bg-muted px-2 py-0.5 text-fg-subtle font-medium"
                    >
                      Tập {ep.episodeNumber}: {ep.title.replace(/^Tập \d+:\s*/, '')}
                    </span>
                  ))}
                </div>
              </Card>
            ))}

            {savedProjects.length === 0 && (
              <Card className="p-8 text-center space-y-2">
                <FileText className="h-8 w-8 text-fg-subtle mx-auto opacity-50" />
                <p className="text-sm font-medium text-fg">Chưa có kịch bản nào được lưu</p>
                <p className="text-xs text-fg-subtle">
                  Các kịch bản bạn tạo và lưu trong Studio sẽ hiển thị tại đây.
                </p>
              </Card>
            )}
          </div>
        </div>
      )}

      {/* ─────────────────────────────────────────────────────────────
          PREVIEW MODAL (QUICK VIEW DRAMA DETAILS & EPISODES)
      ───────────────────────────────────────────────────────────── */}
      {previewDrama && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm">
          <Card className="max-h-[90vh] w-full max-w-3xl overflow-y-auto p-6 space-y-5 shadow-2xl">
            <div className="flex items-start justify-between border-b border-line pb-3">
              <div>
                <div className="flex items-center gap-2 flex-wrap">
                  <Badge tone="info">{previewDrama.genre}</Badge>
                  {previewDrama.isFullSeries && (
                    <Badge tone="success">Trọn Bộ Đầy Đủ ({previewDrama.episodes.length} tập đến Đại Kết Cục)</Badge>
                  )}
                  <span className="text-xs text-amber-500 font-semibold">
                    ⭐ {previewDrama.addictionRating.toFixed(1)} ({previewDrama.viewsEstimate})
                  </span>
                </div>
                <h3 className="text-lg font-bold text-fg mt-1">{previewDrama.title}</h3>
                <p className="text-xs text-fg-subtle italic">{previewDrama.originalTitle}</p>
              </div>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setPreviewDrama(null)}
                className="text-fg-subtle hover:text-fg"
              >
                ✕ Đóng
              </Button>
            </div>

            {/* Hook */}
            <div className="rounded-lg bg-amber-500/10 border border-amber-500/20 p-3 text-xs text-amber-800 dark:text-amber-300">
              <strong className="flex items-center gap-1 text-amber-600 dark:text-amber-400">
                <Flame className="h-4 w-4" /> Điểm gây nghiện mở màn:
              </strong>
              <p className="mt-1">{previewDrama.hook}</p>
            </div>

            {/* Synopsis */}
            <div className="space-y-1">
              <h5 className="font-semibold text-xs text-fg uppercase tracking-wide">Cốt truyện:</h5>
              <p className="text-xs text-fg-subtle leading-relaxed">{previewDrama.synopsis}</p>
            </div>

            {/* Characters */}
            <div className="space-y-2">
              <h5 className="font-semibold text-xs text-fg uppercase tracking-wide">Tuyến nhân vật:</h5>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-xs">
                {previewDrama.characters.map((c) => (
                  <div key={c.name} className="rounded-md border border-line p-2">
                    <p className="font-semibold text-fg">
                      {c.name} <span className="text-brand font-normal">({c.role})</span>
                    </p>
                    <p className="text-fg-subtle text-[11px] mt-0.5">{c.description}</p>
                  </div>
                ))}
              </div>
            </div>

            {/* Pre-made Episodes Preview */}
            <div className="space-y-2">
              <h5 className="font-semibold text-xs text-fg uppercase tracking-wide">
                Các tập mẫu ({previewDrama.episodes.length} tập chuẩn 2p):
              </h5>
              <div className="space-y-2">
                {previewDrama.episodes.map((ep) => (
                  <div key={ep.episodeNumber} className="rounded-md bg-muted/40 p-2.5 text-xs space-y-1">
                    <div className="flex items-center justify-between">
                      <span className="font-semibold text-fg">{ep.title}</span>
                      <span className="text-fg-subtle text-[11px] flex items-center gap-1">
                        <Clock className="h-3 w-3" /> ~2 phút (5 phân cảnh)
                      </span>
                    </div>
                    <p className="text-fg-subtle text-[11px]">
                      <strong>Hook:</strong> {ep.hook}
                    </p>
                  </div>
                ))}
              </div>
            </div>

            {/* Modal Bottom Actions */}
            <div className="flex items-center justify-end gap-2 border-t border-line pt-4">
              <Button variant="secondary" size="sm" onClick={() => setPreviewDrama(null)}>
                Đóng
              </Button>
              <Button
                variant="primary"
                size="sm"
                icon={<Wand2 className="h-4 w-4" />}
                onClick={() => {
                  handleLoadDramaToStudio(previewDrama);
                  setPreviewDrama(null);
                }}
              >
                Đưa Vào Studio Tách Tập 2P
              </Button>
            </div>
          </Card>
        </div>
      )}
    </div>
  );
}

function Save(props: { className?: string }) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={props.className}
    >
      <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z" />
      <polyline points="17 21 17 13 7 13 7 21" />
      <polyline points="7 3 7 8 15 8" />
    </svg>
  );
}
