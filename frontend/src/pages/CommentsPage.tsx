import { useEffect, useState } from 'react';
import { MessageCircle, RefreshCw, Send, X } from 'lucide-react';
import { Badge, Button, Card, EmptyState, PageHeader, Skeleton, Tabs, Textarea } from '../components/ui';
import { useToast } from '../components/feedback';
import { api, PostCommentDto } from '../services/api';
import { timeAgo } from '../lib/format';

type Tab = 'New' | 'Sent' | 'Dismissed';

/**
 * Comments inbox: new comments from viewers on the pages' posts (Facebook and Instagram, also posts made by hand),
 * each with a reply drafted by AI that the owner edits and sends. Also the "first comment" setting.
 */
export function CommentsPage() {
  const toast = useToast();
  const [tab, setTab] = useState<Tab>('New');
  const [comments, setComments] = useState<PostCommentDto[] | null>(null);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState<string | null>(null);
  const [firstComment, setFirstComment] = useState<boolean | null>(null);

  const load = async (t: Tab = tab) => {
    try {
      setComments(await api.getComments(t));
    } catch (e) {
      toast.error('Không tải được bình luận', (e as Error).message);
    }
  };

  useEffect(() => {
    document.title = 'Bình luận · Trạm Tin 24/7';
    api.getEngagementSettings().then((s) => setFirstComment(s.firstComment)).catch(() => undefined);
  }, []);
  useEffect(() => {
    setComments(null);
    load(tab);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab]);

  const send = async (c: PostCommentDto) => {
    const message = (drafts[c.id] ?? c.suggestedReply ?? '').trim();
    if (!message) return toast.error('Câu trả lời đang trống');
    setBusy(c.id);
    try {
      await api.replyComment(c.id, message);
      toast.success('Đã trả lời bình luận');
      setComments((list) => list?.filter((x) => x.id !== c.id) ?? null);
    } catch (e) {
      toast.error('Chưa gửi được', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const dismiss = async (c: PostCommentDto) => {
    setBusy(c.id);
    try {
      await api.dismissComment(c.id);
      setComments((list) => list?.filter((x) => x.id !== c.id) ?? null);
    } finally {
      setBusy(null);
    }
  };

  const refresh = async () => {
    setBusy('refresh');
    try {
      await api.refreshEngagement();
      toast.success('Đang đọc bình luận mới', 'Khoảng 1–2 phút nữa bấm Làm mới để xem.');
    } finally {
      setBusy(null);
    }
  };

  const toggleFirstComment = async () => {
    const next = !firstComment;
    setFirstComment(next);
    try {
      await api.saveEngagementSettings({ firstComment: next });
    } catch {
      setFirstComment(!next);
    }
  };

  return (
    <div className="space-y-4 pb-16">
      <PageHeader
        title="Bình luận"
        description="Bình luận mới trên Facebook và Instagram (cả bài đăng tay). AI soạn sẵn câu trả lời giọng của kênh — bạn sửa nếu muốn rồi bấm Gửi. Tool không bao giờ tự gửi."
        actions={
          <div className="flex gap-2">
            <Button size="sm" variant="secondary" icon={<RefreshCw className="h-3.5 w-3.5" />} loading={busy === 'refresh'} onClick={refresh}>Đọc bình luận mới</Button>
            <Button size="sm" variant="ghost" onClick={() => load()}>Làm mới</Button>
          </div>
        }
      />
      <Card className="flex flex-wrap items-center gap-2 p-3 text-xs text-fg">
        <label className="flex cursor-pointer items-center gap-2">
          <input type="checkbox" checked={!!firstComment} disabled={firstComment === null} onChange={toggleFirstComment} />
          Tự đăng <b>bình luận đầu tiên</b> có câu hỏi gợi kỷ niệm dưới mỗi video mới (Video AI và bài đăng tay, Facebook + Instagram)
        </label>
      </Card>
      <Tabs<Tab>
        items={[{ id: 'New', label: 'Chờ trả lời' }, { id: 'Sent', label: 'Đã trả lời' }, { id: 'Dismissed', label: 'Bỏ qua' }]}
        value={tab}
        onChange={setTab}
      />
      {!comments ? (
        <Skeleton className="h-40 w-full" />
      ) : comments.length === 0 ? (
        <EmptyState icon={<MessageCircle />} title={tab === 'New' ? 'Chưa có bình luận mới' : 'Trống'}
          description={tab === 'New' ? 'Tool đọc bình luận mỗi 30 phút. Bình luận của người xem sẽ hiện ở đây kèm câu trả lời gợi ý.' : ''} />
      ) : (
        <ul className="space-y-2">
          {comments.map((c) => (
            <li key={c.id}>
              <Card className="space-y-2 p-3 text-sm">
                <div className="flex flex-wrap items-center gap-2 text-xs text-fg-muted">
                  <Badge tone={c.network === 'instagram' ? 'warning' : 'info'}>{c.network === 'instagram' ? 'Instagram' : 'Facebook'}</Badge>
                  <span>{c.pageName}</span>·
                  {c.postUrl ? <a href={c.postUrl} target="_blank" rel="noreferrer" className="max-w-[320px] truncate hover:text-fg">{c.postTitle}</a> : <span className="max-w-[320px] truncate">{c.postTitle}</span>}
                  <span className="ml-auto">{timeAgo(c.commentedAtUtc)}</span>
                </div>
                <p className="text-fg"><b>{c.author ?? 'Người xem'}:</b> {c.message}</p>
                {tab === 'New' ? (
                  <>
                    <Textarea rows={2} value={drafts[c.id] ?? c.suggestedReply ?? ''} placeholder={c.suggestedReply == null ? 'AI đang soạn câu trả lời… (hoặc tự viết)' : ''}
                      onChange={(e) => setDrafts((d) => ({ ...d, [c.id]: e.target.value }))} />
                    {c.replyError && <p className="text-xs text-danger">{c.replyError}</p>}
                    <div className="flex gap-2">
                      <Button size="sm" variant="primary" icon={<Send className="h-3.5 w-3.5" />} loading={busy === c.id} onClick={() => send(c)}>Gửi trả lời</Button>
                      <Button size="sm" variant="ghost" icon={<X className="h-3.5 w-3.5" />} disabled={busy === c.id} onClick={() => dismiss(c)}>Bỏ qua</Button>
                    </div>
                  </>
                ) : c.sentReply ? (
                  <p className="rounded-lg bg-subtle p-2 text-xs text-fg">↳ {c.sentReply}</p>
                ) : null}
              </Card>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
