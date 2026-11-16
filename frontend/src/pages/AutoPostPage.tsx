import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { AlertTriangle, CalendarClock, Link2, Power, RotateCcw, Save, Send } from 'lucide-react';
import {
  api,
  AutoPostOverviewDto,
  AutoPostPageDto,
  AutoPostRegionMode,
  AutoPostRunDto,
  AutoPostSettingsDto,
  NewsTopicDto,
} from '../services/api';
import {
  Alert,
  Badge,
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  EmptyState,
  Field,
  Input,
  PageHeader,
  Segmented,
  Select,
  Skeleton,
  cn,
} from '../components/ui';
import { useConfirm, useToast } from '../components/feedback';
import { DailyDigestCard } from '../components/DailyDigestCard';
import { PageInsightsCard } from '../components/PageInsightsCard';
import { AutoPostPageCard, AutoPostTotals, PageAvatar, RegionBadge, RunDetails, StateBadge, isActiveRun } from '../components/AutoPostWidgets';
import { href, navigate, useRoute } from '../lib/router';
import { errorText } from '../lib/format';
import { DURATIONS, VOICES, durationLabel } from '../lib/prefs';

const REGION_OPTIONS: { value: AutoPostRegionMode; label: string }[] = [
  { value: 'Mixed', label: 'Xen kẽ VN + quốc tế' },
  { value: 'VN', label: 'Chỉ Việt Nam' },
  { value: 'World', label: 'Chỉ quốc tế' },
];

const LEAD_OPTIONS = [20, 30, 40, 60, 90];

const formatDay = (isoDate: string) => isoDate.split('-').reverse().join('/');

export const AutoPostPage: React.FC = () => {
  const toast = useToast();
  const confirm = useConfirm();
  const route = useRoute();
  const selectedId = route.query.get('page');
  const [data, setData] = useState<AutoPostOverviewDto | null>(null);
  const [topics, setTopics] = useState<NewsTopicDto[]>([]);

  const load = useCallback(
    async (silent = false) => {
      try {
        setData(await api.getAutoPost());
      } catch (err) {
        if (!silent) toast.error('Không tải được lịch tự động đăng', errorText(err, ''));
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    []
  );

  useEffect(() => {
    load();
    api.getNewsTopics().then(setTopics).catch(() => setTopics([]));
  }, [load]);

  // Follow progress: poll quickly while something is running, slowly otherwise
  const busy = useMemo(() => !!data && data.recentRuns.some(isActiveRun), [data]);
  useEffect(() => {
    const t = setInterval(() => load(true), busy ? 8000 : 30000);
    return () => clearInterval(t);
  }, [busy, load]);

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

  if (!data) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-10 w-64" />
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  const selected = data.pages.find((p) => p.facebookAccountId === selectedId) ?? null;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Tự động đăng"
        description="Mỗi Page một chủ đề và một lịch riêng. App tự chọn tin hot nhất đúng chủ đề (không trùng tin giữa các Page), làm video rồi đăng bằng token riêng của Page đó."
        actions={
          <ButtonLink href={href('/settings/facebook')} icon={<Link2 className="h-4 w-4" />}>
            <span className="hidden sm:inline">Kết nối Page mới</span>
            <span className="sm:hidden">Thêm Page</span>
          </ButtonLink>
        }
      />

      {data.warnings.map((w) => (
        <Alert key={w} tone="warning" icon={<AlertTriangle className="h-4 w-4" />}>
          {w}
        </Alert>
      ))}

      <AutoPostTotals totals={data.totals} />

      {data.pages.length > 0 && (
        <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
          <div className="flex w-max gap-1.5 pb-1">
            <PageTab label="Tất cả Page" active={!selected} onClick={() => navigate('/auto-post')} />
            {data.pages.map((p) => (
              <PageTab
                key={p.facebookAccountId}
                label={p.pageName}
                sub={p.topicName}
                enabled={p.settings.enabled}
                active={selected?.facebookAccountId === p.facebookAccountId}
                onClick={() => navigate(`/auto-post?page=${p.facebookAccountId}`)}
              />
            ))}
          </div>
        </div>
      )}

      {data.pages.length === 0 ? (
        <Card>
          <EmptyState
            icon={<Link2 />}
            title="Chưa có Page nào"
            description="Kết nối từng Page Facebook (mỗi Page một token riêng) rồi quay lại đây để chọn chủ đề và lịch đăng."
            action={
              <ButtonLink variant="primary" href={href('/settings/facebook')}>
                Kết nối Page
              </ButtonLink>
            }
          />
        </Card>
      ) : selected ? (
        <PageDetail
          key={selected.facebookAccountId}
          page={selected}
          index={data.pages.indexOf(selected)}
          topics={topics}
          today={data.today}
          history={data.recentRuns.filter((r) => r.facebookAccountId === selected.facebookAccountId && (r.slotDate !== data.today))}
          onChanged={() => load(true)}
          onCancel={cancelRun}
        />
      ) : (
        <AllPages data={data} onCancel={cancelRun} />
      )}
    </div>
  );
};

function PageTab({
  label,
  sub,
  enabled,
  active,
  onClick,
}: {
  label: string;
  sub?: string;
  enabled?: boolean;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex max-w-[220px] items-center gap-2 rounded-md border px-3 py-1.5 text-left transition-colors',
        active ? 'border-accent bg-accent/[0.06] text-fg' : 'border-line bg-surface text-fg-muted hover:text-fg'
      )}
    >
      {enabled !== undefined && <span className={cn('h-2 w-2 shrink-0 rounded-full', enabled ? 'bg-success' : 'bg-fg-subtle/50')} />}
      <span className="min-w-0">
        <span className="block truncate text-sm font-medium">{label}</span>
        {sub && <span className="block truncate text-[11px] text-fg-subtle">{sub}</span>}
      </span>
    </button>
  );
}

/* ───────────────────────── All pages ───────────────────────── */

function AllPages({ data, onCancel }: { data: AutoPostOverviewDto; onCancel: (run: AutoPostRunDto) => void }) {
  // Today's slots of every page in one timeline
  const timeline = data.pages
    .flatMap((p) => p.todaySlots.map((slot) => ({ page: p, slot })))
    .sort((a, b) => a.slot.scheduledAtUtc.localeCompare(b.slot.scheduledAtUtc));
  const history = data.recentRuns.filter((r) => r.slotDate !== data.today);

  return (
    <>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {data.pages.map((p) => (
          <AutoPostPageCard key={p.facebookAccountId} page={p} onOpen={() => navigate(`/auto-post?page=${p.facebookAccountId}`)} />
        ))}
      </div>

      <Card>
        <CardHeader
          title={`Lịch hôm nay · ${formatDay(data.today)}`}
          description={`Tất cả Page, giờ Việt Nam. Mỗi lần làm ${data.maxConcurrentVideos} video, Page nào đến giờ trước làm trước.`}
        />
        <CardBody className="pt-3">
          {timeline.length === 0 ? (
            <EmptyState icon={<CalendarClock />} title="Hôm nay chưa có lượt nào" />
          ) : (
            <ol className="divide-y divide-line">
              {timeline.map(({ page, slot }) => (
                <li key={`${page.facebookAccountId}-${slot.time}-${slot.run?.id ?? ''}`} className="flex flex-col gap-2 py-3 sm:flex-row sm:items-start sm:gap-4">
                  <div className="flex w-44 shrink-0 items-center gap-2">
                    <span className="font-mono text-base font-semibold text-fg">{slot.time}</span>
                    <a href={href(`/auto-post?page=${page.facebookAccountId}`)} className="min-w-0 truncate text-xs text-fg-muted hover:text-fg hover:underline">
                      {page.pageName}
                    </a>
                  </div>
                  {slot.run ? (
                    <RunDetails run={slot.run} onCancel={onCancel} />
                  ) : (
                    <div className="flex flex-wrap items-center gap-2 text-[13px] text-fg-muted">
                      <StateBadge state={slot.state} />
                      <RegionBadge region={slot.plannedRegion} />
                      <Badge tone="neutral">{page.topicName}</Badge>
                      {!page.settings.enabled && slot.state === 'Upcoming' && <span>Page đang tắt tự động đăng</span>}
                    </div>
                  )}
                </li>
              ))}
            </ol>
          )}
        </CardBody>
      </Card>

      <HistoryCard runs={history} showPage onCancel={onCancel} />
    </>
  );
}

function HistoryCard({ runs, showPage, onCancel }: { runs: AutoPostRunDto[]; showPage?: boolean; onCancel: (run: AutoPostRunDto) => void }) {
  return (
    <Card>
      <CardHeader title="Lịch sử 7 ngày" description="Các lượt đã chạy trước hôm nay." />
      <CardBody className="pt-3">
        {runs.length === 0 ? (
          <p className="py-6 text-center text-[13px] text-fg-muted">Chưa có lượt nào.</p>
        ) : (
          <ul className="divide-y divide-line">
            {runs.map((run) => (
              <li key={run.id} className="flex flex-col gap-1.5 py-3 sm:flex-row sm:items-start sm:gap-4">
                <div className="w-36 shrink-0 text-xs text-fg-muted">
                  <span className="font-mono text-sm text-fg">{run.slotTime}</span> · {formatDay(run.slotDate).slice(0, 5)}
                </div>
                <RunDetails run={run} showPage={showPage} onCancel={onCancel} />
              </li>
            ))}
          </ul>
        )}
      </CardBody>
    </Card>
  );
}

/* ───────────────────────── One page ───────────────────────── */

function PageDetail({
  page,
  index,
  topics,
  today,
  history,
  onChanged,
  onCancel,
}: {
  page: AutoPostPageDto;
  index: number;
  topics: NewsTopicDto[];
  today: string;
  history: AutoPostRunDto[];
  onChanged: () => Promise<void> | void;
  onCancel: (run: AutoPostRunDto) => void;
}) {
  const toast = useToast();
  const confirm = useConfirm();
  const [form, setForm] = useState<AutoPostSettingsDto>(page.settings);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [runningNow, setRunningNow] = useState(false);

  // Keep the form in sync with the server while the user hasn't edited it
  useEffect(() => {
    if (!dirty) setForm(page.settings);
  }, [page.settings, dirty]);

  const update = (patch: Partial<AutoPostSettingsDto>) => {
    setForm((f) => ({ ...f, ...patch }));
    setDirty(true);
  };

  const suggest = async (raw: number) => {
    const count = Math.max(1, Math.min(12, Math.round(raw) || 1));
    update({ postsPerDay: count });
    try {
      // Shift each page's times a little so videos of different pages aren't made at the same moment
      update({ postsPerDay: count, timeSlots: await api.suggestAutoPostSlots(count, index * 10) });
    } catch {
      /* the server suggests times on save if the count doesn't match */
    }
  };

  const save = async (override?: Partial<AutoPostSettingsDto>) => {
    setSaving(true);
    try {
      const saved = await api.saveAutoPostSettings(page.facebookAccountId, { ...form, ...override });
      setForm(saved);
      setDirty(false);
      toast.success(
        override?.enabled === true ? `Đã bật tự động đăng cho ${page.pageName}` : override?.enabled === false ? 'Đã tắt tự động đăng' : 'Đã lưu cài đặt'
      );
      await onChanged();
    } catch (err) {
      toast.error('Lưu thất bại', errorText(err, ''));
    } finally {
      setSaving(false);
    }
  };

  const toggle = async () => {
    if (!form.enabled) {
      const topicName = topics.find((t) => t.key === form.topic)?.name ?? page.topicName;
      const ok = await confirm({
        title: `Bật tự động đăng cho ${page.pageName}?`,
        description:
          `Mỗi ngày ${form.postsPerDay} video chủ đề "${topicName}" vào ${form.timeSlots.join(', ')} (giờ VN), đăng thẳng lên Page bằng token của Page, không cần duyệt tay. ` +
          'Video có lỗi nghiêm trọng vẫn bị chặn đăng. Mỗi video tốn phí AI như khi tạo tay.',
        confirmLabel: 'Bật tự động đăng',
      });
      if (!ok) return;
    }
    await save({ enabled: !form.enabled });
  };

  const runNow = async () => {
    const ok = await confirm({
      title: `Đăng 1 tin lên ${page.pageName} ngay?`,
      description: 'App chọn tin hot nhất đúng chủ đề chưa làm video, tạo video và đăng ngay khi xong (khoảng 5–15 phút). Lượt này không tính vào lịch.',
      confirmLabel: 'Đăng ngay',
    });
    if (!ok) return;
    setRunningNow(true);
    try {
      const run = await api.autoPostRunNow(page.facebookAccountId);
      if (run.status === 'Failed') toast.error('Không bắt đầu được', run.message ?? '');
      else toast.success('Đã bắt đầu', run.articleTitle ?? undefined);
      await onChanged();
    } catch (err) {
      toast.error('Không bắt đầu được', errorText(err, ''));
    } finally {
      setRunningNow(false);
    }
  };

  const nextSlot = page.todaySlots.find((s) => s.state === 'Upcoming');
  const topicMeta = topics.find((t) => t.key === form.topic);

  return (
    <>
      {page.warnings.map((w) => (
        <Alert key={w} tone="warning" icon={<AlertTriangle className="h-4 w-4" />}>
          {w}
        </Alert>
      ))}

      <Card>
        <CardBody className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex min-w-0 items-start gap-3">
            <PageAvatar name={page.pageName} enabled={form.enabled} className="h-10 w-10" />
            <div className="min-w-0">
              <p className="truncate text-[15px] font-semibold text-fg">{page.pageName}</p>
              <p className="mt-0.5 text-[13px] text-fg-muted">
                {page.topicName} · Hôm nay đã đăng <b className="text-fg">{page.publishedToday}</b>/{form.postsPerDay}
                {form.enabled && nextSlot && (
                  <>
                    {' '}· Lượt tiếp theo <b className="text-fg">{nextSlot.time}</b>
                  </>
                )}
                {!form.enabled && ' · Đang tắt tự động đăng'}
              </p>
            </div>
          </div>
          <div className="flex flex-wrap gap-2 sm:shrink-0">
            <Button icon={<Send className="h-4 w-4" />} loading={runningNow} onClick={runNow}>
              Đăng 1 tin ngay
            </Button>
            <Button variant={form.enabled ? 'secondary' : 'primary'} icon={<Power className="h-4 w-4" />} loading={saving} onClick={toggle}>
              {form.enabled ? 'Tắt tự động đăng' : 'Bật tự động đăng'}
            </Button>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader title={`Hôm nay · ${formatDay(today)}`} description="Giờ Việt Nam. Trang tự cập nhật tiến trình." />
        <CardBody className="pt-3">
          {page.todaySlots.length === 0 ? (
            <EmptyState icon={<CalendarClock />} title="Chưa có khung giờ nào" />
          ) : (
            <ol className="divide-y divide-line">
              {page.todaySlots.map((slot) => (
                <li key={`${slot.time}-${slot.run?.id ?? ''}`} className="flex flex-col gap-2 py-3 sm:flex-row sm:items-start sm:gap-4">
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
                        (form.enabled ? 'App sẽ chọn tin hot nhất đúng chủ đề lúc gần đến giờ.' : 'Tự động đăng đang tắt nên lượt này sẽ không chạy.')}
                      {slot.state === 'Missed' && 'Không có lượt đăng ở khung giờ này.'}
                    </div>
                  )}
                </li>
              ))}
            </ol>
          )}
        </CardBody>
      </Card>

      <DailyDigestCard page={page} settings={page.settings} />

      <PageInsightsCard
        pageId={page.facebookAccountId}
        autoTune={page.settings.autoTune}
        currentSlots={page.settings.timeSlots}
        onApplySlots={(slots) => save({ timeSlots: slots, postsPerDay: slots.length })}
      />

      <Card>
        <CardHeader title="Cài đặt Page" description="Chủ đề, số video, khung giờ và giọng đọc riêng cho Page này. Token kết nối Page sửa ở Cài đặt → Facebook." />
        <CardBody className="space-y-5">
          <div className="grid gap-5 sm:grid-cols-2">
            <Field label="Chủ đề của Page" hint={topicMeta?.description}>
              <Select value={form.topic} onChange={(e) => update({ topic: e.target.value })}>
                {topics.length === 0 && <option value={form.topic}>{page.topicName}</option>}
                {topics.map((t) => (
                  <option key={t.key} value={t.key}>
                    {t.name}
                  </option>
                ))}
              </Select>
            </Field>
            <Field
              label={form.topic === 'tu-chon' ? 'Từ khoá (bắt buộc)' : 'Từ khoá thêm'}
              hint={
                form.topic === 'tu-chon'
                  ? 'Chỉ lấy tin có chứa một trong các từ này. Cách nhau bằng dấu phẩy.'
                  : 'Không bắt buộc. Tin chứa các từ này cũng được tính vào chủ đề. Cách nhau bằng dấu phẩy.'
              }
            >
              <Input
                value={form.topicKeywords ?? ''}
                onChange={(e) => update({ topicKeywords: e.target.value })}
                placeholder="vd: bóng đá Anh, Premier League"
              />
            </Field>
          </div>

          <div className="grid gap-5 sm:grid-cols-2">
            <Field label="Số video tối đa mỗi ngày" hint="Từ 1 đến 12. Mặc định 5.">
              <input
                type="number"
                min={1}
                max={12}
                value={form.postsPerDay}
                onChange={(e) => suggest(Number(e.target.value))}
                className="h-9 w-28 rounded-md border border-line-strong bg-surface px-3 text-sm text-fg focus:border-accent focus:ring-[3px] focus:ring-accent/15"
              />
            </Field>
            <Field label="Lấy tin từ">
              <Segmented size="sm" options={REGION_OPTIONS} value={form.regionMode} onChange={(v) => update({ regionMode: v })} className="flex-wrap" />
            </Field>
          </div>

          <Field
            label="Khung giờ đăng (giờ Việt Nam)"
            hint="Gợi ý theo giờ đông người xem: sáng đi làm, nghỉ trưa, tan tầm, giờ vàng tối, trước khi ngủ. Mỗi Page lệch nhau vài phút để video không phải làm cùng lúc."
          >
            <div className="flex flex-wrap items-center gap-2">
              {form.timeSlots.map((t, i) => (
                <div key={i} className="flex items-center gap-1.5 rounded-md border border-line bg-subtle px-2 py-1">
                  <span className="text-xs text-fg-subtle">#{i + 1}</span>
                  <input
                    type="time"
                    value={t}
                    onChange={(e) => {
                      const next = [...form.timeSlots];
                      next[i] = e.target.value;
                      update({ timeSlots: next });
                    }}
                    className="h-7 rounded border-0 bg-transparent px-1 font-mono text-sm text-fg focus:ring-0"
                  />
                  {form.regionMode === 'Mixed' && <span className="text-[11px] text-fg-subtle">{i % 2 === 0 ? 'VN' : 'QT'}</span>}
                </div>
              ))}
              <Button size="sm" variant="ghost" icon={<RotateCcw className="h-3.5 w-3.5" />} onClick={() => suggest(form.postsPerDay)}>
                Gợi ý lại
              </Button>
            </div>
          </Field>

          <div className="grid gap-5 sm:grid-cols-3">
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
          </div>

          <div className="space-y-3 border-t border-line pt-5">
            <Checkbox
              checked={form.digestEnabled}
              onChange={(v) => update({ digestEnabled: v })}
              label="Bản tin tối (Điểm tin nóng)"
              description="Mỗi ngày 1 lần: chọn các tin giật gân nhất trong ngày về chủ đề của Page, mỗi tin 1 ảnh có tóm tắt ngắn in trên ảnh, đăng 1 bài viết nhiều ảnh + 1 Story."
            />
            {form.digestEnabled && (
              <div className="grid gap-5 pl-6 sm:grid-cols-2">
                <Field label="Giờ đăng bản tin" hint="Giờ Việt Nam. Gợi ý 20:00–21:00, lúc đông người lướt Facebook.">
                  <input
                    type="time"
                    value={form.digestTime}
                    onChange={(e) => update({ digestTime: e.target.value })}
                    className="h-9 w-32 rounded-md border border-line-strong bg-surface px-3 font-mono text-sm text-fg focus:border-accent focus:ring-[3px] focus:ring-accent/15"
                  />
                </Field>
                <Field label="Số tin trong bản tin" hint="Story dài khoảng 4 + 6 giây mỗi tin (tối đa 60 giây).">
                  <Select value={form.digestItems} onChange={(e) => update({ digestItems: Number(e.target.value) })}>
                    {[3, 4, 5, 6, 7, 8, 9].map((n) => (
                      <option key={n} value={n}>
                        {n} tin
                      </option>
                    ))}
                  </Select>
                </Field>
                <div className="sm:col-span-2">
                  <Checkbox
                    checked={form.weeklyDigestEnabled}
                    onChange={(v) => update({ weeklyDigestEnabled: v })}
                    label="Chủ nhật: bản tin tuần “Tuần qua có gì”"
                    description="Tối Chủ nhật thay bản tin ngày bằng những tin lớn nhất của cả tuần."
                  />
                </div>
              </div>
            )}
          </div>

          <div className="space-y-3 border-t border-line pt-5">
            <Checkbox
              checked={form.breakingEnabled}
              onChange={(v) => update({ breakingEnabled: v })}
              label="Tin nóng đăng ngay"
              description="Khi một tin đúng chủ đề bùng lên (từ 6 báo cùng đưa trong 3 giờ, hoặc lên Google Trends), app làm video và đăng ngay, không chờ khung giờ. Chỉ 6:00–23:00, cách nhau ít nhất 2 giờ."
            />
            {form.breakingEnabled && (
              <div className="pl-6">
                <Field label="Tối đa mỗi ngày">
                  <Select value={form.breakingPerDay} onChange={(e) => update({ breakingPerDay: Number(e.target.value) })} className="w-32">
                    {[1, 2, 3, 4, 5].map((n) => (
                      <option key={n} value={n}>
                        {n} tin
                      </option>
                    ))}
                  </Select>
                </Field>
              </div>
            )}
            <Checkbox
              checked={form.autoTune}
              onChange={(v) => update({ autoTune: v })}
              label="Tự học từ lượt xem"
              description="Khi chọn tin, ưu tiên loại tin và khu vực (Việt Nam / quốc tế) mà người xem Page này xem nhiều nhất. Chỉ áp dụng khi đã có đủ số liệu."
            />
          </div>

          <div className="flex flex-wrap items-center justify-end gap-2 border-t border-line pt-4">
            {dirty && <span className="mr-auto text-xs text-warning">Có thay đổi chưa lưu</span>}
            {dirty && (
              <Button
                variant="ghost"
                onClick={() => {
                  setForm(page.settings);
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

      <HistoryCard runs={history} onCancel={onCancel} />
    </>
  );
}
