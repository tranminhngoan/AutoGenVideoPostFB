import React, { useState, useEffect } from 'react';
import { Sidebar } from './components/Sidebar';
import { Header } from './components/Header';
import { DashboardPage } from './pages/DashboardPage';
import { CreateVideoPage } from './pages/CreateVideoPage';
import { ArticlesPage } from './pages/ArticlesPage';
import { VideosPage } from './pages/VideosPage';
import { VideoDetailPage } from './pages/VideoDetailPage';
import { AiConfigPage } from './pages/AiConfigPage';
import { TtsConfigPage } from './pages/TtsConfigPage';
import { FacebookConfigPage } from './pages/FacebookConfigPage';
import { JobHistoryPage } from './pages/JobHistoryPage';
import { SystemConfig } from './types';
import { api } from './services/api';

export function App() {
  const [activeTab, setActiveTab] = useState('dashboard');
  const [selectedProjectId, setSelectedProjectId] = useState<string>('proj-001');
  const [apiHealthy, setApiHealthy] = useState(false);
  const [config, setConfig] = useState<SystemConfig | null>(null);

  useEffect(() => {
    // Initial health check & configuration loading
    const checkStatus = async () => {
      const healthy = await api.getHealth();
      setApiHealthy(healthy);
      if (healthy) {
        const conf = await api.getConfigSummary();
        setConfig(conf);
      }
    };

    checkStatus();
    const interval = setInterval(checkStatus, 10000);
    return () => clearInterval(interval);
  }, []);

  const getPageTitle = () => {
    switch (activeTab) {
      case 'dashboard':
        return { title: 'Bảng điều khiển Studio', subtitle: 'Tổng quan hệ thống sản xuất video tin tức ngắn' };
      case 'create':
        return { title: 'Tạo video tin tức mới', subtitle: 'Chuyển đổi bài báo thành video 9:16' };
      case 'articles':
        return { title: 'Bài báo đã trích xuất', subtitle: 'Kho nội dung và hình ảnh bài báo' };
      case 'videos':
        return { title: 'Danh sách dự án Video', subtitle: 'Quản lý các bản dựng video Reels' };
      case 'detail':
        return { title: 'Chi tiết video & Phê duyệt', subtitle: 'Kiểm tra chất lượng và duyệt xuất bản' };
      case 'ai-config':
        return { title: 'Cấu hình AI Editor', subtitle: 'Mô hình ngôn ngữ Gemini & Claude' };
      case 'tts-config':
        return { title: 'Cấu hình giọng đọc (TTS)', subtitle: 'Giọng lồng tiếng tiếng Việt và ngắt nghỉ' };
      case 'facebook-config':
        return { title: 'Cấu hình Facebook Page', subtitle: 'Kết nối Meta Graph API & Reels' };
      case 'history':
        return { title: 'Lịch sử xử lý & Hàng đợi', subtitle: 'Nhật ký các tác vụ Worker' };
      default:
        return { title: 'AI News Studio', subtitle: '' };
    }
  };

  const { title, subtitle } = getPageTitle();

  return (
    <div className="flex bg-[#0B0F19] text-[#F9FAFB] min-h-screen">
      {/* Fixed Sidebar */}
      <Sidebar
        activeTab={activeTab === 'detail' ? 'videos' : activeTab}
        setActiveTab={setActiveTab}
        apiHealthy={apiHealthy}
      />

      {/* Main Content Area */}
      <div className="flex-1 ml-64 flex flex-col min-h-screen">
        <Header title={title} subtitle={subtitle} apiHealthy={apiHealthy} />

        <main className="p-8 flex-1">
          {activeTab === 'dashboard' && (
            <DashboardPage
              onNavigate={setActiveTab}
              config={config}
              apiHealthy={apiHealthy}
            />
          )}

          {activeTab === 'create' && (
            <CreateVideoPage
              onCreated={(id) => {
                setSelectedProjectId(id);
                setActiveTab('detail');
              }}
            />
          )}

          {activeTab === 'articles' && (
            <ArticlesPage
              onSelectArticle={() => {
                setActiveTab('create');
              }}
            />
          )}

          {activeTab === 'videos' && (
            <VideosPage
              onSelectProject={(id) => {
                setSelectedProjectId(id);
                setActiveTab('detail');
              }}
            />
          )}

          {activeTab === 'detail' && (
            <VideoDetailPage
              projectId={selectedProjectId}
              onBack={() => setActiveTab('videos')}
            />
          )}

          {activeTab === 'ai-config' && <AiConfigPage config={config} />}

          {activeTab === 'tts-config' && <TtsConfigPage config={config} />}

          {activeTab === 'facebook-config' && <FacebookConfigPage config={config} />}

          {activeTab === 'history' && <JobHistoryPage />}
        </main>
      </div>
    </div>
  );
}

export default App;
