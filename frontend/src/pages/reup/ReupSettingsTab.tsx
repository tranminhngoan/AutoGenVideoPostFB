import { useEffect, useState } from 'react';
import { Gauge, RefreshCw, Save, ShieldAlert } from 'lucide-react';
import { Alert, Badge, Button, Card, CardBody, CardHeader, Checkbox, Field, Input, Segmented, Select, Skeleton, Textarea } from '../../components/ui';
import { useToast } from '../../components/feedback';
import { formatDateTime } from '../../lib/format';
import { reup, ReupSettings } from '../../services/reup';
import { useReup } from './ReupPage';

export function ReupSettingsTab() {
  const { fail, overview, refreshOverview } = useReup();
  const toast = useToast();
  const [s, setS] = useState<ReupSettings | null>(null);
  const [saving, setSaving] = useState(false);
  const [checking, setChecking] = useState<boolean | null>(null);

  useEffect(() => {
    reup.settings().then(setS).catch((e) => fail(e, 'Không tải được cài đặt.'));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (!s) return <Skeleton className="h-96 w-full" />;
  const set = (p: Partial<ReupSettings>) => setS({ ...s, ...p });
  const e = overview?.engine;

  const save = async () => {
    setSaving(true);
    try {
      setS(await reup.saveSettings(s));
      toast.success('Đã lưu cài đặt.');
      refreshOverview();
    } catch (err) {
      fail(err, 'Không lưu được.');
    } finally {
      setSaving(false);
    }
  };

  const check = async (bench: boolean) => {
    setChecking(bench);
    try {
      await reup.checkEngine(bench);
      toast.info(overview?.pcOnline ? 'Đã gửi yêu cầu — máy tính báo lại sau vài giây' + (bench ? ' (đo tốc độ ~20 giây).' : '.') : 'Máy tính đang tắt — sẽ kiểm tra khi máy bật.');
      setTimeout(refreshOverview, bench ? 25000 : 6000);
    } catch (err) {
      fail(err);
    } finally {
      setChecking(null);
    }
  };

  return (
    <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_400px]">
      <div className="space-y-5">
        <Card>
          <CardHeader title="Xử lý trên máy tính" description="Áp dụng cho video bắt đầu sau khi lưu." />
          <CardBody className="grid gap-4 sm:grid-cols-2">
            <Field label="Số luồng CPU" hint={`0 = tự động (tối đa 14 — trên Xeon 2 CPU, dùng nhiều hơn không nhanh hơn). Máy có ${e?.cpuThreads ?? '?'} luồng.`}>
              <Input type="number" min={0} max={64} value={s.threads} onChange={(ev) => set({ threads: Number(ev.target.value) })} />
            </Field>
            <Field label="Giới hạn RAM (MB)" hint={`Engine bị dừng nếu dùng quá mức này, để máy không treo. Máy có ${e?.ramGb ?? '?'} GB.`}>
              <Input type="number" min={1024} step={512} value={s.maxRamMb} onChange={(ev) => set({ maxRamMb: Number(ev.target.value) })} />
            </Field>
            <Field label="Bộ xử lý" hint="DirectML dùng card đồ hoạ sẵn có trong máy (không phải GPU thuê). Cần cài engine bằng setup-reup-engine.bat -DirectML.">
              <Segmented
                value={s.executionProvider}
                onChange={(v) => set({ executionProvider: v })}
                options={[
                  { value: 'cpu', label: 'CPU' },
                  { value: 'directml', label: 'Card đồ hoạ (DirectML)' },
                ]}
              />
            </Field>
            <Field label="Thư mục tạm trên máy tính" optional hint={`Để trống = ${e?.home ? e.home + '\\work' : '<thư mục engine>\\work'}. Nên đặt ở ổ còn nhiều chỗ trống.`}>
              <Input value={s.workDir ?? ''} onChange={(ev) => set({ workDir: ev.target.value })} placeholder="D:\TramTinAI\work" />
            </Field>
            <Field label="Tốc độ nén H.264" hint="Chậm hơn = file nhỏ hơn cùng chất lượng.">
              <Select value={s.preset} onChange={(ev) => set({ preset: ev.target.value })}>
                <option value="veryfast">veryfast</option>
                <option value="faster">faster</option>
                <option value="fast">fast</option>
                <option value="medium">medium (khuyên dùng)</option>
                <option value="slow">slow</option>
              </Select>
            </Field>
            <Field label="Chất lượng (CRF)" hint="14–28; càng nhỏ càng nét, file càng lớn. 18 ≈ gần như không mất chất lượng.">
              <Input type="number" min={14} max={28} value={s.crf} onChange={(ev) => set({ crf: Number(ev.target.value) })} />
            </Field>
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Lưu trữ" description="Kho file R2 miễn phí 10 GB — tool tự dọn để không vượt." />
          <CardBody className="grid gap-4 sm:grid-cols-2">
            <Field label="Giữ video nguồn + kết quả (ngày)" hint="Sau đó tự xoá khỏi kho file (ảnh xem trước và lịch sử vẫn giữ).">
              <Input type="number" min={1} max={60} value={s.outputRetentionDays} onChange={(ev) => set({ outputRetentionDays: Number(ev.target.value) })} />
            </Field>
            <Field label="Hạn mức thư viện cảnh (MB)" hint={`Đang dùng ${overview?.backdropStorageMb.toFixed(0) ?? '?'} MB.`}>
              <Input type="number" min={200} max={9000} step={100} value={s.backdropQuotaMb} onChange={(ev) => set({ backdropQuotaMb: Number(ev.target.value) })} />
            </Field>
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Tìm cảnh miễn phí" description="Khoá API miễn phí, chỉ dùng để tìm; mỗi lần thêm một file do bạn chọn." />
          <CardBody className="grid gap-4 sm:grid-cols-2">
            <Field label="Khoá Pexels API" optional hint="Đăng ký miễn phí: pexels.com/api">
              <Input type="password" autoComplete="off" value={s.pexelsApiKey ?? ''} onChange={(ev) => set({ pexelsApiKey: ev.target.value })} />
            </Field>
            <Field label="Khoá Pixabay API" optional hint="Đăng ký miễn phí: pixabay.com/api/docs">
              <Input type="password" autoComplete="off" value={s.pixabayApiKey ?? ''} onChange={(ev) => set({ pixabayApiKey: ev.target.value })} />
            </Field>
          </CardBody>
        </Card>

        <Card className="border-warning/40">
          <CardHeader title="Model thay khuôn mặt (giấy phép)" />
          <CardBody className="space-y-3 text-[13px]">
            <Alert tone="warning" icon={<ShieldAlert />} title="Vì sao thay mặt đang khoá">
              Các model thay mặt tốt và phổ biến (inswapper_128 + ArcFace/buffalo_l của InsightFace, HyperSwap, SimSwap…) chỉ cho dùng <b>phi thương mại</b>.
              Đăng Facebook có kiếm tiền là dùng thương mại, nên tool không tự tải các model này. Khi bạn có <b>giấy phép thương mại</b> (ví dụ mua của
              InsightFace), đặt 2 file vào <code>{e?.home ?? 'D:\\TramTinAI'}\models\licensed\</code>: <code>swapper.onnx</code> (kiểu inswapper) và{' '}
              <code>identity.onnx</code> (ArcFace đi kèm), rồi xác nhận dưới đây.
            </Alert>
            <div className="flex flex-wrap gap-2 text-xs">
              <Badge tone={e?.models?.swapper ? 'success' : 'neutral'}>swapper.onnx: {e?.models?.swapper ? 'có trên máy' : 'chưa có'}</Badge>
              <Badge tone={e?.models?.identity ? 'success' : 'neutral'}>identity.onnx: {e?.models?.identity ? 'có trên máy' : 'chưa có'}</Badge>
            </div>
            <Field label="Giấy phép thương mại" hint="Bên cấp phép, số hợp đồng / hoá đơn, phạm vi sử dụng.">
              <Textarea rows={2} value={s.swapperLicenseNote ?? ''} onChange={(ev) => set({ swapperLicenseNote: ev.target.value })} />
            </Field>
            <Checkbox
              checked={s.swapperLicenseConfirmed}
              onChange={(v) => set({ swapperLicenseConfirmed: v })}
              label="Tôi có giấy phép dùng thương mại cho 2 model trên"
              description="Bạn chịu trách nhiệm về giấy phép này. Không có xác nhận thì phần thay mặt luôn khoá."
            />
          </CardBody>
        </Card>

        <Button variant="primary" icon={<Save className="h-4 w-4" />} loading={saving} onClick={save}>
          Lưu cài đặt
        </Button>
      </div>

      <div className="space-y-5 xl:sticky xl:top-4 xl:self-start">
        <Card>
          <CardHeader title="Engine trên máy tính" description={e?.checkedAtUtc ? `Báo cáo lúc ${formatDateTime(e.checkedAtUtc)}` : undefined} />
          <CardBody className="space-y-3 text-[13px]">
            <div className="flex items-center gap-2">
              <Badge tone={overview?.pcOnline ? 'success' : 'neutral'} dot>
                {overview?.pcOnline ? 'Máy tính đang bật' : 'Máy tính đang tắt'}
              </Badge>
              <Badge tone={e?.ready ? 'success' : 'danger'}>{e?.ready ? 'Engine sẵn sàng' : 'Engine chưa sẵn sàng'}</Badge>
            </div>
            {e?.error && <p className="text-xs text-danger">{e.error}</p>}
            <dl className="grid grid-cols-[110px_1fr] gap-x-3 gap-y-1 text-xs">
              <dt className="text-fg-muted">Máy</dt>
              <dd className="text-fg">{e?.machine ?? '—'}</dd>
              <dt className="text-fg-muted">Thư mục</dt>
              <dd className="break-all text-fg">{e?.home ?? '—'}</dd>
              <dt className="text-fg-muted">Python / ORT</dt>
              <dd className="text-fg">
                {e?.python ?? '—'} / {e?.onnxRuntime ?? '—'}
              </dd>
              <dt className="text-fg-muted">OpenCV</dt>
              <dd className="text-fg">{e?.openCv ?? '—'}</dd>
              <dt className="text-fg-muted">Bộ xử lý</dt>
              <dd className="text-fg">{e?.providers?.filter((p) => p !== 'AzureExecutionProvider').join(', ') || '—'}</dd>
              <dt className="text-fg-muted">Model</dt>
              <dd className="text-fg">
                {e?.models
                  ? Object.entries(e.models)
                      .map(([k, v]) => `${k} ${v ? '✓' : '✗'}`)
                      .join(' · ')
                  : '—'}
              </dd>
              <dt className="text-fg-muted">Tốc độ thay nền</dt>
              <dd className="text-fg">
                {e?.backgroundFps && Object.keys(e.backgroundFps).length
                  ? Object.entries(e.backgroundFps)
                      .map(([k, v]) => `${k}: ${v} khung/s`)
                      .join(' · ')
                  : 'chưa đo'}
              </dd>
              <dt className="text-fg-muted">Ổ tạm trống</dt>
              <dd className="text-fg">{e?.freeDiskGb != null ? `${e.freeDiskGb} GB` : '—'}</dd>
            </dl>
            <div className="flex flex-wrap gap-2">
              <Button size="sm" icon={<RefreshCw className="h-4 w-4" />} loading={checking === false} onClick={() => check(false)}>
                Kiểm tra lại
              </Button>
              <Button size="sm" icon={<Gauge className="h-4 w-4" />} loading={checking === true} onClick={() => check(true)}>
                Đo tốc độ
              </Button>
            </div>
            <div className="rounded-md bg-subtle p-3 text-xs text-fg-muted">
              <div className="mb-1 font-medium text-fg">Cài engine (một lần, trên máy tính)</div>
              Bấm đúp <code>scripts\pc\setup-reup-engine.bat</code> — tạo Python venv, cài ONNX Runtime + OpenCV, tải ~55 MB model miễn phí (YuNet MIT,
              SFace Apache-2.0, Robust Video Matting GPL-3.0 dùng nội bộ), kiểm tra SHA-256. Sau đó xử lý video hoàn toàn offline.
            </div>
          </CardBody>
        </Card>
      </div>
    </div>
  );
}
