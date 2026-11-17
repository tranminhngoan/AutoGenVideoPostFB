import React, { useCallback, useEffect, useState } from 'react';
import { AlertTriangle, CheckCircle2, ExternalLink, ImageIcon, RefreshCw, ShieldCheck, XCircle } from 'lucide-react';
import { api, FactCheckReportDto, GateStatus } from '../services/api';
import { Alert, Badge, Button, Card, CardBody, CardHeader, EmptyState, cn } from './ui';
import { useToast } from './feedback';
import { errorText, formatDateTime, Tone } from '../lib/format';

export const GATE: Record<GateStatus, { label: string; tone: Tone }> = {
  APPROVED: { label: 'Đạt kiểm chứng', tone: 'success' },
  REVIEW_REQUIRED: { label: 'Cần người duyệt', tone: 'warning' },
  REJECTED: { label: 'Bị chặn', tone: 'danger' },
  PENDING: { label: 'Chưa kiểm chứng', tone: 'neutral' },
};

const CATEGORY: Record<string, string> = {
  A: 'Nhân vật',
  B: 'Sản phẩm',
  C: 'Công trình / địa điểm',
  D: 'Sự kiện',
  E: 'Số liệu',
};

const IMAGE_TYPE: Record<string, string> = {
  REAL: 'Ảnh báo',
  LICENSED: 'Ảnh có giấy phép',
  AI_ILLUSTRATION: 'Minh hoạ AI',
  INFOGRAPHIC: 'Đồ hoạ tự vẽ',
};

/** Video page tab: publication gate, what the video claims and from which sentence, where every picture comes from. */
export function FactCheckPanel({ projectId }: { projectId: string }) {
  const toast = useToast();
  const [report, setReport] = useState<FactCheckReportDto | null>(null);
  const [running, setRunning] = useState<'free' | 'ai' | null>(null);
  const [removing, setRemoving] = useState<string | null>(null);

  // "Thay ảnh": the picture is no longer used; the next render picks another one (article / Commons / chart)
  const removeImage = async (imageId: string) => {
    if (!report?.articleId) return;
    setRemoving(imageId);
    try {
      await api.updateArticleImage(report.articleId, imageId, { selectedForVideo: false });
      toast.success('Đã bỏ ảnh này', 'Bấm "Dựng lại video" để app thay bằng ảnh khác phù hợp.');
      load();
    } catch (err) {
      toast.error('Không bỏ được ảnh', errorText(err, ''));
    } finally {
      setRemoving(null);
    }
  };

  const load = useCallback(() => {
    api.getFactCheck(projectId).then(setReport).catch(() => setReport(null));
  }, [projectId]);
  useEffect(load, [load]);

  const run = async (ai: boolean) => {
    setRunning(ai ? 'ai' : 'free');
    try {
      setReport(await api.runFactCheck(projectId, ai));
      toast.success('Đã kiểm chứng lại');
    } catch (err) {
      toast.error('Không kiểm chứng được', errorText(err, ''));
    } finally {
      setRunning(null);
    }
  };

  if (!report) return <p className="text-sm text-fg-muted">Đang tải…</p>;
  const gate = GATE[report.gate.status] ?? GATE.PENDING;
  const sheet = report.factSheet;

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader
          title="Kết quả kiểm chứng"
          description="Video chỉ được tự đăng khi đạt kiểm chứng. Đây là tiêu chuẩn nội bộ, không phải đánh giá của Meta."
          actions={
            <>
              <Button size="sm" variant="ghost" icon={<RefreshCw className="h-3.5 w-3.5" />} loading={running === 'free'} disabled={running !== null} onClick={() => run(false)}>
                Kiểm tra lại
              </Button>
              <Button size="sm" icon={<ShieldCheck className="h-3.5 w-3.5" />} loading={running === 'ai'} disabled={running !== null} onClick={() => run(true)}>
                Kiểm tra lại bằng AI
              </Button>
            </>
          }
        />
        <CardBody className="space-y-3 pt-3">
          <div className="flex flex-wrap items-center gap-2 text-[13px]">
            <Badge tone={gate.tone} dot>
              {gate.label}
            </Badge>
            {report.gate.checkedAtUtc && <span className="text-fg-muted">lúc {formatDateTime(report.gate.checkedAtUtc)}</span>}
          </div>
          {report.gate.reasons.length > 0 && (
            <Alert tone={report.gate.status === 'REJECTED' ? 'danger' : 'warning'} icon={<AlertTriangle />}>
              <ul className="list-disc space-y-0.5 pl-4">
                {report.gate.reasons.map((r) => (
                  <li key={r}>{r}</li>
                ))}
              </ul>
            </Alert>
          )}
          {report.articleUrl && (
            <p className="text-[13px] text-fg-muted">
              Bài gốc:{' '}
              <a href={report.articleUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-accent hover:underline">
                {report.articlePublisher || 'mở bài'} <ExternalLink className="h-3 w-3" />
              </a>
              {report.articlePublishedAtUtc && ` · đăng ${formatDateTime(report.articlePublishedAtUtc)}`}
            </p>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Đối chiếu với bài gốc" description="Mỗi ý chính kèm câu nguyên văn trong bài làm căn cứ. Mọi con số trong lời đọc được so tự động với bài." />
        <CardBody className="space-y-4 pt-3">
          {!sheet ? (
            <EmptyState title="Chưa có hồ sơ kiểm chứng" description="Kịch bản được viết trước khi có tính năng này. Viết lại kịch bản để kiểm chứng." />
          ) : (
            <>
              {sheet.entities.length > 0 && (
                <div className="flex flex-wrap gap-1.5">
                  {sheet.entities.map((e) => (
                    <Badge key={e.name} tone={e.category === 'A' ? 'info' : 'neutral'}>
                      {CATEGORY[e.category] ?? e.category}: {e.name}
                    </Badge>
                  ))}
                </div>
              )}
              <ul className="space-y-3 text-[13px]">
                {sheet.claims.map((c, i) => (
                  <li key={i} className="flex gap-2.5">
                    {c.quoteFound ? (
                      <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-success" />
                    ) : (
                      <XCircle className="mt-0.5 h-4 w-4 shrink-0 text-warning" />
                    )}
                    <span className="min-w-0">
                      <span className="text-fg">{c.fact}</span>
                      {c.certainty === 'unconfirmed' && <Badge tone="warning" className="ml-1.5">Chưa xác nhận</Badge>}
                      {c.quote && (
                        <span className={cn('mt-1 block border-l-2 pl-2 text-xs', c.quoteFound ? 'border-line text-fg-muted' : 'border-warning text-warning')}>
                          “{c.quote}” {!c.quoteFound && '— không tìm thấy câu này trong bài'}
                        </span>
                      )}
                    </span>
                  </li>
                ))}
              </ul>
              <p className={cn('text-[13px]', sheet.numbersNotInArticle.length ? 'text-warning' : 'text-fg-muted')}>
                {sheet.numbersNotInArticle.length
                  ? `Số liệu không có trong bài gốc: ${sheet.numbersNotInArticle.join(', ')}`
                  : 'Mọi con số trong lời đọc đều có trong bài gốc.'}
              </p>
              {report.editorialPass && (
                <p className="text-[13px] text-fg-muted">
                  AI đối chiếu tiêu đề và nội dung: {report.editorialPass.consistent ? 'khớp với bài gốc.' : 'CHƯA khớp — ' + report.editorialPass.issues.join('; ')}
                </p>
              )}
            </>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Nguồn ảnh" description="Ảnh dùng trong video, nguồn, quyền sử dụng và ai/cái gì trong ảnh. Nguồn ảnh được ghi ngay trên video." />
        <CardBody className="pt-3">
          {report.images.length === 0 ? (
            <EmptyState icon={<ImageIcon />} title="Chưa có ảnh" />
          ) : (
            <ul className="-mx-5 divide-y divide-line border-y border-line">
              {report.images.map((img) => (
                <li key={img.imageId} className="flex gap-3 px-5 py-3 text-[13px]">
                  {img.url ? (
                    <img src={img.url} alt={img.caption ?? ''} loading="lazy" className="h-16 w-24 shrink-0 rounded border border-line object-cover" />
                  ) : (
                    <div className="flex h-16 w-24 shrink-0 items-center justify-center rounded border border-line bg-muted text-xs text-fg-subtle">Biểu đồ</div>
                  )}
                  <div className="min-w-0 flex-1 space-y-1">
                    <div className="flex flex-wrap items-center gap-1.5">
                      <Badge tone="neutral">{IMAGE_TYPE[img.imageType ?? 'REAL'] ?? img.imageType}</Badge>
                      {img.subjectName && (
                        <Badge tone={img.verificationStatus === 'VERIFIED' ? 'success' : 'warning'}>
                          {img.subjectName} · {img.verificationStatus === 'VERIFIED' ? 'đã xác minh' : 'chưa xác minh'}
                        </Badge>
                      )}
                      <span className="text-xs text-fg-subtle">Cảnh {img.scenes.join(', ')}</span>
                    </div>
                    <p className="text-fg">
                      {img.sourceName} · <span className="text-fg-muted">{img.licenseType}</span>
                    </p>
                    {img.licenseEvidence && <p className="line-clamp-2 text-xs text-fg-subtle">{img.licenseEvidence}</p>}
                    <div className="flex flex-wrap items-center gap-3">
                      {img.sourcePageUrl && (
                        <a href={img.sourcePageUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-xs text-accent hover:underline">
                          Xem nguồn <ExternalLink className="h-3 w-3" />
                        </a>
                      )}
                      {report.articleId && (
                        <button
                          type="button"
                          disabled={removing !== null}
                          onClick={() => removeImage(img.imageId)}
                          className="text-xs text-danger hover:underline disabled:opacity-50"
                        >
                          {removing === img.imageId ? 'Đang bỏ…' : 'Thay ảnh này'}
                        </button>
                      )}
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>
    </div>
  );
}
