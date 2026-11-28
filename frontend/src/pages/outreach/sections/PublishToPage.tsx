import { useState } from 'react';
import { ExternalLink, Send, Share2 } from 'lucide-react';
import { Button } from '../../../components/ui';
import { useConfirm, useToast } from '../../../components/feedback';
import { href } from '../../../lib/router';
import { Campaign, outreach, OutreachPage, WallPost } from '../outreachApi';

/**
 * "Đăng lên tường Page" for a text post (no picture): pick a helper Page (+ optional campaign), confirm, publish
 * through the Graph API. Sharing into Groups is then done by hand ("Chia sẻ vào nhóm").
 */
export function PublishToPage({ text, pages, campaigns }: { text: string; pages: OutreachPage[]; campaigns: Campaign[] }) {
  const toast = useToast();
  const confirm = useConfirm();
  const helpers = pages.filter((p) => p.isHelper && p.health !== 'error');
  const active = campaigns.filter((c) => c.status === 'Running' || c.status === 'PendingApproval' || c.status === 'Draft');
  const [pageId, setPageId] = useState('');
  const [campaignId, setCampaignId] = useState('');
  const [busy, setBusy] = useState(false);
  const [posted, setPosted] = useState<WallPost | null>(null);
  const page = helpers.find((p) => p.id === pageId) ?? helpers[0];

  const publish = async () => {
    if (!page || !text.trim()) return;
    if (!(await confirm({
      title: `Đăng bài này lên tường "${page.name}"?`,
      description: 'Bài (chữ) sẽ hiện công khai trên tường Page. Sau đó mở bài trên Facebook và bấm Chia sẻ → Nhóm.',
      confirmLabel: 'Đăng lên tường',
    }))) return;
    setBusy(true);
    try {
      const wall = await outreach.publishWallPost(page.id, text, null, campaignId || null);
      setPosted(wall);
      toast.success(`Đã đăng lên tường ${page.name}`, 'Mở bài → Chia sẻ → Nhóm, rồi tích “Đã chia sẻ” ở mục Chia sẻ vào nhóm.');
    } catch (e) {
      toast.error('Chưa đăng được', (e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  if (!helpers.length) return null;
  return (
    <div className="flex flex-wrap items-center gap-2 rounded-lg border border-line bg-subtle px-2 py-1.5">
      <select value={page?.id ?? ''} onChange={(e) => setPageId(e.target.value)} disabled={busy}
        className="h-8 rounded-md border border-line bg-surface px-2 text-xs text-fg" title="Đăng lên tường Page">
        {helpers.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
      </select>
      {active.length > 0 && (
        <select value={campaignId} onChange={(e) => setCampaignId(e.target.value)} disabled={busy}
          className="h-8 max-w-[180px] rounded-md border border-line bg-surface px-2 text-xs text-fg" title="Thuộc chiến dịch (để tính tiến độ)">
          <option value="">Không gắn chiến dịch</option>
          {active.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
      )}
      <Button size="sm" variant="primary" icon={<Send className="h-3.5 w-3.5" />} loading={busy} disabled={!text.trim()} onClick={publish}>
        Đăng lên tường
      </Button>
      {posted && (
        <span className="flex flex-wrap items-center gap-2 text-xs text-fg">
          ✅ Đã đăng lên <b>{posted.pageName}</b>
          <a href={posted.permalinkUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-accent">Mở bài <ExternalLink className="h-3 w-3" /></a>
          <a href={href('/outreach/share')} className="inline-flex items-center gap-1 text-accent">Chia sẻ vào nhóm <Share2 className="h-3 w-3" /></a>
        </span>
      )}
    </div>
  );
}
