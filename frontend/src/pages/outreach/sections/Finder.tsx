import { FormEvent, useRef, useState } from 'react';
import { ExternalLink, FileUp, Info, Plus, Search } from 'lucide-react';
import { Alert, Button, Card, CardHeader, Input, Textarea } from '../../../components/ui';
import { useToast } from '../../../components/feedback';
import { href } from '../../../lib/router';
import { outreach } from '../outreachApi';
import { GroupForm } from './Groups';

const PRESETS = ['tương tác chéo', 'follow chéo uy tín', 'hoài niệm tuổi thơ', 'ký ức 8x 9x', 'giải trí showbiz', 'công nghệ'];

export function FinderSection() {
  const toast = useToast();
  const [keyword, setKeyword] = useState('');
  const [result, setResult] = useState<{ keyword: string; facebookSearchUrl: string; relatedKeywords: string[] } | null>(null);
  const [busy, setBusy] = useState(false);
  const [adding, setAdding] = useState(false);
  const [csv, setCsv] = useState('');
  const [importing, setImporting] = useState(false);
  const [report, setReport] = useState<{ added: number; duplicates: number; invalid: string[] } | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  const search = async (k: string, e?: FormEvent) => {
    e?.preventDefault();
    if (k.trim().length < 2) return;
    setKeyword(k);
    setBusy(true);
    try {
      setResult(await outreach.searchGroups(k.trim()));
    } catch (err) {
      toast.error('Không tạo được tìm kiếm', (err as Error).message);
    } finally {
      setBusy(false);
    }
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

  return (
    <div className="space-y-4">
      <Alert tone="info" icon={<Info className="h-4 w-4" />}>
        Meta không còn API tìm Group hay đọc bài trong Group. Công cụ tạo sẵn đường dẫn tìm kiếm của chính Facebook — bạn mở bằng tài khoản
        của mình, chọn Group phù hợp rồi dán link vào đây. Hệ thống không tự tham gia Group và không thu thập dữ liệu Group riêng tư.
      </Alert>

      <Card>
        <CardHeader title="Tìm Group trên Facebook" description="Nhập từ khoá → mở kết quả tìm kiếm Group của Facebook trong tab mới." />
        <div className="space-y-3 p-4 pt-0">
          <form onSubmit={(e) => search(keyword, e)} className="flex gap-2">
            <Input value={keyword} onChange={(e) => setKeyword(e.target.value)} placeholder="VD: hoài niệm tuổi thơ, tương tác chéo…" leading={<Search className="h-4 w-4" />} />
            <Button type="submit" variant="primary" loading={busy}>Tìm</Button>
          </form>
          <div className="flex flex-wrap gap-1.5">
            {PRESETS.map((p) => (
              <button key={p} type="button" onClick={() => search(p)} className="rounded-full border border-line px-2.5 py-1 text-xs text-fg-muted hover:bg-muted">
                {p}
              </button>
            ))}
          </div>
          {result && (
            <div className="rounded-lg border border-rose-500/30 bg-rose-500/[0.04] p-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="text-sm text-fg">Kết quả cho “<b>{result.keyword}</b>”</p>
                <a href={result.facebookSearchUrl} target="_blank" rel="noreferrer">
                  <Button size="sm" variant="primary" icon={<ExternalLink className="h-3.5 w-3.5" />}>Mở tìm kiếm Group trên Facebook</Button>
                </a>
              </div>
              <p className="mt-2 text-xs text-fg-muted">Thử thêm các từ khoá liên quan:</p>
              <div className="mt-1 flex flex-wrap gap-1.5">
                {result.relatedKeywords.map((k) => (
                  <button key={k} type="button" onClick={() => search(k)} className="rounded-full bg-muted px-2.5 py-1 text-xs text-fg hover:bg-line">
                    {k}
                  </button>
                ))}
              </div>
            </div>
          )}
          <div className="flex flex-wrap items-center gap-2 border-t border-line pt-3">
            <p className="text-sm text-fg-muted">Tìm được Group hợp?</p>
            <Button size="sm" variant="secondary" icon={<Plus className="h-3.5 w-3.5" />} onClick={() => setAdding(true)}>Thêm Group bằng link</Button>
            <a href={href('/outreach/groups')} className="text-xs text-accent">Xem danh sách Group →</a>
          </div>
        </div>
      </Card>

      <Card>
        <CardHeader
          title="Nhập danh sách Group từ CSV"
          description={<>Dòng đầu là tiêu đề: <code>url,ten,chu_de,loai,ghi_chu</code> (loại cách nhau bởi <code>;</code> — follow;like;comment;share;view;mixed). Hoặc mỗi dòng một link.</>}
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
          <Textarea rows={6} value={csv} onChange={(e) => setCsv(e.target.value)} className="font-mono text-xs"
            placeholder={'url,ten,chu_de,loai\nhttps://www.facebook.com/groups/abc,Hội tương tác chéo,tương tác,follow;like'} />
          <div className="flex items-center gap-2">
            <Button variant="primary" loading={importing} disabled={!csv.trim()} onClick={doImport}>Nhập vào danh sách</Button>
            {csv && <span className="text-xs text-fg-subtle">{csv.split(/\r?\n/).filter((l) => l.trim()).length} dòng</span>}
          </div>
          {report && (
            <Alert tone={report.invalid.length ? 'warning' : 'success'}>
              Đã thêm <b>{report.added}</b> · trùng {report.duplicates} · không hợp lệ {report.invalid.length}
              {report.invalid.length > 0 && (
                <ul className="mt-1 list-disc pl-4 text-xs">
                  {report.invalid.slice(0, 8).map((l) => <li key={l} className="truncate">{l}</li>)}
                </ul>
              )}
            </Alert>
          )}
        </div>
      </Card>

      <GroupForm open={adding} onClose={() => setAdding(false)} onSaved={() => undefined} />
    </div>
  );
}
