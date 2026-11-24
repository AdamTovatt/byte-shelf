/** A signal that aborts as `own` does, or as `signal`, when given, does. */
export function eitherAborts(own: AbortSignal, signal?: AbortSignal): AbortSignal {
  return signal ? AbortSignal.any([own, signal]) : own;
}
