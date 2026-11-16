import { api } from '../services/api';
import { detectPublisher } from './format';
import { getPrefs } from './prefs';

export interface CreateOptions {
  url: string;
  title?: string;
  aiProvider?: string;
  voice?: string;
  speed?: string;
  targetDuration?: number;
  /** Page the video is made for; defaults to the remembered choice */
  facebookAccountId?: string;
}

/** Creates a project from an article URL and queues the processing pipeline. */
export async function createVideoFromUrl(opts: CreateOptions) {
  const prefs = getPrefs();
  const url = opts.url.trim();
  const project = await api.createVideoProject({
    // Empty title → the server uses the real article title once the article has been read
    title: opts.title?.trim() || '',
    sourceUrl: url,
    publisher: detectPublisher(url),
    targetDurationSeconds: opts.targetDuration ?? prefs.targetDuration,
    aiProvider: opts.aiProvider ?? prefs.aiProvider,
    voice: opts.voice ?? prefs.voice,
    voiceSpeed: opts.speed ?? prefs.speed,
    facebookAccountId: (opts.facebookAccountId ?? prefs.defaultPageId) || undefined,
  });
  await api.processVideoProject(project.id);
  return project;
}
