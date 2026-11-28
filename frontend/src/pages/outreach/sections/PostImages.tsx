import { useEffect, useRef, useState } from 'react';
import { BookmarkPlus, Download, ExternalLink, ImageIcon, Palette, RefreshCw, Send, Share2, Sparkles } from 'lucide-react';
import { Badge, Button, Card, CardHeader, Field, Input, Select, Textarea } from '../../../components/ui';
import { useConfirm, useToast } from '../../../components/feedback';
import { href } from '../../../lib/router';
import { Campaign, Draft, outreach, OutreachPage, WallPost } from '../outreachApi';
import { CopyButton, useLoad } from '../shared';

const SIZE = 1080;

/** Backgrounds for the text pictures — each post gets a different one. */
const BACKGROUNDS: { name: string; draw: (c: CanvasRenderingContext2D, seed: number) => void }[] = [
  { name: 'Bầu trời đêm', draw: (c, seed) => { gradient(c, ['#0b1026', '#1e2a5a', '#3b2f63']); stars(c, seed); } },
  { name: 'Hoàng hôn', draw: (c) => gradient(c, ['#f97316', '#db2777', '#7c3aed']) },
  { name: 'Biển xanh', draw: (c, seed) => { gradient(c, ['#0f766e', '#0e7490', '#1e3a8a']); bubbles(c, seed); } },
  { name: 'Tím hồng', draw: (c, seed) => { gradient(c, ['#4c1d95', '#a21caf', '#db2777']); bubbles(c, seed); } },
  { name: 'Rừng xanh', draw: (c) => gradient(c, ['#052e16', '#166534', '#15803d']) },
  { name: 'Facebook', draw: (c, seed) => { gradient(c, ['#0a3fbf', '#0866ff', '#3b82f6']); bubbles(c, seed); } },
  { name: 'Đen sang', draw: (c, seed) => { gradient(c, ['#09090b', '#18181b', '#27272a']); stars(c, seed); } },
  { name: 'Cam đất', draw: (c) => gradient(c, ['#7c2d12', '#c2410c', '#ea580c']) },
];

function rand(seed: number) {
  let s = seed || 1;
  return () => ((s = (s * 16807) % 2147483647) / 2147483647);
}
function gradient(c: CanvasRenderingContext2D, colors: string[]) {
  const g = c.createLinearGradient(0, 0, SIZE, SIZE);
  colors.forEach((col, i) => g.addColorStop(i / (colors.length - 1), col));
  c.fillStyle = g;
  c.fillRect(0, 0, SIZE, SIZE);
  const v = c.createRadialGradient(SIZE / 2, SIZE / 2, SIZE * 0.2, SIZE / 2, SIZE / 2, SIZE * 0.75);
  v.addColorStop(0, 'rgba(0,0,0,0)');
  v.addColorStop(1, 'rgba(0,0,0,0.35)');
  c.fillStyle = v;
  c.fillRect(0, 0, SIZE, SIZE);
}
function stars(c: CanvasRenderingContext2D, seed: number) {
  const r = rand(seed);
  for (let i = 0; i < 220; i++) {
    c.fillStyle = `rgba(255,255,255,${0.15 + r() * 0.6})`;
    c.beginPath();
    c.arc(r() * SIZE, r() * SIZE, r() * 1.8 + 0.3, 0, Math.PI * 2);
    c.fill();
  }
}
function bubbles(c: CanvasRenderingContext2D, seed: number) {
  const r = rand(seed);
  for (let i = 0; i < 14; i++) {
    c.fillStyle = `rgba(255,255,255,${0.03 + r() * 0.06})`;
    c.beginPath();
    c.arc(r() * SIZE, r() * SIZE, 40 + r() * 180, 0, Math.PI * 2);
    c.fill();
  }
}

function wrap(c: CanvasRenderingContext2D, text: string, maxWidth: number) {
  const lines: string[] = [];
  for (const para of text.split(/\n+/)) {
    let line = '';
    for (const word of para.split(/\s+/).filter(Boolean)) {
      const test = line ? `${line} ${word}` : word;
      if (c.measureText(test).width > maxWidth && line) {
        lines.push(line);
        line = word;
      } else line = test;
    }
    if (line) lines.push(line);
  }
  return lines;
}

/** Draws one post picture: background + centred bold text (auto-sized) + small domain tag. */
export function drawPost(canvas: HTMLCanvasElement, text: string, bg: number, seed: number, domain: string) {
  const c = canvas.getContext('2d')!;
  canvas.width = SIZE;
  canvas.height = SIZE;
  BACKGROUNDS[bg % BACKGROUNDS.length].draw(c, seed);

  const pad = 110;
  let size = 64;
  let lines: string[] = [];
  for (; size >= 34; size -= 2) {
    c.font = `700 ${size}px Inter, "Segoe UI", Arial, sans-serif`;
    lines = wrap(c, text, SIZE - pad * 2);
    if (lines.length * size * 1.38 <= SIZE - pad * 2 - 60) break;
  }
  const lh = size * 1.38;
  // leave room at the bottom for the big domain badge
  let y = (SIZE - 150 - lines.length * lh) / 2 + size * 0.85;
  c.shadowColor = 'rgba(0,0,0,0.55)';
  c.shadowBlur = 18;
  c.shadowOffsetY = 3;
  c.textAlign = 'left';
  const lower = domain.toLowerCase();
  for (const l of lines) {
    // draw the line in pieces so the domain stands out in yellow wherever it appears
    const width = c.measureText(l).width;
    let x = (SIZE - width) / 2;
    let rest = l;
    while (rest.length) {
      const at = lower ? rest.toLowerCase().indexOf(lower) : -1;
      const piece = at === -1 ? rest : at === 0 ? rest.slice(0, domain.length) : rest.slice(0, at);
      c.fillStyle = at === 0 ? '#FDE047' : '#ffffff';
      c.fillText(piece, x, y);
      x += c.measureText(piece).width;
      rest = rest.slice(piece.length);
    }
    y += lh;
  }
  c.shadowBlur = 0;
  c.shadowOffsetY = 0;

  // Big yellow badge with the domain — the thing readers must notice and type
  c.font = '800 54px Inter, "Segoe UI", Arial, sans-serif';
  c.textAlign = 'center';
  const tag = `👉 ${domain}`;
  const w = c.measureText(tag).width + 90;
  const h = 96;
  const bx = (SIZE - w) / 2;
  const by = SIZE - 150;
  c.shadowColor = 'rgba(0,0,0,0.35)';
  c.shadowBlur = 24;
  c.fillStyle = '#FDE047';
  c.beginPath();
  c.roundRect(bx, by, w, h, h / 2);
  c.fill();
  c.shadowBlur = 0;
  c.fillStyle = '#111827';
  c.fillText(tag, SIZE / 2, by + h / 2 + 19);
}

/** Caption to paste with the picture: the text + the domain alone on its own line (easy to long-press and copy). */
export function captionOf(text: string, domain: string) {
  const t = text.trim();
  const lastLine = t.split(/\n/).pop()?.trim().toLowerCase() ?? '';
  return lastLine.includes(domain.toLowerCase()) && lastLine.length <= domain.length + 6 ? t : `${t}\n\n👉 ${domain}`;
}

interface Post extends Draft {
  bg: number;
  seed: number;
  posted?: WallPost;
}

function PostCard({ post, index, domain, page, campaign, onChange, onSave }: {
  post: Post; index: number; domain: string; page?: OutreachPage; campaign?: Campaign;
  onChange: (p: Post) => void; onSave: (p: Post) => Promise<boolean>;
}) {
  const toast = useToast();
  const confirm = useConfirm();
  const [publishing, setPublishing] = useState(false);
  const ref = useRef<HTMLCanvasElement>(null);

  const publish = async () => {
    if (!page || !ref.current) return;
    if (!(await confirm({
      title: `Đăng bài ${index + 1} lên tường "${page.name}"?`,
      description: 'Bài (ảnh + chữ) sẽ hiện công khai trên tường Page. Sau đó bạn mở bài trên Facebook và bấm Chia sẻ → Nhóm.',
      confirmLabel: 'Đăng lên tường',
    }))) return;
    setPublishing(true);
    try {
      const blob = await new Promise<Blob | null>((resolve) => ref.current!.toBlob(resolve, 'image/jpeg', 0.92));
      if (!blob) throw new Error('Không tạo được ảnh.');
      const wall = await outreach.publishWallPost(page.id, captionOf(post.text, domain), blob, campaign?.id);
      onChange({ ...post, posted: wall });
      toast.success(`Đã đăng lên tường ${page.name}`, 'Mở bài → Chia sẻ → Nhóm, rồi tích “Đã chia sẻ” ở mục Chia sẻ vào nhóm.');
    } catch (e) {
      toast.error('Chưa đăng được', (e as Error).message);
    } finally {
      setPublishing(false);
    }
  };
  useEffect(() => {
    if (ref.current) drawPost(ref.current, post.text, post.bg, post.seed, domain);
  }, [post.text, post.bg, post.seed, domain]);

  const download = () => {
    ref.current?.toBlob((blob) => {
      if (!blob) return;
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = `bai-dang-${index + 1}.png`;
      a.click();
      setTimeout(() => URL.revokeObjectURL(a.href), 2000);
    }, 'image/png');
  };

  return (
    <Card className="grid gap-3 p-3 sm:grid-cols-[220px_minmax(0,1fr)]">
      <canvas ref={ref} className="aspect-square w-full rounded-lg border border-line" />
      <div className="flex min-w-0 flex-col gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-xs font-medium text-fg-muted">Bài {index + 1}</span>
          {post.similarity >= 70 ? <Badge tone="danger" dot>Trùng {post.similarity}%</Badge>
            : post.similarity >= 40 ? <Badge tone="warning" dot>Giống {post.similarity}%</Badge>
              : <Badge tone="success" dot>Khác biệt · {post.similarity}%</Badge>}
          <span className="text-xs text-fg-subtle">Nền: {BACKGROUNDS[post.bg % BACKGROUNDS.length].name}</span>
        </div>
        <Textarea rows={4} value={post.text} onChange={(e) => onChange({ ...post, text: e.target.value })} />
        {post.posted && (
          <div className="flex flex-wrap items-center gap-2 rounded-lg border border-success/30 bg-success/5 px-2.5 py-2 text-xs text-fg">
            <span>✅ Đã đăng lên <b>{post.posted.pageName}</b></span>
            <a href={post.posted.permalinkUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-accent">Mở bài <ExternalLink className="h-3 w-3" /></a>
            <a href={href('/outreach/share')} className="inline-flex items-center gap-1 text-accent">Chia sẻ vào nhóm <Share2 className="h-3 w-3" /></a>
          </div>
        )}
        <div className="mt-auto flex flex-wrap gap-2">
          <Button size="sm" variant="primary" icon={<Send className="h-3.5 w-3.5" />} loading={publishing} disabled={!page || !!post.posted} onClick={publish}>
            {post.posted ? 'Đã đăng' : page ? `Đăng lên tường ${page.name.replace(/^Trạm Tin\s*/i, '')}` : 'Đăng lên tường'}
          </Button>
          <Button size="sm" variant="secondary" icon={<Download className="h-3.5 w-3.5" />} onClick={download}>Tải ảnh</Button>
          <CopyButton text={captionOf(post.text, domain)} label="Copy chữ đăng kèm" />
          <Button size="sm" variant="ghost" icon={<Palette className="h-3.5 w-3.5" />}
            onClick={() => onChange({ ...post, bg: (post.bg + 1) % BACKGROUNDS.length, seed: post.seed + 7 })}>Đổi nền</Button>
          <Button size="sm" variant="ghost" icon={<BookmarkPlus className="h-3.5 w-3.5" />} disabled={post.similarity >= 90} onClick={() => onSave(post)}>Lưu thư viện</Button>
        </div>
      </div>
    </Card>
  );
}

/** AI writes many differently-worded posts with the same message; each becomes a text picture to download. */
export function PostImagesCard({ onSaved }: { onSaved: () => void }) {
  const toast = useToast();
  const [count, setCount] = useState(5);
  const [extra, setExtra] = useState('');
  const [busy, setBusy] = useState(false);
  const [wait, setWait] = useState(0);
  const [posts, setPosts] = useState<Post[]>([]);
  const [domain, setDomain] = useState('tuongtaccheo.xyz');
  const [fontReady, setFontReady] = useState(false);
  const { data: pages } = useLoad(() => outreach.pages());
  const { data: campaigns } = useLoad(() => outreach.campaigns());
  const helpers = (pages ?? []).filter((p) => p.isHelper && p.health !== 'error');
  const [pageId, setPageId] = useState('');
  const [campaignId, setCampaignId] = useState('');
  const page = helpers.find((p) => p.id === pageId) ?? helpers[0];
  const active = (campaigns ?? []).filter((c) => c.status === 'Running' || c.status === 'PendingApproval' || c.status === 'Draft');
  const campaign = active.find((c) => c.id === campaignId);

  useEffect(() => {
    // Vietnamese diacritics need the web font loaded before drawing
    document.fonts?.load('700 40px Inter').then(() => setFontReady(true)).catch(() => setFontReady(true));
  }, []);

  const write = async () => {
    setBusy(true);
    try {
      setWait(0);
      const drafts = await outreach.variants(count, extra || undefined, setWait);
      const start = Math.floor(Math.random() * BACKGROUNDS.length);
      setPosts(drafts.map((d, i) => ({ ...d, bg: (start + i) % BACKGROUNDS.length, seed: Date.now() % 100000 + i * 97 })));
    } catch (e) {
      toast.error('AI chưa viết được', (e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const save = async (p: Post) => {
    try {
      await outreach.saveContent({ text: p.text, style: 'post', origin: 'ai' });
      toast.success('Đã lưu vào thư viện');
      onSaved();
      return true;
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
      return false;
    }
  };

  return (
    <Card>
      <CardHeader
        title={<span className="flex items-center gap-2"><ImageIcon className="h-4 w-4 text-rose-500" /> Bài đăng ảnh chữ (cho Group cấm link)</span>}
        description="AI viết nhiều bài khác câu chữ nhưng cùng ý: vào trang, follow / like / cmt ủng hộ, cmt “done”, mình nhận thông báo sẽ ghé lại uy tín. Mỗi bài thành một ảnh nền khác nhau để tải về đăng."
      />
      <div className="space-y-3 p-4 pt-0">
        <div className="grid gap-3 sm:grid-cols-[120px_minmax(0,1fr)_200px_auto] sm:items-end">
          <Field label="Số bài">
            <Select value={count} onChange={(e) => setCount(Number(e.target.value))}>
              {[3, 5, 8, 10].map((n) => <option key={n} value={n}>{n}</option>)}
            </Select>
          </Field>
          <Field label="Yêu cầu thêm" optional>
            <Input value={extra} onChange={(e) => setExtra(e.target.value)} placeholder="VD: thêm emoji, viết ngắn hơn…" />
          </Field>
          <Field label="Tên trang ghi trên ảnh">
            <Input value={domain} onChange={(e) => setDomain(e.target.value)} />
          </Field>
          <Button variant="primary" icon={posts.length ? <RefreshCw className="h-4 w-4" /> : <Sparkles className="h-4 w-4" />} loading={busy} disabled={!fontReady} onClick={write}>
            {posts.length ? 'Viết lại' : `AI viết ${count} bài`}
          </Button>
          {busy && <span className="text-xs text-fg-muted sm:col-span-4">AI đang viết… {wait} giây (lần đầu có thể mất 1–3 phút, cứ để trang mở)</span>}
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Đăng lên tường Page">
            <Select value={page?.id ?? ''} onChange={(e) => setPageId(e.target.value)}>
              {helpers.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </Select>
          </Field>
          <Field label="Thuộc chiến dịch" optional hint="Để tính tiến độ chiến dịch khi bạn chia sẻ bài vào nhóm.">
            <Select value={campaignId} onChange={(e) => setCampaignId(e.target.value)}>
              <option value="">— Không gắn chiến dịch —</option>
              {active.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </Select>
          </Field>
        </div>
        <p className="text-xs text-fg-subtle">
          AI không viết các ý “tài khoản nuôi / follow 1 trả 5 / quà / tiền” — đó là thứ Facebook tự gỡ bài và phạt Page.
        </p>
        {posts.map((p, i) => (
          <PostCard key={i} post={p} index={i} domain={domain} page={page} campaign={campaign}
            onChange={(n) => setPosts((list) => list.map((x, j) => (j === i ? n : x)))}
            onSave={save} />
        ))}
      </div>
    </Card>
  );
}
