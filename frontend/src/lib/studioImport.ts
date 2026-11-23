import { NostalgiaScript } from '../data/nostalgiaScriptLibrary';
import { formatNostalgiaProductionScript } from './nostalgiaScriptEngine';
import type { StudioScriptImport } from '../services/studio';

/**
 * Hand-off of a finished library script (/drama-scripts) to the video studio: the script travels whole — hook, ending,
 * mood and every scene with its English video prompt, narration, sound notes and camera — and the server builds the
 * shots from it exactly (no AI planning). Kept in sessionStorage between the two pages.
 */
const KEY = 'studio_import_script';

export function toStudioImport(script: NostalgiaScript): StudioScriptImport {
  return {
    title: script.title,
    code: script.code,
    synopsis: script.synopsis,
    mood: script.mood,
    region: script.region,
    era: script.era,
    hook: script.hook,
    ending: script.endingNote,
    script: formatNostalgiaProductionScript(script),
    scenes: script.scenes.map((s) => ({
      title: s.title,
      visualVi: s.visualVi,
      promptEn: s.aiPromptEn,
      voiceover: s.voiceover,
      sound: s.sound,
      camera: s.camera,
      durationSeconds: s.durationSeconds,
    })),
  };
}

export function queueStudioImport(script: NostalgiaScript) {
  try {
    sessionStorage.setItem(KEY, JSON.stringify(toStudioImport(script)));
  } catch {
    /* private mode: the studio page simply opens empty */
  }
}

export function takeStudioImport(): StudioScriptImport | null {
  try {
    const raw = sessionStorage.getItem(KEY);
    sessionStorage.removeItem(KEY);
    return raw ? (JSON.parse(raw) as StudioScriptImport) : null;
  } catch {
    return null;
  }
}

/** Same choice the server makes: a narrator voice and reading speed that suit the mood (changeable before creating). */
export function narratorFor(mood?: string | null): { voice: string; speed: string } {
  const m = (mood ?? '').toLowerCase();
  if (m.includes('nghẹn') || m.includes('nước mắt')) return { voice: 'gemini:Sulafat', speed: '-8%' };
  if (m.includes('da diết') || m.includes('day dứt') || m.includes('nhớ cha mẹ')) return { voice: 'gemini:Charon', speed: '-6%' };
  if (m.includes('xót xa') || m.includes('thương quê')) return { voice: 'gemini:Algenib', speed: '-6%' };
  if (m.includes('hóm hỉnh') || m.includes('ngây ngô') || m.includes('vui')) return { voice: 'gemini:Puck', speed: '+0%' };
  if (m.includes('ấm áp') || m.includes('hoài niệm')) return { voice: 'gemini:Aoede', speed: '-4%' };
  return { voice: 'gemini:Charon', speed: '-4%' };
}

const words = (t?: string | null) => (t ?? '').trim().split(/\s+/).filter(Boolean).length;

/**
 * Rough size of the video the server will build (it splits each scene into ≤ 12 s clips and reads ~3 words a second):
 * number of clips and seconds, for the cost shown before creating.
 */
export function estimateImport(s: StudioScriptImport): { shots: number; seconds: number } {
  let shots = 0;
  let seconds = 0;
  s.scenes.forEach((scene, i) => {
    let narration = scene.voiceover ?? '';
    if (i === 0 && s.hook && !narration.includes(s.hook.slice(0, 20))) narration = `${s.hook} ${narration}`;
    if (i === s.scenes.length - 1 && s.ending && !narration.includes(s.ending.slice(0, 20))) narration = `${narration} ${s.ending}`;
    const w = words(narration);
    const pieces = Math.ceil(w / 28);
    const count = Math.min(6, Math.max(pieces, Math.ceil(Math.max(scene.durationSeconds, 4) / 10), 1));
    shots += Math.max(count, pieces);
    seconds += Math.max(scene.durationSeconds, Math.ceil(w / 3 + pieces));
  });
  return { shots, seconds };
}

export const SPEEDS = [
  { id: '-12%', label: 'Rất chậm (-12%)' },
  { id: '-8%', label: 'Chậm, lắng (-8%)' },
  { id: '-4%', label: 'Hơi chậm (-4%)' },
  { id: '+0%', label: 'Bình thường' },
  { id: '+6%', label: 'Hơi nhanh (+6%)' },
];
