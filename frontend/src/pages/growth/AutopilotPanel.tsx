import { useEffect, useState } from 'react';
import { AlertTriangle, Bot, CheckCircle2, Info, Play, RefreshCw } from 'lucide-react';
import { Alert, Badge, Button, ProgressBar, Select, cn } from '../../components/ui';
import { useToast } from '../../components/feedback';
import { autopilotApi, type Autopilot } from '../../services/growth';
import { syncScripts } from '../../lib/growthScriptSync';
import { KIND_LABEL, STATUS_LABEL, vnShort, vnTime } from '../../lib/growthMath';
import { ContentBadge, KindIcon, contentTitle, type GrowthCtx } from './shared';

/**
 * "Chế độ tự động": one switch. Every day the app makes the script post (AI picture + text), posts the newest finished
 * video as a Reel and cuts Stories from the Page's videos — the owner only watches the notices here.
 */
export function AutopilotPanel({ ctx, onChanged }: { ctx: GrowthCtx; onChanged?: () => void }) {
  const page = ctx.page;
  const toast = useToast();
  const [data, setData] = useState<Autopilot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [syncNote, setSyncNote] = useState<string | null>(null);

  const load = async () => {
    if (!page) return;
    try {
      setData(await autopilotApi.get(page.id));
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    }
  };

  useEffect(() => {
    setData(null);
    load();
    // the scripts of "Lên kịch bản" live in this browser: send the ones the server doesn't have yet
    syncScripts()
      .then((added) => {
        if (added > 0) {
          setSyncNote(`Đã đồng bộ ${added} kịch bản từ "Lên kịch bản".`);
          load();
        }
      })
      .catch((e) => setSyncNote('Chưa đồng bộ được kịch bản: ' + (e as Error).message));
    const t = setInterval(load, 60000);
    return () => clearInterval(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page?.id]);

  if (!page) return null;

  const update = async (patch: Partial<Pick<Autopilot, 'enabled' | 'contentMode' | 'scriptPost' | 'dailyVideo' | 'storiesPerDay'>>) => {
    setBusy('set');
    try {
      const next = await autopilotApi.set({ facebookAccountId: page.id, ...patch });
      setData(next);
      if (patch.enabled === true) {
        toast.success('Đã bật chế độ tự động', 'Ứng dụng sẽ chuẩn bị và đăng nội dung mỗi ngày từ 05:00.');
        runNow(true);
      }
    } catch (e) {
      toast.error('Không lưu được', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const runNow = async (quiet = false) => {
    setBusy('run');
    try {
      const next = await autopilotApi.run(page.id);
      setData(next);
      ctx.reloadCampaigns();
      onChanged?.();
      if (!quiet) toast.success('Đã chuẩn bị nội dung hôm nay');
    } catch (e) {
      toast.error('Chạy không thành công', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const s = data?.scripts;
  const news = data?.effectiveMode === 'News';
  const errors = data?.notices.filter((n) => n.level === 'error') ?? [];
  const warnings = data?.notices.filter((n) => n.level === 'warning') ?? [];
  const infos = data?.notices.filter((n) => n.level === 'info') ?? [];

  return (
    <section className={cn('overflow-hidden rounded-lg border bg-surface shadow-card', data?.enabled ? 'border-success/40' : 'border-line')}>
      <div className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:p-5">
        <div className="flex min-w-0 flex-1 items-start gap-3">
          <span className={cn('mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-lg', data?.enabled ? 'bg-success/15 text-success' : 'bg-muted text-fg-muted')}>
            <Bot className="h-[18px] w-[18px]" />
          </span>
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="text-[15px] font-semibold text-fg">Chế độ tự động</h2>
              {data && (data.enabled ? <Badge tone="success" dot pulse>Đang bật</Badge> : <Badge>Đang tắt</Badge>)}
            </div>
            <p className="mt-0.5 text-[13px] text-fg-muted">
              {news
                ? `Page tin tức (${data?.newsTopicName ?? 'chủ đề của Page'}): mỗi ngày 1 bài ảnh từ tin hot nhất đúng chủ đề (ảnh bài báo + tóm tắt viết trên ảnh, không giọng đọc) và ${data?.storiesPerDay ?? 2} Tin là video tin có giọng đọc như video tự động đăng.`
                : `Page kịch bản: mỗi ngày 1 bài viết từ kịch bản (ảnh AI + bài viết), 1 video Reels có sẵn, ${data?.storiesPerDay ?? 2} Tin cắt từ video.`} Đăng lên Facebook{ctx.page?.instagramUsername ? ` và Instagram @${ctx.page.instagramUsername}` : ''}. Bạn chỉ cần xem — khi cần bạn, thông báo hiện ở đây.
            </p>
          </div>
        </div>
        <div className="flex shrink-0 flex-wrap items-center gap-2">
          {data?.enabled && <Button size="sm" variant="ghost" icon={<Play className="h-3.5 w-3.5" />} loading={busy === 'run'} onClick={() => runNow()}>Chạy ngay</Button>}
          <Button size="sm" variant={data?.enabled ? 'secondary' : 'primary'} loading={busy === 'set'} disabled={!data} onClick={() => update({ enabled: !data!.enabled })}>
            {data?.enabled ? 'Tắt' : 'Bật tự động'}
          </Button>
        </div>
      </div>

      {error && <div className="px-4 pb-4 sm:px-5"><Alert tone="danger">{error}</Alert></div>}

      {data && (
        <div className="space-y-4 border-t border-line bg-subtle/60 px-4 py-4 sm:px-5">
          {errors.map((n) => (
            <Alert key={n.code} tone="danger" icon={<AlertTriangle className="h-4 w-4" />} title={n.code === 'scripts_empty' ? 'Hết kịch bản — cần bạn' : undefined}>
              {n.message}
            </Alert>
          ))}
          {warnings.map((n) => (
            <Alert key={n.code} tone="warning">{n.message}</Alert>
          ))}

          <div className="grid gap-4 lg:grid-cols-[1fr_1.4fr]">
            <div className="space-y-3">
              <label className="flex items-center justify-between gap-2 rounded-md border border-line bg-surface px-3 py-2 text-[13px]">
                <span>Loại nội dung</span>
                <Select size="sm" className="w-56" value={data.contentMode} disabled={!!busy} onChange={(e) => update({ contentMode: e.target.value as Autopilot['contentMode'] })}>
                  <option value="Auto">Tự nhận ({data.effectiveMode === 'News' ? 'tin tức' : 'kịch bản'})</option>
                  <option value="News">Tin tức theo chủ đề Page</option>
                  <option value="Scripts">Kịch bản hoài niệm</option>
                </Select>
              </label>
              {!news && <div>
                <div className="flex items-baseline justify-between text-[13px]">
                  <span className="font-medium text-fg">Kho kịch bản</span>
                  <span className="tabular-nums text-fg-muted">còn {s?.remaining ?? 0} / {s?.total ?? 0}</span>
                </div>
                <ProgressBar className="mt-1.5" value={s && s.total ? (s.remaining * 100) / s.total : 0} tone={s && s.remaining <= 7 ? 'warning' : 'success'} />
                <p className="mt-1 text-xs text-fg-subtle">Mỗi kịch bản chỉ dùng 1 lần cho Page này; các chủ đề lần lượt xoay vòng.</p>
                {syncNote && <p className="mt-1 text-xs text-fg-muted">{syncNote}</p>}
              </div>}
              <div className="grid grid-cols-1 gap-2 text-[13px] sm:grid-cols-3 lg:grid-cols-1">
                {!news && <Toggle label="Bài viết từ kịch bản" checked={data.scriptPost} disabled={!!busy} onChange={(v) => update({ scriptPost: v })} />}
                {!news && <Toggle label="Đăng video Reels có sẵn" checked={data.dailyVideo} disabled={!!busy} onChange={(v) => update({ dailyVideo: v })} />}
                <label className="flex items-center justify-between gap-2 rounded-md border border-line bg-surface px-3 py-2">
                  <span>Tin mỗi ngày</span>
                  <Select size="sm" className="w-20" value={data.storiesPerDay} disabled={!!busy} onChange={(e) => update({ storiesPerDay: Number(e.target.value) })}>
                    {[0, 1, 2, 3, 4].map((n) => <option key={n} value={n}>{n}</option>)}
                  </Select>
                </label>
              </div>
              <p className="text-xs text-fg-subtle">
                {news
                  ? 'Tin chọn từ tin hot nhất cùng chủ đề, không trùng tin đã đăng hay đã làm video. Reels tin tức vẫn do "Tự động đăng" đảm nhận như cũ.'
                  : 'Video mới không được tự tạo (mỗi video ~2 USD) — ứng dụng đăng video bạn đã dựng xong.'}{' '}
                Mục tiêu tuần của Meta: 7 bài viết + 14 tin. Lần chạy gần nhất: {vnShort(data.lastRunAtUtc)}.
              </p>
            </div>

            <div>
              <div className="mb-2 flex items-center justify-between">
                <span className="text-[13px] font-medium text-fg">Hôm nay</span>
                <Button size="sm" variant="ghost" icon={<RefreshCw className="h-3.5 w-3.5" />} onClick={load}>Làm mới</Button>
              </div>
              {data.today.length === 0 ? (
                <p className="rounded-md border border-dashed border-line py-6 text-center text-[13px] text-fg-subtle">
                  {data.enabled ? 'Chưa có nội dung hôm nay — ứng dụng chuẩn bị từ 05:00 (hoặc bấm "Chạy ngay").' : 'Bật chế độ tự động để ứng dụng tự chuẩn bị nội dung.'}
                </p>
              ) : (
                <ul className="space-y-1.5">
                  {data.today.map((c) => (
                    <li key={c.id} className="flex items-center gap-2.5 rounded-md border border-line bg-surface px-2.5 py-2 text-[13px]">
                      <span className="w-11 shrink-0 text-xs tabular-nums text-fg-muted">{c.scheduledAtUtc ? vnTime(c.publishedAtUtc ?? c.scheduledAtUtc) : '—'}</span>
                      {c.assets[0]?.kind === 'Image' ? (
                        <img src={c.assets[0].url} alt="" className="h-9 w-9 shrink-0 rounded object-cover" />
                      ) : (
                        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded bg-muted text-fg-muted"><KindIcon kind={c.kind} className="h-4 w-4" /></span>
                      )}
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-fg">{contentTitle(c)}</span>
                        <span className="block text-[11px] text-fg-subtle">
                          {KIND_LABEL[c.kind]}
                          {c.clipStartSeconds != null && ` · đoạn ${Math.round(c.clipStartSeconds)}s–${Math.round(c.clipStartSeconds + (c.clipSeconds ?? 0))}s`}
                          {c.status === 'Failed' && c.lastError ? ` · ${c.lastError.slice(0, 80)}` : ''}
                          {c.status === 'Published' && (c.instagramMediaId ? ' · cả Instagram' : c.instagramError ? ' · Instagram: chưa đăng được' : '')}
                        </span>
                      </span>
                      {c.status === 'Published' ? <CheckCircle2 className="h-4 w-4 text-success" aria-label={STATUS_LABEL[c.status]} /> : <ContentBadge status={c.status} />}
                    </li>
                  ))}
                </ul>
              )}
              {infos.length > 0 && (
                <ul className="mt-2 space-y-1">
                  {infos.map((n) => (
                    <li key={n.code} className="flex gap-1.5 text-xs text-fg-muted"><Info className="mt-0.5 h-3 w-3 shrink-0" />{n.message}</li>
                  ))}
                </ul>
              )}
            </div>
          </div>
        </div>
      )}
    </section>
  );
}

function Toggle({ label, checked, disabled, onChange }: { label: string; checked: boolean; disabled?: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className={cn('flex cursor-pointer items-center justify-between gap-2 rounded-md border border-line bg-surface px-3 py-2', disabled && 'opacity-60')}>
      <span>{label}</span>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-label={label}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={cn('relative h-5 w-9 shrink-0 rounded-full transition-colors', checked ? 'bg-success' : 'bg-line-strong')}
      >
        <span className={cn('absolute top-0.5 h-4 w-4 rounded-full bg-white shadow transition-all', checked ? 'left-[18px]' : 'left-0.5')} />
      </button>
    </label>
  );
}
