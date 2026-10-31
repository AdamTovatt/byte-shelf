/** Shared spring transition presets for motion animations. */
export const springs = {
  /** Fast with a subtle bounce — tooltips, popovers, dropdowns. */
  snappy: { type: 'spring' as const, stiffness: 1000, damping: 85, mass: 2.5 },

  /** Balanced feel — modals, panels, drawers. */
  smooth: { type: 'spring' as const, stiffness: 700, damping: 70, mass: 3 },

  /** Playful overshoot — emphasis animations, onboarding, celebrations. */
  bouncy: { type: 'spring' as const, stiffness: 800, damping: 40, mass: 1.5 },

  /** Crisp, near-instant — expand/collapse, sidebar resize. */
  stiff: { type: 'spring' as const, stiffness: 1500, damping: 100, mass: 0.8 },
};

/** Shared cubic-bezier curves for tween animations (non-bouncing slides). */
export const easings = {
  /** Standard ease-in-out for panels sliding in/out. */
  standard: [0.25, 0.1, 0.25, 1] as const,
};

/** Shared tween transition presets. */
export const tweens = {
  /** Panel/drawer slide — 200ms, standard ease. */
  panel: { type: 'tween' as const, duration: 0.2, ease: easings.standard },
};
