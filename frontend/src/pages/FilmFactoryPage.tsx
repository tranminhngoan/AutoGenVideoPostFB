import React, { useEffect, useState, useMemo } from 'react';
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
  ChevronRight,
  BookOpen,
  Users,
  MapPin,
  AlertCircle,
  AlertTriangle,
  Download,
  ArrowLeft,
  Loader2,
  RefreshCw,
  Info
} from 'lucide-react';
import {
  Button,
  Card,
  CardHeader,
  CardBody,
  Badge,
  Modal,
  PageHeader,
  IconButton,
  ProgressBar,
  Input,
  Textarea,
  Select,
  Field,
  Checkbox,
  EmptyState,
  Skeleton,
  Tabs,
  cn
} from '../components/ui';
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

const BUSY_EPISODE_STATUSES = [
  'Queued',
  'Scripting',
  'VisualAssetsGenerating',
  'VoiceGenerating',
  'GpuQueued',
  'GpuRendering',
  'GpuDone',
  'FilmEditing',
  'QualityCheck'
];

const BUSY_PROJECT_STATUSES = ['ShowrunnerPlanning', 'BibleReady'];

const EPISODE_STATUS_LABELS: Record<string, string> = {
  Pending: 'Chưa dựng',
  ScriptReady: 'Kịch bản sẵn sàng',
  Queued: 'Đang chờ dựng…',
  Scripting: 'Đang viết kịch bản…',
  VisualAssetsGenerating: 'Đang tạo nhân vật & bối cảnh…',
  VoiceGenerating: 'Đang lồng tiếng tiếng Việt…',
  GpuQueued: 'Chờ xếp hàng GPU…',
  GpuRendering: 'GPU Wan 2.2 đang dựng video…',
  GpuDone: 'GPU hoàn tất, đang ghép…',
  FilmEditing: 'Đang biên tập & render video…',
  QualityCheck: 'Đang kiểm tra chất lượng…',
  Completed: 'Hoàn tất',
  Failed: 'Lỗi dựng',
};

function getStatusPercent(status: string): number {
  switch (status) {
    case 'Queued': return 10;
    case 'Scripting': return 25;
    case 'VisualAssetsGenerating': return 40;
    case 'VoiceGenerating': return 55;
    case 'GpuQueued': return 65;
    case 'GpuRendering': return 80;
    case 'GpuDone': return 90;
    case 'FilmEditing': return 95;
    case 'QualityCheck': return 98;
    case 'Completed': return 100;
    default: return 0;
  }
}

export function FilmFactoryPage() {
  const toast = useToast();

  // Navigation: main overview tab ('projects' | 'pages')
  const [activeMainTab, setActiveMainTab] = useState<'projects' | 'pages'>('projects');

  // Currently opened project id (null means we're on the list view)
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);

  // Workbench sub-tab
  const [workbenchTab, setWorkbenchTab] = useState<'episodes' | 'story' | 'characters' | 'locations' | 'cost'>('episodes');

  // Data states
  const [pages, setPages] = useState<FilmPageDto[]>([]);
  const [projects, setProjects] = useState<FilmProjectDto[]>([]);
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

  // Deletion state & modal
  const [deletingProject, setDeletingProject] = useState<{ id: string; title: string } | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);

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
    } else {
      setSelectedProject(null);
      setCostSummary(null);
    }
  }, [selectedProjectId]);

  // Polling while server is processing
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
        // Also refresh projects list in background to sync count
        const projs = await api.getFilmProjects();
        setProjects(projs);
      } catch {
        // Keep last state on failure
      }
    }, 5000);
    return () => window.clearInterval(timer);
  }, [selectedProjectId, projectBusy]);

  // Refresh All
  const handleRefreshAll = async () => {
    await Promise.all([
      loadPages(),
      loadProjects(),
      selectedProjectId ? loadProjectDetail(selectedProjectId) : Promise.resolve(),
    ]);
    toast.success('Đã làm mới dữ liệu');
  };

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
      setWorkbenchTab('episodes');
    } catch (err: any) {
      toast.error(err.message || 'Tạo dự án phim thất bại');
    } finally {
      setProducing(false);
    }
  };

  // Trigger Produce All
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

  // Render Single Episode
  const handleRenderEpisode = async (episodeId: string) => {
    try {
      setRenderingEpisodeId(episodeId);
      const res = await api.renderFilmEpisode(episodeId);
      toast.success(`${res.message} Quá trình diễn ra tự động trong nền.`);
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

  // Confirm Delete Project
  const handleConfirmDelete = async () => {
    if (!deletingProject) return;
    try {
      setIsDeleting(true);
      await api.deleteFilmProject(deletingProject.id);
      toast.success(`Đã xóa vĩnh viễn series "${deletingProject.title}"`);
      // If currently inside deleted project, close it
      if (selectedProjectId === deletingProject.id) {
        setSelectedProjectId(null);
        setSelectedProject(null);
      }
      setDeletingProject(null);
      await loadProjects();
    } catch (err: any) {
      toast.error(err.message || 'Xóa dự án phim thất bại');
    } finally {
      setIsDeleting(false);
    }
  };

  return (
    <div className="space-y-6 pb-12">
      {/* ── Page Header Standard ── */}
      <PageHeader
        title={
          <div className="flex items-center gap-2.5">
            <span>AI Film Factory</span>
            <Badge tone="info" className="text-[11px] font-normal">v2.0</Badge>
          </div>
        }
        description="Hệ thống tự động sản xuất phim nhiều tập với AI Showrunner, giữ nhất quán nhân vật và Wan 2.2 / RunPod GPU."
        actions={
          <div className="flex items-center gap-2">
            <IconButton
              label="Làm mới dữ liệu"
              onClick={handleRefreshAll}
              className="border border-line-strong bg-surface shadow-card"
            >
              <RefreshCw className={cn('h-4 w-4', (loadingProjects || loadingPages || loadingDetail) && 'animate-spin')} />
            </IconButton>
            <Button
              variant="primary"
              icon={<Plus className="h-4 w-4" />}
              onClick={() => setShowCreateModal(true)}
            >
              Khai hoả Series Mới
            </Button>
          </div>
        }
      />

      {/* ── Top Metric Stats (Always visible when in list view) ── */}
      {!selectedProjectId && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Card className="p-4">
            <div className="flex items-center gap-2 text-xs text-fg-subtle">
              <Tv className="h-4 w-4 text-fg-muted" />
              <span>Kênh Phim Hoạt Động</span>
            </div>
            <div className="mt-2 text-xl font-semibold text-fg">
              {pages.length} / 5 Kênh
            </div>
            <div className="mt-1 text-[11px] text-fg-muted">Drama, Sci-Fi, Mystery, Comedy, Action</div>
          </Card>

          <Card className="p-4">
            <div className="flex items-center gap-2 text-xs text-fg-subtle">
              <Film className="h-4 w-4 text-fg-muted" />
              <span>Tổng Series Phim</span>
            </div>
            <div className="mt-2 text-xl font-semibold text-fg">
              {projects.length} Series
            </div>
            <div className="mt-1 text-[11px] text-fg-muted">Showrunner quản lý cốt truyện</div>
          </Card>

          <Card className="p-4">
            <div className="flex items-center gap-2 text-xs text-fg-subtle">
              <Clapperboard className="h-4 w-4 text-fg-muted" />
              <span>Định Dạng Tiêu Chuẩn</span>
            </div>
            <div className="mt-2 text-xl font-semibold text-fg">
              10 Tập × ~2.5 Phút
            </div>
            <div className="mt-1 text-[11px] text-fg-muted">100% Motion Clips · Wan 2.2 GPU</div>
          </Card>

          <Card className="p-4">
            <div className="flex items-center gap-2 text-xs text-fg-subtle">
              <ShieldCheck className="h-4 w-4 text-emerald-500" />
              <span>Bảo Vệ Ngân Sách</span>
            </div>
            <div className="mt-2 text-xl font-semibold text-emerald-500">
              &lt; $5.00 / Series
            </div>
            <div className="mt-1 text-[11px] text-fg-muted">Free Gemini TTS keys + Spot GPU</div>
          </Card>
        </div>
      )}

      {/* ══════════════════════════════════════════════════════════════════════
          MODE A: PROJECT WORKBENCH / STUDIO (When a series is opened)
          ══════════════════════════════════════════════════════════════════════ */}
      {selectedProjectId && selectedProject ? (
        <div className="space-y-6">
          {/* Back Button & Project Header Card */}
          <div>
            <div className="mb-3">
              <Button
                variant="ghost"
                size="sm"
                icon={<ArrowLeft className="h-4 w-4" />}
                onClick={() => setSelectedProjectId(null)}
                className="text-fg-muted hover:text-fg"
              >
                Quay lại danh sách Series
              </Button>
            </div>

            <Card className="overflow-hidden">
              <div className="border-b border-line p-5">
                <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
                  <div className="space-y-2 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="rounded bg-muted px-2 py-0.5 text-xs font-semibold text-fg">
                        {selectedProject.filmPageName}
                      </span>
                      <span className="rounded bg-muted px-2 py-0.5 text-xs text-fg-subtle">
                        {selectedProject.genre}
                      </span>
                      <span className="rounded bg-muted px-2 py-0.5 text-xs text-fg-subtle">
                        {selectedProject.visualArtStyle}
                      </span>
                      <Badge
                        tone={
                          selectedProject.status === 'Completed'
                            ? 'success'
                            : selectedProject.status === 'Failed'
                              ? 'danger'
                              : projectBusy
                                ? 'warning'
                                : 'neutral'
                        }
                        dot
                        pulse={projectBusy}
                      >
                        {projectBusy ? 'Đang xử lý trong nền' : selectedProject.status}
                      </Badge>
                    </div>

                    <h2 className="text-xl font-bold text-fg sm:text-2xl">{selectedProject.title}</h2>
                    <p className="text-sm text-fg-muted max-w-3xl leading-relaxed">{selectedProject.logline}</p>

                    <div className="flex flex-wrap items-center gap-6 pt-2 text-xs text-fg-subtle">
                      <div>
                        Tiến độ:{' '}
                        <strong className="text-fg font-semibold">
                          {selectedProject.completedEpisodesCount} / {selectedProject.targetEpisodes} tập hoàn thành
                        </strong>
                      </div>
                      <div>
                        Chi phí thực tế:{' '}
                        <strong className="text-emerald-500 font-semibold">
                          ${selectedProject.totalActualCostUsd.toFixed(4)}
                        </strong>{' '}
                        / ${selectedProject.totalBudgetLimitUsd.toFixed(2)} USD
                      </div>
                    </div>

                    {/* Progress bar */}
                    <div className="pt-2 max-w-md">
                      <ProgressBar
                        value={(selectedProject.completedEpisodesCount / (selectedProject.targetEpisodes || 10)) * 100}
                        tone={selectedProject.completedEpisodesCount === selectedProject.targetEpisodes ? 'success' : 'info'}
                      />
                    </div>
                  </div>

                  {/* Actions for this series */}
                  <div className="flex flex-wrap items-center gap-2 self-start pt-1">
                    <Button
                      variant="primary"
                      icon={producing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />}
                      loading={producing}
                      disabled={producing}
                      onClick={() => handleTriggerProduce(selectedProject.id)}
                    >
                      Dựng toàn bộ 10 tập
                    </Button>
                    <Button
                      variant="secondary"
                      icon={<RotateCw className={cn('h-4 w-4', loadingDetail && 'animate-spin')} />}
                      onClick={() => loadProjectDetail(selectedProject.id)}
                    >
                      Tải lại
                    </Button>
                    <Button
                      variant="danger-soft"
                      icon={<Trash2 className="h-4 w-4" />}
                      onClick={() => setDeletingProject({ id: selectedProject.id, title: selectedProject.title })}
                      title="Xóa series này"
                    >
                      Xóa Series
                    </Button>
                  </div>
                </div>
              </div>

              {/* Busy Callout Banner */}
              {projectBusy && (
                <div className="flex items-center gap-3 bg-muted/60 px-5 py-3 text-xs text-fg border-b border-line">
                  <Loader2 className="h-4 w-4 animate-spin text-fg shrink-0" />
                  <div className="flex-1">
                    <span className="font-semibold">Hệ thống đang sản xuất tự động trong nền:</span>{' '}
                    GPU Wan 2.2 trên RunPod và AI Showrunner đang chạy độc lập. Trang tự động cập nhật mỗi 5 giây. Bạn không cần treo máy và có thể tắt trình duyệt an tâm.
                  </div>
                </div>
              )}
            </Card>
          </div>

          {/* Workbench Tabs */}
          <Tabs
            items={[
              {
                id: 'episodes',
                label: '10 Tập Phim',
                count: `${selectedProject.completedEpisodesCount}/${selectedProject.episodes.length}`,
              },
              {
                id: 'story',
                label: 'Story Bible',
              },
              {
                id: 'characters',
                label: 'Nhân Vật',
                count: selectedProject.characters.length,
              },
              {
                id: 'locations',
                label: 'Bối Cảnh',
                count: selectedProject.locations.length,
              },
              {
                id: 'cost',
                label: 'Ngân Sách AI',
                count: `$${selectedProject.totalActualCostUsd.toFixed(3)}`,
              },
            ]}
            value={workbenchTab}
            onChange={(v) => setWorkbenchTab(v as any)}
          />

          {/* ── SUB-TAB: 10 EPISODES ── */}
          {workbenchTab === 'episodes' && (
            <div className="space-y-4">
              <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
                {selectedProject.episodes.map((ep) => {
                  const isEpBusy = BUSY_EPISODE_STATUSES.includes(ep.status) || renderingEpisodeId === ep.id;
                  const isCompleted = ep.status === 'Completed' || !!ep.outputVideoPath;
                  const isFailed = ep.status === 'Failed';
                  const isPending = ep.status === 'Pending';
                  const isScriptReady = ep.status === 'ScriptReady';

                  return (
                    <Card
                      key={ep.id}
                      className={cn(
                        'flex flex-col justify-between transition-all border-line',
                        isEpBusy && 'ring-1 ring-fg-muted/40 shadow-md',
                        isCompleted && 'border-emerald-500/30'
                      )}
                    >
                      <div className="p-5 space-y-3.5">
                        {/* Top: Episode Number + Title + Status Badge */}
                        <div className="flex items-start justify-between gap-3">
                          <div className="flex items-center gap-3">
                            <span className="flex h-8 w-11 items-center justify-center rounded-md bg-muted font-bold text-xs text-fg tracking-wide shrink-0 border border-line">
                              E{ep.episodeNumber < 10 ? `0${ep.episodeNumber}` : ep.episodeNumber}
                            </span>
                            <div>
                              <h4 className="font-semibold text-fg text-sm sm:text-base leading-snug">
                                {ep.title}
                              </h4>
                              <div className="text-[11px] text-fg-subtle mt-0.5">
                                Thời lượng: ~{Math.round(ep.actualDurationSeconds || ep.targetDurationSeconds / 60 * 60)}s · {ep.scenesCount} cảnh · {ep.shotsCount} shots
                              </div>
                            </div>
                          </div>

                          <div className="shrink-0">
                            {isCompleted ? (
                              <Badge tone="success" dot>
                                <CheckCircle2 className="h-3 w-3 mr-1 inline" />
                                Hoàn tất
                              </Badge>
                            ) : isFailed ? (
                              <Badge tone="danger" dot>
                                <AlertTriangle className="h-3 w-3 mr-1 inline" />
                                Lỗi
                              </Badge>
                            ) : isEpBusy ? (
                              <Badge tone="info" dot pulse>
                                <Loader2 className="h-3 w-3 animate-spin mr-1 inline" />
                                {EPISODE_STATUS_LABELS[ep.status] || ep.status}
                              </Badge>
                            ) : isScriptReady ? (
                              <Badge tone="info">Kịch bản sẵn sàng</Badge>
                            ) : (
                              <Badge tone="neutral">Chưa dựng</Badge>
                            )}
                          </div>
                        </div>

                        {/* Synopsis */}
                        <p className="text-xs text-fg-muted leading-relaxed line-clamp-2">
                          {ep.synopsis}
                        </p>

                        {/* Busy Active Progress Feedback */}
                        {isEpBusy && (
                          <div className="rounded-md border border-line bg-muted/60 p-3 space-y-2">
                            <div className="flex items-center justify-between text-xs font-medium text-fg">
                              <span className="flex items-center gap-2">
                                <Loader2 className="h-3.5 w-3.5 animate-spin text-fg" />
                                {EPISODE_STATUS_LABELS[ep.status] || ep.status}
                              </span>
                              <span className="text-[11px] text-fg-subtle">
                                {getStatusPercent(ep.status)}%
                              </span>
                            </div>
                            <div className="h-1.5 w-full bg-line rounded-full overflow-hidden">
                              <div
                                className="h-full bg-fg animate-pulse rounded-full transition-all duration-500"
                                style={{ width: `${getStatusPercent(ep.status)}%` }}
                              />
                            </div>
                            <p className="text-[11px] text-fg-subtle">
                              GPU và AI đang xử lý ngầm, dữ liệu sẽ tự cập nhật khi hoàn thành.
                            </p>
                          </div>
                        )}

                        {/* Error Message Box */}
                        {isFailed && ep.errorMessage && (
                          <div className="rounded-md border border-danger/30 bg-danger/10 p-2.5 text-xs text-danger break-words">
                            <div className="font-semibold flex items-center gap-1.5">
                              <AlertCircle className="h-3.5 w-3.5 shrink-0" />
                              Lỗi trong quá trình dựng:
                            </div>
                            <div className="mt-1 text-[11px] opacity-90">{ep.errorMessage}</div>
                          </div>
                        )}

                        {/* Hook & Cliffhanger */}
                        <div className="rounded-md bg-muted/50 p-2.5 text-[11px] space-y-1.5 border border-line/60">
                          <div className="text-fg-subtle">
                            <span className="font-semibold text-fg">Mở đầu (Hook):</span> {ep.openingHook}
                          </div>
                          <div className="text-fg-subtle">
                            <span className="font-semibold text-fg">Kết nút thắt (Cliffhanger):</span> {ep.endingCliffhanger}
                          </div>
                        </div>

                        {/* Completed Video Preview Box */}
                        {isCompleted && (
                          <div className="rounded-lg border border-line bg-muted/30 p-3 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                            <div className="flex items-center gap-2.5">
                              <div
                                className="flex h-10 w-10 items-center justify-center rounded-md bg-fg text-canvas cursor-pointer hover:opacity-90 shadow-sm shrink-0"
                                onClick={() => setPreviewVideoEpisode(ep)}
                                title="Bấm để phát video"
                              >
                                <Play className="h-5 w-5 fill-current ml-0.5" />
                              </div>
                              <div>
                                <div className="text-xs font-semibold text-fg flex items-center gap-1.5">
                                  <CheckCircle2 className="h-3.5 w-3.5 text-emerald-500" />
                                  Video MP4 Hoàn Chỉnh
                                </div>
                                <div className="text-[11px] text-fg-subtle">
                                  {Math.round(ep.actualDurationSeconds || ep.targetDurationSeconds)}s · QA: {Math.round(ep.qualityScore)}/100 · 30 FPS
                                </div>
                              </div>
                            </div>

                            <div className="flex items-center gap-2 self-end sm:self-center">
                              <a
                                href={api.getFilmEpisodeDownloadUrl(ep.id)}
                                download
                                className="inline-flex items-center gap-1 rounded-md border border-line-strong bg-surface px-2.5 py-1 text-xs font-medium text-fg hover:bg-muted shadow-card"
                              >
                                <Download className="h-3 w-3" />
                                Tải MP4
                              </a>
                              <Button
                                size="sm"
                                variant="secondary"
                                onClick={() => setPreviewVideoEpisode(ep)}
                              >
                                Xem trước
                              </Button>
                            </div>
                          </div>
                        )}
                      </div>

                      {/* Card Footer Actions */}
                      <div className="flex items-center justify-between border-t border-line px-5 py-3 bg-muted/20">
                        <span className="text-[11px] text-fg-subtle">
                          Chi phí: <strong className="text-fg font-medium">${ep.actualCostUsd.toFixed(4)}</strong>
                        </span>

                        <div className="flex items-center gap-2">
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => handleViewEpisodeShots(ep.id)}
                          >
                            Xem phân cảnh
                          </Button>

                          {isCompleted ? (
                            <Button
                              size="sm"
                              variant="secondary"
                              icon={<RotateCw className="h-3 w-3" />}
                              onClick={() => handleRenderEpisode(ep.id)}
                              disabled={isEpBusy}
                            >
                              Dựng lại
                            </Button>
                          ) : isEpBusy ? (
                            <Button
                              size="sm"
                              variant="secondary"
                              loading
                              disabled
                            >
                              Đang dựng…
                            </Button>
                          ) : isFailed ? (
                            <Button
                              size="sm"
                              variant="secondary"
                              icon={<RotateCw className="h-3 w-3" />}
                              onClick={() => handleRenderEpisode(ep.id)}
                            >
                              Thử lại
                            </Button>
                          ) : (
                            <Button
                              size="sm"
                              variant="primary"
                              icon={<Play className="h-3 w-3" />}
                              onClick={() => handleRenderEpisode(ep.id)}
                            >
                              Dựng tập E{ep.episodeNumber}
                            </Button>
                          )}
                        </div>
                      </div>
                    </Card>
                  );
                })}
              </div>
            </div>
          )}

          {/* ── SUB-TAB: STORY BIBLE ── */}
          {workbenchTab === 'story' && selectedProject.storyBible && (
            <div className="space-y-4">
              <Card className="p-5 space-y-4">
                <div className="flex items-center gap-2 border-b border-line pb-3">
                  <BookOpen className="h-4 w-4 text-fg" />
                  <h3 className="font-semibold text-fg text-sm">Story Bible Tổng Thể</h3>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
                  <div className="space-y-1">
                    <span className="text-fg-subtle font-medium">Thể loại:</span>
                    <p className="rounded-md bg-muted p-2 text-fg">{selectedProject.storyBible.genre}</p>
                  </div>
                  <div className="space-y-1">
                    <span className="text-fg-subtle font-medium">Thời đại:</span>
                    <p className="rounded-md bg-muted p-2 text-fg">{selectedProject.storyBible.timePeriod}</p>
                  </div>
                  <div className="space-y-1">
                    <span className="text-fg-subtle font-medium">Bối cảnh địa lý:</span>
                    <p className="rounded-md bg-muted p-2 text-fg">{selectedProject.storyBible.geographicSetting}</p>
                  </div>
                  <div className="space-y-1">
                    <span className="text-fg-subtle font-medium">Quy luật thế giới:</span>
                    <p className="rounded-md bg-muted p-2 text-fg">{selectedProject.storyBible.worldRules}</p>
                  </div>
                </div>

                <div className="space-y-1 text-xs">
                  <span className="text-fg-subtle font-medium">Xung đột chính:</span>
                  <p className="rounded-md bg-muted p-2.5 text-fg leading-relaxed">
                    {selectedProject.storyBible.mainConflict}
                  </p>
                </div>

                <div className="space-y-1 text-xs">
                  <span className="text-fg-subtle font-medium">Bí mật & Nút thắt cốt truyện (Plot Twists):</span>
                  <p className="rounded-md bg-muted p-2.5 text-fg leading-relaxed">
                    {selectedProject.storyBible.keyMysteriesAndTwists}
                  </p>
                </div>

                <div className="space-y-1 text-xs">
                  <span className="text-fg-subtle font-medium">Cao trào & Kết thúc:</span>
                  <p className="rounded-md bg-muted p-2.5 text-fg leading-relaxed">
                    {selectedProject.storyBible.climaxResolution}
                  </p>
                </div>
              </Card>
            </div>
          )}

          {/* ── SUB-TAB: CHARACTERS ── */}
          {workbenchTab === 'characters' && (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {selectedProject.characters.map((c) => (
                <Card key={c.id} className="p-4 space-y-3">
                  <div className="flex items-center justify-between border-b border-line pb-2.5">
                    <div className="flex items-center gap-2.5">
                      {c.frontReferenceImageUrl ? (
                        <img
                          src={c.frontReferenceImageUrl}
                          alt={c.name}
                          className="h-10 w-10 rounded-full object-cover border border-line"
                        />
                      ) : (
                        <div className="h-10 w-10 rounded-full bg-muted flex items-center justify-center font-bold text-sm text-fg border border-line">
                          {c.name.substring(0, 1)}
                        </div>
                      )}
                      <div>
                        <h4 className="font-semibold text-fg text-sm">{c.name}</h4>
                        <span className="text-[11px] text-fg-subtle">{c.role} · {c.age} tuổi</span>
                      </div>
                    </div>
                    <span className="rounded bg-muted px-2 py-0.5 text-[11px] text-fg-muted font-mono">{c.characterId}</span>
                  </div>

                  <div className="text-xs space-y-1.5 text-fg-muted">
                    <div><strong className="text-fg">Ngoại hình:</strong> {c.physicalAppearance}</div>
                    <div><strong className="text-fg">Trang phục:</strong> {c.defaultCostume}</div>
                    <div><strong className="text-fg">Tính cách:</strong> {c.personality}</div>
                    <div><strong className="text-fg">Giọng đọc:</strong> <span className="font-mono text-fg">{c.voiceModel}</span></div>
                  </div>

                  <div className="rounded bg-muted/70 p-2 text-[10px] font-mono text-fg-subtle break-words">
                    <span className="text-fg font-semibold block mb-0.5">Visual Anchor Prompt:</span>
                    {c.consistencyPrompt}
                  </div>
                </Card>
              ))}
            </div>
          )}

          {/* ── SUB-TAB: LOCATIONS ── */}
          {workbenchTab === 'locations' && (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {selectedProject.locations.map((loc) => (
                <Card key={loc.id} className="p-4 space-y-2.5">
                  <div className="flex justify-between items-center border-b border-line pb-2">
                    <h4 className="font-semibold text-fg text-sm">{loc.name}</h4>
                    <span className="rounded bg-muted px-2 py-0.5 text-[11px] text-fg-muted font-mono">{loc.locationId}</span>
                  </div>
                  <div className="text-xs space-y-1 text-fg-muted">
                    <div><strong className="text-fg">Kiến trúc & Địa điểm:</strong> {loc.architectureStyle}</div>
                    <div><strong className="text-fg">Ánh sáng & Tâm trạng:</strong> {loc.lightingMood}</div>
                    <div><strong className="text-fg">Thời điểm:</strong> {loc.timeOfDay}</div>
                    <div><strong className="text-fg">Đạo cụ chính:</strong> {loc.keyProps}</div>
                  </div>
                </Card>
              ))}
            </div>
          )}

          {/* ── SUB-TAB: COST & BUDGET MONITOR ── */}
          {workbenchTab === 'cost' && costSummary && (
            <div className="space-y-4">
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                <Card className="p-4">
                  <div className="text-xs text-fg-subtle">Ngân sách giới hạn</div>
                  <div className="text-2xl font-bold text-fg mt-1">
                    ${costSummary.totalBudgetLimitUsd.toFixed(2)} USD
                  </div>
                  <div className="text-xs text-emerald-500 mt-1">Giới hạn bảo vệ chi phí tự động</div>
                </Card>

                <Card className="p-4">
                  <div className="text-xs text-fg-subtle">Đã sử dụng thực tế</div>
                  <div className="text-2xl font-bold text-fg mt-1">
                    ${costSummary.totalSpentUsd.toFixed(4)} USD
                  </div>
                  <div className="text-xs text-fg-muted mt-1">
                    Trạng thái: <strong className="text-emerald-500">{costSummary.budgetStatus}</strong>
                  </div>
                </Card>

                <Card className="p-4">
                  <div className="text-xs text-fg-subtle">Ngân sách còn lại</div>
                  <div className="text-2xl font-bold text-emerald-500 mt-1">
                    ${costSummary.remainingBudgetUsd.toFixed(4)} USD
                  </div>
                  <div className="text-xs text-fg-muted mt-1">Đủ hoàn tất chuỗi 10 tập</div>
                </Card>
              </div>

              <Card className="p-5 space-y-3">
                <h4 className="font-semibold text-fg text-sm border-b border-line pb-2">
                  Chi tiết phân bổ chi phí công nghệ
                </h4>
                <div className="space-y-2 text-xs">
                  <div className="flex justify-between py-1.5 border-b border-line">
                    <span className="text-fg-muted">AI Showrunner / Kịch bản (Gemini API 4 Free Keys):</span>
                    <span className="font-semibold text-fg">${costSummary.scriptCostUsd.toFixed(4)}</span>
                  </div>
                  <div className="flex justify-between py-1.5 border-b border-line">
                    <span className="text-fg-muted">Tạo khung hình & Video GPU Wan 2.2 (RunPod RTX 3090):</span>
                    <span className="font-semibold text-fg">${costSummary.visualCostUsd.toFixed(4)}</span>
                  </div>
                  <div className="flex justify-between py-1.5 border-b border-line">
                    <span className="text-fg-muted">Lồng tiếng nhân vật tiếng Việt (EdgeTTS):</span>
                    <span className="font-semibold text-emerald-500">$0.0000 (Miễn phí 100%)</span>
                  </div>
                  <div className="flex justify-between py-1.5 border-b border-line">
                    <span className="text-fg-muted">Ghép & Biên tập video (FFmpeg Engine):</span>
                    <span className="font-semibold text-emerald-500">$0.0000 (Miễn phí 100%)</span>
                  </div>
                </div>
              </Card>
            </div>
          )}
        </div>
      ) : (
        /* ══════════════════════════════════════════════════════════════════════
            MODE B: OVERVIEW (SERIES LIST & 5 FILM PAGES)
            ══════════════════════════════════════════════════════════════════════ */
        <div className="space-y-6">
          {/* Main Navigation Tabs */}
          <Tabs
            items={[
              {
                id: 'projects',
                label: 'Dự án Series Phim',
                count: projects.length,
              },
              {
                id: 'pages',
                label: '5 Kênh Phim Chuyên Biệt',
                count: pages.length,
              },
            ]}
            value={activeMainTab}
            onChange={(v) => setActiveMainTab(v as any)}
          />

          {/* ── MAIN TAB: PROJECTS LIST ── */}
          {activeMainTab === 'projects' && (
            <div className="space-y-4">
              {loadingProjects && projects.length === 0 ? (
                <div className="space-y-3">
                  {[1, 2].map((i) => (
                    <Card key={i} className="p-6">
                      <Skeleton className="h-6 w-1/3 mb-2" />
                      <Skeleton className="h-4 w-2/3 mb-4" />
                      <Skeleton className="h-8 w-1/4" />
                    </Card>
                  ))}
                </div>
              ) : projects.length === 0 ? (
                <EmptyState
                  icon={<Film />}
                  title="Chưa có series phim nào"
                  description="Hãy bấm 'Khai hoả Series Mới' để AI Showrunner tự động lên kịch bản 10 tập và chuẩn bị sản xuất."
                  action={
                    <Button variant="primary" icon={<Plus className="h-4 w-4" />} onClick={() => setShowCreateModal(true)}>
                      Tạo Series Đầu Tiên
                    </Button>
                  }
                />
              ) : (
                <div className="grid grid-cols-1 gap-4">
                  {projects.map((proj) => {
                    const isBusy = BUSY_PROJECT_STATUSES.includes(proj.status);
                    return (
                      <Card
                        key={proj.id}
                        className="overflow-hidden border-line hover:border-line-strong transition-all"
                      >
                        <div className="p-5 flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                          <div className="space-y-2 flex-1">
                            <div className="flex flex-wrap items-center gap-2">
                              <span className="rounded bg-muted px-2 py-0.5 text-xs font-semibold text-fg">
                                {proj.filmPageName}
                              </span>
                              <span className="rounded bg-muted px-2 py-0.5 text-xs text-fg-subtle">
                                {proj.genre}
                              </span>
                              <Badge
                                tone={
                                  proj.status === 'Completed'
                                    ? 'success'
                                    : proj.status === 'Failed'
                                      ? 'danger'
                                      : isBusy
                                        ? 'warning'
                                        : 'neutral'
                                }
                                dot
                                pulse={isBusy}
                              >
                                {proj.status}
                              </Badge>
                            </div>

                            <h3 className="text-lg font-bold text-fg">{proj.title}</h3>
                            <p className="text-sm text-fg-muted max-w-3xl line-clamp-2 leading-relaxed">
                              {proj.logline}
                            </p>

                            <div className="flex flex-wrap items-center gap-5 text-xs text-fg-subtle pt-1">
                              <span>
                                Tiến độ:{' '}
                                <strong className="text-fg font-medium">
                                  {proj.completedEpisodesCount} / {proj.targetEpisodes} tập
                                </strong>
                              </span>
                              <span>
                                Phong cách: <strong className="text-fg font-medium">{proj.visualArtStyle}</strong>
                              </span>
                              <span>
                                Chi phí:{' '}
                                <strong className="text-emerald-500 font-medium">
                                  ${proj.totalActualCostUsd.toFixed(4)}
                                </strong>{' '}
                                / ${proj.totalBudgetLimitUsd.toFixed(2)}
                              </span>
                            </div>

                            <div className="pt-1 max-w-xs">
                              <ProgressBar
                                value={(proj.completedEpisodesCount / (proj.targetEpisodes || 10)) * 100}
                                tone={proj.completedEpisodesCount === proj.targetEpisodes ? 'success' : 'info'}
                              />
                            </div>
                          </div>

                          {/* Action Buttons */}
                          <div className="flex items-center gap-2 self-end md:self-center">
                            <Button
                              size="sm"
                              variant="primary"
                              icon={<Eye className="h-4 w-4" />}
                              onClick={() => {
                                setSelectedProjectId(proj.id);
                                setWorkbenchTab('episodes');
                              }}
                            >
                              Vào Studio
                            </Button>
                            <Button
                              size="sm"
                              variant="secondary"
                              icon={<Play className="h-4 w-4" />}
                              loading={producing && selectedProjectId === proj.id}
                              onClick={() => handleTriggerProduce(proj.id)}
                            >
                              Dựng 10 tập
                            </Button>
                            <IconButton
                              label="Xóa Series"
                              className="text-danger hover:bg-danger/10 border border-transparent hover:border-danger/20"
                              onClick={() => setDeletingProject({ id: proj.id, title: proj.title })}
                            >
                              <Trash2 className="h-4 w-4" />
                            </IconButton>
                          </div>
                        </div>
                      </Card>
                    );
                  })}
                </div>
              )}
            </div>
          )}

          {/* ── MAIN TAB: 5 FILM PAGES ── */}
          {activeMainTab === 'pages' && (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
              {pages.map((p) => (
                <Card key={p.id} className="relative overflow-hidden border-line hover:border-line-strong transition-all">
                  <div
                    className="h-1.5 w-full"
                    style={{ backgroundColor: p.brandColorHex || '#4F46E5' }}
                  />
                  <div className="p-5 space-y-4">
                    <div className="flex items-start justify-between gap-3">
                      <div className="flex items-center gap-3">
                        <div
                          className="flex h-11 w-11 items-center justify-center rounded-lg font-bold text-white shadow-sm text-sm"
                          style={{ backgroundColor: p.brandColorHex || '#4F46E5' }}
                        >
                          P{p.pageNumber}
                        </div>
                        <div>
                          <h3 className="font-semibold text-fg text-sm">{p.name}</h3>
                          <div className="text-xs text-fg-subtle">{p.primaryGenre}</div>
                        </div>
                      </div>
                      <Badge tone="success" dot>Hoạt động</Badge>
                    </div>

                    <p className="text-xs text-fg-muted line-clamp-2 leading-relaxed">{p.description}</p>

                    <div className="space-y-1.5 rounded-md bg-muted/60 p-3 text-xs border border-line">
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

                    <div className="flex items-center justify-between pt-1">
                      <div className="text-xs text-fg-subtle">
                        {p.linkedFacebookPageName ? (
                          <span className="font-medium text-fg">FB: {p.linkedFacebookPageName}</span>
                        ) : (
                          'Chưa liên kết FB'
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
        </div>
      )}

      {/* ── MODAL: CONFIRM DELETE SERIES ── */}
      <Modal
        open={!!deletingProject}
        onClose={() => setDeletingProject(null)}
        title="Xác nhận xóa Series Phim"
      >
        <div className="space-y-4">
          <div className="rounded-md border border-danger/30 bg-danger/10 p-4 text-xs text-danger space-y-1.5">
            <div className="flex items-center gap-2 font-semibold text-sm">
              <AlertTriangle className="h-4 w-4 shrink-0" />
              Cảnh báo hành động không thể khôi phục
            </div>
            <p className="leading-relaxed">
              Bạn có chắc chắn muốn xóa vĩnh viễn series:
            </p>
            <p className="font-bold text-sm text-fg bg-surface/50 p-2 rounded border border-line">
              {deletingProject?.title}
            </p>
            <p className="text-[11px] opacity-90 leading-relaxed">
              Hành động này sẽ xóa toàn bộ 10 tập phim, kịch bản, các nhân vật, bối cảnh và tất cả video clip MP4 liên quan khỏi hệ thống.
            </p>
          </div>

          <div className="flex justify-end gap-2 pt-2 border-t border-line">
            <Button
              variant="secondary"
              onClick={() => setDeletingProject(null)}
              disabled={isDeleting}
            >
              Hủy bỏ
            </Button>
            <Button
              variant="danger"
              icon={<Trash2 className="h-4 w-4" />}
              loading={isDeleting}
              onClick={handleConfirmDelete}
            >
              Xác nhận xóa vĩnh viễn
            </Button>
          </div>
        </div>
      </Modal>

      {/* ── MODAL: CREATE NEW SERIES ── */}
      <Modal
        open={showCreateModal}
        onClose={() => setShowCreateModal(false)}
        title="Khai Hoả Series Phim Mới (AI Showrunner)"
      >
        <form onSubmit={handleCreateProject} className="space-y-4">
          <Field label={<span>Chọn 1 trong 5 Film Page <span className="text-danger">*</span></span>}>
            <Select
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
            >
              {pages.map((p) => (
                <option key={p.id} value={p.id}>
                  Kênh {p.pageNumber}: {p.name} ({p.primaryGenre})
                </option>
              ))}
            </Select>
          </Field>

          <Field label={<span>Tên Series Phim <span className="text-danger">*</span></span>}>
            <Input
              type="text"
              placeholder="VD: Người Nhìn Thấu Tương Lai"
              value={createForm.title}
              onChange={(e) => setCreateForm((prev) => ({ ...prev, title: e.target.value }))}
              required
            />
          </Field>

          <Field
            label={<span>Ý tưởng cốt truyện / Logline <span className="text-danger">*</span></span>}
            hint="Mô tả tóm tắt ý tưởng chính để AI Showrunner triển khai cốt truyện 10 tập."
          >
            <Textarea
              rows={4}
              placeholder="VD: Một tài xế công nghệ phát hiện mình có thể nhìn trước tai nạn 30 giây, nhưng mỗi lần anh cứu người thì một ký ức thời thơ ấu của anh lại biến mất..."
              value={createForm.logline}
              onChange={(e) => setCreateForm((prev) => ({ ...prev, logline: e.target.value }))}
              required
            />
          </Field>

          <div className="grid grid-cols-2 gap-3">
            <Field label="Thể loại">
              <Input
                type="text"
                value={createForm.genre}
                onChange={(e) => setCreateForm((prev) => ({ ...prev, genre: e.target.value }))}
              />
            </Field>
            <Field label="Phong cách hình ảnh">
              <Input
                type="text"
                value={createForm.visualArtStyle}
                onChange={(e) => setCreateForm((prev) => ({ ...prev, visualArtStyle: e.target.value }))}
              />
            </Field>
          </div>

          <div className="rounded-md border border-line bg-muted/50 p-3 text-xs space-y-1">
            <div className="flex items-center gap-1.5 font-semibold text-fg">
              <ShieldCheck className="h-4 w-4 text-emerald-500" />
              Chính sách tự động hóa chuẩn:
            </div>
            <p className="text-fg-subtle">
              • Cấu trúc: <strong>10 tập × ~2.5 phút = ~25 phút điện ảnh</strong>.
            </p>
            <p className="text-fg-subtle">
              • Ngân sách bảo vệ: <strong>Tối đa $5.00 USD</strong> (luân phiên 4 Gemini free keys + RunPod RTX 3090).
            </p>
          </div>

          <div className="pt-1">
            <Checkbox
              label="Tự động kích hoạt toàn bộ quy trình AI Showrunner ngay sau khi tạo"
              checked={!!createForm.autoProduceAll}
              onChange={(v) => setCreateForm((prev) => ({ ...prev, autoProduceAll: v }))}
            />
          </div>

          <div className="flex justify-end gap-2 pt-3 border-t border-line">
            <Button variant="secondary" onClick={() => setShowCreateModal(false)}>
              Hủy
            </Button>
            <Button
              type="submit"
              variant="primary"
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
          title={`Phân Cảnh: Tập ${selectedEpisode.episodeNumber} - ${selectedEpisode.title}`}
        >
          <div className="max-h-[70vh] overflow-y-auto space-y-4 pr-1">
            <div className="text-xs text-fg-muted bg-muted p-3 rounded-md">
              <strong className="text-fg">Tóm tắt:</strong> {selectedEpisode.synopsis}
            </div>

            {selectedEpisode.scenes.map((scene) => (
              <div key={scene.id} className="border border-line rounded-lg p-3 space-y-3 bg-surface">
                <div className="flex justify-between items-center text-xs border-b border-line pb-2">
                  <span className="font-semibold text-fg">
                    Cảnh {scene.sceneNumber}: {scene.locationName}
                  </span>
                  <span className="text-fg-subtle">{scene.timeOfDay} · ~{Math.round(scene.estimatedDurationSeconds)}s</span>
                </div>

                <div className="space-y-2">
                  {scene.shots.map((shot) => (
                    <div key={shot.id} className="rounded-md bg-muted/60 p-2.5 text-xs space-y-1 border border-line/60">
                      <div className="flex justify-between text-fg-subtle text-[11px]">
                        <span>Shot {shot.shotNumber} ({shot.shotType} · {shot.cameraMovement})</span>
                        <span className="font-mono text-emerald-500">~{shot.durationSeconds.toFixed(1)}s</span>
                      </div>
                      {shot.dialogueText && (
                        <div className="text-fg font-medium bg-surface/70 p-1.5 rounded border border-line/40">
                          <strong className="text-fg">{shot.speakerCharacterId}:</strong> "{shot.dialogueText}"
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
          title={`Xem Video: Tập ${previewVideoEpisode.episodeNumber} - ${previewVideoEpisode.title}`}
        >
          <div className="space-y-4">
            <div className="relative rounded-lg overflow-hidden bg-black shadow-lg flex items-center justify-center border border-line">
              <video
                key={previewVideoEpisode.id}
                src={api.getFilmEpisodeVideoUrl(previewVideoEpisode.id)}
                controls
                autoPlay
                playsInline
                className="w-full max-h-[60vh] object-contain rounded-md"
              />
            </div>

            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 rounded-md bg-muted/60 p-3 text-xs border border-line">
              <div className="space-y-1">
                <div className="font-semibold text-fg text-sm">
                  Tập {previewVideoEpisode.episodeNumber}: {previewVideoEpisode.title}
                </div>
                <p className="text-fg-subtle line-clamp-2 max-w-xl">{previewVideoEpisode.synopsis}</p>
                <div className="text-[11px] text-emerald-500 font-medium pt-0.5 flex items-center gap-1.5">
                  <CheckCircle2 className="h-3.5 w-3.5" />
                  Chất lượng QA: {Math.round(previewVideoEpisode.qualityScore)}/100 · {Math.round(previewVideoEpisode.actualDurationSeconds)}s · 30 FPS H.264
                </div>
              </div>

              <div className="flex items-center gap-2 shrink-0">
                <a
                  href={api.getFilmEpisodeDownloadUrl(previewVideoEpisode.id)}
                  download
                  className="inline-flex items-center gap-1.5 rounded-md border border-line-strong bg-surface px-3 py-2 text-xs font-semibold text-fg hover:bg-muted shadow-card transition-colors"
                >
                  <Download className="h-3.5 w-3.5" />
                  Tải MP4
                </a>
                <Button
                  variant="secondary"
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
