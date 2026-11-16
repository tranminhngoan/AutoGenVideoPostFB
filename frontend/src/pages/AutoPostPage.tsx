import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle,
  CalendarClock,
  CheckCircle2,
  ExternalLink,
  Film,
  Globe2,
  Newspaper,
  Power,
  RotateCcw,
  Save,
  Send,
  X,
} from 'lucide-react';
import {
  api,
  AutoPostOverviewDto,
  AutoPostRegionMode,
  AutoPostRunDto,
  AutoPostSettingsDto,
  AutoPostSlotDto,
  FacebookAccountDto,
} from '../services/api';
import { Alert, Badge, Button, ButtonLink, Card, CardBody, CardHeader, EmptyState, Field, PageHeader, ProgressBar, Segmented, Select, Skeleton, cn } from '../components/ui';
import { useConfirm, useToast } from '../components/feedback';
import { href } from '../lib/router';
import { errorText, formatDateTime, Tone } from '../lib/format';
import { DURATIONS, VOICES, durationLabel } from '../lib/prefs';

const STATE_META: Record<string, { label: string; tone: Tone; pulse?: boolean }> = {
  Upcoming: { label: 'Sắp tới', tone: 'neutral' },
  Missed: { label: 'Đã qua', tone: 'neutral' },
  Producing: { label: 'Đang làm video', tone: 'info', pulse: true },
  WaitingSlot: { label: 'Chờ đến giờ đăng', tone: 'info' },
  Publishing: { label: 'Đang đăng', tone: 'info', pulse: true },
  Published: { label: 'Đã đăng', tone: 'success' },
  Failed: { label: 'Lỗi', tone: 'danger' },
  Skipped: { label: 'Đã huỷ', tone: 'neutral' },
};

const REGION_OPTIONS: { value: AutoPostRegionMode; label: string }[] = [
  { value: 'Mixed', label: 'Xen kẽ VN + quốc tế' },
  { value: 'VN', label: 'Chỉ Việt Nam' },
  { value: 'World', label: 'Chỉ quốc tế' },
];

const LEAD_OPTIONS = [20, 30, 40, 60, 90];

function RegionBadge({ region }: { region?: string | null }) {
  if (!region) return null;
  return region === 'World' ? (
    <Badge tone="info">
      <Globe2 className="h-3 w-3" /> Quốc tế
    </Badge>
  ) : (
    <Badge tone="warning">
      <Newspaper className="h-3 w-3" /> Việt Nam
    </Badge>
  );
}

function StateBadge({ state }: { state: string }) {
  const meta = STATE_META[state] ?? { label: state, tone: 'neutral' as Tone };
  return (
    <Badge tone={meta.tone} dot pulse={meta.pulse}>
      {meta.label}
    </Badge>
  );
}

const isActiveRun = (run?: AutoPostRunDto | null) => !!run && ['Producing', 'WaitingSlot', 'Publishing'].includes(run.status);

export const AutoPostPage: React.FC = () => {
  const toast = useToast();
  const confirm = useConfirm();
  const [data, setData] = useState<AutoPostOverviewDto | null>(null);
  const [form, setForm] = useState<AutoPostSettingsDto | null>(null);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [runningNow, setRunningNow] = useState(false);
  const [pages, setPages] = useState<FacebookAccountDto[]>([]);

  const load = useCallback(
    async (silent = false) => {
      try {
        const overview = await api.getAutoPost();
        setData(overview);
        setForm((current) => (current && dirty ? current : overview.settings));
      } catch (err) {
        if (!silent) toast.error('Không tải được lịch tự động đăng', errorText(err, ''));
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [dirty]
  );

  useEffect(() => {
    load();
    api.getFacebookPages().then(setPages).catch(() => setPages([]));
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // Follow progress: poll quickly while something is running, slowly otherwise
  const busy = useMemo(
    () => !!data && (data.todaySlots.some((s) => isActiveRun(s.run)) || data.recentRuns.some(isActiveRun)),
    [data]
  );
  useEffect(() => {
    const t = setInterval(() => load(true), busy ? 8000 : 30000);
    return () => clearInterval(t);
  }, [busy, load]);

  const update = (patch: Partial<AutoPostSettingsDto>) => {
    setForm((f) => (f ? { ...f, ...patch } : f));
    setDirty(true);
  };

  const changeCount = async (raw: number) => {
    const count = Math.max(1, Math.min(12, Math.round(raw) || 1));
    update({ postsPerDay: count });
    try {
      update({ postsPerDay: count, timeSlots: await api.suggestAutoPostSlots(count) });
    } catch {
      /* keep the current times; the server suggests new ones on save if the count doesn't match */
    }
  };

  const setSlot = (index: number, value: string) => {
    if (!form) return;
    const next = [...form.timeSlots];
    next[index] = value;
    update({ timeSlots: next });
  };

  const save = async (override?: Partial<AutoPostSettingsDto>) => {
    if (!form) return;
    setSaving(true);
    try {
      const saved = await api.saveAutoPostSettings({ ...form, ...override });
      setForm(saved);
      setDirty(false);
      toast.success(
        override?.enabled === true ? 'Đã bật tự động đăng' : override?.enabled === false ? 'Đã tắt tự động đăng' : 'Đã lưu cài đặt'
      );
      await load(true);
    } catch (err) {
      toast.error('Lưu thất bại', errorText(err, ''));
    } finally {
      setSaving(false);
    }
  };

  const toggle = async () => {
    if (!form) return;
    if (!form.enabled) {
      const ok = await confirm({
        title: `Bật tự động đăng ${form.postsPerDay} video mỗi ngày?`,
        description:
          `Vào các giờ ${form.timeSlots.join(', ')} (giờ Việt Nam), app sẽ tự chọn tin hot nhất chưa làm, tạo video và đăng thẳng lên Page Facebook, không cần duyệt tay. ` +
          'Video có lỗi nghiêm trọng (kiểm tra chất lượng) vẫn bị chặn đăng. Mỗi video tốn phí AI như khi bạn tạo tay.',
        confirmLabel: 'Bật tự động đăng',
      });
      if (!ok) return;
    }
    await save({ enabled: !form.enabled });
  };

  const runNow = async () => {
    const ok = await confirm({
      title: 'Đăng 1 tin ngay bây giờ?',
      description: 'App chọn tin hot nhất chưa làm video, tạo video và đăng lên Facebook ngay khi xong (khoảng 5–15 phút). Lượt này không tính vào lịch.',
      confirmLabel: 'Đăng ngay',
    });
    if (!ok) return;
    setRunningNow(true);
    try {
      const run = await api.autoPostRunNow();
      if (run.status === 'Failed') toast.error('Không bắt đầu được', run.message ?? '');
      else toast.success('Đã bắt đầu', run.articleTitle ?? undefined);
      await load(true);
    } catch (err) {
      toast.error('Không bắt đầu được', errorText(err, ''));
    } finally {
      setRunningNow(false);
    }
  };

  const cancelRun = async (run: AutoPostRunDto) => {
    const ok = await confirm({
      title: 'Huỷ lượt đăng này?',
      description: 'Video (nếu đã làm) không bị xoá, vẫn nằm ở trang Video để bạn duyệt tay.',
      confirmLabel: 'Huỷ lượt',
      danger: true,
    });
    if (!ok) return;
    try {
      await api.cancelAutoPostRun(run.id);
      toast.success('Đã huỷ lượt đăng');
      await load(true);
    } catch (err) {
      toast.error('Không huỷ được', errorText(err, ''));
    }
  };

  if (!data || !form) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-10 w-64" />
        <Skeleton className="h-40 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  const nextSlot = data.todaySlots.find((s) => s.state === 'Upcoming');
  const history = data.recentRuns.filter((r) => r.slotDate !== data.today || r.isManual);

  return (
    <div className="space-y-5">
      <PageHeader
        title="Tự động đăng"
        description="Mỗi ngày tự chọn tin hot nhất (Việt Nam và quốc tế, không trùng tin), làm video rồi đăng lên Page Facebook theo khung giờ."
        actions={
          <Button icon={<Send className="h-4 w-4" />} loading={runningNow} onClick={runNow}>
            <span className="hidden sm:inline">Đăng 1 tin ngay</span>
            <span className="sm:hidden">Đăng ngay</span>
          </Button>
        }
      />

      {data.warnings.map((w) => (
        <Alert key={w} tone="warning" icon={<AlertTriangle className="h-4 w-4" />}>
          {w}
        </Alert>
      ))}

      {/* On / off + summary */}
      <Card>
        <CardBody className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-start gap-3">
            <span
              className={cn(
                'mt-0.5 flex h-10 w-10 shrink-0 items-center justify-center rounded-full',
                form.enabled ? 'bg-success/10 text-success' : 'bg-muted text-fg-subtle'
              )}
            >
              <Power className="h-5 w-5" />
            </span>
            <div>
              <p className="text-[15px] font-semibold text-fg">
                {form.enabled ? 'Đang bật tự động đăng' : 'Tự động đăng đang tắt'}
              </p>
              <p className="mt-0.5 text-[13px] text-fg-muted">
                Hôm nay đã đăng <b className="text-fg">{data.publishedToday}</b>/{form.postsPerDay} video
                {form.enabled && nextSlot && (
                  <>
                    {' '}· Lượt tiếp theo <b className="text-fg">{nextSlot.time}</b> ({nextSlot.plannedRegion === 'World' ? 'tin quốc tế' : 'tin Việt Nam'}),
                    bắt đầu làm video trước {form.leadMinutes} phút
                  </>
                )}
                {form.enabled && !nextSlot && ' · Hết lượt hôm nay, mai chạy tiếp'}
              </p>
            </div>
          </div>
          <Button
            variant={form.enabled ? 'secondary' : 'primary'}
            icon={<Power className="h-4 w-4" />}
            loading={saving}
            onClick={toggle}
            className="sm:shrink-0"
          >
            {form.enabled ? 'Tắt tự động đăng' : 'Bật tự động đăng'}
          </Button>
        </CardBody>
      </Card>

      {/* Today */}
      <Card>
        <CardHeader title={`Hôm nay · ${data.today.split('-').reverse().join('/')}`} description="Giờ Việt Nam. Trang tự cập nhật tiến trình." />
        <CardBody className="pt-3">
          {data.todaySlots.length === 0 ? (
            <EmptyState icon={<CalendarClock />} title="Chưa có khung giờ nào" />
          ) : (
            <ol className="divide-y divide-line">
              {data.todaySlots.map((slot) => (
                <SlotRow key={slot.time} slot={slot} enabled={form.enabled} onCancel={cancelRun} />
              ))}
            </ol>
          )}
        </CardBody>
      </Card>

      {/* Settings */}
      <Card>
        <CardHeader
          title="Cài đặt lịch đăng"
          description="Đổi số video mỗi ngày thì khung giờ được gợi ý lại theo giờ đông người xem Facebook ở Việt Nam. Bạn vẫn sửa từng giờ được."
        />
        <CardBody className="space-y-5">
          <div className="grid gap-5 sm:grid-cols-2">
            <Field label="Số video tối đa mỗi ngày" hint="Từ 1 đến 12. Mặc định 5.">
              <input
                type="number"
                min={1}
                max={12}
                value={form.postsPerDay}
                onChange={(e) => changeCount(Number(e.target.value))}
                className="h-9 w-28 rounded-md border border-line-strong bg-surface px-3 text-sm text-fg focus:border-accent focus:ring-[3px] focus:ring-accent/15"
              />
            </Field>
            <Field label="Lấy tin từ">
              <Segmented size="sm" options={REGION_OPTIONS} value={form.regionMode} onChange={(v) => update({ regionMode: v })} className="flex-wrap" />
            </Field>
          </div>

          <Field
            label="Khung giờ đăng (giờ Việt Nam)"
            hint="Mặc định: 07:00 ăn sáng / đi làm · 11:30 nghỉ trưa · 17:30 tan tầm · 20:00 giờ vàng buổi tối · 21:30 trước giờ ngủ."
          >
            <div className="flex flex-wrap items-center gap-2">
              {form.timeSlots.map((t, i) => (
                <div key={i} className="flex items-center gap-1.5 rounded-md border border-line bg-subtle px-2 py-1">
                  <span className="text-xs text-fg-subtle">#{i + 1}</span>
                  <input
                    type="time"
                    value={t}
                    onChange={(e) => setSlot(i, e.target.value)}
                    className="h-7 rounded border-0 bg-transparent px-1 font-mono text-sm text-fg focus:ring-0"
                  />
                  {form.regionMode === 'Mixed' && (
                    <span className="text-[11px] text-fg-subtle">{i % 2 === 0 ? 'VN' : 'QT'}</span>
                  )}
                </div>
              ))}
              <Button size="sm" variant="ghost" icon={<RotateCcw className="h-3.5 w-3.5" />} onClick={() => changeCount(form.postsPerDay)}>
                Gợi ý lại
              </Button>
            </div>
          </Field>

          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
            <Field label="Giọng đọc">
              <Select value={form.voice ?? ''} onChange={(e) => update({ voice: e.target.value || null })}>
                <option value="">Mặc định của máy chủ</option>
                <optgroup label="Giọng tự nhiên (Gemini, tốn phí)">
                  {VOICES.filter((v) => v.group === 'natural').map((v) => (
                    <option key={v.id} value={v.id}>
                      {v.name}: {v.desc}
                    </option>
                  ))}
                </optgroup>
                <optgroup label="Giọng miễn phí (Edge)">
                  {VOICES.filter((v) => v.group === 'basic').map((v) => (
                    <option key={v.id} value={v.id}>
                      {v.name}: {v.desc}
                    </option>
                  ))}
                </optgroup>
              </Select>
            </Field>
            <Field label="Độ dài tối đa">
              <Select value={form.targetDurationSeconds} onChange={(e) => update({ targetDurationSeconds: Number(e.target.value) })}>
                {DURATIONS.map((d) => (
                  <option key={d} value={d}>
                    {durationLabel(d)}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Bắt đầu làm video trước" hint="Để video kịp xong đúng giờ đăng.">
              <Select value={form.leadMinutes} onChange={(e) => update({ leadMinutes: Number(e.target.value) })}>
                {LEAD_OPTIONS.map((m) => (
                  <option key={m} value={m}>
                    {m} phút
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Đăng lên Page">
              <Select value={form.facebookAccountId ?? ''} onChange={(e) => update({ facebookAccountId: e.target.value || null })}>
                <option value="">{pages.length ? `Page đầu tiên (${pages[0].pageName})` : 'Chưa kết nối Page'}</option>
                {pages.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.pageName}
                  </option>
                ))}
              </Select>
            </Field>
          </div>

          <div className="flex flex-wrap items-center justify-end gap-2 border-t border-line pt-4">
            {dirty && <span className="mr-auto text-xs text-warning">Có thay đổi chưa lưu</span>}
            {dirty && (
              <Button
                variant="ghost"
                onClick={() => {
                  setForm(data.settings);
                  setDirty(false);
                }}
              >
                Bỏ thay đổi
              </Button>
            )}
            <Button variant="primary" icon={<Save className="h-4 w-4" />} loading={saving} disabled={!dirty} onClick={() => save()}>
              Lưu cài đặt
            </Button>
          </div>
        </CardBody>
      </Card>

      {/* History */}
      <Card>
        <CardHeader title="Lịch sử 7 ngày" description="Các lượt đã chạy, kể cả lượt bấm “Đăng ngay”." />
        <CardBody className="pt-3">
          {history.length === 0 ? (
            <p className="py-6 text-center text-[13px] text-fg-muted">Chưa có lượt nào.</p>
          ) : (
            <ul className="divide-y divide-line">
              {history.map((run) => (
                <li key={run.id} className="flex flex-col gap-1.5 py-3 sm:flex-row sm:items-start sm:gap-4">
                  <div className="w-36 shrink-0 text-xs text-fg-muted">
                    <span className="font-mono text-sm text-fg">{run.slotTime}</span> · {run.slotDate.split('-').reverse().slice(0, 2).join('/')}
                    {run.isManual && <span className="ml-1 text-fg-subtle">(đăng ngay)</span>}
                  </div>
                  <RunDetails run={run} onCancel={cancelRun} />
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>
    </div>
  );
};

function SlotRow({ slot, enabled, onCancel }: { slot: AutoPostSlotDto; enabled: boolean; onCancel: (run: AutoPostRunDto) => void }) {
  return (
    <li className="flex flex-col gap-2 py-3 sm:flex-row sm:items-start sm:gap-4">
      <div className="flex w-36 shrink-0 items-center gap-2 sm:flex-col sm:items-start sm:gap-1">
        <span className="font-mono text-lg font-semibold leading-none text-fg">{slot.time}</span>
        <RegionBadge region={slot.plannedRegion} />
      </div>
      {slot.run ? (
        <RunDetails run={slot.run} onCancel={onCancel} />
      ) : (
        <div className="flex min-w-0 flex-1 flex-wrap items-center gap-2 text-[13px] text-fg-muted">
          <StateBadge state={slot.state} />
          {slot.state === 'Upcoming' &&
            (enabled ? 'App sẽ chọn tin hot nhất lúc gần đến giờ.' : 'Tự động đăng đang tắt nên lượt này sẽ không chạy.')}
          {slot.state === 'Missed' && 'Không có lượt đăng ở khung giờ này.'}
        </div>
      )}
    </li>
  );
}

function RunDetails({ run, onCancel }: { run: AutoPostRunDto; onCancel: (run: AutoPostRunDto) => void }) {
  const producing = run.status === 'Producing';
  return (
    <div className="min-w-0 flex-1 space-y-1.5">
      <div className="flex flex-wrap items-center gap-2">
        <StateBadge state={run.status} />
        {run.region && <RegionBadge region={run.region} />}
        {run.hotScore != null && <span className="text-xs text-fg-subtle">Độ hot {Math.round(run.hotScore)}</span>}
        {run.attempts > 1 && <span className="text-xs text-fg-subtle">Lần thử {run.attempts}</span>}
      </div>
      {run.articleTitle && (
        <p className="text-sm font-medium leading-snug text-fg">
          {run.articleUrl ? (
            <a href={run.articleUrl} target="_blank" rel="noreferrer" className="hover:underline">
              {run.articleTitle}
            </a>
          ) : (
            run.articleTitle
          )}
          {run.sourceName && <span className="ml-1.5 font-normal text-fg-muted">· {run.sourceName}</span>}
        </p>
      )}
      {producing && run.progressPercentage != null && <ProgressBar value={run.progressPercentage} className="max-w-sm" />}
      {run.message && (
        <p className={cn('text-[13px]', run.status === 'Failed' ? 'text-danger' : 'text-fg-muted')}>{run.message}</p>
      )}
      {run.publishedAtUtc && run.status === 'Published' && (
        <p className="flex items-center gap-1 text-xs text-fg-subtle">
          <CheckCircle2 className="h-3.5 w-3.5 text-success" /> Đăng lúc {formatDateTime(run.publishedAtUtc)}
        </p>
      )}
      <div className="flex flex-wrap gap-2 pt-0.5">
        {run.videoProjectId && (
          <ButtonLink size="sm" variant="ghost" href={href(`/videos/${run.videoProjectId}`)} icon={<Film className="h-3.5 w-3.5" />}>
            Xem video
          </ButtonLink>
        )}
        {run.facebookPostUrl && (
          <ButtonLink size="sm" variant="ghost" href={run.facebookPostUrl} target="_blank" rel="noreferrer" icon={<ExternalLink className="h-3.5 w-3.5" />}>
            Xem trên Facebook
          </ButtonLink>
        )}
        {(run.status === 'Producing' || run.status === 'WaitingSlot') && (
          <Button size="sm" variant="danger-soft" icon={<X className="h-3.5 w-3.5" />} onClick={() => onCancel(run)}>
            Huỷ lượt
          </Button>
        )}
      </div>
    </div>
  );
}
