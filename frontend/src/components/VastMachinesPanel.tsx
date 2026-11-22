import { useEffect, useState } from 'react';
import { Copy, Cpu, HardDrive, MapPin, RotateCw, Search, ShoppingCart, Trash2, Wifi } from 'lucide-react';
import { Badge, Button, Checkbox, Drawer, EmptyState, Field, Input, Select, Skeleton, cn } from './ui';
import { useConfirm, useToast } from './feedback';
import { api, VastInstance, VastOffer } from '../services/api';

const GPU_PRESETS: { key: string; label: string; names: string[]; hint: string }[] = [
  { key: 'pro6000', label: 'RTX PRO 6000 (96 GB)', names: ['RTX PRO 6000 WS', 'RTX PRO 6000 Max-Q', 'RTX PRO 6000 S'], hint: 'Khuyên dùng — Wan 14B nằm trọn trong VRAM' },
  { key: 'a100', label: 'A100 80 GB', names: ['A100 SXM4', 'A100 PCIE'], hint: 'Chạy được, chậm hơn' },
  { key: 'h100', label: 'H100 80–94 GB', names: ['H100 SXM', 'H100 PCIE', 'H100 NVL'], hint: 'Nhanh, thường đắt hơn' },
  { key: 'h200', label: 'H200 141 GB', names: ['H200', 'H200 NVL'], hint: 'Rất nhanh, đắt' },
];

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

/**
 * Find, rent and destroy Vast.ai machines for the film worker. Destroying (not stopping) is what ends all billing:
 * a stopped machine still pays for its disk.
 */
export function VastMachinesPanel({ open, onClose, onChanged }: { open: boolean; onClose: () => void; onChanged?: () => void }) {
  const toast = useToast();
  const confirm = useConfirm();

  const [instances, setInstances] = useState<VastInstance[] | null>(null);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [instancesError, setInstancesError] = useState<string | null>(null);
  const [loadingInstances, setLoadingInstances] = useState(false);
  const [destroyingId, setDestroyingId] = useState<string | null>(null);

  const [presets, setPresets] = useState<string[]>(['pro6000']);
  const [diskGb, setDiskGb] = useState(200);
  const [maxPrice, setMaxPrice] = useState('');
  const [maxDownload, setMaxDownload] = useState('0.01');
  const [minReliability, setMinReliability] = useState('0.98');
  const [offers, setOffers] = useState<VastOffer[] | null>(null);
  const [modelGb, setModelGb] = useState(110);
  const [searching, setSearching] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [rentingId, setRentingId] = useState<number | null>(null);

  const loadInstances = async () => {
    setLoadingInstances(true);
    setInstancesError(null);
    try {
      const r = await api.listVastInstances();
      setInstances(r.instances);
      setActiveId(r.activeInstanceId ?? null);
    } catch (e: any) {
      setInstancesError(e.message);
    } finally {
      setLoadingInstances(false);
    }
  };

  const search = async () => {
    setSearching(true);
    setSearchError(null);
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
      setSearchError(e.message);
      setOffers(null);
    } finally {
      setSearching(false);
    }
  };

  useEffect(() => {
    if (!open) return;
    loadInstances();
    if (offers === null) search();
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
      setOffers((list) => list?.filter((x) => x.id !== o.id) ?? null);
      await loadInstances();
      onChanged?.();
    } catch (e: any) {
      toast.error(e.message);
    } finally {
      setRentingId(null);
    }
  };

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
        const force = await confirm({
          title: 'Máy có thể đang dựng phim',
          description: `${e.message} Vẫn xoá?`,
          confirmLabel: 'Vẫn xoá',
          danger: true,
        });
        if (!force) return;
        r = await api.destroyVastInstance(inst.id, true);
      }
      toast.success(r.message);
      await loadInstances();
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
    <Drawer open={open} onClose={onClose} title="Quản lý máy GPU Vast.ai" subtitle="Thuê khi cần dựng phim — xoá khi xong để không mất phí giữ ổ">
      <div className="space-y-7">
        {/* ── Máy đang thuê ── */}
        <section className="space-y-3">
          <div className="flex items-center justify-between gap-2">
            <h3 className="text-sm font-semibold text-fg">Máy đang thuê</h3>
            <Button size="sm" variant="ghost" icon={<RotateCw className={cn('h-3.5 w-3.5', loadingInstances && 'animate-spin')} />} onClick={loadInstances}>
              Làm mới
            </Button>
          </div>

          {instancesError ? (
            <p className="rounded-lg border border-danger/25 bg-danger/[0.06] p-3 text-xs text-danger">{instancesError}</p>
          ) : instances === null ? (
            <Skeleton className="h-20 w-full" />
          ) : instances.length === 0 ? (
            <p className="rounded-lg border border-line bg-subtle p-3 text-xs text-fg-muted">Không có máy nào — không bị tính tiền GPU hay phí ổ.</p>
          ) : (
            <ul className="space-y-2.5">
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
                  {inst.sshCommand && (
                    <div className="mt-2 flex items-center gap-1.5">
                      <code className="min-w-0 flex-1 truncate rounded bg-muted px-2 py-1 font-mono text-[11px] text-fg">{inst.sshCommand}</code>
                      <Button size="sm" variant="ghost" icon={<Copy className="h-3.5 w-3.5" />} onClick={() => copy(inst.sshCommand!)} aria-label="Chép lệnh SSH" />
                    </div>
                  )}
                  <div className="mt-2.5 flex justify-end">
                    <Button
                      size="sm"
                      variant="danger-soft"
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
        </section>

        {/* ── Tìm máy ── */}
        <section className="space-y-3">
          <h3 className="text-sm font-semibold text-fg">Tìm máy để thuê</h3>

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
              Tìm máy
            </Button>
          </div>

          {searchError ? (
            <p className="rounded-lg border border-danger/25 bg-danger/[0.06] p-3 text-xs text-danger">{searchError}</p>
          ) : offers === null ? (
            searching ? <Skeleton className="h-32 w-full" /> : null
          ) : offers.length === 0 ? (
            <EmptyState title="Không có máy nào khớp" description="Nới bộ lọc (phí tải về, độ ổn định, giá) hoặc thêm loại GPU." />
          ) : (
            <ul className="space-y-2.5">
              <li className="text-xs text-fg-subtle">{offers.length} máy, rẻ nhất trước. Giá đã gồm ổ {diskGb} GB.</li>
              {offers.map((o) => (
                <li key={o.id} className="rounded-lg border border-line bg-surface p-3 text-xs">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
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
                      <div className="text-[11px] text-fg-subtle">GPU {usd(o.gpuPricePerHour, 3)} + ổ {usd(o.storagePerHour, 3)}</div>
                    </div>
                  </div>

                  <div className="mt-2.5 grid grid-cols-2 gap-x-3 gap-y-1 text-fg-muted sm:grid-cols-3">
                    <span className="flex items-center gap-1"><Cpu className="h-3 w-3" />{Math.round(o.cpuCores)} luồng · {o.ramGb} GB RAM</span>
                    <span className="flex items-center gap-1"><HardDrive className="h-3 w-3" />trống {o.diskAvailableGb} GB · {Math.round(o.diskMbps)} MB/s</span>
                    <span className="flex items-center gap-1"><Wifi className="h-3 w-3" />↓{o.downloadMbps} / ↑{o.uploadMbps} Mbps</span>
                    <span>CUDA {o.cudaMax} · ổn định {(o.reliability * 100).toFixed(1)}%</span>
                    <span>Thuê tối đa {o.maxDurationDays} ngày</span>
                    <span>Máy #{o.machineId}</span>
                  </div>

                  <div className="mt-2.5 flex flex-col gap-2 border-t border-line pt-2.5 sm:flex-row sm:items-center sm:justify-between">
                    <div className="space-y-0.5 text-fg-muted">
                      <div>Tải model lần đầu (~{modelGb} GB): <b className="text-fg">{usd(o.modelDownloadCostUsd)}</b></div>
                      <div>Nếu chỉ tắt mà không xoá: <b className="text-fg">{usd(o.storagePerMonth)}/tháng</b> phí giữ ổ</div>
                    </div>
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
        </section>
      </div>
    </Drawer>
  );
}
