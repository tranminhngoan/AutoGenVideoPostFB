import { useCallback, useEffect, useState } from 'react';
import { ArrowLeft, Plus, RotateCw, Trash2 } from 'lucide-react';
import { Badge, Button, Card, EmptyState, PageHeader, Skeleton, Tabs } from '../components/ui';
import { MusicLibrary } from '../components/studio/MusicLibrary';
import { CostPanel } from '../components/studio/CostPanel';
import { VoiceDesigner } from '../components/studio/VoiceDesigner';
import { StudioUnlock } from '../components/studio/StudioUnlock';
import { VideoEditor, fmtDuration } from '../components/studio/VideoEditor';
import { BUSY, STATUS_LABEL, StudioError, StudioSettings, StudioVideoSummary, getStudioKey, studio, usd } from '../services/studio';
import { navigate } from '../lib/router';
import { useConfirm, useToast } from '../components/feedback';

type Tab = 'videos' | 'music' | 'voices' | 'costs';

/**
 * AI video studio: make live-action videos with Google Veo (text-to-video) from a script, AI voices and the music store, track
 * the cost, and post them to a Facebook Page. Routes: /studio[/assets|/music|/costs], /studio/new, /studio/videos/:id.
 */
export function StudioPage({ segments }: { segments: string[] }) {
  const [first, second] = segments;
  const tab: Tab = first === 'music' || first === 'costs' || first === 'voices' ? first : 'videos';
  const editingId = first === 'videos' && second ? second : undefined;
  const creating = first === 'new';

  const [needsKey, setNeedsKey] = useState(false);
  const [settings, setSettings] = useState<StudioSettings | null>(null);

  useEffect(() => {
    document.title = (creating ? 'Video mới' : editingId ? 'Chỉnh video' : 'Studio Video AI') + ' · Facebook chéo uy tính';
  }, [creating, editingId]);

  useEffect(() => {
    studio.settings().then(setSettings).catch(() => setSettings(null));
    // check the stored key once so the unlock box shows before the first failed action
    studio.checkAccess().then(() => setNeedsKey(false)).catch((e) => setNeedsKey(e instanceof StudioError && e.needsKey));
  }, []);

  /** Shared handler: a 401 from any write shows the unlock box instead of a toast. */
  const onAuthError = useCallback((e: unknown) => {
    if (e instanceof StudioError && e.needsKey) {
      setNeedsKey(true);
      window.scrollTo({ top: 0, behavior: 'smooth' });
      return true;
    }
    return false;
  }, []);

  const header = (
    <PageHeader
      title={
        <span className="flex items-center gap-2">
          Studio Video AI <Badge tone="info" className="text-[11px] font-normal">{settings?.videoModel ?? 'grok-imagine'}</Badge>
        </span>
      }
      description="Dựng video bằng Google Veo (text-to-video): kịch bản → hồ sơ nhân vật → video + giọng đọc AI + kho nhạc → 1080p → đăng Page. Chi phí theo giây video."
      back={
        editingId || creating ? (
          <Button variant="ghost" size="sm" icon={<ArrowLeft className="h-4 w-4" />} onClick={() => navigate('/studio')}>
            Danh sách video
          </Button>
        ) : undefined
      }
      actions={
        !editingId && !creating ? (
          <Button variant="primary" icon={<Plus className="h-4 w-4" />} onClick={() => navigate('/studio/new')}>
            Video mới
          </Button>
        ) : undefined
      }
    />
  );

  return (
    <div className="space-y-5 pb-12">
      {header}

      {settings && !settings.videoConfigured && (
        <Card className="border-danger/30 bg-danger/[0.05] p-3 text-xs text-danger">
          Server chưa có khoá Vercel AI Gateway (biến AI_GATEWAY_API_KEY) nên chưa tạo được video — tạo khoá ở vercel.com → AI Gateway (nạp tối thiểu $10 credit) rồi thêm biến này
          trên Railway.
        </Card>
      )}
      {needsKey && <StudioUnlock onUnlocked={() => setNeedsKey(false)} />}
      {!needsKey && !getStudioKey() && settings?.videoConfigured && (
        <p className="text-xs text-fg-subtle">Mẹo: lần đầu thao tác, app sẽ hỏi khoá Vercel AI Gateway (hoặc Gemini) để mở khoá trên trình duyệt này.</p>
      )}

      {creating ? (
        <VideoEditor onAuthError={onAuthError} />
      ) : editingId ? (
        <VideoEditor key={editingId} videoId={editingId} onAuthError={onAuthError} />
      ) : (
        <>
          <Tabs<Tab>
            items={[
              { id: 'videos', label: 'Video' },
              { id: 'music', label: 'Kho nhạc' },
              { id: 'voices', label: 'Giọng đọc' },
              { id: 'costs', label: 'Chi phí' },
            ]}
            value={tab}
            onChange={(t) => navigate(t === 'videos' ? '/studio' : `/studio/${t}`)}
          />
          {tab === 'videos' && <VideoList onAuthError={onAuthError} />}
          {tab === 'music' && <MusicLibrary onAuthError={onAuthError} />}
          {tab === 'voices' && <VoiceDesigner onAuthError={onAuthError} />}
          {tab === 'costs' && <CostPanel onAuthError={onAuthError} />}
        </>
      )}
    </div>
  );
}

function VideoList({ onAuthError }: { onAuthError: (e: unknown) => boolean }) {
  const [videos, setVideos] = useState<StudioVideoSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [deleting, setDeleting] = useState(false);
  const confirm = useConfirm();
  const toast = useToast();

  const load = async () => {
    try {
      setVideos(await studio.videos());
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
  };
  useEffect(() => {
    load();
    const t = window.setInterval(load, 10000);
    return () => window.clearInterval(t);
  }, []);

  const toggle = (id: string) => setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));

  /** Deletes videos for good (clips, voices, final file); a post already on Facebook stays there. */
  const remove = async (ids: string[]) => {
    if (ids.length === 0 || !videos) return;
    const one = ids.length === 1 ? videos.find((v) => v.id === ids[0]) : null;
    if (!(await confirm({
      title: one ? `Xoá video “${one.title}”?` : `Xoá ${ids.length} video đã chọn?`,
      description: 'Xoá hẳn khỏi tool (clip, giọng đọc, video ghép). Bài đã đăng trên Facebook vẫn còn trên Facebook. Video đang tạo / ghép thì chờ xong mới xoá được.',
      confirmLabel: 'Xoá',
      danger: true,
    }))) return;
    setDeleting(true);
    const errors: string[] = [];
    for (const id of ids) {
      try {
        await studio.deleteVideo(id);
      } catch (e) {
        if (onAuthError(e)) break;
        errors.push((e as Error).message);
      }
    }
    setDeleting(false);
    setSelected([]);
    await load();
    if (errors.length) toast.error(`Không xoá được ${errors.length} video — ${errors[0]}`);
    else toast.success(ids.length === 1 ? 'Đã xoá video' : `Đã xoá ${ids.length} video`);
  };

  if (error) return <p className="text-sm text-danger">{error}</p>;
  if (!videos) return <Skeleton className="h-40 w-full" />;
  if (videos.length === 0)
    return (
      <EmptyState
        title="Chưa có video nào"
        description="Bắt đầu bằng việc thêm ảnh nhân vật / bối cảnh và nhạc vào kho, rồi bấm Video mới và dán kịch bản."
        action={
          <Button variant="primary" icon={<Plus className="h-4 w-4" />} onClick={() => navigate('/studio/new')}>
            Video mới
          </Button>
        }
      />
    );

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center justify-end gap-2">
        {selected.length > 0 && (
          <>
            <span className="text-xs text-fg-muted">Đã chọn {selected.length}</span>
            <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} loading={deleting} onClick={() => remove(selected)}>
              Xoá {selected.length} video
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setSelected([])}>Bỏ chọn</Button>
          </>
        )}
        <Button size="sm" variant="ghost" onClick={() => setSelected(selected.length === videos.length ? [] : videos.map((v) => v.id))}>
          {selected.length === videos.length ? 'Bỏ chọn tất cả' : 'Chọn tất cả'}
        </Button>
        <Button size="sm" variant="ghost" icon={<RotateCw className="h-3.5 w-3.5" />} onClick={load}>
          Làm mới
        </Button>
      </div>
      {videos.map((v) => (
        <Card key={v.id} className="cursor-pointer p-3 transition-colors hover:bg-subtle" onClick={() => navigate(`/studio/videos/${v.id}`)}>
          <div className="flex flex-wrap items-center gap-2">
            <input type="checkbox" aria-label="Chọn video" checked={selected.includes(v.id)}
              onClick={(e) => e.stopPropagation()} onChange={() => toggle(v.id)} />
            <span className="min-w-0 flex-1 truncate text-sm font-semibold text-fg">{v.title}</span>
            <Badge
              tone={v.status === 'Published' || v.status === 'Ready' ? 'success' : v.status === 'Failed' ? 'danger' : BUSY.includes(v.status) ? 'warning' : 'neutral'}
              dot
              pulse={BUSY.includes(v.status)}
            >
              {STATUS_LABEL[v.status]}
            </Badge>
            <button type="button" aria-label="Xoá video" title="Xoá video"
              className="rounded-md p-1.5 text-fg-subtle hover:bg-danger/10 hover:text-danger"
              onClick={(e) => { e.stopPropagation(); remove([v.id]); }}>
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
          <div className="mt-1 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-fg-muted">
            <span>{v.pageName ?? 'Chưa chọn Page'}</span>
            <span>{v.shotCount ? '' : '~'}{fmtDuration(v.targetDurationSeconds)} · {v.shotCount} shot</span>
            <span>Ước tính {usd(v.estimatedCostUsd)} · đã chi {usd(v.actualCostUsd, 3)}</span>
            <span>{new Date(v.createdAtUtc).toLocaleString('vi-VN', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })}</span>
          </div>
          {v.progress && <div className="mt-1 text-xs text-warning">{v.progress}</div>}
        </Card>
      ))}
    </div>
  );
}
