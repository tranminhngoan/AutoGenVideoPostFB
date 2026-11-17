import React, { useRef, useState } from 'react';
import { ImagePlus, Trash2 } from 'lucide-react';
import { api } from '../services/api';
import { Button, Field, Select } from './ui';
import { useToast } from './feedback';
import { errorText } from '../lib/format';

export const WRITING_TONES = [
  { value: 'neutral', label: 'Trung tính, khách quan' },
  { value: 'youthful', label: 'Trẻ trung, gần gũi' },
  { value: 'expert', label: 'Phân tích chuyên sâu' },
  { value: 'brief', label: 'Ngắn gọn, đi thẳng vào việc' },
];

/** Page brand kit: writing tone (part of the page settings form) and logo (uploaded right away). */
export function BrandKitFields({
  pageId,
  tone,
  hasLogo,
  onToneChange,
  onLogoChanged,
}: {
  pageId: string;
  tone: string;
  hasLogo: boolean;
  onToneChange: (tone: string) => void;
  onLogoChanged: () => void;
}) {
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [version, setVersion] = useState(0);

  const upload = async (file?: File) => {
    if (!file) return;
    setBusy(true);
    try {
      await api.uploadPageLogo(pageId, file);
      setVersion((v) => v + 1);
      toast.success('Đã lưu logo', 'Logo hiện ở khung tên Page trên video và bản tin từ lần dựng tiếp theo.');
      onLogoChanged();
    } catch (err) {
      toast.error('Không lưu được logo', errorText(err, ''));
    } finally {
      setBusy(false);
      if (input.current) input.current.value = '';
    }
  };

  const remove = async () => {
    setBusy(true);
    try {
      await api.deletePageLogo(pageId);
      onLogoChanged();
    } catch (err) {
      toast.error('Không xoá được logo', errorText(err, ''));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="grid gap-5 sm:grid-cols-2">
      <Field label="Giọng văn của Page" hint="Chỉ đổi cách viết lời dẫn, không bao giờ đổi sự thật trong bài.">
        <Select value={tone} onChange={(e) => onToneChange(e.target.value)}>
          {WRITING_TONES.map((t) => (
            <option key={t.value} value={t.value}>
              {t.label}
            </option>
          ))}
        </Select>
      </Field>
      <Field label="Logo của Page" hint="Ảnh vuông PNG/JPEG, tối đa 400 KB. Hiện trong khung tên Page trên video.">
        <div className="flex items-center gap-3">
          {hasLogo ? (
            <img src={api.pageLogoUrl(pageId, version)} alt="Logo" className="h-11 w-11 rounded-full border border-line object-cover" />
          ) : (
            <div className="flex h-11 w-11 items-center justify-center rounded-full border border-dashed border-line-strong text-fg-subtle">
              <ImagePlus className="h-4 w-4" />
            </div>
          )}
          <input ref={input} type="file" accept="image/png,image/jpeg,image/webp" className="hidden" onChange={(e) => upload(e.target.files?.[0])} />
          <Button size="sm" loading={busy} onClick={() => input.current?.click()}>
            {hasLogo ? 'Đổi logo' : 'Tải logo lên'}
          </Button>
          {hasLogo && (
            <Button size="sm" variant="ghost" icon={<Trash2 className="h-3.5 w-3.5" />} disabled={busy} onClick={remove}>
              Xoá
            </Button>
          )}
        </div>
      </Field>
    </div>
  );
}
