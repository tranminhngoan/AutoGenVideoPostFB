import { useEffect, useState } from 'react';
import { Mic, Square, Star, Trash2, Volume2 } from 'lucide-react';
import { Badge, Button, Card, EmptyState, Field, Input, Select, Skeleton, Textarea } from '../ui';
import { useConfirm, useToast } from '../feedback';
import { StudioSettings, studio } from '../../services/studio';

/** Ready-made descriptions for the channel's narrator: Southern (Mekong Delta) storytellers. */
const PRESETS = [
  {
    name: 'Anh Ba miền Tây',
    gender: 'male',
    description:
      'Một người đàn ông Việt Nam khoảng 50 tuổi, quê miền Tây Nam Bộ (Bến Tre, Cần Thơ), nói giọng miền Tây đặc trưng, tự nhiên, rõ chữ. ' +
      'Giọng trầm ấm, chân chất, mộc mạc, hiền lành; kể chuyện thong thả như đang ngồi hiên nhà kể cho hàng xóm nghe, thỉnh thoảng cười nhẹ trong giọng. ' +
      'Không bi lụy, không kịch, không giọng phát thanh viên. ' +
      'A Vietnamese man around 50 from the Mekong Delta with a natural Southern Vietnamese accent: warm, deep, rustic and friendly storyteller, relaxed pace.',
  },
  {
    name: 'Chị Út miền Tây',
    gender: 'female',
    description:
      'Một người phụ nữ Việt Nam khoảng 35 tuổi, quê miền Tây Nam Bộ (Vĩnh Long, An Giang), nói giọng miền Tây ngọt ngào, tự nhiên, rõ chữ. ' +
      'Giọng ấm, dịu dàng, chân chất, gần gũi; kể chuyện thong thả như tâm sự với người nhà, có nụ cười trong giọng. ' +
      'Không bi lụy, không nũng nịu, không giọng phát thanh viên. ' +
      'A Vietnamese woman around 35 from the Mekong Delta with a natural Southern Vietnamese accent: warm, gentle, sincere storyteller, relaxed pace.',
  },
  {
    name: 'Ông Tư kể chuyện xưa',
    gender: 'male',
    description:
      'Một ông lão Việt Nam khoảng 68 tuổi ở miền Tây sông nước, giọng miền Tây khàn nhẹ, chậm rãi, từng trải, hóm hỉnh hiền hậu; ' +
      'kể chuyện ngày xưa như ông nội kể cho cháu nghe bên bếp lửa. Không bi lụy, không kịch. ' +
      'An elderly Vietnamese man from the Mekong Delta, slightly husky Southern accent, slow, wise, gently humorous storyteller.',
  },
];

/** Voices designed for this channel (Gemini voice design): a Southern accent, the narrator used by default. */
export function VoiceDesigner({ onAuthError }: { onAuthError: (e: unknown) => boolean }) {
  const toast = useToast();
  const confirm = useConfirm();
  const [settings, setSettings] = useState<StudioSettings | null>(null);
  const [name, setName] = useState(PRESETS[0].name);
  const [gender, setGender] = useState(PRESETS[0].gender);
  const [description, setDescription] = useState(PRESETS[0].description);
  const [makeDefault, setMakeDefault] = useState(true);
  const [creating, setCreating] = useState(false);
  const [playing, setPlaying] = useState<string | null>(null);
  const [audio] = useState(() => (typeof Audio !== 'undefined' ? new Audio() : null));

  useEffect(() => {
    studio.settings().then(setSettings).catch((e) => toast.error((e as Error).message));
    if (!audio) return;
    const stop = () => setPlaying(null);
    audio.addEventListener('ended', stop);
    return () => {
      audio.pause();
      audio.removeEventListener('ended', stop);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const play = (id: string) => {
    if (!audio) return;
    if (playing === id) {
      audio.pause();
      setPlaying(null);
      return;
    }
    audio.src = studio.voicePreviewUrl(id);
    audio.play().catch(() => setPlaying(null));
    setPlaying(id);
  };

  const run = async (action: () => Promise<StudioSettings>, ok?: string) => {
    try {
      setSettings(await action());
      if (ok) toast.success(ok);
    } catch (e) {
      if (!onAuthError(e)) toast.error((e as Error).message);
    }
  };

  const create = async () => {
    setCreating(true);
    await run(() => studio.designVoice({ name: name.trim(), gender, description: description.trim(), makeDefault }),
      `Đã tạo giọng "${name.trim()}" — bấm nghe thử ở danh sách bên dưới.`);
    setCreating(false);
  };

  const designed = (settings?.voices ?? []).filter((v) => v.designed);

  return (
    <div className="space-y-5">
      <Card className="space-y-3 p-4">
        <div className="flex items-center gap-2 text-sm font-semibold text-fg">
          <Mic className="h-4 w-4" /> Tạo giọng đọc riêng cho kênh (giọng miền Tây…)
        </div>
        <p className="text-xs text-fg-muted">
          Tả giọng bằng lời (tuổi, giới tính, vùng miền, chất giọng) — Google Gemini tạo một giọng mới chỉ của kênh bạn, có bản nghe thử ngay.
          Giọng đặt làm mặc định sẽ đọc lời bình mọi video dựng từ trang kịch bản. Gần như miễn phí (vài phần trăm cent).
        </p>
        <div className="flex flex-wrap gap-1.5">
          {PRESETS.map((p) => (
            <button key={p.name} type="button"
              onClick={() => { setName(p.name); setGender(p.gender); setDescription(p.description); }}
              className="rounded-full border border-line px-2.5 py-1 text-xs text-fg-muted hover:bg-muted">
              {p.name}
            </button>
          ))}
        </div>
        <div className="grid gap-3 sm:grid-cols-[1fr_140px]">
          <Field label="Tên giọng">
            <Input value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label="Giới tính">
            <Select value={gender} onChange={(e) => setGender(e.target.value)}>
              <option value="male">Nam</option>
              <option value="female">Nữ</option>
            </Select>
          </Field>
        </div>
        <Field label="Mô tả giọng (càng cụ thể càng giống ý)">
          <Textarea rows={4} value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <label className="flex items-center gap-2 text-xs text-fg">
          <input type="checkbox" checked={makeDefault} onChange={(e) => setMakeDefault(e.target.checked)} />
          Dùng làm giọng lời bình mặc định
        </label>
        <Button variant="primary" size="sm" icon={<Mic className="h-4 w-4" />} loading={creating}
          disabled={!name.trim() || description.trim().length < 20} onClick={create}>
          Tạo giọng
        </Button>
      </Card>

      {!settings ? (
        <Skeleton className="h-24 w-full" />
      ) : designed.length === 0 ? (
        <EmptyState title="Chưa có giọng riêng" description="Chọn một mẫu giọng miền Tây ở trên rồi bấm Tạo giọng." />
      ) : (
        <ul className="space-y-2">
          {designed.map((v) => (
            <li key={v.id}>
              <Card className="flex flex-wrap items-center gap-2 p-3 text-xs">
                <Button size="sm" variant="ghost" icon={playing === v.id ? <Square className="h-3.5 w-3.5" /> : <Volume2 className="h-3.5 w-3.5" />}
                  onClick={() => play(v.id)} aria-label="Nghe thử" />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2 font-semibold text-fg">
                    {v.name} <span className="font-normal text-fg-muted">· {v.gender === 'female' ? 'Nữ' : 'Nam'}</span>
                    {settings.defaultNarratorVoice === v.id && <Badge tone="success">Mặc định</Badge>}
                  </div>
                  <div className="truncate text-fg-muted">{v.description}</div>
                </div>
                {settings.defaultNarratorVoice !== v.id ? (
                  <Button size="sm" variant="secondary" icon={<Star className="h-3.5 w-3.5" />}
                    onClick={() => run(() => studio.setDefaultNarrator(v.id), `Đã đặt "${v.name}" làm giọng mặc định.`)}>
                    Đặt mặc định
                  </Button>
                ) : (
                  <Button size="sm" variant="ghost" onClick={() => run(() => studio.setDefaultNarrator(null), 'Đã bỏ giọng mặc định.')}>
                    Bỏ mặc định
                  </Button>
                )}
                <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} aria-label="Xoá giọng"
                  onClick={async () => {
                    if (await confirm({ title: `Xoá giọng "${v.name}"?`, description: 'Video đang dùng giọng này sẽ đọc bằng giọng có sẵn tương tự.', confirmLabel: 'Xoá', danger: true }))
                      await run(() => studio.deleteDesignedVoice(v.id), 'Đã xoá giọng.');
                  }} />
              </Card>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
