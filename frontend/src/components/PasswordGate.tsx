import { useState, type FormEvent, type ReactNode } from 'react';
import { Lock } from 'lucide-react';
import { Button, Card, Input } from './ui';

// Simple front-door lock: one hard-coded password, remembered in this browser.
const PASSWORD = '0974210897zZ@';
const STORAGE_KEY = 'app-unlocked';

function isUnlocked() {
  try {
    return localStorage.getItem(STORAGE_KEY) === '1';
  } catch {
    return false;
  }
}

export function PasswordGate({ children }: { children: ReactNode }) {
  const [unlocked, setUnlocked] = useState(isUnlocked);
  const [value, setValue] = useState('');
  const [error, setError] = useState(false);

  if (unlocked) return <>{children}</>;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (value === PASSWORD) {
      try {
        localStorage.setItem(STORAGE_KEY, '1');
      } catch {
        /* private mode: unlock for this tab only */
      }
      setUnlocked(true);
    } else {
      setError(true);
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-canvas p-4">
      <Card className="w-full max-w-sm p-6">
        <form onSubmit={submit} className="space-y-4">
          <div className="flex items-center gap-2 text-fg">
            <Lock className="h-5 w-5" />
            <h1 className="text-lg font-semibold">Nhập mật khẩu</h1>
          </div>
          <Input
            type="password"
            autoFocus
            autoComplete="current-password"
            placeholder="Mật khẩu"
            value={value}
            onChange={(e) => {
              setValue(e.target.value);
              setError(false);
            }}
          />
          {error && <p className="text-sm text-danger">Sai mật khẩu</p>}
          <Button type="submit" variant="primary" className="w-full">
            Vào trang
          </Button>
        </form>
      </Card>
    </div>
  );
}
