// A new passkey is named after the browser and device it was made on, such as "Safari on iPhone", so
// the list of passkeys tells them apart before anyone renames one.

const BROWSERS: [RegExp, string][] = [
  [/Edg(A|iOS)?\//, 'Edge'],
  [/OPR\/|Opera/, 'Opera'],
  [/SamsungBrowser\//, 'Samsung Internet'],
  [/Firefox\/|FxiOS\//, 'Firefox'],
  [/Chrome\/|CriOS\//, 'Chrome'],
  [/Safari\//, 'Safari'],
];

const DEVICES: [RegExp, string][] = [
  [/iPhone/, 'iPhone'],
  [/iPad/, 'iPad'],
  [/Android/, 'Android'],
  [/CrOS/, 'ChromeOS'],
  [/Mac OS X|Macintosh/, 'Mac'],
  [/Windows/, 'Windows'],
  [/Linux/, 'Linux'],
];

/** "Browser on device" for `userAgent`, leaving out what it does not tell. */
export function deviceName(userAgent: string): string {
  const browser = BROWSERS.find(([pattern]) => pattern.test(userAgent))?.[1];
  const device = DEVICES.find(([pattern]) => pattern.test(userAgent))?.[1];
  if (browser && device) return `${browser} on ${device}`;
  return browser ?? device ?? 'Passkey';
}
