import { useEffect, useMemo, useState } from 'react';
import { Clapperboard, TrendingUp } from 'lucide-react';
import { Button, Card } from './ui';
import { api, PagePostStatsDto } from '../services/api';
import { studio } from '../services/studio';
import { ALL_NOSTALGIA_SCRIPTS, NOSTALGIA_CATEGORIES, NostalgiaScript } from '../data/nostalgiaScriptLibrary';
import { queueStudioImport } from '../lib/studioImport';
import { navigate } from '../lib/router';
import { formatNumber } from '../lib/format';

const norm = (s: string) => s.toLowerCase().normalize('NFC').replace(/\s+/g, ' ').trim();

/** The library script a post was made from: its code "(BCN-03)" in the title, or the script's title in the text. */
function scriptOf(post: PagePostStatsDto): NostalgiaScript | undefined {
  const code = post.title.match(/\(([A-Z]{2,5}-\d{1,4})\)/)?.[1];
  if (code) {
    const byCode = ALL_NOSTALGIA_SCRIPTS.find((s) => s.code === code);
    if (byCode) return byCode;
  }
  const text = norm(post.title);
  return ALL_NOSTALGIA_SCRIPTS.find((s) => text.includes(norm(s.title)) || (s.title.length > 24 && text.includes(norm(s.title).slice(0, 24))));
}

/**
 * "What to make next": the channel's own numbers (views on Facebook + Instagram of the videos made from the library,
 * also the ones posted by hand) by script theme, and scripts of the best themes not made yet.
 */
export function ScriptSuggestions() {
  const [posts, setPosts] = useState<PagePostStatsDto[] | null>(null);
  const [made, setMade] = useState<Set<string>>(new Set());

  useEffect(() => {
    api.getPageStats(30)
      .then((s) => setPosts(s.pages.flatMap((p) => p.recentPosts ?? []).filter((p) => p.source !== 'Tool')))
      .catch(() => setPosts([]));
    studio.videos()
      .then((list) => setMade(new Set(list.map((v) => v.title.match(/\(([A-Z]{2,5}-\d{1,4})\)/)?.[1]).filter((c): c is string => !!c))))
      .catch(() => undefined);
  }, []);

  const themes = useMemo(() => {
    if (!posts) return [];
    const byCategory = new Map<string, { views: number; videos: number; best?: { title: string; views: number } }>();
    for (const p of posts) {
      const script = scriptOf(p);
      if (!script) continue;
      const views = (p.views ?? 0) + (p.instagramViews ?? 0);
      const t = byCategory.get(script.category) ?? { views: 0, videos: 0 };
      t.views += views;
      t.videos += 1;
      if (!t.best || views > t.best.views) t.best = { title: script.title, views };
      byCategory.set(script.category, t);
    }
    return [...byCategory.entries()]
      .map(([id, t]) => ({ id, ...t, average: Math.round(t.views / Math.max(1, t.videos)), category: NOSTALGIA_CATEGORIES.find((c) => c.id === id) }))
      .filter((t) => t.category)
      .sort((a, b) => b.average - a.average);
  }, [posts]);

  if (!posts) return null;

  const build = (s: NostalgiaScript) => {
    queueStudioImport(s);
    navigate('/studio/new');
  };

  return (
    <Card className="space-y-3 border-accent/30 p-4">
      <div className="flex items-center gap-2 text-sm font-semibold text-fg">
        <TrendingUp className="h-4 w-4" /> Gợi ý làm tiếp theo số liệu kênh
      </div>
      {themes.length === 0 ? (
        <p className="text-xs text-fg-muted">
          Chưa đủ số liệu: đăng thêm vài video từ kho kịch bản (đăng qua tool hoặc đăng tay đều được) — tool sẽ xem chủ đề nào được xem nhiều nhất
          rồi gợi ý kịch bản cùng chủ đề chưa làm.
        </p>
      ) : (
        <>
          <ul className="space-y-1 text-xs text-fg-muted">
            {themes.slice(0, 3).map((t, i) => (
              <li key={t.id}>
                {i === 0 ? '🏆 ' : '• '}
                <b className="text-fg">{t.category!.icon} {t.category!.name}</b>: trung bình {formatNumber(t.average)} lượt xem / video ({t.videos} video)
                {t.best && <> · nổi bật: <i>{t.best.title}</i> ({formatNumber(t.best.views)})</>}
              </li>
            ))}
          </ul>
          <div>
            <p className="mb-1.5 text-xs font-medium text-fg">Kịch bản chưa làm thuộc chủ đề đang được xem nhiều nhất:</p>
            <ul className="grid gap-1.5 sm:grid-cols-2">
              {ALL_NOSTALGIA_SCRIPTS.filter((s) => s.category === themes[0].id && !made.has(s.code)).slice(0, 6).map((s) => (
                <li key={s.id} className="flex items-center justify-between gap-2 rounded-lg border border-line px-2.5 py-1.5 text-xs">
                  <span className="min-w-0">
                    <span className="block truncate font-medium text-fg">{s.title}</span>
                    <span className="text-fg-muted">{s.code} · {s.mood}</span>
                  </span>
                  <Button size="sm" variant="secondary" icon={<Clapperboard className="h-3.5 w-3.5" />} onClick={() => build(s)}>Dựng video</Button>
                </li>
              ))}
            </ul>
          </div>
        </>
      )}
    </Card>
  );
}
