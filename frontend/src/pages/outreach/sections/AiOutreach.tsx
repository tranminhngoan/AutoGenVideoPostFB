import { useState } from 'react';
import { BookmarkPlus, Library, Sparkles, Trash2, Wand2 } from 'lucide-react';
import { Alert, Badge, Button, Card, CardHeader, EmptyState, Field, Input, Segmented, Select, Skeleton, Textarea } from '../../../components/ui';
import { useConfirm, useToast } from '../../../components/feedback';
import { Draft, outreach } from '../outreachApi';
import { CopyButton, fmtDate, useLoad } from '../shared';

const STYLE_LABEL: Record<string, string> = { friendly: 'Thân thiện', short: 'Ngắn gọn', funny: 'Hài hước', custom: 'Tự viết' };

function SimilarityBadge({ d }: { d: Draft }) {
  if (d.similarity >= 70) return <Badge tone="danger" dot>Trùng {d.similarity}% với mẫu đã có</Badge>;
  if (d.similarity >= 40) return <Badge tone="warning" dot>Giống {d.similarity}%</Badge>;
  return <Badge tone="success" dot>Mới · giống {d.similarity}%</Badge>;
}

/** How the text looks once posted in a Group (preview). */
function Preview({ text }: { text: string }) {
  return (
    <div className="rounded-lg border border-line bg-subtle p-3">
      <div className="mb-2 flex items-center gap-2">
        <span className="h-7 w-7 rounded-full bg-gradient-to-br from-rose-500 to-fuchsia-600" />
        <div className="leading-tight">
          <p className="text-xs font-semibold text-fg">Page phụ của bạn</p>
          <p className="text-[10px] text-fg-subtle">Vừa xong · Group</p>
        </div>
      </div>
      <p className="whitespace-pre-wrap text-sm text-fg">{text.split(/(https?:\/\/\S+)/).map((part, i) => (/^https?:\/\//.test(part) ? <span key={i} className="text-accent">{part}</span> : part))}</p>
    </div>
  );
}

export function AiSection() {
  const toast = useToast();
  const confirm = useConfirm();
  const { data: groups } = useLoad(() => outreach.groups());
  const { data: library, setData: setLibrary, loading } = useLoad(() => outreach.contents());
  const [style, setStyle] = useState('friendly');
  const [topic, setTopic] = useState('');
  const [groupId, setGroupId] = useState('');
  const [count, setCount] = useState(3);
  const [extra, setExtra] = useState('');
  const [busy, setBusy] = useState(false);
  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [manual, setManual] = useState('');
  const [manualCheck, setManualCheck] = useState<Draft | null>(null);
  const [filter, setFilter] = useState('all');

  const write = async () => {
    setBusy(true);
    try {
      setDrafts(await outreach.write({ style, topic: topic || undefined, groupId: groupId || undefined, count, extra: extra || undefined }));
    } catch (e) {
      toast.error('AI chưa viết được', (e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const save = async (text: string, origin: 'ai' | 'manual', st = style) => {
    try {
      const c = await outreach.saveContent({ text, style: st, topic: topic || null, origin });
      setLibrary((l) => [c, ...(l ?? [])]);
      toast.success('Đã lưu vào thư viện');
      return true;
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
      return false;
    }
  };

  const shown = (library ?? []).filter((c) => filter === 'all' || c.style === filter || c.origin === filter);

  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      <div className="space-y-4">
        <Card>
          <CardHeader title={<span className="flex items-center gap-2"><Sparkles className="h-4 w-4 text-rose-500" /> AI viết lời mời</span>}
            description="Lời mời ghé Page chính, kêu gọi Follow, xem video, Like / bình luận nếu thấy hay. Link /cheo-uy-tinh tự gắn ở cuối." />
          <div className="grid gap-3 p-4 pt-0 sm:grid-cols-2">
            <Field label="Phong cách" className="sm:col-span-2">
              <Segmented value={style} onChange={setStyle} options={[
                { value: 'friendly', label: 'Thân thiện' }, { value: 'short', label: 'Ngắn gọn' }, { value: 'funny', label: 'Hài hước' },
              ]} />
            </Field>
            <Field label="Viết cho Group" optional>
              <Select value={groupId} onChange={(e) => setGroupId(e.target.value)}>
                <option value="">— Không chọn —</option>
                {(groups ?? []).filter((g) => g.fit !== 'unfit').map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
              </Select>
            </Field>
            <Field label="Chủ đề Group" optional>
              <Input value={topic} onChange={(e) => setTopic(e.target.value)} placeholder="hoài niệm, giải trí…" />
            </Field>
            <Field label="Số phiên bản">
              <Select value={count} onChange={(e) => setCount(Number(e.target.value))}>
                {[1, 2, 3, 4, 5].map((n) => <option key={n} value={n}>{n}</option>)}
              </Select>
            </Field>
            <Field label="Yêu cầu thêm" optional>
              <Input value={extra} onChange={(e) => setExtra(e.target.value)} placeholder="VD: nhắc video Tết xưa" />
            </Field>
            <div className="sm:col-span-2">
              <Button variant="primary" icon={<Wand2 className="h-4 w-4" />} loading={busy} onClick={write}>AI viết {count} phiên bản</Button>
            </div>
          </div>
        </Card>

        {drafts.length > 0 && (
          <div className="space-y-3">
            <p className="px-1 text-xs font-semibold uppercase tracking-wide text-fg-subtle">Bản nháp — xem trước, sửa, copy</p>
            {drafts.map((d, i) => (
              <Card key={i} className="space-y-2.5 p-3.5">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="text-xs font-medium text-fg-muted">Phiên bản {i + 1}</span>
                  <SimilarityBadge d={d} />
                </div>
                <Textarea rows={4} value={d.text} onChange={(e) => setDrafts((list) => list.map((x, j) => (j === i ? { ...x, text: e.target.value } : x)))} />
                <Preview text={d.text} />
                <div className="flex flex-wrap gap-2">
                  <CopyButton text={d.text} />
                  <Button size="sm" variant="secondary" icon={<BookmarkPlus className="h-3.5 w-3.5" />} disabled={d.similarity >= 90}
                    onClick={async () => { if (await save(d.text, 'ai')) setDrafts((list) => list.filter((_, j) => j !== i)); }}>
                    Lưu vào thư viện
                  </Button>
                </div>
              </Card>
            ))}
          </div>
        )}

        <Card>
          <CardHeader title="Tự viết" description="Viết tay rồi kiểm tra trùng lặp với thư viện trước khi lưu." />
          <div className="space-y-2.5 p-4 pt-0">
            <Textarea rows={4} value={manual} onChange={(e) => { setManual(e.target.value); setManualCheck(null); }}
              placeholder="Mọi người ghé ủng hộ Page của mình nhé ❤️ …" />
            {manualCheck && <SimilarityBadge d={manualCheck} />}
            {manualCheck?.similarTo && manualCheck.similarity >= 40 && <p className="line-clamp-2 text-xs text-fg-subtle">Giống: “{manualCheck.similarTo}”</p>}
            <div className="flex flex-wrap gap-2">
              <Button size="sm" variant="secondary" disabled={!manual.trim()} onClick={async () => {
                try { setManualCheck(await outreach.check(manual)); } catch (e) { toast.error('Không kiểm tra được', (e as Error).message); }
              }}>Kiểm tra trùng lặp</Button>
              <CopyButton text={manual} />
              <Button size="sm" variant="primary" disabled={!manual.trim()} onClick={async () => { if (await save(manual, 'manual', 'custom')) { setManual(''); setManualCheck(null); } }}>
                Lưu vào thư viện
              </Button>
            </div>
          </div>
        </Card>
      </div>

      <Card className="self-start">
        <CardHeader title={<span className="flex items-center gap-2"><Library className="h-4 w-4" /> Thư viện nội dung</span>}
          description="Mẫu đã duyệt — bấm Copy rồi tự đăng vào Group. Hệ thống không tự đăng."
          actions={
            <Select size="sm" value={filter} onChange={(e) => setFilter(e.target.value)} className="w-auto">
              <option value="all">Tất cả</option>
              <option value="friendly">Thân thiện</option>
              <option value="short">Ngắn gọn</option>
              <option value="funny">Hài hước</option>
              <option value="manual">Tự viết</option>
            </Select>
          } />
        <div className="space-y-2 p-4 pt-0">
          {loading && !library ? <Skeleton className="h-32 w-full" /> : shown.length === 0 ? (
            <EmptyState icon={<Library />} title="Thư viện trống" description="Lưu bản nháp AI hoặc nội dung tự viết vào đây." />
          ) : shown.map((c) => (
            <div key={c.id} className="rounded-lg border border-line p-3">
              <p className="whitespace-pre-wrap text-sm text-fg">{c.text}</p>
              <div className="mt-2 flex flex-wrap items-center gap-1.5 text-xs text-fg-muted">
                <Badge>{STYLE_LABEL[c.style] ?? c.style}</Badge>
                <Badge tone={c.origin === 'ai' ? 'info' : 'neutral'}>{c.origin === 'ai' ? 'AI' : 'Tự viết'}</Badge>
                {c.topic && <Badge>{c.topic}</Badge>}
                <span>· đã copy {c.copiedCount} lần · {fmtDate(c.createdAtUtc)}</span>
                <span className="ml-auto flex gap-1">
                  <CopyButton text={c.text} onCopied={() => {
                    outreach.copied(c.id).catch(() => undefined);
                    setLibrary((l) => l?.map((x) => (x.id === c.id ? { ...x, copiedCount: x.copiedCount + 1 } : x)) ?? null);
                  }} />
                  <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} aria-label="Xoá" onClick={async () => {
                    if (!(await confirm({ title: 'Xoá mẫu này khỏi thư viện?', confirmLabel: 'Xoá', danger: true }))) return;
                    try { await outreach.deleteContent(c.id); setLibrary((l) => l?.filter((x) => x.id !== c.id) ?? null); }
                    catch (e) { toast.error('Chưa xoá được', (e as Error).message); }
                  }} />
                </span>
              </div>
            </div>
          ))}
          <Alert tone="neutral" className="mt-2">Không đăng hàng loạt cùng một nội dung vào nhiều Group — Facebook có thể coi là spam. Mỗi Group nên dùng một phiên bản khác.</Alert>
        </div>
      </Card>
    </div>
  );
}
