import { Component, type ReactNode } from 'react';

/**
 * Draws its children anew as one that goes throws in an effect's cleanup, which no boundary that goes with it
 * catches: without this one the whole app would go. What throws as it is drawn is for an `ErrorBoundary`
 * around it, and passes this one by as it is drawn anew.
 */
export class CleanupBoundary extends Component<{ children: ReactNode }> {
  state = {};

  static getDerivedStateFromError() {
    return {};
  }

  render(): ReactNode {
    return this.props.children;
  }
}
