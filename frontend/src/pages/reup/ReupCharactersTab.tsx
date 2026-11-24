import { useEffect, useState } from 'react';
import { Pencil, RefreshCw, Trash2, UserRound, UserRoundPlus } from 'lucide-react';
import { Alert, Badge, Button, Card, CardBody, Checkbox, EmptyState, Field, Input, Modal, Skeleton, Textarea, Thumb } from '../../components/ui';
import { useConfirm, useToast } from '../../components/feedback';
import { timeAgo } from '../../lib/format';
import { reup, ReupCharacter } from '../../services/reup';
import { useReup } from './ReupPage';
import { Dropzone } from './ReupBits';

const STATUS: Record<ReupCharacter['status'], { label: string; tone: 'success' | 'warning' | 'danger' | 'neutral' }> = {
  Ready: { label: 'Khuôn mặt đạt', tone: 'success' },
  Checking: { label: 'Chờ máy tính kiểm tra', tone: 'warning' },
  Rejected: { label: 'Ảnh không đạt', tone: 'danger' },
  Error: { label: 'Lỗi kiểm tra', tone: 'danger' },
};

export function ReupCharactersTab() {
  const { fail, overview } = useReup();
  const toast = useToast();
  const confirm = useConfirm();
  const [items, setItems] = useState<ReupCharacter[] | null>(null);
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<ReupCharacter | null>(null);

  const load = () => reup.characters().then(setItems).catch((e) => fail(e, 'Không tải được nhân vật.'));
  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  // characters waiting for the PC's face check refresh by themselves
  useEffect(() => {
    if (!items?.some((c) => c.status === 'Checking')) return;
    const t = setInterval(load, 4000);
    return () => clearInterval(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [items]);

  const remove = async (c: ReupCharacter) => {
    if (!(await confirm({ title: `Xoá nhân vật "${c.name}"?`, description: 'Ảnh sẽ bị xoá khỏi kho file. Video đã xuất không bị ảnh hưởng.', confirmLabel: 'Xoá', danger: true }))) return;
    try {
      await reup.deleteCharacter(c.id);
      toast.success('Đã xoá nhân vật.');
      load();
    } catch (e) {
      fail(e);
    }
  };

  const recheck = async (c: ReupCharacter) => {
    try {
      await reup.recheckCharacter(c.id);
      load();
    } catch (e) {
      fail(e);
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-2xl text-[13px] text-fg-muted">
          Ảnh chân dung nhân vật AI dùng để thay mặt. Máy tính kiểm tra mỗi ảnh có đúng <b>một</b> khuôn mặt rõ, nhìn thẳng, đủ lớn. Ảnh gốc giữ
          nguyên, không bị chỉnh sửa. Một nhân vật dùng được cho nhiều video.
        </p>
        <Button variant="primary" icon={<UserRoundPlus className="h-4 w-4" />} onClick={() => setAdding(true)}>
          Thêm nhân vật
        </Button>
      </div>
      {overview && !overview.pcOnline && items?.some((c) => c.status === 'Checking') && (
        <Alert tone="warning">Máy tính đang tắt — ảnh sẽ được kiểm tra khi máy bật.</Alert>
      )}
      {items == null ? (
        <Skeleton className="h-48 w-full" />
      ) : items.length === 0 ? (
        <Card>
          <EmptyState icon={<UserRound />} title="Chưa có nhân vật" description="Thêm ảnh chân dung nhân vật AI (nhìn thẳng, rõ mặt, một người)." />
        </Card>
      ) : (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
          {items.map((c) => (
            <Card key={c.id} className="overflow-hidden">
              <a href={reup.characterImageUrl(c.id)} target="_blank" rel="noreferrer">
                <Thumb src={c.hasThumb ? reup.characterThumbUrl(c.id) : reup.characterImageUrl(c.id)} className="aspect-square w-full" />
              </a>
              <CardBody className="space-y-2 px-3 py-3">
                <div className="truncate text-sm font-semibold text-fg">{c.name}</div>
                <Badge tone={STATUS[c.status].tone}>{STATUS[c.status].label}</Badge>
                {c.rejectReason && <p className="text-xs text-danger">{c.rejectReason}</p>}
                {c.notes && <p className="line-clamp-2 text-xs text-fg-muted">{c.notes}</p>}
                <div className="text-[11px] text-fg-subtle">
                  {c.usedByJobs ? `Dùng trong ${c.usedByJobs} video · ` : ''}
                  {timeAgo(c.createdAtUtc)}
                </div>
                <div className="flex gap-1">
                  <Button size="sm" variant="ghost" onClick={() => setEditing(c)} title="Sửa tên / ghi chú">
                    <Pencil className="h-4 w-4" />
                  </Button>
                  {c.status !== 'Ready' && c.status !== 'Checking' && (
                    <Button size="sm" variant="ghost" onClick={() => recheck(c)} title="Kiểm tra lại">
                      <RefreshCw className="h-4 w-4" />
                    </Button>
                  )}
                  <Button size="sm" variant="danger-soft" onClick={() => remove(c)} title="Xoá">
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              </CardBody>
            </Card>
          ))}
        </div>
      )}
      <AddCharacter open={adding} onClose={() => setAdding(false)} onAdded={load} />
      {editing && <EditCharacter c={editing} onClose={() => setEditing(null)} onSaved={load} />}
    </div>
  );
}

function AddCharacter({ open, onClose, onAdded }: { open: boolean; onClose: () => void; onAdded: () => void }) {
  const { fail } = useReup();
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null);
  const [name, setName] = useState('');
  const [notes, setNotes] = useState('');
  const [consent, setConsent] = useState(false);
  const [progress, setProgress] = useState<number | null>(null);
  const [preview, setPreview] = useState<string | null>(null);

  useEffect(() => {
    if (!file) return setPreview(null);
    const u = URL.createObjectURL(file);
    setPreview(u);
    return () => URL.revokeObjectURL(u);
  }, [file]);

  const reset = () => {
    setFile(null);
    setName('');
    setNotes('');
    setConsent(false);
  };

  const submit = async () => {
    if (!file) return;
    setProgress(0);
    try {
      await reup.addCharacter(file, name, notes, consent, setProgress);
      toast.success('Đã thêm — máy tính đang kiểm tra khuôn mặt.');
      reset();
      onAdded();
      onClose();
    } catch (e) {
      fail(e, 'Không thêm được nhân vật.');
    } finally {
      setProgress(null);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Thêm nhân vật AI"
      description="Ảnh chân dung JPG / PNG / WEBP, tối đa 15 MB, mặt nhìn thẳng, rõ nét, chỉ một người."
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Huỷ
          </Button>
          <Button variant="primary" loading={progress != null} disabled={!file || !name.trim() || !consent} onClick={submit}>
            Thêm
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        {preview ? (
          <div className="flex items-center gap-3">
            <img src={preview} alt="" className="h-28 w-28 rounded-lg object-cover" />
            <Button size="sm" variant="ghost" onClick={() => setFile(null)}>
              Chọn ảnh khác
            </Button>
          </div>
        ) : (
          <Dropzone accept="image/jpeg,image/png,image/webp" label="Chọn ảnh nhân vật" onFile={(f) => { setFile(f); if (!name) setName(f.name.replace(/\.[^.]+$/, '')); }} />
        )}
        <Field label="Tên nhân vật">
          <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Ví dụ: Lan Chi" />
        </Field>
        <Field label="Thông tin" optional>
          <Textarea rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Phong cách, kênh sử dụng…" />
        </Field>
        <Checkbox
          checked={consent}
          onChange={setConsent}
          label="Ảnh do AI tạo, hoặc người trong ảnh đã đồng ý cho dùng khuôn mặt"
          description="Không dùng ảnh người thật khi chưa có sự đồng ý của họ."
        />
      </div>
    </Modal>
  );
}

function EditCharacter({ c, onClose, onSaved }: { c: ReupCharacter; onClose: () => void; onSaved: () => void }) {
  const { fail } = useReup();
  const [name, setName] = useState(c.name);
  const [notes, setNotes] = useState(c.notes ?? '');
  const [saving, setSaving] = useState(false);
  const save = async () => {
    setSaving(true);
    try {
      await reup.updateCharacter(c.id, { name, notes });
      onSaved();
      onClose();
    } catch (e) {
      fail(e);
    } finally {
      setSaving(false);
    }
  };
  return (
    <Modal
      open
      onClose={onClose}
      title="Sửa nhân vật"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Huỷ
          </Button>
          <Button variant="primary" loading={saving} disabled={!name.trim()} onClick={save}>
            Lưu
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="Tên nhân vật">
          <Input value={name} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="Thông tin" optional>
          <Textarea rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </Field>
      </div>
    </Modal>
  );
}
