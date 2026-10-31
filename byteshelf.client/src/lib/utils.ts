import toast from 'react-hot-toast';

type ClassValue =string | number | boolean | null | undefined | ClassDictionary | ClassArray;
type ClassDictionary = Record<string, boolean | undefined | null>;
type ClassArray = ClassValue[];

function appendClassNames(value: ClassValue, classes: string[]): void {
  if (!value) {
    return;
  }

  if (typeof value === 'string' || typeof value === 'number') {
    classes.push(String(value));
    return;
  }

  if (Array.isArray(value)) {
    for (const nestedValue of value) {
      appendClassNames(nestedValue, classes);
    }
    return;
  }

  for (const [key, isEnabled] of Object.entries(value)) {
    if (isEnabled) {
      classes.push(key);
    }
  }
}

export function cn(...inputs: ClassValue[]): string {
  const classes: string[] = [];

  for (const input of inputs) {
    appendClassNames(input, classes);
  }

  return classes.join(' ');
}

export function getErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error) {
    return error.message || fallback;
  }
  return fallback;
}

/** Runs an action started outside a form, such as a row's button, and shows a refusal as a toast. */
export async function runOrToast(action: () => Promise<unknown>, failure: string): Promise<void> {
  try {
    await action();
  } catch (caught) {
    toast.error(getErrorMessage(caught, failure));
  }
}
