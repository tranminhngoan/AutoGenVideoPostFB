import { useEffect, useRef, useState } from 'react';
import { Music2, Pencil, Trash2, Upload } from 'lucide-react';
import { Button, Card, EmptyState, Field, Input, Skeleton } from '../ui';
import { useConfirm, useToast } from '../feedback';
import { StudioMusic, studio } from '../../services/studio';

const MOOD_SUGGESTIONS = ['vui tươi', 'hài hước', 'quê hương', 'nhẹ nhàng', 'buồn', 'cảm động', 'hồi hộp', 'kịch tính', 'lãng mạn', 'hùng tráng'];

const fmtTime = (s: number) => `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, '0')}`;

/** The music store the planner picks from. Use only music you have the right to post (e.g. Meta Sound Collection). */
export function MusicLibrary({ onAuthError }: { onAuthError: (e: unknown) => boolean }) {
  const toast = useToast();
  const confirm = useConfirm();
  const [tracks, setTracks] = useState<StudioMusic[] | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [name, setName] = useState('');
  const [mood, setMood] = useState('');
  const [uploading, setUploading] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const [editMood, setEditMood] = useState('');
  const inputRef = useRef<HTMLInputElement | null>(null);

  const load = async () => {
    try {
      setTracks(await studio.music());
    } catch (e: any) {
      toast.error(e.message);
    }
  };
  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const pick = (f: File | null) => {
    setFile(f);
    if (f && !name) setName(f.name.replace(/\.[^.]+$/, ''));
  };

  const upload = async () => {
    if (!file || !name.trim()) return;
    setUploading(true);
    try {
      await studio.addMusic(file, name.trim(), mood.trim());
      toast.success(`Đã thêm "${name.trim()}"`);
      setFile(null);
      setName('');
      setMood('');
      if (inputRef.current) inputRef.current.value = '';
      await load();
    } catch (e) {
      if (!onAuthError(e)) toast.error((e as Error).message);
    } finally {
      setUploading(false);
    }
  };

  const remove = async (t: StudioMusic) => {
    if (!(await confirm({ title: `Xoá "${t.name}"?`, confirmLabel: 'Xoá', danger: true }))) return;
    try {
      await studio.deleteMusic(t.id);
      await load();
    } catch (e) {
      if (!onAuthError(e)) toast.error((e as Error).message);
    }
  };

  const saveEdit = async (id: string) => {
    try {
      await studio.updateMusic(id, { name: editName, mood: editMood });
      setEditingId(null);
      await load();
    } catch (e) {
      if (!onAuthError(e)) toast.error((e as Error).message);
    }
  };

  return (
    <div className="space-y-5">
      <Card className="space-y-3 p-4">
        <div className="flex items-center gap-2 text-sm font-semibold text-fg">
          <Music2 className="h-4 w-4" /> Thêm nhạc vào kho
        </div>
        <p className="text-xs text-fg-muted">
          Khi lập kế hoạch, AI đọc tên và tâm trạng từng bài để chọn bài hợp với kịch bản; nhạc được lặp/cắt cho vừa video và tự nhỏ lại khi có
          thoại. Chỉ dùng nhạc anh có quyền đăng (ví dụ tải từ Meta Sound Collection) — nhạc có bản quyền sẽ làm video bị chặn kiếm tiền.
        </p>
        <div className="grid gap-3 sm:grid-cols-3">
          <Field label="File nhạc (MP3, M4A, WAV, OGG — tối đa 40 MB)">
            <Input ref={inputRef} type="file" accept="audio/*" onChange={(e) => pick(e.target.files?.[0] ?? null)} />
          </Field>
          <Field label="Tên bài">
            <Input value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label="Tâm trạng (cách nhau bằng dấu phẩy)">
            <Input value={mood} onChange={(e) => setMood(e.target.value)} placeholder="vd: vui tươi, quê hương" />
          </Field>
        </div>
        <div className="flex flex-wrap gap-1.5">
          {MOOD_SUGGESTIONS.map((m) => (
            <button
              key={m}
              type="button"
              onClick={() => setMood((cur) => (cur.split(',').map((x) => x.trim()).includes(m) ? cur : [cur.trim(), m].filter(Boolean).join(', ')))}
              className="rounded-full border border-line px-2 py-0.5 text-[11px] text-fg-muted hover:bg-muted"
            >
              + {m}
            </button>
          ))}
        </div>
        <Button variant="primary" size="sm" icon={<Upload className="h-4 w-4" />} loading={uploading} disabled={!file || !name.trim()} onClick={upload}>
          Tải lên
        </Button>
      </Card>

      {tracks === null ? (
        <Skeleton className="h-32 w-full" />
      ) : tracks.length === 0 ? (
        <EmptyState title="Kho nhạc trống" description="Video vẫn làm được (không có nhạc nền), hoặc tải vài bài lên để AI tự chọn." />
      ) : (
        <ul className="space-y-2">
          {tracks.map((t) => (
            <li key={t.id}>
              <Card className="space-y-2 p-3 text-xs">
                {editingId === t.id ? (
                  <div className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
                    <Input value={editName} onChange={(e) => setEditName(e.target.value)} />
                    <Input value={editMood} onChange={(e) => setEditMood(e.target.value)} />
                    <div className="flex gap-1">
                      <Button size="sm" variant="primary" onClick={() => saveEdit(t.id)}>Lưu</Button>
                      <Button size="sm" variant="ghost" onClick={() => setEditingId(null)}>Huỷ</Button>
                    </div>
                  </div>
                ) : (
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="min-w-0">
                      <div className="truncate font-semibold text-fg">{t.name}</div>
                      <div className="text-fg-muted">
                        {fmtTime(t.durationSeconds)} · {t.mood || 'chưa gắn tâm trạng'} · đã dùng {t.useCount} lần
                      </div>
                    </div>
                    <div className="flex gap-1">
                      <Button
                        size="sm"
                        variant="ghost"
                        icon={<Pencil className="h-3.5 w-3.5" />}
                        aria-label="Sửa"
                        onClick={() => {
                          setEditingId(t.id);
                          setEditName(t.name);
                          setEditMood(t.mood);
                        }}
                      />
                      <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} aria-label="Xoá" onClick={() => remove(t)} />
                    </div>
                  </div>
                )}
                <audio controls preload="none" src={studio.musicAudioUrl(t.id)} className="h-8 w-full" />
              </Card>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
