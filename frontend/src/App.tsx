import { useEffect, useState, type ReactNode } from 'react';
import { Layout } from './components/Layout';
import { FeedbackProvider } from './components/feedback';
import { DashboardPage } from './pages/DashboardPage';
import { CreateVideoPage } from './pages/CreateVideoPage';
import { ArticlesPage } from './pages/ArticlesPage';
import { TrendingPage } from './pages/TrendingPage';
import { VideosPage } from './pages/VideosPage';
import { VideoDetailPage } from './pages/VideoDetailPage';
import { ActivityPage } from './pages/ActivityPage';
import { SettingsPage } from './pages/SettingsPage';
import { AutoPostPage } from './pages/AutoPostPage';
import { StudioPage } from './pages/StudioPage';
import { DramaScriptsPage } from './pages/DramaScriptsPage';
import { CommentsPage } from './pages/CommentsPage';
import { ReupPage } from './pages/reup/ReupPage';
import { SystemConfig, VideoProject } from './types';
import { api } from './services/api';
import { useRoute } from './lib/router';

const TITLES: Record<string, string> = {
  dashboard: 'Tổng quan',
  trending: 'Tin đang hot',
  'auto-post': 'Tự động đăng',
  create: 'Tạo video',
  videos: 'Video',
  articles: 'Bài báo',
  activity: 'Hoạt động',
  settings: 'Cài đặt',
  studio: 'Studio Video AI',
  'drama-scripts': 'Lên kịch bản',
  comments: 'Bình luận',
  reup: 'Reup Video',
};

export function App() {
  const route = useRoute();
  const [apiHealthy, setApiHealthy] = useState<boolean | null>(null);
  const [config, setConfig] = useState<SystemConfig | null>(null);
  const [projects, setProjects] = useState<VideoProject[] | null>(null);

  // Health + config
  useEffect(() => {
    let alive = true;
    const check = async () => {
      const healthy = await api.getHealth();
      if (!alive) return;
      setApiHealthy(healthy);
      if (healthy && !config) {
        const conf = await api.getConfigSummary();
        if (alive) setConfig(conf);
      }
    };
    check();
    const t = setInterval(check, 15000);
    return () => {
      alive = false;
      clearInterval(t);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Shared project list (sidebar badge, dashboard, videos, activity)
  const refreshProjects = async () => {
    const data = await api.getVideoProjects(1, 100);
    setProjects(data);
    return data;
  };

  useEffect(() => {
    refreshProjects();
    const t = setInterval(refreshProjects, 15000);
    return () => clearInterval(t);
  }, []);

  const [first = '', second] = route.segments;
  const active =
    first === '' ? 'dashboard' : first === 'videos' ? 'videos' : TITLES[first] ? first : 'dashboard';

  useEffect(() => {
    const base = second && first === 'videos' ? 'Chi tiết video' : TITLES[active];
    // (the video studio and Reup Video set their own sub-page titles)
    if (active === 'reup') return;
    document.title = base ? `${base} · Trạm Tin 24/7` : 'Trạm Tin 24/7';
  }, [active, first, second]);

  const awaitingCount = (projects ?? []).filter((p) => p.status === 'AwaitingApproval').length;

  let page: ReactNode;
  if (first === 'videos' && second) {
    page = <VideoDetailPage key={second} projectId={second} onChanged={refreshProjects} />;
  } else {
    switch (active) {
      case 'create':
        page = <CreateVideoPage initialUrl={route.query.get('url') ?? ''} onCreated={refreshProjects} />;
        break;
      case 'videos':
        page = <VideosPage projects={projects} onRefresh={refreshProjects} />;
        break;
      case 'trending':
        page = <TrendingPage onCreated={refreshProjects} />;
        break;
      case 'auto-post':
        page = <AutoPostPage />;
        break;
      case 'articles':
        page = <ArticlesPage />;
        break;
      case 'activity':
        page = <ActivityPage projects={projects} onRefresh={refreshProjects} />;
        break;
      case 'settings':
        page = <SettingsPage section={second} config={config} apiHealthy={apiHealthy} />;
        break;
      case 'studio':
        page = <StudioPage segments={route.segments.slice(1)} />;
        break;
      case 'drama-scripts':
        page = <DramaScriptsPage />;
        break;
      case 'comments':
        page = <CommentsPage />;
        break;
      case 'reup':
        page = <ReupPage segments={route.segments.slice(1)} />;
        break;
      default:
        page = <DashboardPage projects={projects} onCreated={refreshProjects} apiHealthy={apiHealthy} />;
    }
  }

  return (
    <FeedbackProvider>
      <Layout active={first === 'create' ? 'create' : active} apiHealthy={apiHealthy} awaitingCount={awaitingCount} simulation={config?.simulation}>
        {page}
      </Layout>
    </FeedbackProvider>
  );
}

export default App;
