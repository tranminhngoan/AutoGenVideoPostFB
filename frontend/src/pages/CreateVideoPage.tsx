import React, { useEffect, useRef, useState } from 'react';
import { Link2, ClipboardPaste, Info } from 'lucide-react';
import { Button, Card, Field, Input, PageHeader, RadioCard, Segmented, Checkbox, Divider, Select } from '../components/ui';
import { api, FacebookAccountDto } from '../services/api';
import { useToast } from '../components/feedback';
import { createVideoFromUrl } from '../lib/actions';
import { navigate } from '../lib/router';
import { detectPublisher, errorText, isValidUrl } from '../lib/format';
import { AI_PROVIDERS, DURATIONS, AiProvider, durationLabel, getPrefs, savePrefs } from '../lib/prefs';
import { VoicePicker } from '../components/VoicePicker';

interface CreateVideoPageProps {
  initialUrl?: string;
  onCreated: () => void;
}

export const CreateVideoPage: React.FC<CreateVideoPageProps> = ({ initialUrl = '', onCreated }) => {
  const toast = useToast();
  const prefs = getPrefs();
  const urlRef = useRef<HTMLInputElement>(null);

  const [url, setUrl] = useState(initialUrl);
  const [title, setTitle] = useState('');
  const [aiProvider, setAiProvider] = useState<AiProvider>(prefs.aiProvider);
  const [duration, setDuration] = useState(prefs.targetDuration);
  const [voice, setVoice] = useState(prefs.voice);
  const [pages, setPages] = useState<FacebookAccountDto[]>([]);
  const [pageId, setPageId] = useState(prefs.defaultPageId ?? '');

  useEffect(() => {
    api
      .getFacebookPages()
      .then((list) => {
        const active = list.filter((p) => p.isActive);
        setPages(active);
        // Forget a remembered page that was disconnected
        setPageId((cur) => (cur && !active.some((p) => p.id === cur) ? '' : cur));
      })
      .catch(() => setPages([]));
  }, []);
  const [rememberDefaults, setRememberDefaults] = useState(false);
  const [touched, setTouched] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    setUrl(initialUrl);
  }, [initialUrl]);

  useEffect(() => {
    if (!initialUrl) urlRef.current?.focus();
  }, [initialUrl]);

  const urlValid = isValidUrl(url);
  const urlError = touched && url && !urlValid ? 'Link chưa hợp lệ. Hãy dán đường dẫn đầy đủ, bắt đầu bằng https://' : undefined;

  const handlePaste = async () => {
    try {
      const text = await navigator.clipboard.readText();
      if (text) {
        setUrl(text.trim());
        setTouched(true);
      }
    } catch {
      toast.info('Trình duyệt chưa cho phép đọc clipboard', 'Hãy dán bằng Ctrl+V.');
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (!urlValid) return;
    setSubmitting(true);
    try {
      // The chosen page is always remembered: quick "Tạo video" buttons elsewhere use it too
      savePrefs(rememberDefaults ? { aiProvider, voice, targetDuration: duration, defaultPageId: pageId } : { defaultPageId: pageId });
      const project = await createVideoFromUrl({ url, title, aiProvider, voice, targetDuration: duration, facebookAccountId: pageId });
      toast.success('Đã bắt đầu tạo video', 'Bạn có thể theo dõi tiến độ ở trang chi tiết.');
      onCreated();
      navigate(`/videos/${project.id}`);
    } catch (err) {
      toast.error('Không tạo được video', errorText(err, 'Không kết nối được máy chủ.'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="mx-auto max-w-2xl">
      <PageHeader
        title="Tạo video"
        description="Chuyển một bài báo thành video dọc 9:16, độ dài theo nội dung bài."
      />

      <form onSubmit={handleSubmit}>
        <Card>
          <div className="space-y-5 p-5">
            <Field
              label="Link bài báo"
              htmlFor="url"
              error={urlError}
              hint={urlValid ? `Nguồn: ${detectPublisher(url)}` : 'Hỗ trợ VnExpress, Tuổi Trẻ, Thanh Niên, Dân Trí và hầu hết báo điện tử.'}
            >
              <div className="flex gap-2">
                <div className="flex-1">
                  <Input
                    ref={urlRef}
                    id="url"
                    type="url"
                    leading={<Link2 />}
                    value={url}
                    onChange={(e) => setUrl(e.target.value)}
                    onBlur={() => setTouched(true)}
                    placeholder="https://"
                    className="h-10"
                  />
                </div>
                <Button type="button" size="lg" icon={<ClipboardPaste className="h-4 w-4" />} onClick={handlePaste}>
                  Dán
                </Button>
              </div>
            </Field>

            <Field
              label="Tiêu đề video"
              htmlFor="title"
              optional
              hint="Để trống, AI sẽ tự đặt tiêu đề theo nội dung bài."
            >
              <Input id="title" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Ví dụ: Cầu Mỹ Thuận 2 chính thức thông xe" />
            </Field>

            {pages.length > 0 && (
              <Field
                label="Làm video cho Page"
                htmlFor="page"
                hint="Tên Page hiện trên video, màu video theo chủ đề của Page (chỉnh ở Tự động đăng). Video cũng đăng mặc định lên Page này."
              >
                <Select id="page" value={pageId} onChange={(e) => setPageId(e.target.value)}>
                  <option value="">{pages.length === 1 ? pages[0].pageName : 'Chưa chọn (dùng tên mặc định)'}</option>
                  {pages.length > 1 &&
                    pages.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.pageName}
                      </option>
                    ))}
                </Select>
              </Field>
            )}
          </div>

          <Divider />

          <div className="space-y-5 p-5">
            <div>
              <div className="mb-2 text-[13px] font-medium text-fg">AI viết kịch bản</div>
              <div className="grid gap-2 sm:grid-cols-2" role="radiogroup">
                {AI_PROVIDERS.map((p) => (
                  <RadioCard
                    key={p.id}
                    checked={aiProvider === p.id}
                    onClick={() => setAiProvider(p.id)}
                    title={p.name}
                    description={p.desc}
                  />
                ))}
              </div>
            </div>

            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <div className="text-[13px] font-medium text-fg">Thời lượng tối đa</div>
                <div className="text-xs text-fg-subtle">Bài ít nội dung sẽ ngắn hơn. Đọc hết là video dừng.</div>
              </div>
              <Segmented
                value={duration}
                onChange={setDuration}
                options={DURATIONS.map((d) => ({ value: d, label: durationLabel(d) }))}
              />
            </div>
          </div>

          <Divider />

          <div className="p-5">
            <div className="mb-3 text-[13px] font-medium text-fg">Giọng đọc</div>
            <VoicePicker value={voice} onChange={setVoice} />
          </div>

          <div className="flex flex-col gap-4 rounded-b-lg border-t border-line bg-subtle px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
            <Checkbox checked={rememberDefaults} onChange={setRememberDefaults} label="Lưu làm thiết lập mặc định" />
            <Button type="submit" variant="primary" size="lg" loading={submitting}>
              {submitting ? 'Đang tạo…' : 'Tạo video'}
            </Button>
          </div>
        </Card>
      </form>

      <div className="mt-4 flex gap-2.5 px-1 text-xs leading-relaxed text-fg-muted">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0 text-fg-subtle" />
        <p>
          Video chỉ dùng ảnh có sẵn trong bài báo, không lấy ảnh ngoài hay ảnh do AI tạo. Video luôn cần bạn duyệt
          trước khi đăng lên Facebook. File video, ảnh và âm thanh chỉ lưu tạm và tự xoá sau khi đăng.
        </p>
      </div>
    </div>
  );
};
