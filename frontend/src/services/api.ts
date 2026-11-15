import { SystemConfig } from '../types';

const API_BASE = '/api';

export const api = {
  async getHealth() {
    try {
      const res = await fetch('/health');
      return res.ok;
    } catch {
      return false;
    }
  },

  async getDetailedHealth() {
    try {
      const res = await fetch(`${API_BASE}/health`);
      if (!res.ok) throw new Error('Health check failed');
      return await res.json();
    } catch (e) {
      return { status: 'Unreachable', environment: 'Unknown', components: {} };
    }
  },

  async getConfigSummary(): Promise<SystemConfig> {
    try {
      const res = await fetch(`${API_BASE}/system/config-summary`);
      if (!res.ok) throw new Error('Config summary failed');
      return await res.json();
    } catch (e) {
      return {
        environment: 'Development',
        ai: { provider: 'Gemini', isConfigured: false, model: 'gemini-1.5-flash' },
        tts: { provider: 'EdgeTts', voice: 'vi-VN-HoaiMyNeural', isConfigured: true },
        facebook: { isConfigured: false, pageId: '' },
        storage: { tempPath: './tmp/newsstudio', maxConcurrentRenders: 2, maxStorageMb: 5120, ttlMinutes: 120 }
      };
    }
  },

  async getSystemInfo() {
    try {
      const res = await fetch(`${API_BASE}/system/info`);
      if (!res.ok) throw new Error('System info failed');
      return await res.json();
    } catch (e) {
      return { appName: 'AI News Studio', version: '1.0.0', dotnetVersion: '8.0' };
    }
  }
};
