import { useState } from 'react';
import { ExternalLink, Plus, Sparkles, Trash2 } from 'lucide-react';
import { Alert, Badge, Button, Field, Input, Modal, Select, Textarea } from '../../components/ui';
import { useConfirm, useToast } from '../../components/feedback';
import { growth, type Competitor, type CompetitorAnalysis } from '../../services/growth';
import { dm, num, vnToday } from '../../lib/growthMath';
import { BarList } from './charts';
import { LoadError, LoadingBlock, Section, useLoad, type GrowthCtx } from './shared';

const FORMATS = ['Reel', 'Photo', 'Album', 'Story', 'Text', 'Live', 'Video'];

/** Up to 5 competitor Pages, followed by hand: no scraping, no access to their insights. */
export function CompetitorsView({ ctx }: { ctx: GrowthCtx }) {
  const page = ctx.page;
  const toast = useToast();
  const confirm = useConfirm();
  const { data, error, loading, reload, setData } = useLoad(() => (page ? growth.competitors(page.id) : Promise.resolve([])), [page?.id], ctx.onNeedKey);
  const [adding, setAdding] = useState(false);
  const [noteFor, setNoteFor] = useState<Competitor | null>(null);
  const [analysis, setAnalysis] = useState<CompetitorAnalysis | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  if (!page) return <p className="text-[13px] text-fg-muted">Chọn một Page ở phía trên.</p>;
  if (loading && !data) return <LoadingBlock rows={2} />;
  if (error) return <LoadError message={error} onRetry={reload} />;
  const list = data ?? [];

  const analyze = async (ai: boolean) => {
    setBusy(ai ? 'ai' : 'plain');
    try {
      setAnalysis(await growth.analyzeCompetitors(page.id, ctx.campaign?.id, ai));
      if (ai) ctx.reloadCampaigns();
    } catch (e) {
      toast.error('Không phân tích được', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="space-y-4">
      <Alert tone="info" title="Chỉ dùng dữ liệu công khai do bạn nhập">
        Công cụ không thu thập (scrape) dữ liệu Facebook và không truy cập Insights của Page khác. Hãy nhập số liệu công khai bạn nhìn thấy (follower, bài nổi bật, tần suất đăng).
      </Alert>
      <Section
        title={`Đối thủ (${list.length}/5)`}
        actions={<Button size="sm" variant="primary" icon={<Plus className="h-3.5 w-3.5" />} disabled={list.length >= 5} onClick={() => setAdding(true)}>Thêm đối thủ</Button>}
      >
        {list.length === 0 ? (
          <p className="py-6 text-center text-[13px] text-fg-subtle">Chưa có đối thủ nào.</p>
        ) : (
          <div className="grid gap-3 md:grid-cols-2">
            {list.map((c) => (
              <div key={c.id} className="rounded-md border border-line p-3">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <a href={c.url} target="_blank" rel="noreferrer noopener" className="inline-flex items-center gap-1 text-[14px] font-medium text-fg hover:underline">
                      {c.name} <ExternalLink className="h-3 w-3 text-fg-subtle" />
                    </a>
                    <div className="text-xs text-fg-subtle">{c.niche || 'Chưa ghi lĩnh vực'}</div>
                  </div>
                  <Button
                    size="sm"
                    variant="danger-soft"
                    aria-label="Xoá đối thủ"
                    icon={<Trash2 className="h-3.5 w-3.5" />}
                    onClick={async () => {
                      if (!(await confirm({ title: `Xoá ${c.name}?`, description: 'Các ghi chép về Page này cũng bị xoá.', danger: true, confirmLabel: 'Xoá' }))) return;
                      try {
                        await growth.deleteCompetitor(c.id, page.id);
                        setData(list.filter((x) => x.id !== c.id));
                      } catch (e) {
                        toast.error('Không xoá được', (e as Error).message);
                      }
                    }}
                  />
                </div>
                <div className="mt-2 flex gap-4 text-[13px]">
                  <span><span className="text-fg-subtle">Follower:</span> <b className="tabular-nums">{num(c.latestFollowers)}</b></span>
                  <span><span className="text-fg-subtle">Bài/tuần:</span> <b className="tabular-nums">{num(c.latestPostsPerWeek)}</b></span>
                  <span className="text-fg-subtle">{c.snapshots.length} ghi chép</span>
                </div>
                {c.notes && <p className="mt-1 line-clamp-2 text-xs text-fg-muted">{c.notes}</p>}
                {c.snapshots.slice(0, 3).map((s) => (
                  <div key={s.id} className="mt-1.5 truncate text-xs text-fg-muted">
                    {dm(s.date)} · {s.format && <Badge>{s.format}</Badge>} {s.postTitle ?? s.note ?? ''} {s.reactions != null && `· ${num(s.reactions)} cảm xúc`}
                  </div>
                ))}
                <Button size="sm" variant="ghost" className="mt-2" icon={<Plus className="h-3.5 w-3.5" />} onClick={() => setNoteFor(c)}>Ghi chép</Button>
              </div>
            ))}
          </div>
        )}
      </Section>

      <Section
        title="Phân tích"
        description="Tổng hợp từ các ghi chép của bạn."
        actions={
          <>
            <Button size="sm" variant="ghost" loading={busy === 'plain'} disabled={list.length === 0} onClick={() => analyze(false)}>Tổng hợp</Button>
            <Button size="sm" icon={<Sparkles className="h-3.5 w-3.5" />} loading={busy === 'ai'} disabled={list.length === 0 || !ctx.campaign} onClick={() => analyze(true)}>Phân tích bằng AI</Button>
          </>
        }
      >
        {!analysis ? (
          <p className="text-[13px] text-fg-subtle">Bấm "Tổng hợp" để xem chủ đề, kiểu nội dung, tần suất đăng và bài tương tác tốt.</p>
        ) : analysis.observations === 0 ? (
          <p className="text-[13px] text-fg-subtle">Chưa có ghi chép nào.</p>
        ) : (
          <div className="grid gap-5 lg:grid-cols-2">
            <div>
              <div className="mb-2 text-[13px] font-medium">Kiểu nội dung (số bài ghi nhận)</div>
              <BarList items={analysis.formats.map((f) => ({ label: f.label, value: f.value }))} empty="Chưa ghi kiểu nội dung." />
            </div>
            <div>
              <div className="mb-2 text-[13px] font-medium">Tần suất đăng (bài/tuần)</div>
              <BarList items={analysis.postingFrequency.map((f) => ({ label: f.label, value: f.value }))} empty="Chưa ghi tần suất." />
            </div>
            <div>
              <div className="mb-2 text-[13px] font-medium">Chủ đề phổ biến</div>
              <div className="flex flex-wrap gap-1.5">{analysis.topics.length ? analysis.topics.map((t) => <Badge key={t.label}>{t.label} · {t.value}</Badge>) : <span className="text-xs text-fg-subtle">Chưa ghi chủ đề.</span>}</div>
            </div>
            <div>
              <div className="mb-2 text-[13px] font-medium">Bài tương tác tốt</div>
              <ul className="space-y-1 text-[13px]">
                {analysis.topPosts.map((p) => (
                  <li key={p.id} className="truncate">
                    {p.postUrl ? <a className="text-accent hover:underline" href={p.postUrl} target="_blank" rel="noreferrer noopener">{p.postTitle || p.postUrl}</a> : p.postTitle}
                    <span className="text-xs text-fg-subtle"> · {p.note}</span>
                  </li>
                ))}
              </ul>
            </div>
            {analysis.aiNotes.length > 0 && (
              <div className="lg:col-span-2">
                <div className="mb-2 text-[13px] font-medium">Nhận định của AI</div>
                <ul className="space-y-1.5 text-[13px]">{analysis.aiNotes.map((n, i) => <li key={i} className="flex gap-2"><span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-accent" />{n}</li>)}</ul>
              </div>
            )}
          </div>
        )}
      </Section>

      <AddCompetitor open={adding} pageId={page.id} onClose={() => setAdding(false)} onAdded={(c) => { setData([...list, c]); setAdding(false); }} />
      {noteFor && <AddNote competitor={noteFor} pageId={page.id} onClose={() => setNoteFor(null)} onSaved={(c) => { setData(list.map((x) => (x.id === c.id ? c : x))); setNoteFor(null); }} />}
    </div>
  );
}

function AddCompetitor({ open, pageId, onClose, onAdded }: { open: boolean; pageId: string; onClose: () => void; onAdded: (c: Competitor) => void }) {
  const [f, setF] = useState({ name: '', url: '', niche: '', notes: '' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      onAdded(await growth.createCompetitor({ facebookAccountId: pageId, ...f }));
      setF({ name: '', url: '', niche: '', notes: '' });
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal open={open} onClose={onClose} title="Thêm Page đối thủ" footer={<Button variant="primary" loading={busy} disabled={!f.name.trim() || !f.url.trim()} onClick={save}>Thêm</Button>}>
      <div className="space-y-3">
        {error && <Alert tone="danger">{error}</Alert>}
        <Field label="Tên Page"><Input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
        <Field label="URL" hint="https://www.facebook.com/…"><Input value={f.url} onChange={(e) => setF({ ...f, url: e.target.value })} /></Field>
        <Field label="Lĩnh vực" optional><Input value={f.niche} onChange={(e) => setF({ ...f, niche: e.target.value })} /></Field>
        <Field label="Ghi chú" optional><Textarea rows={2} value={f.notes} onChange={(e) => setF({ ...f, notes: e.target.value })} /></Field>
      </div>
    </Modal>
  );
}

function AddNote({ competitor, pageId, onClose, onSaved }: { competitor: Competitor; pageId: string; onClose: () => void; onSaved: (c: Competitor) => void }) {
  const [f, setF] = useState({ date: vnToday(), followers: '', postsPerWeek: '', postTitle: '', postUrl: '', format: '', topic: '', reactions: '', comments: '', shares: '', views: '', note: '' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const n = (s: string) => (s.trim() === '' ? null : Number(s.replace(/[.,\s]/g, '')));
  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      onSaved(await growth.addSnapshot(competitor.id, pageId, {
        date: f.date, followers: n(f.followers), postsPerWeek: n(f.postsPerWeek), postTitle: f.postTitle || null, postUrl: f.postUrl || null,
        format: f.format || null, topic: f.topic || null, reactions: n(f.reactions), comments: n(f.comments), shares: n(f.shares), views: n(f.views), note: f.note || null,
      }));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  const inp = (k: keyof typeof f, label: string, numeric = false) => (
    <Field label={label} optional><Input inputMode={numeric ? 'numeric' : undefined} value={f[k]} onChange={(e) => setF({ ...f, [k]: e.target.value })} /></Field>
  );
  return (
    <Modal open onClose={onClose} title={`Ghi chép: ${competitor.name}`} description="Số liệu công khai bạn thấy trên Page của họ." size="lg" footer={<Button variant="primary" loading={busy} onClick={save}>Lưu</Button>}>
      <div className="space-y-3">
        {error && <Alert tone="danger">{error}</Alert>}
        <div className="grid gap-3 sm:grid-cols-3">
          <Field label="Ngày"><Input type="date" max={vnToday()} value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} /></Field>
          {inp('followers', 'Follower', true)}
          {inp('postsPerWeek', 'Bài / tuần', true)}
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          {inp('postTitle', 'Bài nổi bật (tiêu đề)')}
          {inp('postUrl', 'URL bài')}
          <Field label="Kiểu nội dung" optional>
            <Select value={f.format} onChange={(e) => setF({ ...f, format: e.target.value })}>
              <option value="">—</option>
              {FORMATS.map((x) => <option key={x}>{x}</option>)}
            </Select>
          </Field>
          {inp('topic', 'Chủ đề')}
        </div>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {inp('reactions', 'Cảm xúc', true)}
          {inp('comments', 'Bình luận', true)}
          {inp('shares', 'Chia sẻ', true)}
          {inp('views', 'Lượt xem', true)}
        </div>
        <Field label="Ghi chú" optional><Textarea rows={2} value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} /></Field>
      </div>
    </Modal>
  );
}
