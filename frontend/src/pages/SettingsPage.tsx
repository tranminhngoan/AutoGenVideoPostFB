import React, { useEffect, useState } from 'react';
import { CheckCircle2, AlertTriangle, Trash2, KeyRound, RefreshCw, ShieldCheck, Plus, Wrench } from 'lucide-react';
import { api, AiModelsDto, FacebookAccountDto, SystemMetricsDto, UserTokenPagesResultDto } from '../services/api';
import { SystemConfig } from '../types';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Divider,
  EmptyState,
  Field,
  Input,
  PageHeader,
  ProgressBar,
  RadioCard,
  Segmented,
  cn,
} from '../components/ui';
import { useConfirm, useToast } from '../components/feedback';
import { href } from '../lib/router';
import { errorText, formatDateTime } from '../lib/format';
import { AI_PROVIDERS, DURATIONS, SPEEDS, StudioPrefs, durationLabel, getPrefs, savePrefs } from '../lib/prefs';
import { VoicePicker } from '../components/VoicePicker';

type Section = 'defaults' | 'facebook' | 'system';

const SECTIONS: { id: Section; label: string; desc: string }[] = [
  { id: 'defaults', label: 'Mặc định sản xuất', desc: 'AI, giọng đọc, thời lượng' },
  { id: 'facebook', label: 'Facebook', desc: 'Trang dùng để đăng Reels' },
  { id: 'system', label: 'Hệ thống', desc: 'Máy chủ, bộ nhớ, bảo trì' },
];

interface SettingsPageProps {
  section?: string;
  config: SystemConfig | null;
  apiHealthy: boolean | null;
}

export const SettingsPage: React.FC<SettingsPageProps> = ({ section, config, apiHealthy }) => {
  const active: Section = SECTIONS.some((s) => s.id === section) ? (section as Section) : 'defaults';

  return (
    <div>
      <PageHeader title="Cài đặt" />
      <div className="grid gap-8 lg:grid-cols-[200px_minmax(0,1fr)]">
        <nav className="scrollbar-none -mx-4 flex gap-1 overflow-x-auto px-4 lg:mx-0 lg:flex-col lg:px-0">
          {SECTIONS.map((s) => (
            <a
              key={s.id}
              href={href(`/settings/${s.id}`)}
              className={cn(
                'shrink-0 rounded-md px-3 py-2 text-sm transition-colors',
                active === s.id ? 'bg-muted font-medium text-fg' : 'text-fg-muted hover:bg-muted/70 hover:text-fg'
              )}
            >
              {s.label}
              <span className="mt-0.5 hidden text-xs font-normal text-fg-subtle lg:block">{s.desc}</span>
            </a>
          ))}
        </nav>
        <div className="min-w-0 max-w-2xl">
          {active === 'defaults' && <DefaultsSection config={config} />}
          {active === 'facebook' && <FacebookSection />}
          {active === 'system' && <SystemSection config={config} apiHealthy={apiHealthy} />}
        </div>
      </div>
    </div>
  );
};

/* ───────────────────────── Defaults ───────────────────────── */

/** Live check of the configured Gemini model against the models the API key can really use. */
function GeminiModelStatus() {
  const [data, setData] = useState<AiModelsDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      setData(await api.getAiModels());
    } catch (err) {
      setError(errorText(err, 'Không lấy được danh sách model'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  const textModels = (data?.models ?? []).filter(
    (m) => !/(tts|image|lyria|banana|embedding|aqa|deep-research)/i.test(m.id)
  );

  return (
    <div className="rounded-md border border-line bg-subtle px-3 py-2.5 text-xs">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-fg-muted">Model Gemini đang dùng:</span>
        <code className="font-mono text-fg">{data?.current ?? '…'}</code>
        {data && (
          <Badge tone={data.currentAvailable ? 'success' : 'danger'}>
            {data.currentAvailable ? 'Khả dụng' : 'Không có trong danh sách'}
          </Badge>
        )}
        <button type="button" onClick={load} className="ml-auto text-fg-subtle hover:text-fg" disabled={loading}>
          <RefreshCw className={cn('h-3.5 w-3.5', loading && 'animate-spin')} />
        </button>
      </div>
      {error && <p className="mt-1.5 text-danger">{error}</p>}
      {textModels.length > 0 && (
        <details className="mt-2">
          <summary className="cursor-pointer select-none text-fg-muted hover:text-fg">
            {textModels.length} model viết văn bản mà key này dùng được
          </summary>
          <div className="mt-2 flex flex-wrap gap-1">
            {textModels.map((m) => (
              <code
                key={m.id}
                title={m.displayName}
                className={cn(
                  'rounded border px-1.5 py-0.5 font-mono',
                  m.id === data?.current ? 'border-success/40 bg-success/10 text-success' : 'border-line bg-surface text-fg-muted'
                )}
              >
                {m.id}
              </code>
            ))}
          </div>
          <p className="mt-2 text-fg-subtle">
            Đổi model bằng lệnh <code className="font-mono text-fg">dotnet user-secrets set "Ai:Gemini:Model" "tên-model"</code> trong thư mục
            NewsStudio.Api, rồi khởi động lại API và Worker. Model cũ có thể vẫn nằm trong danh sách nhưng bị Google khoá với key mới.
          </p>
        </details>
      )}
    </div>
  );
}

function DefaultsSection({ config }: { config: SystemConfig | null }) {
  const toast = useToast();
  const [prefs, setPrefs] = useState<StudioPrefs>(getPrefs);
  const [dirty, setDirty] = useState(false);

  const set = <K extends keyof StudioPrefs>(k: K, v: StudioPrefs[K]) => {
    setPrefs((p) => ({ ...p, [k]: v }));
    setDirty(true);
  };

  const save = () => {
    savePrefs(prefs);
    setDirty(false);
    toast.success('Đã lưu thiết lập mặc định', 'Áp dụng cho các video tạo mới.');
  };

  const selectedKeyMissing = config?.ai[prefs.aiProvider === 'Claude' ? 'claude' : 'gemini']?.isConfigured === false;

  return (
    <div className="space-y-6">
      {selectedKeyMissing && (
        <Alert tone="warning" icon={<KeyRound />} title={`Máy chủ chưa có API key cho ${prefs.aiProvider}`}>
          Video mới sẽ báo lỗi ở bước viết kịch bản cho đến khi có key. Đặt biến môi trường{' '}
          <code className="font-mono text-fg">{prefs.aiProvider === 'Claude' ? 'ANTHROPIC_API_KEY' : 'GEMINI_API_KEY'}</code> rồi khởi động lại máy chủ.
        </Alert>
      )}
      <Card>
        <CardHeader title="AI viết kịch bản" description="Dùng khi tạo video mới và khi viết lại kịch bản." />
        <CardBody className="space-y-4">
          <div className="grid gap-2 sm:grid-cols-2" role="radiogroup">
            {AI_PROVIDERS.map((p) => (
              <RadioCard
                key={p.id}
                checked={prefs.aiProvider === p.id}
                onClick={() => set('aiProvider', p.id)}
                title={p.name}
                description={p.desc}
              />
            ))}
          </div>
          {config && (
            <div className="space-y-1.5 text-xs text-fg-muted">
              {(['gemini', 'claude'] as const).map((key) => {
                const info = config.ai[key];
                if (!info) return null;
                return (
                  <div key={key} className="flex flex-wrap items-center gap-2">
                    <span className="w-14 font-medium capitalize text-fg">{key}</span>
                    <span>{info.model}</span>
                    <Badge tone={info.isConfigured ? 'success' : 'warning'}>
                      {info.isConfigured ? 'Đã có API key' : 'Chưa có API key'}
                    </Badge>
                  </div>
                );
              })}
            </div>
          )}
          <p className="flex gap-2 text-xs text-fg-subtle">
            <KeyRound className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            API key được cấu hình bằng biến môi trường trên máy chủ, không nhập ở giao diện.
          </p>
          {config?.ai.gemini && (
            <div className="space-y-1.5 rounded-lg border border-line p-3 text-xs text-fg-muted">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium text-fg">Key Gemini miễn phí</span>
                <Badge tone={config.ai.gemini.hasFreeKey ? 'success' : 'neutral'}>
                  {config.ai.gemini.hasFreeKey ? 'Đã có · dùng trước' : 'Chưa có'}
                </Badge>
                <span className="font-medium text-fg">Key trả phí</span>
                <Badge tone={config.ai.gemini.hasPaidKey ? 'success' : 'neutral'}>
                  {config.ai.gemini.hasPaidKey ? 'Đã có · dùng khi hết lượt free' : 'Chưa có'}
                </Badge>
              </div>
              <p>
                Mỗi lần viết kịch bản, đọc giọng hay tìm ảnh, app dùng key miễn phí trước. Khi Google báo hết lượt free,
                app tự chuyển sang key trả phí. Key miễn phí lấy từ một project <b>không gắn billing</b>, đặt vào biến{' '}
                <code className="font-mono text-fg">Ai__Gemini__FreeApiKey</code>.
              </p>
            </div>
          )}
          {config?.ai.gemini?.isConfigured && <GeminiModelStatus />}
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Giọng đọc" />
        <CardBody className="space-y-4">
          <VoicePicker value={prefs.voice} onChange={(id) => set('voice', id)} />
          <div className="flex flex-wrap items-center justify-between gap-3 pt-1">
            <div>
              <div className="text-[13px] font-medium text-fg">Tốc độ đọc</div>
              <div className="text-xs text-fg-subtle">Giọng tự nhiên nghe hay nhất ở tốc độ chuẩn.</div>
            </div>
            <Segmented value={prefs.speed} onChange={(v) => set('speed', v)} options={SPEEDS.map((s) => ({ value: s.id, label: s.label }))} />
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Video và bài đăng" />
        <CardBody className="space-y-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <div className="text-[13px] font-medium text-fg">Thời lượng tối đa mặc định</div>
              <div className="text-xs text-fg-subtle">Video dài theo nội dung bài, không vượt quá mức này.</div>
            </div>
            <Segmented
              value={prefs.targetDuration}
              onChange={(v) => set('targetDuration', v)}
              options={DURATIONS.map((d) => ({ value: d, label: durationLabel(d) }))}
            />
          </div>
          <Field label="Hashtag cuối bài đăng" hint="Tự động thêm vào nội dung khi đăng lên Facebook. Có thể sửa trước khi đăng.">
            <Input value={prefs.hashtags} onChange={(e) => set('hashtags', e.target.value)} placeholder="#TinTuc #Reels" />
          </Field>
        </CardBody>
      </Card>

      <div className="sticky bottom-0 -mx-1 flex items-center justify-end gap-3 border-t border-line bg-canvas/90 px-1 py-3 backdrop-blur">
        {dirty && <span className="text-[13px] text-fg-muted">Có thay đổi chưa lưu</span>}
        <Button variant="primary" onClick={save} disabled={!dirty}>
          Lưu thay đổi
        </Button>
      </div>
    </div>
  );
}

/* ───────────────────────── Facebook ───────────────────────── */

function FacebookSection() {
  const toast = useToast();
  const confirm = useConfirm();
  const [pages, setPages] = useState<FacebookAccountDto[] | null>(null);
  const [pageId, setPageId] = useState('');
  const [pageName, setPageName] = useState('');
  const [token, setToken] = useState('');
  const [verifying, setVerifying] = useState(false);
  const [saving, setSaving] = useState(false);
  const [verifyResult, setVerifyResult] = useState<{ ok: boolean; message: string } | null>(null);

  const load = async () => setPages(await api.getFacebookPages());

  useEffect(() => {
    // Read the linked Instagram accounts again (falls back to the stored list)
    api.refreshInstagram().then(setPages).catch(() => load());
  }, []);

  const verify = async () => {
    if (!token.trim()) return;
    setVerifying(true);
    setVerifyResult(null);
    try {
      const r = await api.verifyFacebookToken(token.trim());
      if (r.isValid) {
        setVerifyResult({ ok: true, message: `Token hợp lệ cho trang ${r.pageName || 'Facebook'}${r.pageId ? ` (${r.pageId})` : ''}.` });
        if (r.pageId && !pageId) setPageId(r.pageId);
        if (r.pageName && !pageName) setPageName(r.pageName);
      } else {
        setVerifyResult({ ok: false, message: r.errorMessage || 'Token không hợp lệ hoặc đã hết hạn.' });
      }
    } catch (err) {
      setVerifyResult({ ok: false, message: errorText(err, 'Không kết nối được tới Facebook.') });
    } finally {
      setVerifying(false);
    }
  };

  const connect = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!pageId.trim() || !token.trim()) return;
    setSaving(true);
    try {
      await api.connectFacebookPage({
        pageId: pageId.trim(),
        pageName: pageName.trim() || 'Facebook Page',
        pageAccessToken: token.trim(),
      });
      toast.success('Đã kết nối trang Facebook');
      setPageId('');
      setPageName('');
      setToken('');
      setVerifyResult(null);
      await load();
    } catch (err) {
      toast.error('Không kết nối được trang', errorText(err, ''));
    } finally {
      setSaving(false);
    }
  };

  const disconnect = async (p: FacebookAccountDto) => {
    const ok = await confirm({
      title: `Ngắt kết nối “${p.pageName}”?`,
      description: 'Bạn sẽ không đăng được video lên trang này cho tới khi kết nối lại.',
      confirmLabel: 'Ngắt kết nối',
      danger: true,
    });
    if (!ok) return;
    const done = await api.disconnectFacebookPage(p.id);
    if (done) {
      toast.success(`Đã ngắt kết nối ${p.pageName}`);
      await load();
    } else {
      toast.error('Không ngắt kết nối được');
    }
  };

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader
          title="Trang đã kết nối"
          description="Kết nối được nhiều Page, mỗi Page một token riêng. Chủ đề và lịch đăng của từng Page chỉnh ở mục Tự động đăng."
          actions={
            <Button size="sm" variant="ghost" icon={<RefreshCw className="h-3.5 w-3.5" />} onClick={load}>
              Làm mới
            </Button>
          }
        />
        <CardBody className="pt-3">
          {!pages ? (
            <div className="h-12 animate-pulse rounded-md bg-muted" />
          ) : pages.length === 0 ? (
            <EmptyState title="Chưa kết nối trang nào" description="Thêm trang Facebook ở bên dưới để đăng video." className="py-8" />
          ) : (
            <ul className="-mx-5 divide-y divide-line border-y border-line">
              {pages.map((p) => (
                <li key={p.id} className="flex items-center justify-between gap-3 px-5 py-3">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="truncate text-sm font-medium text-fg">{p.pageName}</span>
                      <Badge tone={p.isActive ? 'success' : 'neutral'} dot>
                        {p.isActive ? 'Hoạt động' : 'Tạm ngắt'}
                      </Badge>
                    </div>
                    <div className="mt-0.5 truncate text-xs text-fg-muted">
                      ID {p.pageId} · Token <span className="font-mono">{p.maskedToken || '••••'}</span>
                      {' · '}
                      {p.instagramUsername ? `Instagram @${p.instagramUsername}` : 'Chưa liên kết Instagram'}
                      {p.tokenExpiresAtUtc && ` · Hết hạn ${formatDateTime(p.tokenExpiresAtUtc)}`}
                    </div>
                  </div>
                  <div className="flex shrink-0 items-center gap-1">
                    <a
                      href={href(`/auto-post?page=${p.id}`)}
                      className="inline-flex h-8 items-center rounded-md px-2.5 text-[13px] text-fg-muted hover:bg-muted hover:text-fg"
                    >
                      Chủ đề &amp; lịch
                    </a>
                    <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} onClick={() => disconnect(p)}>
                      Ngắt
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>

      <QuickConnectCard onConnected={load} />

      <Card>
        <CardHeader title="Kết nối thủ công từng Page" description="Cách cũ: dán Page Access Token của từng Page. Nên dùng Kết nối nhanh ở trên để token không hết hạn." />
        <form onSubmit={connect}>
          <CardBody className="space-y-4">
            <Field
              label="Page Access Token"
              htmlFor="fb-token"
              hint="Token được mã hoá trước khi lưu và không bao giờ hiển thị lại đầy đủ."
            >
              <div className="flex gap-2">
                <div className="flex-1">
                  <Input
                    id="fb-token"
                    type="password"
                    autoComplete="off"
                    value={token}
                    onChange={(e) => {
                      setToken(e.target.value);
                      setVerifyResult(null);
                    }}
                    placeholder="EAAB…"
                    className="font-mono"
                  />
                </div>
                <Button onClick={verify} loading={verifying} disabled={!token.trim()} icon={<ShieldCheck className="h-4 w-4" />}>
                  Kiểm tra
                </Button>
              </div>
            </Field>

            {verifyResult && (
              <Alert tone={verifyResult.ok ? 'success' : 'danger'} icon={verifyResult.ok ? <CheckCircle2 /> : <AlertTriangle />}>
                {verifyResult.message}
              </Alert>
            )}

            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Page ID" htmlFor="fb-id">
                <Input id="fb-id" value={pageId} onChange={(e) => setPageId(e.target.value)} placeholder="100200300400500" className="font-mono" />
              </Field>
              <Field label="Tên trang" htmlFor="fb-name" optional>
                <Input id="fb-name" value={pageName} onChange={(e) => setPageName(e.target.value)} placeholder="Tự điền khi kiểm tra token" />
              </Field>
            </div>

            <details className="group text-[13px]">
              <summary className="cursor-pointer select-none text-fg-muted hover:text-fg">Token cần những quyền nào?</summary>
              <ul className="mt-2 space-y-1.5 pl-1 text-xs text-fg-muted">
                <li><code className="font-mono text-fg">pages_show_list</code> — xem danh sách trang bạn quản lý</li>
                <li><code className="font-mono text-fg">pages_manage_posts</code> — đăng video Reels lên trang</li>
                <li><code className="font-mono text-fg">pages_read_engagement</code> — đọc lượt xem, tương tác</li>
                <li><code className="font-mono text-fg">read_insights</code> — thống kê hiệu quả video</li>
              </ul>
            </details>
          </CardBody>
          <div className="flex justify-end border-t border-line px-5 py-3.5">
            <Button type="submit" variant="primary" loading={saving} disabled={!pageId.trim() || !token.trim()} icon={<Plus className="h-4 w-4" />}>
              Kết nối trang
            </Button>
          </div>
        </form>
      </Card>
    </div>
  );
}

/**
 * Kết nối nhanh: the user pastes ONE extended user token; the app lists every page they manage with its own
 * page token (which then never expires) and connects the ticked ones.
 */
function QuickConnectCard({ onConnected }: { onConnected: () => Promise<void> | void }) {
  const toast = useToast();
  const [token, setToken] = useState('');
  const [loading, setLoading] = useState(false);
  const [connecting, setConnecting] = useState(false);
  const [result, setResult] = useState<UserTokenPagesResultDto | null>(null);
  const [selected, setSelected] = useState<string[]>([]);

  const readPages = async () => {
    setLoading(true);
    setResult(null);
    try {
      const r = await api.connectFromUserToken(token.trim());
      setResult(r);
      // Pages already in the app are ticked: their old (expiring) tokens get replaced
      setSelected(r.pages.filter((p) => p.alreadyConnected).map((p) => p.pageId));
    } catch (err) {
      toast.error('Không đọc được mã', errorText(err, ''));
    } finally {
      setLoading(false);
    }
  };

  const connect = async () => {
    setConnecting(true);
    try {
      const r = await api.connectFromUserToken(token.trim(), selected);
      setResult(r);
      const n = r.pages.filter((p) => p.connected).length;
      toast.success(`Đã kết nối ${n} Page`, r.longLived ? 'Token của các Page này không hết hạn.' : undefined);
      setToken('');
      await onConnected();
      api.syncPageStats().catch(() => undefined); // refresh view counts with the new tokens
    } catch (err) {
      toast.error('Kết nối thất bại', errorText(err, ''));
    } finally {
      setConnecting(false);
    }
  };

  const toggle = (id: string) => setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));

  return (
    <Card>
      <CardHeader title="Kết nối nhanh tất cả Page" description="Dán 1 mã duy nhất, app tự lấy token riêng cho từng Page. Token Page sẽ không hết hạn." />
      <CardBody className="space-y-4">
        <ol className="list-decimal space-y-1.5 pl-5 text-[13px] text-fg-muted">
          <li>
            Mở{' '}
            <a href="https://developers.facebook.com/tools/explorer" target="_blank" rel="noreferrer" className="text-accent hover:underline">
              Graph API Explorer
            </a>
            . Ô <b className="text-fg">Người dùng hoặc Trang</b> chọn <b className="text-fg">Quay lại mã người dùng</b>. Bấm{' '}
            <b className="text-fg">Generate Access Token</b>, tick tất cả Page.
          </li>
          <li>
            Bấm nút <b className="text-fg">ⓘ</b> cạnh mã → <b className="text-fg">Mở trong Trình gỡ lỗi</b> → kéo xuống cuối bấm{' '}
            <b className="text-fg">Gia hạn mã truy cập</b> → copy mã mới.
          </li>
          <li>Dán mã đó vào ô dưới, bấm Lấy danh sách Page.</li>
        </ol>

        <div className="flex gap-2">
          <div className="flex-1">
            <Input
              type="password"
              autoComplete="off"
              value={token}
              onChange={(e) => {
                setToken(e.target.value);
                setResult(null);
              }}
              placeholder="Dán mã người dùng đã gia hạn (EAA…)"
              className="font-mono"
            />
          </div>
          <Button onClick={readPages} loading={loading} disabled={!token.trim()} icon={<KeyRound className="h-4 w-4" />}>
            Lấy danh sách Page
          </Button>
        </div>

        {result && (
          <>
            {!result.longLived && (
              <Alert tone="warning" icon={<AlertTriangle />}>
                Mã này <b>chưa được gia hạn</b>
                {result.userTokenExpiresAtUtc ? ` (hết hạn lúc ${formatDateTime(result.userTokenExpiresAtUtc)})` : ''}, nên token Page cũng sẽ hết hạn
                theo. Làm lại bước 2 (Gia hạn mã truy cập) rồi dán mã mới.
              </Alert>
            )}
            {result.missingPermissions.length > 0 && (
              <Alert tone="warning" icon={<AlertTriangle />}>
                Mã thiếu quyền <span className="font-mono">{result.missingPermissions.join(', ')}</span>. Thêm quyền ở ô Quyền trong Graph API Explorer
                rồi làm lại từ bước 1.
              </Alert>
            )}
            <ul className="-mx-5 divide-y divide-line border-y border-line">
              {result.pages.map((p) => (
                <li key={p.pageId}>
                  <label className="flex cursor-pointer items-center gap-3 px-5 py-2.5">
                    <input
                      type="checkbox"
                      checked={selected.includes(p.pageId)}
                      onChange={() => toggle(p.pageId)}
                      className="h-4 w-4 shrink-0 cursor-pointer rounded border-line-strong accent-[rgb(var(--accent))]"
                    />
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm text-fg">{p.pageName}</span>
                      <span className="block text-xs text-fg-muted">
                        {p.connected ? 'Vừa kết nối' : p.alreadyConnected ? 'Đang có trong app, sẽ thay token mới' : 'Chưa có trong app'}
                        {p.instagramUsername ? ` · Instagram @${p.instagramUsername}` : ''}
                      </span>
                    </span>
                    {p.neverExpires ? (
                      <Badge tone="success">Không hết hạn</Badge>
                    ) : (
                      <Badge tone="warning">Hết hạn {p.expiresAtUtc ? formatDateTime(p.expiresAtUtc) : ''}</Badge>
                    )}
                  </label>
                </li>
              ))}
            </ul>
          </>
        )}
      </CardBody>
      {result && (
        <div className="flex items-center justify-end gap-3 border-t border-line px-5 py-3.5">
          <span className="text-xs text-fg-muted">Chỉ các Page được tick mới kết nối vào app.</span>
          <Button variant="primary" loading={connecting} disabled={selected.length === 0} icon={<Plus className="h-4 w-4" />} onClick={connect}>
            Kết nối {selected.length} Page
          </Button>
        </div>
      )}
    </Card>
  );
}

/* ───────────────────────── System ───────────────────────── */

function SystemSection({ config, apiHealthy }: { config: SystemConfig | null; apiHealthy: boolean | null }) {
  const toast = useToast();
  const [metrics, setMetrics] = useState<SystemMetricsDto | null>(null);
  const [busy, setBusy] = useState<'cleanup' | 'recover' | null>(null);
  const [lastReport, setLastReport] = useState<string[] | null>(null);

  const load = async () => setMetrics(await api.getSystemMetrics().catch(() => null));
  useEffect(() => {
    load();
  }, []);

  const cleanup = async () => {
    setBusy('cleanup');
    try {
      const r = await api.triggerStorageCleanup();
      toast.success(
        `Đã dọn ${r.expiredAssetsCleanedCount} file hết hạn`,
        `Giải phóng ${r.totalBytesFreedMb} MB. Đang dùng ${r.currentStorageUsedMb}/${r.storageLimitMb} MB.`
      );
      await load();
    } catch (err) {
      toast.error('Dọn dẹp thất bại', errorText(err, ''));
    } finally {
      setBusy(null);
    }
  };

  const recover = async () => {
    setBusy('recover');
    try {
      const r = await api.triggerJobRecovery();
      toast.success(
        `Đã rà soát ${r.totalDanglingProjectsExamined} video dở dang`,
        `${r.recoveredToAwaitingApprovalCount} chuyển về chờ duyệt, ${r.resetToPendingCount} đặt lại hàng đợi, ${r.markedFailedCount} đánh dấu lỗi.`
      );
      setLastReport(r.actionLogs?.length ? r.actionLogs : null);
      await load();
    } catch (err) {
      toast.error('Rà soát thất bại', errorText(err, ''));
    } finally {
      setBusy(null);
    }
  };

  const rows: [string, React.ReactNode][] = [
    ['Trạng thái', apiHealthy ? 'Hoạt động' : apiHealthy === null ? 'Đang kiểm tra' : 'Mất kết nối'],
    ['Môi trường', metrics?.environment || config?.environment || '—'],
    ['Cơ sở dữ liệu', metrics ? `${metrics.databaseStatus} · ${metrics.databaseLatencyMs} ms` : '—'],
    ['RAM đang dùng', metrics ? `${metrics.memoryWorkingSetMb} MB` : '—'],
    ['Thời gian chạy', metrics?.uptime ? metrics.uptime.split('.')[0] : '—'],
    ['Tác vụ', metrics ? `${metrics.activeJobsCount} đang chạy · ${metrics.pendingJobsCount} chờ · ${metrics.completedJobsCount} xong · ${metrics.failedJobsCount} lỗi` : '—'],
    ['Giọng đọc', config ? `${config.tts.provider} · ${config.tts.voice}` : '—'],
    ['Dựng song song tối đa', config ? `${config.storage.maxConcurrentRenders} video` : '—'],
    ['File tạm tự xoá sau', config ? `${config.storage.ttlMinutes} phút` : '—'],
  ];

  const usage = metrics?.tempStorageUsagePercent ?? 0;

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader
          title="Máy chủ"
          actions={
            <Button size="sm" variant="ghost" icon={<RefreshCw className="h-3.5 w-3.5" />} onClick={load}>
              Làm mới
            </Button>
          }
        />
        <CardBody className="pt-2">
          <dl className="divide-y divide-line text-[13px]">
            {rows.map(([k, v]) => (
              <div key={k} className="grid grid-cols-[112px_1fr] gap-3 py-2.5 sm:grid-cols-[160px_1fr]">
                <dt className="text-fg-muted">{k}</dt>
                <dd className="min-w-0 break-words text-fg">{v}</dd>
              </div>
            ))}
          </dl>
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Bộ nhớ tạm" description={metrics?.tempStoragePath || config?.storage.tempPath} />
        <CardBody className="space-y-2">
          <div className="flex items-baseline justify-between text-[13px]">
            <span className="text-fg">
              <span className="text-lg font-semibold tabular">{metrics?.tempStorageUsedMb ?? '—'}</span>
              <span className="text-fg-muted"> / {metrics?.tempStorageLimitMb ?? config?.storage.maxStorageMb ?? '—'} MB</span>
            </span>
            <span className="tabular text-fg-muted">{usage}%</span>
          </div>
          <ProgressBar value={usage} tone={usage > 80 ? 'danger' : usage > 60 ? 'warning' : 'neutral'} />
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Bảo trì" />
        <CardBody className="space-y-0 p-0">
          <MaintenanceRow
            title="Dọn file tạm hết hạn"
            desc="Xoá video, âm thanh và ảnh tạm đã quá hạn lưu trữ để giải phóng ổ đĩa."
            action={
              <Button size="sm" icon={<Trash2 className="h-3.5 w-3.5" />} loading={busy === 'cleanup'} disabled={busy !== null} onClick={cleanup}>
                Dọn ngay
              </Button>
            }
          />
          <Divider />
          <MaintenanceRow
            title="Rà soát tác vụ bị gián đoạn"
            desc="Khôi phục các video bị kẹt giữa chừng do máy chủ khởi động lại."
            action={
              <Button size="sm" icon={<Wrench className="h-3.5 w-3.5" />} loading={busy === 'recover'} disabled={busy !== null} onClick={recover}>
                Rà soát
              </Button>
            }
          />
          {lastReport && (
            <div className="border-t border-line bg-subtle px-5 py-3">
              <div className="mb-1 text-xs font-medium text-fg-muted">Nhật ký lần rà soát gần nhất</div>
              <ul className="max-h-40 space-y-0.5 overflow-y-auto font-mono text-[11px] text-fg-muted">
                {lastReport.map((l, i) => (
                  <li key={i}>{l}</li>
                ))}
              </ul>
            </div>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Quy tắc nội dung" description="Áp dụng cố định cho mọi video." />
        <CardBody>
          <ul className="list-disc space-y-1.5 pl-4 text-[13px] text-fg-muted">
            <li>Chỉ dùng ảnh có trong bài báo gốc. Không tìm ảnh ngoài, không dùng ảnh hay khuôn mặt do AI tạo.</li>
            <li>AI không được bịa số liệu, phải tách rõ dữ kiện và bình luận biên tập.</li>
            <li>Mọi video phải được người duyệt trước khi đăng lên Facebook.</li>
            <li>Không lưu lâu dài file video, ảnh, âm thanh — tự xoá sau khi đăng hoặc hết hạn.</li>
          </ul>
        </CardBody>
      </Card>
    </div>
  );
}

function MaintenanceRow({ title, desc, action }: { title: string; desc: string; action: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-3 px-5 py-4 sm:flex-row sm:items-center sm:justify-between sm:gap-4">
      <div>
        <div className="text-sm font-medium text-fg">{title}</div>
        <div className="mt-0.5 text-[13px] text-fg-muted">{desc}</div>
      </div>
      <div className="shrink-0">{action}</div>
    </div>
  );
}
