import { Component, type ReactNode } from 'react';

/**
 * Draws its children anew as one that goes throws in an effect's cleanup, which no boundary that goes with it
 * catches: without this one the whole app would go. What keeps throwing as it is drawn is for an
 * `ErrorBoundary` around it, which gets it once React has seen it drawn anew too often.
 */
export class CleanupBoundary extends Component<{ children: ReactNode }, { emptied: boolean }> {
  state = { emptied: false };

  static getDerivedStateFromError() {
    return { emptied: true };
  }

  componentDidMount(): void {
    this.drawAnew();
  }

  componentDidUpdate(): void {
    this.drawAnew();
  }

  /** Once all that was drawn has gone, so that what else throws as it goes does so into nothing, and not as it would be drawn anew. */
  private drawAnew(): void {
    if (this.state.emptied) this.setState({ emptied: false });
  }

  render(): ReactNode {
    return this.state.emptied ? null : this.props.children;
  }
}
