import React from 'react';
import { ShieldCheck, Cpu, HardDrive } from 'lucide-react';

interface HeaderProps {
  title: string;
  subtitle?: string;
  apiHealthy: boolean;
}

export const Header: React.FC<HeaderProps> = ({ title, subtitle, apiHealthy }) => {
  return (
    <header className="h-16 border-b border-[#1F2937] bg-[#0B0F19]/80 backdrop-blur-md px-8 flex items-center justify-between sticky top-0 z-20">
      <div>
        <h2 className="text-lg font-bold text-white tracking-tight">{title}</h2>
        {subtitle && <p className="text-xs text-gray-400">{subtitle}</p>}
      </div>

      <div className="flex items-center space-x-4 text-xs">
        {/* Policy Badge */}
        <div className="flex items-center space-x-1.5 px-3 py-1.5 rounded-full bg-indigo-950/40 border border-indigo-800/40 text-indigo-300">
          <ShieldCheck className="w-3.5 h-3.5 text-indigo-400" />
          <span className="font-medium text-[11px]">Strict Article Image Policy</span>
        </div>

        {/* Runtime Badge */}
        <div className="flex items-center space-x-1.5 px-3 py-1.5 rounded-full bg-[#111827] border border-[#1F2937] text-gray-300">
          <Cpu className="w-3.5 h-3.5 text-purple-400" />
          <span className="font-mono text-[11px]">.NET 8 + FFmpeg 9:16</span>
        </div>

        {/* Temp storage status */}
        <div className="flex items-center space-x-1.5 px-3 py-1.5 rounded-full bg-[#111827] border border-[#1F2937] text-gray-300">
          <HardDrive className="w-3.5 h-3.5 text-emerald-400" />
          <span className="font-mono text-[11px]">Ephemerality: Active</span>
        </div>
      </div>
    </header>
  );
};
