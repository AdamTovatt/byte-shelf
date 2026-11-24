import { Component, type ReactNode } from 'react';

interface ErrorBoundaryProps {
  /** Shown in place of what threw while rendering. */
  fallback: ReactNode;
  children: ReactNode;
}

/** Keeps something that throws while rendering from taking the page down with it. A new key tries again. */
export class ErrorBoundary extends Component<ErrorBoundaryProps, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render(): ReactNode {
    return this.state.failed ? this.props.fallback : this.props.children;
  }
}
