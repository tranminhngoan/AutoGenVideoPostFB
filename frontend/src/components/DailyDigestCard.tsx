import React, { useCallback, useEffect, useState } from 'react';
import { ExternalLink, Eye, Newspaper, Send } from 'lucide-react';
import { api, AutoPostPageDto, AutoPostSettingsDto, DailyDigestRunDto, DailyDigestStatus } from '../services/api';
import { Alert, Badge, Button, ButtonLink, Card, CardBody, CardHeader, EmptyState, Segmented, cn } from './ui';
import { useConfirm, useToast } from './feedback';
import { errorText, formatDateTime, Tone } from '../lib/format';

const STATUS: Record<DailyDigestStatus, { label: string; tone: Tone }> = {
  Working: { label: 'Đang làm', tone: 'info' },
  Published: { label: 'Đã đăng', tone: 'success' },
  Partial: { label: 'Đăng một phần', tone: 'warning' },
  Failed: { label: 'Lỗi', tone: 'danger' },
  Preview: { label: 'Bản thử', tone: 'neutral' },
};

const formatDay = (isoDate: string) => isoDate.split('-').reverse().join('/');

/** Daily "Điểm tin nóng" of one page: schedule summary, latest digest (pictures, text, links) and history. */
export function DailyDigestCard({ page, settings }: { page: AutoPostPageDto; settings: AutoPostSettingsDto }) {
  const toast = useToast();
  const confirm = useConfirm();
  const [runs, setRuns] = useState<DailyDigestRunDto[] | null>(null);
  const [busy, setBusy] = useState<'preview' | 'publish' | null>(null);
  const [view, setView] = useState<'post' | 'story'>('post');

  const load = useCallback(async () => {
    try {
      setRuns(await api.getDailyDigests(page.facebookAccountId));
    } catch {
      setRuns([]);
    }
  }, [page.facebookAccountId]);

  useEffect(() => {
    load();
  }, [load]);

  // A digest being made by the scheduler: refresh until it's done
  const working = runs?.some((r) => r.status === 'Working');
  useEffect(() => {
    if (!working) return;
    const t = setInterval(load, 15000);
    return () => clearInterval(t);
  }, [working, load]);

  const preview = async () => {
    setBusy('preview');
    try {
      const run = await api.previewDailyDigest(page.facebookAccountId);
      if (run.status === 'Failed') toast.error('Không tạo được bản tin', run.message ?? '');
      else toast.success('Đã tạo bản tin thử', 'Chưa đăng gì lên Facebook.');
      await load();
    } catch (err) {
      toast.error('Không tạo được bản tin', errorText(err, ''));
    } finally {
      setBusy(null);
    }
  };

  const publish = async () => {
    const ok = await confirm({
      title: `Đăng bản tin tối lên ${page.pageName} ngay?`,
      description: `App chọn ${settings.digestItems} tin giật gân nhất hôm nay, làm ảnh và đăng 1 bài viết + 1 Story ngay (khoảng 1–3 phút). Hôm nay sẽ không đăng thêm bản tin lúc ${settings.digestTime}.`,
      confirmLabel: 'Đăng ngay',
    });
    if (!ok) return;
    setBusy('publish');
    try {
      const run = await api.publishDailyDigest(page.facebookAccountId);
      if (run.status === 'Published') toast.success('Đã đăng bản tin', run.message ?? undefined);
      else if (run.status === 'Partial') toast.error('Đăng một phần', run.message ?? '');
      else toast.error('Không đăng được bản tin', run.message ?? '');
      await load();
    } catch (err) {
      toast.error('Không đăng được bản tin', errorText(err, ''));
    } finally {
      setBusy(null);
    }
  };

  const latest = runs?.[0];
  const older = runs?.slice(1, 6) ?? [];
  const off = !settings.enabled || !settings.digestEnabled;

  return (
    <Card>
      <CardHeader
        title="Bản tin tối · Điểm tin nóng"
        description={
          off
            ? !settings.digestEnabled
              ? 'Đang tắt. Bật trong Cài đặt Page bên dưới.'
              : 'Chỉ tự đăng khi Tự động đăng của Page đang bật.'
            : `Mỗi ngày lúc ${settings.digestTime}: ${settings.digestItems} tin giật gân nhất về chủ đề của Page, đăng 1 bài viết nhiều ảnh + 1 Story.`
        }
        actions={
          <div className="flex flex-wrap gap-2">
            <Button size="sm" icon={<Eye className="h-3.5 w-3.5" />} loading={busy === 'preview'} disabled={busy !== null} onClick={preview}>
              {busy === 'preview' ? 'Đang làm (1–2 phút)…' : 'Tạo thử'}
            </Button>
            <Button size="sm" variant="primary" icon={<Send className="h-3.5 w-3.5" />} loading={busy === 'publish'} disabled={busy !== null} onClick={publish}>
              Đăng ngay
            </Button>
          </div>
        }
      />
      <CardBody className="space-y-4 pt-3">
        {runs === null ? (
          <p className="text-sm text-fg-muted">Đang tải…</p>
        ) : !latest ? (
          <EmptyState
            icon={<Newspaper />}
            title="Chưa có bản tin nào"
            description="Bấm “Tạo thử” để xem trước ảnh và nội dung bản tin hôm nay mà không đăng gì."
          />
        ) : (
          <>
            <div className="flex flex-wrap items-center gap-2 text-[13px]">
              <Badge tone={STATUS[latest.status].tone} dot pulse={latest.status === 'Working'}>
                {STATUS[latest.status].label}
              </Badge>
              <span className="text-fg-muted">
                {formatDay(latest.digestDate)} · {formatDateTime(latest.publishedAtUtc ?? latest.createdAtUtc)}
              </span>
              {latest.postUrl && (
                <ButtonLink size="sm" variant="ghost" href={latest.postUrl} target="_blank" rel="noreferrer" icon={<ExternalLink className="h-3.5 w-3.5" />}>
                  Mở bài viết
                </ButtonLink>
              )}
            </div>
            {latest.message && (
              <p className={cn('text-[13px]', latest.status === 'Failed' ? 'text-danger' : 'text-fg-muted')}>{latest.message}</p>
            )}
            {latest.status === 'Partial' && (
              <Alert tone="warning">{latest.postError ?? latest.storyError}</Alert>
            )}

            {(latest.postImages.length > 0 || latest.storyImages.length > 0) && (
              <div className="space-y-2">
                <Segmented
                  size="sm"
                  value={view}
                  onChange={setView}
                  options={[
                    { value: 'post', label: `Bài viết (${latest.postImages.length} ảnh)` },
                    { value: 'story', label: `Story (${latest.storyImages.length} khung)` },
                  ]}
                />
                <div className="-mx-1 flex gap-2 overflow-x-auto px-1 pb-2">
                  {(view === 'post' ? latest.postImages : latest.storyImages).map((src, i) => (
                    <a key={src} href={src} target="_blank" rel="noreferrer" className="shrink-0" title={i === 0 ? 'Ảnh bìa' : `Tin ${i}`}>
                      <img
                        src={src}
                        alt={i === 0 ? 'Ảnh bìa' : latest.items[i - 1]?.headline ?? `Tin ${i}`}
                        loading="lazy"
                        className={cn('rounded-md border border-line object-cover', view === 'post' ? 'h-40 w-32' : 'h-44 w-[99px]')}
                      />
                    </a>
                  ))}
                </div>
              </div>
            )}

            {latest.items.length > 0 && (
              <ol className="space-y-2.5 text-[13px]">
                {latest.items.map((item, i) => (
                  <li key={item.url} className="flex gap-2.5">
                    <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-accent/10 text-[11px] font-semibold text-accent">
                      {i + 1}
                    </span>
                    <span className="min-w-0">
                      <span className="block font-medium text-fg">{item.headline}</span>
                      <span className="block text-fg-muted">{item.summary}</span>
                      <a href={item.url} target="_blank" rel="noreferrer" className="text-xs text-fg-subtle hover:text-accent hover:underline">
                        {item.sourceName}
                        {item.coverageCount > 1 ? ` · ${item.coverageCount} báo cùng đưa` : ''}
                      </a>
                    </span>
                  </li>
                ))}
              </ol>
            )}

            {latest.caption && (
              <details className="rounded-md border border-line px-3 py-2 text-[13px]">
                <summary className="cursor-pointer text-fg-muted">Nội dung bài viết</summary>
                <p className="mt-2 whitespace-pre-line text-fg">{latest.caption}</p>
              </details>
            )}
          </>
        )}

        {older.length > 0 && (
          <div className="border-t border-line pt-3">
            <p className="mb-2 text-xs font-medium uppercase tracking-wide text-fg-subtle">Trước đó</p>
            <ul className="space-y-1.5 text-[13px]">
              {older.map((r) => (
                <li key={r.id} className="flex flex-wrap items-center gap-2">
                  <Badge tone={STATUS[r.status].tone}>{STATUS[r.status].label}</Badge>
                  <span className="text-fg-muted">{formatDay(r.digestDate)}</span>
                  <span className="min-w-0 flex-1 truncate text-fg-muted">{r.message}</span>
                  {r.postUrl && (
                    <a href={r.postUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-accent hover:underline">
                      Mở <ExternalLink className="h-3 w-3" />
                    </a>
                  )}
                </li>
              ))}
            </ul>
          </div>
        )}
      </CardBody>
    </Card>
  );
}
