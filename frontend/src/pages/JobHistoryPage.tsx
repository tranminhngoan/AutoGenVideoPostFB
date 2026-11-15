import React, { useState } from 'react';
import { Clock, RefreshCw, CheckCircle2, AlertCircle, Play, Layers, HardDrive } from 'lucide-react';
import { JobHistoryItem, VideoProjectStatus } from '../types';

export const JobHistoryPage: React.FC = () => {
  const [jobs, setJobs] = useState<JobHistoryItem[]>([
    {
      id: 'job-9841',
      projectId: 'proj-001',
      projectTitle: 'Metro số 1 chính thức đón khách: Dấu mốc giao thông TP.HCM',
      status: 'AwaitingApproval',
      startedAt: '15/11/2026 09:20:12',
      completedAt: '15/11/2026 09:25:04',
      durationMs: 292000,
      retryCount: 0,
      lastMessage: 'Video 1080x1920 rendered successfully. Awaiting user editorial approval.'
    },
    {
      id: 'job-9840',
      projectId: 'proj-002',
      projectTitle: 'Việt Nam bứt phá trong chuỗi cung ứng bán dẫn toàn cầu',
      status: 'Published',
      startedAt: '14/11/2026 16:10:00',
      completedAt: '14/11/2026 16:30:15',
      durationMs: 1215000,
      retryCount: 0,
      lastMessage: 'Published to Facebook Reels via Graph API. Temp files cleaned up.'
    },
    {
      id: 'job-9839',
      projectId: 'proj-003',
      projectTitle: 'Dự báo thời tiết đợt không khí lạnh mới',
      status: 'Rendering',
      startedAt: '15/11/2026 10:00:00',
      durationMs: 45000,
      retryCount: 0,
      lastMessage: 'FFmpeg filter complex rendering scenes 3/6...'
    }
  ]);

  const handleRetry = (jobId: string) => {
    alert(`Đã gửi yêu cầu thử lại cho Job ${jobId}`);
  };

  return (
    <div className="max-w-7xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">Lịch sử tác vụ & Hàng đợi Hangfire</h1>
          <p className="text-sm text-gray-400 mt-1">
            Theo dõi tiến trình xử lý bất đồng bộ của Worker Service và nhật ký vòng đời các tác vụ.
          </p>
        </div>
      </div>

      {/* Lifecycle Diagram */}
      <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-3">
        <h3 className="text-xs font-bold uppercase tracking-wider text-gray-400 flex items-center gap-2">
          <Layers className="w-4 h-4 text-indigo-400" />
          Vòng đời trạng thái Job chuẩn (14 Lifecycle States)
        </h3>
        <div className="flex flex-wrap items-center gap-2 text-[11px] font-mono">
          {[
            'Pending',
            'Extracting',
            'Extracted',
            'Analyzing',
            'Scripting',
            'PlanningScenes',
            'GeneratingAudio',
            'Rendering',
            'QualityChecking',
            'AwaitingApproval',
            'Publishing',
            'Published'
          ].map((st, i) => (
            <React.Fragment key={st}>
              <span className="px-2.5 py-1 rounded-lg bg-[#111827] border border-gray-800 text-gray-300">
                {st}
              </span>
              {i < 11 && <span className="text-indigo-500">→</span>}
            </React.Fragment>
          ))}
        </div>
      </div>

      {/* Ephemeral Cleanup Monitor */}
      <div className="p-4 rounded-xl bg-[#111827] border border-gray-800 flex items-center justify-between text-xs">
        <div className="flex items-center space-x-3">
          <HardDrive className="w-5 h-5 text-emerald-400" />
          <div>
            <p className="font-semibold text-white">Chính sách lưu trữ file tạm (/tmp/newsstudio)</p>
            <p className="text-gray-400">
              Chỉ lưu file tạm trong quá trình render. MP4, audio và ảnh tạm được dọn dẹp sau khi đăng thành công hoặc hết hạn xem trước.
            </p>
          </div>
        </div>
        <span className="px-2.5 py-1 rounded-full text-[10px] font-bold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
          Clean Job Active
        </span>
      </div>

      {/* Jobs Table */}
      <div className="glass-panel rounded-2xl border border-[#1F2937] overflow-hidden">
        <div className="p-4 border-b border-[#1F2937] flex items-center justify-between text-xs font-semibold text-gray-400">
          <span>Danh sách tác vụ gần đây</span>
          <span>{jobs.length} tác vụ</span>
        </div>
        <div className="divide-y divide-gray-800">
          {jobs.map((job) => (
            <div key={job.id} className="p-4 hover:bg-[#111827]/60 transition flex flex-col md:flex-row md:items-center justify-between gap-4">
              <div className="space-y-1">
                <div className="flex items-center space-x-2 text-xs">
                  <span className="font-mono text-indigo-400 font-bold">{job.id}</span>
                  <span className="text-gray-500">•</span>
                  <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-gray-800 text-gray-300">
                    {job.status}
                  </span>
                  <span className="text-gray-500">•</span>
                  <span className="text-gray-400">{job.startedAt}</span>
                </div>
                <h4 className="font-bold text-sm text-white">{job.projectTitle}</h4>
                <p className="text-xs text-gray-400 font-mono truncate">{job.lastMessage}</p>
              </div>

              <div className="flex items-center space-x-3 shrink-0">
                <span className="text-xs font-mono text-gray-400">
                  {Math.round(job.durationMs / 1000)}s
                </span>
                <button
                  onClick={() => handleRetry(job.id)}
                  className="p-2 rounded-lg bg-[#1F2937] hover:bg-[#374151] text-gray-300 hover:text-white transition"
                  title="Thử lại tác vụ"
                >
                  <RefreshCw className="w-3.5 h-3.5" />
                </button>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
