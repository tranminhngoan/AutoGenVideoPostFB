import { useEffect, useMemo, useState } from 'react';
import { ArrowDown, ArrowUp, Camera, ChevronDown, Copy, History, Mic, RotateCcw, Save, Sparkles, Wand2 } from 'lucide-react';
import { Alert, Badge, Button, Drawer, Field, Input, Segmented, Select, Textarea, Thumb, cn } from '../../components/ui';
import { useToast } from '../../components/feedback';
import { formatDateTime } from '../../lib/format';
import { studio } from '../../services/studio';
import { aiStudio, type Json, type Project, type PromptMode, type PromptVersion, type Scene, type Variation } from '../../services/aiStudio';
import { CAMERA_VI, Chip, ConfidenceMeter, GenStatusBadge, MODE_INFO, Swatches, fact, sec, usd, useAiStudio } from './bits';
import type { WorkspaceProps } from './Workspace';

export function Storyboard({ project: p, setProject, reload }: WorkspaceProps) {
  const [selected, setSelected] = useState<string | null>(p.scenes[0]?.id ?? null);
  const total = p.scenes.reduce((a, s) => a + (s.endSeconds - s.startSeconds), 0) || 1;

  return (
    <div className="space-y-5">
      <PromptSettings project={p} setProject={setProject} />

      {/* timeline: one block per scene, width = its length in the reference */}
      <div>
        <div className="mb-1.5 flex items-center justify-between text-xs text-fg-muted">
          <span>Dòng thời gian ({p.scenes.length} cảnh · {sec(p.durationSeconds)})</span>
          <span>Bấm một cảnh để xem</span>
        </div>
        <div className="flex h-14 w-full overflow-hidden rounded-xl border border-line">
          {p.scenes.map((s) => (
            <button key={s.id} type="button" title={`Cảnh ${s.order + 1}: ${sec(s.startSeconds)}–${sec(s.endSeconds)}`}
              onClick={() => { setSelected(s.id); document.getElementById(`scene-${s.id}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' }); }}
              style={{ width: `${((s.endSeconds - s.startSeconds) / total) * 100}%` }}
              className={cn('relative min-w-[18px] border-r border-line last:border-r-0', !s.enabled && 'opacity-30', selected === s.id && 'ring-2 ring-inset ring-accent')}>
              {s.hasThumb && <img src={aiStudio.sceneThumbUrl(s.id)} alt="" className="absolute inset-0 h-full w-full object-cover" loading="lazy" />}
              <span className="absolute left-1 top-1 rounded bg-black/60 px-1 text-[10px] font-semibold text-white">{s.order + 1}</span>
              {s.latest?.status === 'Done' && <span className="absolute bottom-1 right-1 h-2 w-2 rounded-full bg-success ring-2 ring-white" />}
            </button>
          ))}
        </div>
      </div>

      <div className="space-y-3">
        {p.scenes.map((s, i) => (
          <SceneCard key={s.id} project={p} scene={s} first={i === 0} last={i === p.scenes.length - 1}
            open={selected === s.id} onToggle={() => setSelected(selected === s.id ? null : s.id)}
            onChanged={reload} setProject={setProject} />
        ))}
      </div>
    </div>
  );
}

// ───────────────────────── prompt settings ─────────────────────────

function PromptSettings({ project: p, setProject }: { project: Project; setProject: (p: Project) => void }) {
  const toast = useToast();
  const { overview } = useAiStudio();
  const [mode, setMode] = useState<PromptMode>(p.promptMode);
  const [topic, setTopic] = useState(p.topic ?? '');
  const [v, setV] = useState<Variation>(p.variation ?? {});
  const [global, setGlobal] = useState(p.globalPrompt);
  const [negative, setNegative] = useState(p.negativePrompt);
  const [overwrite, setOverwrite] = useState(false);
  const [saving, setSaving] = useState(false);
  const [versions, setVersions] = useState(false);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    setMode(p.promptMode);
    setTopic(p.topic ?? '');
    setV(p.variation ?? {});
    setGlobal(p.globalPrompt);
    setNegative(p.negativePrompt);
  }, [p.promptMode, p.topic, p.globalPrompt, p.negativePrompt, JSON.stringify(p.variation)]);

  const dirty = mode !== p.promptMode || topic !== (p.topic ?? '') || global !== p.globalPrompt || negative !== p.negativePrompt || JSON.stringify(v) !== JSON.stringify(p.variation ?? {});

  const apply = async () => {
    setSaving(true);
    try {
      setProject(await aiStudio.promptSettings(p.id, { mode, topic, variation: v, globalPrompt: global, negativePrompt: negative, overwriteEdited: overwrite }));
      toast.success('Đã áp dụng', overwrite ? 'Tất cả prompt đã được viết lại.' : 'Prompt của các cảnh (trừ cảnh bạn tự sửa) đã được viết lại.');
    } catch (e) {
      toast.error('Chưa áp dụng được', (e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  const vf: [keyof Variation, string, string][] = [
    ['character', 'Nhân vật mới', 'a young man in his 20s with short dark hair'],
    ['wardrobe', 'Trang phục', 'a white linen shirt and beige trousers'],
    ['setting', 'Bối cảnh', 'a rooftop garden in Saigon at sunset'],
    ['camera', 'Góc quay', 'low-angle tracking shot'],
    ['lighting', 'Ánh sáng', 'golden hour backlight'],
    ['style', 'Phong cách', 'warm Kodak film look'],
  ];

  return (
    <div className="rounded-xl border border-line bg-subtle p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2 text-[13px] font-semibold text-fg"><Wand2 className="h-4 w-4 text-accent" />Prompt chung
          <Chip>{MODE_INFO[p.promptMode].label}</Chip>
          <Chip>{(overview?.topics ?? []).find((t) => t.id === p.topic)?.label ?? 'Không chủ đề'}</Chip>
        </div>
        <Button size="sm" variant={open ? 'ghost' : 'secondary'} icon={<ChevronDown className={cn('h-4 w-4 transition', open && 'rotate-180')} />} onClick={() => setOpen(!open)}>
          {open ? 'Thu gọn' : 'Chỉnh chủ đề / kiểu prompt'}
        </Button>
      </div>
      {open && (<>
      <div className="mt-3"><Segmented<PromptMode> size="sm" value={mode} onChange={setMode}
        options={(['Faithful', 'Creative', 'Cinematic'] as PromptMode[]).map((m) => ({ value: m, label: MODE_INFO[m].label }))} /></div>
      <p className="mt-2 text-xs text-fg-muted">{MODE_INFO[mode].description}</p>
      <div className="mt-3 grid gap-2 sm:grid-cols-3">
        {[{ id: '', label: 'Không chủ đề', description: 'Theo đúng video' }, ...(overview?.topics ?? [])].map((t) => (
          <button key={t.id} type="button" onClick={() => setTopic(t.id)}
            className={cn('rounded-lg border px-3 py-2 text-left text-xs transition', topic === t.id ? 'border-accent bg-accent/[0.06] ring-1 ring-accent/40' : 'border-line bg-surface hover:bg-muted')}>
            <div className="text-[13px] font-medium text-fg">{t.label}</div>
            <div className="text-fg-muted">{t.description}</div>
          </button>
        ))}
      </div>

      {mode === 'Creative' && (
        <div className="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {vf.map(([k, label, ph]) => (
            <Field key={k} label={label} optional>
              <Input value={v[k] ?? ''} placeholder={ph} onChange={(e) => setV({ ...v, [k]: e.target.value })} />
            </Field>
          ))}
        </div>
      )}

      <div className="mt-3 grid gap-3 lg:grid-cols-2">
        <Field label="Global prompt (đặt ở đầu mọi cảnh)" hint="Khoá phong cách chung. Tiếng Anh.">
          <Textarea rows={3} value={global} onChange={(e) => setGlobal(e.target.value)} />
        </Field>
        <Field label="Ràng buộc / tránh" hint="Seedance không có ô negative prompt riêng: phần này được ghi cuối prompt dạng “Avoid: …”.">
          <Textarea rows={3} value={negative} onChange={(e) => setNegative(e.target.value)} />
        </Field>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-xs text-fg-muted"><input type="checkbox" checked={overwrite} onChange={(e) => setOverwrite(e.target.checked)} />Viết lại cả prompt tôi đã tự sửa</label>
        <Button size="sm" variant="ghost" icon={<History className="h-4 w-4" />} onClick={() => setVersions(true)}>Phiên bản global prompt</Button>
        <Button size="sm" variant="primary" className="ml-auto" icon={<Sparkles className="h-4 w-4" />} loading={saving} disabled={!dirty && !overwrite} onClick={apply}>
          Áp dụng & viết lại prompt
        </Button>
      </div>
      </>)}
      {versions && <VersionsDrawer project={p} onClose={() => setVersions(false)} onRestored={(np) => { setProject(np); setVersions(false); }} />}
    </div>
  );
}

// ───────────────────────── one scene ─────────────────────────

const GROUPS: [string, string, [string, string][]][] = [
  ['body_movement', 'Chuyển động cơ thể', [['head', 'Đầu'], ['shoulders', 'Vai'], ['arms', 'Cánh tay'], ['hands', 'Bàn tay'], ['torso', 'Thân'], ['hips', 'Hông'], ['legs', 'Chân'], ['feet', 'Bàn chân'], ['coordination', 'Phối hợp']]],
  ['facial_expression', 'Biểu cảm', [['expression', 'Nét mặt'], ['emotion', 'Cảm xúc'], ['eyes', 'Ánh mắt'], ['gaze_direction', 'Hướng nhìn'], ['changes', 'Thay đổi']]],
  ['environment', 'Bối cảnh', [['location', 'Địa điểm'], ['space', 'Không gian'], ['landscape', 'Cảnh quan'], ['architecture', 'Kiến trúc'], ['objects', 'Đồ vật'], ['background', 'Hậu cảnh'], ['foreground', 'Tiền cảnh'], ['weather', 'Thời tiết'], ['time_of_day', 'Thời điểm']]],
  ['camera', 'Máy quay (AI thấy)', [['angle', 'Góc'], ['shot_size', 'Cỡ cảnh'], ['movement_seen', 'Chuyển động']]],
  ['composition', 'Bố cục', [['subject_placement', 'Vị trí chủ thể'], ['rule_of_thirds', 'Quy tắc 1/3'], ['leading_lines', 'Đường dẫn'], ['foreground', 'Tiền cảnh'], ['background', 'Hậu cảnh'], ['negative_space', 'Khoảng trống'], ['depth', 'Chiều sâu']]],
  ['lighting', 'Ánh sáng', [['source', 'Nguồn'], ['direction', 'Hướng'], ['intensity', 'Cường độ'], ['shadows', 'Bóng'], ['highlights', 'Vùng sáng'], ['color_temperature', 'Nhiệt độ màu']]],
  ['color', 'Màu', [['dominant', 'Chủ đạo'], ['tone', 'Tông'], ['saturation', 'Bão hoà'], ['contrast', 'Tương phản'], ['white_balance', 'Cân bằng trắng'], ['grading', 'Chỉnh màu']]],
  ['depth_of_field', 'Độ sâu trường ảnh', [['focus_subject', 'Lấy nét'], ['background_blur', 'Mờ hậu cảnh'], ['foreground_blur', 'Mờ tiền cảnh'], ['focus_change', 'Chuyển nét'], ['lens_look', 'Cảm giác ống kính']]],
  ['motion_quality', 'Chất lượng chuyển động', [['naturalness', 'Tự nhiên'], ['acceleration', 'Tăng/giảm tốc'], ['inertia', 'Quán tính'], ['balance', 'Thăng bằng'], ['cloth', 'Vải'], ['hair', 'Tóc'], ['environment', 'Môi trường']]],
  ['realism', 'Độ chân thực', [['skin', 'Da'], ['hair', 'Tóc'], ['fabric', 'Vải'], ['surfaces', 'Bề mặt'], ['reflections', 'Phản xạ'], ['shadows', 'Bóng đổ'], ['physics', 'Vật lý'], ['overall', 'Tổng thể']]],
];

function SceneCard({ project: p, scene: s, first, last, open, onToggle, onChanged, setProject }: {
  project: Project; scene: Scene; first: boolean; last: boolean; open: boolean; onToggle: () => void;
  onChanged: () => Promise<void>; setProject: (p: Project) => void;
}) {
  const toast = useToast();
  const { overview } = useAiStudio();
  const [text, setText] = useState(s.prompt);
  const [saving, setSaving] = useState(false);
  const [versions, setVersions] = useState(false);
  useEffect(() => setText(s.prompt), [s.prompt]);
  const a: Json = s.analysis ?? {};
  const m: Json = s.measured ?? {};
  const moves: string[] = Array.isArray(m.camera_movement) ? m.camera_movement : [];
  const palette: string[] = Array.isArray(m.dominant_colors) ? m.dominant_colors.map((c: Json) => c.hex) : [];
  const timeline: Json[] = Array.isArray(a.timeline) ? a.timeline : [];
  const characters: Json[] = Array.isArray(a.characters) ? a.characters : [];
  const max = overview?.maxClipSeconds ?? 12;
  const min = overview?.minClipSeconds ?? 2;

  const run = async (f: () => Promise<unknown>, ok?: string) => {
    setSaving(true);
    try {
      await f();
      if (ok) toast.success(ok);
      await onChanged();
    } catch (e) {
      toast.error('Không lưu được', (e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  const move = (dir: -1 | 1) => {
    const ids = p.scenes.map((x) => x.id);
    const i = ids.indexOf(s.id);
    [ids[i], ids[i + dir]] = [ids[i + dir], ids[i]];
    run(async () => setProject(await aiStudio.reorder(p.id, ids)));
  };

  return (
    <div id={`scene-${s.id}`} className={cn('scroll-mt-24 rounded-xl border bg-surface transition', open ? 'border-accent/40 shadow-pop' : 'border-line', !s.enabled && 'opacity-60')}>
      <button type="button" onClick={onToggle} className="flex w-full items-center gap-3 p-3 text-left">
        <Thumb src={s.hasThumb ? aiStudio.sceneThumbUrl(s.id) : null} className="h-16 w-12 shrink-0 rounded-md sm:h-20 sm:w-14" />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-semibold text-fg">Cảnh {s.order + 1}</span>
            <span className="text-xs text-fg-muted">{sec(s.startSeconds)}–{sec(s.endSeconds)} · tạo {s.targetSeconds}s</span>
            {s.isContinuation && <Chip>nối tiếp cảnh trước</Chip>}
            {!s.enabled && <Badge>Đang tắt</Badge>}
            {s.promptEdited && <Chip>prompt tự sửa</Chip>}
            {s.narration && <Chip title="Cảnh này có lời bình (đọc liền một mạch cùng cả video)"><Mic className="h-3 w-3" />có lời bình</Chip>}
            {s.latest && <GenStatusBadge status={s.latest.status} />}
          </div>
          <p className="mt-0.5 line-clamp-1 text-[13px] text-fg-muted">{s.summaryVi ?? fact(a.summary) ?? '—'}</p>
          <div className="mt-1 flex flex-wrap items-center gap-1.5">
            <ConfidenceMeter value={s.confidence} />
            {moves.map((x) => <Chip key={x} title="Đo bằng optical flow"><Camera className="h-3 w-3" />{CAMERA_VI[x] ?? x}</Chip>)}
            {m.motion_level && <Chip>chuyển động {m.motion_level === 'high' ? 'mạnh' : m.motion_level === 'medium' ? 'vừa' : 'nhẹ'}</Chip>}
            {palette.length > 0 && <Swatches colors={palette} />}
          </div>
        </div>
        <ChevronDown className={cn('h-4 w-4 shrink-0 text-fg-subtle transition', open && 'rotate-180')} />
      </button>

      {open && (
        <div className="space-y-4 border-t border-line p-4">
          {/* keyframes */}
          <div className="flex gap-2 overflow-x-auto pb-1">
            {s.frames.map((f) => (
              <figure key={f.t} className="w-28 shrink-0">
                {f.blobId && <img src={aiStudio.frameUrl(s.id, f.blobId)} alt="" loading="lazy" className="aspect-[9/16] w-full rounded-md object-cover" />}
                <figcaption className="mt-1 text-center text-[10px] text-fg-subtle">{sec(f.t)} · {f.reason === 'start' ? 'đầu' : f.reason === 'end' ? 'cuối' : f.reason === 'peak' ? 'hành động' : 'thay đổi'}</figcaption>
              </figure>
            ))}
          </div>

          <div className="grid gap-4 xl:grid-cols-2">
            <div className="space-y-3">
              <div>
                <div className="mb-1.5 text-[13px] font-semibold text-fg">Hành động theo thời gian</div>
                <ol className="space-y-1.5">
                  {timeline.map((t, i) => (
                    <li key={i} className="grid grid-cols-[100px_1fr] gap-2 rounded-lg bg-subtle px-2.5 py-1.5 text-xs">
                      <span className="whitespace-nowrap font-mono text-[11px] text-fg-muted">{sec(t.start)}–{sec(t.end)}</span>
                      <span>
                        <span className="text-fg">{fact(t.action) ?? 'Không thấy rõ'}</span>
                        {fact(t.body) && <span className="block text-fg-muted">{t.body}</span>}
                        {t.confidence === 'low' && <span className="text-warning"> · độ tin cậy thấp</span>}
                      </span>
                    </li>
                  ))}
                </ol>
              </div>
              {characters.length > 0 && (
                <div className="text-xs">
                  <div className="mb-1 text-[13px] font-semibold text-fg">Nhân vật trong cảnh</div>
                  {characters.map((c, i) => (
                    <p key={i} className="text-fg-muted">{[c.label, c.age_group, c.apparent_ethnicity, c.skin_tone, c.hair, c.clothing, c.footwear, c.posture].map(fact).filter(Boolean).join(' · ')}</p>
                  ))}
                </div>
              )}
              {fact(a.object_interaction) && <p className="text-xs text-fg-muted"><b className="text-fg">Tương tác đồ vật:</b> {a.object_interaction}</p>}
              {s.narration && <p className="text-xs text-fg-muted"><Mic className="mr-1 inline h-3 w-3" /><b className="text-fg">Lời bình:</b> {s.narration} <span className="text-fg-subtle">(sửa ở tab Lời bình)</span></p>}
              <details className="rounded-lg border border-line">
                <summary className="cursor-pointer px-3 py-2 text-xs font-medium text-fg">Phân tích chi tiết (15 nhóm)</summary>
                <div className="grid gap-3 p-3 sm:grid-cols-2">
                  {GROUPS.map(([key, title, fields]) => {
                    const g: Json = a[key] ?? {};
                    const rows = fields.map(([k, l]) => [l, fact(g[k])] as const).filter(([, v]) => v);
                    if (rows.length === 0) return null;
                    return (
                      <div key={key}>
                        <div className="mb-1 text-[11px] font-semibold uppercase tracking-wide text-fg-subtle">{title}</div>
                        {rows.map(([l, v]) => <div key={l} className="text-xs"><span className="text-fg-subtle">{l}: </span><span className="text-fg">{v}</span></div>)}
                      </div>
                    );
                  })}
                  <div>
                    <div className="mb-1 text-[11px] font-semibold uppercase tracking-wide text-fg-subtle">Đo bằng máy (không AI)</div>
                    <div className="text-xs text-fg-muted">Lia {m.pan_speed ?? 0} · Ngước {m.tilt_speed ?? 0} · Zoom {m.zoom_rate ?? 0} · Rung {m.shake ?? 0}</div>
                    <div className="text-xs text-fg-muted">Sáng {m.brightness ?? '—'} · Bão hoà {m.saturation ?? '—'} · Tông {m.tone ?? '—'} · Tương phản {m.contrast ?? '—'}</div>
                  </div>
                </div>
              </details>
            </div>

            <div className="space-y-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="text-[13px] font-semibold text-fg">Prompt cho Seedance <span className="font-normal text-fg-subtle">v{s.promptVersion} · {text.length} ký tự</span></div>
                <div className="flex gap-1">
                  <Button size="sm" variant="ghost" icon={<Copy className="h-3.5 w-3.5" />} onClick={() => { navigator.clipboard.writeText(text); toast.success('Đã copy prompt'); }}>Copy</Button>
                  <Button size="sm" variant="ghost" icon={<History className="h-3.5 w-3.5" />} onClick={() => setVersions(true)}>Phiên bản</Button>
                </div>
              </div>
              <Textarea rows={11} value={text} onChange={(e) => setText(e.target.value)} className="font-mono text-[12px] leading-relaxed" />
              <div className="flex flex-wrap items-center gap-2">
                <Button size="sm" variant="primary" icon={<Save className="h-3.5 w-3.5" />} loading={saving} disabled={text.trim() === s.prompt.trim() || !text.trim()}
                  onClick={() => run(() => aiStudio.editPrompt(p.id, s.id, text), 'Đã lưu prompt')}>Lưu</Button>
                <Button size="sm" variant="secondary" icon={<RotateCcw className="h-3.5 w-3.5" />} disabled={saving}
                  onClick={() => run(() => aiStudio.regeneratePrompt(p.id, s.id), 'Đã tạo lại prompt từ phân tích')}>Tạo lại từ phân tích</Button>
                <span className="ml-auto text-xs text-fg-muted">~{usd(s.estimatedCostUsd)} / lần tạo</span>
              </div>
              <div className="flex flex-wrap items-center gap-3 rounded-lg bg-subtle px-3 py-2 text-xs">
                <label className="flex items-center gap-2"><input type="checkbox" checked={s.enabled} disabled={saving}
                  onChange={(e) => run(() => aiStudio.updateScene(p.id, s.id, { enabled: e.target.checked }))} />Dùng cảnh này</label>
                <label className="flex items-center gap-2">Thời lượng clip
                  <Select size="sm" className="w-20" value={String(s.targetSeconds)} disabled={saving}
                    onChange={(e) => { const n = Number(e.target.value); run(() => aiStudio.updateScene(p.id, s.id, { targetSeconds: n })); }}>
                    {Array.from({ length: max - min + 1 }, (_, i) => <option key={i} value={min + i}>{min + i}s</option>)}
                  </Select>
                </label>
                <span className="text-fg-subtle">gốc {(s.endSeconds - s.startSeconds).toFixed(1)}s</span>
                <span className="ml-auto flex gap-1">
                  <Button size="sm" variant="ghost" icon={<ArrowUp className="h-3.5 w-3.5" />} disabled={first || saving} onClick={() => move(-1)} />
                  <Button size="sm" variant="ghost" icon={<ArrowDown className="h-3.5 w-3.5" />} disabled={last || saving} onClick={() => move(1)} />
                </span>
              </div>
            </div>
          </div>
        </div>
      )}
      {versions && <VersionsDrawer project={p} scene={s} onClose={() => setVersions(false)} onRestored={(np) => { setProject(np); setVersions(false); }} />}
    </div>
  );
}

// ───────────────────────── narration ─────────────────────────

/** Step "Lời bình": the narrator (voice, direction, the one reading) and every scene's words, editable in one list. */
export function NarrationTab({ project: p, setProject, reload }: WorkspaceProps) {
  const withText = p.scenes.filter((s) => s.narration);
  return (
    <div className="space-y-4">
      <NarrationPanel project={p} setProject={setProject} reload={reload} />
      <div>
        <div className="mb-2 text-[13px] font-semibold text-fg">Lời bình từng cảnh <span className="font-normal text-fg-subtle">({withText.length}/{p.scenes.length} cảnh có lời)</span></div>
        <div className="space-y-2">
          {p.scenes.map((s) => (
            <div key={s.id} className={cn('flex gap-3', !s.enabled && 'opacity-50')}>
              <div className="w-12 shrink-0 text-center">
                <Thumb src={s.hasThumb ? aiStudio.sceneThumbUrl(s.id) : null} className="h-16 w-12 rounded-md" />
                <div className="mt-1 text-[11px] font-semibold text-fg">Cảnh {s.order + 1}</div>
                <div className="text-[10px] text-fg-subtle">{sec(s.startSeconds)}</div>
              </div>
              <div className="min-w-0 flex-1"><SceneNarration project={p} scene={s} onChanged={reload} /></div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

/** Narrator for the whole video: a voice like the reference speaker, its acting direction, and reading every scene. */
function NarrationPanel({ project: p, setProject, reload }: { project: Project; setProject: (p: Project) => void; reload: () => Promise<void> }) {
  const toast = useToast();
  const { overview } = useAiStudio();
  const [voice, setVoice] = useState(p.narratorVoice);
  const [style, setStyle] = useState(p.narratorStyle);
  const [saving, setSaving] = useState(false);
  const [reading, setReading] = useState(false);
  useEffect(() => { setVoice(p.narratorVoice); setStyle(p.narratorStyle); }, [p.narratorVoice, p.narratorStyle]);

  const withText = p.scenes.filter((s) => s.enabled && s.narration);
  const videoSeconds = p.scenes.filter((s) => s.enabled).reduce((t, s) => t + (s.latest?.status === 'Done' ? s.latest.seconds : s.targetSeconds), 0);
  const segments: Json[] = Array.isArray(p.transcript?.segments) ? p.transcript.segments : [];
  const dirty = voice !== p.narratorVoice || style !== p.narratorStyle;
  const voices = overview?.voices ?? [];

  const save = async (body: { voice?: string; style?: string; suggest?: boolean }, ok: string) => {
    setSaving(true);
    try { setProject(await aiStudio.narrator(p.id, body)); toast.success(ok); }
    catch (e) { toast.error('Chưa lưu được', (e as Error).message); }
    finally { setSaving(false); }
  };

  const readAll = async () => {
    setReading(true);
    try {
      const next = await aiStudio.speakNarration(p.id);
      setProject(next);
      toast.success('Đã đọc xong lời bình', `Đọc liền một mạch ${next.narrationSeconds?.toFixed(1) ?? '?'}s cho cả video.`);
    } catch (e) {
      toast.error('Chưa đọc được lời bình', (e as Error).message);
      await reload();
    } finally {
      setReading(false);
    }
  };

  return (
    <div className="rounded-xl border border-line bg-subtle p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-[13px] font-semibold text-fg"><Mic className="h-4 w-4 text-accent" />Lời bình & giọng AI đọc lại</div>
        <span className={cn('text-xs', p.narrationCurrent ? 'text-fg-muted' : 'text-warning')}>
          {p.narrationCurrent ? `Đã đọc ${p.narrationSeconds?.toFixed(1)}s` : p.hasNarrationTake ? 'Chữ / giọng đã đổi — cần đọc lại' : 'Chưa đọc'}
        </span>
      </div>
      <p className="mt-1 text-xs text-fg-muted">
        Lời bình được nghe từ video gốc (chạy trên máy), chia theo từng cảnh, đưa vào prompt làm ngữ cảnh câu chuyện, rồi một giọng AI đọc lại <b>liền một mạch cho cả video</b> (không ngắt theo từng cảnh) theo phong cách người đọc gốc. Không dùng giọng của người trong video gốc.
      </p>
      <p className="mt-1 text-xs"><b className="text-fg">Giọng gốc đo được:</b> <span className="text-fg-muted">{p.speakerProfileVi ?? 'chưa đo được (phân tích lại video để đo).'}</span></p>
      {segments.length === 0 && <Alert className="mt-2" tone="warning" title="Chưa có lời bình">Bản phân tích này chưa nghe được lời nói nào. Nếu video có lời bình, bấm Phân tích lại ở bước 2.</Alert>}
      {segments.length > 0 && withText.length === 0 && (
        <div className="mt-2 flex items-center gap-2 text-xs text-fg-muted">
          Đã nghe được {segments.length} câu nhưng chưa chia vào cảnh.
          <Button size="sm" variant="secondary" loading={saving} onClick={async () => { setSaving(true); try { setProject(await aiStudio.resetNarration(p.id)); } catch (e) { toast.error('Chưa chia được', (e as Error).message); } finally { setSaving(false); } }}>Chia lời bình vào cảnh</Button>
        </div>
      )}
      <div className="mt-3 grid gap-3 lg:grid-cols-[260px_1fr]">
        <Field label="Giọng đọc" hint="Giọng thiết kế ở tab Giọng đọc của Studio (anh 3, chị ít…) nằm ở nhóm đầu.">
          <Select value={voice} onChange={(e) => setVoice(e.target.value)}>
            {voices.some((v) => v.designed) && (
              <optgroup label="Giọng thiết kế của kênh">
                {voices.filter((v) => v.designed).map((v) => <option key={v.id} value={v.id}>{v.name} ({v.gender === 'female' ? 'nữ' : 'nam'})</option>)}
              </optgroup>
            )}
            <optgroup label="Giọng có sẵn">
              {voices.filter((v) => !v.designed).map((v) => <option key={v.id} value={v.id}>{v.name} — {v.description}</option>)}
            </optgroup>
          </Select>
          {voice && <audio key={voice} controls preload="none" src={studio.voicePreviewUrl(voice)} className="mt-1.5 h-8 w-full" title="Nghe thử giọng này" />}
        </Field>
        <Field label="Cách đọc (tiếng Anh — gửi riêng cho model giọng, không bị đọc thành lời)">
          <Textarea rows={2} value={style} onChange={(e) => setStyle(e.target.value)} />
        </Field>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <Button size="sm" variant="secondary" icon={<Save className="h-3.5 w-3.5" />} loading={saving} disabled={!dirty} onClick={() => save({ voice, style }, 'Đã lưu giọng đọc')}>Lưu giọng</Button>
        <Button size="sm" variant="ghost" icon={<Wand2 className="h-3.5 w-3.5" />} disabled={saving} onClick={() => save({ suggest: true }, 'Đã chọn lại giọng theo giọng gốc')}>Gợi ý theo giọng gốc</Button>
        {segments.length > 0 && withText.length > 0 && (
          <Button size="sm" variant="ghost" icon={<RotateCcw className="h-3.5 w-3.5" />} disabled={saving}
            onClick={async () => { if (!window.confirm('Chia lại lời bình từ video gốc? Chữ bạn đã sửa sẽ bị thay.')) return; setSaving(true); try { setProject(await aiStudio.resetNarration(p.id)); } catch (e) { toast.error('Chưa chia được', (e as Error).message); } finally { setSaving(false); } }}>
            Chia lại từ video gốc
          </Button>
        )}
        <Button size="sm" variant="primary" className="ml-auto" icon={<Mic className="h-3.5 w-3.5" />} loading={reading} disabled={dirty || withText.length === 0 || saving || p.narrationCurrent} onClick={readAll}>
          {reading ? 'Đang đọc…' : p.narrationCurrent ? 'Đã đọc' : p.hasNarrationTake ? 'Đọc lại lời bình' : 'Đọc lời bình'}
        </Button>
      </div>
      {dirty && <p className="mt-1 text-right text-[11px] text-warning">Lưu giọng trước rồi mới đọc.</p>}
      {p.hasNarrationTake && (
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <audio controls preload="none" src={aiStudio.narrationUrl(p.id, p.narrationSeconds)} className="h-8 w-full max-w-md" />
          {p.narrationCurrent && (p.narrationSeconds ?? 0) > videoSeconds && (
            <span className="text-[11px] text-fg-muted">
              Bản đọc dài hơn video (~{videoSeconds}s) — khi ghép sẽ đọc nhanh lên chút (tối đa 25%), còn dư thì giữ khung hình cuối cho đọc hết.
            </span>
          )}
        </div>
      )}
    </div>
  );
}

/** One scene's narration: the words (editable) and their English meaning in the prompt (read with the whole video). */
function SceneNarration({ project: p, scene: s, onChanged }: { project: Project; scene: Scene; onChanged: () => Promise<void> }) {
  const toast = useToast();
  const [text, setText] = useState(s.narration ?? '');
  const [busy, setBusy] = useState(false);
  useEffect(() => setText(s.narration ?? ''), [s.narration]);
  const dirty = text.trim() !== (s.narration ?? '').trim();

  const save = async () => {
    setBusy(true);
    try {
      await aiStudio.editNarration(p.id, s.id, text);
      await onChanged();
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="rounded-lg border border-line p-2.5">
      <Textarea rows={2} value={text} placeholder="Cảnh này không có lời bình (để trống = im lặng)." onChange={(e) => setText(e.target.value)} />
      {s.narrationEn && !s.narrationEdited && <p className="mt-1 text-[11px] text-fg-subtle"><b>Ý nghĩa trong prompt:</b> {s.narrationEn}</p>}
      <div className="mt-2 flex flex-wrap items-center gap-2">
        <Button size="sm" variant="secondary" icon={<Save className="h-3.5 w-3.5" />} loading={busy} disabled={!dirty || busy} onClick={save}>Lưu chữ</Button>
        {s.narrationEdited && <Chip>bạn đã sửa</Chip>}
        {dirty && <span className="text-[11px] text-fg-subtle">Lưu xong bấm “Đọc lại lời bình” ở khung trên.</span>}
      </div>
    </div>
  );
}

// ───────────────────────── versions ─────────────────────────

function VersionsDrawer({ project, scene, onClose, onRestored }: { project: Project; scene?: Scene; onClose: () => void; onRestored: (p: Project) => void }) {
  const toast = useToast();
  const [list, setList] = useState<PromptVersion[] | null>(null);
  useEffect(() => {
    aiStudio.versions(project.id, scene?.id).then(setList).catch((e) => toast.error('Không tải được phiên bản', (e as Error).message));
  }, [project.id, scene?.id]);
  const current = scene ? scene.prompt : project.globalPrompt;
  const SOURCE: Record<string, string> = { engine: 'Engine viết', user: 'Bạn sửa', restore: 'Khôi phục' };
  const sorted = useMemo(() => list ?? [], [list]);

  return (
    <Drawer open onClose={onClose} title={scene ? `Phiên bản prompt · cảnh ${scene.order + 1}` : 'Phiên bản global prompt'} subtitle="Khôi phục tạo một phiên bản mới, không xoá phiên bản nào.">
      <div className="space-y-3">
        {list == null && <p className="text-sm text-fg-muted">Đang tải…</p>}
        {list?.length === 0 && <p className="text-sm text-fg-muted">Chưa có phiên bản nào.</p>}
        {sorted.map((v) => (
          <div key={v.id} className={cn('rounded-lg border p-3', v.text === current ? 'border-accent/50 bg-accent/[0.04]' : 'border-line')}>
            <div className="mb-1.5 flex items-center justify-between gap-2 text-xs">
              <span className="font-semibold text-fg">v{v.version} · {SOURCE[v.source] ?? v.source} · {MODE_INFO[v.mode]?.short ?? v.mode}</span>
              <span className="text-fg-subtle">{formatDateTime(v.createdAtUtc)}</span>
            </div>
            <p className="line-clamp-6 whitespace-pre-wrap font-mono text-[11px] text-fg-muted">{v.text}</p>
            {v.text !== current && (
              <Button size="sm" className="mt-2" icon={<RotateCcw className="h-3.5 w-3.5" />}
                onClick={async () => { try { onRestored(await aiStudio.restore(project.id, v.id)); toast.success(`Đã khôi phục v${v.version}`); } catch (e) { toast.error('Không khôi phục được', (e as Error).message); } }}>
                Khôi phục
              </Button>
            )}
          </div>
        ))}
      </div>
    </Drawer>
  );
}
