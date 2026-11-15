/** @type {import('tailwindcss').Config} */
const token = (name) => `rgb(var(--${name}) / <alpha-value>)`;

export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  darkMode: ['selector', '[data-theme="dark"]'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'system-ui', '-apple-system', 'Segoe UI', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'ui-monospace', 'monospace'],
      },
      fontSize: {
        '2xs': ['11px', '16px'],
      },
      colors: {
        canvas: token('canvas'),
        surface: token('surface'),
        subtle: token('subtle'),
        muted: token('muted'),
        line: token('line'),
        'line-strong': token('line-strong'),
        fg: token('fg'),
        'fg-muted': token('fg-muted'),
        'fg-subtle': token('fg-subtle'),
        accent: token('accent'),
        'accent-fg': token('accent-fg'),
        success: token('success'),
        warning: token('warning'),
        danger: token('danger'),
        brand: token('brand'),
      },
      boxShadow: {
        card: '0 1px 2px rgb(0 0 0 / 0.04)',
        pop: '0 8px 24px -6px rgb(0 0 0 / 0.16), 0 2px 6px -2px rgb(0 0 0 / 0.08)',
      },
      keyframes: {
        'fade-in': { from: { opacity: '0' }, to: { opacity: '1' } },
        'scale-in': {
          from: { opacity: '0', transform: 'translateY(4px) scale(0.98)' },
          to: { opacity: '1', transform: 'none' },
        },
        'slide-in': { from: { transform: 'translateX(100%)' }, to: { transform: 'none' } },
        'slide-in-left': { from: { transform: 'translateX(-100%)' }, to: { transform: 'none' } },
      },
      animation: {
        'fade-in': 'fade-in 120ms ease-out',
        'scale-in': 'scale-in 140ms ease-out',
        'slide-in': 'slide-in 200ms cubic-bezier(0.2, 0.8, 0.2, 1)',
        'slide-in-left': 'slide-in-left 200ms cubic-bezier(0.2, 0.8, 0.2, 1)',
      },
    },
  },
  plugins: [],
};
