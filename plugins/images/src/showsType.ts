const CHOSEN = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';
const OTHER = 'data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==';

/** What this browser answered of each type, which does not change while the page is open. */
const answers = new Map<string, Promise<boolean>>();

/** Whether this browser shows images of `mediaType` in an `<img>`, which it says by the source it chooses for a picture. */
export function showsType(mediaType: string): Promise<boolean> {
  let answer = answers.get(mediaType);
  if (!answer) {
    answer = choosesSourceOf(mediaType);
    answers.set(mediaType, answer);
  }
  return answer;
}

/** Whether the browser chooses a source of `mediaType` for a picture over its image's own, which it does not when it loads neither. */
function choosesSourceOf(mediaType: string): Promise<boolean> {
  const picture = document.createElement('picture');
  const source = picture.appendChild(document.createElement('source'));
  source.type = mediaType;
  source.srcset = CHOSEN;
  const image = picture.appendChild(document.createElement('img'));
  return new Promise((resolve) => {
    image.onload = image.onerror = () => resolve(image.currentSrc === CHOSEN);
    image.src = OTHER;
  });
}
