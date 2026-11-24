import hotToast from 'react-hot-toast';

/** Tells the person how something a plugin did went, as the app does. */
export const toast = {
  success: (message: string): void => void hotToast.success(message),
  error: (message: string): void => void hotToast.error(message),
};
