import React from 'react';
import {
  LayoutDashboard,
  PlusCircle,
  Newspaper,
  Film,
  Bot,
  Volume2,
  Share2,
  Clock,
  Sparkles,
  ExternalLink
} from 'lucide-react';

interface SidebarProps {
  activeTab: string;
  setActiveTab: (tab: string) => void;
  apiHealthy: boolean;
}

export const Sidebar: React.FC<SidebarProps> = ({ activeTab, setActiveTab, apiHealthy }) => {
  const navItems = [
    { id: 'dashboard', label: 'Dashboard', icon: LayoutDashboard },
    { id: 'create', label: 'Tạo video mới', icon: PlusCircle, highlight: true },
    { id: 'articles', label: 'Danh sách bài báo', icon: Newspaper },
    { id: 'videos', label: 'Danh sách video', icon: Film },
    { id: 'ai-config', label: 'Cấu hình AI', icon: Bot },
    { id: 'tts-config', label: 'Cấu hình TTS', icon: Volume2 },
    { id: 'facebook-config', label: 'Cấu hình Facebook', icon: Share2 },
    { id: 'history', label: 'Lịch sử xử lý', icon: Clock },
  ];

  return (
    <aside className="w-64 bg-[#0B0F19] border-r border-[#1F2937] flex flex-col h-screen fixed left-0 top-0 z-30 select-none">
      {/* Brand Logo */}
      <div className="p-5 border-b border-[#1F2937] flex items-center justify-between">
        <div className="flex items-center space-x-3">
          <div className="w-9 h-9 rounded-xl bg-gradient-to-tr from-indigo-600 via-indigo-500 to-purple-500 flex items-center justify-center shadow-lg shadow-indigo-500/20">
            <Sparkles className="w-5 h-5 text-white animate-pulse" />
          </div>
          <div>
            <h1 className="font-bold text-base tracking-tight text-white flex items-center gap-1.5">
              AI News Studio
            </h1>
            <p className="text-[11px] font-medium text-indigo-400">Reels 9:16 Automation</p>
          </div>
        </div>
      </div>

      {/* Navigation Links */}
      <nav className="flex-1 p-3 space-y-1 overflow-y-auto">
        <div className="px-3 py-2 text-[10px] font-semibold tracking-wider uppercase text-gray-500">
          Studio Workflow
        </div>
        {navItems.map((item) => {
          const Icon = item.icon;
          const isActive = activeTab === item.id;
          return (
            <button
              key={item.id}
              onClick={() => setActiveTab(item.id)}
              className={`w-full flex items-center space-x-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-all duration-150 ${
                isActive
                  ? 'bg-indigo-600/15 text-indigo-400 border border-indigo-500/30 shadow-sm'
                  : 'text-gray-400 hover:text-gray-200 hover:bg-[#111827]'
              } ${item.highlight && !isActive ? 'text-indigo-300 hover:text-indigo-200 bg-indigo-950/20' : ''}`}
            >
              <Icon className={`w-4 h-4 ${isActive ? 'text-indigo-400' : 'text-gray-400'}`} />
              <span className="flex-1 text-left">{item.label}</span>
              {item.highlight && (
                <span className="px-1.5 py-0.5 text-[10px] font-semibold bg-indigo-500/20 text-indigo-300 rounded border border-indigo-500/30">
                  New
                </span>
              )}
            </button>
          );
        })}
      </nav>

      {/* System Status Footer */}
      <div className="p-3 border-t border-[#1F2937] bg-[#0E1322]">
        <div className="glass-panel p-3 rounded-lg">
          <div className="flex items-center justify-between text-xs">
            <span className="text-gray-400">Backend API</span>
            <div className="flex items-center space-x-1.5">
              <span className={`w-2 h-2 rounded-full ${apiHealthy ? 'bg-emerald-500 animate-ping' : 'bg-amber-500'}`} />
              <span className={`font-mono text-[11px] font-semibold ${apiHealthy ? 'text-emerald-400' : 'text-amber-400'}`}>
                {apiHealthy ? 'ONLINE' : 'CONNECTING'}
              </span>
            </div>
          </div>
          <div className="mt-2 pt-2 border-t border-gray-800/80 flex items-center justify-between text-[11px] text-gray-400">
            <span>Swagger API</span>
            <a
              href="http://localhost:8080/swagger"
              target="_blank"
              rel="noreferrer"
              className="text-indigo-400 hover:text-indigo-300 flex items-center gap-1 font-mono text-[10px]"
            >
              /swagger <ExternalLink className="w-2.5 h-2.5" />
            </a>
          </div>
        </div>
      </div>
    </aside>
  );
};
