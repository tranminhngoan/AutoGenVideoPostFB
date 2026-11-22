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
  Info,
  Share2,
  ExternalLink,
  Cpu,
  HardDrive,
  Zap,
  Copy,
  Check,
  Upload,
  Power,
  PowerOff,
  Activity,
  Mic,
  Image as ImageIcon
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
  FilmCostSummaryDto,
  ConnectFacebookPageRequestDto,
  UserTokenPagesResultDto,
  FacebookAccountDto,
  RunPodGpuLiveStatus
} from '../services/api';
import { useToast } from '../components/feedback';
import { VastMachinesPanel } from '../components/VastMachinesPanel';

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

function calculateDynamicEpisodeProgress(status: string, batchDetail?: string | null): number {
  if (status === 'Completed') return 100;
  if (status === 'GpuDone' || status === 'FilmEditing' || status === 'QualityCheck') return 95;
  if (status === 'GpuRendering' && batchDetail) {
    const motionMatch = batchDetail.match(/Stage motion:\s*(\d+)\/(\d+)/i);
    if (motionMatch) {
      const cur = parseInt(motionMatch[1], 10);
      const total = parseInt(motionMatch[2], 10) || 26;
      return Math.min(92, Math.round(65 + (cur / total) * 27));
    }
    const imageMatch = batchDetail.match(/Stage image:\s*(\d+)\/(\d+)/i);
    if (imageMatch) {
      const cur = parseInt(imageMatch[1], 10);
      const total = parseInt(imageMatch[2], 10) || 16;
      return Math.min(65, Math.round(45 + (cur / total) * 20));
    }
    return 75;
  }
  return getStatusPercent(status);
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
  const distinctPages = useMemo(() => {
    const map = new Map<string, FilmPageDto>();
    for (const p of pages) {
      const key = p.linkedFacebookAccountId || p.id;
      if (!map.has(key)) {
        map.set(key, p);
      }
    }
    return Array.from(map.values());
  }, [pages]);
  const [projects, setProjects] = useState<FilmProjectDto[]>([]);
  const [selectedProject, setSelectedProject] = useState<FilmProjectDetailDto | null>(null);
  const [selectedEpisode, setSelectedEpisode] = useState<FilmEpisodeDetailDto | null>(null);
  const [previewVideoEpisode, setPreviewVideoEpisode] = useState<FilmEpisodeDto | null>(null);
  const [trackingEpisode, setTrackingEpisode] = useState<FilmEpisodeDto | null>(null);
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

  // Connect Real Facebook Page Modal States
  const [showConnectFbModal, setShowConnectFbModal] = useState(false);
  const [connectFbTab, setConnectFbTab] = useState<'quick' | 'existing' | 'manual'>('quick');
  const [userToken, setUserToken] = useState('');
  const [loadingUserTokenPages, setLoadingUserTokenPages] = useState(false);
  const [userTokenResult, setUserTokenResult] = useState<UserTokenPagesResultDto | null>(null);
  const [selectedUserTokenPages, setSelectedUserTokenPages] = useState<string[]>([]);
  const [connectingUserToken, setConnectingUserToken] = useState(false);

  // Character portrait states
  const [generatingPortraitId, setGeneratingPortraitId] = useState<string | null>(null);
  const [uploadingPortraitId, setUploadingPortraitId] = useState<string | null>(null);
  const [copiedPromptId, setCopiedPromptId] = useState<string | null>(null);
  const [portraitTimestamps, setPortraitTimestamps] = useState<Record<string, number>>({});
  const [generatingStoryBible, setGeneratingStoryBible] = useState(false);

  // Existing Facebook Accounts from database
  const [existingFbAccounts, setExistingFbAccounts] = useState<FacebookAccountDto[]>([]);
  const [loadingExistingFbAccounts, setLoadingExistingFbAccounts] = useState(false);

  const [connectingFb, setConnectingFb] = useState(false);
  const [fbForm, setFbForm] = useState<ConnectFacebookPageRequestDto>({
    pageId: '',
    pageName: '',
    pageAccessToken: '',
  });

  useEffect(() => {
    if (showConnectFbModal) {
      setLoadingExistingFbAccounts(true);
      api.getFacebookPages()
        .then((accs: FacebookAccountDto[]) => setExistingFbAccounts(accs))
        .catch(() => undefined)
        .finally(() => setLoadingExistingFbAccounts(false));
    }
  }, [showConnectFbModal]);

  // Create Project Modal & Form
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [createForm, setCreateForm] = useState<CreateFilmProjectRequestDto>({
    filmPageId: '',
    title: '',
    logline: '',
    genre: 'Drama',
    visualArtStyle: 'Cinematic 35mm Realism',
    aspectRatio: '9:16',
    budgetLimitUsd: 5.0,
    targetEpisodes: 5,
    targetDurationMinutes: 2.5,
    autoProduceAll: true,
  });

  // Synchronize createForm.filmPageId with distinctPages
  useEffect(() => {
    if (distinctPages.length > 0 && (!createForm.filmPageId || !distinctPages.some((p) => p.id === createForm.filmPageId))) {
      setCreateForm((prev) => ({
        ...prev,
        filmPageId: distinctPages[0].id,
        genre: distinctPages[0].primaryGenre || prev.genre,
        visualArtStyle: distinctPages[0].visualArtStyle || prev.visualArtStyle,
      }));
    }
  }, [distinctPages]);

  // GPU Status
  const [gpuStatus, setGpuStatus] = useState<RunPodGpuLiveStatus | null>(null);
  const [editingPodId, setEditingPodId] = useState(false);
  const [podInput, setPodInput] = useState('');
  const [savingPodId, setSavingPodId] = useState(false);
  const [stoppingGpu, setStoppingGpu] = useState(false);
  const [startingGpu, setStartingGpu] = useState(false);
  const [vastPanelOpen, setVastPanelOpen] = useState(false);

  const loadGpuStatus = async () => {
    try {
      const st = await api.getRunPodGpuStatus();
      setGpuStatus(st);
      if (st.podId && !podInput) setPodInput(st.podId);
    } catch {
      // ignore
    }
  };

  const handleStopGpu = async () => {
    const isVast = gpuStatus?.provider === 'VastAi';
    const providerName = isVast ? 'Vast.ai' : 'RunPod';
    const rateStr = gpuStatus?.hourlyCost ? `$${gpuStatus.hourlyCost.toFixed(2)}/h` : isVast ? '~$0.28/h' : '$2.22/h';
    const isBusy = gpuStatus?.state === 'busy' || gpuStatus?.activeEpisodeStatus === 'GpuRendering';
    const confirmMsg = isBusy
      ? `GPU ${providerName} đang render tập phim dở dang. Dừng máy sẽ tạm ngừng tiến trình render hiện tại.\n\nBạn có chắc chắn muốn DỪNG GPU để lập tức ngừng tính tiền (${rateStr}) không?`
      : isVast
        ? `Bạn có chắc chắn muốn DỪNG máy GPU ${providerName}?\n\n(Ngừng tính tiền GPU ${rateStr}. Ổ cứng được giữ nhưng Vast.ai VẪN tính phí giữ ổ khi máy tắt — muốn hết phí hẳn thì dùng "Quản lý máy" → Xoá máy).`
        : `Bạn có chắc chắn muốn DỪNG máy GPU ${providerName}?\n\n(Hệ thống sẽ ngừng tính tiền thuê máy ${rateStr} ngay lập tức, toàn bộ dữ liệu ổ cứng vẫn được bảo lưu).`;

    if (!window.confirm(confirmMsg)) return;

    try {
      setStoppingGpu(true);
      const res = await api.stopRunPodGpu(gpuStatus?.podId || undefined);
      toast.success(res.message || `Đã dừng GPU ${providerName} thành công!`);
      await loadGpuStatus();
    } catch (err: any) {
      toast.error(err.message || 'Lỗi khi dừng GPU');
    } finally {
      setStoppingGpu(false);
    }
  };

  const handleStartGpu = async () => {
    const isVast = gpuStatus?.provider === 'VastAi';
    const providerName = isVast ? 'Vast.ai' : 'RunPod';
    try {
      setStartingGpu(true);
      const res = await api.startRunPodGpu(gpuStatus?.podId || undefined);
      toast.success(res.message || `Đã gửi lệnh bật lại GPU ${providerName}! Đang khởi động...`);
      await loadGpuStatus();
    } catch (err: any) {
      toast.error(err.message || 'Lỗi khi bật lại GPU');
    } finally {
      setStartingGpu(false);
    }
  };

  const handleSavePodId = async () => {
    if (!podInput.trim()) return;
    try {
      setSavingPodId(true);
      await api.setRunPodPodId(podInput.trim());
      toast.success('Đã cập nhật GPU ID / Endpoint thành công!');
      setEditingPodId(false);
      await loadGpuStatus();
    } catch (err: any) {
      toast.error(err.message || 'Lỗi cập nhật ID');
    } finally {
      setSavingPodId(false);
    }
  };

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
      toast.error(err.message || 'Lỗi tải danh sách Facebook Film Page');
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

  // Initial Load & GPU Polling
  useEffect(() => {
    loadPages();
    loadProjects();
    loadGpuStatus();
  }, []);

  // Polling while server is processing or GPU is active
  const projectBusy =
    !!selectedProject &&
    (BUSY_PROJECT_STATUSES.includes(selectedProject.status) ||
      gpuStatus?.state === 'busy' ||
      selectedProject.episodes.some((e) => BUSY_EPISODE_STATUSES.includes(e.status)));

  useEffect(() => {
    const isBusy = gpuStatus?.state === 'busy' || projectBusy;
    const interval = isBusy ? 3000 : 8000;
    const gpuTimer = window.setInterval(loadGpuStatus, interval);
    return () => window.clearInterval(gpuTimer);
  }, [gpuStatus?.state, projectBusy]);

  // Sync Project Detail when selection changes
  useEffect(() => {
    if (selectedProjectId) {
      loadProjectDetail(selectedProjectId);
    } else {
      setSelectedProject(null);
      setCostSummary(null);
    }
  }, [selectedProjectId]);

  useEffect(() => {
    if (!selectedProjectId || !projectBusy) return;
    const timer = window.setInterval(async () => {
      try {
        const data = await api.getFilmProjectDetail(selectedProjectId);
        setSelectedProject(data);
        setCostSummary(data.costSummary);
        const projs = await api.getFilmProjects();
        setProjects(projs);
      } catch {
        // Keep last state on failure
      }
    }, 4000);
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

  // Quick Connect User Token Handlers
  const handleFetchUserTokenPages = async () => {
    if (!userToken.trim()) {
      toast.info('Vui lòng dán mã User Access Token từ Facebook');
      return;
    }
    try {
      setLoadingUserTokenPages(true);
      setUserTokenResult(null);
      const r = await api.connectFromUserToken(userToken.trim());
      setUserTokenResult(r);
      // Default tick all pages
      setSelectedUserTokenPages(r.pages.map((p) => p.pageId));
    } catch (err: any) {
      toast.error(err.message || 'Không đọc được danh sách Page từ Facebook');
    } finally {
      setLoadingUserTokenPages(false);
    }
  };

  const handleConnectSelectedUserTokenPages = async () => {
    if (selectedUserTokenPages.length === 0) {
      toast.info('Vui lòng chọn ít nhất 1 Facebook Page để kết nối');
      return;
    }
    try {
      setConnectingUserToken(true);
      const r = await api.connectFromUserToken(userToken.trim(), selectedUserTokenPages);
      const connectedCount = r.pages.filter((p) => p.connected).length;
      toast.success(
        `Đã kết nối thành công ${connectedCount} Facebook Page làm Kênh Phim!`,
        r.longLived ? 'Token vĩnh viễn không bao giờ hết hạn.' : undefined
      );
      setShowConnectFbModal(false);
      setUserToken('');
      setUserTokenResult(null);
      await loadPages();
    } catch (err: any) {
      toast.error(err.message || 'Kết nối Facebook Page thất bại');
    } finally {
      setConnectingUserToken(false);
    }
  };

  const handleSelectExistingAccount = async (account: FacebookAccountDto) => {
    await loadPages();
    toast.success(`Đã đồng bộ Kênh Phim "${account.pageName}" thành công!`);
    setShowConnectFbModal(false);
  };

  // Handle Connect Real Facebook Page (Manual)
  const handleConnectFacebook = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!fbForm.pageId.trim() || !fbForm.pageName.trim() || !fbForm.pageAccessToken.trim()) {
      toast.info('Vui lòng nhập đầy đủ Page ID, Tên Page và Page Access Token');
      return;
    }
    try {
      setConnectingFb(true);
      await api.connectFilmFacebookPage(fbForm);
      toast.success(`Đã kết nối thành công Facebook Page "${fbForm.pageName}"!`);
      setShowConnectFbModal(false);
      setFbForm({ pageId: '', pageName: '', pageAccessToken: '' });
      await loadPages();
    } catch (err: any) {
      toast.error(err.message || 'Kết nối Facebook Page thất bại');
    } finally {
      setConnectingFb(false);
    }
  };

  // Handle Create Project
  const handleCreateProject = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!createForm.title.trim() || !createForm.logline.trim() || !createForm.filmPageId) {
      toast.info('Vui lòng nhập đầy đủ tiêu đề, ý tưởng và chọn Facebook Page');
      return;
    }

    try {
      setProducing(true);
      const created = await api.createFilmProject(createForm);
      toast.success(`Đã tạo series "${created.title}" gồm ${created.targetEpisodes} tập và kích hoạt AI Showrunner!`);
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
      toast.success('Đã kích hoạt quy trình sản xuất toàn bộ các tập phim!');
      await loadProjectDetail(id);
      await loadProjects();
    } catch (err: any) {
      toast.error(err.message || 'Kích hoạt sản xuất thất bại');
    } finally {
      setProducing(false);
    }
  };

  // Render Single Episode (support force)
  const handleRenderEpisode = async (episodeId: string, force: boolean = false) => {
    try {
      setRenderingEpisodeId(episodeId);
      const res = await api.renderFilmEpisode(episodeId, force);
      toast.success(`${res.message} Quá trình diễn ra tự động trong nền.`);
      await loadGpuStatus();
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

  const handleCopyPrompt = (characterId: string, promptText: string) => {
    navigator.clipboard.writeText(promptText);
    setCopiedPromptId(characterId);
    setTimeout(() => setCopiedPromptId(null), 2500);
  };

  const handleGeneratePortrait = async (characterId: string) => {
    try {
      setGeneratingPortraitId(characterId);
      const updated = await api.generateCharacterPortrait(characterId);
      setPortraitTimestamps(prev => ({ ...prev, [characterId]: Date.now() }));
      if (selectedProject) {
        setSelectedProject({
          ...selectedProject,
          characters: selectedProject.characters.map(c => c.id === characterId ? updated : c)
        });
      }
      toast.success(`Đã tạo ảnh mẫu thành công cho ${updated.name}`);
    } catch (err: any) {
      toast.error(`Tạo ảnh chân dung thất bại: ${err.message}`);
    } finally {
      setGeneratingPortraitId(null);
    }
  };

  const handleUploadPortrait = async (characterId: string, file: File) => {
    try {
      setUploadingPortraitId(characterId);
      const updated = await api.uploadCharacterPortrait(characterId, file);
      setPortraitTimestamps(prev => ({ ...prev, [characterId]: Date.now() }));
      if (selectedProject) {
        setSelectedProject({
          ...selectedProject,
          characters: selectedProject.characters.map(c => c.id === characterId ? updated : c)
        });
      }
      toast.success(`Đã tải ảnh mẫu thành công cho ${updated.name}`);
    } catch (err: any) {
      toast.error(`Tải ảnh chân dung thất bại: ${err.message}`);
    } finally {
      setUploadingPortraitId(null);
    }
  };

  const handleGenerateStoryBibleOnly = async () => {
    if (!selectedProjectId) return;
    try {
      setGeneratingStoryBible(true);
      const updated = await api.generateStoryBibleOnly(selectedProjectId);
      setSelectedProject(updated);
      setWorkbenchTab('characters');
      toast.success('Đã lên kịch bản và danh sách nhân vật!');
    } catch (err: any) {
      toast.error(`Tạo kịch bản & nhân vật thất bại: ${err.message}`);
    } finally {
      setGeneratingStoryBible(false);
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
        description="Hệ thống tự động sản xuất phim nhiều tập: AI Showrunner, Wan 2.2 / GPU Cloud (Vast.ai, RunPod), và xuất bản trực tiếp lên Facebook Reels."
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <IconButton
              label="Làm mới dữ liệu"
              onClick={handleRefreshAll}
              className="border border-line-strong bg-surface shadow-card"
            >
              <RefreshCw className={cn('h-4 w-4', (loadingProjects || loadingPages || loadingDetail) && 'animate-spin')} />
            </IconButton>
            <Button
              variant="secondary"
              icon={<Share2 className="h-4 w-4" />}
              onClick={() => setShowConnectFbModal(true)}
            >
              <span className="hidden sm:inline">Kết nối Facebook Page</span>
              <span className="sm:hidden">Kết nối Page</span>
            </Button>
            <Button
              variant="primary"
              icon={<Plus className="h-4 w-4" />}
              onClick={() => setShowCreateModal(true)}
            >
              <span className="hidden sm:inline">Khai hoả Series Mới</span>
              <span className="sm:hidden">Series Mới</span>
            </Button>
          </div>
        }
      />

      {/* ── Top Metric Stats (Always visible when in list view) ── */}
      {!selectedProjectId && (
        <div className="grid grid-cols-2 gap-2.5 sm:gap-3 lg:grid-cols-4">
          <Card className="p-3 sm:p-4">
            <div className="flex items-center gap-2 text-xs text-fg-subtle">
              <Tv className="h-3.5 w-3.5 sm:h-4 sm:w-4 text-fg-muted shrink-0" />
              <span className="truncate">Facebook Film Pages</span>
            </div>
            <div className="mt-1.5 sm:mt-2 text-base sm:text-xl font-semibold text-fg">
              {distinctPages.length} Kênh Kết Nối
            </div>
            <div className="mt-1 text-[11px] text-fg-muted line-clamp-1">Tự động đăng Reels lên Page</div>
          </Card>

          <Card className="p-3 sm:p-4">
            <div className="flex items-center gap-2 text-xs text-fg-subtle">
              <Film className="h-3.5 w-3.5 sm:h-4 sm:w-4 text-fg-muted shrink-0" />
              <span className="truncate">Tổng Series Phim</span>
            </div>
            <div className="mt-1.5 sm:mt-2 text-base sm:text-xl font-semibold text-fg">
              {projects.length} Series
            </div>
            <div className="mt-1 text-[11px] text-fg-muted line-clamp-1">AI Showrunner chỉ đạo</div>
          </Card>

          <Card className="p-3 sm:p-4">
            <div className="flex items-center gap-2 text-xs text-fg-subtle">
              <Clapperboard className="h-3.5 w-3.5 sm:h-4 sm:w-4 text-fg-muted shrink-0" />
              <span className="truncate">Quy Mô Linh Hoạt</span>
            </div>
            <div className="mt-1.5 sm:mt-2 text-base sm:text-xl font-semibold text-fg">
              1 - 20 Tập / Series
            </div>
            <div className="mt-1 text-[11px] text-fg-muted line-clamp-1">1 - 5 phút/tập</div>
          </Card>

          <Card className="p-3 sm:p-4">
            <div className="flex items-center gap-2 text-xs text-fg-subtle">
              <ShieldCheck className="h-3.5 w-3.5 sm:h-4 sm:w-4 text-emerald-500 shrink-0" />
              <span className="truncate">Bảo Vệ Ngân Sách</span>
            </div>
            <div className="mt-1.5 sm:mt-2 text-base sm:text-xl font-semibold text-emerald-500">
              &lt; $5.00 / Series
            </div>
            <div className="mt-1 text-[11px] text-fg-muted line-clamp-1">4 Free Gemini + GPU Cloud</div>
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

            <Card className="overflow-hidden shadow-card border-line">
              {/* Project Overview & Info Header */}
              <div className="p-4 sm:p-6 space-y-4">
                <div className="space-y-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="rounded bg-muted px-2.5 py-0.5 text-xs font-semibold text-fg border border-line">
                      {selectedProject.filmPageName}
                    </span>
                    <span className="rounded bg-muted px-2.5 py-0.5 text-xs text-fg-subtle border border-line">
                      {selectedProject.genre}
                    </span>
                    <span className="rounded bg-muted px-2.5 py-0.5 text-xs text-fg-subtle border border-line">
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

                  <h2 className="text-xl sm:text-2xl font-bold text-fg tracking-tight leading-snug">{selectedProject.title}</h2>
                  <p className="text-xs sm:text-sm text-fg-muted max-w-3xl leading-relaxed">{selectedProject.logline}</p>

                  <div className="flex flex-wrap items-center gap-4 sm:gap-6 pt-1 text-xs text-fg-subtle">
                    <div className="flex items-center gap-1.5">
                      <Clapperboard className="h-3.5 w-3.5 text-accent" />
                      Quy mô:{' '}
                      <strong className="text-fg font-semibold">
                        {selectedProject.completedEpisodesCount} / {selectedProject.targetEpisodes} tập hoàn thành
                      </strong>{' '}
                      (~{Math.round(selectedProject.targetDurationPerEpisodeSeconds / 60 * 10) / 10} phút/tập)
                    </div>
                    <div className="flex items-center gap-1.5">
                      <DollarSign className="h-3.5 w-3.5 text-emerald-500" />
                      Chi phí thực tế:{' '}
                      <strong className="text-emerald-500 font-semibold">
                        ${selectedProject.totalActualCostUsd.toFixed(4)}
                      </strong>{' '}
                      / ${selectedProject.totalBudgetLimitUsd.toFixed(2)} USD
                    </div>
                  </div>

                  {/* Progress bar */}
                  <div className="pt-1 max-w-md">
                    <ProgressBar
                      value={(selectedProject.completedEpisodesCount / (selectedProject.targetEpisodes || 1)) * 100}
                      tone={selectedProject.completedEpisodesCount === selectedProject.targetEpisodes ? 'success' : 'info'}
                    />
                  </div>
                </div>

                {/* Project Action Toolbar: Desktop & Mobile responsive */}
                <div className="pt-3 border-t border-line/70 flex flex-col sm:flex-row sm:items-center justify-between gap-2.5">
                  <div className="flex flex-wrap items-center gap-2">
                    <Button
                      variant="primary"
                      size="sm"
                      className="w-full sm:w-auto"
                      icon={producing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4 fill-current" />}
                      loading={producing}
                      disabled={producing || generatingStoryBible}
                      onClick={() => handleTriggerProduce(selectedProject.id)}
                    >
                      Dựng toàn bộ {selectedProject.targetEpisodes} tập
                    </Button>

                    {(!selectedProject.storyBible || selectedProject.characters.length === 0) && (
                      <Button
                        variant="secondary"
                        size="sm"
                        className="w-full sm:w-auto"
                        icon={generatingStoryBible ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4 text-amber-400" />}
                        loading={generatingStoryBible}
                        disabled={generatingStoryBible || producing}
                        onClick={handleGenerateStoryBibleOnly}
                      >
                        Lên kịch bản & nhân vật trước
                      </Button>
                    )}
                  </div>

                  <div className="flex items-center gap-2 self-end sm:self-auto">
                    <Button
                      variant="secondary"
                      size="sm"
                      icon={<RotateCw className={cn('h-3.5 w-3.5', loadingDetail && 'animate-spin')} />}
                      onClick={() => loadProjectDetail(selectedProject.id)}
                    >
                      Tải lại
                    </Button>
                    <Button
                      variant="danger-soft"
                      size="sm"
                      icon={<Trash2 className="h-3.5 w-3.5" />}
                      onClick={() => setDeletingProject({ id: selectedProject.id, title: selectedProject.title })}
                      title="Xóa series này"
                    >
                      Xóa Series
                    </Button>
                  </div>
                </div>
              </div>

              {/* Live GPU & Production Command Center: Modern Responsive Deck */}
              <div className="bg-surface border-t border-line p-3.5 sm:p-4 space-y-3">
                {/* Deck Row: GPU spec, Disk metric, Master Power Button, Pod controls */}
                <div className="flex flex-col md:flex-row md:items-center justify-between gap-3 text-xs">
                  <div className="flex flex-wrap items-center gap-2 sm:gap-2.5">
                    <span className="font-semibold text-fg flex items-center gap-1.5 shrink-0">
                      <Cpu className="h-4 w-4 text-accent" />
                      {gpuStatus?.provider === 'VastAi' ? 'Vast.ai GPU:' : 'GPU Máy Chủ:'}
                    </span>

                    {gpuStatus?.online && gpuStatus.gpu ? (
                      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-[11px] font-semibold bg-emerald-500/10 text-emerald-500 border border-emerald-500/20 shadow-sm">
                        <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
                        Online: {gpuStatus.gpu} ({gpuStatus.vramGb || 24}GB VRAM)
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-[11px] font-medium bg-amber-500/10 text-amber-500 border border-amber-500/20" title={gpuStatus?.pingDetail || ''}>
                        <span className="w-2 h-2 rounded-full bg-amber-500" />
                        {gpuStatus?.podId ? (gpuStatus.podDesiredStatus === 'stopped' ? `Đã tắt máy (${gpuStatus.podId})` : `Chờ Worker (${gpuStatus.podId})`) : 'Chưa kết nối GPU'}
                      </span>
                    )}

                    {gpuStatus?.diskTotalGb && (
                      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded bg-muted/60 text-[11px] text-fg-muted font-mono border border-line">
                        <HardDrive className="h-3 w-3 text-fg-subtle" />
                        Ổ cứng: còn trống {gpuStatus.diskFreeGb} GB / {gpuStatus.diskTotalGb} GB
                      </span>
                    )}
                  </div>

                  <div className="flex flex-wrap items-center gap-2 w-full md:w-auto justify-between md:justify-end">
                    {/* Master GPU Switch: Start / Stop to save money */}
                    {gpuStatus?.hasApiKey && (
                      (gpuStatus.online || gpuStatus.podDesiredStatus === 'RUNNING' || gpuStatus.podDesiredStatus === 'running') ? (
                        <Button
                          size="sm"
                          variant="danger"
                          icon={<PowerOff className="h-3.5 w-3.5" />}
                          loading={stoppingGpu}
                          onClick={handleStopGpu}
                          className="flex-1 sm:flex-initial"
                          title={`Dừng máy GPU trên ${gpuStatus?.provider === 'VastAi' ? 'Vast.ai' : 'RunPod'} để lập tức ngừng tính tiền (Ổ cứng và dữ liệu vẫn được giữ nguyên)`}
                        >
                          Dừng GPU ({gpuStatus?.hourlyCost ? `$${gpuStatus.hourlyCost.toFixed(2)}/h` : gpuStatus?.provider === 'VastAi' ? '~$0.28/h' : '$2.22/h'})
                        </Button>
                      ) : (
                        <Button
                          size="sm"
                          variant="primary"
                          icon={<Play className="h-3.5 w-3.5" />}
                          loading={startingGpu}
                          onClick={handleStartGpu}
                          className="flex-1 sm:flex-initial"
                          title={`Bật lại máy GPU trên ${gpuStatus?.provider === 'VastAi' ? 'Vast.ai' : 'RunPod'} để tiếp tục dựng phim`}
                        >
                          Bật Lại GPU
                        </Button>
                      )
                    )}

                    {gpuStatus?.provider === 'VastAi' && (
                      <Button
                        size="sm"
                        variant="secondary"
                        icon={<HardDrive className="h-3.5 w-3.5" />}
                        onClick={() => setVastPanelOpen(true)}
                        className="flex-1 sm:flex-initial"
                        title="Tìm & thuê máy GPU theo cấu hình phim cần, hoặc xoá máy để ngừng mọi chi phí"
                      >
                        Quản lý máy Vast
                      </Button>
                    )}

                    {editingPodId ? (
                      <div className="flex items-center gap-1.5 flex-1 sm:flex-initial">
                        <input
                          type="text"
                          placeholder="Vast ID hoặc URL http://..."
                          className="px-2.5 py-1 text-xs rounded border border-line bg-surface text-fg focus:outline-none focus:ring-1 focus:ring-accent w-full sm:w-48"
                          value={podInput}
                          onChange={(e) => setPodInput(e.target.value)}
                        />
                        <Button size="sm" variant="primary" loading={savingPodId} onClick={handleSavePodId}>
                          Lưu
                        </Button>
                        <Button size="sm" variant="ghost" onClick={() => setEditingPodId(false)}>
                          Hủy
                        </Button>
                      </div>
                    ) : (
                      <Button size="sm" variant="ghost" onClick={() => setEditingPodId(true)}>
                        Đổi ID/URL ({gpuStatus?.podId || (gpuStatus?.provider === 'VastAi' ? 'Vast.ai' : 'GPU')})
                      </Button>
                    )}
                  </div>
                </div>

                {/* Live Mission Control Banner */}
                {gpuStatus?.online && (
                  <div className={cn(
                    "rounded-lg p-3 sm:p-3.5 text-xs border transition-all",
                    gpuStatus.state === 'busy' || gpuStatus.activeEpisodeStatus
                      ? "bg-accent/5 border-accent/30 text-fg shadow-sm"
                      : "bg-muted/40 border-line text-fg-muted"
                  )}>
                    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                      <div className="flex items-start sm:items-center gap-2.5">
                        {gpuStatus.state === 'busy' || gpuStatus.activeEpisodeStatus ? (
                          <div className="flex h-7 w-7 items-center justify-center rounded-full bg-accent/20 text-accent shrink-0 mt-0.5 sm:mt-0">
                            <Loader2 className="h-4 w-4 animate-spin" />
                          </div>
                        ) : (
                          <div className="flex h-7 w-7 items-center justify-center rounded-full bg-emerald-500/20 text-emerald-500 shrink-0 mt-0.5 sm:mt-0">
                            <CheckCircle2 className="h-4 w-4" />
                          </div>
                        )}
                        <div>
                          <span className="font-semibold text-fg text-xs sm:text-sm">
                            {gpuStatus.state === 'busy'
                              ? `GPU đang thực thi: ${gpuStatus.currentBatch || 'Tác vụ'}`
                              : gpuStatus.activeEpisodeStatus
                              ? `Tiến trình: Tập ${gpuStatus.activeEpisodeNumber || '?'}: ${gpuStatus.activeEpisodeTitle || ''} (${EPISODE_STATUS_LABELS[gpuStatus.activeEpisodeStatus] || gpuStatus.activeEpisodeStatus})`
                              : 'GPU đang ở trạng thái rảnh rỗi (Idle) — Sẵn sàng nhận việc'}
                          </span>
                          {gpuStatus.batchDetail ? (
                            <div className="mt-1 text-[11px] text-accent font-medium flex items-center gap-1.5">
                              <span className="w-1.5 h-1.5 rounded-full bg-accent animate-ping" />
                              Chi tiết: {gpuStatus.batchDetail}
                            </div>
                          ) : (
                            <div className="mt-0.5 text-[11px] text-fg-subtle">
                              Trang tự động cập nhật liên tục mỗi 3-4 giây.
                            </div>
                          )}
                        </div>
                      </div>

                      {gpuStatus.activeEpisodeId && (
                        <div className="flex flex-wrap sm:flex-nowrap items-center gap-2 w-full sm:w-auto">
                          <Button
                            size="sm"
                            variant="primary"
                            icon={<Activity className="h-3.5 w-3.5" />}
                            className="flex-1 sm:flex-initial"
                            onClick={() => {
                              const found = selectedProject?.episodes.find(e => e.id === gpuStatus.activeEpisodeId);
                              setTrackingEpisode(found || {
                                id: gpuStatus.activeEpisodeId!,
                                filmProjectId: selectedProject?.id || '',
                                episodeNumber: gpuStatus.activeEpisodeNumber || 2,
                                title: gpuStatus.activeEpisodeTitle || `Tập ${gpuStatus.activeEpisodeNumber}`,
                                synopsis: '',
                                openingHook: '',
                                endingCliffhanger: '',
                                targetDurationSeconds: 180,
                                actualDurationSeconds: 0,
                                status: gpuStatus.activeEpisodeStatus || 'GpuRendering',
                                estimatedCostUsd: 0,
                                actualCostUsd: 0,
                                qualityScore: 0,
                                scenesCount: 4,
                                shotsCount: 26,
                              } as FilmEpisodeDto);
                            }}
                          >
                            Xem Tiến Độ Chi Tiết
                          </Button>
                          <Button
                            size="sm"
                            variant="secondary"
                            icon={<RotateCw className="h-3 w-3" />}
                            className="flex-1 sm:flex-initial"
                            onClick={() => handleRenderEpisode(gpuStatus.activeEpisodeId!, true)}
                          >
                            Ép Dựng Lại
                          </Button>
                        </div>
                      )}
                    </div>
                  </div>
                )}

                {/* Stalled Warning */}
                {gpuStatus?.isStalled && (
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 bg-amber-500/10 border border-amber-500/30 rounded-lg p-3 text-xs text-amber-500">
                    <div className="flex items-center gap-2">
                      <AlertTriangle className="h-4 w-4 shrink-0" />
                      <div>
                        <span className="font-semibold">Phát hiện tác vụ bị ngắt quãng:</span>{' '}
                        Tập {gpuStatus.activeEpisodeNumber} đang ghi nhận là GpuRendering nhưng máy GPU báo rảnh rỗi (do máy vừa khởi động lại).
                      </div>
                    </div>
                    {gpuStatus.activeEpisodeId && (
                      <Button
                        size="sm"
                        variant="primary"
                        onClick={() => handleRenderEpisode(gpuStatus.activeEpisodeId!, true)}
                      >
                        Gỡ Kẹt & Tiếp Tục Ngay
                      </Button>
                    )}
                  </div>
                )}
              </div>
            </Card>
          </div>

          {/* Workbench Tabs */}
          <Tabs
            items={[
              {
                id: 'episodes',
                label: `${selectedProject.episodes.length} Tập Phim`,
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

          {/* ── SUB-TAB: EPISODES ── */}
          {workbenchTab === 'episodes' && (
            <div className="space-y-4">
              <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
                {selectedProject.episodes.map((ep) => {
                  const isCompleted = ep.status === 'Completed' || (!!ep.outputVideoPath && ep.status !== 'VoiceGenerating' && ep.status !== 'Queued' && ep.status !== 'GpuQueued' && ep.status !== 'GpuRendering');
                  const isEpBusy = !isCompleted && (BUSY_EPISODE_STATUSES.includes(ep.status) || renderingEpisodeId === ep.id);
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
                                Thời lượng mục tiêu: ~{Math.round(ep.targetDurationSeconds / 60 * 10) / 10} phút ({ep.scenesCount} cảnh · {ep.shotsCount} shots)
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
                              <div className="flex items-center gap-2">
                                <Button
                                  size="sm"
                                  variant="ghost"
                                  className="h-6 px-2 text-[11px] text-accent hover:text-accent-strong flex items-center gap-1"
                                  onClick={() => setTrackingEpisode(ep)}
                                >
                                  <Activity className="h-3 w-3" />
                                  Xem tiến độ
                                </Button>
                                <span className="text-[11px] text-fg-subtle font-mono font-medium">
                                  {calculateDynamicEpisodeProgress(ep.status, ((gpuStatus?.activeEpisodeId === ep.id) || (gpuStatus?.currentBatch && gpuStatus.currentBatch.startsWith(`ep${ep.episodeNumber}-`))) ? gpuStatus?.batchDetail : null)}%
                                </span>
                              </div>
                            </div>
                            <div className="h-1.5 w-full bg-line rounded-full overflow-hidden">
                              <div
                                className="h-full bg-fg animate-pulse rounded-full transition-all duration-500"
                                style={{ width: `${calculateDynamicEpisodeProgress(ep.status, ((gpuStatus?.activeEpisodeId === ep.id) || (gpuStatus?.currentBatch && gpuStatus.currentBatch.startsWith(`ep${ep.episodeNumber}-`))) ? gpuStatus?.batchDetail : null)}%` }}
                              />
                            </div>
                            {((gpuStatus?.activeEpisodeId === ep.id) || (gpuStatus?.currentBatch && gpuStatus.currentBatch.startsWith(`ep${ep.episodeNumber}-`))) && gpuStatus?.batchDetail ? (
                              <div className="text-[11px] font-medium text-emerald-500 flex items-center gap-1.5 bg-emerald-500/10 px-2 py-1 rounded border border-emerald-500/20">
                                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-ping" />
                                <span>{gpuStatus.batchDetail}</span>
                              </div>
                            ) : (
                              <p className="text-[11px] text-fg-subtle">
                                {ep.status === 'GpuRendering' ? (gpuStatus?.gpu ? `Đang thực thi mô hình AI trên RunPod (${gpuStatus.gpu})...` : 'Đang thực thi mô hình AI trên RunPod...') : 'GPU và AI đang xử lý ngầm, dữ liệu sẽ tự cập nhật khi hoàn thành.'}
                              </p>
                            )}
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

                      {/* Card Footer Actions: Responsive Desktop & Mobile 3-col Grid */}
                      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2.5 border-t border-line px-4 py-3 bg-muted/20 sm:px-5">
                        <span className="text-[11px] text-fg-subtle">
                          Chi phí: <strong className="text-fg font-medium">${ep.actualCostUsd.toFixed(4)}</strong>
                        </span>

                        <div className="grid grid-cols-3 gap-1.5 sm:flex sm:items-center sm:gap-2 w-full sm:w-auto">
                          <Button
                            size="sm"
                            variant="ghost"
                            className="justify-center text-xs"
                            icon={<Activity className="h-3 w-3 text-fg-subtle" />}
                            onClick={() => setTrackingEpisode(ep)}
                          >
                            Tiến độ
                          </Button>

                          <Button
                            size="sm"
                            variant="ghost"
                            className="justify-center text-xs"
                            onClick={() => handleViewEpisodeShots(ep.id)}
                          >
                            Phân cảnh
                          </Button>

                          {isCompleted ? (
                            <Button
                              size="sm"
                              variant="secondary"
                              className="justify-center text-xs"
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
                              className="justify-center text-xs"
                              loading
                              disabled
                            >
                              Đang dựng
                            </Button>
                          ) : isFailed ? (
                            <Button
                              size="sm"
                              variant="secondary"
                              className="justify-center text-xs"
                              icon={<RotateCw className="h-3 w-3" />}
                              onClick={() => handleRenderEpisode(ep.id, true)}
                            >
                              Thử lại
                            </Button>
                          ) : (
                            <Button
                              size="sm"
                              variant="secondary"
                              className="justify-center text-xs"
                              icon={<Play className="h-3 w-3" />}
                              onClick={() => handleRenderEpisode(ep.id)}
                            >
                              Dựng tập
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
            <div className="space-y-4">
              {/* Notice guide banner */}
              <div className="rounded-lg border border-blue-500/30 bg-blue-500/10 p-4 text-xs text-fg flex items-start gap-3">
                <Sparkles className="h-5 w-5 text-blue-400 shrink-0 mt-0.5" />
                <div className="space-y-1">
                  <h5 className="font-semibold text-blue-300 text-sm">
                    Studio Khóa Diện Mạo Diễn Viên (Character Consistency & Face Anchor)
                  </h5>
                  <p className="text-fg-muted leading-relaxed">
                    Để nhân vật giữ nguyên 100% khuôn mặt, vóc dáng và thần thái xuyên suốt mọi tập phim, mỗi nhân vật cần có 1 ảnh chân dung mốc chuẩn.
                    Bạn có thể bấm <strong>"Tạo ảnh mẫu với AI"</strong> để hệ thống tự tạo, hoặc bấm <strong>"Sao chép Prompt"</strong> dán vào Gemini tạo ảnh rồi bấm <strong>"Tải ảnh lên"</strong>. Khi render Wan 2.1, hệ thống sẽ tự động khóa khuôn mặt theo ảnh này!
                  </p>
                </div>
              </div>

              {selectedProject.characters.length === 0 ? (
                <Card className="p-8 text-center space-y-3">
                  <Users className="h-10 w-10 text-fg-muted mx-auto" />
                  <h4 className="font-semibold text-fg">Chưa có danh sách nhân vật</h4>
                  <p className="text-xs text-fg-muted max-w-md mx-auto">
                    Dự án chưa có hồ sơ kịch bản và nhân vật. Hãy bấm tạo kịch bản & nhân vật để AI lên dàn diễn viên trước.
                  </p>
                  <Button
                    onClick={handleGenerateStoryBibleOnly}
                    disabled={generatingStoryBible}
                  >
                    {generatingStoryBible ? (
                      <>
                        <Loader2 className="h-4 w-4 animate-spin mr-2" />
                        Đang tạo kịch bản & nhân vật...
                      </>
                    ) : (
                      <>
                        <Sparkles className="h-4 w-4 mr-2" />
                        Tạo Kịch Bản & Nhân Vật với AI
                      </>
                    )}
                  </Button>
                </Card>
              ) : (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
                  {selectedProject.characters.map((c) => {
                    const hasPortrait = !!c.frontReferenceImageUrl;
                    const isGenThis = generatingPortraitId === c.id;
                    const isUpThis = uploadingPortraitId === c.id;
                    const isCopied = copiedPromptId === c.id;
                    const portraitSrc = `/api/film-factory/characters/${c.id}/portrait?t=${portraitTimestamps[c.id] || ''}`;

                    return (
                      <Card key={c.id} className="flex flex-col justify-between overflow-hidden border-line">
                        <div className="p-4 space-y-3.5">
                          {/* Header: Name, Role, ID */}
                          <div className="flex items-start justify-between gap-2 border-b border-line pb-2.5">
                            <div>
                              <h4 className="font-semibold text-fg text-base">{c.name}</h4>
                              <span className="text-xs text-fg-subtle">{c.role} · {c.gender} · {c.age} tuổi</span>
                            </div>
                            <span className="rounded bg-muted px-2 py-0.5 text-[11px] text-fg-muted font-mono">{c.characterId}</span>
                          </div>

                          {/* Big Portrait Frame */}
                          <div className="relative aspect-[3/4] w-full rounded-lg overflow-hidden bg-muted/60 border border-line flex items-center justify-center group">
                            {hasPortrait ? (
                              <img
                                src={portraitSrc}
                                alt={c.name}
                                className="w-full h-full object-cover transition-transform group-hover:scale-105 duration-300"
                              />
                            ) : (
                              <div className="text-center p-4 space-y-2">
                                <div className="h-12 w-12 rounded-full bg-muted mx-auto flex items-center justify-center font-bold text-lg text-fg border border-line">
                                  {c.name.substring(0, 1)}
                                </div>
                                <div className="text-xs text-fg-muted font-medium">Chưa có ảnh chân dung mốc</div>
                                <div className="text-[11px] text-fg-subtle">Bấm tạo ảnh AI hoặc tải ảnh lên bên dưới</div>
                              </div>
                            )}

                            {/* Status Overlay Badge */}
                            <div className="absolute top-2.5 left-2.5">
                              {hasPortrait ? (
                                <Badge tone="success" dot className="bg-emerald-950/80 backdrop-blur-sm border border-emerald-500/40 text-emerald-200">
                                  <CheckCircle2 className="h-3 w-3 mr-1 inline" />
                                  Đã khóa khuôn mặt
                                </Badge>
                              ) : (
                                <Badge tone="warning" dot className="bg-amber-950/80 backdrop-blur-sm border border-amber-500/40 text-amber-200">
                                  <AlertTriangle className="h-3 w-3 mr-1 inline" />
                                  Chưa có ảnh mẫu
                                </Badge>
                              )}
                            </div>
                          </div>

                          {/* Character Specs */}
                          <div className="text-xs space-y-1.5 text-fg-muted bg-muted/30 p-2.5 rounded-md border border-line/60">
                            <div><strong className="text-fg">Ngoại hình:</strong> {c.physicalAppearance}</div>
                            <div><strong className="text-fg">Trang phục:</strong> {c.defaultCostume}</div>
                            <div><strong className="text-fg">Tính cách:</strong> {c.personality}</div>
                            <div><strong className="text-fg">Giọng đọc:</strong> <span className="font-mono text-fg text-[11px] bg-muted px-1.5 py-0.5 rounded border border-line">{c.voiceModel}</span></div>
                          </div>

                          {/* Prompt Box with Quick Copy */}
                          <div className="space-y-1.5">
                            <div className="flex items-center justify-between text-[11px]">
                              <span className="text-fg font-semibold flex items-center gap-1">
                                <Sparkles className="h-3 w-3 text-amber-400" />
                                Visual Anchor Prompt (EN):
                              </span>
                              <button
                                type="button"
                                onClick={() => handleCopyPrompt(c.id, c.consistencyPrompt)}
                                className={cn(
                                  "flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-medium transition-colors border",
                                  isCopied
                                    ? "bg-emerald-500/20 text-emerald-300 border-emerald-500/40"
                                    : "bg-muted hover:bg-muted/80 text-fg-muted hover:text-fg border-line"
                                )}
                              >
                                {isCopied ? (
                                  <>
                                    <Check className="h-3 w-3 text-emerald-400" />
                                    Đã chép prompt!
                                  </>
                                ) : (
                                  <>
                                    <Copy className="h-3 w-3" />
                                    Sao chép Prompt
                                  </>
                                )}
                              </button>
                            </div>
                            <div className="rounded bg-muted/70 p-2 text-[10px] font-mono text-fg-subtle break-words max-h-24 overflow-y-auto border border-line/60">
                              {c.consistencyPrompt}
                            </div>
                          </div>
                        </div>

                        {/* Bottom Actions: Generate with AI or Upload Custom */}
                        <div className="p-3 bg-muted/20 border-t border-line grid grid-cols-2 gap-2">
                          <Button
                            size="sm"
                            variant="secondary"
                            className="w-full text-xs"
                            disabled={isGenThis || isUpThis}
                            onClick={() => handleGeneratePortrait(c.id)}
                          >
                            {isGenThis ? (
                              <>
                                <Loader2 className="h-3.5 w-3.5 animate-spin mr-1.5" />
                                Đang vẽ...
                              </>
                            ) : (
                              <>
                                <Sparkles className="h-3.5 w-3.5 text-blue-400 mr-1.5" />
                                Tạo ảnh mẫu AI
                              </>
                            )}
                          </Button>

                          <label className={cn(
                            "flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-md border border-line bg-muted/50 hover:bg-muted text-xs font-medium cursor-pointer transition-colors text-fg",
                            (isGenThis || isUpThis) && "opacity-50 pointer-events-none"
                          )}>
                            {isUpThis ? (
                              <>
                                <Loader2 className="h-3.5 w-3.5 animate-spin" />
                                <span>Đang tải...</span>
                              </>
                            ) : (
                              <>
                                <Upload className="h-3.5 w-3.5 text-emerald-400" />
                                <span>Tải ảnh lên</span>
                              </>
                            )}
                            <input
                              type="file"
                              accept="image/png,image/jpeg,image/webp"
                              className="hidden"
                              onChange={(e) => {
                                const file = e.target.files?.[0];
                                if (file) handleUploadPortrait(c.id, file);
                                e.target.value = '';
                              }}
                            />
                          </label>
                        </div>
                      </Card>
                    );
                  })}
                </div>
              )}
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
                  <div className="text-xs text-fg-muted mt-1">Đủ hoàn tất các tập còn lại</div>
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
                    <span className="text-fg-muted">Tạo khung hình & Video GPU Wan ({gpuStatus?.gpu || (gpuStatus?.provider === 'VastAi' ? 'Vast.ai GPU' : 'Cloud GPU')}):</span>
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
            MODE B: OVERVIEW (SERIES LIST & REAL FACEBOOK FILM PAGES)
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
                label: 'Kênh Facebook Phim Đã Kết Nối',
                count: distinctPages.length,
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
                  description="Hãy bấm 'Khai hoả Series Mới' để AI Showrunner tự động lên cốt truyện và chuẩn bị sản xuất."
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
                                Quy mô:{' '}
                                <strong className="text-fg font-medium">
                                  {proj.completedEpisodesCount} / {proj.targetEpisodes} tập
                                </strong>{' '}
                                (~{Math.round(proj.targetDurationPerEpisodeSeconds / 60 * 10) / 10} phút/tập)
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
                                value={(proj.completedEpisodesCount / (proj.targetEpisodes || 1)) * 100}
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
                              Dựng {proj.targetEpisodes} tập
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

          {/* ── MAIN TAB: REAL FACEBOOK FILM PAGES ── */}
          {activeMainTab === 'pages' && (
            <div className="space-y-4">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                <div>
                  <h3 className="text-base font-semibold text-fg">Facebook Pages Kênh Phim</h3>
                  <p className="text-xs text-fg-muted">
                    Các trang Facebook thật đang kết nối. Video và Reels tạo từ Film Factory sẽ xuất bản trực tiếp lên trang này.
                  </p>
                </div>
                <Button
                  variant="primary"
                  size="sm"
                  icon={<Plus className="h-4 w-4" />}
                  onClick={() => setShowConnectFbModal(true)}
                >
                  Kết nối Facebook Page
                </Button>
              </div>

              {distinctPages.length === 0 ? (
                <EmptyState
                  icon={<Tv />}
                  title="Chưa có Facebook Page nào được kết nối"
                  description="Hãy kết nối ít nhất 1 Facebook Page thật (bằng Page ID và Access Token) để bắt đầu sản xuất phim."
                  action={
                    <Button
                      variant="primary"
                      icon={<Plus className="h-4 w-4" />}
                      onClick={() => setShowConnectFbModal(true)}
                    >
                      Kết nối Facebook Page Ngay
                    </Button>
                  }
                />
              ) : (
                <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
                  {distinctPages.map((p) => (
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
                              <h3 className="font-semibold text-fg text-sm">{p.linkedFacebookPageName || p.name}</h3>
                              <div className="text-xs text-fg-subtle">
                                {p.linkedFacebookPageId ? `ID: ${p.linkedFacebookPageId}` : p.primaryGenre}
                              </div>
                            </div>
                          </div>
                          <Badge tone="success" dot>Đã liên kết FB</Badge>
                        </div>

                        <p className="text-xs text-fg-muted line-clamp-2 leading-relaxed">{p.description}</p>

                        <div className="space-y-1.5 rounded-md bg-muted/60 p-3 text-xs border border-line">
                          <div className="flex justify-between">
                            <span className="text-fg-subtle">Khung hình chuẩn:</span>
                            <span className="font-medium text-fg">{p.aspectRatio} (Reels/Shorts)</span>
                          </div>
                          <div className="flex justify-between">
                            <span className="text-fg-subtle">Tổng series đã tạo:</span>
                            <span className="font-medium text-fg">{p.totalProjectsCount} series</span>
                          </div>
                          <div className="flex justify-between">
                            <span className="text-fg-subtle">Tập phim hoàn thành:</span>
                            <span className="font-medium text-emerald-500">{p.totalEpisodesCompleted} tập</span>
                          </div>
                        </div>

                        <div className="flex items-center justify-between pt-1">
                          <span className="text-[11px] text-fg-subtle">
                            Trạng thái: <strong className="text-emerald-500 font-medium">Hoạt động</strong>
                          </span>
                          <Button
                            size="sm"
                            variant="primary"
                            onClick={() => {
                              setCreateForm((prev) => ({ ...prev, filmPageId: p.id, genre: p.primaryGenre }));
                              setShowCreateModal(true);
                            }}
                          >
                            Tạo Series cho Page này &rarr;
                          </Button>
                        </div>
                      </div>
                    </Card>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* ── MODAL: CONNECT REAL FACEBOOK PAGE ── */}
      <Modal
        open={showConnectFbModal}
        onClose={() => setShowConnectFbModal(false)}
        title="Kết nối Facebook Page cho Kênh Phim"
      >
        <div className="space-y-4">
          <Tabs
            items={[
              { id: 'quick', label: '⚡ Quét tự động từ Facebook (Token vĩnh viễn)' },
              { id: 'existing', label: `📋 Page đã có sẵn (${existingFbAccounts.length})` },
              { id: 'manual', label: '⚙️ Nhập thủ công' },
            ]}
            value={connectFbTab}
            onChange={(id) => setConnectFbTab(id as any)}
          />

          {connectFbTab === 'quick' && (
            <div className="space-y-3">
              <div className="rounded-lg border border-accent/25 bg-accent/5 p-3.5 text-xs text-fg space-y-2">
                <div className="font-semibold text-accent flex items-center gap-1.5">
                  <Sparkles className="h-4 w-4 shrink-0" />
                  Tự động kết nối tất cả Page — Token vĩnh viễn không lo hết hạn
                </div>
                <ol className="list-decimal space-y-1 pl-4 text-fg-muted leading-relaxed">
                  <li>
                    Mở{' '}
                    <a
                      href="https://developers.facebook.com/tools/explorer"
                      target="_blank"
                      rel="noreferrer"
                      className="text-accent underline font-medium inline-flex items-center gap-0.5"
                    >
                      Graph API Explorer <ExternalLink className="h-3 w-3" />
                    </a>
                    . Ô <b>Người dùng hoặc Trang</b> chọn <b>Quay lại mã người dùng</b>.
                  </li>
                  <li>
                    Bấm <b>Generate Access Token</b>, tick chọn các Page (kể cả Page bạn mới tạo).
                  </li>
                  <li>
                    <i>(Để vĩnh viễn không hết hạn)</i> Bấm biểu tượng <b>ⓘ</b> cạnh mã → <b>Mở trong Trình gỡ lỗi</b> → kéo xuống bấm <b>Gia hạn mã truy cập</b>.
                  </li>
                  <li>Copy mã đó dán vào ô bên dưới rồi bấm <b>Lấy danh sách Page</b>.</li>
                </ol>
              </div>

              <div className="space-y-2">
                <Field label={<span>Mã User Access Token <span className="text-danger">*</span></span>}>
                  <div className="flex gap-2">
                    <Input
                      type="password"
                      placeholder="Dán mã EAAG... vào đây"
                      value={userToken}
                      onChange={(e) => {
                        setUserToken(e.target.value);
                        setUserTokenResult(null);
                      }}
                      className="flex-1"
                    />
                    <Button
                      variant="primary"
                      loading={loadingUserTokenPages}
                      onClick={handleFetchUserTokenPages}
                    >
                      Lấy danh sách Page
                    </Button>
                  </div>
                </Field>
              </div>

              {userTokenResult && (
                <div className="space-y-3 pt-2 border-t border-line">
                  {userTokenResult.longLived ? (
                    <div className="rounded-md border border-emerald-500/30 bg-emerald-500/10 p-2.5 text-xs text-emerald-400 flex items-center gap-2">
                      <ShieldCheck className="h-4 w-4 shrink-0" />
                      <span><strong>Tuyệt vời!</strong> Token này là mã dài hạn. Toàn bộ Page được chọn sẽ có <strong>Token VĨNH VIỄN KHÔNG HẾT HẠN</strong>.</span>
                    </div>
                  ) : (
                    <div className="rounded-md border border-amber-500/30 bg-amber-500/10 p-2.5 text-xs text-amber-400 flex items-center gap-2">
                      <AlertTriangle className="h-4 w-4 shrink-0" />
                      <span>Mã ngắn hạn (~1-2 giờ). Nếu bạn có cấu hình AppId/AppSecret trên Railway, hệ thống sẽ tự động nâng cấp sang vĩnh viễn.</span>
                    </div>
                  )}

                  <div className="text-xs font-semibold text-fg">
                    Chọn các Page bạn muốn kết nối làm Kênh Phim ({userTokenResult.pages.length} Page tìm thấy):
                  </div>

                  <div className="max-h-60 overflow-y-auto space-y-2 pr-1">
                    {userTokenResult.pages.map((p) => {
                      const isSelected = selectedUserTokenPages.includes(p.pageId);
                      return (
                        <div
                          key={p.pageId}
                          onClick={() => {
                            setSelectedUserTokenPages((prev) =>
                              prev.includes(p.pageId) ? prev.filter((id) => id !== p.pageId) : [...prev, p.pageId]
                            );
                          }}
                          className={cn(
                            'p-3 rounded-md border cursor-pointer transition-all flex items-center justify-between',
                            isSelected
                              ? 'border-accent bg-accent/10 text-fg'
                              : 'border-line hover:border-line-strong bg-surface text-fg-muted'
                          )}
                        >
                          <div className="space-y-0.5">
                            <div className="font-semibold text-sm text-fg flex items-center gap-1.5">
                              {p.pageName}
                              {p.alreadyConnected && (
                                <Badge tone="success">Đã trong hệ thống</Badge>
                              )}
                              {p.neverExpires && (
                                <Badge tone="info">Vĩnh viễn</Badge>
                              )}
                            </div>
                            <div className="text-[11px] font-mono text-fg-muted">ID: {p.pageId}</div>
                          </div>
                          <input
                            type="checkbox"
                            checked={isSelected}
                            readOnly
                            className="h-4 w-4 rounded border-line text-accent focus:ring-accent pointer-events-none"
                          />
                        </div>
                      );
                    })}
                  </div>

                  <div className="flex justify-end gap-2 pt-2 border-t border-line">
                    <Button variant="secondary" onClick={() => setShowConnectFbModal(false)}>
                      Hủy
                    </Button>
                    <Button
                      variant="primary"
                      loading={connectingUserToken}
                      onClick={handleConnectSelectedUserTokenPages}
                    >
                      Kết nối {selectedUserTokenPages.length} Page đã chọn
                    </Button>
                  </div>
                </div>
              )}
            </div>
          )}

          {connectFbTab === 'existing' && (
            <div className="space-y-3">
              <p className="text-xs text-fg-muted leading-relaxed">
                Tất cả Facebook Page đã từng kết nối vào hệ thống (ở trang Báo / Cài đặt) đều có thể dùng ngay làm Kênh Phim mà không cần nhập lại token.
              </p>

              {loadingExistingFbAccounts ? (
                <div className="py-8 flex justify-center text-fg-muted">
                  <Loader2 className="h-6 w-6 animate-spin" />
                </div>
              ) : existingFbAccounts.length === 0 ? (
                <EmptyState
                  icon={<Tv />}
                  title="Chưa có Page nào trong hệ thống"
                  description="Hãy chuyển sang tab 'Quét tự động từ Facebook' để kết nối Page của bạn bằng User Token một lần duy nhất."
                />
              ) : (
                <div className="max-h-72 overflow-y-auto space-y-2 pr-1">
                  {existingFbAccounts.map((acc) => {
                    const isLinked = distinctPages.some((p) => p.linkedFacebookAccountId === acc.id);
                    return (
                      <div
                        key={acc.id}
                        className="p-3 rounded-md border border-line bg-surface hover:border-line-strong flex items-center justify-between transition-all"
                      >
                        <div className="space-y-0.5">
                          <div className="font-semibold text-sm text-fg flex items-center gap-1.5">
                            {acc.pageName}
                            {isLinked ? (
                              <Badge tone="success">Đã là Kênh Phim</Badge>
                            ) : (
                              <Badge tone="neutral">Trang Báo</Badge>
                            )}
                          </div>
                          <div className="text-[11px] font-mono text-fg-muted">ID: {acc.pageId}</div>
                        </div>
                        <Button
                          size="sm"
                          variant={isLinked ? 'secondary' : 'primary'}
                          onClick={() => handleSelectExistingAccount(acc)}
                        >
                          {isLinked ? 'Đã liên kết' : 'Chọn làm Kênh Phim'}
                        </Button>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          )}

          {connectFbTab === 'manual' && (
            <form onSubmit={handleConnectFacebook} className="space-y-4">
              <p className="text-xs text-fg-muted leading-relaxed">
                Dành cho trường hợp bạn có sẵn Page Access Token riêng lẻ của một Page cụ thể.
              </p>

              <Field
                label={<span>Facebook Page ID <span className="text-danger">*</span></span>}
                hint="Dãy số ID của trang Facebook (VD: 109283746152839)."
              >
                <Input
                  type="text"
                  placeholder="VD: 109283746152839"
                  value={fbForm.pageId}
                  onChange={(e) => setFbForm((prev) => ({ ...prev, pageId: e.target.value }))}
                  required
                />
              </Field>

              <Field
                label={<span>Tên Facebook Page <span className="text-danger">*</span></span>}
                hint="Tên hiển thị của trang Facebook."
              >
                <Input
                  type="text"
                  placeholder="VD: Phim Ngắn Điện Ảnh HD"
                  value={fbForm.pageName}
                  onChange={(e) => setFbForm((prev) => ({ ...prev, pageName: e.target.value }))}
                  required
                />
              </Field>

              <Field
                label={<span>Page Access Token <span className="text-danger">*</span></span>}
                hint="Mã truy cập trang (Page Token) bắt đầu bằng EAAG... có quyền đăng Reels."
              >
                <Textarea
                  rows={3}
                  placeholder="EAAG..."
                  value={fbForm.pageAccessToken}
                  onChange={(e) => setFbForm((prev) => ({ ...prev, pageAccessToken: e.target.value }))}
                  required
                />
              </Field>

              <div className="flex justify-end gap-2 pt-3 border-t border-line">
                <Button variant="secondary" onClick={() => setShowConnectFbModal(false)}>
                  Hủy
                </Button>
                <Button
                  type="submit"
                  variant="primary"
                  loading={connectingFb}
                >
                  Kết nối Facebook Page
                </Button>
              </div>
            </form>
          )}
        </div>
      </Modal>

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
              Hành động này sẽ xóa toàn bộ các tập phim, kịch bản, các nhân vật, bối cảnh và tất cả video clip MP4 liên quan khỏi hệ thống.
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

      {/* ── MODAL: CREATE NEW SERIES (FLEXIBLE EPISODES & DURATION) ── */}
      <Modal
        open={showCreateModal}
        onClose={() => setShowCreateModal(false)}
        title="Khai Hoả Series Phim Mới (AI Showrunner)"
      >
        <form onSubmit={handleCreateProject} className="space-y-4">
          {distinctPages.length === 0 ? (
            <div className="rounded-md border border-warning/40 bg-warning/10 p-3 text-xs text-fg flex items-center justify-between">
              <span>Chưa có Facebook Page nào được kết nối.</span>
              <Button size="sm" variant="primary" onClick={() => { setShowCreateModal(false); setShowConnectFbModal(true); }}>
                Kết nối Page ngay
              </Button>
            </div>
          ) : (
            <Field label={<span>Chọn Facebook Page Kênh Phim <span className="text-danger">*</span></span>}>
              <Select
                value={createForm.filmPageId}
                onChange={(e) => {
                  const pid = e.target.value;
                  const p = distinctPages.find((x) => x.id === pid) || pages.find((x) => x.id === pid);
                  setCreateForm((prev) => ({
                    ...prev,
                    filmPageId: pid,
                    genre: p ? p.primaryGenre : prev.genre,
                    visualArtStyle: p ? p.visualArtStyle : prev.visualArtStyle,
                  }));
                }}
              >
                {distinctPages.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.linkedFacebookPageName ? `${p.linkedFacebookPageName} (Facebook Page)` : p.name}
                  </option>
                ))}
              </Select>
            </Field>
          )}

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
            hint="Mô tả tóm tắt ý tưởng chính để AI Showrunner phát triển cốt truyện xuyên suốt các tập."
          >
            <Textarea
              rows={4}
              placeholder="VD: Một tài xế công nghệ phát hiện mình có thể nhìn trước tai nạn 30 giây, nhưng mỗi lần anh cứu người thì một ký ức thời thơ ấu của anh lại biến mất..."
              value={createForm.logline}
              onChange={(e) => setCreateForm((prev) => ({ ...prev, logline: e.target.value }))}
              required
            />
          </Field>

          {/* Cấu hình linh hoạt: Số tập và Thời lượng mỗi tập */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <Field label="Số tập phim (1 - 20 tập)" hint="Linh hoạt số lượng tập phim bạn muốn tạo.">
              <Input
                type="number"
                min={1}
                max={20}
                value={createForm.targetEpisodes || 5}
                onChange={(e) => setCreateForm((prev) => ({ ...prev, targetEpisodes: parseInt(e.target.value) || 5 }))}
                required
              />
            </Field>

            <Field label="Thời lượng mỗi tập" hint="Thời lượng mục tiêu cho từng tập video.">
              <Select
                value={String(createForm.targetDurationMinutes || 2.5)}
                onChange={(e) => setCreateForm((prev) => ({ ...prev, targetDurationMinutes: parseFloat(e.target.value) || 2.5 }))}
              >
                <option value="1">1 phút (Siêu ngắn / Reels)</option>
                <option value="2">2 phút (Tiêu chuẩn)</option>
                <option value="2.5">2.5 phút (Chuẩn Wan 2.2)</option>
                <option value="3">3 phút (Kịch tính)</option>
                <option value="5">5 phút (Tập dài)</option>
              </Select>
            </Field>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
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
              • Quy mô cấu hình: <strong>{createForm.targetEpisodes || 5} tập × ~{createForm.targetDurationMinutes || 2.5} phút/tập</strong>.
            </p>
            <p className="text-fg-subtle">
              • Ngân sách bảo vệ: <strong>Tối đa $5.00 USD</strong> (luân phiên 4 Gemini free keys + GPU Cloud).
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

      {/* ── MODAL: DETAILED EPISODE PRODUCTION PIPELINE TRACKER ── */}
      {trackingEpisode && (() => {
        const isThisEpActive = (gpuStatus?.activeEpisodeId === trackingEpisode.id) || 
          (!!gpuStatus?.currentBatch && gpuStatus.currentBatch.startsWith(`ep${trackingEpisode.episodeNumber}-`));
        const currentDetail = isThisEpActive ? gpuStatus?.batchDetail : null;
        const totalProgress = calculateDynamicEpisodeProgress(trackingEpisode.status, currentDetail);

        const motionMatch = currentDetail?.match(/Stage motion:\s*(\d+)\/(\d+)/i);
        const imageMatch = currentDetail?.match(/Stage image:\s*(\d+)\/(\d+)/i);

        // Stage 1: Script & Story Bible
        const step1Done = trackingEpisode.status !== 'Draft' && trackingEpisode.status !== 'Pending';
        // Stage 2: Voice acting (EdgeTTS)
        const step2Active = trackingEpisode.status === 'VoiceGenerating';
        const step2Done = ['GpuQueued', 'GpuRendering', 'GpuDone', 'FilmEditing', 'QualityCheck', 'Completed'].includes(trackingEpisode.status);
        // Stage 3: Visual Frame Generation (SDXL)
        const step3Active = isThisEpActive && imageMatch != null;
        const step3Done = motionMatch != null || ['GpuDone', 'FilmEditing', 'QualityCheck', 'Completed'].includes(trackingEpisode.status);
        // Stage 4: Motion Video Generation (Wan 2.1 14B)
        const step4Active = isThisEpActive && motionMatch != null;
        const step4Done = ['GpuDone', 'FilmEditing', 'QualityCheck', 'Completed'].includes(trackingEpisode.status);
        // Stage 5: Assembly & Anti-Reup
        const step5Active = ['GpuDone', 'FilmEditing', 'QualityCheck'].includes(trackingEpisode.status);
        const step5Done = trackingEpisode.status === 'Completed';

        return (
          <Modal
            open={!!trackingEpisode}
            onClose={() => setTrackingEpisode(null)}
            title={`Tiến Độ Sản Xuất: Tập ${trackingEpisode.episodeNumber} - ${trackingEpisode.title}`}
          >
            <div className="space-y-4 text-xs max-h-[75vh] overflow-y-auto pr-1">
              {/* Overall Progress Header */}
              <div className="rounded-lg bg-surface border border-line p-4 space-y-3 shadow-sm">
                <div className="flex items-center justify-between">
                  <div>
                    <div className="text-xs text-fg-subtle font-medium uppercase tracking-wider">
                      Tiến Trình Sản Xuất Tổng Thể
                    </div>
                    <div className="text-xl sm:text-2xl font-bold text-fg flex items-center gap-2 mt-0.5">
                      {totalProgress}%
                      <span className="text-xs font-normal text-fg-subtle">
                        ({EPISODE_STATUS_LABELS[trackingEpisode.status] || trackingEpisode.status})
                      </span>
                    </div>
                  </div>
                  <div className="text-right">
                    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-[11px] font-semibold bg-emerald-500/10 text-emerald-500 border border-emerald-500/20">
                      <Cpu className="h-3 w-3" />
                      {gpuStatus?.gpu || 'NVIDIA A100 SXM 80GB'}
                    </span>
                    <div className="text-[11px] text-fg-subtle mt-1 font-mono">
                      VRAM: {gpuStatus?.vramGb || 80}GB | Trống: {gpuStatus?.diskFreeGb || 203}GB
                    </div>
                  </div>
                </div>

                <div className="h-2.5 w-full bg-line rounded-full overflow-hidden">
                  <div
                    className="h-full bg-emerald-500 rounded-full transition-all duration-700 ease-out"
                    style={{ width: `${totalProgress}%` }}
                  />
                </div>

                {currentDetail && (
                  <div className="text-[11px] text-accent font-medium flex items-center gap-1.5 bg-accent/10 px-2.5 py-1.5 rounded border border-accent/20">
                    <span className="w-2 h-2 rounded-full bg-accent animate-ping" />
                    <span>Chi tiết GPU thời gian thực: {currentDetail}</span>
                  </div>
                )}
              </div>

              {/* 5-Step Pipeline Breakdown */}
              <div className="space-y-2.5">
                <div className="font-semibold text-fg text-xs flex items-center gap-1.5">
                  <Layers className="h-4 w-4 text-accent" />
                  Quy Trình 5 Bước Sản Xuất Điện Ảnh Tự Động:
                </div>

                {/* Bước 1 */}
                <div className={cn(
                  "p-3 rounded-lg border transition-all space-y-1.5",
                  step1Done ? "bg-emerald-500/5 border-emerald-500/20 text-fg" : "bg-muted/30 border-line text-fg-muted"
                )}>
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2 font-semibold text-xs">
                      {step1Done ? (
                        <CheckCircle2 className="h-4 w-4 text-emerald-500 shrink-0" />
                      ) : (
                        <Clock className="h-4 w-4 text-fg-subtle shrink-0" />
                      )}
                      <span>Bước 1: Kịch bản & Biên kịch AI Showrunner</span>
                    </div>
                    <span className="text-[11px] font-mono font-medium text-emerald-500">100%</span>
                  </div>
                  <p className="text-[11px] text-fg-subtle pl-6">
                    Đã hoàn thành Story Bible, 4 cảnh, các câu thoại diễn xuất, mở đầu (Hook) và kết nút thắt (Cliffhanger).
                  </p>
                </div>

                {/* Bước 2 */}
                <div className={cn(
                  "p-3 rounded-lg border transition-all space-y-1.5",
                  step2Done ? "bg-emerald-500/5 border-emerald-500/20 text-fg" :
                  step2Active ? "bg-accent/5 border-accent/30 text-fg" : "bg-muted/30 border-line text-fg-muted"
                )}>
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2 font-semibold text-xs">
                      {step2Done ? (
                        <CheckCircle2 className="h-4 w-4 text-emerald-500 shrink-0" />
                      ) : step2Active ? (
                        <Loader2 className="h-4 w-4 animate-spin text-accent shrink-0" />
                      ) : (
                        <Clock className="h-4 w-4 text-fg-subtle shrink-0" />
                      )}
                      <span>Bước 2: Thu âm & Lồng tiếng nhân vật Việt Nam (EdgeTTS)</span>
                    </div>
                    <span className="text-[11px] font-mono font-medium text-emerald-500">
                      {step2Done ? '100%' : (step2Active ? 'Đang thu âm...' : '0%')}
                    </span>
                  </div>
                  <p className="text-[11px] text-fg-subtle pl-6">
                    {step2Done
                      ? 'Đã thu âm lồng tiếng xong toàn bộ câu thoại của từng nhân vật theo đúng ngữ điệu và giới tính.'
                      : step2Active ? 'Đang chuyển đổi văn bản sang giọng đọc tự nhiên (tiếng Sài Gòn, miền Tây, miền Bắc)...' : 'Chờ thực thi'}
                  </p>
                </div>

                {/* Bước 3 */}
                <div className={cn(
                  "p-3 rounded-lg border transition-all space-y-1.5",
                  step3Done ? "bg-emerald-500/5 border-emerald-500/20 text-fg" :
                  step3Active ? "bg-accent/5 border-accent/30 text-fg" : "bg-muted/30 border-line text-fg-muted"
                )}>
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2 font-semibold text-xs">
                      {step3Done ? (
                        <CheckCircle2 className="h-4 w-4 text-emerald-500 shrink-0" />
                      ) : step3Active ? (
                        <Loader2 className="h-4 w-4 animate-spin text-accent shrink-0" />
                      ) : (
                        <Clock className="h-4 w-4 text-fg-subtle shrink-0" />
                      )}
                      <span>Bước 3: Dựng khung hình nghệ thuật 4K (Stage 1: SDXL Base)</span>
                    </div>
                    <span className="text-[11px] font-mono font-medium text-emerald-500">
                      {step3Done ? '100% (Xong 16/16 shots)' : (imageMatch ? `${imageMatch[1]}/${imageMatch[2]} shots (${Math.round(parseInt(imageMatch[1])/parseInt(imageMatch[2])*100)}%)` : 'Chờ tới lượt')}
                    </span>
                  </div>
                  <div className="pl-6 space-y-1">
                    <p className="text-[11px] text-fg-subtle">
                      Khởi tạo ảnh bối cảnh siêu thực với Character Consistency Visual Prompts và Upscale 2x sắc nét.
                    </p>
                    {step3Active && imageMatch && (
                      <div className="h-1.5 w-full bg-line rounded-full overflow-hidden mt-1">
                        <div
                          className="h-full bg-accent rounded-full"
                          style={{ width: `${Math.round(parseInt(imageMatch[1])/parseInt(imageMatch[2])*100)}%` }}
                        />
                      </div>
                    )}
                  </div>
                </div>

                {/* Bước 4 */}
                <div className={cn(
                  "p-3 rounded-lg border transition-all space-y-1.5",
                  step4Done ? "bg-emerald-500/5 border-emerald-500/20 text-fg" :
                  step4Active ? "bg-accent/5 border-accent/30 text-fg" : "bg-muted/30 border-line text-fg-muted"
                )}>
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2 font-semibold text-xs">
                      {step4Done ? (
                        <CheckCircle2 className="h-4 w-4 text-emerald-500 shrink-0" />
                      ) : step4Active ? (
                        <Loader2 className="h-4 w-4 animate-spin text-accent shrink-0" />
                      ) : (
                        <Clock className="h-4 w-4 text-fg-subtle shrink-0" />
                      )}
                      <span>Bước 4: Tạo chuyển động điện ảnh diễn viên (Stage 2: Wan 2.1 14B)</span>
                    </div>
                    <span className="text-[11px] font-mono font-medium text-emerald-500">
                      {step4Done ? '100% (Hoàn thành)' : (motionMatch ? `Clip ${motionMatch[1]}/${motionMatch[2]} (${Math.round(parseInt(motionMatch[1])/parseInt(motionMatch[2])*100)}%)` : 'Chờ xong bước 3')}
                    </span>
                  </div>
                  <div className="pl-6 space-y-1">
                    <p className="text-[11px] text-fg-subtle">
                      Chạy trực tiếp trong 80GB VRAM trên A100 SXM (~60s/clip), xuất 81 khung hình mượt mà, chân thực như phim quay.
                    </p>
                    {step4Active && motionMatch && (
                      <div className="h-1.5 w-full bg-line rounded-full overflow-hidden mt-1">
                        <div
                          className="h-full bg-accent rounded-full transition-all duration-500"
                          style={{ width: `${Math.round(parseInt(motionMatch[1])/parseInt(motionMatch[2])*100)}%` }}
                        />
                      </div>
                    )}
                  </div>
                </div>

                {/* Bước 5 */}
                <div className={cn(
                  "p-3 rounded-lg border transition-all space-y-1.5",
                  step5Done ? "bg-emerald-500/5 border-emerald-500/20 text-fg" :
                  step5Active ? "bg-accent/5 border-accent/30 text-fg" : "bg-muted/30 border-line text-fg-muted"
                )}>
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2 font-semibold text-xs">
                      {step5Done ? (
                        <CheckCircle2 className="h-4 w-4 text-emerald-500 shrink-0" />
                      ) : step5Active ? (
                        <Loader2 className="h-4 w-4 animate-spin text-accent shrink-0" />
                      ) : (
                        <Clock className="h-4 w-4 text-fg-subtle shrink-0" />
                      )}
                      <span>Bước 5: Hậu kỳ, Ghép Audio & Chống Reup Bản Quyền</span>
                    </div>
                    <span className="text-[11px] font-mono font-medium text-emerald-500">
                      {step5Done ? '100%' : (step5Active ? 'Đang xuất MP4...' : 'Chờ hoàn tất')}
                    </span>
                  </div>
                  <p className="text-[11px] text-fg-subtle pl-6">
                    {step5Done
                      ? 'Video MP4 1080p đã xuất xưởng thành công! Đã hòa âm nhạc nền, phủ watermark @PageName và chèn siêu dữ liệu chống reup.'
                      : step5Active ? 'Đang hòa âm tiếng thoại + nhạc nền, phủ watermark @PageName và đóng gói MP4 1080p 30fps...' : 'Chờ xong bước 4'}
                  </p>
                </div>
              </div>

              {/* Live GPU Terminal Card */}
              <div className="rounded-lg bg-black/90 p-3.5 text-[11px] font-mono text-emerald-400 space-y-1.5 border border-line shadow-inner">
                <div className="flex items-center justify-between text-neutral-400 text-[10px] pb-1 border-b border-neutral-800">
                  <span className="flex items-center gap-1.5 font-sans font-semibold text-neutral-300">
                    <span className="w-2 h-2 rounded-full bg-emerald-500 animate-ping" />
                    Bảng Điều Khiển GPU Worker Thời Gian Thực
                  </span>
                  <span>Cập nhật liên tục 3-4s</span>
                </div>
                <div>[GPU] {gpuStatus?.gpu || 'NVIDIA A100-SXM4-80GB'} | VRAM: {gpuStatus?.vramGb || 80} GB</div>
                <div>[DISK] Dung lượng trống: {gpuStatus?.diskFreeGb || 203} GB / {gpuStatus?.diskTotalGb || 220} GB</div>
                <div>[BATCH] ID: {gpuStatus?.currentBatch || 'Không có'}</div>
                <div>[DETAIL] {currentDetail || (gpuStatus?.activeEpisodeStatus ? `Trạng thái: ${EPISODE_STATUS_LABELS[gpuStatus.activeEpisodeStatus] || gpuStatus.activeEpisodeStatus}` : 'Đang xử lý ngầm...')}</div>
              </div>

              {/* Modal Actions */}
              <div className="flex items-center justify-between pt-2 border-t border-line">
                <Button
                  size="sm"
                  variant="secondary"
                  icon={<RotateCw className="h-3.5 w-3.5" />}
                  onClick={loadGpuStatus}
                >
                  Làm mới dữ liệu
                </Button>
                <Button
                  size="sm"
                  variant="primary"
                  onClick={() => setTrackingEpisode(null)}
                >
                  Đóng
                </Button>
              </div>
            </div>
          </Modal>
        );
      })()}

      <VastMachinesPanel open={vastPanelOpen} onClose={() => setVastPanelOpen(false)} onChanged={loadGpuStatus} />
    </div>
  );
}

export default FilmFactoryPage;
