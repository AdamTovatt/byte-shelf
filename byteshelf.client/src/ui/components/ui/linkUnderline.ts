/**
 * The `always` underline, for a control that has to be a button but reads as a link — a name in a row that opens
 * a tab rather than a route, say — so it and `InternalLink` draw their underline alike. Hover and keyboard focus
 * both thicken it, so focus still shows where a container clips the shared focus outline. In a module of its own
 * so the button styles can use it without importing the router `InternalLink` needs.
 */
export const LINK_UNDERLINE_CLASS = 'underline hover:decoration-2 focus-visible:decoration-2';
