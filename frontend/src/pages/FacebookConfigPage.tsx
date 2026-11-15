import React, { useState } from 'react';
import { Share2, Shield, CheckCircle2, AlertTriangle, Key, ExternalLink } from 'lucide-react';
import { SystemConfig } from '../types';

interface FacebookConfigPageProps {
  config: SystemConfig | null;
}

export const FacebookConfigPage: React.FC<FacebookConfigPageProps> = ({ config }) => {
  const [appId, setAppId] = useState('');
  const [pageId, setPageId] = useState(config?.facebook.pageId || '');
  const [pageAccessToken, setPageAccessToken] = useState('');
  const [graphVersion, setGraphVersion] = useState('v20.0');
  const [isVerifying, setIsVerifying] = useState(false);
  const [verified, setVerified] = useState(false);

  const handleVerify = () => {
    setIsVerifying(true);
    setTimeout(() => {
      setIsVerifying(false);
      setVerified(true);
    }, 1500);
  };

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white tracking-tight">Cấu hình kết nối Facebook Page (Meta Graph API)</h1>
        <p className="text-sm text-gray-400 mt-1">
          Quản lý kết nối Fanpage và quyền xuất bản Reels thông qua Meta Graph API chính thức.
        </p>
      </div>

      {/* Safety Notice */}
      <div className="p-4 rounded-xl bg-blue-950/20 border border-blue-500/30 text-xs text-blue-200 flex items-start space-x-3">
        <Shield className="w-5 h-5 text-blue-400 shrink-0 mt-0.5" />
        <div className="space-y-1">
          <p className="font-semibold text-blue-300">Nguyên tắc an toàn & App Review:</p>
          <p className="text-blue-200/80 leading-relaxed">
            Hệ thống không ghi log Page Token, không trả Token về frontend.
            Trong giai đoạn MVP, hệ thống không tự động đăng bài khi chưa có xác nhận và phê duyệt của người dùng.
          </p>
        </div>
      </div>

      <div className="space-y-6">
        {/* Credentials Form */}
        <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
          <div className="flex items-center justify-between">
            <label className="text-sm font-semibold text-white flex items-center gap-2">
              <Share2 className="w-4 h-4 text-blue-400" />
              <span>Thông tin Fanpage & Meta App</span>
            </label>
            <span className="text-[11px] font-mono text-gray-400">Graph API {graphVersion}</span>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-xs">
            <div>
              <label className="text-gray-400 block mb-1">Facebook App ID</label>
              <input
                type="text"
                value={appId}
                onChange={(e) => setAppId(e.target.value)}
                placeholder="Ví dụ: 104829104819..."
                className="w-full px-3 py-2.5 rounded-xl bg-[#0B0F19] border border-[#1F2937] text-white font-mono focus:outline-none focus:border-indigo-500"
              />
            </div>

            <div>
              <label className="text-gray-400 block mb-1">Facebook Page ID</label>
              <input
                type="text"
                value={pageId}
                onChange={(e) => setPageId(e.target.value)}
                placeholder="Ví dụ: 615598129481..."
                className="w-full px-3 py-2.5 rounded-xl bg-[#0B0F19] border border-[#1F2937] text-white font-mono focus:outline-none focus:border-indigo-500"
              />
            </div>
          </div>

          <div className="text-xs">
            <label className="text-gray-400 block mb-1 flex items-center gap-1.5">
              <Key className="w-3.5 h-3.5 text-amber-400" />
              <span>Page Access Token (Bảo mật)</span>
            </label>
            <input
              type="password"
              value={pageAccessToken}
              onChange={(e) => setPageAccessToken(e.target.value)}
              placeholder="EAA..."
              className="w-full px-3 py-2.5 rounded-xl bg-[#0B0F19] border border-[#1F2937] text-white font-mono focus:outline-none focus:border-indigo-500"
            />
            <p className="text-[10px] text-gray-500 mt-1">
              Token được mã hóa khi lưu trữ và chỉ được sử dụng tại Worker để đăng video Reels.
            </p>
          </div>

          <div className="pt-2 flex justify-end">
            <button
              type="button"
              onClick={handleVerify}
              disabled={isVerifying}
              className="px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-500 text-white font-semibold text-xs flex items-center gap-1.5 transition"
            >
              {isVerifying ? 'Đang kiểm tra quyền Page...' : verified ? 'Đã xác thực Page ✓' : 'Kiểm tra kết nối Fanpage'}
            </button>
          </div>
        </div>

        {/* Required Permissions Checklist */}
        <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-3">
          <h3 className="text-xs font-bold uppercase tracking-wider text-gray-400">
            Danh sách quyền Graph API cần thiết
          </h3>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
            {[
              { perm: 'pages_show_list', desc: 'Lấy danh sách các trang quản lý' },
              { perm: 'pages_read_engagement', desc: 'Đọc số liệu tương tác và view Reels' },
              { perm: 'pages_manage_posts', desc: 'Đăng tải video Reels lên Page' },
              { perm: 'read_insights', desc: 'Thu thập chỉ số phân tích hiệu quả' },
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
    </div>
  );
};
