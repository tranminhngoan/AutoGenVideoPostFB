// Run: npm test  (Node's built-in test runner; Node ≥ 22.6 runs TypeScript directly)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  vnDate, vnTime, vnToUtc, vnShort, addDays, weekDays, monthGrid, growthPercent, conversionPercent, num, countHashtags, wrapWords, newKey,
} from '../src/lib/growthMath.ts';

test('Vietnam time is UTC+7 whatever the browser time zone', () => {
  assert.equal(vnToUtc('2026-11-26', '07:30'), '2026-11-26T00:30:00.000Z');
  assert.equal(vnToUtc('2026-11-26', '03:00'), '2026-11-25T20:00:00.000Z');
  assert.equal(vnDate('2026-11-25T18:00:00Z'), '2026-11-26');
  assert.equal(vnTime('2026-11-25T18:05:00Z'), '01:05');
  assert.equal(vnShort('2026-11-26T13:00:00Z'), '26/11 20:00');
  assert.equal(vnShort(null), '—');
});

test('calendar grids', () => {
  assert.equal(addDays('2026-12-31', 1), '2027-01-01');
  const week = weekDays('2026-11-26'); // Thursday
  assert.equal(week[0], '2026-11-23');
  assert.equal(week[6], '2026-11-29');
  const grid = monthGrid('2026-11-10');
  assert.equal(grid.length % 7, 0);
  assert.ok(grid.includes('2026-11-01') && grid.includes('2026-11-30'));
  assert.equal(grid[0], '2026-10-26'); // Monday before Nov 1st (a Sunday)
});

test('KPI formulas return null instead of inventing a number', () => {
  assert.equal(growthPercent(50, 1000), 5);
  assert.equal(growthPercent(50, 0), null);
  assert.equal(growthPercent(null, 1000), null);
  assert.equal(conversionPercent(30, 600), 5);
  assert.equal(conversionPercent(30, null), null);
  assert.equal(conversionPercent(30, 0), null);
});

test('number formatting shows a dash for missing values', () => {
  assert.equal(num(null), '—');
  assert.equal(num(undefined), '—');
  assert.ok(num(12345).endsWith('K'));
  assert.ok(num(2_500_000).endsWith('Tr'));
  assert.ok(num(5, '%').endsWith('%'));
});

test('hashtags and wrapping', () => {
  assert.equal(countHashtags('Tuổi thơ #TuoiTho #kyniem', '#tuoitho #quê'), 3);
  assert.deepEqual(wrapWords('một hai ba bốn năm sáu', 7), ['một hai', 'ba bốn', 'năm sáu']);
  assert.notEqual(newKey(), newKey());
});
