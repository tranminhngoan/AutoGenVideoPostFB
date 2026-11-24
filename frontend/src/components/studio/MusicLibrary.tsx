import { useEffect, useRef, useState } from 'react';
import { ExternalLink, Music2, Pencil, Sparkles, Trash2, Upload } from 'lucide-react';
import { Button, Card, EmptyState, Field, Input, Skeleton } from '../ui';
import { useConfirm, useToast } from '../feedback';
import { StudioMusic, StudioMusicAi, studio } from '../../services/studio';
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
  const [ai, setAi] = useState<StudioMusicAi | null>(null);
  const [chosen, setChosen] = useState<string[] | null>(null);
  const [wish, setWish] = useState('');
  const [composing, setComposing] = useState(false);

  const load = async () => {
    try {
      setTracks(await studio.music());
    } catch (e: any) {
      toast.error(e.message);
    }
  };
  useEffect(() => {
    load();
    studio.musicAi().then(setAi).catch(() => undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // while the AI composes, follow its progress and show each new track as it lands
  useEffect(() => {
    if (!ai?.running) return;
    const timer = window.setInterval(async () => {
      try {
        const next = await studio.musicAi();
        if (next.done !== ai.done || !next.running) await load();
        setAi(next);
        if (!next.running) {
          if (next.added.length) toast.success(`AI đã sáng tác xong ${next.added.length} bài và thêm vào kho nhạc.`);
          if (next.errors.length) toast.error(`Chưa sáng tác được ${next.errors.length} bài — ${next.errors.join(' | ')}`);
        }
      } catch {
        /* try again on the next tick */
      }
    }, 8000);
    return () => window.clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ai?.running, ai?.done]);

  // by default: every ready-made brief not yet in the store
  const selected = chosen ?? (ai?.briefs ?? []).filter((b) => !(tracks ?? []).some((t) => t.name.startsWith(b.name))).map((b) => b.id);
  const toggleBrief = (id: string) => setChosen(selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]);
  const songCount = selected.length + (wish.trim() ? 1 : 0);
  const compose = async () => {
    setComposing(true);
    try {
      setAi(await studio.composeMusic({ briefIds: selected, wish: wish.trim() || undefined }));
      setChosen([]);
      setWish('');
      toast.success('AI đang sáng tác — mỗi bài khoảng 1–2 phút, bài xong sẽ tự hiện trong kho nhạc bên dưới.');
    } catch (e) {
      if (!onAuthError(e)) toast.error((e as Error).message);
    } finally {
      setComposing(false);
    }
  };

  const pick = (list: FileList | null) => {
    // the picker shows every file (an audio-only filter makes many phones open their music app, which doesn't list
    // fresh downloads): anything that isn't audio is left out here, and the server checks each file anyway
    const all = Array.from(list ?? []);
    const picked = all.filter((f) => f.type.startsWith('audio/') || f.type === 'video/mp4' || /\.(mp3|m4a|mp4|aac|wav|ogg|oga|flac)$/i.test(f.name));
    if (picked.length < all.length) toast.error(`Bỏ qua ${all.length - picked.length} file không phải nhạc.`);
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
      {ai && (
        <Card className="space-y-3 border-accent/30 p-4">
          <div className="flex items-center gap-2 text-sm font-semibold text-fg">
            <Sparkles className="h-4 w-4" /> Sáng tác nhạc bằng AI — không cần tải gì, không lo bản quyền
          </div>
          <p className="text-xs text-fg-muted">
            Google Lyria sáng tác bài nhạc không lời <b>mới, của riêng bạn</b> (dài khoảng 2 phút), rồi tự thêm vào kho để AI chọn khi dựng video.
            Khoảng {`$${ai.pricePerSongUsd.toFixed(2)}`} một bài, tính vào ngân sách Studio.
          </p>
          {ai.running ? (
            <p className="rounded-lg bg-subtle p-2.5 text-xs text-fg">
              🎼 Đang sáng tác {ai.done}/{ai.total} bài… (mỗi bài khoảng 1–2 phút, trang tự cập nhật — có thể rời trang, AI vẫn làm tiếp)
            </p>
          ) : (
            <>
              <ul className="grid gap-1.5 sm:grid-cols-2">
                {ai.briefs.map((b) => (
                  <li key={b.id}>
                    <label className="flex cursor-pointer items-start gap-2 rounded-lg border border-line px-2.5 py-1.5 text-xs hover:bg-muted">
                      <input type="checkbox" className="mt-0.5" checked={selected.includes(b.id)} onChange={() => toggleBrief(b.id)} />
                      <span className="min-w-0">
                        <span className="block font-semibold text-fg">{b.name}</span>
                        <span className="block text-fg-muted">{b.mood}</span>
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
              <Field label="Hoặc thêm 1 bài theo ý bạn (tuỳ chọn)">
                <Input value={wish} onChange={(e) => setWish(e.target.value)} placeholder="vd: nhạc buồn sáo trúc, chậm, nhớ bà ngoại ngày Tết" />
              </Field>
              <Button variant="primary" size="sm" icon={<Sparkles className="h-4 w-4" />} loading={composing}
                disabled={!ai.configured || songCount === 0 || songCount > 8} onClick={compose}>
                Sáng tác {songCount} bài (~{`$${(songCount * ai.pricePerSongUsd).toFixed(2)}`})
              </Button>
              {!ai.configured && <p className="text-xs text-warning">Chưa có khoá Gemini trên server nên chưa sáng tác được.</p>}
              {ai.errors.length > 0 && <p className="text-xs text-danger">Lần trước chưa sáng tác được: {ai.errors.join(' | ')}</p>}
            </>
          )}
        </Card>
      )}

      <Card className="space-y-3 p-4">
        <div className="flex items-center gap-2 text-sm font-semibold text-fg">
          <Music2 className="h-4 w-4" /> Thêm nhạc vào kho
        </div>
        <p className="text-xs text-fg-muted">
          Khi lập kế hoạch, AI đọc tên và tâm trạng từng bài để chọn bài hợp với kịch bản; nhạc được lặp/cắt cho vừa video và tự nhỏ lại khi có
          thoại. Chỉ dùng nhạc anh có quyền đăng (ví dụ tải từ Meta Sound Collection) — nhạc có bản quyền sẽ làm video bị chặn kiếm tiền.
        </p>
        <div className="grid gap-3 sm:grid-cols-3">
          <Field label="File nhạc (MP3, MP4, M4A, WAV, OGG — tối đa 25 MB; chọn được nhiều file một lần)">
            <Input ref={inputRef} type="file" multiple onChange={(e) => pick(e.target.files)} />
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
