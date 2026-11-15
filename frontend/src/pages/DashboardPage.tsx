import React, { useEffect, useState } from 'react';
import {
  Film,
  PlusCircle,
  Play,
  CheckCircle2,
  AlertCircle,
  Cpu,
  Layers,
  Sparkles,
  ArrowRight,
  HardDrive,
  Database,
  DollarSign,
  Trash2,
  RefreshCw,
  Activity,
  Server
} from 'lucide-react';
import { SystemConfig } from '../types';
import { api, SystemMetricsDto, SystemCostSummaryDto, StorageCleanupReportDto, JobRecoveryReportDto } from '../services/api';

interface DashboardPageProps {
  onNavigate: (tab: string) => void;
  config: SystemConfig | null;
  apiHealthy: boolean;
}

export const DashboardPage: React.FC<DashboardPageProps> = ({ onNavigate, config, apiHealthy }) => {
  const [metrics, setMetrics] = useState<SystemMetricsDto | null>(null);
  const [costs, setCosts] = useState<SystemCostSummaryDto | null>(null);
  const [loadingMetrics, setLoadingMetrics] = useState(false);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [actionLoading, setActionLoading] = useState(false);

  const fetchMetricsAndCosts = async () => {
    setLoadingMetrics(true);
    try {
      const [m, c] = await Promise.all([
        api.getSystemMetrics().catch(() => null),
        api.getSystemCosts().catch(() => null)
      ]);
      setMetrics(m);
      setCosts(c);
    } catch {
      // Ignore
    } finally {
      setLoadingMetrics(false);
    }
  };

  useEffect(() => {
    fetchMetricsAndCosts();
    const interval = setInterval(fetchMetricsAndCosts, 30000); // 30s auto-refresh
    return () => clearInterval(interval);
  }, []);

  const handleCleanupStorage = async () => {
    setActionLoading(true);
    setActionMessage(null);
    try {
      const rep: StorageCleanupReportDto = await api.triggerStorageCleanup();
      setActionMessage(`Đã dọn dẹp ${rep.expiredAssetsCleanedCount} tệp tin hết hạn, giải phóng ${rep.totalBytesFreedMb} MB. Bộ nhớ hiện tại: ${rep.currentStorageUsedMb} MB.`);
      fetchMetricsAndCosts();
    } catch (e: any) {
      setActionMessage(`Lỗi dọn dẹp: ${e.message}`);
    } finally {
      setActionLoading(false);
    }
  };

  const handleRecoverJobs = async () => {
    setActionLoading(true);
    setActionMessage(null);
    try {
      const rep: JobRecoveryReportDto = await api.triggerJobRecovery();
      setActionMessage(`Kiểm toán xong ${rep.totalDanglingProjectsExamined} dự án dang dở. Khôi phục về duyệt: ${rep.recoveredToAwaitingApprovalCount}, Đặt lại chờ: ${rep.resetToPendingCount}.`);
      fetchMetricsAndCosts();
    } catch (e: any) {
      setActionMessage(`Lỗi phục hồi: ${e.message}`);
    } finally {
      setActionLoading(false);
    }
  };

  return (
    <div className="space-y-8 max-w-7xl mx-auto">
      {/* Welcome Banner */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-r from-indigo-900/60 via-purple-900/40 to-[#111827] border border-indigo-500/20 p-8 shadow-2xl">
        <div className="relative z-10 max-w-2xl space-y-4">
          <div className="inline-flex items-center space-x-2 px-3 py-1 rounded-full bg-indigo-500/20 border border-indigo-400/30 text-indigo-300 text-xs font-semibold">
            <Sparkles className="w-3.5 h-3.5" />
            <span>AI News Video Studio Engine • Production Ready</span>
          </div>
          <h1 className="text-3xl font-extrabold text-white tracking-tight leading-tight">
            Chuyển bài báo thành video Reels chuyên nghiệp trong 60 giây
          </h1>
          <p className="text-sm text-gray-300 leading-relaxed">
            Hệ thống tự động đọc báo, trích xuất ảnh thực tế, viết kịch bản biên tập tin tức,
            lồng tiếng tiếng Việt và render video dọc 9:16 (1080×1920) sẵn sàng đăng Facebook Reels.
          </p>
          <div className="pt-2 flex items-center space-x-4">
            <button
              onClick={() => onNavigate('create')}
              className="inline-flex items-center space-x-2 px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-sm shadow-lg shadow-indigo-600/30 transition-all hover:scale-[1.02]"
            >
              <PlusCircle className="w-4 h-4" />
              <span>Tạo video mới ngay</span>
              <ArrowRight className="w-4 h-4" />
            </button>
            <button
              onClick={() => onNavigate('videos')}
              className="inline-flex items-center space-x-2 px-4 py-2.5 rounded-xl bg-[#1F2937] hover:bg-[#374151] text-gray-200 font-medium text-sm border border-gray-700 transition"
            >
              <Film className="w-4 h-4" />
              <span>Xem danh sách video</span>
            </button>
          </div>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-5">
        <div className="glass-panel p-5 rounded-xl border border-[#1F2937]">
          <div className="flex items-center justify-between text-gray-400 mb-3">
            <span className="text-xs font-semibold uppercase tracking-wider">Tổng dự án video</span>
            <Film className="w-4 h-4 text-indigo-400" />
          </div>
          <div className="text-2xl font-bold text-white">{costs?.totalProjectsCount ?? 0}</div>
          <p className="text-xs text-indigo-300 mt-2 flex items-center gap-1 font-mono">
            <span>Đã đăng Reels: {costs?.publishedProjectsCount ?? 0}</span>
          </p>
        </div>

        <div className="glass-panel p-5 rounded-xl border border-[#1F2937]">
          <div className="flex items-center justify-between text-gray-400 mb-3">
            <span className="text-xs font-semibold uppercase tracking-wider">Thời lượng video</span>
            <Play className="w-4 h-4 text-purple-400" />
          </div>
          <div className="text-2xl font-bold text-white">45–60s</div>
          <p className="text-xs text-gray-400 mt-2 font-mono">
            Chuẩn mục tiêu Facebook Reels
          </p>
        </div>

        <div className="glass-panel p-5 rounded-xl border border-[#1F2937]">
          <div className="flex items-center justify-between text-gray-400 mb-3">
            <span className="text-xs font-semibold uppercase tracking-wider">Chi phí AI & TTS TB</span>
            <DollarSign className="w-4 h-4 text-emerald-400" />
          </div>
          <div className="text-2xl font-bold text-white">
            ${costs ? costs.averageCostPerProjectUsd.toFixed(4) : '0.0000'}
          </div>
          <p className="text-xs text-emerald-400 mt-2">
            Tổng: ${costs ? costs.totalCostUsd.toFixed(4) : '0.00'} ({costs?.totalTokensUsed.toLocaleString() || 0} tokens)
          </p>
        </div>

        <div className="glass-panel p-5 rounded-xl border border-[#1F2937]">
          <div className="flex items-center justify-between text-gray-400 mb-3">
            <span className="text-xs font-semibold uppercase tracking-wider">Hạ tầng Pipeline</span>
            <Cpu className="w-4 h-4 text-amber-400" />
          </div>
          <div className="text-2xl font-bold text-white flex items-center gap-2">
            <span>{apiHealthy ? 'Online' : 'Checking'}</span>
            <span className={`w-2.5 h-2.5 rounded-full ${apiHealthy ? 'bg-emerald-500 animate-pulse' : 'bg-amber-500'}`} />
          </div>
          <p className="text-xs text-gray-400 mt-2 font-mono truncate">
            {metrics?.environment || config?.environment || 'Production'} mode
          </p>
        </div>
      </div>

      {/* Production System Monitoring & Resources */}
      <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-5">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Activity className="w-5 h-5 text-indigo-400" />
            <h3 className="text-sm font-bold uppercase tracking-wider text-gray-200">
              Chỉ số vận hành hệ thống (Railway Production Telemetry)
            </h3>
          </div>
          <button
            onClick={fetchMetricsAndCosts}
            disabled={loadingMetrics}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-[#1F2937] hover:bg-[#374151] text-xs text-gray-300 font-medium transition"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loadingMetrics ? 'animate-spin' : ''}`} />
            <span>Làm mới</span>
          </button>
        </div>

        {actionMessage && (
          <div className="p-3 rounded-lg bg-indigo-950/40 border border-indigo-500/30 text-xs text-indigo-200">
            {actionMessage}
          </div>
        )}

        <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
          {/* CPU & Memory */}
          <div className="p-4 rounded-xl bg-[#111827] border border-gray-800 space-y-2">
            <div className="flex items-center justify-between text-xs text-gray-400">
              <span className="flex items-center gap-1.5">
                <Cpu className="w-4 h-4 text-blue-400" /> Bộ nhớ RAM
              </span>
              <span className="font-mono text-white font-bold">{metrics?.memoryWorkingSetMb ?? 0} MB</span>
            </div>
            <div className="flex items-center justify-between text-xs text-gray-400 pt-1">
              <span className="flex items-center gap-1.5">
                <Server className="w-4 h-4 text-cyan-400" /> Thời gian Uptime
              </span>
              <span className="font-mono text-gray-300">{metrics?.uptime ? metrics.uptime.substring(0, 8) : '00:00:00'}</span>
            </div>
          </div>

          {/* Database Health */}
          <div className="p-4 rounded-xl bg-[#111827] border border-gray-800 space-y-2">
            <div className="flex items-center justify-between text-xs text-gray-400">
              <span className="flex items-center gap-1.5">
                <Database className="w-4 h-4 text-emerald-400" /> PostgreSQL 16
              </span>
              <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-emerald-500/20 text-emerald-300">
                {metrics?.databaseStatus || 'Connected'}
              </span>
            </div>
            <div className="flex items-center justify-between text-xs text-gray-400 pt-1">
              <span>Độ trễ phản hồi:</span>
              <span className="font-mono text-white font-semibold">{metrics?.databaseLatencyMs ?? 0} ms</span>
            </div>
          </div>

          {/* Ephemeral Temp Storage */}
          <div className="p-4 rounded-xl bg-[#111827] border border-gray-800 space-y-2 md:col-span-2">
            <div className="flex items-center justify-between text-xs text-gray-400">
              <span className="flex items-center gap-1.5">
                <HardDrive className="w-4 h-4 text-amber-400" /> Bộ nhớ tạm (Ephemeral Storage)
              </span>
              <span className="font-mono text-white font-bold">
                {metrics?.tempStorageUsedMb ?? 0} MB / {metrics?.tempStorageLimitMb ?? 5120} MB ({metrics?.tempStorageUsagePercent ?? 0}%)
              </span>
            </div>
            {/* Progress Bar */}
            <div className="w-full h-2 rounded-full bg-gray-800 overflow-hidden">
              <div
                className={`h-full transition-all duration-500 ${
                  (metrics?.tempStorageUsagePercent ?? 0) > 80 ? 'bg-rose-500' : 'bg-indigo-500'
                }`}
                style={{ width: `${Math.min(100, metrics?.tempStorageUsagePercent ?? 0)}%` }}
              />
            </div>
            <div className="flex items-center justify-between text-[11px] text-gray-400 pt-1">
              <span className="truncate max-w-[250px]">Đường dẫn: {metrics?.tempStoragePath || '/tmp/newsstudio'}</span>
              <div className="flex items-center gap-2">
                <button
                  onClick={handleCleanupStorage}
                  disabled={actionLoading}
                  className="inline-flex items-center gap-1 px-2.5 py-1 rounded bg-amber-500/20 hover:bg-amber-500/30 text-amber-300 text-[11px] font-medium border border-amber-500/30 transition"
                  title="Dọn dẹp các tệp tạm hết hạn TTL"
                >
                  <Trash2 className="w-3 h-3" />
                  <span>Dọn dẹp đĩa</span>
                </button>
                <button
                  onClick={handleRecoverJobs}
                  disabled={actionLoading}
                  className="inline-flex items-center gap-1 px-2.5 py-1 rounded bg-indigo-500/20 hover:bg-indigo-500/30 text-indigo-300 text-[11px] font-medium border border-indigo-500/30 transition"
                  title="Rà soát các job bị gián đoạn do container restart"
                >
                  <CheckCircle2 className="w-3 h-3" />
                  <span>Kiểm toán Job</span>
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Visual Pipeline Flow */}
      <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
        <h3 className="text-sm font-bold uppercase tracking-wider text-gray-400 flex items-center gap-2">
          <Layers className="w-4 h-4 text-indigo-400" />
          Quy trình sản xuất tự động 9:16 Reels (Khép kín 7 bước)
        </h3>
        <div className="grid grid-cols-2 md:grid-cols-7 gap-3 text-center">
          {[
            { step: '1', title: 'URL Báo', desc: 'Dán link bài báo', color: 'border-blue-500/40 text-blue-400' },
            { step: '2', title: 'Ảnh Trong Bài', desc: 'Chỉ ảnh bài báo', color: 'border-indigo-500/40 text-indigo-400' },
            { step: '3', title: 'AI Biên tập', desc: 'Kịch bản 45-60s', color: 'border-purple-500/40 text-purple-400' },
            { step: '4', title: 'TTS Tiếng Việt', desc: 'Hoài My / Nam Minh', color: 'border-pink-500/40 text-pink-400' },
            { step: '5', title: 'FFmpeg 9:16', desc: 'Dựng dọc 1080x1920', color: 'border-amber-500/40 text-amber-400' },
            { step: '6', title: 'Kiểm duyệt QC', desc: '10 tiêu chí rà soát', color: 'border-emerald-500/40 text-emerald-400' },
            { step: '7', title: 'Meta Reels', desc: 'Đăng Page tự động', color: 'border-cyan-500/40 text-cyan-400' },
          ].map((s) => (
            <div key={s.step} className={`p-3 rounded-xl bg-[#111827] border ${s.color} space-y-1`}>
              <div className="w-5 h-5 mx-auto rounded-full bg-gray-800 text-xs font-mono font-bold flex items-center justify-center">
                {s.step}
              </div>
              <p className="text-xs font-bold text-white">{s.title}</p>
              <p className="text-[11px] text-gray-400">{s.desc}</p>
            </div>
          ))}
        </div>
      </div>

      {/* Strict Policy Box */}
      <div className="p-4 rounded-xl bg-amber-950/20 border border-amber-500/30 flex items-start space-x-3 text-amber-200 text-xs">
        <AlertCircle className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
        <div className="space-y-1">
          <p className="font-semibold text-amber-300">Quy tắc nghiệp vụ cố định:</p>
          <p className="text-amber-200/80 leading-relaxed">
            Hệ thống chỉ trích xuất hình ảnh nằm trong chính bài báo. Tuyệt đối không tìm ảnh ngoài Internet,
            không dùng ảnh AI, không tạo khuôn mặt giả. Không lưu MP4/ảnh/audio lâu dài (tự động xóa sau khi xử lý).
            Tất cả video phải được người dùng duyệt trước khi xuất bản lên Facebook Page.
          </p>
        </div>
      </div>
    </div>
  );
};
