/* Sends the drama scripts of "Lên kịch bản" (the 300-script library + the owner's own saved scripts, which live only in
   this browser) to the server, so the autopilot can make one post a day from them. Only scripts the server doesn't have
   yet are sent; the library is loaded on demand (its own chunk). */
import { autopilotApi, type ScriptImport } from '../services/growth';
import type { NostalgiaScript } from '../data/nostalgiaScriptLibrary';

function toImport(s: NostalgiaScript, source: 'library' | 'custom', code?: string): ScriptImport {
  return {
    code: (code ?? s.code ?? s.id).slice(0, 100),
    title: s.title,
    category: s.category,
    categoryName: s.categoryName,
    mood: s.mood,
    era: s.era,
    region: s.region,
    synopsis: s.synopsis,
    hook: s.hook,
    endingNote: s.endingNote,
    scenes: (s.scenes ?? []).map((x) => ({ title: x.title, visualVi: x.visualVi, aiPromptEn: x.aiPromptEn, voiceover: x.voiceover })),
    source,
  };
}

let running: Promise<number> | null = null;

/** Returns how many scripts were added (0 when the server already had them all). */
export function syncScripts(): Promise<number> {
  if (running) return running;
  running = (async () => {
    const stats = await autopilotApi.scriptStats();
    const have = new Set(stats.codes);
    const [{ ALL_NOSTALGIA_SCRIPTS }, { nostalgiaStorage }] = await Promise.all([import('../data/nostalgiaScriptLibrary'), import('./nostalgiaStorage')]);
    const items: ScriptImport[] = ALL_NOSTALGIA_SCRIPTS.map((s) => toImport(s, 'library'));
    for (const p of nostalgiaStorage.getSavedProjects()) items.push(toImport(p.script, 'custom', `custom-${p.id}`));
    const missing = items.filter((i) => i.code && !have.has(i.code));
    let added = 0;
    for (let i = 0; i < missing.length; i += 50) added += (await autopilotApi.importScripts(missing.slice(i, i + 50))).added;
    return added;
  })().finally(() => {
    running = null;
  });
  return running;
}
