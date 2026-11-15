import React, { useState } from 'react';
import { Film, Play, Eye, CheckCircle2, Clock, Share2, AlertTriangle } from 'lucide-react';
import { VideoProject, VideoProjectStatus } from '../types';

interface VideosPageProps {
  onSelectProject: (projectId: string) => void;
}

export const VideosPage: React.FC<VideosPageProps> = ({ onSelectProject }) => {
  const [filter, setFilter] = useState<string>('All');

  const mockProjects: VideoProject[] = [
    {
      id: 'proj-001',
      title: 'Metro số 1 chính thức đón khách: Dấu mốc giao thông TP.HCM',
      sourceUrl: 'https://vnexpress.net/tuyen-metro-so-1-ben-thanh-suoi-tien-van-hanh-4829102.html',
      publisher: 'VnExpress',
      status: 'AwaitingApproval',
      durationSeconds: 54,
      sceneCount: 7,
      createdAt: '15/11/2026 09:20',
      updatedAt: '15/11/2026 09:25',
      scenes: []
    },
    {
      id: 'proj-002',
      title: 'Việt Nam bứt phá trong chuỗi cung ứng bán dẫn toàn cầu',
      sourceUrl: 'https://tuoitre.vn/viet-nam-phat-trien-cong-nghe-ban-dan-20261114.htm',
      publisher: 'Tuổi Trẻ Online',
      status: 'Published',
      durationSeconds: 48,
      sceneCount: 6,
      createdAt: '14/11/2026 16:10',
      updatedAt: '14/11/2026 16:30',
      facebookPostId: 'fb_post_9812471',
      scenes: []
    },
    {
      id: 'proj-003',
      title: 'Dự báo thời tiết đợt không khí lạnh mới tràn về miền Bắc',
      sourceUrl: 'https://thanhnien.vn/du-bao-thoi-tiet-khong-khi-lanh-20261113.htm',
      publisher: 'Thanh Niên',
      status: 'Rendering',
      durationSeconds: 50,
      sceneCount: 6,
      createdAt: '15/11/2026 10:00',
      updatedAt: '15/11/2026 10:02',
      scenes: []
    }
  ];

  const getStatusBadge = (status: VideoProjectStatus) => {
    switch (status) {
      case 'AwaitingApproval':
        return (
          <span className="px-2.5 py-1 rounded-full text-xs font-semibold bg-amber-500/20 text-amber-300 border border-amber-500/30 flex items-center gap-1">
            <Clock className="w-3 h-3" /> Chờ duyệt
          </span>
        );
      case 'Published':
        return (
          <span className="px-2.5 py-1 rounded-full text-xs font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 flex items-center gap-1">
            <CheckCircle2 className="w-3 h-3" /> Đã đăng Reels
          </span>
        );
      case 'Rendering':
        return (
          <span className="px-2.5 py-1 rounded-full text-xs font-semibold bg-indigo-500/20 text-indigo-300 border border-indigo-500/30 flex items-center gap-1 animate-pulse">
            <Play className="w-3 h-3" /> Đang dựng FFmpeg
          </span>
        );
      case 'Failed':
        return (
          <span className="px-2.5 py-1 rounded-full text-xs font-semibold bg-rose-500/20 text-rose-300 border border-rose-500/30 flex items-center gap-1">
            <AlertTriangle className="w-3 h-3" /> Lỗi
          </span>
        );
      default:
        return (
          <span className="px-2.5 py-1 rounded-full text-xs font-semibold bg-gray-700 text-gray-300">
            {status}
          </span>
        );
    }
  };

  const filteredProjects = mockProjects.filter((p) => {
    if (filter === 'All') return true;
    return p.status === filter;
  });

  return (
    <div className="space-y-6 max-w-7xl mx-auto">
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">Danh sách dự án Video Reels</h1>
          <p className="text-sm text-gray-400 mt-1">
            Quản lý, xem trước chất lượng và phê duyệt video trước khi xuất bản lên Facebook Page.
          </p>
        </div>

        {/* Filter Tabs */}
        <div className="flex items-center space-x-1.5 p-1 rounded-xl bg-[#111827] border border-[#1F2937] text-xs">
          {['All', 'AwaitingApproval', 'Rendering', 'Published'].map((tab) => (
            <button
              key={tab}
              onClick={() => setFilter(tab)}
              className={`px-3 py-1.5 rounded-lg font-medium transition ${
                filter === tab
                  ? 'bg-indigo-600 text-white shadow'
                  : 'text-gray-400 hover:text-white'
              }`}
            >
              {tab === 'All' ? 'Tất cả' : tab === 'AwaitingApproval' ? 'Chờ duyệt' : tab === 'Rendering' ? 'Đang dựng' : 'Đã đăng'}
            </button>
          ))}
        </div>
      </div>

      {/* Grid of Video Projects */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {filteredProjects.map((proj) => (
          <div
            key={proj.id}
            className="glass-panel rounded-2xl border border-[#1F2937] hover:border-indigo-500/40 transition-all flex flex-col overflow-hidden group"
          >
            {/* 9:16 Mock Thumbnail Preview */}
            <div className="h-56 bg-gradient-to-b from-gray-900 to-[#0B0F19] relative flex items-center justify-center border-b border-[#1F2937] overflow-hidden">
              <div className="w-24 h-44 rounded-lg bg-black border border-gray-700 shadow-2xl relative overflow-hidden flex flex-col justify-between p-2">
                <span className="text-[7px] font-bold text-amber-300 uppercase tracking-wider bg-red-600 px-1 py-0.5 rounded self-start">
                  TIN NÓNG 9:16
                </span>
                <div className="text-center">
                  <Play className="w-6 h-6 text-white/80 mx-auto drop-shadow" />
                </div>
                <div className="text-[7px] text-white/90 bg-black/60 p-1 rounded backdrop-blur font-mono">
                  1080 × 1920
                </div>
              </div>

              {/* Status overlay badge */}
              <div className="absolute top-3 left-3">
                {getStatusBadge(proj.status)}
              </div>

              {/* Duration badge */}
              <div className="absolute bottom-3 right-3 px-2 py-0.5 rounded bg-black/80 text-white font-mono text-xs border border-gray-700">
                {proj.durationSeconds}s
              </div>
            </div>

            {/* Content Details */}
            <div className="p-5 flex-1 flex flex-col justify-between space-y-4">
              <div className="space-y-2">
                <div className="flex items-center space-x-2 text-[11px] text-gray-400">
                  <span className="font-semibold text-indigo-400">{proj.publisher}</span>
                  <span>•</span>
                  <span>{proj.sceneCount} cảnh</span>
                  <span>•</span>
                  <span>{proj.createdAt}</span>
                </div>
                <h3 className="font-bold text-sm text-white line-clamp-2 leading-snug">
                  {proj.title}
                </h3>
              </div>

              <div className="pt-2 border-t border-gray-800 flex items-center justify-between">
                <button
                  onClick={() => onSelectProject(proj.id)}
                  className="w-full py-2 rounded-xl bg-[#1F2937] hover:bg-indigo-600 hover:text-white text-gray-200 text-xs font-semibold flex items-center justify-center space-x-1.5 transition"
                >
                  <Eye className="w-3.5 h-3.5" />
                  <span>Xem chi tiết & Kiểm duyệt</span>
                </button>
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};
