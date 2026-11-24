/** Why an element does not show what it loaded: a connection lost on the way, or contents the browser may not be able to show. */
export type ShowFailure = 'connectionLost' | 'cannotShow';

/** Why a player stopped. */
export function playerFailure({ error }: HTMLMediaElement): ShowFailure {
  return error && error.code === error.MEDIA_ERR_NETWORK ? 'connectionLost' : 'cannotShow';
}
