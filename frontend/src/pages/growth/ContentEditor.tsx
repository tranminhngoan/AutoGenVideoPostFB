import { useEffect, useMemo, useRef, useState } from 'react';
import { CalendarClock, Check, Download, ExternalLink, Hand, Loader2, Play, RotateCcw, Send, Sparkles, Trash2, Upload, X } from 'lucide-react';
import { Alert, Badge, Button, Checkbox, Drawer, Field, Input, Segmented, Select, Textarea, cn } from '../../components/ui';
import { useConfirm, useToast } from '../../components/feedback';
import { growth, type Asset, type Campaign, type CaptionVariant, type Content, type ContentInput, type ContentKind, type SourceType, type VideoSource } from '../../services/growth';
import { countHashtags, KIND_LABEL, newKey, STYLE_LABEL, vnDate, vnTime, vnToUtc, vnToday, vnShort } from '../../lib/growthMath';
import { ContentBadge, KindIcon, PageAvatar, type GrowthCtx } from './shared';

export interface ContentDefaults {
  kind?: ContentKind;
  date?: string;
  time?: string;
  title?: string;
  caption?: string;
  hashtags?: string;
  style?: string;
  cta?: string;
  assetIds?: string[];
  sourceType?: SourceType;
  sourceId?: string;
}

const MAX_IMAGE = 8 * 1024 * 1024;
const MAX_VIDEO = 100 * 1024 * 1024;
const KINDS: ContentKind[] = ['Reel', 'Photo', 'Album', 'Story', 'Interactive'];

/** Create / edit one calendar item. Posting itself is done by the server's scheduler (or "Đăng ngay"). */
export function ContentEditor({
  ctx,
  campaign,
  item,
  defaults,
  open,
  onClose,
  onSaved,
}: {
  ctx: GrowthCtx;
  campaign: Campaign;
  item?: Content | null;
  defaults?: ContentDefaults;
  open: boolean;
  onClose: () => void;
  onSaved: (c: Content | null) => void;
}) {
  const toast = useToast();
  const confirm = useConfirm();
  const editing = !!item;
  const firstSlot = campaign.timeSlots[0] ?? '20:00';
  const startDefault = vnToday() < campaign.startDate ? campaign.startDate : vnToday();

  const [kind, setKind] = useState<ContentKind>(item?.kind ?? defaults?.kind ?? 'Photo');
  const [title, setTitle] = useState(item?.title ?? defaults?.title ?? '');
  const [caption, setCaption] = useState(item?.caption ?? defaults?.caption ?? '');
  const [hashtags, setHashtags] = useState(item?.hashtags ?? defaults?.hashtags ?? '');
  const [style, setStyle] = useState(item?.style ?? defaults?.style ?? '');
  const [cta, setCta] = useState(item?.cta ?? defaults?.cta ?? '');
  const [sourceKey, setSourceKey] = useState(
    item?.sourceType && item.sourceId ? `${item.sourceType}:${item.sourceId}` : defaults?.sourceType && defaults.sourceId ? `${defaults.sourceType}:${defaults.sourceId}` : ''
  );
  const [assetIds, setAssetIds] = useState<string[]>(item?.assets.map((a) => a.id) ?? defaults?.assetIds ?? []);
  const [date, setDate] = useState(item?.scheduledAtUtc ? vnDate(item.scheduledAtUtc) : defaults?.date ?? startDefault);
  const [time, setTime] = useState(item?.scheduledAtUtc ? vnTime(item.scheduledAtUtc) : defaults?.time ?? firstSlot);
  const [manual, setManual] = useState(item?.manualPublish ?? false);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const keyRef = useRef(newKey()); // one key per editor: a double click never makes two items

  const [sources, setSources] = useState<VideoSource[] | null>(null);
  const [assets, setAssets] = useState<Asset[] | null>(item?.assets ?? null);
  const [permalink, setPermalink] = useState('');

  const locked = !!item && !['Draft', 'PendingApproval', 'Scheduled', 'Failed'].includes(item.status);
  const needsVideo = kind === 'Reel';
  const allowsVideoSource = kind === 'Reel' || kind === 'Story';
  const allowsAssets = kind !== 'Reel';

  useEffect(() => {
    if (!open) return;
    if (allowsVideoSource && sources === null) growth.sources(campaign.id).then(setSources).catch((e) => setError(e.message));
    if (allowsAssets && assets === null) growth.assets(campaign.id).then(setAssets).catch((e) => setError(e.message));
  }, [open, allowsVideoSource, allowsAssets, campaign.id, sources, assets]);

  const selectedAssets = useMemo(() => assetIds.map((id) => assets?.find((a) => a.id === id) ?? item?.assets.find((a) => a.id === id)).filter(Boolean) as Asset[], [assetIds, assets, item]);
  const source = useMemo(() => sources?.find((s) => `${s.type}:${s.id}` === sourceKey) ?? null, [sources, sourceKey]);
  const tags = countHashtags(caption, hashtags);

  const input = (submit?: boolean): ContentInput => {
    const [st, sid] = sourceKey ? (sourceKey.split(':') as [SourceType, string]) : ['', ''];
    return {
      kind,
      title,
      caption,
      hashtags,
      style: style || null,
      cta: cta || null,
      sourceType: allowsVideoSource && st ? (st as SourceType) : '',
      sourceId: allowsVideoSource ? sid || null : null,
      assetIds: allowsAssets ? assetIds : [],
      scheduledAtUtc: date && time ? vnToUtc(date, time) : null,
      manualPublish: kind === 'Story' ? manual : false,
      idempotencyKey: editing ? undefined : keyRef.current,
      submit,
    };
  };

  const run = async (label: string, fn: () => Promise<Content | null>, success: string) => {
    setBusy(label);
    setError(null);
    try {
      const saved = await fn();
      toast.success(success);
      onSaved(saved);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const save = (submit: boolean) =>
    run(
      submit ? 'submit' : 'save',
      async () => {
        if (!editing) return growth.createContent(campaign.id, input(submit));
        let c = await growth.updateContent(campaign.id, item!.id, input());
        if (submit && c.status === 'Draft') c = await growth.contentAction(campaign.id, c.id, 'submit');
        return c;
      },
      submit ? (campaign.approvalMode === 'Auto' ? 'Đã lên lịch đăng' : 'Đã gửi chờ duyệt') : 'Đã lưu'
    );

  const act = (a: 'approve' | 'cancel' | 'publish-now' | 'retry', msg: string) => run(a, () => growth.contentAction(campaign.id, item!.id, a), msg);

  const upload = async (files: FileList | null) => {
    if (!files?.length) return;
    setError(null);
    for (const f of Array.from(files)) {
      const isImage = f.type === 'image/jpeg' || f.type === 'image/png';
      const isVideo = f.type === 'video/mp4' || f.type === 'video/quicktime';
      if (!isImage && !isVideo) {
        setError(`${f.name}: chỉ nhận ảnh JPEG / PNG hoặc video MP4 / MOV.`);
        continue;
      }
      if (isVideo && kind !== 'Story') {
        setError('Video tải lên chỉ dùng cho Story. Reels lấy video từ kho video của ứng dụng.');
        continue;
      }
      if (f.size > (isImage ? MAX_IMAGE : MAX_VIDEO)) {
        setError(`${f.name}: quá lớn (tối đa ${isImage ? 8 : 100} MB).`);
        continue;
      }
      setBusy('upload');
      try {
        const a = await growth.upload(campaign.id, f, f.name);
        setAssets((list) => [a, ...(list ?? [])]);
        setAssetIds((ids) => (kind === 'Photo' || kind === 'Story' ? [a.id] : [...ids, a.id].slice(0, 10)));
      } catch (e) {
        setError((e as Error).message);
      } finally {
        setBusy(null);
      }
    }
  };

  const toggleAsset = (a: Asset) => {
    if (kind === 'Photo' || kind === 'Story') setAssetIds((ids) => (ids[0] === a.id ? [] : [a.id]));
    else setAssetIds((ids) => (ids.includes(a.id) ? ids.filter((x) => x !== a.id) : [...ids, a.id].slice(0, 10)));
  };

  const status = item?.status;
  const footer = (
    <div className="flex flex-wrap items-center justify-end gap-2">
      {item && ['Draft', 'Cancelled'].includes(item.status) && (
        <Button
          variant="danger-soft"
          size="sm"
          icon={<Trash2 className="h-3.5 w-3.5" />}
          loading={busy === 'delete'}
          onClick={async () => {
            if (!(await confirm({ title: 'Xoá nội dung này?', danger: true, confirmLabel: 'Xoá' }))) return;
            run('delete', async () => { await growth.deleteContent(campaign.id, item.id); return null; }, 'Đã xoá');
          }}
        >
          Xoá
        </Button>
      )}
      {item && ['Draft', 'PendingApproval', 'Scheduled', 'Failed'].includes(item.status) && (
        <Button
          variant="ghost"
          size="sm"
          icon={<X className="h-3.5 w-3.5" />}
          loading={busy === 'cancel'}
          onClick={async () => {
            if (!(await confirm({ title: 'Huỷ lịch đăng?', description: 'Nội dung sẽ không được đăng. Có thể tạo lại sau.', confirmLabel: 'Huỷ lịch' }))) return;
            act('cancel', 'Đã huỷ lịch');
          }}
        >
          Huỷ lịch
        </Button>
      )}
      <div className="flex-1" />
      {!locked && (
        <Button size="sm" loading={busy === 'save'} disabled={!!busy} onClick={() => save(false)}>
          {editing ? 'Lưu' : 'Lưu nháp'}
        </Button>
      )}
      {(!item || status === 'Draft') && (
        <Button size="sm" variant="primary" icon={<CalendarClock className="h-3.5 w-3.5" />} loading={busy === 'submit'} disabled={!!busy} onClick={() => save(true)}>
          {campaign.approvalMode === 'Auto' ? 'Lên lịch đăng' : 'Gửi duyệt'}
        </Button>
      )}
      {status === 'PendingApproval' && (
        <Button size="sm" variant="primary" icon={<Check className="h-3.5 w-3.5" />} loading={busy === 'approve'} disabled={!!busy} onClick={async () => { await growth.updateContent(campaign.id, item!.id, input()).catch(() => null); act('approve', 'Đã duyệt — sẽ đăng đúng giờ'); }}>
          Duyệt
        </Button>
      )}
      {status === 'Failed' && !item?.externalPostId && (
        <Button size="sm" icon={<RotateCcw className="h-3.5 w-3.5" />} loading={busy === 'retry'} disabled={!!busy} onClick={() => act('retry', 'Đã đưa lại vào hàng đợi')}>
          Thử lại
        </Button>
      )}
      {item && ['Draft', 'PendingApproval', 'Scheduled', 'Failed'].includes(item.status) && !item.manualPublish && campaign.status === 'Running' && (
        <Button
          size="sm"
          variant="accent"
          icon={<Send className="h-3.5 w-3.5" />}
          loading={busy === 'publish-now'}
          disabled={!!busy}
          onClick={async () => {
            if (!(await confirm({ title: 'Đăng ngay lên Facebook?', description: `Nội dung sẽ được đăng lên Page ${campaign.pageName} trong vài giây.`, confirmLabel: 'Đăng ngay' }))) return;
            act('publish-now', 'Đang đăng — trạng thái cập nhật trong giây lát');
          }}
        >
          Đăng ngay
        </Button>
      )}
    </div>
  );

  return (
    <Drawer open={open} onClose={onClose} title={editing ? 'Chỉnh sửa nội dung' : 'Thêm nội dung'} subtitle={campaign.name} footer={footer}>
      <div className="space-y-5">
        {item && (
          <div className="flex flex-wrap items-center gap-2 text-[13px]">
            <ContentBadge status={item.status} />
            {item.publishedAtUtc && <span className="text-fg-muted">Đăng lúc {vnShort(item.publishedAtUtc)}{item.publishedManually ? ' (thủ công)' : ''}</span>}
            {item.permalinkUrl && (
              <a className="inline-flex items-center gap-1 text-accent hover:underline" href={item.permalinkUrl} target="_blank" rel="noreferrer noopener">
                Xem trên Facebook <ExternalLink className="h-3 w-3" />
              </a>
            )}
            {item.instagramPermalink?.startsWith('http') && (
              <a className="inline-flex items-center gap-1 text-accent hover:underline" href={item.instagramPermalink} target="_blank" rel="noreferrer noopener">
                Xem trên Instagram <ExternalLink className="h-3 w-3" />
              </a>
            )}
            {item.instagramMediaId && !item.instagramPermalink?.startsWith('http') && <Badge tone="success">Đã đăng Instagram</Badge>}
            {item.attempts > 0 && item.status !== 'Published' && <span className="text-fg-subtle">Đã thử {item.attempts}/{item.maxAttempts} lần</span>}
          </div>
        )}
        {item?.lastError && item.status !== 'Published' && (
          <Alert tone={item.status === 'Failed' ? 'danger' : 'warning'} title={item.status === 'Failed' ? 'Đăng thất bại' : 'Sẽ thử lại'}>
            {item.lastError}
            {item.nextAttemptAtUtc && <div className="mt-1 text-xs">Lần thử tiếp theo: {vnShort(item.nextAttemptAtUtc)}</div>}
          </Alert>
        )}
        {item?.status === 'Published' && item.instagramError && (
          <Alert tone="warning" title="Facebook đã đăng — Instagram chưa đăng được">{item.instagramError}</Alert>
        )}
        {error && <Alert tone="danger">{error}</Alert>}

        <Field label="Loại nội dung">
          <Segmented
            size="sm"
            options={KINDS.map((k) => ({ value: k, label: <span className="inline-flex items-center gap-1"><KindIcon kind={k} />{KIND_LABEL[k]}</span> }))}
            value={kind}
            onChange={(k) => {
              if (editing) return;
              setKind(k);
              if (k === 'Photo' || k === 'Story') setAssetIds((ids) => ids.slice(0, 1));
            }}
          />
          {editing && <p className="mt-1 text-xs text-fg-subtle">Không đổi loại khi sửa — tạo nội dung mới nếu cần.</p>}
        </Field>

        {allowsVideoSource && (
          <Field label={kind === 'Reel' ? 'Video từ kho hiện có' : 'Video từ kho (tuỳ chọn)'} hint={kind === 'Reel' ? 'Reels được đăng bằng chức năng đăng video hiện có của ứng dụng — không tạo video mới.' : 'Story video tối đa 60 giây. Hoặc chọn 1 ảnh bên dưới.'}>
            {sources === null ? (
              <div className="flex h-9 items-center gap-2 text-[13px] text-fg-subtle"><Loader2 className="h-4 w-4 animate-spin" /> Đang tải kho video…</div>
            ) : sources.length === 0 ? (
              <p className="text-[13px] text-fg-muted">Chưa có video hoàn thành cho Page này. Tạo video ở Studio Video AI hoặc AI Video Studio.</p>
            ) : (
              <Select value={sourceKey} disabled={locked} onChange={(e) => { setSourceKey(e.target.value); if (e.target.value && kind === 'Story') setAssetIds([]); }}>
                <option value="">{kind === 'Reel' ? '— Chọn video —' : '— Không dùng video —'}</option>
                {sources.map((s) => (
                  <option key={`${s.type}:${s.id}`} value={`${s.type}:${s.id}`} disabled={s.alreadyPublished && `${s.type}:${s.id}` !== sourceKey}>
                    [{s.typeLabel}] {s.title}
                    {s.durationSeconds ? ` · ${Math.round(s.durationSeconds)}s` : ''}
                    {s.alreadyPublished ? ' · đã đăng' : s.scheduled ? ' · đã có trong lịch' : ''}
                  </option>
                ))}
              </Select>
            )}
            {source?.previewUrl && (
              <video key={source.previewUrl} src={source.previewUrl} controls preload="metadata" className="mt-2 aspect-[9/16] max-h-64 rounded-md border border-line bg-black" />
            )}
            {source?.type === 'VideoProject' && <p className="mt-1 text-xs text-warning">Video tin tức cần được ban biên tập duyệt ở trang Video trước giờ đăng.</p>}
            {source?.caption && !caption && (
              <button type="button" className="mt-1 text-xs text-accent hover:underline" onClick={() => setCaption(source.caption!)}>Dùng chú thích sẵn có của video</button>
            )}
          </Field>
        )}

        {allowsAssets && !(kind === 'Story' && sourceKey) && (
          <Field label={kind === 'Album' ? 'Ảnh (2–10)' : kind === 'Interactive' ? 'Ảnh kèm (tuỳ chọn)' : kind === 'Story' ? 'Ảnh / video Story' : 'Ảnh'} hint="JPEG/PNG ≤ 8 MB. Tạo ảnh từ template ở Studio nội dung.">
            <div className="grid grid-cols-4 gap-2 sm:grid-cols-5">
              {(assets ?? []).filter((a) => kind === 'Story' || a.kind === 'Image').slice(0, 30).map((a) => {
                const idx = assetIds.indexOf(a.id);
                return (
                  <button
                    key={a.id}
                    type="button"
                    disabled={locked}
                    onClick={() => toggleAsset(a)}
                    className={cn('relative aspect-square overflow-hidden rounded-md border bg-muted', idx >= 0 ? 'border-accent ring-2 ring-accent/40' : 'border-line hover:border-line-strong')}
                    title={a.fileName}
                  >
                    {a.kind === 'Image' ? <img src={a.url} alt={a.fileName} loading="lazy" className="h-full w-full object-cover" /> : <span className="flex h-full items-center justify-center text-fg-subtle"><Play className="h-5 w-5" /></span>}
                    {idx >= 0 && <span className="absolute right-1 top-1 flex h-5 w-5 items-center justify-center rounded-full bg-accent text-[11px] font-semibold text-accent-fg">{idx + 1}</span>}
                  </button>
                );
              })}
              <label className={cn('flex aspect-square cursor-pointer flex-col items-center justify-center gap-1 rounded-md border border-dashed border-line-strong text-[11px] text-fg-muted hover:bg-muted', (locked || busy === 'upload') && 'pointer-events-none opacity-60')}>
                {busy === 'upload' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}
                Tải lên
                <input type="file" className="sr-only" multiple={kind === 'Album' || kind === 'Interactive'} accept={kind === 'Story' ? 'image/jpeg,image/png,video/mp4,video/quicktime' : 'image/jpeg,image/png'} onChange={(e) => { upload(e.target.files); e.target.value = ''; }} />
              </label>
            </div>
            {assets === null && <div className="mt-2 text-xs text-fg-subtle">Đang tải kho ảnh…</div>}
          </Field>
        )}

        {kind !== 'Story' && (
          <>
            <Field htmlFor="ce-title" label="Tiêu đề nội bộ" optional hint="Chỉ để quản lý lịch — không đăng lên Facebook.">
              <Input id="ce-title" value={title} disabled={locked} onChange={(e) => setTitle(e.target.value)} maxLength={300} />
            </Field>
            <Field htmlFor="ce-caption" label="Caption" hint={`${caption.length}/5000 ký tự`}>
              <Textarea rows={6} id="ce-caption" value={caption} disabled={locked} onChange={(e) => setCaption(e.target.value)} maxLength={5000} />
            </Field>
            {!locked && (
              <CaptionAssistant
                campaign={campaign}
                kind={kind}
                brief={[title, caption].filter(Boolean).join('\n')}
                source={sourceKey}
                onPick={(v) => {
                  setCaption(v.caption + (v.question ? `\n\n${v.question}` : ''));
                  setHashtags(v.hashtags.join(' '));
                  setStyle(v.style);
                }}
                onSpent={ctx.reloadCampaigns}
              />
            )}
            <div className="grid gap-4 sm:grid-cols-2">
              <Field htmlFor="ce-hashtags" label="Hashtag" error={tags > 10 ? 'Tối đa 10 hashtag — nhiều hơn dễ bị coi là spam.' : undefined} hint={`${tags} hashtag`}>
                <Input id="ce-hashtags" value={hashtags} disabled={locked} onChange={(e) => setHashtags(e.target.value)} placeholder="#tuoitho #kyniem" />
              </Field>
              <Field htmlFor="ce-style" label="Phong cách" optional>
                <Select id="ce-style" value={style} disabled={locked} onChange={(e) => setStyle(e.target.value)}>
                  <option value="">—</option>
                  {Object.entries(STYLE_LABEL).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
                </Select>
              </Field>
            </div>
            <Field htmlFor="ce-cta" label="CTA / câu hỏi mời bình luận" optional hint="Ghi lại để phân tích CTA nào hiệu quả.">
              <Input id="ce-cta" value={cta} disabled={locked} onChange={(e) => setCta(e.target.value)} maxLength={500} />
            </Field>
          </>
        )}
        {kind === 'Story' && <p className="text-xs text-fg-muted">Story trên Facebook không có caption — chữ nên nằm trên ảnh (dùng template 9:16 ở Studio nội dung).</p>}

        <div className="grid gap-4 sm:grid-cols-2">
          <Field htmlFor="ce-date" label="Ngày đăng (giờ Việt Nam)" hint={`Chiến dịch ${campaign.startDate.split('-').reverse().slice(0, 2).join('/')} – ${campaign.endDate.split('-').reverse().slice(0, 2).join('/')}`}>
            <Input type="date" id="ce-date" value={date} min={campaign.startDate} max={campaign.endDate} disabled={locked} onChange={(e) => setDate(e.target.value)} />
          </Field>
          <Field htmlFor="ce-time" label="Giờ đăng">
            <div className="flex gap-2">
              <Input type="time" id="ce-time" value={time} disabled={locked} onChange={(e) => setTime(e.target.value)} className="flex-1" />
            </div>
            <div className="mt-1.5 flex flex-wrap gap-1">
              {campaign.timeSlots.map((s) => (
                <button key={s} type="button" disabled={locked} onClick={() => setTime(s)} className={cn('rounded border px-1.5 py-0.5 text-[11px] tabular-nums', time === s ? 'border-accent text-accent' : 'border-line text-fg-muted hover:bg-muted')}>
                  {s}
                </button>
              ))}
            </div>
          </Field>
        </div>

        {kind === 'Story' && (
          <div className="space-y-2 rounded-md border border-line bg-subtle p-3">
            <Checkbox
              checked={manual}
              disabled={locked}
              onChange={setManual}
              label="Tôi sẽ đăng Story này thủ công"
              description="Dùng khi Meta không cho ứng dụng đăng Story lên Page này. Ứng dụng chỉ nhắc giờ và ghi nhận, không tự đăng."
            />
          </div>
        )}

        {kind === 'Story' && (item?.manualPublish || (item?.status === 'Failed' && item.errorKind === 'Unsupported')) && item.status !== 'Published' && (
          <ManualStoryGuide
            assets={selectedAssets}
            sourcePreview={source?.previewUrl ?? item?.sourcePreviewUrl ?? null}
            permalink={permalink}
            setPermalink={setPermalink}
            busy={busy === 'manual'}
            onDone={() => run('manual', () => growth.markManual(campaign.id, item!.id, permalink || undefined), 'Đã ghi nhận đăng thủ công')}
          />
        )}

        <Preview ctx={ctx} kind={kind} caption={caption} hashtags={hashtags} assets={selectedAssets} sourcePreview={source?.previewUrl ?? item?.sourcePreviewUrl ?? null} />
        {needsVideo && !sourceKey && <p className="text-xs text-fg-subtle">Chọn video để lên lịch Reels.</p>}
      </div>
    </Drawer>
  );
}

/** AI caption variants (existing AI provider; campaign AI budget). Never applied without a click. */
function CaptionAssistant({ campaign, kind, brief, source, onPick, onSpent }: { campaign: Campaign; kind: string; brief: string; source: string; onPick: (v: CaptionVariant) => void; onSpent: () => void }) {
  const [styles, setStyles] = useState<string[]>(['Emotional', 'Curiosity', 'Discussion']);
  const [variants, setVariants] = useState<CaptionVariant[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [st, sid] = source ? source.split(':') : ['', ''];

  const generate = async () => {
    setBusy(true);
    setError(null);
    try {
      const r = await growth.captions(campaign.id, { brief, sourceType: st || undefined, sourceId: sid || undefined, styles, variants: styles.length, kind: KIND_LABEL[kind] });
      setVariants(r.variants);
      onSpent();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  if (!open)
    return (
      <Button size="sm" variant="ghost" icon={<Sparkles className="h-3.5 w-3.5" />} onClick={() => setOpen(true)}>
        Viết caption bằng AI
      </Button>
    );
  return (
    <div className="space-y-3 rounded-md border border-line bg-subtle p-3">
      <div className="flex flex-wrap gap-1.5">
        {Object.entries(STYLE_LABEL).map(([k, v]) => (
          <button
            key={k}
            type="button"
            onClick={() => setStyles((s) => (s.includes(k) ? s.filter((x) => x !== k) : [...s, k].slice(0, 7)))}
            className={cn('rounded-full border px-2.5 py-0.5 text-xs', styles.includes(k) ? 'border-accent bg-accent/10 text-accent' : 'border-line text-fg-muted hover:bg-muted')}
          >
            {v}
          </button>
        ))}
      </div>
      <div className="flex items-center gap-2">
        <Button size="sm" variant="primary" icon={<Sparkles className="h-3.5 w-3.5" />} loading={busy} disabled={styles.length === 0 || (!brief.trim() && !source)} onClick={generate}>
          Tạo {styles.length} phiên bản
        </Button>
        <span className="text-xs text-fg-subtle">AI đã dùng {campaign.aiSpentUsd.toFixed(3)} / {campaign.aiBudgetUsd.toFixed(2)} USD</span>
      </div>
      {!brief.trim() && !source && <p className="text-xs text-fg-subtle">Nhập vài dòng nội dung hoặc chọn video để AI có cơ sở viết (AI không bịa thông tin).</p>}
      {error && <p className="text-xs text-danger">{error}</p>}
      {variants && (
        <div className="space-y-2">
          {variants.map((v, i) => (
            <div key={i} className="rounded-md border border-line bg-surface p-3">
              <div className="mb-1 flex items-center justify-between gap-2">
                <Badge tone="info">{STYLE_LABEL[v.style] ?? v.style}</Badge>
                <Button size="sm" variant="secondary" onClick={() => onPick(v)}>Dùng bản này</Button>
              </div>
              <p className="whitespace-pre-wrap text-[13px] text-fg">{v.caption}</p>
              {v.question && <p className="mt-1 text-[13px] text-fg-muted">{v.question}</p>}
              {v.hashtags.length > 0 && <p className="mt-1 text-xs text-accent">{v.hashtags.join(' ')}</p>}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function ManualStoryGuide({ assets, sourcePreview, permalink, setPermalink, busy, onDone }: { assets: Asset[]; sourcePreview: string | null; permalink: string; setPermalink: (v: string) => void; busy: boolean; onDone: () => void }) {
  const file = assets[0]?.url ?? sourcePreview;
  return (
    <Alert tone="info" icon={<Hand className="h-4 w-4" />} title="Đăng Story thủ công">
      <ol className="ml-4 list-decimal space-y-1 text-[13px]">
        <li>Tải ảnh / video Story về máy.</li>
        <li>Mở Meta Business Suite (hoặc ứng dụng Facebook với vai trò Page) → Tạo tin.</li>
        <li>Chọn tệp vừa tải, đăng lên Page.</li>
        <li>Quay lại đây bấm "Đã đăng thủ công" (dán liên kết nếu có).</li>
      </ol>
      <div className="mt-3 flex flex-wrap gap-2">
        {file && (
          <a href={file} download className="inline-flex h-8 items-center gap-1.5 rounded-md border border-line-strong bg-surface px-3 text-[13px] font-medium text-fg hover:bg-muted">
            <Download className="h-3.5 w-3.5" /> Tải tệp
          </a>
        )}
        <a href="https://business.facebook.com/latest/home" target="_blank" rel="noreferrer noopener" className="inline-flex h-8 items-center gap-1.5 rounded-md border border-line-strong bg-surface px-3 text-[13px] font-medium text-fg hover:bg-muted">
          <ExternalLink className="h-3.5 w-3.5" /> Mở Meta Business Suite
        </a>
      </div>
      <div className="mt-3 flex flex-col gap-2 sm:flex-row">
        <Input placeholder="https://www.facebook.com/… (tuỳ chọn)" value={permalink} onChange={(e) => setPermalink(e.target.value)} />
        <Button size="sm" variant="primary" className="h-9 shrink-0" loading={busy} onClick={onDone}>
          Đã đăng thủ công
        </Button>
      </div>
    </Alert>
  );
}

/** What the post will look like (feed card, or a 9:16 Story frame). */
function Preview({ ctx, kind, caption, hashtags, assets, sourcePreview }: { ctx: GrowthCtx; kind: ContentKind; caption: string; hashtags: string; assets: Asset[]; sourcePreview: string | null }) {
  const text = [caption.trim(), hashtags.trim()].filter(Boolean).join('\n\n');
  return (
    <div>
      <div className="mb-2 text-[13px] font-medium text-fg">Xem trước</div>
      {kind === 'Story' ? (
        <div className="mx-auto aspect-[9/16] w-48 overflow-hidden rounded-xl border border-line bg-black shadow-pop">
          {assets[0]?.kind === 'Image' ? (
            <img src={assets[0].url} alt="" className="h-full w-full object-cover" />
          ) : assets[0]?.kind === 'Video' || sourcePreview ? (
            <video src={assets[0]?.url ?? sourcePreview!} muted playsInline controls className="h-full w-full object-cover" />
          ) : (
            <div className="flex h-full items-center justify-center p-4 text-center text-xs text-white/60">Chọn ảnh hoặc video 9:16</div>
          )}
        </div>
      ) : (
        <div className="mx-auto max-w-sm overflow-hidden rounded-lg border border-line bg-surface shadow-card">
          <div className="flex items-center gap-2 px-3 py-2.5">
            <PageAvatar page={ctx.page} size={32} />
            <div className="min-w-0">
              <div className="truncate text-[13px] font-semibold text-fg">{ctx.page?.pageName}</div>
              <div className="text-[11px] text-fg-subtle">Sắp đăng · 🌐</div>
            </div>
          </div>
          {text && <p className="line-clamp-6 whitespace-pre-wrap px-3 pb-2 text-[13px] text-fg">{text}</p>}
          {kind === 'Reel' ? (
            sourcePreview ? <video src={sourcePreview} muted playsInline controls className="aspect-[9/16] max-h-80 w-full bg-black object-contain" /> : <div className="flex aspect-video items-center justify-center bg-muted text-xs text-fg-subtle">Chưa chọn video</div>
          ) : assets.length > 0 ? (
            <div className={cn('grid gap-0.5', assets.length > 1 && 'grid-cols-2')}>
              {assets.slice(0, 4).map((a, i) => (
                <div key={a.id} className="relative">
                  <img src={a.url} alt="" className={cn('w-full object-cover', assets.length > 1 ? 'aspect-square' : 'max-h-96')} />
                  {i === 3 && assets.length > 4 && <span className="absolute inset-0 flex items-center justify-center bg-black/50 text-lg font-semibold text-white">+{assets.length - 4}</span>}
                </div>
              ))}
            </div>
          ) : null}
        </div>
      )}
    </div>
  );
}
