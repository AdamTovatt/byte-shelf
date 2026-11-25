/// <reference lib="dom" />
import { crc32, deflateSync } from 'node:zlib';
import type { Browser, JSHandle, Page } from '@playwright/test';

type Color = readonly [number, number, number];

interface Size {
  width: number;
  height: number;
}

interface Box extends Size {
  x: number;
  y: number;
}

/** The colours of a quartered image: top left, top right, bottom left and bottom right. */
const QUARTERS: readonly Color[] = [[200, 40, 40], [40, 160, 60], [50, 70, 200], [220, 180, 40]];

/** A PNG of four colours, with noise that is seen only in how many bytes it takes. */
export function quarteredImage(width: number, height: number): Buffer {
  const rows = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0, at = 0; y < height; y++) {
    rows[at++] = 0;
    for (let x = 0; x < width; x++) {
      const color = QUARTERS[(y < height / 2 ? 0 : 2) + (x < width / 2 ? 0 : 1)];
      for (const channel of color) rows[at++] = channel + Math.floor(Math.random() * 4);
    }
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  // 8 bits of red, green and blue each.
  header.set([8, 2], 8);
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows, { level: 1 })), chunk('IEND', Buffer.alloc(0))]);
}

function chunk(type: string, data: Buffer): Buffer {
  const named = Buffer.concat([Buffer.from(type, 'latin1'), data]);
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const check = Buffer.alloc(4);
  check.writeUInt32BE(crc32(named));
  return Buffer.concat([length, named, check]);
}

interface TraceEvent {
  name: string;
  ts: number;
  args: { snapshot?: string };
}

/**
 * Records every frame the browser draws of `page` while `during` runs, which answers the box a quartered image
 * fills from then on, however it is zoomed about its middle: of each frame, whether that is there in all of it.
 */
export async function recordFrames(browser: Browser, page: Page, during: () => Promise<Box>): Promise<boolean[]> {
  await browser.startTracing(page, { screenshots: true });
  const box = await during();
  const { traceEvents } = JSON.parse((await browser.stopTracing()).toString()) as { traceEvents: TraceEvent[] };
  const frames = traceEvents.filter(({ name }) => name === 'Screenshot').sort((one, other) => one.ts - other.ts).map(({ args }) => args.snapshot!);

  // Nothing that reads them is drawn in the page they are of.
  const reader = await page.context().newPage();
  const shown = await reader.evaluate(showPoints, { frames, points: pointsOf(box, page.viewportSize()!), tolerance: 30 });
  await reader.close();
  return shown;
}

/**
 * Watches the image called `name` in every frame the page draws from now on, which the browser's own recording
 * samples: of each frame from the first it is in, whether it is there once and loaded whole.
 */
export function watchLoaded(page: Page, name: string): Promise<JSHandle<boolean[]>> {
  return page.evaluateHandle((alt) => {
    const frames: boolean[] = [];
    (function look() {
      const seen = Array.from(document.querySelectorAll<HTMLImageElement>(`img[alt="${alt}"]`));
      if (frames.length > 0 || seen.length > 0) frames.push(seen.length === 1 && seen[0].complete && seen[0].naturalWidth > 0);
      requestAnimationFrame(look);
    })();
    return frames;
  }, name);
}

/** Records in which places the image called `name` is, in every frame from now on. */
export function recordPlaces(page: Page, name: string): Promise<JSHandle<string[]>> {
  return page.evaluateHandle((alt) => {
    const places: string[] = [];
    (function look() {
      const place = JSON.stringify(document.querySelector(`img[alt="${alt}"]`)?.getBoundingClientRect());
      if (!places.includes(place)) places.push(place);
      requestAnimationFrame(look);
    })();
    return places;
  }, name);
}

interface ColoredPoint {
  x: number;
  y: number;
  color: Color;
}

/** The points of a quartered image in `box` and their colours, as parts of a page of `size`. */
function pointsOf(box: Box, size: Size): ColoredPoint[] {
  const parts = [0.03, 0.15, 0.3, 0.45, 0.55, 0.7, 0.85, 0.97];
  return parts.flatMap((y) => parts.map((x) => ({
    x: (box.x + box.width * x) / size.width,
    y: (box.y + box.height * y) / size.height,
    color: QUARTERS[(y < 0.5 ? 0 : 2) + (x < 0.5 ? 0 : 1)],
  })));
}

/** Which of `frames`, JPEGs in base64, have every one of `points` in its colour. */
async function showPoints({ frames, points, tolerance }: { frames: string[]; points: ColoredPoint[]; tolerance: number }): Promise<boolean[]> {
  const shown: boolean[] = [];
  for (const frame of frames) {
    const image = await createImageBitmap(await (await fetch(`data:image/jpeg;base64,${frame}`)).blob());
    const context = new OffscreenCanvas(image.width, image.height).getContext('2d')!;
    context.drawImage(image, 0, 0);
    shown.push(points.every(({ x, y, color }) => {
      const { data } = context.getImageData(Math.floor(x * image.width), Math.floor(y * image.height), 1, 1);
      return color.every((channel, index) => Math.abs(channel - data[index]) <= tolerance);
    }));
  }
  return shown;
}
