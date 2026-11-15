import React, { useState } from 'react';
import { Bot, Key, Sliders, ShieldCheck, CheckCircle2, AlertCircle } from 'lucide-react';
import { SystemConfig } from '../types';

interface AiConfigPageProps {
  config: SystemConfig | null;
}

export const AiConfigPage: React.FC<AiConfigPageProps> = ({ config }) => {
  const [provider, setProvider] = useState<'Gemini' | 'Claude'>('Gemini');
  const [model, setModel] = useState('gemini-1.5-flash');
  const [apiKey, setApiKey] = useState('');
  const [temperature, setTemperature] = useState(0.7);
  const [timeout, setTimeoutSeconds] = useState(60);
  const [saved, setSaved] = useState(false);

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    setSaved(true);
    setTimeout(() => setSaved(false), 3000);
  };

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white tracking-tight">Cấu hình nhà cung cấp AI Editor</h1>
        <p className="text-sm text-gray-400 mt-1">
          Thiết lập mô hình AI dùng để đọc bài báo, biên tập kịch bản ngắn 45–60s và phân tích phân cảnh.
        </p>
      </div>

      <form onSubmit={handleSave} className="space-y-6">
        {/* Provider Selection */}
        <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
          <label className="text-sm font-semibold text-white flex items-center gap-2">
            <Bot className="w-4 h-4 text-purple-400" />
            <span>Chọn nhà cung cấp AI</span>
          </label>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div
              onClick={() => {
                setProvider('Gemini');
                setModel('gemini-1.5-flash');
              }}
              className={`p-4 rounded-xl border cursor-pointer transition ${
                provider === 'Gemini'
                  ? 'bg-indigo-600/15 border-indigo-500 ring-1 ring-indigo-500'
                  : 'bg-[#0B0F19] border-[#1F2937] hover:border-gray-700'
              }`}
            >
              <div className="flex items-center justify-between mb-1">
                <span className="font-bold text-sm text-white">Google Gemini</span>
                <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/20 text-emerald-300">
                  Khuyên dùng
                </span>
              </div>
              <p className="text-xs text-gray-400">
                gemini-1.5-flash / gemini-1.5-pro: Tốc độ cao, chi phí tối ưu, xử lý tiếng Việt rất tự nhiên.
              </p>
            </div>

            <div
              onClick={() => {
                setProvider('Claude');
                setModel('claude-3-5-sonnet');
              }}
              className={`p-4 rounded-xl border cursor-pointer transition ${
                provider === 'Claude'
                  ? 'bg-purple-600/15 border-purple-500 ring-1 ring-purple-500'
                  : 'bg-[#0B0F19] border-[#1F2937] hover:border-gray-700'
              }`}
            >
              <div className="flex items-center justify-between mb-1">
                <span className="font-bold text-sm text-white">Anthropic Claude</span>
                <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-purple-500/20 text-purple-300">
                  Cao cấp
                </span>
              </div>
              <p className="text-xs text-gray-400">
                claude-3-5-sonnet: Biên tập văn phong sắc bén, khả năng phân tích sự kiện chính xác.
              </p>
            </div>
          </div>
        </div>

        {/* API Credentials */}
        <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
          <label className="text-sm font-semibold text-white flex items-center gap-2">
            <Key className="w-4 h-4 text-amber-400" />
            <span>API Key ({provider})</span>
          </label>
          <div className="space-y-2">
            <input
              type="password"
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              placeholder="Nhập API Key bảo mật (không hardcode vào mã nguồn)..."
              className="w-full px-4 py-3 rounded-xl bg-[#0B0F19] border border-[#1F2937] text-white text-sm font-mono focus:outline-none focus:border-indigo-500"
            />
            <p className="text-[11px] text-gray-400">
              Quy tắc bảo mật: API Key được mã hóa khi lưu và chỉ truyền qua biến môi trường (AI_API_KEY).
            </p>
          </div>
        </div>

        {/* Hyperparameters */}
        <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
          <label className="text-sm font-semibold text-white flex items-center gap-2">
            <Sliders className="w-4 h-4 text-indigo-400" />
            <span>Tham số mô hình</span>
          </label>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-6 text-xs">
            <div>
              <div className="flex justify-between text-gray-400 mb-1">
                <span>Temperature (Tính sáng tạo)</span>
                <span className="font-mono text-indigo-400">{temperature}</span>
              </div>
              <input
                type="range"
                min="0.1"
                max="1.0"
                step="0.05"
                value={temperature}
                onChange={(e) => setTemperature(parseFloat(e.target.value))}
                className="w-full accent-indigo-600 bg-gray-700 h-1.5 rounded-lg appearance-none cursor-pointer"
              />
              <span className="text-[10px] text-gray-500 mt-1 block">
                Khuyến nghị 0.6 – 0.7 để cân bằng giữa sự hấp dẫn và độ chính xác của tin tức.
              </span>
            </div>

            <div>
              <div className="flex justify-between text-gray-400 mb-1">
                <span>Timeout (Giây)</span>
                <span className="font-mono text-indigo-400">{timeout}s</span>
              </div>
              <input
                type="range"
                min="30"
                max="120"
                step="5"
                value={timeout}
                onChange={(e) => setTimeoutSeconds(parseInt(e.target.value))}
                className="w-full accent-indigo-600 bg-gray-700 h-1.5 rounded-lg appearance-none cursor-pointer"
              />
              <span className="text-[10px] text-gray-500 mt-1 block">
                Thời gian chờ tối đa cho mỗi phản hồi kịch bản từ AI.
              </span>
            </div>
          </div>
        </div>

        {/* System Prompt Safeguards */}
        <div className="p-4 rounded-xl bg-purple-950/20 border border-purple-500/30 text-xs text-purple-200 space-y-1">
          <p className="font-semibold text-purple-300">Ràng buộc System Prompt bắt buộc:</p>
          <p className="text-purple-200/80">
            AI Editor luôn được cấu hình với chỉ thị nghiêm ngặt: Tuyệt đối không bịa đặt số liệu hoặc sự kiện,
            không gán ghép thông tin chưa xác thực, phân biệt rõ giữa Fact và Bình luận biên tập.
          </p>
        </div>

        <div className="flex justify-end items-center space-x-3">
          {saved && (
            <span className="text-xs text-emerald-400 flex items-center gap-1">
              <CheckCircle2 className="w-4 h-4" /> Đã lưu cấu hình thành công!
            </span>
          )}
          <button
            type="submit"
            className="px-6 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-sm transition"
          >
            Lưu thay đổi
          </button>
        </div>
      </form>
    </div>
  );
};
