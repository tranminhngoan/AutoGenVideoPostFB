import React, { useEffect, useState } from 'react';
import { Newspaper, ExternalLink, RefreshCw, Search, Link2, Pencil, Check, ArrowRight, ShieldAlert, ImageIcon } from 'lucide-react';
import { api, ArticleDto, ArticleImageDto } from '../services/api';
import {
  Badge,
  Button,
  ButtonLink,
  Card,
  Checkbox,
  Drawer,
  EmptyState,
  Field,
  IconButton,
  Input,
  Modal,
  PageHeader,
  Select,
  Skeleton,
  Textarea,
  Thumb,
  cn,
} from '../components/ui';
import { useToast } from '../components/feedback';
import { href } from '../lib/router';
import { IMAGE_CATEGORY, RIGHTS, domainOf, errorText, formatDateTime, isValidUrl, timeAgo } from '../lib/format';

export const ArticlesPage: React.FC = () => {
  const toast = useToast();
  const [articles, setArticles] = useState<ArticleDto[] | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [extracting, setExtracting] = useState(false);
  const [inputUrl, setInputUrl] = useState('');
  const [query, setQuery] = useState('');
  const [limit, setLimit] = useState(20);

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editingImage, setEditingImage] = useState<ArticleImageDto | null>(null);
  const [savingImage, setSavingImage] = useState(false);

  const selected = articles?.find((a) => a.id === selectedId) ?? null;

  const load = async () => {
    setRefreshing(true);
    try {
      setArticles(await api.getArticles(1, 50));
    } finally {
      setRefreshing(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  const handleExtract = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isValidUrl(inputUrl)) {
      toast.error('Link chưa hợp lệ', 'Hãy dán đường dẫn đầy đủ, bắt đầu bằng https://');
      return;
    }
    setExtracting(true);
    try {
      const extracted = await api.extractArticle(inputUrl.trim());
      toast.success('Đã đọc xong bài báo', `${extracted.imageCount} ảnh được lấy từ bài.`);
      setInputUrl('');
      setArticles((prev) => [extracted, ...(prev ?? []).filter((a) => a.id !== extracted.id)]);
      setSelectedId(extracted.id);
    } catch (err) {
      toast.error('Không đọc được bài báo', errorText(err, ''));
    } finally {
      setExtracting(false);
    }
  };

  const replaceImage = (articleId: string, updated: ArticleImageDto) => {
    setArticles((prev) =>
      (prev ?? []).map((a) =>
        a.id !== articleId ? a : { ...a, images: a.images.map((img) => (img.id === updated.id ? updated : img)) }
      )
    );
  };

  const updateImage = async (articleId: string, image: ArticleImageDto, patch: Partial<ArticleImageDto>) => {
    // optimistic
    replaceImage(articleId, { ...image, ...patch });
    try {
      const updated = await api.updateArticleImage(articleId, image.id, patch);
      replaceImage(articleId, updated);
    } catch (err) {
      replaceImage(articleId, image);
      toast.error('Không cập nhật được ảnh', errorText(err, ''));
    }
  };

  const saveImageModal = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingImage || !selected) return;
    setSavingImage(true);
    try {
      const updated = await api.updateArticleImage(selected.id, editingImage.id, {
        caption: editingImage.caption,
        rightsStatus: editingImage.rightsStatus,
        category: editingImage.category,
        selectedForVideo: editingImage.selectedForVideo,
      });
      replaceImage(selected.id, updated);
      setEditingImage(null);
      toast.success('Đã lưu thông tin ảnh');
    } catch (err) {
      toast.error('Không lưu được', errorText(err, ''));
    } finally {
      setSavingImage(false);
    }
  };

  const q = query.trim().toLowerCase();
  const visible = (articles ?? []).filter(
    (a) => !q || a.title.toLowerCase().includes(q) || a.publisher.toLowerCase().includes(q)
  );

  return (
    <div>
      <PageHeader
        title="Bài báo"
        description="Bài báo đã đọc và ảnh lấy từ chính bài viết. Chọn ảnh nào được dùng trong video."
        actions={
          <IconButton label="Làm mới" onClick={load} className="border border-line-strong bg-surface shadow-card">
            <RefreshCw className={cn('h-4 w-4', refreshing && 'animate-spin')} />
          </IconButton>
        }
      />

      <Card className="mb-6">
        <form onSubmit={handleExtract} className="flex flex-col gap-2 p-4 sm:flex-row">
          <div className="flex-1">
            <Input
              leading={<Link2 />}
              value={inputUrl}
              onChange={(e) => setInputUrl(e.target.value)}
              placeholder="Dán link bài báo để đọc nội dung và lấy ảnh…"
              disabled={extracting}
            />
          </div>
          <Button type="submit" loading={extracting} disabled={!inputUrl.trim()}>
            {extracting ? 'Đang đọc…' : 'Đọc bài báo'}
          </Button>
        </form>
      </Card>

      {articles && articles.length > 0 && (
        <div className="mb-3 flex items-center justify-between gap-3">
          <p className="shrink-0 whitespace-nowrap text-[13px] text-fg-muted">{articles.length} bài báo</p>
          <div className="w-full sm:w-64">
            <Input leading={<Search />} value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Tìm bài báo…" />
          </div>
        </div>
      )}

      <Card className="overflow-hidden">
        {!articles ? (
          <div className="divide-y divide-line">
            {Array.from({ length: 4 }).map((_, i) => (
              <div key={i} className="flex gap-4 p-4">
                <Skeleton className="h-[72px] w-24" />
                <div className="flex-1 space-y-2">
                  <Skeleton className="h-3 w-1/4" />
                  <Skeleton className="h-4 w-3/4" />
                  <Skeleton className="h-3 w-full" />
                </div>
              </div>
            ))}
          </div>
        ) : visible.length === 0 ? (
          <EmptyState
            icon={<Newspaper />}
            title={articles.length === 0 ? 'Chưa có bài báo nào' : 'Không tìm thấy bài báo'}
            description={
              articles.length === 0
                ? 'Dán link bài báo ở ô phía trên. Hệ thống sẽ lấy nội dung và các ảnh có trong bài.'
                : 'Thử từ khoá khác.'
            }
          />
        ) : (
          <ul className="divide-y divide-line">
            {visible.slice(0, limit).map((a) => {
              const used = a.images.filter((i) => i.selectedForVideo).length;
              return (
                <li key={a.id} className="group flex gap-3 p-4 transition-colors hover:bg-subtle sm:gap-4">
                  <button type="button" onClick={() => setSelectedId(a.id)} className="shrink-0">
                    <Thumb src={a.images[0]?.originalImageUrl} className="h-16 w-20 rounded-md border border-line sm:h-[72px] sm:w-24" />
                  </button>
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-x-1.5 text-xs text-fg-muted">
                      <span className="font-medium text-fg">{a.publisher}</span>
                      <span className="text-fg-subtle">·</span>
                      <span>{timeAgo(a.publishedAtUtc ?? a.createdAtUtc)}</span>
                      <span className="text-fg-subtle">·</span>
                      <span>
                        {a.images.length} ảnh{a.images.length > 0 && `, dùng ${used}`}
                      </span>
                    </div>
                    <button
                      type="button"
                      onClick={() => setSelectedId(a.id)}
                      className="mt-1 line-clamp-2 text-left text-sm font-medium text-fg hover:underline"
                    >
                      {a.title}
                    </button>
                    <p className="mt-1 line-clamp-1 text-[13px] text-fg-muted">
                      {a.description || a.contentBody?.slice(0, 200)}
                    </p>
                    {/* Phones: actions under the text */}
                    <div className="mt-2.5 flex items-center gap-2 sm:hidden">
                      <ButtonLink size="sm" variant="primary" href={href(`/create?url=${encodeURIComponent(a.url)}`)}>
                        Tạo video
                      </ButtonLink>
                      <ButtonLink size="sm" variant="ghost" href={a.url} target="_blank" rel="noreferrer" icon={<ExternalLink className="h-3.5 w-3.5" />}>
                        Bài gốc
                      </ButtonLink>
                    </div>
                  </div>
                  <div className="hidden shrink-0 items-start gap-1 sm:flex">
                    <IconButton label="Mở bài gốc" size="sm" onClick={() => window.open(a.url, '_blank', 'noreferrer')}>
                      <ExternalLink className="h-3.5 w-3.5" />
                    </IconButton>
                    <ButtonLink size="sm" href={href(`/create?url=${encodeURIComponent(a.url)}`)}>
                      Tạo video
                    </ButtonLink>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
        {visible.length > limit && (
          <div className="flex items-center justify-between border-t border-line px-4 py-3 text-[13px] text-fg-muted">
            <span>
              Đang hiện {limit}/{visible.length} bài
            </span>
            <Button size="sm" onClick={() => setLimit((l) => l + 20)}>
              Xem thêm
            </Button>
          </div>
        )}
      </Card>

      {/* Article drawer */}
      <Drawer
        open={!!selected}
        onClose={() => setSelectedId(null)}
        subtitle={
          selected && (
            <span>
              {selected.publisher} · {formatDateTime(selected.publishedAtUtc ?? selected.createdAtUtc)}
            </span>
          )
        }
        title={selected?.title}
        footer={
          selected && (
            <>
              <ButtonLink href={selected.url} target="_blank" rel="noreferrer" variant="ghost" icon={<ExternalLink className="h-4 w-4" />}>
                Bài gốc
              </ButtonLink>
              <ButtonLink
                href={href(`/create?url=${encodeURIComponent(selected.url)}`)}
                variant="primary"
                onClick={() => setSelectedId(null)}
              >
                Tạo video từ bài này <ArrowRight className="h-4 w-4" />
              </ButtonLink>
            </>
          )
        }
      >
        {selected && (
          <div className="space-y-8">
            <section>
              <div className="mb-3 flex items-baseline justify-between">
                <h3 className="text-sm font-semibold text-fg">Ảnh trong bài</h3>
                <span className="text-xs text-fg-muted">
                  Dùng {selected.images.filter((i) => i.selectedForVideo).length}/{selected.images.length}
                </span>
              </div>
              {selected.images.length === 0 ? (
                <div className="rounded-lg border border-dashed border-line px-4 py-6 text-center text-[13px] text-fg-muted">
                  Bài báo không có ảnh. Video sẽ dùng nền chữ.
                </div>
              ) : (
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
                  {selected.images.map((img) => (
                    <div key={img.id} className={cn('group', !img.selectedForVideo && 'opacity-60')}>
                      <div className="relative">
                        <Thumb
                          src={img.originalImageUrl}
                          alt={img.caption || ''}
                          className={cn(
                            'aspect-[4/3] rounded-md border',
                            img.selectedForVideo ? 'border-accent ring-1 ring-accent' : 'border-line'
                          )}
                        />
                        <button
                          type="button"
                          onClick={() => updateImage(selected.id, img, { selectedForVideo: !img.selectedForVideo })}
                          aria-label={img.selectedForVideo ? 'Bỏ chọn ảnh' : 'Chọn ảnh'}
                          className={cn(
                            'absolute right-1.5 top-1.5 flex h-6 w-6 items-center justify-center rounded-md border transition-colors',
                            img.selectedForVideo
                              ? 'border-accent bg-accent text-accent-fg'
                              : 'border-white/70 bg-black/30 text-transparent hover:bg-black/50'
                          )}
                        >
                          <Check className="h-3.5 w-3.5" />
                        </button>
                        <span className="absolute bottom-1.5 left-1.5 rounded bg-black/60 px-1.5 text-[11px] tabular text-white">
                          {img.articleImageOrder}
                        </span>
                        <button
                          type="button"
                          onClick={() => setEditingImage(img)}
                          aria-label="Sửa thông tin ảnh"
                          className="absolute bottom-1.5 right-1.5 flex h-7 w-7 items-center justify-center rounded-md bg-black/55 text-white transition-opacity hover:bg-black/70 sm:h-6 sm:w-6 sm:opacity-0 sm:group-hover:opacity-100"
                        >
                          <Pencil className="h-3 w-3" />
                        </button>
                      </div>
                      <p className="mt-1.5 line-clamp-2 text-xs text-fg-muted" title={img.caption || ''}>
                        {img.caption || <span className="text-fg-subtle">Không có chú thích</span>}
                      </p>
                      <div className="mt-1.5 flex flex-wrap gap-1">
                        <Badge tone={RIGHTS[img.rightsStatus]?.tone ?? 'neutral'}>
                          {RIGHTS[img.rightsStatus]?.label ?? img.rightsStatus}
                        </Badge>
                        {img.category && <Badge>{IMAGE_CATEGORY[img.category] ?? img.category}</Badge>}
                      </div>
                    </div>
                  ))}
                </div>
              )}
              <p className="mt-3 text-xs text-fg-subtle">Bấm ô vuông ở góc ảnh để chọn hoặc bỏ chọn, bấm biểu tượng bút để sửa chú thích và quyền sử dụng.</p>
            </section>

            <section>
              <h3 className="mb-2 text-sm font-semibold text-fg">Nội dung</h3>
              <div className="whitespace-pre-wrap text-[14px] leading-7 text-fg-muted">
                {selected.contentBody || 'Không có nội dung.'}
              </div>
              <a
                href={selected.url}
                target="_blank"
                rel="noreferrer"
                className="mt-3 inline-flex items-center gap-1 text-xs text-fg-subtle hover:text-fg"
              >
                {domainOf(selected.url)} <ExternalLink className="h-3 w-3" />
              </a>
            </section>
          </div>
        )}
      </Drawer>

      {/* Edit image modal */}
      <Modal
        open={!!editingImage}
        onClose={() => setEditingImage(null)}
        title={editingImage ? `Ảnh số ${editingImage.articleImageOrder}` : ''}
        footer={
          <>
            <Button variant="ghost" onClick={() => setEditingImage(null)}>
              Huỷ
            </Button>
            <Button variant="primary" type="submit" form="image-form" loading={savingImage}>
              Lưu
            </Button>
          </>
        }
      >
        {editingImage && (
          <form id="image-form" onSubmit={saveImageModal} className="space-y-4">
            <Thumb src={editingImage.originalImageUrl} className="aspect-video w-full rounded-md border border-line" fallback={<ImageIcon className="h-5 w-5" />} />
            <Field label="Chú thích">
              <Textarea
                rows={3}
                value={editingImage.caption || ''}
                onChange={(e) => setEditingImage({ ...editingImage, caption: e.target.value })}
              />
            </Field>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Loại ảnh">
                <Select value={editingImage.category} onChange={(e) => setEditingImage({ ...editingImage, category: e.target.value })}>
                  {Object.entries(IMAGE_CATEGORY).map(([v, l]) => (
                    <option key={v} value={v}>
                      {l}
                    </option>
                  ))}
                </Select>
              </Field>
              <Field label="Quyền sử dụng">
                <Select
                  value={editingImage.rightsStatus}
                  onChange={(e) => setEditingImage({ ...editingImage, rightsStatus: e.target.value })}
                >
                  {Object.entries(RIGHTS).map(([v, r]) => (
                    <option key={v} value={v}>
                      {r.label}
                    </option>
                  ))}
                </Select>
              </Field>
            </div>
            <div className="flex gap-2 rounded-md bg-warning/[0.07] px-3 py-2 text-xs text-fg-muted">
              <ShieldAlert className="mt-0.5 h-3.5 w-3.5 shrink-0 text-warning" />
              Chỉ đặt “Được phép dùng” khi đã xác nhận quyền sử dụng ảnh với toà soạn.
            </div>
            <Checkbox
              checked={editingImage.selectedForVideo}
              onChange={(v) => setEditingImage({ ...editingImage, selectedForVideo: v })}
              label="Dùng ảnh này trong video"
            />
          </form>
        )}
      </Modal>
    </div>
  );
};
