import { useEffect, useRef, useState } from 'react';
import { Check, ImagePlus, Pencil, Trash2, Upload } from 'lucide-react';
import { Badge, Button, Card, EmptyState, Field, Input, Modal, Select, Skeleton, Textarea, cn } from '../ui';
import { useConfirm, useToast } from '../feedback';
import { AssetKind, StudioAsset, studio } from '../../services/studio';

export const KIND_LABEL: Record<AssetKind, string> = { Character: 'Nhân vật', Location: 'Bối cảnh', Prop: 'Đạo cụ / phong cách' };

const KIND_HINT: Record<AssetKind, string> = {
  Character: 'Ảnh rõ mặt, đủ người. Mô tả: tuổi, dáng, tóc, trang phục, tính cách (vd "anh Hai, 45 tuổi, ria mép, áo xanh bạc màu, giọng rổn rảng").',
  Location: 'Ảnh bối cảnh (vd thôn quê miền Tây, hàng ba nhà gỗ). Mô tả: nơi chốn, thời điểm, ánh sáng, đồ vật cố định.',
  Prop: 'Đồ vật hoặc ảnh tham khảo phong cách (vd bếp than đất, xe máy Wave đỏ).',
};

/** Upload + manage the reusable reference images (characters, locations, props). */
export function AssetLibrary({ onAuthError }: { onAuthError: (e: unknown) => boolean }) {
  const toast = useToast();
  const confirm = useConfirm();
  const [assets, setAssets] = useState<StudioAsset[] | null>(null);
  const [filter, setFilter] = useState<AssetKind | 'All'>('All');
  const [file, setFile] = useState<File | null>(null);
  const [name, setName] = useState('');
  const [kind, setKind] = useState<AssetKind>('Character');
  const [description, setDescription] = useState('');
  const [uploading, setUploading] = useState(false);
  const [editing, setEditing] = useState<StudioAsset | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);

  const load = async () => {
    try {
      setAssets(await studio.assets());
    } catch (e: any) {
      toast.error(e.message);
    }
  };
  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const upload = async () => {
    if (!file || !name.trim()) return;
    setUploading(true);
    try {
      await studio.addAsset(file, name.trim(), kind, description.trim());
      toast.success(`Đã thêm "${name.trim()}"`);
      setFile(null);
      setName('');
      setDescription('');
      if (inputRef.current) inputRef.current.value = '';
      await load();
    } catch (e) {
      if (!onAuthError(e)) toast.error((e as Error).message);
    } finally {
      setUploading(false);
    }
  };

  const remove = async (a: StudioAsset) => {
    if (!(await confirm({ title: `Xoá "${a.name}"?`, description: 'Ảnh bị xoá khỏi kho; video đã tạo không bị ảnh hưởng.', confirmLabel: 'Xoá', danger: true }))) return;
    try {
      await studio.deleteAsset(a.id);
      await load();
    } catch (e) {
      if (!onAuthError(e)) toast.error((e as Error).message);
    }
  };

  const saveEdit = async () => {
    if (!editing) return;
    try {
      await studio.updateAsset(editing.id, { name: editing.name, kind: editing.kind, description: editing.description });
      setEditing(null);
      await load();
    } catch (e) {
      if (!onAuthError(e)) toast.error((e as Error).message);
    }
  };

  const shown = (assets ?? []).filter((a) => filter === 'All' || a.kind === filter);

  return (
    <div className="space-y-5">
      <Card className="space-y-3 p-4">
        <div className="flex items-center gap-2 text-sm font-semibold text-fg">
          <ImagePlus className="h-4 w-4" /> Thêm ảnh nhân vật / bối cảnh
        </div>
        <p className="text-xs text-fg-muted">
          Mỗi clip bắt đầu từ <b>1 ảnh</b> rồi chuyển động tiếp, nên ảnh tốt nhất là ảnh có sẵn nhân vật đứng trong bối cảnh (vd: anh Hai ngồi
          bên bếp than trước hàng ba), dọc 9:16 hoặc đủ rộng để cắt dọc. Ảnh AI anh tự tạo đều dùng được. Mô tả càng cụ thể thì video càng đúng.
        </p>
        <div className="grid gap-3 sm:grid-cols-[1fr_1fr_160px]">
          <Field label="Ảnh">
            <Input ref={inputRef} type="file" accept="image/*" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          </Field>
          <Field label="Tên">
            <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="vd: Anh Hai" />
          </Field>
          <Field label="Loại">
            <Select value={kind} onChange={(e) => setKind(e.target.value as AssetKind)}>
              <option value="Character">Nhân vật</option>
              <option value="Location">Bối cảnh</option>
              <option value="Prop">Đạo cụ / phong cách</option>
            </Select>
          </Field>
        </div>
        <Field label="Mô tả" hint={KIND_HINT[kind]}>
          <Textarea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <Button variant="primary" size="sm" icon={<Upload className="h-4 w-4" />} loading={uploading} disabled={!file || !name.trim()} onClick={upload}>
          Tải lên
        </Button>
      </Card>

      <div className="flex flex-wrap gap-2">
        {(['All', 'Character', 'Location', 'Prop'] as const).map((k) => (
          <Button key={k} size="sm" variant={filter === k ? 'primary' : 'secondary'} onClick={() => setFilter(k)}>
            {k === 'All' ? 'Tất cả' : KIND_LABEL[k]}
          </Button>
        ))}
      </div>

      {assets === null ? (
        <Skeleton className="h-40 w-full" />
      ) : shown.length === 0 ? (
        <EmptyState title="Chưa có ảnh nào" description="Tải ảnh nhân vật và bối cảnh lên để AI dựa vào đó dựng video." />
      ) : (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {shown.map((a) => (
            <Card key={a.id} className="overflow-hidden">
              <img src={studio.assetImageUrl(a.id)} alt={a.name} className="aspect-[3/4] w-full bg-muted object-cover" loading="lazy" />
              <div className="space-y-1 p-2.5 text-xs">
                <div className="flex items-center justify-between gap-1">
                  <span className="truncate font-semibold text-fg">{a.name}</span>
                  <Badge tone={a.kind === 'Character' ? 'info' : a.kind === 'Location' ? 'success' : 'neutral'}>{KIND_LABEL[a.kind]}</Badge>
                </div>
                <p className="line-clamp-3 text-fg-muted">{a.description || <i>Chưa có mô tả</i>}</p>
                <div className="flex justify-end gap-1 pt-1">
                  <Button size="sm" variant="ghost" icon={<Pencil className="h-3.5 w-3.5" />} onClick={() => setEditing({ ...a })} aria-label="Sửa" />
                  <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} onClick={() => remove(a)} aria-label="Xoá" />
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      <Modal
        open={!!editing}
        onClose={() => setEditing(null)}
        title="Sửa ảnh"
        footer={
          <>
            <Button variant="ghost" onClick={() => setEditing(null)}>Huỷ</Button>
            <Button variant="primary" onClick={saveEdit}>Lưu</Button>
          </>
        }
      >
        {editing && (
          <div className="space-y-3">
            <Field label="Tên">
              <Input value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
            </Field>
            <Field label="Loại">
              <Select value={editing.kind} onChange={(e) => setEditing({ ...editing, kind: e.target.value as AssetKind })}>
                <option value="Character">Nhân vật</option>
                <option value="Location">Bối cảnh</option>
                <option value="Prop">Đạo cụ / phong cách</option>
              </Select>
            </Field>
            <Field label="Mô tả" hint={KIND_HINT[editing.kind]}>
              <Textarea rows={4} value={editing.description} onChange={(e) => setEditing({ ...editing, description: e.target.value })} />
            </Field>
          </div>
        )}
      </Modal>
    </div>
  );
}

/** Lower-case, accent-free text, so "Triển Chiêu" in a script matches an asset named "trien chieu". */
export const fold = (t: string) =>
  t.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase().trim();

/** Assets whose name appears in the script (whole words, accents ignored). */
export function assetsNamedInScript(assets: StudioAsset[], script: string): string[] {
  const text = ` ${fold(script).replace(/[^a-z0-9]+/g, ' ')} `;
  return assets
    .filter((a) => {
      const name = fold(a.name).replace(/[^a-z0-9]+/g, ' ').trim();
      return name.length >= 2 && text.includes(` ${name} `);
    })
    .map((a) => a.id);
}

/** Checkbox grid used in the video form and on each shot. Selected pictures carry a ✓ (or their order) and keep the click order. */
export function AssetPicker({
  assets,
  value,
  onChange,
  compact,
  disabled,
}: {
  assets: StudioAsset[];
  value: string[];
  onChange: (ids: string[]) => void;
  compact?: boolean;
  disabled?: boolean;
}) {
  if (assets.length === 0) return <p className="text-xs text-fg-muted">Kho ảnh trống — thêm ảnh ở tab "Nhân vật & bối cảnh".</p>;
  const toggle = (id: string) => onChange(value.includes(id) ? value.filter((x) => x !== id) : [...value, id]);
  return (
    <div className={cn('grid gap-2', compact ? 'grid-cols-4 sm:grid-cols-6 lg:grid-cols-8' : 'grid-cols-3 sm:grid-cols-4 lg:grid-cols-6')}>
      {assets.map((a) => {
        const order = value.indexOf(a.id);
        const on = order >= 0;
        return (
          <button
            key={a.id}
            type="button"
            disabled={disabled}
            onClick={() => toggle(a.id)}
            aria-pressed={on}
            className={cn(
              'relative overflow-hidden rounded-lg border-2 text-left transition',
              on ? 'border-accent shadow-sm' : 'border-transparent opacity-55 grayscale-[40%] hover:opacity-100 hover:grayscale-0',
            )}
            title={`${KIND_LABEL[a.kind]}: ${a.description}`}
          >
            <img src={studio.assetImageUrl(a.id)} alt={a.name} className="aspect-square w-full bg-muted object-cover" loading="lazy" />
            {on && (
              <span className="absolute right-1 top-1 flex h-5 min-w-[1.25rem] items-center justify-center rounded-full bg-accent px-1 text-[11px] font-bold text-white">
                {compact ? order + 1 : <Check className="h-3.5 w-3.5" />}
              </span>
            )}
            <div className={cn('truncate px-1 py-0.5 text-[11px]', on ? 'bg-accent font-medium text-white' : 'bg-muted text-fg-muted')}>{a.name}</div>
          </button>
        );
      })}
    </div>
  );
}

/**
 * The cast of one video: the main characters (and locations / props) picked from the library. Only these pictures
 * reach the planner — side characters, extras and scenery are invented by the AI to fit the setting.
 */
export function VideoCastPicker({
  assets,
  value,
  onChange,
  script,
  disabled,
}: {
  assets: StudioAsset[];
  value: string[];
  onChange: (ids: string[]) => void;
  script: string;
  disabled?: boolean;
}) {
  const [query, setQuery] = useState('');
  const [showAll, setShowAll] = useState(false);
  if (assets.length === 0) return <p className="text-xs text-fg-muted">Kho ảnh trống — thêm ảnh ở tab "Nhân vật & bối cảnh".</p>;

  const named = assetsNamedInScript(assets, script);
  const q = fold(query);
  const small = assets.length <= 12; // a small library is always shown whole
  const visible = (kinds: AssetKind[]) =>
    assets.filter(
      (a) =>
        kinds.includes(a.kind) &&
        (!q || fold(a.name).includes(q) || fold(a.description).includes(q)) &&
        (small || showAll || q || value.includes(a.id) || named.includes(a.id)),
    );
  const chosen = (kinds: AssetKind[]) => assets.filter((a) => kinds.includes(a.kind) && value.includes(a.id));
  const groups: { kinds: AssetKind[]; title: string; empty: string }[] = [
    { kinds: ['Character'], title: 'Nhân vật chính', empty: 'Chưa chọn nhân vật nào — AI tự tạo nhân vật theo kịch bản (mặt có thể khác nhau giữa các shot).' },
    { kinds: ['Location', 'Prop'], title: 'Bối cảnh & đạo cụ', empty: 'Chưa chọn — AI tự dựng bối cảnh theo kịch bản.' },
  ];
  const missing = named.filter((id) => !value.includes(id));

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <Input className="max-w-xs" placeholder="Tìm theo tên / mô tả…" value={query} onChange={(e) => setQuery(e.target.value)} />
        <Button size="sm" variant="secondary" disabled={disabled || missing.length === 0} onClick={() => onChange([...value, ...missing])}
          title={named.length ? 'Chọn các ảnh có tên xuất hiện trong kịch bản' : 'Không thấy tên ảnh nào trong kịch bản'}>
          Chọn theo kịch bản{named.length ? ` (${named.length})` : ''}
        </Button>
        {value.length > 0 && (
          <Button size="sm" variant="ghost" disabled={disabled} onClick={() => onChange([])}>
            Bỏ chọn hết
          </Button>
        )}
        {!small && !q && (
          <Button size="sm" variant="ghost" onClick={() => setShowAll((v) => !v)}>
            {showAll ? 'Chỉ hiện ảnh liên quan' : `Hiện cả kho (${assets.length})`}
          </Button>
        )}
      </div>
      {groups.map((g) => {
        const list = visible(g.kinds);
        const picked = chosen(g.kinds);
        return (
          <div key={g.title} className="space-y-1.5">
            <div className="text-xs font-semibold text-fg">
              {g.title}{' '}
              <span className="font-normal text-fg-muted">
                · đã chọn {picked.length}
                {picked.length ? `: ${picked.map((a) => a.name).join(', ')}` : ''}
              </span>
            </div>
            {picked.length === 0 && <p className="text-[11px] text-fg-subtle">{g.empty}</p>}
            {list.length > 0 ? (
              <AssetPicker assets={list} value={value} disabled={disabled} onChange={onChange} />
            ) : (
              <p className="text-[11px] text-fg-subtle">{q ? 'Không có ảnh khớp.' : 'Không có ảnh nào liên quan tới kịch bản — bấm "Hiện cả kho" để chọn.'}</p>
            )}
          </div>
        );
      })}
    </div>
  );
}
