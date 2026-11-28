import { useState } from 'react';
import { KeyRound, LogOut, ShieldAlert, ShieldCheck } from 'lucide-react';
import { Alert, Badge, Button, Card, CardHeader, Field, Input, Select, Skeleton } from '../../../components/ui';
import { useConfirm, useToast } from '../../../components/feedback';
import { outreach } from '../outreachApi';
import { fmtDateTime, useLoad } from '../shared';

export function SecuritySection() {
  const toast = useToast();
  const confirm = useConfirm();
  const { data: s, loading, reload } = useLoad(() => outreach.security());
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [again, setAgain] = useState('');
  const [hours, setHours] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);

  const save = async () => {
    if (next && next !== again) return toast.error('Hai lần nhập mật khẩu mới không khớp');
    setBusy(true);
    try {
      await outreach.changeSecurity({ currentPassword: current, newPassword: next || undefined, sessionHours: hours ?? undefined });
      setCurrent('');
      setNext('');
      setAgain('');
      if (next) {
        toast.success('Đã đổi mật khẩu chiến dịch', 'Mọi phiên cũ đã kết thúc — nhập mật khẩu mới để vào lại.');
        outreach.lock();
      } else {
        toast.success('Đã lưu cấu hình');
        reload();
      }
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  if (loading && !s) return <Skeleton className="h-64 w-full" />;
  if (!s) return null;
  const failures = s.recentAttempts.filter((a) => !a.success).length;

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <div className="space-y-4">
        <Card>
          <CardHeader title={<span className="flex items-center gap-2"><ShieldCheck className="h-4 w-4 text-success" /> Trạng thái bảo vệ</span>} />
          <div className="space-y-2 p-4 pt-0 text-sm text-fg">
            <p>• Mật khẩu chiến dịch kiểm tra trên máy chủ, chỉ lưu dạng hash PBKDF2-SHA256 ({s.passwordSource === 'database' ? 'đã đổi trong menu này' : 'mật khẩu ban đầu từ cấu hình máy chủ'}).</p>
            <p>• Phiên mở khoá kéo dài <b>{s.sessionHours} giờ</b>, đóng tab là phải nhập lại.</p>
            <p>• Sai <b>{s.maxAttempts} lần</b> trong {s.lockMinutes} phút → địa chỉ đó bị khoá {s.lockMinutes} phút.</p>
            <p>• Trang công khai <code>/cheo-uy-tinh</code> không bị ảnh hưởng.</p>
          </div>
        </Card>
        <Card>
          <CardHeader title={<span className="flex items-center gap-2"><KeyRound className="h-4 w-4" /> Đổi mật khẩu / thời hạn phiên</span>}
            description="Đổi mật khẩu sẽ kết thúc mọi phiên đang mở." />
          <div className="space-y-3 p-4 pt-0">
            <Field label="Mật khẩu hiện tại">
              <Input type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} />
            </Field>
            <Field label="Mật khẩu mới" optional hint="Ít nhất 10 ký tự, có chữ và số. Để trống nếu chỉ đổi thời hạn phiên.">
              <Input type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} />
            </Field>
            {next && (
              <Field label="Nhập lại mật khẩu mới">
                <Input type="password" autoComplete="new-password" value={again} onChange={(e) => setAgain(e.target.value)} />
              </Field>
            )}
            <Field label="Thời hạn phiên">
              <Select value={hours ?? s.sessionHours} onChange={(e) => setHours(Number(e.target.value))}>
                {[1, 2, 4, 8, 12, 24].map((h) => <option key={h} value={h}>{h} giờ</option>)}
              </Select>
            </Field>
            <Button variant="primary" loading={busy} disabled={!current} onClick={save}>Lưu</Button>
          </div>
        </Card>
        <Card className="p-4">
          <div className="flex flex-wrap items-center gap-3">
            <div className="min-w-0 flex-1">
              <p className="text-sm font-medium text-fg">Khoá ở mọi nơi</p>
              <p className="text-xs text-fg-muted">Kết thúc mọi phiên đang mở trên mọi trình duyệt (kể cả phiên này).</p>
            </div>
            <Button variant="danger-soft" icon={<LogOut className="h-3.5 w-3.5" />} onClick={async () => {
              if (!(await confirm({ title: 'Khoá menu chiến dịch ở mọi nơi?', description: 'Mọi người đang mở menu sẽ phải nhập lại mật khẩu.', confirmLabel: 'Khoá', danger: true }))) return;
              try { await outreach.lockAll(); outreach.lock(); } catch (e) { toast.error('Chưa khoá được', (e as Error).message); }
            }}>Khoá tất cả</Button>
          </div>
        </Card>
      </div>

      <Card className="self-start">
        <CardHeader title="Nhật ký mở khoá" description="30 lần gần nhất." />
        <div className="p-4 pt-0">
          {failures >= 3 && (
            <Alert tone="warning" icon={<ShieldAlert className="h-4 w-4" />} className="mb-3">
              Có {failures} lần nhập sai gần đây. Nếu không phải bạn, hãy đổi mật khẩu.
            </Alert>
          )}
          <div className="divide-y divide-line">
            {s.recentAttempts.length === 0 && <p className="text-sm text-fg-muted">Chưa có lần mở khoá nào.</p>}
            {s.recentAttempts.map((a, i) => (
              <div key={i} className="flex items-center gap-2 py-1.5 text-sm">
                <Badge tone={a.success ? 'success' : 'danger'} dot>{a.success ? 'Thành công' : 'Sai mật khẩu'}</Badge>
                <span className="font-mono text-xs text-fg-muted">{a.ip}</span>
                <span className="ml-auto text-xs text-fg-subtle">{fmtDateTime(a.atUtc)}</span>
              </div>
            ))}
          </div>
        </div>
      </Card>
    </div>
  );
}
