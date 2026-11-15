import { SystemConfig, VideoProject, VideoProjectStatus } from '../types';

const API_BASE = '/api';

export interface CreateProjectRequest {
  title: string;
  sourceUrl: string;
  publisher?: string;
  targetDurationSeconds?: number;
  aiProvider?: string;
  voice?: string;
}

export interface ProcessingJobDto {
  id: string;
  videoProjectId: string;
  hangfireJobId?: string;
  step: string;
  status: string;
  retryCount: number;
  progressPercentage: number;
  startedAtUtc?: string;
  finishedAtUtc?: string;
  errorMessage?: string;
}

export const api = {
  async getHealth(): Promise<boolean> {
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
  },

  // Phase 2 Video Projects & Jobs APIs
  async createVideoProject(data: CreateProjectRequest): Promise<VideoProject> {
    const res = await fetch(`${API_BASE}/video-projects`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || 'Failed to create video project');
    }
    return await res.json();
  },

  async getVideoProjects(page = 1, pageSize = 20): Promise<VideoProject[]> {
    try {
      const res = await fetch(`${API_BASE}/video-projects?page=${page}&pageSize=${pageSize}`);
      if (!res.ok) return [];
      return await res.json();
    } catch {
      return [];
    }
  },

  async getVideoProjectById(id: string): Promise<VideoProject | null> {
    try {
      const res = await fetch(`${API_BASE}/video-projects/${id}`);
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  },

  async processVideoProject(id: string): Promise<ProcessingJobDto> {
    const res = await fetch(`${API_BASE}/video-projects/${id}/process`, {
      method: 'POST'
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || 'Failed to enqueue processing job');
    }
    return await res.json();
  },

  async getJobById(id: string): Promise<ProcessingJobDto | null> {
    try {
      const res = await fetch(`${API_BASE}/jobs/${id}`);
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  },

  async cancelJob(id: string): Promise<boolean> {
    try {
      const res = await fetch(`${API_BASE}/jobs/${id}/cancel`, {
        method: 'POST'
      });
      return res.ok;
    } catch {
      return false;
    }
  }
};
