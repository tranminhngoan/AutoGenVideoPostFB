import React, { useEffect, useState } from 'react';
import {
  Film,
  Sparkles,
  Plus,
  Play,
  RotateCw,
  Trash2,
  DollarSign,
  ShieldCheck,
  Video,
  Clapperboard,
  Tv,
  Eye,
  CheckCircle2,
  Clock,
  Layers,
  Sliders,
  ExternalLink,
  ChevronRight,
  BookOpen,
  Users,
  MapPin,
  Flame,
  AlertCircle,
  Download
} from 'lucide-react';
import { Button, Card, Dot, Modal, cn } from '../components/ui';
import {
  api,
  FilmPageDto,
  FilmProjectDto,
  FilmProjectDetailDto,
  CreateFilmProjectRequestDto,
  FilmEpisodeDto,
  FilmEpisodeDetailDto,
  FilmCostSummaryDto
} from '../services/api';
import { useToast } from '../components/feedback';

const BUSY_EPISODE_STATUSES = ['Queued', 'Scripting', 'VisualAssetsGenerating', 'VoiceGenerating', 'GpuQueued', 'GpuRendering', 'GpuDone', 'FilmEditing', 'QualityCheck'];
const BUSY_PROJECT_STATUSES = ['ShowrunnerPlanning', 'BibleReady'];

const EPISODE_STATUS_LABELS: Record<string, string> = {
  Pending: 'Chưa viết kịch bản',
  ScriptReady: 'Đã có kịch bản',
  Queued: 'Đang chờ dựng…',
  Scripting: 'Đang viết kịch bản…',
  VisualAssetsGenerating: 'Đang vẽ khung hình…',
  VoiceGenerating: 'Đang lồng tiếng…',
  GpuQueued: 'Chờ máy GPU…',
  GpuRendering: 'GPU đang vẽ ảnh & clip…',
  GpuDone: 'GPU xong, đang ghép…',
  FilmEditing: 'Đang dựng video…',
  QualityCheck: 'Đang kiểm tra…',
  Completed: 'Hoàn tất',
  Failed: 'Lỗi',
};

export function FilmFactoryPage() {
  const toast = useToast();

  // Navigation tabs
  const [activeTab, setActiveTab] = useState<'pages' | 'projects' | 'detail' | 'cost'>('projects');

  // Data
  const [pages, setPages] = useState<FilmPageDto[]>([]);
  const [projects, setProjects] = useState<FilmProjectDto[]>([]);
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);
  const [selectedProject, setSelectedProject] = useState<FilmProjectDetailDto | null>(null);
  const [selectedEpisode, setSelectedEpisode] = useState<FilmEpisodeDetailDto | null>(null);
  const [previewVideoEpisode, setPreviewVideoEpisode] = useState<FilmEpisodeDto | null>(null);
  const [costSummary, setCostSummary] = useState<FilmCostSummaryDto | null>(null);

  // Loading states
  const [loadingPages, setLoadingPages] = useState(false);
  const [loadingProjects, setLoadingProjects] = useState(false);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [producing, setProducing] = useState(false);
  const [renderingEpisodeId, setRenderingEpisodeId] = useState<string | null>(null);

  // Create Project Modal
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [createForm, setCreateForm] = useState<CreateFilmProjectRequestDto>({
    filmPageId: '',
    title: '',
    logline: '',
    genre: 'Sci-Fi / Thriller',
    visualArtStyle: 'Cinematic 35mm Realism',
    aspectRatio: '9:16',
    budgetLimitUsd: 5.0,
    autoProduceAll: true,
  });

  // Series Detail Workbench Tab
  const [detailTab, setDetailTab] = useState<'story' | 'characters' | 'locations' | 'episodes' | 'cost'>('episodes');

  // Load Pages
  const loadPages = async () => {
    try {
      setLoadingPages(true);
      const data = await api.getFilmPages();
      setPages(data);
      if (data.length > 0 && !createForm.filmPageId) {
        setCreateForm((prev) => ({ ...prev, filmPageId: data[0].id }));
      }
    } catch (err: any) {
      toast.error(err.message || 'Lỗi tải danh sách Film Page');
    } finally {
      setLoadingPages(false);
    }
  };

  // Load Projects
  const loadProjects = async () => {
    try {
      setLoadingProjects(true);
      const data = await api.getFilmProjects();
      setProjects(data);
      if (data.length > 0 && !selectedProjectId) {
        setSelectedProjectId(data[0].id);
      }
    } catch (err: any) {
      toast.error(err.message || 'Lỗi tải danh sách dự án phim');
    } finally {
      setLoadingProjects(false);
    }
  };

  // Load Project Detail
  const loadProjectDetail = async (id: string) => {
    try {
      setLoadingDetail(true);
      const data = await api.getFilmProjectDetail(id);
      setSelectedProject(data);
      setCostSummary(data.costSummary);
    } catch (err: any) {
      toast.error(err.message || 'Lỗi tải chi tiết dự án');
    } finally {
      setLoadingDetail(false);
    }
  };

  // Initial Load
  useEffect(() => {
    loadPages();
    loadProjects();
  }, []);

  // Sync Project Detail when selection changes
  useEffect(() => {
    if (selectedProjectId) {
      loadProjectDetail(selectedProjectId);
    }
  }, [selectedProjectId]);

  // Poll while the server is writing or rendering something for this project
  const projectBusy =
    !!selectedProject &&
    (BUSY_PROJECT_STATUSES.includes(selectedProject.status) ||
      selectedProject.episodes.some((e) => BUSY_EPISODE_STATUSES.includes(e.status)));
  useEffect(() => {
    if (!selectedProjectId || !projectBusy) return;
    const timer = window.setInterval(async () => {
      try {
        const data = await api.getFilmProjectDetail(selectedProjectId);
        setSelectedProject(data);
        setCostSummary(data.costSummary);
      } catch {
        // keep the last known state; the next tick retries
      }
    }, 8000);
    return () => window.clearInterval(timer);
  }, [selectedProjectId, projectBusy]);

  // Handle Create Project
  const handleCreateProject = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!createForm.title.trim() || !createForm.logline.trim() || !createForm.filmPageId) {
      toast.info('Vui lòng nhập đầy đủ tiêu đề, ý tưởng và chọn Film Page');
      return;
    }

    try {
      setProducing(true);
      const created = await api.createFilmProject(createForm);
      toast.success(`Đã tạo series "${created.title}" và kích hoạt AI Showrunner!`);
      setShowCreateModal(false);
      await loadProjects();
      setSelectedProjectId(created.id);
      setActiveTab('detail');
    } catch (err: any) {
      toast.error(err.message || 'Tạo dự án phim thất bại');
    } finally {
      setProducing(false);
    }
  };

  // Trigger Produce
  const handleTriggerProduce = async (id: string) => {
    try {
      setProducing(true);
      await api.produceFilmProject(id);
      toast.success('Đã kích hoạt toàn bộ quy trình sản xuất phim 10 tập!');
      await loadProjectDetail(id);
      await loadProjects();
    } catch (err: any) {
      toast.error(err.message || 'Kích hoạt sản xuất thất bại');
    } finally {
      setProducing(false);
    }
  };

  // Render Episode — runs in the background on the server (several minutes); the page polls for progress.
  const handleRenderEpisode = async (episodeId: string) => {
    try {
      setRenderingEpisodeId(episodeId);
      const res = await api.renderFilmEpisode(episodeId);
      toast.success(`${res.message} Quá trình mất vài phút, trang sẽ tự cập nhật.`);
      if (selectedProjectId) {
        await loadProjectDetail(selectedProjectId);
      }
    } catch (err: any) {
      toast.error(err.message || 'Dựng tập phim thất bại');
    } finally {
      setRenderingEpisodeId(null);
    }
  };

  // View Episode Shots
  const handleViewEpisodeShots = async (episodeId: string) => {
    try {
      const ep = await api.getFilmEpisodeDetail(episodeId);
      setSelectedEpisode(ep);
    } catch (err: any) {
      toast.error(err.message || 'Không thể xem phân cảnh tập');
    }
  };

  return (
    <div className="space-y-6 pb-12">
      {/* ── Luxury Tech Top Header ── */}
      <div className="relative overflow-hidden rounded-xl border border-indigo-500/20 bg-gradient-to-r from-slate-950 via-indigo-950/40 to-slate-950 p-6 shadow-2xl backdrop-blur-xl">
        <div className="absolute -right-10 -top-10 h-48 w-48 rounded-full bg-indigo-500/10 blur-3xl" />
        <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
          <div className="space-y-1.5">
            <div className="inline-flex items-center gap-2 rounded-full border border-indigo-400/30 bg-indigo-500/10 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-indigo-300">
              <Sparkles className="h-3.5 w-3.5 text-amber-400" />
              AI Film Factory v2.0 · 5 Film Pages · 10 Tập × 5 Phút
            </div>
            <h1 className="text-2xl font-bold tracking-tight text-white md:text-3xl">
              Hệ Thống Tự Động Sản Xuất Phim Người Thật AI
            </h1>
            <p className="max-w-2xl text-sm text-slate-300">
              Showrunner AI đa tác tử: Kịch bản 10 tập, giữ vững tính nhất quán nhân vật, EdgeTTS diễn xuất tiếng Việt,
              và dựng phim điện ảnh tốc độ cao với mức ngân sách tuyệt đối &lt; $5.00/Series.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <Button
              variant="secondary"
              icon={<RotateCw className={cn('h-4 w-4', (loadingProjects || loadingPages) && 'animate-spin')} />}
              onClick={() => {
                loadPages();
                loadProjects();
                if (selectedProjectId) loadProjectDetail(selectedProjectId);
              }}
            >
              Làm mới
            </Button>
            <Button
              variant="accent"
              className="bg-indigo-600 font-semibold text-white hover:bg-indigo-500 shadow-lg shadow-indigo-600/30"
              icon={<Plus className="h-4 w-4" />}
              onClick={() => setShowCreateModal(true)}
            >
              Khai Hoả Series Mới
            </Button>
          </div>
        </div>

        {/* Quick Stats Ribbon */}
        <div className="mt-6 grid grid-cols-2 gap-3 border-t border-slate-800/80 pt-4 md:grid-cols-4">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-indigo-500/20 text-indigo-400">
              <Tv className="h-5 w-5" />
            </div>
            <div>
              <div className="text-xs text-slate-400">Film Pages Hoạt Động</div>
              <div className="text-base font-bold text-white">{pages.length} / 5 Kênh Phim</div>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-amber-500/20 text-amber-400">
              <Film className="h-5 w-5" />
            </div>
            <div>
              <div className="text-xs text-slate-400">Tổng Series Phim</div>
              <div className="text-base font-bold text-white">{projects.length} Dự án</div>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-emerald-500/20 text-emerald-400">
              <Clapperboard className="h-5 w-5" />
            </div>
            <div>
              <div className="text-xs text-slate-400">Định Dạng Chuẩn</div>
              <div className="text-base font-bold text-white">10 Tập × 5 Phút (50 Phút)</div>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-cyan-500/20 text-cyan-400">
              <ShieldCheck className="h-5 w-5" />
            </div>
            <div>
              <div className="text-xs text-slate-400">Bảo Vệ Ngân Sách</div>
              <div className="text-base font-bold text-emerald-400">&lt; $5.00 / Series</div>
            </div>
          </div>
        </div>
      </div>

      {/* ── Navigation Tabs ── */}
      <div className="flex border-b border-line">
        <button
          onClick={() => setActiveTab('projects')}
          className={cn(
            'flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-medium transition-colors',
            activeTab === 'projects'
              ? 'border-indigo-500 text-indigo-500'
              : 'border-transparent text-fg-muted hover:text-fg'
          )}
        >
          <Clapperboard className="h-4 w-4" />
          Dự án Series Phim ({projects.length})
        </button>
        <button
          onClick={() => setActiveTab('pages')}
          className={cn(
            'flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-medium transition-colors',
            activeTab === 'pages'
              ? 'border-indigo-500 text-indigo-500'
              : 'border-transparent text-fg-muted hover:text-fg'
          )}
        >
          <Tv className="h-4 w-4" />
          5 Kênh Phim Chuyên Biệt ({pages.length})
        </button>
        {selectedProjectId && (
          <button
            onClick={() => setActiveTab('detail')}
            className={cn(
              'flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-medium transition-colors',
              activeTab === 'detail'
                ? 'border-indigo-500 text-indigo-500'
                : 'border-transparent text-fg-muted hover:text-fg'
            )}
          >
            <Sparkles className="h-4 w-4" />
            Studio Series: {selectedProject?.title || 'Đang mở'}
          </button>
        )}
      </div>

      {/* ── TAB 1: 5 FILM PAGES ── */}
      {activeTab === 'pages' && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {pages.map((p) => (
            <Card key={p.id} className="relative overflow-hidden border-line hover:border-indigo-500/50 transition-all">
              <div
                className="h-2 w-full"
                style={{ backgroundColor: p.brandColorHex || '#6366F1' }}
              />
              <div className="p-5 space-y-4">
                <div className="flex items-start justify-between gap-3">
                  <div className="flex items-center gap-3">
                    <div
                      className="flex h-12 w-12 items-center justify-center rounded-xl font-bold text-white shadow"
                      style={{ backgroundColor: p.brandColorHex || '#6366F1' }}
                    >
                      P{p.pageNumber}
                    </div>
                    <div>
                      <h3 className="font-semibold text-fg">{p.name}</h3>
                      <div className="text-xs text-fg-subtle">{p.primaryGenre}</div>
                    </div>
                  </div>
                  <span className="rounded-full bg-emerald-500/10 px-2 py-0.5 text-xs font-medium text-emerald-500">
                    Hoạt động
                  </span>
                </div>

                <p className="text-xs text-fg-muted line-clamp-2">{p.description}</p>

                <div className="space-y-1.5 rounded-lg bg-muted/60 p-3 text-xs">
                  <div className="flex justify-between">
                    <span className="text-fg-subtle">Phong cách:</span>
                    <span className="font-medium text-fg">{p.visualArtStyle}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-fg-subtle">Giọng đọc chính:</span>
                    <span className="font-medium text-fg">{p.defaultVoice}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-fg-subtle">Khung hình:</span>
                    <span className="font-medium text-fg">{p.aspectRatio}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-fg-subtle">Tập hoàn thành:</span>
                    <span className="font-medium text-emerald-500">{p.totalEpisodesCompleted} tập</span>
                  </div>
                </div>

                <div className="flex items-center justify-between pt-2">
                  <div className="text-xs text-fg-subtle">
                    {p.linkedFacebookPageName ? (
                      <span className="text-indigo-400 font-medium">FB: {p.linkedFacebookPageName}</span>
                    ) : (
                      'Chưa kết nối FB'
                    )}
                  </div>
                  <Button
                    size="sm"
                    variant="ghost"
                    onClick={() => {
                      setCreateForm((prev) => ({ ...prev, filmPageId: p.id, genre: p.primaryGenre }));
                      setShowCreateModal(true);
                    }}
                  >
                    Tạo Series &rarr;
                  </Button>
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      {/* ── TAB 2: PROJECTS LIST ── */}
      {activeTab === 'projects' && (
        <div className="space-y-4">
          {projects.length === 0 ? (
            <div className="rounded-xl border border-dashed border-line p-12 text-center">
              <Film className="mx-auto h-12 w-12 text-fg-subtle" />
              <h3 className="mt-4 text-base font-semibold text-fg">Chưa có series phim nào</h3>
              <p className="mt-1 text-sm text-fg-subtle">
                Hãy bấm "Khai Hoả Series Mới" để AI Showrunner tự động lên kịch bản 10 tập và sản xuất.
              </p>
              <Button
                variant="accent"
                className="mt-4 bg-indigo-600 text-white"
                onClick={() => setShowCreateModal(true)}
              >
                Tạo Series Đầu Tiên
              </Button>
            </div>
          ) : (
            <div className="grid grid-cols-1 gap-4">
              {projects.map((proj) => (
                <Card
                  key={proj.id}
                  className={cn(
                    'overflow-hidden border-line transition-all hover:border-indigo-500/40',
                    selectedProjectId === proj.id && 'border-indigo-500/80 shadow-md ring-1 ring-indigo-500/20'
                  )}
                >
                  <div className="p-5 flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                    <div className="space-y-2 flex-1">
                      <div className="flex items-center gap-2">
                        <span className="rounded bg-indigo-500/10 px-2 py-0.5 text-xs font-semibold text-indigo-400">
                          {proj.filmPageName}
                        </span>
                        <span className="rounded bg-muted px-2 py-0.5 text-xs text-fg-subtle">
                          {proj.genre}
                        </span>
                        <span
                          className={cn(
                            'rounded-full px-2 py-0.5 text-xs font-medium',
                            proj.status === 'Completed'
                              ? 'bg-emerald-500/10 text-emerald-400'
                              : proj.status === 'Failed'
                              ? 'bg-rose-500/10 text-rose-400'
                              : 'bg-amber-500/10 text-amber-400'
                          )}
                        >
                          {proj.status}
                        </span>
                      </div>

                      <h3 className="text-lg font-bold text-fg">{proj.title}</h3>
                      <p className="text-sm text-fg-muted max-w-3xl line-clamp-2">{proj.logline}</p>

                      <div className="flex flex-wrap items-center gap-4 text-xs text-fg-subtle pt-1">
                        <span>
                          Tiến độ: <strong className="text-fg">{proj.completedEpisodesCount} / {proj.targetEpisodes} tập</strong> (Mỗi tập ~5 phút)
                        </span>
                        <span>
                          Phong cách: <strong className="text-fg">{proj.visualArtStyle}</strong>
                        </span>
                        <span>
                          Chi phí: <strong className="text-emerald-500">${proj.totalActualCostUsd.toFixed(4)}</strong> / ${proj.totalBudgetLimitUsd.toFixed(2)}
                        </span>
                      </div>
                    </div>

                    <div className="flex items-center gap-2 self-end md:self-center">
                      <Button
                        size="sm"
                        variant="secondary"
                        icon={<Eye className="h-4 w-4" />}
                        onClick={() => {
                          setSelectedProjectId(proj.id);
                          setActiveTab('detail');
                        }}
                      >
                        Vào Studio
                      </Button>
                      <Button
                        size="sm"
                        variant="accent"
                        className="bg-indigo-600 text-white"
                        icon={<Play className="h-4 w-4" />}
                        loading={producing && selectedProjectId === proj.id}
                        onClick={() => handleTriggerProduce(proj.id)}
                      >
                        Sản Xuất
                      </Button>
                    </div>
                  </div>
                </Card>
              ))}
            </div>
          )}
        </div>
      )}

      {/* ── TAB 3: PROJECT WORKBENCH (DETAIL) ── */}
      {activeTab === 'detail' && selectedProject && (
        <div className="space-y-6">
          {/* Workbench Top Bar */}
          <div className="flex flex-col gap-3 rounded-lg border border-line bg-surface p-4 md:flex-row md:items-center md:justify-between">
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-xl font-bold text-fg">{selectedProject.title}</h2>
                <span className="rounded bg-indigo-500/10 px-2 py-0.5 text-xs font-semibold text-indigo-400">
                  {selectedProject.filmPageName}
                </span>
                <span className="rounded bg-muted px-2 py-0.5 text-xs text-fg-muted">
                  {selectedProject.status}
                </span>
              </div>
              <p className="text-xs text-fg-subtle mt-0.5">
                Chi phí thực tế: <span className="font-semibold text-emerald-400">${selectedProject.totalActualCostUsd.toFixed(4)}</span> / ${selectedProject.totalBudgetLimitUsd.toFixed(2)} USD
              </p>
            </div>

            <div className="flex items-center gap-2">
              <Button
                variant="secondary"
                size="sm"
                icon={<RotateCw className="h-4 w-4" />}
                onClick={() => loadProjectDetail(selectedProject.id)}
              >
                Tải lại
              </Button>
              <Button
                variant="accent"
                size="sm"
                className="bg-indigo-600 text-white"
                icon={<Play className="h-4 w-4" />}
                loading={producing}
                onClick={() => handleTriggerProduce(selectedProject.id)}
              >
                Chạy toàn bộ 10 tập
              </Button>
            </div>
          </div>

          {/* Workbench Inner Tabs */}
          <div className="flex border-b border-line text-sm">
            <button
              onClick={() => setDetailTab('episodes')}
              className={cn(
                'flex items-center gap-2 border-b-2 px-4 py-2 font-medium',
                detailTab === 'episodes'
                  ? 'border-indigo-500 text-indigo-500'
                  : 'border-transparent text-fg-muted hover:text-fg'
              )}
            >
              <Clapperboard className="h-4 w-4" />
              10 Tập Phim ({selectedProject.episodes.length}/10)
            </button>
            <button
              onClick={() => setDetailTab('story')}
              className={cn(
                'flex items-center gap-2 border-b-2 px-4 py-2 font-medium',
                detailTab === 'story'
                  ? 'border-indigo-500 text-indigo-500'
                  : 'border-transparent text-fg-muted hover:text-fg'
              )}
            >
              <BookOpen className="h-4 w-4" />
              Story Bible
            </button>
            <button
              onClick={() => setDetailTab('characters')}
              className={cn(
                'flex items-center gap-2 border-b-2 px-4 py-2 font-medium',
                detailTab === 'characters'
                  ? 'border-indigo-500 text-indigo-500'
                  : 'border-transparent text-fg-muted hover:text-fg'
              )}
            >
              <Users className="h-4 w-4" />
              Nhân Vật ({selectedProject.characters.length})
            </button>
            <button
              onClick={() => setDetailTab('locations')}
              className={cn(
                'flex items-center gap-2 border-b-2 px-4 py-2 font-medium',
                detailTab === 'locations'
                  ? 'border-indigo-500 text-indigo-500'
                  : 'border-transparent text-fg-muted hover:text-fg'
              )}
            >
              <MapPin className="h-4 w-4" />
              Bối Cảnh ({selectedProject.locations.length})
            </button>
            <button
              onClick={() => setDetailTab('cost')}
              className={cn(
                'flex items-center gap-2 border-b-2 px-4 py-2 font-medium',
                detailTab === 'cost'
                  ? 'border-indigo-500 text-indigo-500'
                  : 'border-transparent text-fg-muted hover:text-fg'
              )}
            >
              <DollarSign className="h-4 w-4" />
              Ngân Sách AI (${selectedProject.totalActualCostUsd.toFixed(4)})
            </button>
          </div>

          {/* ── DETAIL: 10 EPISODES GRID ── */}
          {detailTab === 'episodes' && (
            <div className="space-y-4">
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                {selectedProject.episodes.map((ep) => (
                  <Card key={ep.id} className="p-4 border-line space-y-3">
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex items-center gap-2">
                        <span className="flex h-7 w-7 items-center justify-center rounded bg-indigo-500/20 font-bold text-xs text-indigo-400">
                          E{ep.episodeNumber}
                        </span>
                        <div>
                          <h4 className="font-semibold text-fg text-sm">{ep.title}</h4>
                          <span className="text-[11px] text-fg-subtle">
                            Thời lượng mục tiêu: {Math.round(ep.targetDurationSeconds / 60)} phút ({ep.scenesCount} cảnh · {ep.shotsCount} shots)
                          </span>
                        </div>
                      </div>
                      <span
                        className={cn(
                          'rounded-full px-2 py-0.5 text-[11px] font-medium',
                          ep.status === 'Completed'
                            ? 'bg-emerald-500/10 text-emerald-400'
                            : ep.status === 'Failed'
                              ? 'bg-red-500/10 text-red-400'
                              : 'bg-amber-500/10 text-amber-400'
                        )}
                      >
                        {EPISODE_STATUS_LABELS[ep.status] ?? ep.status}
                      </span>
                    </div>

                    <p className="text-xs text-fg-muted line-clamp-2">{ep.synopsis}</p>

                    {ep.errorMessage ? (
                      <div
                        className={cn(
                          'rounded border p-2 text-[11px] whitespace-pre-wrap break-words',
                          ep.status === 'Failed'
                            ? 'border-red-500/30 bg-red-500/10 text-red-400'
                            : 'border-amber-500/30 bg-amber-500/10 text-amber-400'
                        )}
                      >
                        {ep.errorMessage}
                      </div>
                    ) : null}

                    <div className="rounded bg-muted/60 p-2 text-[11px] space-y-1">
                      <div className="text-fg-subtle">
                        <strong>Mở đầu:</strong> {ep.openingHook}
                      </div>
                      <div className="text-fg-subtle">
                        <strong>Kết nút thắt:</strong> {ep.endingCliffhanger}
                      </div>
                    </div>

                    {ep.outputVideoPath || ep.status === 'Completed' ? (
                      <div className="relative group overflow-hidden rounded-lg bg-black border border-indigo-500/40 shadow-lg">
                        <div
                          className="aspect-[16/9] w-full bg-slate-950 flex flex-col items-center justify-center relative cursor-pointer hover:bg-slate-900 transition-colors"
                          onClick={() => setPreviewVideoEpisode(ep)}
                        >
                          <div className="flex h-12 w-12 items-center justify-center rounded-full bg-indigo-600/90 text-white shadow-xl transition-transform group-hover:scale-110">
                            <Play className="h-6 w-6 ml-0.5 fill-white" />
                          </div>
                          <span className="mt-2 text-xs font-semibold text-white/95">
                            Bấm để xem thử video tập {ep.episodeNumber}
                          </span>
                          <span className="text-[10px] text-slate-400">
                            Thời lượng: ~{Math.round(ep.actualDurationSeconds || ep.targetDurationSeconds)}s · QA: {Math.round(ep.qualityScore)}/100
                          </span>
                        </div>
                        <div className="p-2.5 bg-slate-900 border-t border-slate-800 flex items-center justify-between text-xs">
                          <span className="text-emerald-400 font-medium flex items-center gap-1.5">
                            <CheckCircle2 className="h-3.5 w-3.5" /> Video MP4 sẵn sàng
                          </span>
                          <div className="flex items-center gap-2">
                            <a
                              href={api.getFilmEpisodeDownloadUrl(ep.id)}
                              download
                              className="text-xs text-indigo-400 hover:text-indigo-300 font-medium inline-flex items-center gap-1"
                              onClick={(e) => e.stopPropagation()}
                            >
                              <Download className="h-3 w-3" />
                              Tải MP4
                            </a>
                            <button
                              type="button"
                              onClick={() => setPreviewVideoEpisode(ep)}
                              className="rounded bg-indigo-600 px-2.5 py-1 text-xs font-semibold text-white hover:bg-indigo-500 shadow"
                            >
                              Xem trước
                            </button>
                          </div>
                        </div>
                      </div>
                    ) : null}

                    <div className="flex items-center justify-between pt-2 border-t border-line">
                      <span className="text-[11px] text-fg-subtle">
                        Chi phí: ${ep.actualCostUsd.toFixed(4)}
                      </span>
                      <div className="flex items-center gap-2">
                        {ep.outputVideoPath || ep.status === 'Completed' ? (
                          <Button
                            size="sm"
                            variant="secondary"
                            icon={<Eye className="h-3 w-3" />}
                            onClick={() => setPreviewVideoEpisode(ep)}
                          >
                            Xem video
                          </Button>
                        ) : null}
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => handleViewEpisodeShots(ep.id)}
                        >
                          Xem phân cảnh
                        </Button>
                        <Button
                          size="sm"
                          variant="secondary"
                          icon={<Play className="h-3 w-3" />}
                          loading={renderingEpisodeId === ep.id || BUSY_EPISODE_STATUSES.includes(ep.status)}
                          onClick={() => handleRenderEpisode(ep.id)}
                        >
                          {ep.outputVideoPath || ep.status === 'Completed' ? `Dựng lại E${ep.episodeNumber}` : `Dựng tập E${ep.episodeNumber}`}
                        </Button>
                      </div>
                    </div>
                  </Card>
                ))}
              </div>
            </div>
          )}

          {/* ── DETAIL: STORY BIBLE ── */}
          {detailTab === 'story' && selectedProject.storyBible && (
            <div className="space-y-4">
              <Card className="p-5 border-line space-y-4">
                <h3 className="font-bold text-fg flex items-center gap-2">
                  <BookOpen className="h-4 w-4 text-indigo-400" />
                  Story Bible Tổng Thể
                </h3>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
                  <div className="space-y-1">
                    <span className="text-fg-subtle font-semibold">Thể loại:</span>
                    <p className="rounded bg-muted p-2 text-fg">{selectedProject.storyBible.genre}</p>
                  </div>
                  <div className="space-y-1">
                    <span className="text-fg-subtle font-semibold">Thời đại:</span>
                    <p className="rounded bg-muted p-2 text-fg">{selectedProject.storyBible.timePeriod}</p>
                  </div>
                  <div className="space-y-1">
                    <span className="text-fg-subtle font-semibold">Bối cảnh địa lý:</span>
                    <p className="rounded bg-muted p-2 text-fg">{selectedProject.storyBible.geographicSetting}</p>
                  </div>
                  <div className="space-y-1">
                    <span className="text-fg-subtle font-semibold">Quy luật thế giới:</span>
                    <p className="rounded bg-muted p-2 text-fg">{selectedProject.storyBible.worldRules}</p>
                  </div>
                </div>

                <div className="space-y-1 text-xs">
                  <span className="text-fg-subtle font-semibold">Xung đột chính:</span>
                  <p className="rounded bg-muted p-2.5 text-fg leading-relaxed">
                    {selectedProject.storyBible.mainConflict}
                  </p>
                </div>

                <div className="space-y-1 text-xs">
                  <span className="text-fg-subtle font-semibold">Bí mật & Nút thắt (Plot Twists):</span>
                  <p className="rounded bg-muted p-2.5 text-fg leading-relaxed">
                    {selectedProject.storyBible.keyMysteriesAndTwists}
                  </p>
                </div>

                <div className="space-y-1 text-xs">
                  <span className="text-fg-subtle font-semibold">Đỉnh điểm & Kết thúc:</span>
                  <p className="rounded bg-muted p-2.5 text-fg leading-relaxed">
                    {selectedProject.storyBible.climaxResolution}
                  </p>
                </div>
              </Card>
            </div>
          )}

          {/* ── DETAIL: CHARACTERS ── */}
          {detailTab === 'characters' && (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {selectedProject.characters.map((c) => (
                <Card key={c.id} className="p-4 border-line space-y-3">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2">
                      <div className="h-8 w-8 rounded-full bg-indigo-500/20 flex items-center justify-center font-bold text-xs text-indigo-400">
                        {c.name.substring(0, 1)}
                      </div>
                      <div>
                        <h4 className="font-semibold text-fg text-sm">{c.name}</h4>
                        <span className="text-[11px] text-fg-subtle">{c.role} · {c.age} tuổi</span>
                      </div>
                    </div>
                    <span className="rounded bg-muted px-2 py-0.5 text-[11px] text-fg-muted">{c.characterId}</span>
                  </div>

                  <div className="text-xs space-y-1 text-fg-muted">
                    <div><strong>Ngoại hình:</strong> {c.physicalAppearance}</div>
                    <div><strong>Trang phục:</strong> {c.defaultCostume}</div>
                    <div><strong>Tính cách:</strong> {c.personality}</div>
                    <div><strong>Giọng nói:</strong> <span className="font-mono text-indigo-400">{c.voiceModel}</span></div>
                  </div>

                  <div className="rounded bg-muted/60 p-2 text-[10px] font-mono text-fg-subtle">
                    <span className="text-fg-muted font-bold block mb-0.5">Prompt đồng nhất khuôn mặt:</span>
                    {c.consistencyPrompt}
                  </div>
                </Card>
              ))}
            </div>
          )}

          {/* ── DETAIL: LOCATIONS ── */}
          {detailTab === 'locations' && (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {selectedProject.locations.map((loc) => (
                <Card key={loc.id} className="p-4 border-line space-y-2">
                  <div className="flex justify-between items-center">
                    <h4 className="font-semibold text-fg text-sm">{loc.name}</h4>
                    <span className="rounded bg-muted px-2 py-0.5 text-[11px] text-fg-muted">{loc.locationId}</span>
                  </div>
                  <div className="text-xs space-y-1 text-fg-muted">
                    <div><strong>Kiến trúc:</strong> {loc.architectureStyle}</div>
                    <div><strong>Ánh sáng & Không khí:</strong> {loc.lightingMood}</div>
                    <div><strong>Thời điểm:</strong> {loc.timeOfDay}</div>
                    <div><strong>Đạo cụ chính:</strong> {loc.keyProps}</div>
                  </div>
                </Card>
              ))}
            </div>
          )}

          {/* ── DETAIL: COST & BUDGET MONITOR ── */}
          {detailTab === 'cost' && costSummary && (
            <div className="space-y-4">
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                <Card className="p-4 border-line">
                  <div className="text-xs text-fg-subtle">Ngân sách cho phép</div>
                  <div className="text-2xl font-bold text-fg mt-1">
                    ${costSummary.totalBudgetLimitUsd.toFixed(2)} USD
                  </div>
                  <div className="text-xs text-emerald-400 mt-1">Giới hạn cứng bảo vệ chi phí</div>
                </Card>

                <Card className="p-4 border-line">
                  <div className="text-xs text-fg-subtle">Đã sử dụng thực tế</div>
                  <div className="text-2xl font-bold text-indigo-400 mt-1">
                    ${costSummary.totalSpentUsd.toFixed(4)} USD
                  </div>
                  <div className="text-xs text-fg-muted mt-1">
                    Trạng thái: <strong className="text-emerald-400">{costSummary.budgetStatus}</strong>
                  </div>
                </Card>

                <Card className="p-4 border-line">
                  <div className="text-xs text-fg-subtle">Còn lại</div>
                  <div className="text-2xl font-bold text-emerald-400 mt-1">
                    ${costSummary.remainingBudgetUsd.toFixed(4)} USD
                  </div>
                  <div className="text-xs text-fg-muted mt-1">Đủ hoàn tất chuỗi 10 tập</div>
                </Card>
              </div>

              <Card className="p-5 border-line space-y-3">
                <h4 className="font-bold text-fg text-sm">Chi tiết khoản chi theo công nghệ</h4>
                <div className="space-y-2 text-xs">
                  <div className="flex justify-between py-1.5 border-b border-line">
                    <span className="text-fg-muted">AI Showrunner / Kịch bản (Gemini API):</span>
                    <span className="font-semibold text-fg">${costSummary.scriptCostUsd.toFixed(4)}</span>
                  </div>
                  <div className="flex justify-between py-1.5 border-b border-line">
                    <span className="text-fg-muted">Tạo khung hình & Video visual (Hybrid Ken Burns):</span>
                    <span className="font-semibold text-fg">${costSummary.visualCostUsd.toFixed(4)}</span>
                  </div>
                  <div className="flex justify-between py-1.5 border-b border-line">
                    <span className="text-fg-muted">Giọng nói nhân vật (EdgeTTS Natural Vietnamese):</span>
                    <span className="font-semibold text-emerald-400">$0.0000 (Miễn phí 100%)</span>
                  </div>
                  <div className="flex justify-between py-1.5 border-b border-line">
                    <span className="text-fg-muted">Dựng & Ghép phim 50 phút (FFmpeg CPU Engine):</span>
                    <span className="font-semibold text-emerald-400">$0.0000 (Miễn phí 100%)</span>
                  </div>
                </div>
              </Card>
            </div>
          )}
        </div>
      )}

      {/* ── MODAL: CREATE NEW SERIES ── */}
      <Modal
        open={showCreateModal}
        onClose={() => setShowCreateModal(false)}
        title="Khai Hoả Series Phim 10 Tập (AI Showrunner)"
      >
        <form onSubmit={handleCreateProject} className="space-y-4">
          <div>
            <label className="block text-xs font-medium text-fg mb-1">
              Chọn 1 trong 5 Film Page <span className="text-rose-500">*</span>
            </label>
            <select
              value={createForm.filmPageId}
              onChange={(e) => {
                const pid = e.target.value;
                const p = pages.find((x) => x.id === pid);
                setCreateForm((prev) => ({
                  ...prev,
                  filmPageId: pid,
                  genre: p ? p.primaryGenre : prev.genre,
                  visualArtStyle: p ? p.visualArtStyle : prev.visualArtStyle,
                }));
              }}
              className="w-full rounded-md border border-line bg-surface px-3 py-2 text-sm text-fg focus:border-indigo-500 focus:outline-none"
            >
              {pages.map((p) => (
                <option key={p.id} value={p.id}>
                  Kênh {p.pageNumber}: {p.name} ({p.primaryGenre})
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-xs font-medium text-fg mb-1">
              Tên Series Phim <span className="text-rose-500">*</span>
            </label>
            <input
              type="text"
              placeholder="VD: Người Nhìn Thấu Tương Lai"
              value={createForm.title}
              onChange={(e) => setCreateForm((prev) => ({ ...prev, title: e.target.value }))}
              className="w-full rounded-md border border-line bg-surface px-3 py-2 text-sm text-fg focus:border-indigo-500 focus:outline-none"
              required
            />
          </div>

          <div>
            <label className="block text-xs font-medium text-fg mb-1">
              Ý tưởng phim hoặc chủ đề cốt truyện <span className="text-rose-500">*</span>
            </label>
            <textarea
              rows={4}
              placeholder="VD: Một chàng trai nghèo phát hiện mình có khả năng nhìn thấy tương lai, nhưng mỗi lần thay đổi tương lai thì một người thân của anh biến mất..."
              value={createForm.logline}
              onChange={(e) => setCreateForm((prev) => ({ ...prev, logline: e.target.value }))}
              className="w-full rounded-md border border-line bg-surface px-3 py-2 text-sm text-fg focus:border-indigo-500 focus:outline-none"
              required
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-fg mb-1">Thể loại</label>
              <input
                type="text"
                value={createForm.genre}
                onChange={(e) => setCreateForm((prev) => ({ ...prev, genre: e.target.value }))}
                className="w-full rounded-md border border-line bg-surface px-3 py-2 text-sm text-fg"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-fg mb-1">Phong cách hình ảnh</label>
              <input
                type="text"
                value={createForm.visualArtStyle}
                onChange={(e) => setCreateForm((prev) => ({ ...prev, visualArtStyle: e.target.value }))}
                className="w-full rounded-md border border-line bg-surface px-3 py-2 text-sm text-fg"
              />
            </div>
          </div>

          <div className="rounded-lg bg-indigo-500/10 border border-indigo-500/20 p-3 text-xs text-indigo-300 space-y-1">
            <div className="flex items-center gap-1.5 font-semibold text-indigo-200">
              <ShieldCheck className="h-4 w-4 text-emerald-400" />
              Chính sách tự động hóa trọn gói:
            </div>
            <p>
              • Quy mô mục tiêu: <strong>10 tập × 5 phút = 50 phút hoàn chỉnh</strong>.
            </p>
            <p>
              • Ngân sách giới hạn: <strong>Tối đa $5.00 USD</strong> (Tự động chuyển sang chế độ tiết kiệm nếu chạm ngưỡng).
            </p>
          </div>

          <div className="flex items-center gap-2 pt-2">
            <input
              type="checkbox"
              id="autoProduce"
              checked={createForm.autoProduceAll}
              onChange={(e) => setCreateForm((prev) => ({ ...prev, autoProduceAll: e.target.checked }))}
              className="rounded border-line text-indigo-600 focus:ring-indigo-500"
            />
            <label htmlFor="autoProduce" className="text-xs text-fg select-none">
              Tự động kích hoạt toàn bộ quy trình AI Showrunner ngay sau khi tạo
            </label>
          </div>

          <div className="flex justify-end gap-2 pt-3 border-t border-line">
            <Button variant="ghost" onClick={() => setShowCreateModal(false)}>
              Hủy
            </Button>
            <Button
              type="submit"
              variant="accent"
              className="bg-indigo-600 text-white"
              loading={producing}
            >
              Khai Hoả AI Showrunner
            </Button>
          </div>
        </form>
      </Modal>

      {/* ── MODAL: EPISODE SHOTS VIEWER ── */}
      {selectedEpisode && (
        <Modal
          open={!!selectedEpisode}
          onClose={() => setSelectedEpisode(null)}
          title={`Phân Cảnh Chi Tiết: Tập ${selectedEpisode.episodeNumber} - ${selectedEpisode.title}`}
        >
          <div className="max-h-[70vh] overflow-y-auto space-y-4 pr-1">
            <div className="text-xs text-fg-muted bg-muted p-3 rounded">
              <strong>Tóm tắt:</strong> {selectedEpisode.synopsis}
            </div>

            {selectedEpisode.scenes.map((scene) => (
              <div key={scene.id} className="border border-line rounded-lg p-3 space-y-3 bg-surface">
                <div className="flex justify-between items-center text-xs border-b border-line pb-2">
                  <span className="font-bold text-fg">
                    Cảnh {scene.sceneNumber}: {scene.locationName}
                  </span>
                  <span className="text-fg-subtle">{scene.timeOfDay} · ~{Math.round(scene.estimatedDurationSeconds)}s</span>
                </div>

                <div className="space-y-2">
                  {scene.shots.map((shot) => (
                    <div key={shot.id} className="rounded bg-muted/50 p-2 text-xs space-y-1">
                      <div className="flex justify-between text-fg-subtle text-[11px]">
                        <span>Shot {shot.shotNumber} ({shot.shotType} · {shot.cameraMovement})</span>
                        <span className="font-mono text-emerald-400">~{shot.durationSeconds.toFixed(1)}s</span>
                      </div>
                      {shot.dialogueText && (
                        <div className="text-fg font-medium bg-muted/80 p-1.5 rounded">
                          <strong className="text-indigo-400">{shot.speakerCharacterId}:</strong> "{shot.dialogueText}"
                        </div>
                      )}
                      <div className="text-fg-subtle text-[11px] truncate">
                        <strong>Visual:</strong> {shot.visualPrompt}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </Modal>
      )}

      {/* ── MODAL: EPISODE VIDEO PREVIEW PLAYER ── */}
      {previewVideoEpisode && (
        <Modal
          open={!!previewVideoEpisode}
          onClose={() => setPreviewVideoEpisode(null)}
          title={`Xem Thử Video: Tập ${previewVideoEpisode.episodeNumber} - ${previewVideoEpisode.title}`}
        >
          <div className="space-y-4">
            <div className="relative rounded-xl overflow-hidden bg-black shadow-2xl flex items-center justify-center border border-indigo-500/30">
              <video
                key={previewVideoEpisode.id}
                src={api.getFilmEpisodeVideoUrl(previewVideoEpisode.id)}
                controls
                autoPlay
                playsInline
                className="w-full max-h-[60vh] object-contain rounded-lg"
              />
            </div>

            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 rounded-lg bg-muted/60 p-3 text-xs">
              <div className="space-y-1">
                <div className="font-semibold text-fg text-sm">
                  Tập {previewVideoEpisode.episodeNumber}: {previewVideoEpisode.title}
                </div>
                <p className="text-fg-subtle line-clamp-2 max-w-xl">{previewVideoEpisode.synopsis}</p>
                <div className="text-[11px] text-emerald-400 font-medium pt-0.5 flex items-center gap-1.5">
                  <CheckCircle2 className="h-3.5 w-3.5" />
                  Kiểm tra chất lượng: {Math.round(previewVideoEpisode.qualityScore)}/100 · {Math.round(previewVideoEpisode.actualDurationSeconds)}s · 30FPS H.264 / AAC
                </div>
              </div>

              <div className="flex items-center gap-2 shrink-0">
                <a
                  href={api.getFilmEpisodeDownloadUrl(previewVideoEpisode.id)}
                  download
                  className="inline-flex items-center gap-1.5 rounded-md border border-line bg-surface px-3 py-2 text-xs font-semibold text-fg hover:bg-muted shadow-sm transition-colors"
                >
                  <Download className="h-3.5 w-3.5 text-indigo-400" />
                  Tải MP4 về máy
                </a>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => setPreviewVideoEpisode(null)}
                >
                  Đóng
                </Button>
              </div>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}
export default FilmFactoryPage;
