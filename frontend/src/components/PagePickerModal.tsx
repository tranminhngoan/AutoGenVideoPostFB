import React, { useEffect, useState } from 'react';
import { Sparkles } from 'lucide-react';
import { api, AutoPostPageDto } from '../services/api';
import { Badge, Button, Modal, RadioCard } from './ui';
import { PageAvatar } from './AutoPostWidgets';
import { getPrefs, savePrefs } from '../lib/prefs';

/** A page fits a story when it covers everything (Tổng hợp / Hot trend) or the story is of its topic. */
export function pageFitsStory(page: AutoPostPageDto, storyTopics: string[] = []) {
  const t = page.settings.topic;
  return t === 'tong-hop' || t === 'hot-trend' || storyTopics.includes(t);
}

/**
 * "Tạo video cho Page nào?": the video carries the chosen page's name and its topic's colours, and is posted
 * there. The page whose topic matches the story is preselected; the choice is remembered for next time.
 */
export function PagePickerModal({
  open,
  storyTitle,
  storyTopics,
  creating,
  onClose,
  onConfirm,
}: {
  open: boolean;
  storyTitle?: string;
  storyTopics?: string[];
  creating?: boolean;
  onClose: () => void;
  onConfirm: (facebookAccountId: string) => void;
}) {
  const [pages, setPages] = useState<AutoPostPageDto[] | null>(null);
  const [selected, setSelected] = useState<string>('');

  useEffect(() => {
    if (!open) return;
    api
      .getAutoPost()
      .then((d) => {
        const active = d.pages.filter((p) => p.tokenStatus !== 'Disconnected');
        setPages(active);
        // Preselect: a page of the story's own topic, then the remembered one, then the first
        const specific = active.find((p) => (storyTopics ?? []).includes(p.settings.topic));
        const remembered = active.find((p) => p.facebookAccountId === getPrefs().defaultPageId);
        setSelected((specific ?? remembered ?? active[0])?.facebookAccountId ?? '');
      })
      .catch(() => setPages([]));
  }, [open, storyTopics]);

  const confirm = () => {
    if (!selected) return;
    savePrefs({ ...getPrefs(), defaultPageId: selected });
    onConfirm(selected);
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Tạo video cho Page nào?"
      description={storyTitle ? `“${storyTitle}” — video mang tên và màu chủ đề của Page bạn chọn.` : 'Video mang tên và màu chủ đề của Page bạn chọn.'}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Huỷ
          </Button>
          <Button variant="primary" icon={<Sparkles className="h-4 w-4" />} loading={creating} disabled={!selected} onClick={confirm}>
            Tạo video
          </Button>
        </>
      }
    >
      {pages === null ? (
        <p className="text-sm text-fg-muted">Đang tải danh sách Page…</p>
      ) : pages.length === 0 ? (
        <p className="text-sm text-fg-muted">Chưa kết nối Page nào. Video sẽ dùng tên mặc định.</p>
      ) : (
        <div role="radiogroup" className="space-y-2">
          {pages.map((p) => (
            <RadioCard
              key={p.facebookAccountId}
              checked={selected === p.facebookAccountId}
              onClick={() => setSelected(p.facebookAccountId)}
              title={
                <span className="flex items-center gap-2">
                  <PageAvatar name={p.pageName} enabled={p.settings.enabled} className="h-7 w-7" />
                  <span className="truncate">{p.pageName}</span>
                </span>
              }
              description={p.topicName}
              aside={pageFitsStory(p, storyTopics) && (storyTopics ?? []).includes(p.settings.topic) ? <Badge tone="success">Đúng chủ đề</Badge> : undefined}
            />
          ))}
        </div>
      )}
    </Modal>
  );
}
