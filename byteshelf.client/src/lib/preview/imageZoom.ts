// The image viewer's zoom: an image fitted to its frame, scaled about the frame's centre and moved by
// an offset in screen pixels. A point is measured from the frame's centre too.

export interface Point {
  x: number;
  y: number;
}

export interface Size {
  width: number;
  height: number;
}

export interface ImageView extends Point {
  /** 1 is fitted to the frame. */
  scale: number;
}

export const FITTED: ImageView = { scale: 1, x: 0, y: 0 };

const MAX_SCALE = 16;

/** What a double-click zooms a fitted image to. */
const DOUBLE_CLICK_SCALE = 2.5;

/** `view` scaled by `factor` with what is under `point` staying there, the image kept in the frame. */
export function zoomedAt(view: ImageView, factor: number, point: Point, fitted: Size, frame: Size): ImageView {
  const scale = Math.min(MAX_SCALE, Math.max(1, view.scale * factor));
  const ratio = scale / view.scale;
  return kept({ scale, x: point.x - (point.x - view.x) * ratio, y: point.y - (point.y - view.y) * ratio }, fitted, frame);
}

/** `view` moved by `by`, the image kept in the frame. */
export function movedBy(view: ImageView, by: Point, fitted: Size, frame: Size): ImageView {
  return kept({ scale: view.scale, x: view.x + by.x, y: view.y + by.y }, fitted, frame);
}

/** A zoomed view back to fitted, and a fitted one zoomed in at `point`. */
export function toggledAt(view: ImageView, point: Point, fitted: Size, frame: Size): ImageView {
  return view.scale > 1 ? FITTED : zoomedAt(view, DOUBLE_CLICK_SCALE, point, fitted, frame);
}

/** Moved no further than the image's edge reaching the frame's; a side that fits stays centred. */
function kept(view: ImageView, fitted: Size, frame: Size): ImageView {
  const within = (offset: number, imageSide: number, frameSide: number) => {
    const slack = Math.max(0, (imageSide * view.scale - frameSide) / 2);
    // Adding 0 turns a -0 into 0.
    return Math.min(slack, Math.max(-slack, offset)) + 0;
  };
  return { scale: view.scale, x: within(view.x, fitted.width, frame.width), y: within(view.y, fitted.height, frame.height) };
}
