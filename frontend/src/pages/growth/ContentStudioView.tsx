import { useState } from 'react';
import { CalendarPlus, Copy, ImageIcon, Sparkles, Wand2 } from 'lucide-react';
import { Alert, Badge, Button, Field, Select, Tabs, Textarea, cn } from '../../components/ui';
import { useToast } from '../../components/feedback';
import { navigate } from '../../lib/router';
import { growth, type Asset, type CaptionVariant, type RepurposeResult, type SourceType } from '../../services/growth';
import { STYLE_LABEL } from '../../lib/growthMath';
import { ContentEditor, type ContentDefaults } from './ContentEditor';
import { ImageTemplateStudio, type TemplateDraft } from './ImageTemplateStudio';
import { LoadError, NoCampaign, Section, useLoad, type GrowthCtx } from './shared';

type Tab = 'repurpose' | 'captions' | 'templates';

/** One video → many posts (text by AI, pictures from templates), captions in several styles, image templates. */
export function ContentStudioView({ ctx }: { ctx: GrowthCtx }) {
  const campaign = ctx.campaign;
  const [tab, setTab] = useState<Tab>('repurpose');
  const [editor, setEditor] = useState<ContentDefaults | null>(null);
  const [draft, setDraft] = useState<TemplateDraft | undefined>();
  const [pendingCaption, setPendingCaption] = useState<string>('');
  const toast = useToast();

  if (!campaign) return <NoCampaign ctx={ctx} action={ctx.page && <Button variant="primary" onClick={() => navigate('/growth/campaigns')}>Tạo chiến dịch</Button>} />;
  const closed = campaign.status === 'Completed' || campaign.status === 'Cancelled';

  const toTemplate = (d: TemplateDraft, caption = '') => {
    setDraft({ ...d });
    setPendingCaption(caption);
    setTab('templates');
  };
  const schedule = (d: ContentDefaults) => (closed ? toast.error('Chiến dịch đã kết thúc') : setEditor(d));

  return (
    <div className="space-y-4">
      <Tabs
        items={[
          { id: 'repurpose', label: 'Từ một video' },
          { id: 'captions', label: 'Caption AI' },
          { id: 'templates', label: 'Template ảnh' },
        ]}
        value={tab}
        onChange={setTab}
      />
      {tab === 'repurpose' && <Repurpose ctx={ctx} onTemplate={toTemplate} onSchedule={schedule} />}
      {tab === 'captions' && <Captions ctx={ctx} onSchedule={schedule} />}
      {tab === 'templates' && (
        <Section title="Image Template Studio" description="Tạo ảnh từ template có sẵn — không cần AI tạo ảnh.">
          <ImageTemplateStudio
            campaign={campaign}
            pageName={ctx.page?.pageName ?? campaign.pageName}
            pictureUrl={ctx.page?.pictureUrl}
            initial={draft}
            onSaved={(a: Asset) => {
              toast.success('Đã lưu ảnh vào kho của chiến dịch');
              schedule({ kind: draft?.template === 'story' ? 'Story' : 'Photo', assetIds: [a.id], caption: pendingCaption, title: draft?.headline });
            }}
          />
        </Section>
      )}
      {editor && (
        <ContentEditor ctx={ctx} campaign={campaign} defaults={editor} open onClose={() => setEditor(null)} onSaved={() => { setEditor(null); ctx.reloadCampaigns(); }} />
      )}
    </div>
  );
}

function Repurpose({ ctx, onTemplate, onSchedule }: { ctx: GrowthCtx; onTemplate: (d: TemplateDraft, caption?: string) => void; onSchedule: (d: ContentDefaults) => void }) {
  const campaign = ctx.campaign!;
  const toast = useToast();
  const sources = useLoad(() => growth.sources(campaign.id), [campaign.id], ctx.onNeedKey);
  const [key, setKey] = useState('');
  const [result, setResult] = useState<RepurposeResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [type, id] = key ? (key.split(':') as [SourceType, string]) : (['', ''] as const);
  const selected = sources.data?.find((s) => `${s.type}:${s.id}` === key);

  const run = async () => {
    setBusy(true);
    setError(null);
    try {
      setResult(await growth.repurpose(campaign.id, type as SourceType, id));
      ctx.reloadCampaigns();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  const copy = (t: string) => navigator.clipboard?.writeText(t).then(() => toast.success('Đã chép')).catch(() => toast.error('Không chép được'));

  return (
    <div className="space-y-4">
      <Section title="Chọn video nguồn" description="AI đọc tiêu đề / kịch bản / lời bình của video (không xem video, không cắt hay tạo video mới) để đề xuất 2 bài ảnh, 3 Story, 3 caption, 5 tiêu đề, hashtag, câu hỏi và lời mời theo dõi.">
        {sources.error && <LoadError message={sources.error} onRetry={sources.reload} />}
        <div className="flex flex-col gap-2 sm:flex-row">
          <Select value={key} onChange={(e) => { setKey(e.target.value); setResult(null); }} className="flex-1" disabled={sources.loading}>
            <option value="">{sources.loading ? 'Đang tải kho video…' : sources.data?.length ? '— Chọn video —' : 'Chưa có video hoàn thành'}</option>
            {(sources.data ?? []).map((s) => (
              <option key={`${s.type}:${s.id}`} value={`${s.type}:${s.id}`}>[{s.typeLabel}] {s.title}</option>
            ))}
          </Select>
          <Button variant="primary" icon={<Wand2 className="h-4 w-4" />} loading={busy} disabled={!key} onClick={run}>Tạo nội dung phụ</Button>
        </div>
        {selected?.previewUrl && <video src={selected.previewUrl} controls preload="metadata" className="mt-3 max-h-60 rounded-md border border-line bg-black" />}
        <p className="mt-2 text-xs text-fg-subtle">Ngân sách AI chiến dịch: {campaign.aiSpentUsd.toFixed(3)} / {campaign.aiBudgetUsd.toFixed(2)} USD.</p>
        {error && <Alert tone="danger" className="mt-3">{error}</Alert>}
      </Section>

      {result && (
        <div className="grid gap-4 lg:grid-cols-2">
          <Section title="2 bài đăng ảnh">
            <div className="space-y-3">
              {result.photoPosts.map((p, i) => (
                <div key={i} className="rounded-md border border-line p-3">
                  <div className="text-[13px] font-semibold text-fg">{p.headline}</div>
                  <div className="text-[13px] text-fg-muted">{p.body}</div>
                  <p className="mt-2 whitespace-pre-wrap text-[13px] text-fg">{p.caption}</p>
                  <div className="mt-2 flex gap-2">
                    <Button size="sm" icon={<ImageIcon className="h-3.5 w-3.5" />} onClick={() => onTemplate({ template: 'quote', headline: p.headline, body: p.body }, p.caption)}>Tạo ảnh từ template</Button>
                  </div>
                </div>
              ))}
            </div>
          </Section>
          <Section title="3 nội dung Story">
            <div className="space-y-2">
              {result.stories.map((s, i) => (
                <div key={i} className="flex items-start justify-between gap-3 rounded-md border border-line p-3">
                  <div>
                    <div className="text-[13px] text-fg">{s.text}</div>
                    {s.sticker && <div className="mt-0.5 text-xs text-fg-subtle">Gợi ý sticker: {s.sticker}</div>}
                  </div>
                  <Button size="sm" onClick={() => onTemplate({ template: 'story', headline: s.text, body: '' })}>Ảnh 9:16</Button>
                </div>
              ))}
            </div>
          </Section>
          <Section title="3 caption">
            <div className="space-y-2">
              {result.captions.map((c, i) => (
                <CaptionCard key={i} v={c} onCopy={copy} onSchedule={() => onSchedule({ kind: 'Reel', sourceType: type as SourceType, sourceId: id, caption: c.caption, hashtags: c.hashtags.join(' '), style: c.style })} scheduleLabel="Lên lịch Reels" />
              ))}
            </div>
          </Section>
          <Section title="Tiêu đề, hashtag, tương tác">
            <div className="space-y-4 text-[13px]">
              <div>
                <div className="mb-1 text-xs font-medium uppercase tracking-wide text-fg-subtle">5 tiêu đề thay thế</div>
                <ol className="ml-4 list-decimal space-y-1 text-fg">{result.titles.map((t, i) => <li key={i}>{t}</li>)}</ol>
              </div>
              <div>
                <div className="mb-1 text-xs font-medium uppercase tracking-wide text-fg-subtle">Hashtag</div>
                <div className="flex flex-wrap gap-1.5">{result.hashtags.map((h) => <Badge key={h} tone="info">{h}</Badge>)}</div>
              </div>
              <div>
                <div className="mb-1 text-xs font-medium uppercase tracking-wide text-fg-subtle">Câu hỏi khuyến khích bình luận</div>
                <p className="text-fg">{result.commentQuestion}</p>
                <Button size="sm" variant="ghost" className="mt-1" icon={<CalendarPlus className="h-3.5 w-3.5" />} onClick={() => onSchedule({ kind: 'Interactive', caption: result.commentQuestion, cta: result.commentQuestion, style: 'Discussion' })}>Lên lịch bài tương tác</Button>
              </div>
              <div>
                <div className="mb-1 text-xs font-medium uppercase tracking-wide text-fg-subtle">Lời mời theo dõi Page</div>
                <p className="text-fg">{result.followCta}</p>
                <Button size="sm" variant="ghost" className="mt-1" icon={<Copy className="h-3.5 w-3.5" />} onClick={() => copy(result.followCta)}>Chép</Button>
              </div>
              <p className="text-xs text-fg-subtle">Chi phí AI lần này: {result.costUsd.toFixed(4)} USD.</p>
            </div>
          </Section>
        </div>
      )}
    </div>
  );
}

function CaptionCard({ v, onCopy, onSchedule, scheduleLabel }: { v: CaptionVariant; onCopy: (t: string) => void; onSchedule: () => void; scheduleLabel: string }) {
  const full = [v.caption, v.question, v.hashtags.join(' ')].filter(Boolean).join('\n\n');
  return (
    <div className="rounded-md border border-line p-3">
      <Badge tone="info">{STYLE_LABEL[v.style] ?? v.style}</Badge>
      <p className="mt-2 whitespace-pre-wrap text-[13px] text-fg">{v.caption}</p>
      {v.question && <p className="mt-1 text-[13px] text-fg-muted">{v.question}</p>}
      {v.hashtags.length > 0 && <p className="mt-1 text-xs text-accent">{v.hashtags.join(' ')}</p>}
      <div className="mt-2 flex flex-wrap gap-2">
        <Button size="sm" variant="ghost" icon={<Copy className="h-3.5 w-3.5" />} onClick={() => onCopy(full)}>Chép</Button>
        <Button size="sm" icon={<CalendarPlus className="h-3.5 w-3.5" />} onClick={onSchedule}>{scheduleLabel}</Button>
      </div>
    </div>
  );
}

function Captions({ ctx, onSchedule }: { ctx: GrowthCtx; onSchedule: (d: ContentDefaults) => void }) {
  const campaign = ctx.campaign!;
  const toast = useToast();
  const [brief, setBrief] = useState('');
  const [styles, setStyles] = useState<string[]>(['Emotional', 'Nostalgic', 'Story', 'Curiosity', 'Discussion']);
  const [kind, setKind] = useState<'Photo' | 'Interactive' | 'Album'>('Photo');
  const [variants, setVariants] = useState<CaptionVariant[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const run = async () => {
    setBusy(true);
    setError(null);
    try {
      const r = await growth.captions(campaign.id, { brief, styles, variants: styles.length, kind });
      setVariants(r.variants);
      ctx.reloadCampaigns();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)]">
      <Section title="AI Caption Generator" description="Nhiều phiên bản để chọn. AI chỉ dựa trên nội dung bạn nhập: không bịa, không giật tít, tối đa 5 hashtag.">
        <div className="space-y-4">
          <Field label="Bài viết nói về gì?" hint="Càng cụ thể càng tốt (chi tiết thật, cảm xúc, kỷ niệm…).">
            <Textarea rows={5} value={brief} onChange={(e) => setBrief(e.target.value)} placeholder="Ví dụ: ảnh chiếc xe đạp Phượng Hoàng cũ của bố, hồi những năm 90 cả nhà đi chợ Tết bằng chiếc xe này…" />
          </Field>
          <Field label="Loại bài">
            <Select value={kind} onChange={(e) => setKind(e.target.value as typeof kind)}>
              <option value="Photo">Bài ảnh</option>
              <option value="Album">Bài nhiều ảnh</option>
              <option value="Interactive">Bài tương tác</option>
            </Select>
          </Field>
          <Field label="Phong cách">
            <div className="flex flex-wrap gap-1.5">
              {Object.entries(STYLE_LABEL).map(([k, v]) => (
                <button key={k} type="button" onClick={() => setStyles((s) => (s.includes(k) ? s.filter((x) => x !== k) : [...s, k]))} className={cn('rounded-full border px-2.5 py-1 text-xs', styles.includes(k) ? 'border-accent bg-accent/10 text-accent' : 'border-line text-fg-muted hover:bg-muted')}>
                  {v}
                </button>
              ))}
            </div>
          </Field>
          <Button variant="primary" icon={<Sparkles className="h-4 w-4" />} loading={busy} disabled={brief.trim().length < 10 || styles.length === 0} onClick={run}>
            Viết {styles.length} phiên bản
          </Button>
          {error && <Alert tone="danger">{error}</Alert>}
        </div>
      </Section>
      <Section title="Kết quả">
        {!variants ? (
          <p className="py-8 text-center text-[13px] text-fg-subtle">Các phiên bản caption sẽ hiện ở đây.</p>
        ) : (
          <div className="space-y-2">
            {variants.map((v, i) => (
              <CaptionCard
                key={i}
                v={v}
                onCopy={(t) => navigator.clipboard?.writeText(t).then(() => toast.success('Đã chép'))}
                onSchedule={() => onSchedule({ kind, caption: v.caption + (v.question ? `\n\n${v.question}` : ''), hashtags: v.hashtags.join(' '), style: v.style, cta: v.question ?? undefined })}
                scheduleLabel="Đưa vào lịch"
              />
            ))}
          </div>
        )}
      </Section>
    </div>
  );
}
