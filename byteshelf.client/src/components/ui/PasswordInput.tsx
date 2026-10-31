import { useState } from 'react';
import { Input, type InputProps } from './Input';
import { IconButton } from './IconButton';
import { iconMap } from '../../lib/iconMap';

export type PasswordInputProps = Omit<InputProps, 'type' | 'endElement'>;

/**
 * An `Input` pre-wired with a show/hide toggle. Visibility state is self-contained, so callers use
 * it like a plain password field without threading their own boolean state and toggle button.
 */
export function PasswordInput({ disabled, ...props }: PasswordInputProps) {
  const [visible, setVisible] = useState(false);
  return (
    <Input
      {...props}
      type={visible ? 'text' : 'password'}
      disabled={disabled}
      endElement={
        <IconButton
          type="button"
          icon={visible ? iconMap.viewOff : iconMap.view}
          onClick={() => setVisible(!visible)}
          disabled={disabled}
          size="sm"
          aria-label={visible ? 'Hide password' : 'Show password'}
        />
      }
    />
  );
}
