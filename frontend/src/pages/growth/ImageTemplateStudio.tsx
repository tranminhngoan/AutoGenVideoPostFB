import { useEffect, useRef, useState } from 'react';
import { Download, ImagePlus, Loader2, Save } from 'lucide-react';
import { Alert, Button, Checkbox, Field, Input, Select, Textarea, cn } from '../../components/ui';
import { growth, type Asset, type Campaign } from '../../services/growth';
import { wrapWords } from '../../lib/growthMath';

/* Pictures from ready-made templates, drawn with the browser's Canvas (no new library, no AI image generation):
   background picture (or colour), headline, text, Page name / logo, colours. Exported as JPEG into the campaign files. */

export interface TemplateDraft {
  template?: TemplateId;
  headline?: string;
  body?: string;
}

type TemplateId = 'quote' | 'story' | 'card' | 'question';

const TEMPLATES: { id: TemplateId; label: string; w: number; h: number; ratio: string }[] = [
  { id: 'quote', label: 'Ảnh nền + chữ (4:5)', w: 1080, h: 1350, ratio: '4/5' },
  { id: 'card', label: 'Thẻ màu (1:1)', w: 1080, h: 1080, ratio: '1/1' },
  { id: 'question', label: 'Câu hỏi tương tác (4:5)', w: 1080, h: 1350, ratio: '4/5' },
  { id: 'story', label: 'Story (9:16)', w: 1080, h: 1920, ratio: '9/16' },
];

const COLORS = ['#1f2937', '#9a3412', '#065f46', '#1e3a8a', '#6b21a8', '#be123c', '#a16207'];
const FONT = 'Inter, "Segoe UI", system-ui, sans-serif';

function loadImage(src: string, cross = false): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    if (cross) img.crossOrigin = 'anonymous';
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error('Không tải được ảnh'));
    img.src = src;
  });
}

function cover(ctx: CanvasRenderingContext2D, img: HTMLImageElement, w: number, h: number) {
  const s = Math.max(w / img.width, h / img.height);
  const dw = img.width * s, dh = img.height * s;
  ctx.drawImage(img, (w - dw) / 2, (h - dh) / 2, dw, dh);
}

/** Lines that fit <maxWidth> at the current font. */
function fit(ctx: CanvasRenderingContext2D, text: string, maxWidth: number): string[] {
  const out: string[] = [];
  for (const para of text.split('\n')) {
    let chars = 40;
    let lines = wrapWords(para, chars);
    while (chars > 6 && lines.some((l) => ctx.measureText(l).width > maxWidth)) lines = wrapWords(para, --chars);
    out.push(...lines);
  }
  return out;
}

export function ImageTemplateStudio({
  campaign,
  pageName,
  pictureUrl,
  initial,
  onSaved,
}: {
  campaign: Campaign;
  pageName: string;
  pictureUrl?: string;
  initial?: TemplateDraft;
  onSaved: (a: Asset) => void;
}) {
  const [template, setTemplate] = useState<TemplateId>(initial?.template ?? 'quote');
  const [headline, setHeadline] = useState(initial?.headline ?? '');
  const [body, setBody] = useState(initial?.body ?? '');
  const [color, setColor] = useState(COLORS[0]);
  const [bg, setBg] = useState<string | null>(null);
  const [showName, setShowName] = useState(true);
  const [useLogo, setUseLogo] = useState(true);
  const [logo, setLogo] = useState<HTMLImageElement | null>(null);
  const [logoNote, setLogoNote] = useState<string | null>(null);
  const [assets, setAssets] = useState<Asset[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const canvas = useRef<HTMLCanvasElement>(null);
  const spec = TEMPLATES.find((t) => t.id === template)!;

  useEffect(() => {
    if (initial?.template) setTemplate(initial.template);
    if (initial?.headline !== undefined) setHeadline(initial.headline);
    if (initial?.body !== undefined) setBody(initial.body);
  }, [initial]);

  useEffect(() => {
    growth.assets(campaign.id).then((a) => setAssets(a.filter((x) => x.kind === 'Image'))).catch(() => setAssets([]));
  }, [campaign.id]);

  // the Page picture is on Facebook's CDN: used only when it allows cross-origin drawing (else the export would be blocked)
  useEffect(() => {
    if (!useLogo || !pictureUrl) return setLogo(null);
    loadImage(pictureUrl, true).then(setLogo).catch(() => { setLogo(null); setLogoNote('Không lấy được ảnh đại diện Page để vẽ — tải logo lên thay thế.'); });
  }, [useLogo, pictureUrl]);

  useEffect(() => {
    let cancelled = false;
    const draw = async () => {
      const el = canvas.current;
      if (!el) return;
      const { w, h } = spec;
      el.width = w;
      el.height = h;
      const g = el.getContext('2d')!;
      g.textBaseline = 'alphabetic';
      g.fillStyle = color;
      g.fillRect(0, 0, w, h);
      const img = bg ? await loadImage(bg).catch(() => null) : null;
      if (cancelled) return;
      if (img && template !== 'card') {
        cover(g, img, w, h);
        const grad = g.createLinearGradient(0, h * 0.3, 0, h);
        grad.addColorStop(0, 'rgba(0,0,0,0)');
        grad.addColorStop(1, 'rgba(0,0,0,0.78)');
        g.fillStyle = grad;
        g.fillRect(0, 0, w, h);
      }
      const pad = 88;
      g.fillStyle = '#ffffff';

      if (template === 'question') {
        g.globalAlpha = 0.12;
        g.font = `800 900px ${FONT}`;
        g.fillText('?', w - 520, h - 160);
        g.globalAlpha = 1;
      }
      // headline + body block (bottom for photos, centre for card / story)
      g.font = `800 ${template === 'story' ? 92 : 84}px ${FONT}`;
      const hl = headline.trim() ? fit(g, headline.trim(), w - pad * 2) : [];
      g.font = `500 ${template === 'story' ? 50 : 44}px ${FONT}`;
      const bl = body.trim() ? fit(g, body.trim(), w - pad * 2) : [];
      const hlh = (template === 'story' ? 104 : 96), blh = (template === 'story' ? 66 : 58);
      const blockH = hl.length * hlh + (bl.length ? 28 + bl.length * blh : 0);
      let y = template === 'card' || template === 'story' ? (h - blockH) / 2 + hlh * 0.8 : h - pad - 110 - blockH + hlh * 0.8;
      if (template === 'card') {
        g.fillStyle = 'rgba(255,255,255,0.9)';
        g.fillRect(pad, y - hlh - 30, 120, 10);
        g.fillStyle = '#ffffff';
      }
      g.font = `800 ${template === 'story' ? 92 : 84}px ${FONT}`;
      g.shadowColor = img ? 'rgba(0,0,0,0.45)' : 'transparent';
      g.shadowBlur = img ? 18 : 0;
      for (const l of hl) { g.fillText(l, pad, y); y += hlh; }
      g.font = `500 ${template === 'story' ? 50 : 44}px ${FONT}`;
      g.globalAlpha = 0.92;
      y += bl.length ? 18 : 0;
      for (const l of bl) { g.fillText(l, pad, y); y += blh; }
      g.globalAlpha = 1;
      g.shadowBlur = 0;
      if (template === 'question') {
        g.font = `600 40px ${FONT}`;
        g.fillText('Bình luận cho mình biết nhé 👇', pad, h - pad - 130);
      }
      // brand: Page logo + name
      let bx = pad;
      const by = h - pad;
      if (useLogo && logo) {
        g.save();
        g.beginPath();
        g.arc(bx + 32, by - 22, 32, 0, Math.PI * 2);
        g.clip();
        g.drawImage(logo, bx, by - 54, 64, 64);
        g.restore();
        bx += 84;
      }
      if (showName && pageName) {
        g.font = `700 34px ${FONT}`;
        g.fillStyle = '#ffffff';
        g.fillText(pageName, bx, by - 10);
      }
    };
    draw();
    return () => { cancelled = true; };
  }, [template, headline, body, color, bg, showName, useLogo, logo, pageName, spec]);

  const exportBlob = () =>
    new Promise<Blob>((resolve, reject) => {
      try {
        canvas.current!.toBlob((b) => (b ? resolve(b) : reject(new Error('Không xuất được ảnh'))), 'image/jpeg', 0.92);
      } catch {
        reject(new Error('Trình duyệt chặn xuất ảnh vì logo từ Facebook — bỏ chọn "Logo Page" hoặc tải logo lên.'));
      }
    });

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const blob = await exportBlob();
      const a = await growth.upload(campaign.id, blob, `template-${template}-${Date.now()}.jpg`, 'Template');
      onSaved(a);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const downloadLocal = async () => {
    try {
      const blob = await exportBlob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `template-${template}.jpg`;
      a.click();
      setTimeout(() => URL.revokeObjectURL(url), 2000);
    } catch (e) {
      setError((e as Error).message);
    }
  };

  return (
    <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_320px]">
      <div className="space-y-4">
        <Field label="Template" htmlFor="tpl-kind">
          <Select id="tpl-kind" value={template} onChange={(e) => setTemplate(e.target.value as TemplateId)}>
            {TEMPLATES.map((t) => <option key={t.id} value={t.id}>{t.label}</option>)}
          </Select>
        </Field>
        <Field label="Tiêu đề" htmlFor="tpl-headline" hint="Ngắn, ≤ 8 chữ là dễ đọc nhất.">
          <Input id="tpl-headline" value={headline} onChange={(e) => setHeadline(e.target.value)} maxLength={120} />
        </Field>
        <Field label="Nội dung" htmlFor="tpl-body" optional>
          <Textarea id="tpl-body" rows={3} value={body} onChange={(e) => setBody(e.target.value)} maxLength={300} />
        </Field>
        <Field label="Màu nền / màu chủ đạo">
          <div className="flex flex-wrap items-center gap-2">
            {COLORS.map((c) => (
              <button key={c} type="button" aria-label={c} onClick={() => setColor(c)} className={cn('h-7 w-7 rounded-full border-2', color === c ? 'border-fg' : 'border-transparent')} style={{ background: c }} />
            ))}
            <input type="color" aria-label="Màu tuỳ chọn" value={color} onChange={(e) => setColor(e.target.value)} className="h-7 w-10 cursor-pointer rounded border border-line bg-transparent" />
          </div>
        </Field>
        {template !== 'card' && (
          <Field label="Ảnh nền" hint="Chọn từ kho của chiến dịch hoặc tải ảnh từ máy (ảnh của bạn, không tạo bằng AI).">
            <div className="grid grid-cols-5 gap-2 sm:grid-cols-6">
              <button type="button" onClick={() => setBg(null)} className={cn('aspect-square rounded-md border text-[11px] text-fg-muted', !bg ? 'border-accent ring-2 ring-accent/40' : 'border-line')}>Không</button>
              {(assets ?? []).slice(0, 22).map((a) => (
                <button key={a.id} type="button" onClick={() => setBg(a.url)} className={cn('aspect-square overflow-hidden rounded-md border', bg === a.url ? 'border-accent ring-2 ring-accent/40' : 'border-line')}>
                  <img src={a.url} alt="" loading="lazy" className="h-full w-full object-cover" />
                </button>
              ))}
              <label className="flex aspect-square cursor-pointer flex-col items-center justify-center gap-1 rounded-md border border-dashed border-line-strong text-[11px] text-fg-muted hover:bg-muted">
                <ImagePlus className="h-4 w-4" /> Từ máy
                <input type="file" accept="image/jpeg,image/png" className="sr-only" onChange={(e) => {
                  const f = e.target.files?.[0];
                  if (f) setBg(URL.createObjectURL(f));
                  e.target.value = '';
                }} />
              </label>
            </div>
            {assets === null && <div className="mt-1 text-xs text-fg-subtle"><Loader2 className="mr-1 inline h-3 w-3 animate-spin" />Đang tải kho ảnh…</div>}
          </Field>
        )}
        <div className="flex flex-wrap gap-5">
          <Checkbox checked={showName} onChange={setShowName} label="Tên Page" />
          <Checkbox checked={useLogo} onChange={(v) => { setUseLogo(v); setLogoNote(null); }} label="Logo Page" />
          <label className="cursor-pointer text-xs text-accent hover:underline">
            Tải logo khác
            <input type="file" accept="image/jpeg,image/png" className="sr-only" onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) loadImage(URL.createObjectURL(f)).then((img) => { setLogo(img); setUseLogo(true); setLogoNote(null); });
              e.target.value = '';
            }} />
          </label>
        </div>
        {logoNote && <p className="text-xs text-fg-subtle">{logoNote}</p>}
        {error && <Alert tone="danger">{error}</Alert>}
      </div>
      <div className="space-y-3">
        <div className="text-[13px] font-medium text-fg">Xem trước</div>
        <canvas ref={canvas} className="w-full rounded-lg border border-line bg-muted shadow-card" style={{ aspectRatio: spec.ratio }} aria-label="Ảnh xem trước" />
        <div className="flex gap-2">
          <Button variant="primary" className="flex-1" icon={<Save className="h-4 w-4" />} loading={busy} disabled={!headline.trim() && !body.trim()} onClick={save}>
            Lưu vào kho ảnh
          </Button>
          <Button icon={<Download className="h-4 w-4" />} onClick={downloadLocal} aria-label="Tải ảnh về máy" />
        </div>
        <p className="text-xs text-fg-subtle">{spec.w}×{spec.h} JPEG. Sau khi lưu, ảnh có thể đưa thẳng vào lịch đăng.</p>
      </div>
    </div>
  );
}
