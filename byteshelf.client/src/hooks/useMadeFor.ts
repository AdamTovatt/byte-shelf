import { useState } from 'react';

/** What `make` makes, the same while `keys`, each compared as it is, stay so; null while they are null. */
export function useMadeFor<T>(keys: readonly unknown[] | null, make: () => T): T | null {
  const [made, setMade] = useState<{ keys: readonly unknown[] | null; value: T | null }>({ keys: null, value: null });
  // Drawn again at once, before anything sees what was made for others.
  if (!areSame(made.keys, keys)) setMade({ keys, value: keys && make() });
  return made.value;
}

function areSame(some: readonly unknown[] | null, others: readonly unknown[] | null): boolean {
  if (some === null || others === null) return some === others;
  return some.length === others.length && some.every((key, index) => Object.is(key, others[index]));
}
