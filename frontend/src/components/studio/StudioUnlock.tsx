import { useState } from 'react';
import { KeyRound } from 'lucide-react';
import { Button, Card, Input } from '../ui';
import { getStudioKey, setStudioKey, studio } from '../../services/studio';

/**
 * Asks once for the server's Gemini API key: the app has no login, so this is what stops strangers from spending
 * money through the studio. The key stays in this browser only.
 */
export function StudioUnlock({ onUnlocked }: { onUnlocked: () => void }) {
  const [key, setKey] = useState('');
  const [error, setError] = useState<string | null>(getStudioKey() ? 'Khoá đã lưu không còn khớp với server — dán lại.' : null);
  const [checking, setChecking] = useState(false);

  const unlock = async () => {
    if (!key.trim()) return;
    setChecking(true);
    setStudioKey(key);
    try {
      await studio.checkAccess();
      setError(null);
      setKey('');
      onUnlocked();
    } catch (e) {
      setStudioKey(null);
      setError((e as Error).message);
    } finally {
      setChecking(false);
    }
  };

  return (
    <Card className="space-y-3 border-warning/40 bg-warning/[0.06] p-4">
      <div className="flex items-center gap-2 text-sm font-semibold text-fg">
        <KeyRound className="h-4 w-4" /> Mở khoá Studio Video AI
      </div>
      <p className="text-xs text-fg-muted">
        Dán <b>khoá Vercel AI Gateway</b> đang đặt trên Railway (biến <code>AI_GATEWAY_API_KEY</code>, xem ở vercel.com → AI Gateway → API Keys) — hoặc khoá Gemini đang đặt ở đó.
        Khoá chỉ lưu trên trình duyệt này, để người lạ không dùng app tốn tiền của anh.
      </p>
      {error && <p className="text-xs text-danger">{error}</p>}
      <div className="flex flex-col gap-2 sm:flex-row">
        <Input type="password" autoComplete="off" placeholder="Khoá API…" value={key} onChange={(e) => setKey(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && unlock()} />
        <Button variant="primary" className="shrink-0" loading={checking} disabled={!key.trim()} onClick={unlock}>
          Mở khoá
        </Button>
      </div>
    </Card>
  );
}
