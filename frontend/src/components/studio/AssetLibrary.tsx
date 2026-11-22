import { useEffect, useRef, useState } from 'react';
import { ImagePlus, Pencil, Trash2, Upload } from 'lucide-react';
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
          <ImagePlus className="h-4 w-4" /> Thêm ảnh tham chiếu
        </div>
        <p className="text-xs text-fg-muted">
          Omni dùng ảnh này để giữ đúng mặt nhân vật và bối cảnh trong mọi cảnh (tối đa 6 ảnh mỗi clip). Ảnh càng rõ và mô tả càng cụ thể thì video
          càng giống. Ảnh có thể là ảnh AI anh tự tạo.
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
        <EmptyState title="Chưa có ảnh nào" description="Tải ảnh nhân vật và bối cảnh lên để Omni dựa vào đó dựng video." />
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
        title="Sửa ảnh tham chiếu"
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

/** Checkbox grid used in the video form and on each shot. */
export function AssetPicker({
  assets,
  value,
  onChange,
  compact,
}: {
  assets: StudioAsset[];
  value: string[];
  onChange: (ids: string[]) => void;
  compact?: boolean;
}) {
  if (assets.length === 0) return <p className="text-xs text-fg-muted">Kho ảnh trống — thêm ảnh ở tab "Nhân vật & bối cảnh".</p>;
  const toggle = (id: string) => onChange(value.includes(id) ? value.filter((x) => x !== id) : [...value, id]);
  return (
    <div className={cn('grid gap-2', compact ? 'grid-cols-4 sm:grid-cols-6 lg:grid-cols-8' : 'grid-cols-3 sm:grid-cols-4 lg:grid-cols-6')}>
      {assets.map((a) => {
        const on = value.includes(a.id);
        return (
          <button
            key={a.id}
            type="button"
            onClick={() => toggle(a.id)}
            className={cn('overflow-hidden rounded-lg border-2 text-left transition-colors', on ? 'border-accent' : 'border-transparent opacity-70 hover:opacity-100')}
            title={`${KIND_LABEL[a.kind]}: ${a.description}`}
          >
            <img src={studio.assetImageUrl(a.id)} alt={a.name} className="aspect-square w-full bg-muted object-cover" loading="lazy" />
            <div className={cn('truncate px-1 py-0.5 text-[11px]', on ? 'bg-accent text-white' : 'bg-muted text-fg-muted')}>{a.name}</div>
          </button>
        );
      })}
    </div>
  );
}
