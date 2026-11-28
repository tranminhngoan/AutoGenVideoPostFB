import { useMemo, useState } from 'react';
import { ExternalLink, ImageIcon, Info, Share2 } from 'lucide-react';
import { Alert, Badge, Button, Card, Checkbox, EmptyState, Skeleton, cn } from '../../../components/ui';
import { useToast } from '../../../components/feedback';
import { href } from '../../../lib/router';
import { outreach, WallPost } from '../outreachApi';
import { fmtDateTime, PageAvatar, useLoad } from '../shared';

/**
 * Sharing the helper Pages' wall posts into Groups — by hand on Facebook (Meta has no API for Groups).
 * Per Page: its recent wall posts, and under each the Groups that Page is in, ticked once shared.
 */
export function ShareSection() {
  const toast = useToast();
  const { data: pages } = useLoad(() => outreach.pages());
  const helpers = useMemo(() => (pages ?? []).filter((p) => p.isHelper), [pages]);
  const [pageId, setPageId] = useState<string | null>(null);
  const page = helpers.find((p) => p.id === pageId) ?? helpers[0];
  const { data: posts, setData, loading } = useLoad(() => (page ? outreach.wallPosts(page.id) : Promise.resolve([] as WallPost[])), [page?.id]);
  const { data: groups } = useLoad(() => outreach.groups());
  const [busy, setBusy] = useState<string | null>(null);

  const joined = useMemo(
    () => (groups ?? []).filter((g) => g.fit !== 'unfit' && page && g.memberships.some((m) => m.pageId === page.id && m.status === 'joined')),
    [groups, page],
  );

  const toggle = async (post: WallPost, groupId: string, shared: boolean) => {
    setBusy(post.id + groupId);
    try {
      const r = await outreach.setShare(post.id, groupId, shared);
      setData((list) => list?.map((p) => p.id !== post.id ? p : {
        ...p,
        shares: shared ? [...p.shares.filter((s) => s.groupId !== groupId), r] : p.shares.filter((s) => s.groupId !== groupId),
      }) ?? null);
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  if (!helpers.length) return <EmptyState icon={<Share2 />} title="Chưa có Page phụ" />;

  return (
    <div className="space-y-4">
      <div className="grid gap-2 sm:grid-cols-3">
        {helpers.map((p) => (
          <button key={p.id} type="button" onClick={() => setPageId(p.id)}
            className={cn('flex items-center gap-3 rounded-xl border p-3 text-left transition-colors',
              page?.id === p.id ? 'border-rose-500/60 bg-rose-500/[0.05] ring-1 ring-rose-500/30' : 'border-line bg-surface hover:bg-subtle')}>
            <PageAvatar url={p.pictureUrl} name={p.name} size={40} />
            <div className="min-w-0">
              <p className="truncate text-sm font-semibold text-fg">{p.name}</p>
              <p className="text-xs text-fg-muted">{(groups ?? []).filter((g) => g.memberships.some((m) => m.pageId === p.id && m.status === 'joined')).length} nhóm đã vào</p>
            </div>
          </button>
        ))}
      </div>

      <Alert tone="info" icon={<Info className="h-4 w-4" />}>
        Mở bài trên Facebook (đang dùng Page <b>{page?.name}</b>) → bấm <b>Chia sẻ</b> → <b>Nhóm</b> → chọn nhóm → Đăng, rồi tích ô của nhóm đó ở đây.
        Mỗi bài chỉ nên chia sẻ <b>3–5 nhóm mỗi lượt</b>, rải trong ngày — chia sẻ dồn dập một bài vào nhiều nhóm dễ bị Facebook coi là spam.
      </Alert>

      {loading && !posts ? <Skeleton className="h-48 w-full" /> : (posts ?? []).length === 0 ? (
        <EmptyState icon={<ImageIcon />} title={`${page?.name} chưa có bài nào do công cụ đăng`}
          description="Vào AI Outreach → Bài đăng ảnh chữ → “Đăng lên tường” để tạo bài, rồi quay lại đây chia sẻ vào nhóm."
          action={<a href={href('/outreach/ai')}><Button variant="primary">Tạo bài đăng</Button></a>} />
      ) : (posts ?? []).map((post) => {
        const sharedIds = new Set(post.shares.map((s) => s.groupId));
        return (
          <Card key={post.id} className="space-y-3 p-4">
            <div className="flex flex-wrap items-start gap-3">
              <div className="min-w-0 flex-1">
                <p className="text-xs text-fg-subtle">Đăng {fmtDateTime(post.createdAtUtc)}</p>
                <p className="mt-0.5 line-clamp-3 whitespace-pre-wrap text-sm text-fg">{post.text}</p>
              </div>
              <a href={post.permalinkUrl} target="_blank" rel="noreferrer">
                <Button size="sm" variant="primary" icon={<ExternalLink className="h-3.5 w-3.5" />}>Mở bài để chia sẻ</Button>
              </a>
            </div>
            <div className="flex items-center gap-2 text-xs text-fg-muted">
              <Badge tone={sharedIds.size ? 'success' : 'neutral'}>Đã chia sẻ {sharedIds.size}/{joined.length} nhóm</Badge>
              {sharedIds.size >= 5 && <span className="text-warning">Đủ 5 nhóm — nên dùng bài khác cho lượt sau.</span>}
            </div>
            {joined.length === 0 ? (
              <p className="text-sm text-fg-muted">
                {page?.name} chưa vào nhóm nào. <a href={href('/outreach/join')} className="text-accent">Vào “Tham gia Group”</a> để tham gia và đánh dấu “Đã vào”.
              </p>
            ) : (
              <div className="grid gap-1 sm:grid-cols-2">
                {joined.map((g) => {
                  const s = post.shares.find((x) => x.groupId === g.id);
                  return (
                    <div key={g.id} className={cn('rounded-lg px-2 py-1', s && 'bg-success/5')}>
                      <Checkbox checked={!!s} disabled={busy === post.id + g.id} onChange={(v) => toggle(post, g.id, v)}
                        label={g.name} description={s ? `Đã chia sẻ ${fmtDateTime(s.sharedAtUtc)}` : undefined} />
                    </div>
                  );
                })}
              </div>
            )}
          </Card>
        );
      })}
    </div>
  );
}
