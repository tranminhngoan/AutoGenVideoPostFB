import { useEffect, useMemo, useRef, useState, type MouseEvent } from 'react';
import { ArrowUp, BadgeCheck, Heart, Play, ShieldCheck, Users } from 'lucide-react';
import type { FeaturedPost, LandingConfig } from './cheoUyTinh.config';
import { openFacebook } from './facebookLink';
import { track } from './track';

/** What the public API returns (display data only). */
interface Showcase {
  pageId: string;
  name: string;
  about?: string | null;
  pictureUrl: string;
  coverUrl?: string | null;
  link?: string | null;
  followers?: number | null;
  posts: { id: string; message?: string | null; imageUrl?: string | null; isVideo: boolean; permalinkUrl: string; createdTime?: string | null }[];
}

interface PostView extends FeaturedPost {
  date?: string;
}

function FacebookGlyph({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} aria-hidden="true" fill="currentColor">
      <path d="M9.101 23.691v-7.98H6.627v-3.667h2.474v-1.58c0-4.085 1.848-5.978 5.858-5.978.401 0 .955.042 1.468.103a8.68 8.68 0 0 1 1.141.195v3.325a8.623 8.623 0 0 0-.653-.036 26.805 26.805 0 0 0-.733-.009c-.707 0-1.259.096-1.675.309a1.686 1.686 0 0 0-.679.622c-.258.42-.374.995-.374 1.752v1.297h3.919l-.386 2.103-.287 1.564h-3.246v8.245C19.396 23.238 24 18.179 24 12.044c0-6.627-5.373-12-12-12s-12 5.373-12 12c0 5.628 3.874 10.35 9.101 11.647Z" />
    </svg>
  );
}

function compact(n: number) {
  return new Intl.NumberFormat('vi-VN', { notation: 'compact', maximumFractionDigits: 1 }).format(n);
}

function splitMessage(message?: string | null): { title: string; description?: string } {
  const text = (message ?? '').trim();
  if (!text) return { title: 'Bài viết mới trên Page' };
  // First line = title (CSS clamps it to 2 lines), the other lines = description
  const [first, ...rest] = text.split(/\n+/).map((l) => l.trim()).filter(Boolean);
  const description = rest.join(' ').trim();
  return { title: first, description: description || undefined };
}

let viewTracked = false;

export function CheoUyTinhPage({ config }: { config: LandingConfig }) {
  const [data, setData] = useState<Showcase | null>(null);
  const [state, setState] = useState<'loading' | 'ready' | 'error'>('loading');
  const ctaRef = useRef<HTMLDivElement>(null);
  const [showSticky, setShowSticky] = useState(false);

  useEffect(() => {
    if (!viewTracked) {
      viewTracked = true;
      track(config.slug, 'view');
    }
    let alive = true;
    const count = config.featuredPosts.length ? 1 : config.livePostCount;
    fetch(`/api/public/landing/pages/${config.pageId}?posts=${count}`)
      .then(async (r) => {
        if (!r.ok) throw new Error(String(r.status));
        return (await r.json()) as Showcase;
      })
      .then((d) => {
        if (!alive) return;
        setData(d);
        setState('ready');
      })
      .catch(() => alive && setState('error'));
    return () => {
      alive = false;
    };
  }, [config]);

  // Sticky CTA on phones once the main button has scrolled away
  useEffect(() => {
    const el = ctaRef.current;
    if (!el || !('IntersectionObserver' in window)) return;
    const io = new IntersectionObserver(([entry]) => setShowSticky(!entry.isIntersecting && entry.boundingClientRect.top < 0), {
      threshold: 0,
    });
    io.observe(el);
    return () => io.disconnect();
  }, []);

  const name = data?.name || config.pageName;
  const avatar = config.avatarUrl ?? data?.pictureUrl ?? `https://graph.facebook.com/${config.pageId}/picture?type=large`;
  const cover = config.coverUrl ?? data?.coverUrl ?? null;
  const description = config.description ?? data?.about ?? null;
  const pageUrl = config.pageUrl;

  const posts: PostView[] = useMemo(() => {
    if (config.featuredPosts.length) return config.featuredPosts.slice(0, 6);
    return (data?.posts ?? []).slice(0, config.livePostCount).map((p) => ({
      id: p.id,
      ...splitMessage(p.message),
      imageUrl: p.imageUrl ?? undefined,
      isVideo: p.isVideo,
      url: p.permalinkUrl,
      date: p.createdTime ? new Date(p.createdTime).toLocaleDateString('vi-VN', { day: 'numeric', month: 'long', year: 'numeric' }) : undefined,
    }));
  }, [config, data]);

  useEffect(() => {
    document.title = `${name} · Cùng ủng hộ Page nhé!`;
  }, [name]);

  const onOpenPage = (e: MouseEvent) => {
    e.preventDefault();
    track(config.slug, 'open_page');
    openFacebook(pageUrl);
  };
  const onOpenPost = (e: MouseEvent, post: PostView) => {
    e.preventDefault();
    track(config.slug, 'open_post', post.id);
    openFacebook(post.url);
  };

  return (
    <div className="relative min-h-screen overflow-x-hidden bg-[#0b1220] text-slate-900">
      {/* Background glow */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 top-0 h-[520px] bg-[radial-gradient(60%_60%_at_50%_0%,rgba(8,102,255,0.45),transparent_70%)]" />
      <div aria-hidden className="pointer-events-none absolute -left-24 top-[420px] h-72 w-72 rounded-full bg-pink-500/20 blur-3xl" />

      <main className="relative mx-auto w-full max-w-xl px-4 pb-28 pt-4 sm:pt-10">
        {/* ── Hero ── */}
        <section className="overflow-hidden rounded-3xl bg-white shadow-2xl shadow-black/30">
          <div className="relative aspect-[16/8] w-full overflow-hidden bg-gradient-to-br from-[#0866FF] via-[#5b5bf5] to-[#e0457b]">
            {cover && <img src={cover} alt="" className="absolute inset-0 h-full w-full object-cover" loading="eager" />}
            <div className="absolute inset-0 bg-gradient-to-t from-black/30 to-transparent" />
          </div>
          <div className="relative px-5 pb-6">
            <div className="-mt-12">
              <img
                src={avatar}
                alt={name}
                className="h-24 w-24 shrink-0 rounded-full border-4 border-white bg-slate-100 object-cover shadow-lg"
              />
            </div>
            <h1 className="mt-3 text-2xl font-extrabold tracking-tight text-slate-900 sm:text-3xl">{name}</h1>
            {description && <p className="mt-1.5 text-[15px] leading-relaxed text-slate-600">{description}</p>}
            {/* Trust promise — the one line visitors must notice */}
            <div className="relative mt-4 overflow-hidden rounded-2xl bg-gradient-to-r from-emerald-500 via-emerald-500 to-teal-500 p-[2px] shadow-lg shadow-emerald-500/25">
              <div className="flex items-center gap-3 rounded-[14px] bg-gradient-to-r from-emerald-50 to-teal-50 px-3.5 py-3">
                <span className="relative flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-emerald-500 to-teal-600 text-white shadow-md shadow-emerald-600/40">
                  <span className="absolute inset-0 animate-ping rounded-full bg-emerald-400/40 [animation-duration:2.4s]" />
                  <ShieldCheck className="relative h-6 w-6" strokeWidth={2.4} />
                </span>
                <div className="min-w-0">
                  <p className="flex items-center gap-1 text-[11px] font-extrabold uppercase tracking-[0.12em] text-emerald-700">
                    <BadgeCheck className="h-3.5 w-3.5" /> Cam kết uy tín
                  </p>
                  <p className="mt-0.5 text-[15px] font-extrabold leading-snug text-slate-900 sm:text-base">{config.greeting}</p>
                </div>
              </div>
            </div>

            {/* Followers + main button side by side (wraps under each other on narrow phones) */}
            <div ref={ctaRef} className="mt-4 flex flex-wrap items-center gap-2.5">
              {data?.followers != null && data.followers > 0 && (
                <span className="inline-flex h-11 w-full shrink-0 items-center justify-center gap-1.5 rounded-xl bg-slate-100 px-3 text-sm font-semibold text-slate-700 sm:w-auto">
                  <Users className="h-4 w-4" /> {compact(data.followers)} người theo dõi
                </span>
              )}
              <a
                href={pageUrl}
                onClick={onOpenPage}
                rel="noopener noreferrer"
                className="inline-flex h-11 min-w-[250px] flex-1 items-center justify-center gap-2 whitespace-nowrap rounded-xl bg-[#0866FF] px-5 text-[13px] font-extrabold text-white shadow-md shadow-blue-600/30 transition hover:bg-[#0759e0] active:scale-[0.98]"
              >
                <FacebookGlyph className="h-5 w-5 shrink-0" />
                {config.cta.button}
              </a>
            </div>
          </div>
        </section>

        {/* ── Posts ── */}
        <section className="mt-8">
          <div className="mb-3 flex items-center justify-between px-1">
            <h2 className="text-lg font-bold text-white">Bài viết nổi bật</h2>
            <a href={pageUrl} onClick={onOpenPage} className="text-sm font-semibold text-blue-300 hover:text-blue-200">
              Xem tất cả →
            </a>
          </div>

          {state === 'loading' && !config.featuredPosts.length && (
            <div className="grid gap-4 sm:grid-cols-2">
              {[0, 1, 2].map((i) => (
                <div key={i} className="overflow-hidden rounded-2xl bg-white/10">
                  <div className="aspect-[4/5] animate-pulse bg-white/10" />
                  <div className="space-y-2 p-4">
                    <div className="h-4 w-3/4 animate-pulse rounded bg-white/10" />
                    <div className="h-3 w-1/2 animate-pulse rounded bg-white/10" />
                  </div>
                </div>
              ))}
            </div>
          )}

          {posts.length > 0 && (
            <div className="grid gap-4 sm:grid-cols-2">
              {posts.map((post) => (
                <article key={post.id} className="flex flex-col overflow-hidden rounded-2xl bg-white shadow-lg shadow-black/20">
                  <a href={post.url} onClick={(e) => onOpenPost(e, post)} className="relative block aspect-[4/5] overflow-hidden bg-slate-100">
                    {post.imageUrl ? (
                      <img src={post.imageUrl} alt="" loading="lazy" className="absolute inset-0 h-full w-full object-cover" />
                    ) : (
                      <div className="flex h-full items-center justify-center bg-gradient-to-br from-blue-50 to-pink-50">
                        <Heart className="h-8 w-8 text-pink-400" />
                      </div>
                    )}
                    {post.isVideo && (
                      <span className="absolute inset-0 flex items-center justify-center">
                        <span className="flex h-14 w-14 items-center justify-center rounded-full bg-black/55 backdrop-blur-sm">
                          <Play className="ml-0.5 h-6 w-6 fill-white text-white" />
                        </span>
                      </span>
                    )}
                  </a>
                  <div className="flex flex-1 flex-col p-4">
                    {post.date && <p className="text-xs font-medium text-slate-400">{post.date}</p>}
                    <h3 className="mt-1 line-clamp-2 text-[15px] font-bold leading-snug text-slate-900">{post.title}</h3>
                    {post.description && <p className="mt-1.5 line-clamp-3 text-sm leading-relaxed text-slate-600">{post.description}</p>}
                    <div className="min-h-4 flex-1" />
                    <a
                      href={post.url}
                      onClick={(e) => onOpenPost(e, post)}
                      className="mt-4 inline-flex w-full items-center justify-center gap-1.5 whitespace-nowrap rounded-xl bg-slate-100 px-3 py-2.5 text-[13px] font-bold text-[#0866FF] transition hover:bg-blue-50 active:scale-[0.98]"
                    >
                      <FacebookGlyph className="h-4 w-4" />
                      Xem bài viết trên Facebook
                    </a>
                  </div>
                </article>
              ))}
            </div>
          )}

          {(state === 'error' || state === 'ready') && posts.length === 0 && (
            <div className="rounded-2xl bg-white/10 p-6 text-center text-sm text-slate-200">
              Xem các bài viết và video mới nhất ngay trên Facebook Page nhé!
              <a
                href={pageUrl}
                onClick={onOpenPage}
                className="mt-4 flex items-center justify-center gap-2 rounded-xl bg-white px-4 py-3 font-bold text-[#0866FF]"
              >
                <FacebookGlyph className="h-5 w-5" /> Mở {name} trên Facebook
              </a>
            </div>
          )}
        </section>

        {/* ── Footer ── */}
        <footer className="mt-12 text-center">
          <p className="mx-auto max-w-md text-[15px] leading-relaxed text-slate-300">{config.thanks}</p>
          <button
            type="button"
            onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
            className="mt-5 inline-flex items-center gap-2 rounded-full border border-white/20 px-4 py-2 text-sm font-semibold text-white hover:bg-white/10"
          >
            <ArrowUp className="h-4 w-4" /> Lên đầu trang
          </button>
          <p className="mt-6 text-xs text-slate-500">© {new Date().getFullYear()} {name}</p>
        </footer>
      </main>

      {/* Sticky CTA (phones) */}
      <div
        className={`fixed inset-x-0 bottom-0 z-20 px-4 pb-[max(12px,env(safe-area-inset-bottom))] pt-3 transition-transform duration-300 sm:hidden ${
          showSticky ? 'translate-y-0' : 'translate-y-full'
        }`}
      >
        <a
          href={pageUrl}
          onClick={onOpenPage}
          className="flex min-h-[52px] items-center justify-center gap-2 rounded-2xl bg-[#0866FF] px-4 text-sm font-extrabold text-white shadow-2xl shadow-black/50"
        >
          <FacebookGlyph className="h-5 w-5" />
          {config.cta.button}
        </a>
      </div>
    </div>
  );
}
