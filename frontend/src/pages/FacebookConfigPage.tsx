import React, { useState, useEffect } from 'react';
import {
  Share2,
  Shield,
  CheckCircle2,
  AlertTriangle,
  Key,
  ExternalLink,
  Plus,
  Trash2,
  RefreshCw,
  CheckCircle,
  AlertCircle
} from 'lucide-react';
import { api, FacebookAccountDto } from '../services/api';
import { SystemConfig } from '../types';

interface FacebookConfigPageProps {
  config: SystemConfig | null;
}

export const FacebookConfigPage: React.FC<FacebookConfigPageProps> = ({ config }) => {
  const [pages, setPages] = useState<FacebookAccountDto[]>([]);
  const [loading, setLoading] = useState<boolean>(true);

  // New Page Form State
  const [pageId, setPageId] = useState('');
  const [pageName, setPageName] = useState('');
  const [pageAccessToken, setPageAccessToken] = useState('');
  const [graphVersion, setGraphVersion] = useState('v21.0');

  const [isVerifying, setIsVerifying] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [verifyResult, setVerifyResult] = useState<{ isValid: boolean; message: string } | null>(null);

  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  useEffect(() => {
    loadPages();
  }, []);

  const loadPages = async () => {
    setLoading(true);
    try {
      const data = await api.getFacebookPages();
      setPages(data);
    } catch {
      // ignore
    } finally {
      setLoading(false);
    }
  };

  const handleVerify = async () => {
    if (!pageAccessToken.trim()) {
      alert('Vui lòng nhập Page Access Token trước khi kiểm tra.');
      return;
    }
    setIsVerifying(true);
    setVerifyResult(null);
    try {
      const result = await api.verifyFacebookToken(pageAccessToken);
      if (result.isValid) {
        setVerifyResult({
          isValid: true,
          message: `Token hợp lệ! Trang: ${result.pageName || 'Facebook Page'} (ID: ${result.pageId || pageId})`
        });
        if (result.pageId && !pageId) setPageId(result.pageId);
        if (result.pageName && !pageName) setPageName(result.pageName);
      } else {
        setVerifyResult({
          isValid: false,
          message: result.errorMessage || 'Token không hợp lệ hoặc đã hết hạn.'
        });
      }
    } catch (err: any) {
      setVerifyResult({
        isValid: false,
        message: err.message || 'Lỗi kết nối tới Meta Graph API.'
      });
    } finally {
      setIsVerifying(false);
    }
  };

  const handleConnectPage = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!pageId.trim() || !pageAccessToken.trim()) {
      alert('Vui lòng nhập đầy đủ Page ID và Page Access Token.');
      return;
    }
    setIsSaving(true);
    setErrorMsg(null);
    setSuccessMsg(null);
    try {
      await api.connectFacebookPage({
        pageId: pageId.trim(),
        pageName: pageName.trim() || 'AI News Studio Fanpage',
        pageAccessToken: pageAccessToken.trim()
      });
      setSuccessMsg('Kết nối Facebook Page thành công! Token đã được mã hóa an toàn.');
      setPageId('');
      setPageName('');
      setPageAccessToken('');
      setVerifyResult(null);
      await loadPages();
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi khi lưu kết nối Fanpage');
    } finally {
      setIsSaving(false);
    }
  };

  const handleDisconnect = async (id: string, name: string) => {
    if (!confirm(`Bạn có chắc muốn ngắt kết nối Facebook Page "${name}" không?`)) return;
    try {
      await api.disconnectFacebookPage(id);
      setSuccessMsg(`Đã ngắt kết nối trang ${name}.`);
      await loadPages();
    } catch (err: any) {
      setErrorMsg(err.message || 'Lỗi ngắt kết nối');
    }
  };

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white tracking-tight">Cấu hình kết nối Facebook Page (Meta Graph API)</h1>
        <p className="text-sm text-gray-400 mt-1">
          Quản lý kết nối Fanpage, quản trị token an toàn và quyền xuất bản Reels thông qua Meta Graph API chính thức.
        </p>
      </div>

      {/* Safety Notice */}
      <div className="p-4 rounded-xl bg-blue-950/20 border border-blue-500/30 text-xs text-blue-200 flex items-start space-x-3">
        <Shield className="w-5 h-5 text-blue-400 shrink-0 mt-0.5" />
        <div className="space-y-1">
          <p className="font-semibold text-blue-300">Nguyên tắc an toàn bảo mật Token (Master Plan):</p>
          <p className="text-blue-200/80 leading-relaxed">
            Hệ thống <strong>không ghi log Page Access Token</strong>, <strong>không trả token thô về frontend</strong> (chỉ trả về masked token dạng <code className="text-indigo-300">EAAB••••••••1234</code>).
            Token được mã hóa bằng chuẩn AES-256 trước khi lưu vào PostgreSQL và chỉ được giải mã tại Worker trong bộ nhớ tạm thời khi thực hiện upload Reels.
          </p>
        </div>
      </div>

      {errorMsg && (
        <div className="p-3.5 rounded-xl bg-rose-500/10 border border-rose-500/30 text-rose-300 text-xs flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>{errorMsg}</span>
        </div>
      )}

      {successMsg && (
        <div className="p-3.5 rounded-xl bg-emerald-500/10 border border-emerald-500/30 text-emerald-300 text-xs flex items-center gap-2">
          <CheckCircle className="w-4 h-4 shrink-0" />
          <span>{successMsg}</span>
        </div>
      )}

      {/* Connected Pages List */}
      <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-semibold text-white flex items-center gap-2">
            <Share2 className="w-4 h-4 text-blue-400" />
            <span>Danh sách Facebook Page đã kết nối ({pages.length})</span>
          </h2>
          <button
            onClick={loadPages}
            className="p-1.5 rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs transition"
            title="Làm mới"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          </button>
        </div>

        {pages.length === 0 ? (
          <div className="p-6 text-center text-xs text-gray-500 bg-[#0B0F19] rounded-xl border border-gray-800">
            Chưa có Facebook Page nào được kết nối. Vui lòng nhập thông tin phía dưới để kết nối Fanpage xuất bản Reels.
          </div>
        ) : (
          <div className="space-y-3">
            {pages.map((p) => (
              <div
                key={p.id}
                className="p-4 rounded-xl bg-[#0B0F19] border border-gray-800 flex flex-wrap items-center justify-between gap-3 text-xs"
              >
                <div className="space-y-1">
                  <div className="flex items-center gap-2">
                    <span className="font-bold text-white text-sm">{p.pageName}</span>
                    <span className={`px-2 py-0.5 rounded text-[10px] font-bold ${
                      p.isActive ? 'bg-emerald-500/20 text-emerald-300' : 'bg-gray-800 text-gray-500'
                    }`}>
                      {p.isActive ? 'Đang hoạt động' : 'Đã ngắt'}
                    </span>
                  </div>
                  <div className="flex flex-wrap items-center gap-3 text-gray-400 font-mono text-[11px]">
                    <span>Page ID: {p.pageId}</span>
                    <span>•</span>
                    <span>Token: {p.maskedToken || '••••••••'}</span>
                    <span>•</span>
                    <span>Mã hóa: AES-256 ✓</span>
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    onClick={() => handleDisconnect(p.id, p.pageName)}
                    className="px-3 py-1.5 rounded-lg bg-rose-500/10 hover:bg-rose-500/20 text-rose-300 text-xs font-semibold flex items-center gap-1 transition"
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                    <span>Ngắt kết nối</span>
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Connect New Page Form */}
      <form onSubmit={handleConnectPage} className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
        <div className="flex items-center justify-between">
          <label className="text-sm font-semibold text-white flex items-center gap-2">
            <Plus className="w-4 h-4 text-emerald-400" />
            <span>Kết nối Fanpage mới (Meta Reels Publisher)</span>
          </label>
          <span className="text-[11px] font-mono text-gray-400">Graph API {graphVersion}</span>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-xs">
          <div>
            <label className="text-gray-400 block mb-1">Facebook Page ID *</label>
            <input
              type="text"
              value={pageId}
              onChange={(e) => setPageId(e.target.value)}
              placeholder="Ví dụ: 100200300400500"
              required
              className="w-full px-3 py-2.5 rounded-xl bg-[#0B0F19] border border-[#1F2937] text-white font-mono focus:outline-none focus:border-indigo-500"
            />
          </div>

          <div>
            <label className="text-gray-400 block mb-1">Tên Fanpage</label>
            <input
              type="text"
              value={pageName}
              onChange={(e) => setPageName(e.target.value)}
              placeholder="Ví dụ: Báo Điện Tử 24/7"
              className="w-full px-3 py-2.5 rounded-xl bg-[#0B0F19] border border-[#1F2937] text-white focus:outline-none focus:border-indigo-500"
            />
          </div>
        </div>

        <div className="text-xs space-y-1.5">
          <label className="text-gray-400 block flex items-center gap-1.5">
            <Key className="w-3.5 h-3.5 text-amber-400" />
            <span>Page Access Token (Sẽ được mã hóa AES-256) *</span>
          </label>
          <input
            type="password"
            value={pageAccessToken}
            onChange={(e) => setPageAccessToken(e.target.value)}
            placeholder="EAAB..."
            required
            className="w-full px-3 py-2.5 rounded-xl bg-[#0B0F19] border border-[#1F2937] text-white font-mono focus:outline-none focus:border-indigo-500"
          />
          <p className="text-[10px] text-gray-500">
            Token lấy từ Meta Developer Portal với các quyền: pages_manage_posts, pages_read_engagement, publish_video.
          </p>
        </div>

        {verifyResult && (
          <div className={`p-3 rounded-xl border text-xs flex items-center gap-2 ${
            verifyResult.isValid ? 'bg-emerald-500/10 border-emerald-500/30 text-emerald-300' : 'bg-rose-500/10 border-rose-500/30 text-rose-300'
          }`}>
            {verifyResult.isValid ? <CheckCircle className="w-4 h-4 shrink-0" /> : <AlertTriangle className="w-4 h-4 shrink-0" />}
            <span>{verifyResult.message}</span>
          </div>
        )}

        <div className="pt-2 flex items-center justify-between">
          <button
            type="button"
            onClick={handleVerify}
            disabled={isVerifying}
            className="px-4 py-2 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-300 font-semibold text-xs flex items-center gap-1.5 transition disabled:opacity-50"
          >
            <Shield className="w-3.5 h-3.5 text-blue-400" />
            <span>{isVerifying ? 'Đang kiểm tra token...' : 'Kiểm tra token với Meta'}</span>
          </button>

          <button
            type="submit"
            disabled={isSaving}
            className="px-5 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs flex items-center gap-1.5 transition disabled:opacity-50 shadow-lg shadow-indigo-600/20"
          >
            <Plus className="w-3.5 h-3.5" />
            <span>{isSaving ? 'Đang lưu kết nối...' : 'Lưu & Kết nối Page'}</span>
          </button>
        </div>
      </form>

      {/* Required Permissions Checklist */}
      <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-3">
        <h3 className="text-xs font-bold uppercase tracking-wider text-gray-400">
          Danh sách quyền Meta Graph API yêu cầu cho Reels
        </h3>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
          {[
            { perm: 'pages_show_list', desc: 'Lấy danh sách các trang Facebook người dùng quản lý' },
            { perm: 'pages_read_engagement', desc: 'Đọc số liệu tương tác và lượt xem Reels từ Page Insights' },
            { perm: 'pages_manage_posts', desc: 'Khởi tạo session và đăng tải video Reels lên Page' },
            { perm: 'read_insights', desc: 'Thu thập chỉ số phân tích hiệu quả video (Views, Reach, Engagement)' },
          ].map((p) => (
            <div key={p.perm} className="p-3 rounded-xl bg-[#0B0F19] border border-gray-800 flex items-start space-x-2">
              <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0 mt-0.5" />
              <div>
                <span className="font-mono text-indigo-300 font-semibold">{p.perm}</span>
                <p className="text-[11px] text-gray-400">{p.desc}</p>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
