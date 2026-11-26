import { useState } from 'react';
import { ShieldCheck } from 'lucide-react';
import { Alert, Badge, Button } from '../../components/ui';
import { navigate } from '../../lib/router';
import { growth, type Capabilities } from '../../services/growth';
import { vnShort } from '../../lib/growthMath';
import { CampaignForm } from './CampaignsView';
import { NoCampaign, Section, type GrowthCtx } from './shared';

const AVAIL: Record<string, { label: string; tone: 'success' | 'danger' | 'warning' | 'neutral' }> = {
  Yes: { label: 'Khả dụng', tone: 'success' },
  No: { label: 'Không', tone: 'danger' },
  Unknown: { label: 'Chỉ biết khi đăng', tone: 'warning' },
  Manual: { label: 'Thủ công', tone: 'neutral' },
};

export function SettingsView({ ctx }: { ctx: GrowthCtx }) {
  return (
    <div className="space-y-5">
      {ctx.campaign ? (
        <Section title="Cài đặt chiến dịch" description="Khung giờ, chế độ duyệt, mục tiêu, ngân sách AI và chi phí Meta One.">
          <CampaignForm key={ctx.campaign.id} ctx={ctx} campaign={ctx.campaign} onDone={(c) => ctx.setCampaign(c)} />
        </Section>
      ) : (
        <NoCampaign ctx={ctx} action={ctx.page && <Button variant="primary" onClick={() => navigate('/growth/campaigns')}>Tạo chiến dịch</Button>} />
      )}
      {ctx.page && <CapabilityCheck ctx={ctx} />}
    </div>
  );
}

/** Live check of what this Page's token can do through the Graph API (no posting, no fake results). */
function CapabilityCheck({ ctx }: { ctx: GrowthCtx }) {
  const [data, setData] = useState<Capabilities | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const run = async () => {
    setBusy(true);
    setError(null);
    try {
      setData(await growth.capabilities(ctx.page!.id));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Section
      title="Kiểm tra quyền Facebook Graph API"
      description="Thử thật với mã truy cập của Page (dùng kết nối Page hiện có). Mỗi Page có thể có quyền khác nhau."
      actions={<Button size="sm" icon={<ShieldCheck className="h-3.5 w-3.5" />} loading={busy} onClick={run}>{data ? 'Kiểm tra lại' : 'Kiểm tra'}</Button>}
    >
      {error && <Alert tone="danger">{error}</Alert>}
      {!data && !error && <p className="text-[13px] text-fg-subtle">Bấm "Kiểm tra" để xem chức năng nào dùng được với Page {ctx.page?.pageName}.</p>}
      {data && (
        <>
          <div className="mb-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-fg-muted">
            <span>Graph API {data.graphApiVersion}</span>
            <span>Kiểm tra lúc {vnShort(data.checkedAtUtc)}</span>
            {data.grantedPermissions.length > 0 && <span className="min-w-0 truncate">Quyền: {data.grantedPermissions.join(', ')}</span>}
          </div>
          {data.tokenError && <Alert tone="danger" className="mb-3" title="Mã truy cập có vấn đề">{data.tokenError} — kết nối lại Page ở Cài đặt → Facebook.</Alert>}
          <div className="-mx-4 overflow-x-auto sm:-mx-5">
            <table className="w-full min-w-[720px] text-[13px]">
              <thead>
                <tr className="border-b border-line text-left text-xs text-fg-subtle">
                  <th className="px-3 py-2 font-medium sm:pl-5">Chức năng</th>
                  <th className="px-3 py-2 font-medium">API</th>
                  <th className="px-3 py-2 font-medium">Quyền cần có</th>
                  <th className="px-3 py-2 font-medium sm:pr-5">Khả dụng</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((i) => (
                  <tr key={i.key} className="border-b border-line align-top last:border-0">
                    <td className="px-3 py-2 text-fg sm:pl-5">{i.feature}</td>
                    <td className="px-3 py-2 font-mono text-[11px] text-fg-muted">{i.api}</td>
                    <td className="px-3 py-2 text-xs text-fg-muted">{i.permissions}</td>
                    <td className="px-3 py-2 sm:pr-5">
                      <Badge tone={AVAIL[i.available]?.tone ?? 'neutral'}>{AVAIL[i.available]?.label ?? i.available}</Badge>
                      {i.detail && <div className="mt-1 max-w-xs text-[11px] text-fg-subtle">{i.detail}</div>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </Section>
  );
}
