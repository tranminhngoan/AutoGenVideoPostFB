import { useEffect, useRef, useState } from 'react';
import { Play, Square, Loader2 } from 'lucide-react';
import { RadioCard, Badge, cn } from './ui';
import { useToast } from './feedback';
import { VOICES, voicePreviewUrl } from '../lib/prefs';

/** Shared player so only one preview plays at a time. */
function usePreviewPlayer() {
  const toast = useToast();
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const [playing, setPlaying] = useState<string | null>(null);
  const [loading, setLoading] = useState<string | null>(null);

  useEffect(() => () => audioRef.current?.pause(), []);

  const stop = () => {
    audioRef.current?.pause();
    setPlaying(null);
  };

  const play = async (voiceId: string) => {
    if (playing === voiceId) return stop();
    stop();
    setLoading(voiceId);
    try {
      const res = await fetch(voicePreviewUrl(voiceId));
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.message || 'Không tạo được giọng mẫu.');
      }
      const url = URL.createObjectURL(await res.blob());
      const audio = new Audio(url);
      audioRef.current = audio;
      audio.onended = () => setPlaying(null);
      await audio.play();
      setPlaying(voiceId);
    } catch (err) {
      toast.error('Không nghe thử được', err instanceof Error ? err.message : undefined);
    } finally {
      setLoading(null);
    }
  };

  return { playing, loading, play };
}

export function VoicePicker({ value, onChange }: { value: string; onChange: (id: string) => void }) {
  const { playing, loading, play } = usePreviewPlayer();

  const groups: { key: 'natural' | 'basic'; title: string; hint: string }[] = [
    { key: 'natural', title: 'Giọng tự nhiên', hint: 'Gemini, ngữ điệu gần người thật' },
    { key: 'basic', title: 'Giọng cơ bản', hint: 'Edge, miễn phí' },
  ];

  return (
    <div className="space-y-4">
      {groups.map((g) => (
        <div key={g.key}>
          <div className="mb-2 flex items-center gap-2 text-xs">
            <span className="font-medium text-fg">{g.title}</span>
            <span className="text-fg-subtle">· {g.hint}</span>
          </div>
          <div className="grid gap-2 sm:grid-cols-2" role="radiogroup">
            {VOICES.filter((v) => v.group === g.key).map((v) => (
              <RadioCard
                key={v.id}
                checked={value === v.id}
                onClick={() => onChange(v.id)}
                title={v.name}
                description={v.desc}
                aside={
                  <span className="flex items-center gap-1.5">
                    {v.id === 'gemini:Kore' && <Badge tone="info">Mặc định</Badge>}
                    <span
                      role="button"
                      tabIndex={0}
                      title="Nghe thử"
                      aria-label={`Nghe thử giọng ${v.name}`}
                      onClick={(e) => {
                        e.stopPropagation();
                        play(v.id);
                      }}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          e.stopPropagation();
                          play(v.id);
                        }
                      }}
                      className={cn(
                        'flex h-7 w-7 items-center justify-center rounded-full border transition-colors',
                        playing === v.id
                          ? 'border-accent bg-accent text-accent-fg'
                          : 'border-line-strong text-fg-muted hover:border-fg-muted hover:text-fg'
                      )}
                    >
                      {loading === v.id ? (
                        <Loader2 className="h-3.5 w-3.5 animate-spin" />
                      ) : playing === v.id ? (
                        <Square className="h-3 w-3" />
                      ) : (
                        <Play className="ml-0.5 h-3.5 w-3.5" />
                      )}
                    </span>
                  </span>
                }
              />
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}
