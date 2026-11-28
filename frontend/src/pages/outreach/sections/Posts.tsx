import { useState } from 'react';
import { Check, ExternalLink, FileText, Plus, SkipForward, Sparkles, Trash2, Undo2 } from 'lucide-react';
import { Alert, Badge, Button, Card, CardHeader, EmptyState, Field, Input, Select, Skeleton, Tabs, Textarea } from '../../../components/ui';
import { useConfirm, useToast } from '../../../components/feedback';
import { outreach, PostTask } from '../outreachApi';
import { CopyButton, fmtDateTime, useLoad } from '../shared';

type Tab = 'todo' | 'done' | 'skipped';

function TaskCard({ t, onChange, onRemove }: { t: PostTask; onChange: (t: PostTask) => void; onRemove: (t: PostTask) => void }) {
  const toast = useToast();
  const [options, setOptions] = useState<string[]>([]);
  const [busy, setBusy] = useState<string | null>(null);
  const [comment, setComment] = useState(t.suggestedComment ?? '');

  const save = async (patch: Partial<PostTask>) => {
    setBusy(patch.status ?? 'save');
    try {
      onChange(await outreach.editPost(t.id, { ...t, suggestedComment: comment, ...patch }));
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };
  const suggest = async () => {
    setBusy('ai');
    try {
      const list = await outreach.suggest(t.id);
      setOptions(list);
      if (list[0]) setComment(list[0]);
    } catch (e) {
      toast.error('AI chưa gợi ý được', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  return (
    <Card className="space-y-2.5 p-3.5">
      <div className="flex flex-wrap items-center gap-2 text-xs text-fg-muted">
        {t.groupName && <Badge tone="info">{t.groupName}</Badge>}
        <a href={t.postUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-accent">Mở bài viết <ExternalLink className="h-3 w-3" /></a>
        <span className="ml-auto">{t.status === 'todo' ? `Thêm ${fmtDateTime(t.createdAtUtc)}` : `Xử lý ${fmtDateTime(t.doneAtUtc)}`}</span>
      </div>
      {t.postText ? <p className="line-clamp-4 whitespace-pre-wrap rounded-md bg-subtle p-2 text-sm text-fg">{t.postText}</p>
        : <p className="text-xs text-fg-subtle">Chưa dán nội dung bài — dán vào để AI đọc và gợi ý bình luận.</p>}
      {options.length > 1 && (
        <div className="space-y-1">
          {options.map((o, i) => (
            <button key={i} type="button" onClick={() => setComment(o)}
              className={`block w-full rounded-md border px-2.5 py-1.5 text-left text-sm ${comment === o ? 'border-rose-500/60 bg-rose-500/[0.05] text-fg' : 'border-line text-fg-muted hover:bg-muted'}`}>
              {o}
            </button>
          ))}
        </div>
      )}
      {t.status === 'todo' && (
        <>
          <Textarea rows={2} value={comment} onChange={(e) => setComment(e.target.value)} placeholder="Bình luận sẽ đăng (bấm AI gợi ý hoặc tự viết)" />
          <div className="flex flex-wrap gap-2">
            <Button size="sm" variant="secondary" icon={<Sparkles className="h-3.5 w-3.5" />} loading={busy === 'ai'} disabled={!t.postText} onClick={suggest}>AI gợi ý</Button>
            <CopyButton text={comment} label="Copy bình luận" />
            <Button size="sm" variant="primary" icon={<Check className="h-3.5 w-3.5" />} loading={busy === 'done'} onClick={() => save({ status: 'done' })}>Đã xử lý</Button>
            <Button size="sm" variant="ghost" icon={<SkipForward className="h-3.5 w-3.5" />} loading={busy === 'skipped'} onClick={() => save({ status: 'skipped' })}>Bỏ qua</Button>
            <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} onClick={() => onRemove(t)} aria-label="Xoá" />
          </div>
        </>
      )}
      {t.status !== 'todo' && (
        <div className="flex flex-wrap items-center gap-2">
          {t.suggestedComment && <p className="flex-1 text-xs text-fg-muted">↳ {t.suggestedComment}</p>}
          <Button size="sm" variant="ghost" icon={<Undo2 className="h-3.5 w-3.5" />} loading={busy === 'todo'} onClick={() => save({ status: 'todo' })}>Đưa về chờ</Button>
        </div>
      )}
    </Card>
  );
}

export function PostsSection() {
  const toast = useToast();
  const confirm = useConfirm();
  const [tab, setTab] = useState<Tab>('todo');
  const { data: tasks, setData, loading } = useLoad(() => outreach.posts(tab), [tab]);
  const { data: groups } = useLoad(() => outreach.groups());
  const [form, setForm] = useState({ groupId: '', postUrl: '', postText: '' });
  const [busy, setBusy] = useState(false);

  const add = async () => {
    setBusy(true);
    try {
      const t = await outreach.addPost({ groupId: form.groupId || null, postUrl: form.postUrl, postText: form.postText || null });
      if (tab === 'todo') setData((l) => [t, ...(l ?? [])]);
      setForm({ groupId: form.groupId, postUrl: '', postText: '' });
      toast.success('Đã thêm bài cần xử lý');
    } catch (e) {
      toast.error('Chưa thêm được', (e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="grid gap-4 xl:grid-cols-[380px_minmax(0,1fr)]">
      <div className="space-y-3">
        <Card className="self-start">
          <CardHeader title="Thêm bài viết cần xử lý" description="Dán link bài trong Group và nội dung bài (Facebook không cho đọc bài Group qua API)." />
          <div className="space-y-3 p-4 pt-0">
            <Field label="Group" optional>
              <Select value={form.groupId} onChange={(e) => setForm({ ...form, groupId: e.target.value })}>
                <option value="">— Chọn Group —</option>
                {(groups ?? []).map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
              </Select>
            </Field>
            <Field label="Link bài viết">
              <Input value={form.postUrl} onChange={(e) => setForm({ ...form, postUrl: e.target.value })} placeholder="https://www.facebook.com/groups/…/posts/…" />
            </Field>
            <Field label="Nội dung bài" optional>
              <Textarea rows={5} value={form.postText} onChange={(e) => setForm({ ...form, postText: e.target.value })} placeholder="Copy nội dung bài viết dán vào đây" />
            </Field>
            <Button variant="primary" icon={<Plus className="h-4 w-4" />} loading={busy} disabled={!form.postUrl.trim()} onClick={add}>Thêm</Button>
          </div>
        </Card>
        <Alert tone="neutral">
          Bình luận mang giá trị cho cuộc thảo luận, mỗi bài một nội dung khác nhau. Không bình luận hàng loạt, không luân phiên Page để tạo tương tác giả.
        </Alert>
      </div>

      <div className="space-y-3">
        <Tabs<Tab> items={[{ id: 'todo', label: 'Cần xử lý' }, { id: 'done', label: 'Đã xử lý' }, { id: 'skipped', label: 'Bỏ qua' }]} value={tab} onChange={setTab} />
        {loading && !tasks ? <Skeleton className="h-40 w-full" /> : (tasks ?? []).length === 0 ? (
          <EmptyState icon={<FileText />} title={tab === 'todo' ? 'Không có bài nào chờ' : 'Trống'} description={tab === 'todo' ? 'Thêm bài viết Group bạn muốn bình luận ở khung bên trái.' : ''} />
        ) : (tasks ?? []).map((t) => (
          <TaskCard key={t.id} t={t}
            onChange={(n) => setData((l) => (n.status === tab ? l?.map((x) => (x.id === n.id ? n : x)) : l?.filter((x) => x.id !== n.id)) ?? null)}
            onRemove={async (x) => {
              if (!(await confirm({ title: 'Xoá bài này khỏi danh sách?', confirmLabel: 'Xoá', danger: true }))) return;
              try { await outreach.deletePost(x.id); setData((l) => l?.filter((y) => y.id !== x.id) ?? null); }
              catch (e) { toast.error('Chưa xoá được', (e as Error).message); }
            }} />
        ))}
      </div>
    </div>
  );
}
