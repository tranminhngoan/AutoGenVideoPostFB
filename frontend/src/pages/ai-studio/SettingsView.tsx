import { useEffect, useState } from 'react';
import { CheckCircle2, Cpu, Gauge, HardDrive, RefreshCw, Save, XCircle } from 'lucide-react';
import { Alert, Button, Card, CardBody, CardHeader, Field, Input, Segmented, Skeleton } from '../../components/ui';
import { useToast } from '../../components/feedback';
import { formatDateTime } from '../../lib/format';
import { aiStudio, type Settings } from '../../services/aiStudio';
import { useAiStudio } from './bits';

export function SettingsView() {
  const toast = useToast();
  const { overview, refreshOverview } = useAiStudio();
  const [s, setS] = useState<Settings | null>(null);
  const [saving, setSaving] = useState(false);
  const [checking, setChecking] = useState(false);

  useEffect(() => {
    aiStudio.settings().then(setS).catch((e) => toast.error('Không tải được cài đặt', (e as Error).message));
  }, []);

  const e = overview?.engine;
  const save = async () => {
    if (!s) return;
    setSaving(true);
    try {
      setS(await aiStudio.saveSettings(s));
      toast.success('Đã lưu cài đặt');
    } catch (err) {
      toast.error('Không lưu được', (err as Error).message);
    } finally {
      setSaving(false);
    }
  };
  const check = async (bench: boolean) => {
    setChecking(true);
    try {
      await aiStudio.checkEngine(bench);
      toast.info('Đã gửi yêu cầu', 'Máy tính kiểm tra trong vài giây' + (bench ? ' và đo lại tốc độ (~1 phút).' : '.'));
      setTimeout(refreshOverview, 8000);
    } catch (err) {
      toast.error('Không gửi được', (err as Error).message);
    } finally {
      setChecking(false);
    }
  };

  const Ok = ({ v }: { v?: boolean }) => (v ? <CheckCircle2 className="h-4 w-4 text-success" /> : <XCircle className="h-4 w-4 text-fg-subtle" />);

  return (
    <div className="grid gap-5 lg:grid-cols-2">
      <Card>
        <CardHeader title="Máy phân tích" description={e?.machine ? `${e.machine} · ${e.cpuThreads} luồng CPU · ${e.ramGb} GB RAM` : 'Chưa nhận được báo cáo từ máy tính.'}
          actions={<>
            <Button size="sm" variant="ghost" icon={<RefreshCw className="h-4 w-4" />} loading={checking} onClick={() => check(false)}>Kiểm tra</Button>
            <Button size="sm" variant="secondary" icon={<Gauge className="h-4 w-4" />} loading={checking} onClick={() => check(true)}>Đo tốc độ</Button>
          </>} />
        <CardBody className="space-y-3 text-[13px]">
          {!overview ? <Skeleton className="h-40" /> : (
            <>
              {!overview.pcOnline && <Alert tone="warning" title="Máy tính đang tắt">Chạy <code>scripts\pc\start-pc-worker.bat</code>.</Alert>}
              {e?.error && <Alert tone="danger" title="Chưa sẵn sàng">{e.error} — chạy <code>scripts\pc\setup-ai-studio.bat</code>.</Alert>}
              <div className="grid grid-cols-2 gap-2">
                {[
                  ['llama.cpp · card đồ hoạ (Vulkan)', e?.backends?.vulkan],
                  ['llama.cpp · CPU (dự phòng)', e?.backends?.cpu],
                  ['Qwen3-VL 4B (chuẩn)', e?.models?.['4b']],
                  ['Qwen3-VL 8B (kỹ)', e?.models?.['8b']],
                  ['Whisper (nghe lời nói)', e?.whisper],
                  ['OpenCV ' + (e?.openCv ?? ''), !!e?.openCv],
                ].map(([label, ok]) => (
                  <div key={String(label)} className="flex items-center gap-2 rounded-lg bg-subtle px-3 py-2"><Ok v={!!ok} /><span className="text-fg">{label}</span></div>
                ))}
              </div>
              <div className="grid grid-cols-2 gap-2">
                {(['4b', '8b'] as const).map((m) => (
                  <div key={m} className="rounded-lg border border-line px-3 py-2">
                    <div className="flex items-center gap-1.5 text-xs text-fg-subtle"><Cpu className="h-3.5 w-3.5" />Tốc độ {m.toUpperCase()}</div>
                    <div className="font-semibold text-fg">{e?.tokensPerSecond?.[m] ? `${e.tokensPerSecond[m]} token/giây` : 'chưa đo'}</div>
                    <div className="text-[11px] text-fg-subtle">{e?.benchBackend?.[m] === 'vulkan' ? 'trên card đồ hoạ' : e?.benchBackend?.[m] === 'cpu' ? 'trên CPU' : ''}</div>
                  </div>
                ))}
              </div>
              {e?.freeDiskGb != null && <div className="flex items-center gap-2 text-xs text-fg-muted"><HardDrive className="h-3.5 w-3.5" />Ổ đĩa làm việc còn {e.freeDiskGb} GB · {e.home}</div>}
              {e?.checkedAtUtc && <div className="text-xs text-fg-subtle">Kiểm tra lúc {formatDateTime(e.checkedAtUtc)}</div>}
              <p className="text-xs leading-relaxed text-fg-muted">
                Mã nguồn & model: llama.cpp (MIT), Qwen3-VL GGUF chính thức (Apache-2.0), whisper.cpp + ggml-small (MIT), OpenCV (Apache-2.0). Tất cả chạy offline trên máy.
              </p>
            </>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Cài đặt" description="Áp dụng cho các lần phân tích sau." />
        <CardBody className="space-y-4">
          {!s ? <Skeleton className="h-64" /> : (
            <>
              <Field label="Model mặc định cho dự án mới">
                <Segmented value={s.defaultModel} onChange={(v) => setS({ ...s, defaultModel: v })} options={[{ value: '4b', label: '4B · nhanh' }, { value: '8b', label: '8B · kỹ' }]} />
              </Field>
              <Field label="Chạy model trên" hint="Tự động = card đồ hoạ (Vulkan) nếu được, không thì CPU.">
                <Segmented value={s.gpu} onChange={(v) => setS({ ...s, gpu: v })} options={[{ value: 'auto', label: 'Tự động' }, { value: 'cpu', label: 'Chỉ CPU' }]} />
              </Field>
              <div className="grid grid-cols-2 gap-3">
                <Field label="Khung hình mỗi cảnh" hint="3–10. Nhiều hơn = bắt hành động kỹ hơn nhưng chậm hơn.">
                  <Input type="number" min={3} max={10} value={s.maxFramesPerScene} onChange={(e) => setS({ ...s, maxFramesPerScene: Number(e.target.value) })} />
                </Field>
                <Field label="Luồng CPU" hint="0 = tự động.">
                  <Input type="number" min={0} max={64} value={s.threads} onChange={(e) => setS({ ...s, threads: Number(e.target.value) })} />
                </Field>
                <Field label="Giữ video tham chiếu (ngày)">
                  <Input type="number" min={1} max={60} value={s.sourceRetentionDays} onChange={(e) => setS({ ...s, sourceRetentionDays: Number(e.target.value) })} />
                </Field>
                <Field label="Giữ clip đã tạo (ngày)">
                  <Input type="number" min={1} max={90} value={s.clipRetentionDays} onChange={(e) => setS({ ...s, clipRetentionDays: Number(e.target.value) })} />
                </Field>
                <Field label="Giới hạn RAM (MB)" hint="Vượt thì dừng để máy không treo.">
                  <Input type="number" min={2048} max={65536} step={1024} value={s.maxRamMb} onChange={(e) => setS({ ...s, maxRamMb: Number(e.target.value) })} />
                </Field>
                <Field label="Thư mục làm việc" optional>
                  <Input value={s.workDir ?? ''} placeholder="D:\TramTinAI\work\ai-studio" onChange={(e) => setS({ ...s, workDir: e.target.value })} />
                </Field>
              </div>
              <label className="flex items-center gap-2 text-[13px]"><input type="checkbox" checked={s.transcribe} onChange={(e) => setS({ ...s, transcribe: e.target.checked })} />Nghe lời nói trong video (whisper) để AI hiểu cảnh hơn</label>
              <Button variant="primary" icon={<Save className="h-4 w-4" />} loading={saving} onClick={save}>Lưu cài đặt</Button>
            </>
          )}
        </CardBody>
      </Card>
    </div>
  );
}
