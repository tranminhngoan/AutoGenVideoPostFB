import { Fragment, useEffect, useRef, useState } from 'react';
import { Clapperboard, Copy, Dice5, Download, ImagePlus, Layers, Link2, Play, RefreshCw, Save, Square, Wand2, X } from 'lucide-react';
import { Alert, Button, EmptyState, Field, Input, Modal, Segmented, Select, Spinner, Textarea, Thumb, cn } from '../../components/ui';
import { useToast } from '../../components/feedback';
import { formatDateTime, timeAgo } from '../../lib/format';
import { StudioError } from '../../services/studio';
import { type AudioMode, aiStudio, isGenActive, type Estimate, type Generation, type Project, type Scene } from '../../services/aiStudio';
import { GenStatusBadge, sec, usd, useAiStudio } from './bits';
import type { WorkspaceProps } from './Workspace';

export function GeneratorPanel({ project: p, setProject, reload }: WorkspaceProps) {
  const toast = useToast();
  const { overview, refreshOverview } = useAiStudio();
  const [picked, setPicked] = useState<string[]>(() => p.scenes.filter((s) => s.enabled).map((s) => s.id));
  const [estimate, setEstimate] = useState<Estimate | null>(null);
  const [estimating, setEstimating] = useState(false);
  const [sending, setSending] = useState(false);
  const [seed, setSeed] = useState(p.seed != null ? String(p.seed) : '');
  const imageInput = useRef<HTMLInputElement>(null);
  const models = overview?.videoModels ?? [];
  const model = models.find((m) => m.id === p.videoModel) ?? models[0];
  const enabled = p.scenes.filter((s) => s.enabled);

  useEffect(() => setPicked((cur) => cur.filter((id) => p.scenes.some((s) => s.id === id && s.enabled))), [p.scenes.map((s) => `${s.id}${s.enabled}`).join()]);

  const save = async (patch: Partial<{ videoModel: string; resolution: string; aspectRatio: string; seed: number | null; continuity: boolean; audioMode: AudioMode }>) => {
    try {
      setProject(await aiStudio.generator(p.id, {
        videoModel: patch.videoModel ?? p.videoModel, resolution: patch.resolution ?? p.resolution, aspectRatio: patch.aspectRatio ?? p.genAspectRatio,
        seed: patch.seed !== undefined ? patch.seed : p.seed ?? null, continuity: patch.continuity ?? p.continuity,
        audioMode: patch.audioMode ?? p.audioMode,
      }));
    } catch (e) {
      toast.error('Không lưu được', (e as Error).message);
    }
  };

  const ask = async (ids: string[]) => {
    if (ids.length === 0) return;
    setEstimating(true);
    try {
      setEstimate(await aiStudio.estimate(p.id, ids));
    } catch (e) {
      toast.error('Không ước tính được', (e as Error).message);
    } finally {
      setEstimating(false);
    }
  };

  const go = async () => {
    if (!estimate) return;
    setSending(true);
    try {
      const ids = estimate.lines.filter((l) => !l.skip).map((l) => l.sceneId);
      setProject(await aiStudio.generate(p.id, ids, estimate.totalUsd));
      toast.success('Đã gửi Seedance', `${ids.length} clip · ~${usd(estimate.totalUsd)}. Trang tự cập nhật — có thể đóng trình duyệt, việc vẫn chạy.`);
      setEstimate(null);
      refreshOverview();
    } catch (e) {
      toast.error('Chưa tạo được', (e as Error).message);
    } finally {
      setSending(false);
    }
  };

  const allDone = enabled.length > 0 && enabled.every((s) => s.latest?.status === 'Done');
  const failedIds = enabled.filter((s) => s.latest?.status === 'Failed').map((s) => s.id);
  const missingIds = enabled.filter((s) => !s.latest || s.latest.status === 'Failed' || s.latest.status === 'Cancelled').map((s) => s.id);
  const pickedCost = p.scenes.filter((s) => picked.includes(s.id)).reduce((a, s) => a + s.estimatedCostUsd, 0);

  return (
    <div className="space-y-5">
      {overview && !overview.seedanceConfigured && (
        <Alert tone="danger" title="Seedance chưa có khoá">Đặt biến <code>AI_GATEWAY_API_KEY</code> (Vercel AI Gateway) trên Railway — cùng khoá trang Studio Video AI đang dùng.</Alert>
      )}

      {/* settings */}
      <div className="grid gap-4 rounded-xl border border-line bg-subtle p-4 lg:grid-cols-2">
        <Field label="Model Seedance" hint={model?.note}>
          <Select value={p.videoModel} onChange={(e) => save({ videoModel: e.target.value })}>
            {models.map((m) => <option key={m.id} value={m.id}>{m.label} — {usd(m.pricePerSecondUsd[p.resolution] ?? 0, 4)}/giây ở {p.resolution}</option>)}
          </Select>
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Độ phân giải">
            <Segmented size="sm" value={p.resolution} onChange={(v) => save({ resolution: v })} options={(overview?.resolutions ?? ['480p', '720p', '1080p']).map((r) => ({ value: r, label: r }))} />
          </Field>
          <Field label="Tỷ lệ khung">
            <Segmented size="sm" value={p.genAspectRatio} onChange={(v) => save({ aspectRatio: v })} options={[{ value: '9:16', label: '9:16 dọc' }, { value: '16:9', label: '16:9 ngang' }]} />
          </Field>
        </div>
        <Field label="Seed" optional hint="Cùng seed cho mọi clip giúp nhân vật / màu giống nhau hơn. Để trống = ngẫu nhiên.">
          <div className="flex gap-2">
            <Input inputMode="numeric" value={seed} onChange={(e) => setSeed(e.target.value.replace(/\D/g, '').slice(0, 9))}
              onBlur={() => save({ seed: seed ? Number(seed) : null })} placeholder="ví dụ 1234" />
            <Button variant="secondary" icon={<Dice5 className="h-4 w-4" />} onClick={() => { const v = Math.floor(Math.random() * 1e8); setSeed(String(v)); save({ seed: v }); }} />
          </div>
        </Field>
        <div className="space-y-2">
          <label className="flex items-start gap-2 text-[13px]">
            <input type="checkbox" className="mt-0.5" checked={p.continuity} onChange={(e) => save({ continuity: e.target.checked })} />
            <span><span className="font-medium text-fg"><Link2 className="mr-1 inline h-3.5 w-3.5" />Nối cảnh liền mạch</span>
              <span className="block text-xs text-fg-muted">Clip sau bắt đầu từ khung cuối của clip cảnh trước (ảnh → video). Chạy lần lượt từng cảnh. Nếu model từ chối ảnh, clip báo lỗi rõ (không tính tiền).</span></span>
          </label>
          <div className="flex items-center gap-2 text-[13px]">
            {p.hasReferenceImage ? (
              <>
                <Thumb src={aiStudio.referenceImageUrl(p.id, p.updatedAtUtc)} className="h-12 w-9 rounded" />
                <span className="text-xs text-fg-muted">Ảnh của bạn làm khung đầu cảnh 1</span>
                <Button size="sm" variant="ghost" icon={<X className="h-3.5 w-3.5" />} onClick={async () => setProject(await aiStudio.removeReferenceImage(p.id))}>Bỏ</Button>
              </>
            ) : (
              <Button size="sm" variant="secondary" icon={<ImagePlus className="h-4 w-4" />} onClick={() => imageInput.current?.click()}>Ảnh khung đầu (tuỳ chọn)</Button>
            )}
            <input ref={imageInput} type="file" accept="image/jpeg,image/png,image/webp" hidden onChange={async (e) => {
              const f = e.target.files?.[0]; e.target.value = '';
              if (!f) return;
              try { setProject(await aiStudio.setReferenceImage(p.id, f)); } catch (err) { toast.error('Không tải được ảnh', (err as Error).message); }
            }} />
          </div>
        </div>
      </div>

      {/* scenes */}
      <div>
        <div className="mb-2 flex flex-wrap items-center gap-2">
          <span className="text-[13px] font-semibold text-fg">Cảnh sẽ tạo</span>
          <Button size="sm" variant="ghost" onClick={() => setPicked(enabled.map((s) => s.id))}>Tất cả</Button>
          <Button size="sm" variant="ghost" onClick={() => setPicked(missingIds)} disabled={missingIds.length === 0}>Chưa có clip / lỗi</Button>
          <Button size="sm" variant="ghost" onClick={() => setPicked([])}>Bỏ chọn</Button>
          <span className="ml-auto text-xs text-fg-muted">{picked.length} cảnh · ~{usd(pickedCost)}</span>
          <Button variant="accent" icon={<Wand2 className="h-4 w-4" />} loading={estimating} disabled={picked.length === 0} onClick={() => ask(picked)}>
            Generate Video
          </Button>
        </div>
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {p.scenes.map((s) => (
            <SceneOutput key={s.id} project={p} scene={s} checked={picked.includes(s.id)}
              onCheck={(v) => setPicked((cur) => (v ? [...cur, s.id] : cur.filter((x) => x !== s.id)))}
              onRetry={() => ask([s.id])} onChanged={reload} />
          ))}
        </div>
        {failedIds.length > 0 && (
          <div className="mt-3 flex justify-end">
            <Button size="sm" variant="secondary" icon={<RefreshCw className="h-4 w-4" />} onClick={() => ask(failedIds)}>Tạo lại {failedIds.length} cảnh lỗi</Button>
          </div>
        )}
      </div>

      {/* joined video */}
      <div className="rounded-xl border border-line bg-subtle p-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <div className="flex items-center gap-2 text-[13px] font-semibold text-fg"><Layers className="h-4 w-4 text-accent" />Ghép thành một video</div>
            <p className="text-xs text-fg-muted">Nối clip mới nhất của từng cảnh đang bật theo thứ tự storyboard.</p>
            <div className="mt-2 flex flex-wrap items-center gap-2">
              <Segmented<AudioMode> size="sm" value={p.audioMode} onChange={(v) => save({ audioMode: v })}
                options={[
                  { value: 'None', label: 'Không tiếng (chọn nhạc trên Facebook)' },
                  ...(p.hasAudio ? [{ value: 'Narration' as const, label: 'Giọng AI đọc lời bình' }] : []),
                  ...(p.hasSourceAudio ? [{ value: 'Original' as const, label: 'Giữ tiếng gốc' }] : []),
                ]} />
              {p.audioMode === 'Narration' && (() => {
                const need = enabled.some((s) => s.narration) && !p.narrationCurrent;
                return <span className={cn('text-[11px]', need ? 'text-warning' : 'text-fg-subtle')}>
                  {need ? 'Lời bình chưa đọc (hoặc đã sửa chữ / đổi giọng) — vào tab Storyboard bấm “Đọc lời bình”.' : 'Giọng AI đọc liền một mạch; khi ghép, lời của mỗi cảnh bắt đầu cùng clip của cảnh đó (cắt ở chỗ ngừng thở giữa câu), không cắt chữ.'}
                </span>;
              })()}
              {p.audioMode === 'Original' && <span className="text-[11px] text-fg-subtle">Dùng chính tiếng của video gốc (giọng người khác + nhạc nền nếu có).</span>}
              {!p.hasAudio && <span className="text-[11px] text-fg-subtle">Video tham chiếu không có tiếng.</span>}
            </div>
          </div>
          <Button variant="primary" icon={<Clapperboard className="h-4 w-4" />} loading={p.finalStatus === 'Assembling'} disabled={!allDone}
            onClick={async () => { try { setProject(await aiStudio.assemble(p.id)); } catch (e) { toast.error('Chưa ghép được', (e as Error).message); } }}>
            {p.hasFinal ? 'Ghép lại' : 'Ghép video'}
          </Button>
        </div>
        {p.finalStatus === 'Failed' && p.finalError && <Alert className="mt-3" tone="danger" title="Ghép lỗi">{p.finalError}</Alert>}
        {p.hasFinal && p.finalStatus === 'Done' && (
          <div className="mt-3 flex flex-col gap-3 sm:flex-row sm:items-end">
            <video key={p.finalUpdatedAtUtc ?? ''} src={aiStudio.finalUrl(p.id, false, p.finalUpdatedAtUtc)} controls playsInline className="max-h-[480px] rounded-xl bg-black sm:w-72" />
            <div className="min-w-0 flex-1 space-y-2">
              <a href={aiStudio.finalUrl(p.id, true, p.finalUpdatedAtUtc)} className="inline-flex h-9 items-center gap-2 rounded-md bg-fg px-3.5 text-sm font-medium text-canvas hover:bg-fg/90"><Download className="h-4 w-4" />Tải video MP4</a>
              <p className="text-xs text-fg-muted">Ghép lúc {formatDateTime(p.finalUpdatedAtUtc)}.</p>
              <FacebookCaption project={p} setProject={setProject} />
            </div>
          </div>
        )}
      </div>

      {estimate && (
        <Modal open onClose={() => setEstimate(null)} title="Xác nhận chi phí Seedance"
          description="Tiền chỉ bị trừ khi Seedance nhận việc. Gửi lại cùng lần bấm không trả tiền hai lần."
          footer={<>
            <Button variant="ghost" onClick={() => setEstimate(null)}>Huỷ</Button>
            <Button variant="accent" loading={sending} disabled={!!estimate.blocker || estimate.totalSeconds === 0} icon={<Play className="h-4 w-4" />} onClick={go}>
              Tạo {estimate.lines.filter((l) => !l.skip).length} clip · {usd(estimate.totalUsd)}
            </Button>
          </>}>
          <div className="space-y-3 text-sm">
            <div className="max-h-56 overflow-y-auto rounded-lg border border-line">
              <table className="w-full text-xs">
                <tbody>
                  {estimate.lines.map((l) => (
                    <tr key={l.sceneId} className="border-b border-line last:border-0">
                      <td className="px-3 py-1.5 text-fg">Cảnh {l.order + 1}</td>
                      <td className="px-3 py-1.5 text-fg-muted">{l.seconds}s</td>
                      <td className="px-3 py-1.5 text-right">{l.skip ? <span className="text-warning">{l.skip}</span> : usd(l.costUsd)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="grid grid-cols-3 gap-2 text-center">
              <div className="rounded-lg bg-subtle p-2"><div className="text-[11px] text-fg-subtle">Tổng</div><div className="font-semibold text-fg">{usd(estimate.totalUsd)}</div></div>
              <div className="rounded-lg bg-subtle p-2"><div className="text-[11px] text-fg-subtle">Thời lượng</div><div className="font-semibold text-fg">{estimate.totalSeconds}s</div></div>
              <div className="rounded-lg bg-subtle p-2"><div className="text-[11px] text-fg-subtle">Còn lại tháng</div>
                <div className="font-semibold text-fg">{estimate.budget.monthlyBudgetUsd > 0 ? usd(estimate.budget.monthRemainingUsd, 2) : 'không giới hạn'}</div></div>
            </div>
            <p className="text-xs text-fg-muted">{model?.label ?? p.videoModel} · {p.resolution} · {p.genAspectRatio}{p.seed != null ? ` · seed ${p.seed}` : ''}{p.continuity ? ' · nối cảnh' : ''}. Ước tính theo giá Vercel; số thật ghi ở Lịch sử.</p>
            {estimate.blocker && <Alert tone="danger">{estimate.blocker}</Alert>}
          </div>
        </Modal>
      )}
    </div>
  );
}

function SceneOutput({ project: p, scene: s, checked, onCheck, onRetry, onChanged }: {
  project: Project; scene: Scene; checked: boolean; onCheck: (v: boolean) => void; onRetry: () => void; onChanged: () => Promise<void>;
}) {
  const toast = useToast();
  const g = s.latest;
  const act = async (f: () => Promise<unknown>) => {
    try { await f(); await onChanged(); } catch (e) { toast.error('Không thực hiện được', (e as Error).message); }
  };
  return (
    <div className={cn('overflow-hidden rounded-xl border bg-surface', checked ? 'border-accent/50' : 'border-line', !s.enabled && 'opacity-50')}>
      <div className="relative aspect-[9/16] max-h-80 w-full bg-black">
        {g?.status === 'Done' && g.hasClip ? (
          <video src={aiStudio.clipUrl(g.id)} controls playsInline preload="metadata" className="h-full w-full object-contain" />
        ) : (
          <>
            {s.hasThumb && <img src={aiStudio.sceneThumbUrl(s.id)} alt="" className="h-full w-full object-cover opacity-40" loading="lazy" />}
            <div className="absolute inset-0 grid place-items-center p-3 text-center text-xs text-white">
              {g && isGenActive(g.status) ? <div className="flex flex-col items-center gap-2"><Spinner className="h-6 w-6" />{g.status === 'Queued' ? 'Chờ gửi Seedance…' : 'Seedance đang tạo (1–3 phút)…'}</div>
                : g?.status === 'Failed' ? <span className="text-red-200">{g.errorMessage}</span>
                : <span className="opacity-80">Chưa có clip</span>}
            </div>
          </>
        )}
      </div>
      <div className="space-y-2 p-3">
        <div className="flex items-center gap-2">
          <input type="checkbox" checked={checked} disabled={!s.enabled || isGenActive(g?.status)} onChange={(e) => onCheck(e.target.checked)} />
          <span className="text-[13px] font-semibold text-fg">Cảnh {s.order + 1}</span>
          <span className="text-xs text-fg-muted">{s.targetSeconds}s · ~{usd(s.estimatedCostUsd)}</span>
          {g && <span className="ml-auto"><GenStatusBadge status={g.status} /></span>}
        </div>
        <div className="flex flex-wrap gap-1.5">
          {g?.status === 'Done' && g.hasClip && (
            <a href={aiStudio.clipUrl(g.id, true)} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-line-strong px-2.5 text-[13px] hover:bg-muted"><Download className="h-3.5 w-3.5" />Tải</a>
          )}
          {g?.status === 'Failed' && g.paid && !g.hasClip && (
            <Button size="sm" variant="secondary" icon={<RefreshCw className="h-3.5 w-3.5" />} onClick={() => act(() => aiStudio.recover(g.id))}>Lấy lại clip (miễn phí)</Button>
          )}
          {g?.status === 'Queued' && (
            <Button size="sm" variant="danger-soft" icon={<Square className="h-3.5 w-3.5" />} onClick={() => act(() => aiStudio.cancelGeneration(g.id))}>Huỷ</Button>
          )}
          {s.enabled && !isGenActive(g?.status) && (
            <Button size="sm" variant="ghost" icon={<Wand2 className="h-3.5 w-3.5" />} onClick={onRetry}>{g ? 'Tạo lại' : 'Tạo cảnh này'}</Button>
          )}
        </div>
        {g && g.prompt !== s.prompt && g.status === 'Done' && <p className="text-[11px] text-fg-subtle">Prompt đã sửa sau lần tạo này.</p>}
      </div>
    </div>
  );
}

// ───────────────────────── history ─────────────────────────

export function HistoryTable({ projectId, refreshKey }: { projectId?: string; refreshKey?: string }) {
  const [rows, setRows] = useState<Generation[] | null>(null);
  const [open, setOpen] = useState<string | null>(null);
  const { onKeyNeeded } = useAiStudio();
  useEffect(() => {
    let alive = true;
    const load = () => aiStudio.generations(projectId).then((r) => alive && setRows(r)).catch((e) => { if (e instanceof StudioError && e.needsKey) onKeyNeeded(); });
    load();
    const t = setInterval(load, 15000);
    return () => { alive = false; clearInterval(t); };
  }, [projectId, refreshKey, onKeyNeeded]);

  if (!rows) return <div className="p-5"><Spinner /></div>;
  if (rows.length === 0) return <EmptyState icon={<Clapperboard />} title="Chưa tạo video nào" description="Clip Seedance tạo ra sẽ nằm ở đây, kèm prompt và thông số đã dùng." />;
  const total = rows.reduce((a, r) => a + r.costUsd, 0);
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[760px] text-xs">
        <thead className="text-left text-fg-subtle">
          <tr className="border-b border-line">
            <th className="px-3 py-2 font-medium">Lúc</th>
            {!projectId && <th className="px-3 py-2 font-medium">Dự án</th>}
            <th className="px-3 py-2 font-medium">Cảnh</th>
            <th className="px-3 py-2 font-medium">Model · thông số</th>
            <th className="px-3 py-2 font-medium">Trạng thái</th>
            <th className="px-3 py-2 text-right font-medium">Chi phí</th>
            <th className="px-3 py-2 font-medium" />
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <Fragment key={r.id}>
              <tr className="border-b border-line hover:bg-subtle">
                <td className="px-3 py-2 text-fg-muted" title={formatDateTime(r.createdAtUtc)}>{timeAgo(r.createdAtUtc)}</td>
                {!projectId && <td className="max-w-[180px] truncate px-3 py-2"><a className="text-accent hover:underline" href={`#/ai-studio/p/${r.projectId}`}>{r.projectTitle ?? '—'}</a></td>}
                <td className="px-3 py-2 text-fg">{r.sceneOrder != null ? `Cảnh ${r.sceneOrder + 1}` : '—'}</td>
                <td className="px-3 py-2 text-fg-muted">{r.model.replace('bytedance/', '')} · {r.resolution} · {r.aspectRatio} · {r.seconds}s{r.seed != null ? ` · seed ${r.seed}` : ''}{r.imageSource ? ' · ảnh đầu' : ''}</td>
                <td className="px-3 py-2"><GenStatusBadge status={r.status} /></td>
                <td className="px-3 py-2 text-right text-fg">{r.status === 'Done' || r.costUsd > 0 ? usd(r.costUsd) : <span className="text-fg-subtle">~{usd(r.estimatedCostUsd)}</span>}</td>
                <td className="px-3 py-2 text-right">
                  <span className="inline-flex gap-1">
                    <Button size="sm" variant="ghost" onClick={() => setOpen(open === r.id ? null : r.id)}>{open === r.id ? 'Đóng' : 'Xem'}</Button>
                    {r.hasClip && <a href={aiStudio.clipUrl(r.id, true)} className="inline-flex h-8 items-center rounded-md px-2 text-fg-muted hover:bg-muted hover:text-fg" title="Tải"><Download className="h-4 w-4" /></a>}
                  </span>
                </td>
              </tr>
              {open === r.id && (
                <tr className="border-b border-line bg-subtle">
                  <td colSpan={projectId ? 6 : 7} className="px-3 py-3">
                    <div className="flex flex-col gap-3 md:flex-row">
                      {r.hasClip ? <video src={aiStudio.clipUrl(r.id)} controls playsInline className="max-h-72 rounded-lg bg-black md:w-48" />
                        : r.filesDeleted ? <div className="grid h-24 w-48 place-items-center rounded-lg bg-muted text-center text-[11px] text-fg-muted">Clip đã hết hạn lưu</div> : null}
                      <div className="min-w-0 flex-1 space-y-1.5">
                        <div className="font-medium text-fg">Prompt đã dùng</div>
                        <p className="whitespace-pre-wrap font-mono text-[11px] text-fg-muted">{r.prompt}</p>
                        {r.errorMessage && <p className="text-danger">{r.errorMessage}</p>}
                        <p className="text-fg-subtle">Bắt đầu {formatDateTime(r.startedAtUtc)} · xong {formatDateTime(r.finishedAtUtc)} · {r.attempts} lần chạy{r.paid ? ' · đã gửi Seedance' : ''}</p>
                      </div>
                    </div>
                  </td>
                </tr>
              )}
            </Fragment>
          ))}
        </tbody>
      </table>
      <div className="flex justify-end px-3 py-2 text-xs text-fg-muted">Tổng chi trong danh sách: <b className="ml-1 text-fg">{usd(total)}</b></div>
    </div>
  );
}

/** Caption for posting the joined video on Facebook: written by AI from the story, editable, copied with the AI note. */
function FacebookCaption({ project: p, setProject }: { project: Project; setProject: (p: Project) => void }) {
  const toast = useToast();
  const [text, setText] = useState(p.caption);
  const [busy, setBusy] = useState<'write' | 'save' | null>(null);
  const tried = useRef(false);
  useEffect(() => setText(p.caption), [p.caption]);
  const dirty = text.trim() !== p.caption.trim();

  const write = async () => {
    setBusy('write');
    try { setProject(await aiStudio.writeCaption(p.id)); }
    catch (e) { toast.error('Chưa viết được chú thích', (e as Error).message); }
    finally { setBusy(null); }
  };
  // the first time the joined video is there: a caption is written once (a fraction of a cent)
  useEffect(() => {
    if (!p.caption && !tried.current) { tried.current = true; void write(); }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [p.id]);

  const save = async () => {
    setBusy('save');
    try { setProject(await aiStudio.setCaption(p.id, text)); toast.success('Đã lưu chú thích'); }
    catch (e) { toast.error('Chưa lưu được', (e as Error).message); }
    finally { setBusy(null); }
  };
  const copy = async () => {
    const body = text.trim();
    const post = body.includes('tạo bằng AI') ? body : `${body}\n\n🤖 Video được tạo bằng AI.`;
    try {
      await navigator.clipboard.writeText(post);
      toast.success('Đã copy chú thích', 'Dán vào ô mô tả khi đăng Facebook.');
    } catch {
      toast.error('Trình duyệt không cho copy tự động', 'Bôi đen chú thích rồi copy bằng tay.');
    }
  };

  return (
    <div className="rounded-lg border border-line bg-canvas p-3">
      <div className="mb-1.5 text-[13px] font-semibold text-fg">Đăng Facebook</div>
      <Textarea rows={5} value={text} placeholder={busy === 'write' ? 'Đang viết chú thích…' : 'Chú thích khi đăng (bấm “Viết bằng AI” hoặc tự gõ).'}
        onChange={(e) => setText(e.target.value)} disabled={busy === 'write'} />
      <div className="mt-2 flex flex-wrap items-center gap-2">
        <Button size="sm" variant="primary" icon={<Copy className="h-3.5 w-3.5" />} disabled={!text.trim() || busy != null} onClick={copy}>Copy chú thích</Button>
        <Button size="sm" variant="secondary" icon={<Save className="h-3.5 w-3.5" />} loading={busy === 'save'} disabled={!dirty || busy != null} onClick={save}>Lưu</Button>
        <Button size="sm" variant="ghost" icon={<Wand2 className="h-3.5 w-3.5" />} loading={busy === 'write'} disabled={busy != null} onClick={write}>{p.caption ? 'Viết lại bằng AI' : 'Viết bằng AI'}</Button>
      </div>
      <p className="mt-1.5 text-[11px] text-fg-subtle">
        Khi copy, dòng “🤖 Video được tạo bằng AI.” được thêm ở cuối. Lúc đăng nhớ bật nhãn <b>“Thông tin AI”</b> (Meta yêu cầu với video AI trông như thật).
      </p>
    </div>
  );
}
