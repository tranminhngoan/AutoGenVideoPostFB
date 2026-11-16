import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  ArrowLeft,
  Check,
  CheckCircle2,
  XCircle,
  AlertTriangle,
  RefreshCw,
  Download,
  Trash2,
  Pencil,
  ExternalLink,
  MoreHorizontal,
  FileText,
  Film,
  Image as ImageIcon,
  Type,
  Send,
  Loader2,
  Sparkles,
  RotateCcw,
  Volume2,
  HardDrive,
  Facebook,
  AlertCircle,
  Circle,
} from 'lucide-react';
import {
  api,
  VideoScriptDto,
  VideoScenePlanDto,
  VideoAssetDto,
  VideoReviewDto,
  FacebookPostDto,
  FacebookAccountDto,
  EditorialPassDto,
} from '../services/api';
import { VideoProject } from '../types';
import {
  Alert,
  Badge,
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  EmptyState,
  Field,
  IconButton,
  Input,
  Menu,
  Modal,
  Segmented,
  Select,
  Skeleton,
  Tabs,
  Textarea,
  Thumb,
  cn,
} from '../components/ui';
import { StatusBadge } from '../components/VideoBits';
import { useConfirm, useToast } from '../components/feedback';
import { href, navigate } from '../lib/router';
import {
  MOTION,
  SCENE_PURPOSE,
  TRANSITION,
  domainOf,
  errorText,
  formatBytes,
  formatClock,
  formatDateTime,
  formatNumber,
  formatTime,
  isJobActive,
  isProcessing,
  statusMeta,
} from '../lib/format';
import { AI_PROVIDERS, AiProvider, SPEEDS, VOICES, durationLabel, getPrefs } from '../lib/prefs';

type TabId = 'script' | 'scenes' | 'qc' | 'audio' | 'publish' | 'files';

interface VideoDetailPageProps {
  projectId: string;
  onChanged?: () => void;
}

export const VideoDetailPage: React.FC<VideoDetailPageProps> = ({ projectId, onChanged }) => {
  const toast = useToast();
  const confirm = useConfirm();
  const prefs = useRef(getPrefs()).current;

  const [tab, setTab] = useState<TabId>('script');
  const [project, setProject] = useState<VideoProject | null>(null);
  const [script, setScript] = useState<VideoScriptDto | null>(null);
  const [scenes, setScenes] = useState<VideoScenePlanDto[]>([]);
  const [assets, setAssets] = useState<VideoAssetDto[]>([]);
  const [review, setReview] = useState<VideoReviewDto | null>(null);
  const [fbPosts, setFbPosts] = useState<FacebookPostDto[]>([]);
  const [pages, setPages] = useState<FacebookAccountDto[]>([]);

  const [loading, setLoading] = useState(true);
  const [generating, setGenerating] = useState(false);
  const [rendering, setRendering] = useState(false);
  const [evaluating, setEvaluating] = useState(false);
  const [approving, setApproving] = useState(false);
  const [retrying, setRetrying] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [metricsLoading, setMetricsLoading] = useState<string | null>(null);

  const [provider, setProvider] = useState<AiProvider>(prefs.aiProvider);
  const [voice, setVoice] = useState(prefs.voice);
  const [speed, setSpeed] = useState(prefs.speed);
  const choicesApplied = useRef(false);

  const [showReject, setShowReject] = useState(false);
  const [rejectNotes, setRejectNotes] = useState('');
  const [showEdit, setShowEdit] = useState(false);
  const [editTitle, setEditTitle] = useState('');
  const [editCaption, setEditCaption] = useState('');
  const [showPublish, setShowPublish] = useState(false);
  const [selectedPageId, setSelectedPageId] = useState('');
  const [publishCaption, setPublishCaption] = useState('');
  const [autoCleanup, setAutoCleanup] = useState(true);
  const [firstComment, setFirstComment] = useState('');
  const [editorial, setEditorial] = useState<EditorialPassDto | null>(null);
  const [checking, setChecking] = useState(false);

  const loadData = useCallback(
    async (silent = false) => {
      if (!silent) setLoading(true);
      try {
        const [projData, scriptData, scenesData, assetsData, reviewData, postsData, pagesData] = await Promise.all([
          api.getVideoProjectById(projectId),
          api.getScript(projectId),
          api.getScenes(projectId),
          api.getAssets(projectId),
          api.getReview(projectId),
          api.getProjectFacebookPosts(projectId),
          api.getFacebookPages(),
        ]);
        setProject(projData);
        // Start from the AI/voice/speed chosen when this video was created
        if (projData && !choicesApplied.current) {
          choicesApplied.current = true;
          if (projData.aiProvider === 'Gemini' || projData.aiProvider === 'Claude') setProvider(projData.aiProvider);
          if (projData.voice) setVoice(projData.voice);
          if (projData.voiceSpeed) setSpeed(projData.voiceSpeed);
        }
        setScript(scriptData);
        setScenes(scenesData);
        setAssets(assetsData);
        setReview(reviewData);
        setFbPosts(postsData);
        setPages(pagesData);
        // Default to the page the video was made for (its name is on the video)
        const madeFor = pagesData.find((pg) => pg.id === projData?.facebookAccountId);
        setSelectedPageId((cur) => cur || madeFor?.pageId || pagesData[0]?.pageId || '');
      } catch (err) {
        if (!silent) toast.error('Không tải được dữ liệu video', errorText(err, ''));
      } finally {
        if (!silent) setLoading(false);
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [projectId]
  );

  useEffect(() => {
    loadData();
  }, [loadData]);

  // A processing status only counts if something is really running (a worker job, or a request this
  // page started). Otherwise the status is stale and we must not spin forever.
  const jobs = [...(project?.jobs ?? [])].sort((a, b) => (b.startedAtUtc ?? '').localeCompare(a.startedAtUtc ?? ''));
  const latestJob = jobs[0];
  const hasActiveJob = jobs.some((j) => isJobActive(j.status) && j.hangfireJobId);
  const processing = project
    ? isProcessing(project.status) && (hasActiveJob || generating || rendering)
    : false;
  const failureMessage =
    project?.status === 'Failed'
      ? project.errorMessage || latestJob?.errorMessage || 'Không rõ nguyên nhân.'
      : !processing && latestJob?.status === 'Failed'
      ? latestJob.errorMessage || project?.errorMessage || 'Không rõ nguyên nhân.'
      : null;

  // Keep polling while the worker pipeline is running.
  useEffect(() => {
    if (!processing) return;
    const t = setInterval(() => loadData(true), 5000);
    return () => clearInterval(t);
  }, [processing, loadData]);

  /* ─────────────── Derived state ─────────────── */

  const latestVideoAsset = assets.find((a) => (a.assetType === 'FinalVideo' || a.assetType === 'Video') && !a.isCleanedUp);
  const latestAudioAsset = assets.find((a) => a.assetType === 'Audio' && !a.isCleanedUp);
  const hasVideo = !!latestVideoAsset;
  const hasAudio = !!latestAudioAsset;
  const videoUrl = latestVideoAsset
    ? `/api/video-projects/${projectId}/video?v=${latestVideoAsset.id}`
    : `/api/video-projects/${projectId}/video`;
  const audioUrl = latestAudioAsset
    ? `/api/video-projects/${projectId}/audio?v=${latestAudioAsset.id}`
    : `/api/video-projects/${projectId}/audio`;
  const isApproved = review?.decision === 'Approved';
  const isRejected = review?.decision === 'Rejected';
  const hasFatal = review?.seriousViolationsFound || (review?.assessment?.hasFatalViolations ?? false);
  const publishedPost = fbPosts.find((p) => p.uploadStatus === 'Published');
  const totalSeconds = scenes.length > 0 ? scenes[scenes.length - 1].endTimeSeconds : 0;
  const poster = scenes.find((s) => s.imageUrl)?.imageUrl ?? undefined;
  const score = review?.internalQualityScore;

  /* ─────────────── Actions ─────────────── */

  const runEvaluation = async () => {
    setEvaluating(true);
    try {
      setReview(await api.evaluateQuality(projectId));
    } catch (err) {
      console.warn('Evaluation failed:', err);
    } finally {
      setEvaluating(false);
    }
  };

  const generateScript = async () => {
    setGenerating(true);
    try {
      const s = await api.generateScript(projectId, { aiProvider: provider });
      setScript(s);
      setScenes(await api.getScenes(projectId));
      setTab('script');
      toast.success('Đã viết xong kịch bản', 'Kịch bản đã được chia thành các cảnh.');
      await runEvaluation();
      onChanged?.();
    } catch (err) {
      toast.error('Không tạo được kịch bản', errorText(err, ''));
    } finally {
      setGenerating(false);
    }
  };

  const planScenes = async () => {
    setGenerating(true);
    try {
      setScenes(await api.planScenes(projectId));
      setTab('scenes');
      toast.success('Đã chia lại cảnh');
      await runEvaluation();
    } catch (err) {
      toast.error('Không chia được cảnh', errorText(err, ''));
    } finally {
      setGenerating(false);
    }
  };

  const renderVideo = async () => {
    setRendering(true);
    try {
      await api.renderVideo(projectId, 'EdgeTts', voice, speed);
      toast.success('Đã dựng xong video', 'Bạn có thể xem lại ngay ở khung bên trái.');
      await loadData(true);
      await runEvaluation();
      onChanged?.();
    } catch (err) {
      toast.error('Dựng video thất bại', errorText(err, ''));
    } finally {
      setRendering(false);
    }
  };

  const approve = async () => {
    setApproving(true);
    try {
      setReview(await api.approveReview(projectId, 'Editor', 'Đạt chuẩn kiểm duyệt biên tập nội bộ.'));
      toast.success('Đã duyệt video', 'Video sẵn sàng để đăng lên Facebook.');
      onChanged?.();
    } catch (err) {
      toast.error('Chưa duyệt được video', errorText(err, 'Video còn lỗi nghiêm trọng.'));
    } finally {
      setApproving(false);
    }
  };

  const reject = async () => {
    if (!rejectNotes.trim()) return;
    try {
      setReview(await api.rejectReview(projectId, 'Editor', rejectNotes.trim()));
      setShowReject(false);
      setRejectNotes('');
      toast.success('Đã ghi nhận từ chối');
      onChanged?.();
    } catch (err) {
      toast.error('Không gửi được từ chối', errorText(err, ''));
    }
  };

  const requestAction = async (action: 'RegenerateScript' | 'RegenerateAudio' | 'ReRender') => {
    setActionLoading(action);
    try {
      await api.requestReviewAction(projectId, action);
      if (action === 'RegenerateScript') await generateScript();
      else await renderVideo();
    } catch (err) {
      toast.error('Không thực hiện được yêu cầu', errorText(err, ''));
    } finally {
      setActionLoading(null);
    }
  };

  const openEdit = () => {
    setEditTitle(project?.title ?? '');
    setEditCaption(scenes[0]?.onScreenText ?? '');
    setShowEdit(true);
  };

  const saveMetadata = async () => {
    if (!editTitle.trim()) return;
    try {
      await api.updateVideoMetadata(projectId, { title: editTitle.trim(), caption: editCaption });
      setShowEdit(false);
      toast.success('Đã lưu tiêu đề');
      await loadData(true);
      onChanged?.();
    } catch (err) {
      toast.error('Không lưu được', errorText(err, ''));
    }
  };

  const openPublish = () => {
    const parts = [project?.title, script?.hook, script?.sourceAttribution, prefs.hashtags].filter(Boolean);
    setPublishCaption(parts.join('\n\n'));
    setFirstComment('');
    setEditorial(null);
    setShowPublish(true);
  };

  // AI: check headline/text against the article, write a professional caption and the first comment
  const runEditorial = async () => {
    setChecking(true);
    try {
      const pass = await api.editorialPass(projectId);
      setEditorial(pass);
      setPublishCaption(pass.caption);
      setFirstComment(pass.firstComment);
    } catch (err) {
      toast.error('AI chưa kiểm tra được', errorText(err, ''));
    } finally {
      setChecking(false);
    }
  };

  const publish = async () => {
    setPublishing(true);
    try {
      const result = await api.publishReel(projectId, {
        pageId: selectedPageId || undefined,
        customCaption: publishCaption,
        autoCleanupAssets: autoCleanup,
        firstComment: firstComment.trim() || undefined,
      });
      setShowPublish(false);
      toast.success('Đã đăng lên Facebook', result.firstCommentId ? 'Đã bình luận đầu tiên.' : `Mã video: ${result.facebookVideoId}`);
      if (result.firstCommentError) toast.error('Chưa bình luận được', result.firstCommentError);
      await loadData(true);
      setTab('publish');
      onChanged?.();
    } catch (err) {
      toast.error('Đăng Facebook thất bại', errorText(err, ''));
    } finally {
      setPublishing(false);
    }
  };

  const refreshMetrics = async (postId: string) => {
    setMetricsLoading(postId);
    try {
      await api.fetchPostMetrics(postId);
      toast.success('Đã cập nhật số liệu');
      await loadData(true);
    } catch (err) {
      toast.error('Không cập nhật được số liệu', errorText(err, ''));
    } finally {
      setMetricsLoading(null);
    }
  };

  const retryPipeline = async () => {
    setRetrying(true);
    try {
      await api.processVideoProject(projectId);
      toast.success('Đã chạy lại', 'Hệ thống đang xử lý video từ đầu.');
      await loadData(true);
      onChanged?.();
    } catch (err) {
      toast.error('Không chạy lại được', errorText(err, ''));
    } finally {
      setRetrying(false);
    }
  };

  const deleteProject = async () => {
    const ok = await confirm({
      title: 'Xoá video này?',
      description: 'Video, kịch bản và toàn bộ file tạm sẽ bị xoá vĩnh viễn. Không thể hoàn tác.',
      confirmLabel: 'Xoá video',
      danger: true,
    });
    if (!ok) return;
    const done = await api.deleteVideoProject(projectId);
    if (done) {
      toast.success('Đã xoá video');
      onChanged?.();
      navigate('/videos');
    } else {
      toast.error('Không xoá được video');
    }
  };

  const cleanupAssets = async () => {
    const ok = await confirm({
      title: 'Xoá file tạm?',
      description: 'File MP4, âm thanh và phụ đề của video này sẽ bị xoá khỏi máy chủ. Bạn có thể dựng lại sau.',
      confirmLabel: 'Xoá file tạm',
      danger: true,
    });
    if (!ok) return;
    const done = await api.cleanupAssets(projectId);
    if (done) {
      toast.success('Đã xoá file tạm');
      await loadData(true);
    } else {
      toast.error('Không xoá được file tạm');
    }
  };

  /* ─────────────── Render ─────────────── */

  if (loading && !project) return <DetailSkeleton />;

  if (!project) {
    return (
      <Card>
        <EmptyState
          icon={<Film />}
          title="Không tìm thấy video"
          description="Video có thể đã bị xoá hoặc đường dẫn không đúng."
          action={
            <ButtonLink href={href('/videos')} icon={<ArrowLeft className="h-4 w-4" />}>
              Về danh sách video
            </ButtonLink>
          }
        />
      </Card>
    );
  }

  const steps = [
    { label: 'Kịch bản', done: !!script },
    { label: 'Phân cảnh', done: scenes.length > 0 },
    { label: 'Dựng video', done: hasVideo },
    { label: 'Duyệt', done: isApproved || !!publishedPost },
    { label: 'Đăng', done: !!publishedPost },
  ];
  const currentStep = steps.findIndex((s) => !s.done);

  const primaryAction = (() => {
    if (processing) return null;
    if (!script)
      return (
        <Button variant="primary" icon={<Sparkles className="h-4 w-4" />} loading={generating} onClick={generateScript}>
          Viết kịch bản
        </Button>
      );
    if (!hasVideo && !publishedPost)
      return (
        <Button variant="primary" icon={<Film className="h-4 w-4" />} loading={rendering} onClick={renderVideo}>
          Dựng video
        </Button>
      );
    if (publishedPost)
      return publishedPost.postUrl ? (
        <ButtonLink
          variant="primary"
          href={publishedPost.postUrl}
          target="_blank"
          rel="noreferrer"
          icon={<ExternalLink className="h-4 w-4" />}
        >
          Xem trên Facebook
        </ButtonLink>
      ) : null;
    if (isApproved)
      return (
        <Button variant="primary" icon={<Send className="h-4 w-4" />} onClick={openPublish} disabled={hasFatal}>
          Đăng lên Facebook
        </Button>
      );
    return (
      <>
        <Button onClick={() => setShowReject(true)}>Từ chối</Button>
        <Button
          variant="primary"
          icon={<Check className="h-4 w-4" />}
          loading={approving}
          onClick={approve}
          disabled={hasFatal}
          title={hasFatal ? 'Video còn lỗi nghiêm trọng, cần sửa trước khi duyệt' : undefined}
        >
          Duyệt video
        </Button>
      </>
    );
  })();

  const otherPosts = fbPosts.filter((p) => p !== publishedPost);

  return (
    <div>
      {/* Header */}
      <a href={href('/videos')} className="mb-3 inline-flex items-center gap-1.5 text-[13px] text-fg-muted hover:text-fg">
        <ArrowLeft className="h-3.5 w-3.5" />
        Video
      </a>

      <div className="mb-5 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0">
          <h1 className="text-xl font-semibold leading-snug text-fg sm:text-[22px]">{project.title}</h1>
          <div className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1 text-[13px] text-fg-muted">
            <StatusBadge status={publishedPost ? 'Published' : project.status} />
            {isRejected && !publishedPost && <Badge tone="danger">Đã từ chối</Badge>}
            <span className="text-fg-subtle">·</span>
            <a
              href={project.sourceUrl}
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center gap-1 hover:text-fg"
            >
              {project.publisher || domainOf(project.sourceUrl)}
              <ExternalLink className="h-3 w-3" />
            </a>
            <span className="text-fg-subtle">·</span>
            <span>Tạo lúc {formatDateTime(project.createdAtUtc)}</span>
          </div>
        </div>

        <div className="flex shrink-0 items-center gap-2">
          <Menu
            trigger={
              <IconButton label="Thao tác khác" className="border border-line-strong bg-surface shadow-card">
                <MoreHorizontal className="h-4 w-4" />
              </IconButton>
            }
            items={[
              { label: 'Sửa tiêu đề', icon: <Pencil />, onSelect: openEdit },
              ...(hasVideo
                ? [{ label: 'Tải MP4', icon: <Download />, href: videoUrl, download: `reel_${projectId.slice(0, 8)}.mp4` }]
                : []),
              { label: 'Mở bài báo gốc', icon: <ExternalLink />, href: project.sourceUrl, external: true },
              { label: 'Làm mới dữ liệu', icon: <RefreshCw />, onSelect: () => loadData() },
              { label: '', divider: true },
              { label: 'Xoá file tạm', icon: <HardDrive />, onSelect: cleanupAssets },
              { label: 'Xoá video', icon: <Trash2 />, onSelect: deleteProject, danger: true },
            ]}
          />
          {primaryAction}
        </div>
      </div>

      {/* Stepper */}
      <ol className="mb-6 grid grid-cols-5 gap-2">
        {steps.map((s, i) => {
          const current = i === currentStep;
          return (
            <li key={s.label}>
              <div className={cn('h-1 rounded-full', s.done ? 'bg-success' : current ? 'bg-fg' : 'bg-line')} />
              <div
                className={cn(
                  'mt-2 hidden items-center gap-1.5 text-xs sm:flex',
                  s.done ? 'text-fg-muted' : current ? 'font-medium text-fg' : 'text-fg-subtle'
                )}
              >
                {s.done ? (
                  <Check className="h-3.5 w-3.5 shrink-0 text-success" />
                ) : (
                  <span className="hidden tabular sm:inline">{i + 1}.</span>
                )}
                <span className="truncate">{s.label}</span>
              </div>
            </li>
          );
        })}
      </ol>
      {/* Phones: one readable line instead of five squeezed labels */}
      <p className="-mt-4 mb-5 text-xs text-fg-muted sm:hidden">
        {currentStep === -1 ? (
          <span className="font-medium text-success">Đã hoàn tất cả 5 bước</span>
        ) : (
          <>
            Bước {currentStep + 1}/5 · <span className="font-medium text-fg">{steps[currentStep].label}</span>
          </>
        )}
      </p>

      {/* Banners */}
      <div className="mb-6 space-y-3 empty:hidden">
        {processing && (
          <Alert tone="info" icon={<Loader2 className="animate-spin" />} title={`${statusMeta(project.status).label}…`}>
            Hệ thống đang xử lý video. Trang sẽ tự cập nhật khi có kết quả.
          </Alert>
        )}
        {failureMessage && (
          <Alert
            tone="danger"
            icon={<AlertCircle />}
            title="Xử lý thất bại"
            action={
              <div className="flex gap-2">
                {/API key/i.test(failureMessage) && (
                  <ButtonLink size="sm" variant="ghost" href={href('/settings')}>
                    Mở Cài đặt
                  </ButtonLink>
                )}
                <Button size="sm" icon={<RotateCcw className="h-3.5 w-3.5" />} loading={retrying} onClick={retryPipeline}>
                  Chạy lại
                </Button>
              </div>
            }
          >
            {failureMessage}
          </Alert>
        )}
        {/* Quality-check verdicts only make sense once a video exists; before that the real
            blocker is shown above (or the video simply isn't finished yet). */}
        {hasVideo && hasFatal && !!review?.assessment?.fatalViolations?.length && (
          <Alert
            tone="danger"
            icon={<AlertTriangle />}
            title="Video còn lỗi nghiêm trọng, chưa thể duyệt"
            action={
              tab !== 'qc' && (
                <Button size="sm" variant="ghost" onClick={() => setTab('qc')}>
                  Xem kiểm định
                </Button>
              )
            }
          >
            <ul className="mt-1 list-disc space-y-0.5 pl-4">
              {review.assessment.fatalViolations.map((v, i) => (
                <li key={i}>{v}</li>
              ))}
            </ul>
          </Alert>
        )}
        {isRejected && review?.feedbackNotes && !publishedPost && (
          <Alert tone="warning" icon={<XCircle />} title="Lý do từ chối">
            {review.feedbackNotes}
          </Alert>
        )}
      </div>

      <div className="grid gap-8 lg:grid-cols-[300px_minmax(0,1fr)]">
        {/* Left: preview + render settings */}
        <div className="space-y-4 lg:sticky lg:top-8 lg:self-start">
          <div className="mx-auto w-full max-w-[300px]">
            <div className="relative aspect-[9/16] overflow-hidden rounded-2xl bg-black ring-1 ring-line">
              {hasVideo ? (
                <video
                  key={latestVideoAsset?.id || projectId}
                  src={videoUrl}
                  controls
                  playsInline
                  className="h-full w-full object-cover"
                  poster={poster}
                />
                /* Rule: videos never show running subtitles (no <track>), only the voice-over. */
              ) : (
                <>
                  {poster && (
                    <img
                      src={poster}
                      alt=""
                      referrerPolicy="no-referrer"
                      className="absolute inset-0 h-full w-full object-cover opacity-30 blur-[1px]"
                    />
                  )}
                  <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-black/40 p-6 text-center">
                    {rendering ? (
                      <>
                        <Loader2 className="h-6 w-6 animate-spin text-white/80" />
                        <p className="text-sm text-white/90">Đang dựng video…</p>
                      </>
                    ) : (
                      <>
                        <Film className="h-6 w-6 text-white/60" />
                        <p className="text-sm text-white/90">{publishedPost ? 'File video đã được dọn' : 'Chưa có video'}</p>
                        <p className="text-xs text-white/60">
                          {publishedPost
                            ? 'Video đã đăng, file tạm đã xoá khỏi máy chủ.'
                            : script
                            ? 'Bấm “Dựng video” để tạo bản xem trước.'
                            : 'Cần có kịch bản trước khi dựng.'}
                        </p>
                      </>
                    )}
                  </div>
                </>
              )}
            </div>
          </div>

          <Card>
            <CardBody className="space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-[13px] font-medium text-fg">Giọng đọc</span>
                {hasVideo && project.actualDurationSeconds ? (
                  <span className="text-xs tabular text-fg-subtle">{formatClock(project.actualDurationSeconds)}</span>
                ) : null}
              </div>
              <Select value={voice} onChange={(e) => setVoice(e.target.value)} size="sm" aria-label="Giọng đọc">
                <optgroup label="Giọng tự nhiên (Gemini)">
                  {VOICES.filter((v) => v.group === 'natural').map((v) => (
                    <option key={v.id} value={v.id}>
                      {v.name} — {v.desc.split(',')[0]}
                    </option>
                  ))}
                </optgroup>
                <optgroup label="Giọng cơ bản (Edge)">
                  {VOICES.filter((v) => v.group === 'basic').map((v) => (
                    <option key={v.id} value={v.id}>
                      {v.name}
                    </option>
                  ))}
                </optgroup>
                {!VOICES.some((v) => v.id === voice) && <option value={voice}>{voice}</option>}
              </Select>
              <Segmented
                size="sm"
                className="flex w-full [&>button]:flex-1"
                value={speed}
                onChange={setSpeed}
                options={SPEEDS.map((s) => ({ value: s.id, label: s.label }))}
              />
              <Button
                className="w-full"
                variant={hasVideo || !script ? 'secondary' : 'primary'}
                icon={<Film className="h-4 w-4" />}
                loading={rendering}
                disabled={!script || processing}
                onClick={renderVideo}
              >
                {rendering ? 'Đang dựng…' : hasVideo ? 'Dựng lại video' : 'Dựng video'}
              </Button>
              {hasVideo && (
                <ButtonLink
                  href={videoUrl}
                  download={`reel_${projectId.slice(0, 8)}.mp4`}
                  variant="ghost"
                  size="sm"
                  className="w-full"
                  icon={<Download className="h-4 w-4" />}
                >
                  Tải MP4
                </ButtonLink>
              )}
            </CardBody>
          </Card>
        </div>

        {/* Right: tabs */}
        <div className="min-w-0">
          <Tabs
            className="mb-5"
            value={tab}
            onChange={setTab}
            items={[
              { id: 'script', label: 'Kịch bản' },
              { id: 'scenes', label: 'Phân cảnh', count: scenes.length || undefined },
              { id: 'qc', label: 'Kiểm định', count: score },
              { id: 'audio', label: 'Giọng đọc' },
              { id: 'publish', label: 'Đăng Facebook' },
              { id: 'files', label: 'File tạm', count: assets.length || undefined },
            ]}
          />

          {/* SCRIPT */}
          {tab === 'script' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="flex items-center gap-2 text-[13px] text-fg-muted">
                  <span>Viết bằng</span>
                  <Segmented
                    size="sm"
                    value={provider}
                    onChange={setProvider}
                    options={AI_PROVIDERS.map((p) => ({ value: p.id, label: p.name }))}
                  />
                </div>
                <Button
                  size="sm"
                  icon={<Sparkles className="h-3.5 w-3.5" />}
                  loading={generating}
                  disabled={processing}
                  onClick={generateScript}
                >
                  {script ? 'Viết lại' : 'Viết kịch bản'}
                </Button>
              </div>

              {script ? (
                <>
                  <Card>
                    <CardBody className="space-y-4">
                      <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-fg-subtle">
                        <span>{script.wordCount} từ</span>
                        <span>·</span>
                        <span>khoảng {script.estimatedReadingSeconds} giây đọc</span>
                        <span>·</span>
                        <span>
                          {script.aiProvider}
                          {script.modelName && ` (${script.modelName})`}
                        </span>
                      </div>
                      <h3 className="text-lg font-semibold leading-snug text-fg">{script.title}</h3>
                      {script.hook && (
                        <div className="border-l-2 border-fg pl-4">
                          <div className="mb-0.5 text-xs font-medium text-fg-muted">Câu mở đầu</div>
                          <p className="text-[15px] leading-relaxed text-fg">{script.hook}</p>
                        </div>
                      )}
                    </CardBody>
                  </Card>

                  <Card>
                    <CardHeader title="Lời đọc" description="Toàn bộ nội dung sẽ được đọc trong video." />
                    <CardBody>
                      <p className="max-w-prose whitespace-pre-wrap text-[15px] leading-7 text-fg">{script.voiceOverScript}</p>
                      {script.sourceAttribution && (
                        <p className="mt-4 border-t border-line pt-3 text-xs text-fg-subtle">{script.sourceAttribution}</p>
                      )}
                    </CardBody>
                  </Card>

                  <div className="grid gap-4 md:grid-cols-2">
                    <ScriptBlock title="Dữ kiện chính" body={script.keyFacts} />
                    <ScriptBlock title="Bối cảnh" body={script.context} />
                    <ScriptBlock title="Phân tích" body={script.analysis} note="Phần bình luận của biên tập, không phải dữ kiện." />
                    <ScriptBlock title="Kết luận" body={script.conclusion} />
                  </div>
                </>
              ) : (
                <Card>
                  <EmptyState
                    icon={<FileText />}
                    title={processing ? 'Kịch bản đang được viết' : 'Chưa có kịch bản'}
                    description={
                      processing
                        ? 'Hệ thống sẽ hiển thị kịch bản ngay khi xong.'
                        : 'AI sẽ đọc bài báo và viết lời dẫn khoảng 60 giây, tách rõ dữ kiện và bình luận.'
                    }
                    action={
                      !processing && (
                        <Button variant="primary" icon={<Sparkles className="h-4 w-4" />} loading={generating} onClick={generateScript}>
                          Viết kịch bản
                        </Button>
                      )
                    }
                  />
                </Card>
              )}
            </div>
          )}

          {/* SCENES */}
          {tab === 'scenes' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <p className="text-[13px] text-fg-muted">
                  {scenes.length > 0 ? (
                    <>
                      {scenes.length} cảnh · tổng {formatClock(totalSeconds)}
                      {project.targetDurationSeconds ? (
                        <span className="text-fg-subtle"> (tối đa {durationLabel(project.targetDurationSeconds)})</span>
                      ) : null}
                    </>
                  ) : (
                    'Chưa có phân cảnh'
                  )}
                </p>
                <Button
                  size="sm"
                  icon={<RefreshCw className="h-3.5 w-3.5" />}
                  loading={generating}
                  disabled={!script || processing}
                  onClick={planScenes}
                >
                  Chia cảnh lại
                </Button>
              </div>

              {scenes.length > 0 && scenes.every((s) => !s.imageUrl) && (
                <Alert tone="warning" icon={<AlertTriangle />} title="Cảnh chưa có ảnh">
                  Khi dựng video, hệ thống sẽ tải ảnh trong bài; nếu ảnh bị chặn sẽ tự tìm ảnh minh hoạ liên quan
                  (giấy phép Creative Commons). Video không bao giờ được dựng với nền trống.
                </Alert>
              )}
              {scenes.some((s) => s.imageSource === 'WebSearch') && (
                <p className="text-xs text-fg-subtle">
                  Ảnh gốc của bài không tải được nên một số cảnh dùng ảnh minh hoạ liên quan (Creative Commons, cho phép dùng
                  thương mại). Tác giả và giấy phép xem trong mục Bài viết.
                </p>
              )}
              {scenes.some((s) => s.isImageReused) && (
                <p className="text-xs text-fg-subtle">
                  Một số ảnh được dùng lại ở nhiều cảnh với góc cắt và chuyển động khác nhau để đủ thời lượng.
                </p>
              )}

              {scenes.length === 0 ? (
                <Card>
                  <EmptyState
                    icon={<Film />}
                    title="Chưa có phân cảnh"
                    description={script ? 'Chia kịch bản thành các cảnh, ghép với ảnh trong bài.' : 'Cần viết kịch bản trước.'}
                    action={
                      script && (
                        <Button variant="primary" loading={generating} onClick={planScenes}>
                          Chia cảnh
                        </Button>
                      )
                    }
                  />
                </Card>
              ) : (
                <Card className="divide-y divide-line">
                  {scenes.map((s) => (
                    <div key={s.sceneNumber} className="flex gap-4 p-4">
                      <Thumb
                        src={s.imageUrl}
                        className="aspect-[9/16] w-16 shrink-0 rounded-md border border-line"
                        fallback={
                          <span className="px-1 text-center text-[9px] font-semibold uppercase leading-tight text-fg-subtle">
                            {(s.headline || s.onScreenText || '').slice(0, 40)}
                          </span>
                        }
                      />
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-fg-muted">
                          <span className="font-medium text-fg">Cảnh {s.sceneNumber}</span>
                          <span className="tabular">
                            {formatClock(s.startTimeSeconds)}–{formatClock(s.endTimeSeconds)}
                          </span>
                          <span className="text-fg-subtle">·</span>
                          <span>{SCENE_PURPOSE[s.scenePurpose] ?? s.scenePurpose}</span>
                          {s.visualType === 'Image' && s.imageSource === 'WebSearch' ? (
                            <Badge tone="info" className="ml-1">
                              <ImageIcon className="h-3 w-3" /> Ảnh minh hoạ
                            </Badge>
                          ) : s.visualType === 'Image' ? (
                            <Badge tone="success" className="ml-1">
                              <ImageIcon className="h-3 w-3" /> Ảnh bài viết
                            </Badge>
                          ) : (
                            <Badge tone="warning" className="ml-1">
                              <Type className="h-3 w-3" /> Nền chữ
                            </Badge>
                          )}
                          {s.isImageReused && <Badge>Dùng lại ảnh</Badge>}
                        </div>
                        {(s.headline || s.onScreenText) && (
                          <p className="mt-1.5 text-sm font-medium text-fg">{s.headline || s.onScreenText}</p>
                        )}
                        <p className="mt-1 text-[13px] leading-relaxed text-fg-muted">{s.voiceOver || s.voiceText}</p>
                        <p className="mt-2 text-xs text-fg-subtle">
                          {MOTION[s.motionType] ?? s.motionType} · {TRANSITION[s.transitionType] ?? s.transitionType}
                          {s.cropType && ` · Cắt ${s.cropType}`}
                          {s.imageCaption && <span title={s.imageCaption}> · {s.imageCaption}</span>}
                        </p>
                      </div>
                    </div>
                  ))}
                </Card>
              )}
            </div>
          )}

          {/* QC */}
          {tab === 'qc' && (
            <div className="space-y-4">
              <Card>
                <CardBody className="flex flex-col gap-4 sm:flex-row sm:items-center">
                  <div className="flex items-baseline gap-1">
                    <span
                      className={cn(
                        'text-4xl font-semibold tabular',
                        score === undefined
                          ? 'text-fg-subtle'
                          : score >= 85
                          ? 'text-success'
                          : score >= 70
                          ? 'text-warning'
                          : 'text-danger'
                      )}
                    >
                      {score ?? '—'}
                    </span>
                    <span className="text-sm text-fg-subtle">/100</span>
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="text-sm text-fg">
                      {review?.assessment?.summary || 'Rà soát nội dung, dữ kiện, quyền sử dụng ảnh và kỹ thuật video.'}
                    </p>
                    {!!review?.assessment?.checkItems?.length && (
                      <p className="mt-0.5 text-xs text-fg-muted">
                        {review.assessment.checkItems.filter((c) => c.passed).length}/{review.assessment.checkItems.length} tiêu chí đạt
                      </p>
                    )}
                  </div>
                  <Button size="sm" icon={<RefreshCw className="h-3.5 w-3.5" />} loading={evaluating} onClick={runEvaluation}>
                    Kiểm định lại
                  </Button>
                </CardBody>
              </Card>

              <Card className="divide-y divide-line">
                {review?.assessment?.checkItems?.length ? (
                  review.assessment.checkItems.map((item, idx) => {
                    const failFatal = item.isFatal && !item.passed;
                    return (
                      <div key={idx} className="flex gap-3 px-4 py-3.5">
                        <span className="mt-0.5 shrink-0">
                          {item.passed ? (
                            <CheckCircle2 className="h-4 w-4 text-success" />
                          ) : failFatal ? (
                            <XCircle className="h-4 w-4 text-danger" />
                          ) : (
                            <AlertTriangle className="h-4 w-4 text-warning" />
                          )}
                        </span>
                        <div className="min-w-0 flex-1">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="text-sm font-medium text-fg">{item.title}</span>
                            {item.isFatal && <Badge tone={failFatal ? 'danger' : 'neutral'}>Bắt buộc</Badge>}
                          </div>
                          <p className="mt-0.5 text-[13px] text-fg-muted">{item.details}</p>
                          {item.recommendation && !item.passed && (
                            <p className="mt-1 text-xs text-fg-muted">
                              <span className="font-medium text-fg">Gợi ý:</span> {item.recommendation}
                            </p>
                          )}
                        </div>
                        <span className="shrink-0 text-xs tabular text-fg-subtle">{item.score}/10</span>
                      </div>
                    );
                  })
                ) : (
                  <EmptyState
                    title="Chưa có kết quả kiểm định"
                    description="Bấm “Kiểm định lại” để rà soát nội dung, dữ kiện, quyền ảnh và kỹ thuật video."
                    className="py-10"
                  />
                )}
              </Card>

              {review?.assessment?.notice && <p className="px-1 text-xs text-fg-subtle">{review.assessment.notice}</p>}

              <Card>
                <CardHeader title="Quyết định duyệt" description={decisionText(review?.decision, !!publishedPost)} />
                <CardBody className="flex flex-wrap items-center gap-2">
                  {!publishedPost && (
                    <>
                      <Button
                        variant="primary"
                        size="sm"
                        icon={<Check className="h-3.5 w-3.5" />}
                        loading={approving}
                        disabled={hasFatal || !hasVideo || isApproved}
                        title={!hasVideo ? 'Cần dựng video trước khi duyệt' : hasFatal ? 'Còn lỗi nghiêm trọng' : undefined}
                        onClick={approve}
                      >
                        {isApproved ? 'Đã duyệt' : 'Duyệt video'}
                      </Button>
                      <Button size="sm" onClick={() => setShowReject(true)}>
                        Từ chối
                      </Button>
                      <span className="mx-1 hidden h-5 w-px bg-line sm:block" />
                    </>
                  )}
                  <Button
                    size="sm"
                    variant="ghost"
                    icon={<RotateCcw className="h-3.5 w-3.5" />}
                    loading={actionLoading === 'RegenerateScript'}
                    disabled={actionLoading !== null}
                    onClick={() => requestAction('RegenerateScript')}
                  >
                    Viết lại kịch bản
                  </Button>
                  <Button
                    size="sm"
                    variant="ghost"
                    icon={<Volume2 className="h-3.5 w-3.5" />}
                    loading={actionLoading === 'RegenerateAudio'}
                    disabled={actionLoading !== null || !script}
                    title={!script ? 'Cần có kịch bản trước' : undefined}
                    onClick={() => requestAction('RegenerateAudio')}
                  >
                    Tạo lại giọng đọc
                  </Button>
                  <Button
                    size="sm"
                    variant="ghost"
                    icon={<Film className="h-3.5 w-3.5" />}
                    loading={actionLoading === 'ReRender'}
                    disabled={actionLoading !== null || !script}
                    title={!script ? 'Cần có kịch bản trước' : undefined}
                    onClick={() => requestAction('ReRender')}
                  >
                    Dựng lại video
                  </Button>
                </CardBody>
              </Card>
            </div>
          )}

          {/* AUDIO */}
          {tab === 'audio' && (
            <div className="space-y-4">
              <Card>
                <CardHeader
                  title="Giọng đọc"
                  description={hasAudio ? 'Bản thu đã dùng trong video hiện tại.' : undefined}
                  actions={
                    hasAudio && (
                      <ButtonLink href={audioUrl} download="voiceover.wav" size="sm" variant="ghost" icon={<Download className="h-3.5 w-3.5" />}>
                        WAV
                      </ButtonLink>
                    )
                  }
                />
                <CardBody>
                  {hasAudio ? (
                    <audio key={latestAudioAsset?.id || projectId} src={audioUrl} controls className="h-10 w-full" />
                  ) : (
                    <p className="text-[13px] text-fg-muted">Chưa có giọng đọc. Giọng đọc được tạo cùng lúc khi dựng video.</p>
                  )}
                </CardBody>
              </Card>
            </div>
          )}

          {/* PUBLISH */}
          {tab === 'publish' && (
            <div className="space-y-4">
              {publishedPost ? (
                <>
                  <Card>
                    <CardBody>
                      <div className="flex flex-wrap items-start justify-between gap-4">
                        <div className="flex items-start gap-3">
                          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-success/10 text-success">
                            <Check className="h-4 w-4" />
                          </span>
                          <div>
                            <p className="text-sm font-medium text-fg">Đã đăng lên {publishedPost.pageName || 'Facebook'}</p>
                            <p className="mt-0.5 text-xs text-fg-muted">
                              {formatDateTime(publishedPost.publishedAtUtc)} · Mã bài{' '}
                              <span className="font-mono">{publishedPost.facebookPostId}</span>
                            </p>
                          </div>
                        </div>
                        <div className="flex gap-2">
                          <Button
                            size="sm"
                            icon={<RefreshCw className="h-3.5 w-3.5" />}
                            loading={metricsLoading === publishedPost.id}
                            onClick={() => refreshMetrics(publishedPost.id)}
                          >
                            Cập nhật số liệu
                          </Button>
                          {publishedPost.postUrl && (
                            <ButtonLink
                              size="sm"
                              variant="primary"
                              href={publishedPost.postUrl}
                              target="_blank"
                              rel="noreferrer"
                              icon={<ExternalLink className="h-3.5 w-3.5" />}
                            >
                              Mở bài đăng
                            </ButtonLink>
                          )}
                        </div>
                      </div>
                      {(publishedPost.firstCommentId || publishedPost.firstCommentError) && (
                        <p className={cn('mt-3 border-t border-line pt-3 text-[13px]', publishedPost.firstCommentId ? 'text-fg-muted' : 'text-danger')}>
                          {publishedPost.firstCommentId ? 'Đã bình luận đầu tiên dưới bài.' : `Chưa bình luận được: ${publishedPost.firstCommentError}`}
                        </p>
                      )}
                    </CardBody>
                  </Card>

                  <div className="grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-line bg-line sm:grid-cols-3">
                    <Metric label="Lượt xem" value={formatNumber(publishedPost.latestMetric?.views)} />
                    <Metric label="Tiếp cận" value={formatNumber(publishedPost.latestMetric?.reach)} />
                    <Metric label="Tương tác" value={formatNumber(publishedPost.latestMetric?.engagement)} />
                    <Metric label="Chia sẻ" value={formatNumber(publishedPost.latestMetric?.shares)} />
                    <Metric label="Bình luận" value={formatNumber(publishedPost.latestMetric?.comments)} />
                    <Metric label="Thời gian xem TB" value={`${(publishedPost.latestMetric?.watchTimeSeconds ?? 0).toFixed(1)}s`} />
                  </div>
                  {publishedPost.latestMetric?.collectedAtUtc && (
                    <p className="px-1 text-xs text-fg-subtle">
                      Số liệu cập nhật lúc {formatTime(publishedPost.latestMetric.collectedAtUtc)}
                    </p>
                  )}
                </>
              ) : (
                <Card>
                  <CardHeader title="Đăng lên Facebook" description="Video cần hoàn tất các bước sau trước khi đăng." />
                  <CardBody>
                    <ul className="space-y-2.5 text-sm">
                      <ChecklistItem done={hasVideo} label="Đã dựng video" />
                      <ChecklistItem done={!!review && !hasFatal} label="Không có lỗi nghiêm trọng" />
                      <ChecklistItem done={isApproved} label="Đã được duyệt" />
                      <ChecklistItem
                        done={pages.length > 0}
                        optional
                        label={pages.length > 0 ? `Đã kết nối ${pages.length} trang Facebook` : 'Chưa kết nối trang Facebook'}
                        extra={
                          pages.length === 0 && (
                            <a href={href('/settings/facebook')} className="text-xs text-accent hover:underline">
                              Kết nối
                            </a>
                          )
                        }
                      />
                    </ul>
                    <Button
                      className="mt-5"
                      variant="primary"
                      icon={<Send className="h-4 w-4" />}
                      disabled={!isApproved || hasFatal}
                      onClick={openPublish}
                    >
                      Đăng lên Facebook
                    </Button>
                  </CardBody>
                </Card>
              )}

              {otherPosts.length > 0 && (
                <Card>
                  <CardHeader title="Lịch sử đăng" />
                  <CardBody className="space-y-2 text-[13px]">
                    {otherPosts.map((p) => (
                      <div key={p.id} className="flex items-center justify-between gap-3">
                        <span className="text-fg-muted">
                          {formatDateTime(p.publishedAtUtc)} · {p.pageName || 'Facebook'}
                          {p.errorMessage && <span className="text-danger"> · {p.errorMessage}</span>}
                        </span>
                        <Badge tone={p.uploadStatus === 'Failed' ? 'danger' : 'neutral'}>{p.uploadStatus}</Badge>
                      </div>
                    ))}
                  </CardBody>
                </Card>
              )}
            </div>
          )}

          {/* FILES */}
          {tab === 'files' && (
            <Card>
              <CardHeader
                title="File tạm"
                description="Video, âm thanh và phụ đề chỉ được lưu tạm và tự xoá khi hết hạn."
                actions={
                  assets.some((a) => !a.isCleanedUp) && (
                    <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} onClick={cleanupAssets}>
                      Xoá tất cả
                    </Button>
                  )
                }
              />
              <CardBody className="pt-3">
                {assets.length === 0 ? (
                  <p className="py-6 text-center text-[13px] text-fg-muted">Chưa có file nào. File được tạo khi dựng video.</p>
                ) : (
                  <div className="-mx-5 overflow-x-auto">
                    <table className="w-full text-[13px]">
                      <thead className="border-y border-line bg-subtle text-left text-xs text-fg-muted">
                        <tr>
                          <th className="py-2 pl-5 pr-3 font-medium">Loại</th>
                          <th className="px-3 py-2 font-medium">Tên file</th>
                          <th className="px-3 py-2 text-right font-medium">Dung lượng</th>
                          <th className="px-3 py-2 font-medium">Hết hạn</th>
                          <th className="py-2 pl-3 pr-5 font-medium">Trạng thái</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-line">
                        {assets.map((a) => (
                          <tr key={a.id}>
                            <td className="py-2.5 pl-5 pr-3 text-fg">{a.assetType}</td>
                            <td className="max-w-[220px] truncate px-3 py-2.5 font-mono text-xs text-fg-muted" title={a.localTempPath}>
                              {a.localTempPath.split(/[/\\]/).pop()}
                            </td>
                            <td className="whitespace-nowrap px-3 py-2.5 text-right tabular text-fg-muted">{formatBytes(a.fileSizeBytes)}</td>
                            <td className="whitespace-nowrap px-3 py-2.5 text-fg-muted">{formatTime(a.expiresAtUtc)}</td>
                            <td className="py-2.5 pl-3 pr-5">
                              <Badge tone={a.isCleanedUp ? 'neutral' : 'success'}>{a.isCleanedUp ? 'Đã xoá' : 'Còn lưu'}</Badge>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </CardBody>
            </Card>
          )}
        </div>
      </div>

      {/* Publish modal */}
      <Modal
        open={showPublish}
        onClose={() => setShowPublish(false)}
        title="Đăng lên Facebook"
        description="Video sẽ được đăng dưới dạng Reels lên trang bạn chọn."
        footer={
          <>
            <Button variant="ghost" onClick={() => setShowPublish(false)}>
              Huỷ
            </Button>
            <Button variant="primary" icon={<Send className="h-4 w-4" />} loading={publishing} onClick={publish}>
              {publishing ? 'Đang tải lên…' : 'Đăng ngay'}
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <Field label="Trang Facebook">
            {pages.length > 0 ? (
              <>
                <Select value={selectedPageId} onChange={(e) => setSelectedPageId(e.target.value)}>
                  {pages.map((p) => (
                    <option key={p.id} value={p.pageId}>
                      {p.pageName}
                      {p.id === project?.facebookAccountId ? ' (tên trên video)' : ''}
                    </option>
                  ))}
                </Select>
                {project?.pageName && pages.find((p) => p.pageId === selectedPageId && p.id !== project.facebookAccountId) && (
                  <p className="mt-1.5 text-xs text-warning">
                    Video đang mang tên “{project.pageName}”. Đăng lên Page khác thì tên trên video sẽ không khớp.
                  </p>
                )}
              </>
            ) : (
              <Alert tone="warning" icon={<Facebook />}>
                Chưa kết nối trang nào. Video sẽ được đăng lên trang mặc định cấu hình trên máy chủ.{' '}
                <a href={href('/settings/facebook')} className="font-medium text-fg underline">
                  Kết nối trang
                </a>
              </Alert>
            )}
          </Field>
          <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-line bg-subtle px-3 py-2.5 text-[13px]">
            <span className="text-fg-muted">AI so tiêu đề với bài gốc, viết caption chuyên nghiệp và bình luận đầu tiên.</span>
            <Button size="sm" icon={<Sparkles className="h-3.5 w-3.5" />} loading={checking} onClick={runEditorial}>
              AI kiểm tra &amp; viết caption
            </Button>
          </div>
          {editorial && !editorial.consistent && (
            <Alert tone="danger" icon={<AlertTriangle />} title="Tiêu đề/nội dung video chưa khớp bài gốc">
              <ul className="list-disc pl-4">
                {editorial.issues.map((i) => (
                  <li key={i}>{i}</li>
                ))}
              </ul>
            </Alert>
          )}
          {editorial?.consistent && (
            <Alert tone="success" icon={<CheckCircle2 />}>
              Tiêu đề và nội dung khớp với bài gốc.
            </Alert>
          )}
          <Field label="Nội dung bài đăng" hint={`${publishCaption.length} ký tự`}>
            <Textarea rows={6} value={publishCaption} onChange={(e) => setPublishCaption(e.target.value)} />
          </Field>
          <Field label="Bình luận đầu tiên" optional hint="Page tự bình luận ngay sau khi đăng: câu hỏi mở + link bài gốc, giúp bài có tương tác sớm.">
            <Textarea rows={3} value={firstComment} onChange={(e) => setFirstComment(e.target.value)} placeholder="Để trống nếu không muốn bình luận" />
          </Field>
          <Checkbox
            checked={autoCleanup}
            onChange={setAutoCleanup}
            label="Xoá file tạm sau khi đăng"
            description="Khuyến nghị. Giải phóng dung lượng máy chủ ngay khi tải lên thành công."
          />
        </div>
      </Modal>

      {/* Reject modal */}
      <Modal
        open={showReject}
        onClose={() => setShowReject(false)}
        title="Từ chối video"
        description="Ghi lại lý do để biết cần sửa gì ở lần sau."
        footer={
          <>
            <Button variant="ghost" onClick={() => setShowReject(false)}>
              Huỷ
            </Button>
            <Button variant="danger" disabled={!rejectNotes.trim()} onClick={reject}>
              Từ chối
            </Button>
          </>
        }
      >
        <Textarea
          rows={4}
          autoFocus
          value={rejectNotes}
          onChange={(e) => setRejectNotes(e.target.value)}
          placeholder="Ví dụ: Số liệu ở cảnh 3 chưa khớp với bài báo, cần kiểm tra lại."
        />
      </Modal>

      {/* Edit modal */}
      <Modal
        open={showEdit}
        onClose={() => setShowEdit(false)}
        title="Sửa tiêu đề"
        footer={
          <>
            <Button variant="ghost" onClick={() => setShowEdit(false)}>
              Huỷ
            </Button>
            <Button variant="primary" disabled={!editTitle.trim()} onClick={saveMetadata}>
              Lưu
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <Field label="Tiêu đề video">
            <Input value={editTitle} onChange={(e) => setEditTitle(e.target.value)} autoFocus />
          </Field>
          <Field label="Chữ trên màn hình (cảnh đầu)" optional>
            <Input value={editCaption} onChange={(e) => setEditCaption(e.target.value)} />
          </Field>
        </div>
      </Modal>
    </div>
  );
};

/* ─────────────── Small pieces ─────────────── */

function decisionText(decision: string | undefined, published: boolean) {
  if (published) return 'Video đã được duyệt và đăng.';
  switch (decision) {
    case 'Approved':
      return 'Video đã được duyệt, có thể đăng lên Facebook.';
    case 'Rejected':
      return 'Video đã bị từ chối. Sửa lại rồi duyệt lại khi sẵn sàng.';
    default:
      return 'Xem lại video và kết quả kiểm định trước khi duyệt.';
  }
}

function ScriptBlock({ title, body, note }: { title: string; body?: string; note?: string }) {
  if (!body) return null;
  return (
    <Card>
      <CardBody>
        <div className="mb-1.5 text-[13px] font-medium text-fg">{title}</div>
        <p className="whitespace-pre-wrap text-[13px] leading-relaxed text-fg-muted">{body}</p>
        {note && <p className="mt-2 text-xs text-fg-subtle">{note}</p>}
      </CardBody>
    </Card>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="bg-surface px-4 py-3.5">
      <div className="text-xs text-fg-muted">{label}</div>
      <div className="mt-1 text-xl font-semibold tabular text-fg">{value}</div>
    </div>
  );
}

function ChecklistItem({
  done,
  label,
  optional,
  extra,
}: {
  done: boolean;
  label: string;
  optional?: boolean;
  extra?: React.ReactNode;
}) {
  return (
    <li className="flex items-center gap-2.5">
      {done ? (
        <CheckCircle2 className="h-4 w-4 shrink-0 text-success" />
      ) : (
        <Circle className={cn('h-4 w-4 shrink-0', optional ? 'text-fg-subtle' : 'text-line-strong')} />
      )}
      <span className={done ? 'text-fg' : 'text-fg-muted'}>{label}</span>
      {extra}
    </li>
  );
}

function DetailSkeleton() {
  return (
    <div>
      <Skeleton className="mb-4 h-4 w-16" />
      <Skeleton className="mb-2 h-7 w-2/3" />
      <Skeleton className="mb-8 h-4 w-1/3" />
      <div className="grid gap-8 lg:grid-cols-[300px_1fr]">
        <Skeleton className="mx-auto aspect-[9/16] w-full max-w-[300px] rounded-2xl" />
        <div className="space-y-4">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-40 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
      </div>
    </div>
  );
}
