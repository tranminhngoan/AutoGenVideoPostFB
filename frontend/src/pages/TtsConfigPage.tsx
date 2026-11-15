import React, { useState } from 'react';
import { Volume2, Mic, Play, CheckCircle2, Sliders, VolumeX } from 'lucide-react';
import { SystemConfig } from '../types';

interface TtsConfigPageProps {
  config: SystemConfig | null;
}

export const TtsConfigPage: React.FC<TtsConfigPageProps> = ({ config }) => {
  const [provider, setProvider] = useState('EdgeTts');
  const [voice, setVoice] = useState('vi-VN-HoaiMyNeural');
  const [speed, setSpeed] = useState(0);
  const [pitch, setPitch] = useState(0);
  const [isPlayingTest, setIsPlayingTest] = useState(false);
  const [saved, setSaved] = useState(false);

  const handleTestAudio = () => {
    setIsPlayingTest(true);
    setTimeout(() => {
      setIsPlayingTest(false);
    }, 2500);
  };

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    setSaved(true);
    setTimeout(() => setSaved(false), 3000);
  };

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white tracking-tight">Cấu hình giọng đọc tin tức (TTS)</h1>
        <p className="text-sm text-gray-400 mt-1">
          Lựa chọn nhà cung cấp chuyển văn bản thành giọng nói tiếng Việt tự nhiên và điều chỉnh nhịp đọc.
        </p>
      </div>

      <form onSubmit={handleSave} className="space-y-6">
        {/* Provider selection */}
        <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
          <label className="text-sm font-semibold text-white flex items-center gap-2">
            <Volume2 className="w-4 h-4 text-pink-400" />
            <span>Nhà cung cấp Text-To-Speech</span>
          </label>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 text-xs">
            {[
              { id: 'EdgeTts', name: 'Microsoft Edge TTS', tag: 'Miễn phí & Tự nhiên', desc: 'Giọng Neural tiếng Việt cực mượt, không tốn phí bản quyền' },
              { id: 'ElevenLabs', name: 'ElevenLabs', tag: 'Chuyên nghiệp', desc: 'Biểu cảm giọng nói cao cấp nhất, yêu cầu API Key trả phí' },
              { id: 'Azure', name: 'Azure Cognitive', tag: 'Doanh nghiệp', desc: 'Độ ổn định cao, tùy chỉnh SSML chuyên sâu' },
            ].map((p) => (
              <div
                key={p.id}
                onClick={() => setProvider(p.id)}
                className={`p-4 rounded-xl border cursor-pointer transition ${
                  provider === p.id
                    ? 'bg-indigo-600/15 border-indigo-500 ring-1 ring-indigo-500'
                    : 'bg-[#0B0F19] border-[#1F2937] hover:border-gray-700'
                }`}
              >
                <div className="flex items-center justify-between mb-1.5">
                  <span className="font-bold text-white">{p.name}</span>
                  <span className="text-[10px] font-semibold text-indigo-300 bg-indigo-500/20 px-1.5 py-0.5 rounded">
                    {p.tag}
                  </span>
                </div>
                <p className="text-[11px] text-gray-400 leading-relaxed">{p.desc}</p>
              </div>
            ))}
          </div>
        </div>

        {/* Voice Selection & Preview */}
        <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
          <label className="text-sm font-semibold text-white flex items-center gap-2">
            <Mic className="w-4 h-4 text-indigo-400" />
            <span>Giọng đọc tiếng Việt tuyển chọn</span>
          </label>
          <div className="space-y-3">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div
                onClick={() => setVoice('vi-VN-HoaiMyNeural')}
                className={`p-4 rounded-xl border cursor-pointer transition ${
                  voice === 'vi-VN-HoaiMyNeural'
                    ? 'bg-indigo-600/20 border-indigo-500'
                    : 'bg-[#0B0F19] border-[#1F2937]'
                }`}
              >
                <span className="font-bold text-xs text-white block">Hoài My (Nữ - Giọng Bắc)</span>
                <span className="text-[11px] text-gray-400">Rõ ràng, truyền cảm, nhịp đọc tin tức thời sự lôi cuốn.</span>
              </div>

              <div
                onClick={() => setVoice('vi-VN-NamMinhNeural')}
                className={`p-4 rounded-xl border cursor-pointer transition ${
                  voice === 'vi-VN-NamMinhNeural'
                    ? 'bg-indigo-600/20 border-indigo-500'
                    : 'bg-[#0B0F19] border-[#1F2937]'
                }`}
              >
                <span className="font-bold text-xs text-white block">Nam Minh (Nam - Giọng Bắc)</span>
                <span className="text-[11px] text-gray-400">Trầm ấm, nghiêm túc, phong cách phóng viên chính luận.</span>
              </div>
            </div>

            {/* Test Listen Bar */}
            <div className="pt-2 flex items-center justify-between p-3 rounded-xl bg-[#0B0F19] border border-gray-800">
              <div className="text-xs text-gray-300">
                <span>Câu mẫu: </span>
                <span className="italic text-gray-400">
                  "Kính chào quý vị, đây là bản tin nhanh từ AI News Studio."
                </span>
              </div>
              <button
                type="button"
                onClick={handleTestAudio}
                className="px-3.5 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold flex items-center gap-1.5 transition"
              >
                <Play className="w-3.5 h-3.5" />
                <span>{isPlayingTest ? 'Đang phát thử...' : 'Nghe thử giọng'}</span>
              </button>
            </div>
          </div>
        </div>

        {/* Speed & Pitch Controls */}
        <div className="glass-panel p-6 rounded-2xl border border-[#1F2937] space-y-4">
          <label className="text-sm font-semibold text-white flex items-center gap-2">
            <Sliders className="w-4 h-4 text-purple-400" />
            <span>Tốc độ và Ngắt nghỉ</span>
          </label>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-6 text-xs">
            <div>
              <div className="flex justify-between text-gray-400 mb-1">
                <span>Tốc độ đọc (Speed rate)</span>
                <span className="font-mono text-indigo-400">{speed >= 0 ? `+${speed}%` : `${speed}%`}</span>
              </div>
              <input
                type="range"
                min="-20"
                max="25"
                value={speed}
                onChange={(e) => setSpeed(parseInt(e.target.value))}
                className="w-full accent-indigo-600 bg-gray-700 h-1.5 rounded-lg appearance-none cursor-pointer"
              />
              <span className="text-[10px] text-gray-500 mt-1 block">Khuyến nghị +0% đến +5% cho video tin tức nhịp điệu nhanh.</span>
            </div>

            <div>
              <div className="flex justify-between text-gray-400 mb-1">
                <span>Độ cao giọng (Pitch)</span>
                <span className="font-mono text-indigo-400">{pitch >= 0 ? `+${pitch}Hz` : `${pitch}Hz`}</span>
              </div>
              <input
                type="range"
                min="-10"
                max="10"
                value={pitch}
                onChange={(e) => setPitch(parseInt(e.target.value))}
                className="w-full accent-indigo-600 bg-gray-700 h-1.5 rounded-lg appearance-none cursor-pointer"
              />
              <span className="text-[10px] text-gray-500 mt-1 block">Điều chỉnh độ trầm hoặc bổng của giọng.</span>
            </div>
          </div>
        </div>

        <div className="flex justify-end items-center space-x-3">
          {saved && (
            <span className="text-xs text-emerald-400 flex items-center gap-1">
              <CheckCircle2 className="w-4 h-4" /> Đã lưu cấu hình TTS thành công!
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
