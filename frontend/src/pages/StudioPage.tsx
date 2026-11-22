import { useCallback, useEffect, useState } from 'react';
import { ArrowLeft, Plus, RotateCw } from 'lucide-react';
import { Badge, Button, Card, EmptyState, PageHeader, Skeleton, Tabs } from '../components/ui';
import { AssetLibrary } from '../components/studio/AssetLibrary';
import { MusicLibrary } from '../components/studio/MusicLibrary';
import { CostPanel } from '../components/studio/CostPanel';
import { StudioUnlock } from '../components/studio/StudioUnlock';
import { VideoEditor } from '../components/studio/VideoEditor';
import { BUSY, STATUS_LABEL, StudioError, StudioSettings, StudioVideoSummary, getStudioKey, studio, usd } from '../services/studio';
import { navigate } from '../lib/router';

type Tab = 'videos' | 'assets' | 'music' | 'costs';

/**
 * Omni Studio: make real-people videos with Gemini Omni from a script + reference images + the music store, track
 * the cost, and post them to a Facebook Page. Routes: /studio[/assets|/music|/costs], /studio/new, /studio/videos/:id.
 */
export function StudioPage({ segments }: { segments: string[] }) {
  const [first, second] = segments;
  const tab: Tab = first === 'assets' || first === 'music' || first === 'costs' ? first : 'videos';
  const editingId = first === 'videos' && second ? second : undefined;
  const creating = first === 'new';

  const [needsKey, setNeedsKey] = useState(false);
  const [settings, setSettings] = useState<StudioSettings | null>(null);

  useEffect(() => {
    document.title = (creating ? 'Video mới' : editingId ? 'Chỉnh video' : 'Omni Studio') + ' · Trạm Tin 24/7';
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
          Omni Studio <Badge tone="info" className="text-[11px] font-normal">{settings?.videoModel ?? 'gemini-omni'}</Badge>
        </span>
      }
      description="Dựng video người thật bằng Gemini Omni: kịch bản + ảnh nhân vật/bối cảnh + kho nhạc → video → đăng Page. Chi phí theo giây video."
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

      {settings && !settings.omniConfigured && (
        <Card className="border-danger/30 bg-danger/[0.05] p-3 text-xs text-danger">
          Server chưa có khoá Gemini trả phí (biến GEMINI_API_KEY). Omni không có gói miễn phí nên chưa tạo được video — thêm khoá đã bật thanh toán trên
          Railway.
        </Card>
      )}
      {needsKey && <StudioUnlock onUnlocked={() => setNeedsKey(false)} />}
      {!needsKey && !getStudioKey() && settings?.omniConfigured && (
        <p className="text-xs text-fg-subtle">Mẹo: lần đầu thao tác, app sẽ hỏi khoá Gemini để mở khoá trên trình duyệt này.</p>
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
              { id: 'assets', label: 'Nhân vật & bối cảnh' },
              { id: 'music', label: 'Kho nhạc' },
              { id: 'costs', label: 'Chi phí' },
            ]}
            value={tab}
            onChange={(t) => navigate(t === 'videos' ? '/studio' : `/studio/${t}`)}
          />
          {tab === 'videos' && <VideoList />}
          {tab === 'assets' && <AssetLibrary onAuthError={onAuthError} />}
          {tab === 'music' && <MusicLibrary onAuthError={onAuthError} />}
          {tab === 'costs' && <CostPanel onAuthError={onAuthError} />}
        </>
      )}
    </div>
  );
}

function VideoList() {
  const [videos, setVideos] = useState<StudioVideoSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

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
      <div className="flex justify-end">
        <Button size="sm" variant="ghost" icon={<RotateCw className="h-3.5 w-3.5" />} onClick={load}>
          Làm mới
        </Button>
      </div>
      {videos.map((v) => (
        <Card key={v.id} className="cursor-pointer p-3 transition-colors hover:bg-subtle" onClick={() => navigate(`/studio/videos/${v.id}`)}>
          <div className="flex flex-wrap items-center gap-2">
            <span className="min-w-0 flex-1 truncate text-sm font-semibold text-fg">{v.title}</span>
            <Badge
              tone={v.status === 'Published' || v.status === 'Ready' ? 'success' : v.status === 'Failed' ? 'danger' : BUSY.includes(v.status) ? 'warning' : 'neutral'}
              dot
              pulse={BUSY.includes(v.status)}
            >
              {STATUS_LABEL[v.status]}
            </Badge>
          </div>
          <div className="mt-1 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-fg-muted">
            <span>{v.pageName ?? 'Chưa chọn Page'}</span>
            <span>{v.targetDurationSeconds}s · {v.shotCount} shot</span>
            <span>Ước tính {usd(v.estimatedCostUsd)} · đã chi {usd(v.actualCostUsd, 3)}</span>
            <span>{new Date(v.createdAtUtc).toLocaleString('vi-VN', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })}</span>
          </div>
          {v.progress && <div className="mt-1 text-xs text-warning">{v.progress}</div>}
        </Card>
      ))}
    </div>
  );
}
