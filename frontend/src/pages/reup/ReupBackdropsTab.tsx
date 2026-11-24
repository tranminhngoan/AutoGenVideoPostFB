import { useEffect, useState, type ReactNode } from 'react';
import { ExternalLink, ImagePlus, Images, Search, Trash2 } from 'lucide-react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  EmptyState,
  Field,
  Input,
  Modal,
  ProgressBar,
  Segmented,
  Select,
  Skeleton,
  Spinner,
  Thumb,
} from '../../components/ui';
import { useConfirm, useToast } from '../../components/feedback';
import { formatBytes, formatDateTime } from '../../lib/format';
import { href } from '../../lib/router';
import { BackdropInfo, CATEGORIES, ReupBackdrop, StockItem, categoryLabel, reup } from '../../services/reup';
import { useReup } from './ReupPage';
import { Dropzone, seconds } from './ReupBits';

export function ReupBackdropsTab() {
  const { fail, overview, refreshOverview } = useReup();
  const toast = useToast();
  const confirm = useConfirm();
  const [items, setItems] = useState<ReupBackdrop[] | null>(null);
  const [category, setCategory] = useState('');
  const [kind, setKind] = useState('');
  const [q, setQ] = useState('');
  const [adding, setAdding] = useState(false);
  const [viewing, setViewing] = useState<ReupBackdrop | null>(null);

  const load = () => reup.backdrops(category, kind, q).then(setItems).catch((e) => fail(e, 'Không tải được thư viện cảnh.'));
  useEffect(() => {
    const t = setTimeout(load, 250);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [category, kind, q]);

  const remove = async (b: ReupBackdrop) => {
    if (!(await confirm({ title: `Xoá cảnh "${b.name}"?`, confirmLabel: 'Xoá', danger: true }))) return;
    try {
      await reup.deleteBackdrop(b.id);
      setViewing(null);
      load();
      refreshOverview();
    } catch (e) {
      fail(e);
    }
  };

  const used = overview?.backdropStorageMb ?? 0;
  const quota = overview?.backdropQuotaMb ?? 3072;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <Input leading={<Search />} value={q} onChange={(e) => setQ(e.target.value)} placeholder="Tìm theo tên, thẻ, tác giả…" className="w-60" />
          <Select value={category} onChange={(e) => setCategory(e.target.value)} className="w-44">
            <option value="">Mọi loại cảnh</option>
            {CATEGORIES.map((c) => (
              <option key={c.id} value={c.id}>
                {c.label}
              </option>
            ))}
          </Select>
          <Segmented
            size="sm"
            value={kind}
            onChange={setKind}
            options={[
              { value: '', label: 'Tất cả' },
              { value: 'Image', label: 'Ảnh' },
              { value: 'Video', label: 'Video' },
            ]}
          />
        </div>
        <div className="flex items-center gap-3">
          <div className="w-56 text-xs text-fg-muted">
            <div className="mb-1 flex justify-between">
              <span className="whitespace-nowrap">Dung lượng thư viện</span>
              <span className="tabular">
                {used.toFixed(0)}/{quota} MB
              </span>
            </div>
            <ProgressBar value={(used / quota) * 100} tone={used / quota > 0.85 ? 'warning' : 'info'} />
          </div>
          <Button variant="primary" icon={<ImagePlus className="h-4 w-4" />} onClick={() => setAdding(true)}>
            Thêm cảnh
          </Button>
        </div>
      </div>

      {items == null ? (
        <Skeleton className="h-64 w-full" />
      ) : items.length === 0 ? (
        <Card>
          <EmptyState
            icon={<Images />}
            title={q || category || kind ? 'Không có cảnh phù hợp' : 'Thư viện cảnh đang trống'}
            description="Thêm ảnh / video nền: tự tải lên, dán link một file, hoặc tìm trên Pexels / Pixabay (miễn phí). Mỗi file lưu kèm nguồn và giấy phép."
          />
        </Card>
      ) : (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6">
          {items.map((b) => (
            <button key={b.id} type="button" onClick={() => setViewing(b)} className="group overflow-hidden rounded-lg border border-line bg-surface text-left shadow-card transition-colors hover:border-line-strong">
              <div className="relative">
                <Thumb src={b.hasThumb ? reup.backdropThumbUrl(b.id) : reup.backdropFileUrl(b.id)} className="aspect-[9/16] w-full" />
                <span className="absolute left-1.5 top-1.5 rounded bg-black/60 px-1.5 py-0.5 text-[10px] font-medium text-white">
                  {b.kind === 'Video' ? `Video ${seconds(b.durationSeconds)}` : 'Ảnh'}
                </span>
              </div>
              <div className="space-y-0.5 px-2 py-1.5">
                <div className="truncate text-xs font-medium text-fg">{b.name}</div>
                <div className="truncate text-[11px] text-fg-muted">
                  {categoryLabel(b.category)} · {b.sourceName}
                </div>
              </div>
            </button>
          ))}
        </div>
      )}

      <AddBackdrop
        open={adding}
        onClose={() => setAdding(false)}
        onAdded={() => {
          load();
          refreshOverview();
          toast.success('Đã thêm cảnh vào thư viện.');
        }}
      />
      {viewing && (
        <Modal
          open
          onClose={() => setViewing(null)}
          size="lg"
          title={viewing.name}
          description={`${categoryLabel(viewing.category)} · ${viewing.width}×${viewing.height}${viewing.durationSeconds ? ` · ${seconds(viewing.durationSeconds)}` : ''} · ${formatBytes(viewing.sizeBytes)}`}
          footer={
            <Button variant="danger-soft" icon={<Trash2 className="h-4 w-4" />} onClick={() => remove(viewing)}>
              Xoá cảnh
            </Button>
          }
        >
          <div className="grid gap-4 sm:grid-cols-[1fr_1fr]">
            {viewing.kind === 'Video' ? (
              <video src={reup.backdropFileUrl(viewing.id)} controls autoPlay muted loop className="max-h-[60vh] w-full rounded-lg bg-black object-contain" />
            ) : (
              <img src={reup.backdropFileUrl(viewing.id)} alt="" className="max-h-[60vh] w-full rounded-lg bg-black object-contain" />
            )}
            <dl className="space-y-2 text-[13px]">
              <Row k="Nguồn" v={viewing.sourceUrl ? <a className="inline-flex items-center gap-1 text-accent hover:underline" href={viewing.sourceUrl} target="_blank" rel="noreferrer">{viewing.sourceName} <ExternalLink className="h-3 w-3" /></a> : viewing.sourceName} />
              <Row k="Tác giả" v={viewing.author ?? '—'} />
              <Row k="Giấy phép" v={viewing.licenseUrl ? <a className="text-accent hover:underline" href={viewing.licenseUrl} target="_blank" rel="noreferrer">{viewing.licenseName}</a> : viewing.licenseName} />
              <Row k="Ngày tải" v={formatDateTime(viewing.downloadedAtUtc)} />
              <Row k="Thẻ" v={viewing.tags ?? '—'} />
              <Row k="Đã dùng" v={`${viewing.useCount} video`} />
            </dl>
          </div>
        </Modal>
      )}
    </div>
  );
}

function Row({ k, v }: { k: string; v: ReactNode }) {
  return (
    <div className="flex gap-3">
      <dt className="w-20 shrink-0 text-fg-muted">{k}</dt>
      <dd className="min-w-0 break-words text-fg">{v}</dd>
    </div>
  );
}

type Mode = 'upload' | 'link' | 'stock';

function AddBackdrop({ open, onClose, onAdded }: { open: boolean; onClose: () => void; onAdded: () => void }) {
  const { fail, overview } = useReup();
  const [mode, setMode] = useState<Mode>('upload');
  const [info, setInfo] = useState<BackdropInfo>({ category: 'Beach', sourceName: 'Upload' });
  const [url, setUrl] = useState('');
  const [progress, setProgress] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const set = (p: Partial<BackdropInfo>) => setInfo({ ...info, ...p });

  const upload = async (file: File) => {
    if (info.sourceName && info.sourceName !== 'Upload' && !info.licenseName?.trim()) return fail(new Error('Ghi rõ giấy phép của file trước khi tải lên.'));
    setProgress(0);
    try {
      await reup.addBackdrop(file, info, setProgress);
      onAdded();
      onClose();
    } catch (e) {
      fail(e, 'Không thêm được cảnh.');
    } finally {
      setProgress(null);
    }
  };

  const importLink = async () => {
    setBusy(true);
    try {
      await reup.importBackdrop(url, info);
      setUrl('');
      onAdded();
      onClose();
    } catch (e) {
      fail(e, 'Không tải được file từ link.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal open={open} onClose={onClose} size="lg" title="Thêm cảnh nền" description="Mỗi lần thêm một file — không tải hàng loạt. Nguồn, tác giả, giấy phép và ngày tải được lưu kèm.">
      <div className="space-y-4">
        <Segmented
          value={mode}
          onChange={(m) => {
            setMode(m);
            set({ sourceName: m === 'upload' ? 'Upload' : 'Other' });
          }}
          options={[
            { value: 'upload', label: 'Tải lên' },
            { value: 'link', label: 'Từ link' },
            { value: 'stock', label: 'Tìm Pexels / Pixabay' },
          ]}
        />
        {mode === 'stock' ? (
          <StockSearch category={info.category ?? 'Other'} onCategory={(c) => set({ category: c })} onAdded={onAdded} pexels={overview?.stockSearchPexels} pixabay={overview?.stockSearchPixabay} />
        ) : (
          <>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Tên cảnh" optional>
                <Input value={info.name ?? ''} onChange={(e) => set({ name: e.target.value })} placeholder="Mặc định: tên file" />
              </Field>
              <Field label="Loại cảnh">
                <Select value={info.category} onChange={(e) => set({ category: e.target.value })}>
                  {CATEGORIES.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.label}
                    </option>
                  ))}
                </Select>
              </Field>
              <Field label="Nguồn">
                <Select value={info.sourceName} onChange={(e) => set({ sourceName: e.target.value })}>
                  {mode === 'upload' && <option value="Upload">Của tôi (tự quay / tự chụp)</option>}
                  <option value="Pexels">Pexels</option>
                  <option value="Pixabay">Pixabay</option>
                  <option value="Other">Nguồn khác</option>
                </Select>
              </Field>
              <Field label="Thẻ" optional>
                <Input value={info.tags ?? ''} onChange={(e) => set({ tags: e.target.value })} placeholder="hoàng hôn, cát, sóng" />
              </Field>
              {info.sourceName !== 'Upload' && (
                <>
                  <Field label="Trang gốc của file" optional={mode === 'link'}>
                    <Input value={info.sourceUrl ?? ''} onChange={(e) => set({ sourceUrl: e.target.value })} placeholder="https://www.pexels.com/photo/…" />
                  </Field>
                  <Field label="Tác giả" optional>
                    <Input value={info.author ?? ''} onChange={(e) => set({ author: e.target.value })} />
                  </Field>
                  <Field label="Giấy phép" hint="Bắt buộc. Chỉ dùng file cho phép sử dụng và chỉnh sửa.">
                    <Input value={info.licenseName ?? ''} onChange={(e) => set({ licenseName: e.target.value })} placeholder="Pexels License / Pixabay Content License / CC0…" />
                  </Field>
                  <Field label="Link giấy phép" optional>
                    <Input value={info.licenseUrl ?? ''} onChange={(e) => set({ licenseUrl: e.target.value })} placeholder="https://www.pexels.com/license/" />
                  </Field>
                </>
              )}
            </div>
            {mode === 'upload' ? (
              <Dropzone accept="image/*,video/*" label="Kéo thả ảnh hoặc video nền (tối đa 400 MB)" hint="Nên dùng dọc 9:16, cạnh ngắn ≥ 1080 px." progress={progress} onFile={upload} />
            ) : (
              <div className="flex flex-col gap-2 sm:flex-row">
                <Input value={url} onChange={(e) => setUrl(e.target.value)} placeholder="Link trực tiếp tới file ảnh / video (https://…)" />
                <Button variant="primary" loading={busy} disabled={!url.trim() || !info.licenseName?.trim()} onClick={importLink}>
                  Tải về thư viện
                </Button>
              </div>
            )}
          </>
        )}
      </div>
    </Modal>
  );
}

function StockSearch({ category, onCategory, onAdded, pexels, pixabay }: { category: string; onCategory: (c: string) => void; onAdded: () => void; pexels?: boolean; pixabay?: boolean }) {
  const { fail } = useReup();
  const [provider, setProvider] = useState<'Pexels' | 'Pixabay'>(pexels || !pixabay ? 'Pexels' : 'Pixabay');
  const [kind, setKind] = useState<'Image' | 'Video'>('Video');
  const [q, setQ] = useState('');
  const [results, setResults] = useState<StockItem[] | null>(null);
  const [searching, setSearching] = useState(false);
  const [added, setAdded] = useState<Record<string, 'busy' | 'done'>>({});

  const search = async () => {
    if (!q.trim()) return;
    setSearching(true);
    try {
      setResults(await reup.searchStock(provider, kind, q));
    } catch (e) {
      fail(e, 'Không tìm được.');
    } finally {
      setSearching(false);
    }
  };

  const add = async (item: StockItem) => {
    const key = `${item.provider}-${item.id}`;
    setAdded((a) => ({ ...a, [key]: 'busy' }));
    try {
      await reup.importStock(item, category, `${q.trim() || item.provider} ${item.id}`);
      setAdded((a) => ({ ...a, [key]: 'done' }));
      onAdded();
    } catch (e) {
      setAdded((a) => {
        const n = { ...a };
        delete n[key];
        return n;
      });
      fail(e, 'Không thêm được.');
    }
  };

  const hasKey = provider === 'Pexels' ? pexels : pixabay;
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <Segmented size="sm" value={provider} onChange={setProvider} options={[{ value: 'Pexels', label: 'Pexels' }, { value: 'Pixabay', label: 'Pixabay' }]} />
        <Segmented size="sm" value={kind} onChange={setKind} options={[{ value: 'Video', label: 'Video' }, { value: 'Image', label: 'Ảnh' }]} />
        <Select size="sm" value={category} onChange={(e) => onCategory(e.target.value)} className="w-40">
          {CATEGORIES.map((c) => (
            <option key={c.id} value={c.id}>
              Lưu vào: {c.label}
            </option>
          ))}
        </Select>
      </div>
      {!hasKey && (
        <Alert tone="warning">
          Chưa có khoá API {provider} (miễn phí) —{' '}
          <a className="text-accent hover:underline" href={href('/reup/settings')}>
            nhập trong Cài đặt
          </a>
          .
        </Alert>
      )}
      <div className="flex gap-2">
        <Input leading={<Search />} value={q} onChange={(e) => setQ(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && search()} placeholder="beach, forest, cafe, dance studio… (tiếng Anh cho kết quả tốt nhất)" />
        <Button variant="primary" loading={searching} disabled={!q.trim() || !hasKey} onClick={search}>
          Tìm
        </Button>
      </div>
      {searching && <Spinner />}
      {results && results.length === 0 && <p className="text-sm text-fg-muted">Không có kết quả.</p>}
      {results && results.length > 0 && (
        <>
          <div className="grid max-h-[50vh] grid-cols-3 gap-2 overflow-y-auto sm:grid-cols-4">
            {results.map((r) => {
              const key = `${r.provider}-${r.id}`;
              return (
                <div key={key} className="overflow-hidden rounded-lg border border-line">
                  <Thumb src={r.previewUrl} className="aspect-[9/16] w-full" />
                  <div className="space-y-1 p-1.5">
                    <div className="truncate text-[11px] text-fg-muted">
                      {r.author ?? '—'} · {r.width}×{r.height}
                      {r.durationSeconds ? ` · ${r.durationSeconds}s` : ''}
                    </div>
                    <div className="flex items-center justify-between gap-1">
                      <a href={r.pageUrl} target="_blank" rel="noreferrer" className="text-[11px] text-accent hover:underline">
                        Xem trang
                      </a>
                      {added[key] === 'done' ? (
                        <Badge tone="success">Đã thêm</Badge>
                      ) : (
                        <Button size="sm" loading={added[key] === 'busy'} onClick={() => add(r)}>
                          Thêm
                        </Button>
                      )}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
          <p className="text-xs text-fg-subtle">
            {provider === 'Pexels' ? 'Ảnh và video do Pexels cung cấp (Pexels License).' : 'Ảnh và video do Pixabay cung cấp (Pixabay Content License).'} Tác giả và trang gốc được lưu kèm
            mỗi file.
          </p>
        </>
      )}
    </div>
  );
}
