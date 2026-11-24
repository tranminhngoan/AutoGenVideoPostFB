import { useEffect, useRef, useState } from 'react';
import { ExternalLink, Music2, Pencil, Trash2, Upload } from 'lucide-react';
import { Button, Card, EmptyState, Field, Input, Skeleton } from '../ui';
import { useConfirm, useToast } from '../feedback';
import { StudioMusic, studio } from '../../services/studio';
import { MUSIC_PRESETS, presetForFile } from '../../lib/musicPresets';

const MOOD_SUGGESTIONS = ['vui tươi', 'hài hước', 'quê hương', 'nhẹ nhàng', 'buồn', 'cảm động', 'hồi hộp', 'kịch tính', 'lãng mạn', 'hùng tráng'];

const fmtTime = (s: number) => `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, '0')}`;

/** The music store the planner picks from. Use only music you have the right to post (e.g. Meta Sound Collection). */
export function MusicLibrary({ onAuthError }: { onAuthError: (e: unknown) => boolean }) {
  const toast = useToast();
  const confirm = useConfirm();
  const [tracks, setTracks] = useState<StudioMusic[] | null>(null);
  const [files, setFiles] = useState<File[]>([]);
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

  const pick = (list: FileList | null) => {
    const picked = Array.from(list ?? []);
    setFiles(picked);
    if (picked.length === 1) {
      // a suggested Pixabay track is recognised by its number: Vietnamese name and mood filled in
      const preset = presetForFile(picked[0].name);
      setName(preset?.name ?? picked[0].name.replace(/\.[^.]+$/, ''));
      if (preset) setMood(preset.mood);
    }
  };

  const upload = async () => {
    if (files.length === 0 || (files.length === 1 && !name.trim())) return;
    setUploading(true);
    const added: string[] = [];
    const failed: string[] = [];
    try {
      for (const f of files) {
        const preset = presetForFile(f.name);
        const trackName = files.length === 1 ? name.trim() : preset?.name ?? f.name.replace(/\.[^.]+$/, '');
        const trackMood = files.length === 1 ? mood.trim() : preset?.mood ?? mood.trim();
        try {
          await studio.addMusic(f, trackName, trackMood);
          added.push(trackName);
        } catch (e) {
          if (onAuthError(e)) return;
          failed.push(`${trackName}: ${(e as Error).message}`);
        }
      }
      if (added.length) toast.success(added.length === 1 ? `Đã thêm "${added[0]}"` : `Đã thêm ${added.length} bài vào kho nhạc`);
      if (failed.length) toast.error(`Chưa thêm được ${failed.length} bài — ${failed.join(' | ')}`);
      setFiles([]);
      setName('');
      setMood('');
      if (inputRef.current) inputRef.current.value = '';
      await load();
    } finally {
      setUploading(false);
    }
  };

  const inStore = (presetName: string) => (tracks ?? []).some((t) => t.name.trim().toLowerCase() === presetName.toLowerCase());

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
          <Field label="File nhạc (MP3, M4A, WAV, OGG — tối đa 40 MB; chọn được nhiều file một lần)">
            <Input ref={inputRef} type="file" accept="audio/*" multiple onChange={(e) => pick(e.target.files)} />
          </Field>
          <Field label={files.length > 1 ? `Tên bài (${files.length} file: bài gợi ý tự đặt tên, còn lại lấy tên file)` : 'Tên bài'}>
            <Input value={files.length > 1 ? '' : name} onChange={(e) => setName(e.target.value)} disabled={files.length > 1} />
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
        <Button variant="primary" size="sm" icon={<Upload className="h-4 w-4" />} loading={uploading}
          disabled={files.length === 0 || (files.length === 1 && !name.trim())} onClick={upload}>
          {files.length > 1 ? `Tải lên ${files.length} bài` : 'Tải lên'}
        </Button>
      </Card>

      <Card className="space-y-3 p-4">
        <div className="text-sm font-semibold text-fg">🎵 Nhạc quê gợi ý — miễn phí trên Pixabay</div>
        <p className="text-xs text-fg-muted">
          Không lời, dùng thương mại được, không cần ghi nguồn; đã kiểm tra ngày 24/11/2026 là <b>không</b> đăng ký Content ID (không bị Facebook
          báo bản quyền). Bấm từng bài → nút <b>Download</b> trên Pixabay (nếu trang có ghi “Content ID Registered” thì bỏ bài đó) → rồi chọn
          tất cả file vừa tải ở ô “File nhạc” phía trên: tool tự nhận ra từng bài và điền tên + tâm trạng.
        </p>
        <ul className="grid gap-1.5 sm:grid-cols-2">
          {MUSIC_PRESETS.map((p) => (
            <li key={p.id} className="flex items-center justify-between gap-2 rounded-lg border border-line px-2.5 py-1.5 text-xs">
              <div className="min-w-0">
                <div className="truncate font-semibold text-fg">{p.name} <span className="font-normal text-fg-muted">· {fmtTime(p.seconds)}</span></div>
                <div className="truncate text-fg-muted">{p.mood}</div>
              </div>
              {inStore(p.name) ? (
                <span className="shrink-0 text-success">✓ đã có</span>
              ) : (
                <a href={p.url} target="_blank" rel="noopener noreferrer" className="inline-flex shrink-0 items-center gap-1 text-accent hover:underline">
                  Mở <ExternalLink className="h-3 w-3" />
                </a>
              )}
            </li>
          ))}
        </ul>
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
