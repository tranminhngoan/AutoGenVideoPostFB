import { FormEvent, useRef, useState } from 'react';
import { Check, ExternalLink, FileUp, Info, Plus, Search, Users } from 'lucide-react';
import { Alert, Badge, Button, Card, CardHeader, EmptyState, Input, Textarea } from '../../../components/ui';
import { useToast } from '../../../components/feedback';
import { href } from '../../../lib/router';
import { DiscoveredGroup, outreach } from '../outreachApi';
import { GroupForm } from './Groups';

const PRESETS = ['tương tác chéo', 'follow chéo', 'like chéo fanpage', 'hoài niệm tuổi thơ', 'ký ức 8x 9x', 'showbiz giải trí', 'công nghệ', 'thể thao'];

const compact = (n: number) => new Intl.NumberFormat('vi-VN', { notation: 'compact', maximumFractionDigits: 1 }).format(n);

export function FinderSection() {
  const toast = useToast();
  const [keyword, setKeyword] = useState('');
  const [current, setCurrent] = useState('');
  const [results, setResults] = useState<DiscoveredGroup[]>([]);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [fbUrl, setFbUrl] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [adding, setAdding] = useState(false);
  const [manual, setManual] = useState(false);
  const [csv, setCsv] = useState('');
  const [importing, setImporting] = useState(false);
  const [report, setReport] = useState<{ added: number; duplicates: number; invalid: string[] } | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  const search = async (k: string, p = 1, e?: FormEvent) => {
    e?.preventDefault();
    const term = k.trim();
    if (term.length < 2) return;
    setKeyword(term);
    setBusy(true);
    setError(null);
    try {
      const r = await outreach.discover(term, p);
      setCurrent(term);
      setPage(r.page);
      setHasMore(r.hasMore && r.results.length > 0);
      setFbUrl(r.facebookSearchUrl);
      setResults((list) => {
        if (p === 1) return r.results;
        const seen = new Set(list.map((x) => x.url));
        return [...list, ...r.results.filter((x) => !seen.has(x.url))];
      });
      if (p === 1) setPicked(new Set());
    } catch (err) {
      setError((err as Error).message);
      if (p === 1) setResults([]);
    } finally {
      setBusy(false);
    }
  };

  const toggle = (url: string) => setPicked((s) => {
    const n = new Set(s);
    if (n.has(url)) n.delete(url); else n.add(url);
    return n;
  });

  const addPicked = async () => {
    const chosen = results.filter((r) => picked.has(r.url) && !r.groupId);
    if (!chosen.length) return;
    setAdding(true);
    let ok = 0;
    for (const r of chosen) {
      try {
        const g = await outreach.addGroup({ name: r.name, url: r.url, topic: current, privacy: r.privacy, members: r.members ?? undefined, note: r.description ?? undefined });
        setResults((list) => list.map((x) => (x.url === r.url ? { ...x, groupId: g.id } : x)));
        ok++;
      } catch (err) {
        toast.error(`Chưa thêm được "${r.name}"`, (err as Error).message);
      }
    }
    setPicked(new Set());
    setAdding(false);
    if (ok) toast.success(`Đã thêm ${ok} Group vào danh sách`, 'Vào "Tham gia Group" để mở từng Group theo từng Page.');
  };

  const doImport = async () => {
    if (!csv.trim()) return;
    setImporting(true);
    try {
      const r = await outreach.importCsv(csv);
      setReport(r);
      toast.success(`Đã thêm ${r.added} Group`, r.duplicates ? `${r.duplicates} Group đã có sẵn` : undefined);
      if (r.added > 0) setCsv('');
    } catch (err) {
      toast.error('Nhập CSV lỗi', (err as Error).message);
    } finally {
      setImporting(false);
    }
  };

  const selectable = results.filter((r) => !r.groupId);

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader title="Tìm Group" description="Tìm các Group Facebook công khai theo từ khoá (qua Brave Search). Chọn Group hợp → thêm vào danh sách → sang “Tham gia Group” để vào bằng từng Page." />
        <div className="space-y-3 p-4 pt-0">
          <form onSubmit={(e) => search(keyword, 1, e)} className="flex gap-2">
            <Input value={keyword} onChange={(e) => setKeyword(e.target.value)} placeholder="VD: tương tác chéo, hoài niệm tuổi thơ…" leading={<Search className="h-4 w-4" />} />
            <Button type="submit" variant="primary" loading={busy && page === 1}>Tìm</Button>
          </form>
          <div className="flex flex-wrap gap-1.5">
            {PRESETS.map((p) => (
              <button key={p} type="button" onClick={() => search(p)} className="rounded-full border border-line px-2.5 py-1 text-xs text-fg-muted hover:bg-muted">{p}</button>
            ))}
          </div>
          {error && (
            <Alert tone="warning" icon={<Info className="h-4 w-4" />}>
              {error}
              {fbUrl && <> Tạm thời có thể <a className="text-accent underline" href={fbUrl} target="_blank" rel="noreferrer">tìm trực tiếp trên Facebook</a>.</>}
            </Alert>
          )}
        </div>
      </Card>

      {results.length > 0 && (
        <Card>
          <div className="flex flex-wrap items-center gap-2 border-b border-line px-4 py-3">
            <p className="text-sm text-fg">
              <b>{results.length}</b> Group cho “{current}” · đã có trong danh sách {results.length - selectable.length}
            </p>
            <div className="flex-1" />
            <Button size="sm" variant="ghost" disabled={!selectable.length}
              onClick={() => setPicked(picked.size === selectable.length ? new Set() : new Set(selectable.map((r) => r.url)))}>
              {picked.size === selectable.length && selectable.length > 0 ? 'Bỏ chọn' : 'Chọn tất cả'}
            </Button>
            <Button size="sm" variant="primary" icon={<Plus className="h-3.5 w-3.5" />} loading={adding} disabled={!picked.size} onClick={addPicked}>
              Thêm {picked.size || ''} vào danh sách
            </Button>
          </div>
          <div className="divide-y divide-line">
            {results.map((r) => (
              <label key={r.url} className={`flex cursor-pointer items-start gap-3 px-4 py-3 ${r.groupId ? 'opacity-60' : 'hover:bg-subtle'}`}>
                <input type="checkbox" className="mt-1" disabled={!!r.groupId} checked={picked.has(r.url)} onChange={() => toggle(r.url)} />
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className="font-medium text-fg">{r.name}</span>
                    {r.members != null && <Badge><Users className="h-3 w-3" />{compact(r.members)}</Badge>}
                    {r.privacy !== 'unknown' && <Badge tone={r.privacy === 'public' ? 'success' : 'warning'}>{r.privacy === 'public' ? 'Công khai' : 'Riêng tư'}</Badge>}
                    {r.groupId && <Badge tone="info"><Check className="h-3 w-3" />Đã trong danh sách</Badge>}
                  </div>
                  {r.description && <p className="mt-0.5 line-clamp-2 text-xs text-fg-muted">{r.description}</p>}
                  <p className="mt-0.5 truncate font-mono text-[11px] text-fg-subtle">{r.url}</p>
                </div>
                <a href={r.url} target="_blank" rel="noreferrer" onClick={(e) => e.stopPropagation()}>
                  <Button size="sm" variant="ghost" icon={<ExternalLink className="h-3.5 w-3.5" />}>Xem</Button>
                </a>
              </label>
            ))}
          </div>
          {hasMore && (
            <div className="border-t border-line p-3 text-center">
              <Button size="sm" variant="secondary" loading={busy && page > 0} onClick={() => search(current, page + 1)}>Tải thêm</Button>
            </div>
          )}
        </Card>
      )}
      {!busy && current && results.length === 0 && !error && (
        <EmptyState icon={<Search />} title="Không tìm thấy Group nào" description="Thử từ khoá khác hoặc ngắn hơn." />
      )}

      <div className="flex flex-wrap items-center gap-2">
        <Button size="sm" variant="secondary" icon={<Plus className="h-3.5 w-3.5" />} onClick={() => setManual(true)}>Thêm Group bằng link</Button>
        <a href={href('/outreach/join')} className="text-xs text-accent">Tham gia Group theo từng Page →</a>
      </div>

      <Card>
        <CardHeader
          title="Nhập danh sách Group từ CSV"
          description={<>Dòng đầu là tiêu đề: <code>url,ten,chu_de,loai,ghi_chu</code> (loại cách nhau bởi <code>;</code>). Hoặc mỗi dòng một link.</>}
          actions={
            <>
              <input ref={fileRef} type="file" accept=".csv,text/csv,text/plain" className="hidden" onChange={async (e) => {
                const f = e.target.files?.[0];
                if (f) setCsv(await f.text());
                e.target.value = '';
              }} />
              <Button size="sm" variant="secondary" icon={<FileUp className="h-3.5 w-3.5" />} onClick={() => fileRef.current?.click()}>Chọn file</Button>
            </>
          }
        />
        <div className="space-y-3 p-4 pt-0">
          <Textarea rows={4} value={csv} onChange={(e) => setCsv(e.target.value)} className="font-mono text-xs"
            placeholder={'url,ten,chu_de,loai\nhttps://www.facebook.com/groups/abc,Hội tương tác chéo,tương tác,follow;like'} />
          <Button variant="primary" loading={importing} disabled={!csv.trim()} onClick={doImport}>Nhập vào danh sách</Button>
          {report && (
            <Alert tone={report.invalid.length ? 'warning' : 'success'}>
              Đã thêm <b>{report.added}</b> · trùng {report.duplicates} · không hợp lệ {report.invalid.length}
            </Alert>
          )}
        </div>
      </Card>

      <GroupForm open={manual} onClose={() => setManual(false)} onSaved={() => undefined} />
    </div>
  );
}
