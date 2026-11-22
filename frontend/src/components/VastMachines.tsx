import { useEffect, useState } from 'react';
import { Copy, Cpu, HardDrive, MapPin, RotateCw, Search, ShoppingCart, Trash2, Wifi } from 'lucide-react';
import { Badge, Button, Checkbox, Drawer, EmptyState, Field, Input, Select, Skeleton, cn } from './ui';
import { useConfirm, useToast } from './feedback';
import { api, VastInstance, VastOffer } from '../services/api';

const usd = (v: number | null | undefined, digits = 2) => (v == null ? '—' : `$${v.toFixed(digits)}`);

function statusTone(s: string): 'success' | 'warning' | 'neutral' | 'danger' {
  const v = s.toLowerCase();
  if (v === 'running') return 'success';
  if (v === 'loading' || v === 'created' || v === 'scheduling') return 'warning';
  if (v === 'offline' || v === 'unknown') return 'danger';
  return 'neutral';
}

function statusLabel(s: string) {
  const v = s.toLowerCase();
  if (v === 'running') return 'Đang chạy';
  if (v === 'loading' || v === 'created') return 'Đang khởi động';
  if (v === 'exited' || v === 'stopped') return 'Đã tắt (vẫn tính phí ổ)';
  if (v === 'scheduling') return 'Chờ GPU trống';
  return s;
}

/* ───────────────────────── Machines on the account (shown on the main page) ───────────────────────── */

/**
 * The Vast.ai machines on the account, with "Xoá máy". Destroying (not stopping) is what ends all billing:
 * a stopped machine still pays for its disk. Reloads when `refreshKey` changes (e.g. after renting).
 */
export function VastInstanceList({ refreshKey, onChanged }: { refreshKey?: number; onChanged?: () => void }) {
  const toast = useToast();
  const confirm = useConfirm();
  const [instances, setInstances] = useState<VastInstance[] | null>(null);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [destroyingId, setDestroyingId] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const r = await api.listVastInstances();
      setInstances(r.instances);
      setActiveId(r.activeInstanceId ?? null);
    } catch (e: any) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    // a machine that is booting changes state on its own; check again every 30 s
    const t = window.setInterval(load, 30000);
    return () => window.clearInterval(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshKey]);

  const destroy = async (inst: VastInstance) => {
    const ok = await confirm({
      title: `Xoá vĩnh viễn máy #${inst.id}?`,
      description:
        'Toàn bộ ổ đĩa của máy (model đã tải, file tạm) bị xoá và không khôi phục được. ' +
        'Ngừng tính cả tiền GPU lẫn phí giữ ổ. Kịch bản, giọng và video thành phẩm vẫn nằm trên server, không mất.',
      confirmLabel: 'Xoá máy',
      danger: true,
    });
    if (!ok) return;
    setDestroyingId(inst.id);
    try {
      let r;
      try {
        r = await api.destroyVastInstance(inst.id);
      } catch (e: any) {
        if (!e.needsForce) throw e;
        const force = await confirm({ title: 'Máy có thể đang dựng phim', description: `${e.message} Vẫn xoá?`, confirmLabel: 'Vẫn xoá', danger: true });
        if (!force) return;
        r = await api.destroyVastInstance(inst.id, true);
      }
      toast.success(r.message);
      await load();
      onChanged?.();
    } catch (e: any) {
      toast.error(e.message);
    } finally {
      setDestroyingId(null);
    }
  };

  const copy = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      toast.success('Đã chép lệnh SSH');
    } catch {
      toast.error('Không chép được — hãy bôi đen và sao chép thủ công');
    }
  };

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-xs font-semibold text-fg">
          Máy Vast.ai của bạn {instances && <span className="font-normal text-fg-subtle">({instances.length})</span>}
        </h3>
        <Button size="sm" variant="ghost" icon={<RotateCw className={cn('h-3.5 w-3.5', loading && 'animate-spin')} />} onClick={load}>
          Làm mới
        </Button>
      </div>

      {error ? (
        <p className="rounded-lg border border-danger/25 bg-danger/[0.06] p-3 text-xs text-danger">{error}</p>
      ) : instances === null ? (
        <Skeleton className="h-16 w-full" />
      ) : instances.length === 0 ? (
        <p className="rounded-lg border border-line bg-subtle p-3 text-xs text-fg-muted">
          Chưa thuê máy nào — không bị tính tiền GPU hay phí ổ. Bấm "Thuê máy mới" khi cần dựng phim.
        </p>
      ) : (
        <ul className="space-y-2">
          {instances.map((inst) => (
            <li key={inst.id} className="rounded-lg border border-line bg-surface p-3 text-xs">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-semibold text-fg">{inst.gpuName || 'GPU'}</span>
                <span className="font-mono text-fg-subtle">#{inst.id}</span>
                <Badge tone={statusTone(inst.status)} dot pulse={inst.status === 'loading'}>
                  {statusLabel(inst.status)}
                </Badge>
                {activeId === inst.id && <Badge tone="info">Film Factory đang dùng</Badge>}
              </div>
              <div className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1 text-fg-muted sm:grid-cols-4">
                <span>Giá chạy: <b className="text-fg">{usd(inst.pricePerHour, 3)}/h</b></span>
                <span>Giữ ổ: <b className="text-fg">{usd(inst.storagePerMonth)}/tháng</b></span>
                <span>Ổ: {inst.diskGb ?? '—'} GB</span>
                <span className="truncate">{inst.location || inst.publicIp || ''}</span>
              </div>
              <div className="mt-2 flex flex-col gap-2 sm:flex-row sm:items-center">
                {inst.sshCommand && (
                  <div className="flex min-w-0 flex-1 items-center gap-1.5">
                    <code className="min-w-0 flex-1 truncate rounded bg-muted px-2 py-1 font-mono text-[11px] text-fg">{inst.sshCommand}</code>
                    <Button size="sm" variant="ghost" icon={<Copy className="h-3.5 w-3.5" />} onClick={() => copy(inst.sshCommand!)} aria-label="Chép lệnh SSH" />
                  </div>
                )}
                <Button
                  size="sm"
                  variant="danger-soft"
                  className="sm:ml-auto"
                  icon={<Trash2 className="h-3.5 w-3.5" />}
                  loading={destroyingId === inst.id}
                  onClick={() => destroy(inst)}
                >
                  Xoá máy
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/* ───────────────────────── Find & rent (top 10) ───────────────────────── */

const GPU_PRESETS: { key: string; label: string; names: string[]; hint: string }[] = [
  { key: 'pro6000', label: 'RTX PRO 6000 (96 GB)', names: ['RTX PRO 6000 WS', 'RTX PRO 6000 Max-Q', 'RTX PRO 6000 S'], hint: 'Khuyên dùng — Wan 14B nằm trọn trong VRAM' },
  { key: 'a100', label: 'A100 80 GB', names: ['A100 SXM4', 'A100 PCIE'], hint: 'Chạy được, chậm hơn' },
  { key: 'h100', label: 'H100 80–94 GB', names: ['H100 SXM', 'H100 PCIE', 'H100 NVL'], hint: 'Nhanh, thường đắt hơn' },
  { key: 'h200', label: 'H200 141 GB', names: ['H200', 'H200 NVL'], hint: 'Rất nhanh, đắt' },
];

/**
 * The 10 machines that suit the film worker best, ranked by the cost of one render session
 * (≈3 GPU hours + downloading the models once) — the server fetches a wider pool and sends only those 10.
 */
export function VastRentDrawer({ open, onClose, onRented }: { open: boolean; onClose: () => void; onRented?: () => void }) {
  const toast = useToast();
  const confirm = useConfirm();
  const [presets, setPresets] = useState<string[]>(['pro6000']);
  const [diskGb, setDiskGb] = useState(200);
  const [maxPrice, setMaxPrice] = useState('');
  const [maxDownload, setMaxDownload] = useState('0.01');
  const [minReliability, setMinReliability] = useState('0.98');
  const [offers, setOffers] = useState<VastOffer[] | null>(null);
  const [modelGb, setModelGb] = useState(110);
  const [searching, setSearching] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rentingId, setRentingId] = useState<number | null>(null);

  const search = async () => {
    setSearching(true);
    setError(null);
    try {
      const gpus = GPU_PRESETS.filter((p) => presets.includes(p.key)).flatMap((p) => p.names);
      const r = await api.searchVastOffers({
        gpus,
        diskGb,
        maxPrice: maxPrice ? Number(maxPrice) : null,
        maxDownloadCostPerGb: maxDownload === 'any' ? null : Number(maxDownload),
        minReliability: Number(minReliability),
      });
      setOffers(r.offers);
      setModelGb(r.modelDownloadGb);
    } catch (e: any) {
      setError(e.message);
      setOffers(null);
    } finally {
      setSearching(false);
    }
  };

  useEffect(() => {
    if (open) search();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const rent = async (o: VastOffer) => {
    const ok = await confirm({
      title: `Thuê máy ${o.gpuName} (#${o.id})?`,
      description:
        `Tiền GPU ${usd(o.pricePerHour, 3)}/giờ bắt đầu tính ngay (đã gồm ổ ${diskGb} GB). ` +
        `Máy tự tải model (~${modelGb} GB, phí tải của host ${usd(o.modelDownloadCostUsd)}) và bật worker, thường mất 10–20 phút. ` +
        `Dựng xong nhớ bấm "Xoá máy" — chỉ tắt thì vẫn bị tính phí giữ ổ ${usd(o.storagePerMonth)}/tháng.`,
      confirmLabel: 'Thuê máy',
    });
    if (!ok) return;
    setRentingId(o.id);
    try {
      const r = await api.rentVastOffer(o.id, diskGb);
      toast.success(r.message);
      onRented?.();
      onClose();
    } catch (e: any) {
      toast.error(e.message);
    } finally {
      setRentingId(null);
    }
  };

  return (
    <Drawer open={open} onClose={onClose} title="Thuê máy GPU mới" subtitle="10 máy hợp nhất với Film Factory — xếp theo chi phí một lần dựng">
      <div className="space-y-4">
        <div className="space-y-2 rounded-lg border border-line bg-subtle p-3">
          <p className="text-[13px] font-medium text-fg">Loại GPU</p>
          <div className="grid gap-2 sm:grid-cols-2">
            {GPU_PRESETS.map((p) => (
              <Checkbox
                key={p.key}
                checked={presets.includes(p.key)}
                onChange={(v) => setPresets((s) => (v ? [...s, p.key] : s.filter((k) => k !== p.key)))}
                label={p.label}
                description={p.hint}
              />
            ))}
          </div>
          <div className="grid grid-cols-2 gap-3 pt-1 sm:grid-cols-4">
            <Field label="Ổ đĩa (GB)" hint={diskGb < 150 ? 'Cần ~125 GB — dễ đầy ổ' : 'Model cần ~125 GB'}>
              <Input type="number" min={50} max={2000} value={diskGb} onChange={(e) => setDiskGb(Number(e.target.value) || 0)} />
            </Field>
            <Field label="Giá tối đa ($/h)" optional>
              <Input type="number" step="0.1" min={0} placeholder="Không giới hạn" value={maxPrice} onChange={(e) => setMaxPrice(e.target.value)} />
            </Field>
            <Field label="Phí tải về">
              <Select value={maxDownload} onChange={(e) => setMaxDownload(e.target.value)}>
                <option value="0.005">≤ $0.005/GB</option>
                <option value="0.01">≤ $0.01/GB</option>
                <option value="0.03">≤ $0.03/GB</option>
                <option value="any">Bất kỳ</option>
              </Select>
            </Field>
            <Field label="Độ ổn định">
              <Select value={minReliability} onChange={(e) => setMinReliability(e.target.value)}>
                <option value="0.95">≥ 95%</option>
                <option value="0.98">≥ 98%</option>
                <option value="0.99">≥ 99%</option>
              </Select>
            </Field>
          </div>
          <Button
            variant="primary"
            size="sm"
            className="w-full sm:w-auto"
            icon={<Search className="h-4 w-4" />}
            loading={searching}
            disabled={presets.length === 0 || diskGb < 50}
            onClick={search}
          >
            Tìm lại
          </Button>
        </div>

        {error ? (
          <p className="rounded-lg border border-danger/25 bg-danger/[0.06] p-3 text-xs text-danger">{error}</p>
        ) : offers === null ? (
          searching ? <Skeleton className="h-32 w-full" /> : null
        ) : offers.length === 0 ? (
          <EmptyState title="Không có máy nào khớp" description="Nới bộ lọc (phí tải về, độ ổn định, giá) hoặc thêm loại GPU." />
        ) : (
          <ul className="space-y-2.5">
            <li className="text-xs text-fg-subtle">
              Top {offers.length} máy. "Một lần dựng" = 3 giờ GPU + tải model ~{modelGb} GB. Giá đã gồm ổ {diskGb} GB.
            </li>
            {offers.map((o, i) => (
              <li key={o.id} className="rounded-lg border border-line bg-surface p-3 text-xs">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-fg-subtle">{i + 1}.</span>
                      <span className="font-semibold text-fg">{o.gpuName}</span>
                      <span className="text-fg-subtle">{o.gpuRamGb} GB</span>
                      {o.verification === 'verified' && <Badge tone="success">Verified</Badge>}
                    </div>
                    <div className="mt-0.5 flex items-center gap-1 text-fg-muted">
                      <MapPin className="h-3 w-3 shrink-0" />
                      <span className="truncate">{o.location || '—'}</span>
                    </div>
                  </div>
                  <div className="shrink-0 text-right">
                    <div className="text-base font-bold text-fg">{usd(o.pricePerHour, 3)}<span className="text-xs font-normal text-fg-muted">/h</span></div>
                    <div className="text-[11px] text-fg-subtle">Một lần dựng ≈ {usd(o.sessionCostUsd)}</div>
                  </div>
                </div>

                <div className="mt-2.5 grid grid-cols-2 gap-x-3 gap-y-1 text-fg-muted sm:grid-cols-3">
                  <span className="flex items-center gap-1"><Cpu className="h-3 w-3" />{Math.round(o.cpuCores)} luồng · {o.ramGb} GB RAM</span>
                  <span className="flex items-center gap-1"><HardDrive className="h-3 w-3" />trống {o.diskAvailableGb} GB · {Math.round(o.diskMbps)} MB/s</span>
                  <span className="flex items-center gap-1"><Wifi className="h-3 w-3" />↓{o.downloadMbps} / ↑{o.uploadMbps} Mbps</span>
                  <span>CUDA {o.cudaMax} · ổn định {(o.reliability * 100).toFixed(1)}%</span>
                  <span>Tải model: {usd(o.modelDownloadCostUsd)}</span>
                  <span>Giữ ổ khi tắt: {usd(o.storagePerMonth)}/tháng</span>
                </div>

                <div className="mt-2.5 flex justify-end border-t border-line pt-2.5">
                  <Button
                    size="sm"
                    variant="primary"
                    className="w-full sm:w-auto"
                    icon={<ShoppingCart className="h-3.5 w-3.5" />}
                    loading={rentingId === o.id}
                    disabled={rentingId !== null}
                    onClick={() => rent(o)}
                  >
                    Thuê máy này
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Drawer>
  );
}
