import { Component, type ReactNode } from "react";
import { AlertTriangle, RefreshCw } from "lucide-react";

interface Props {
  children: ReactNode;
  fallback?: ReactNode;
}

interface State {
  hasError: boolean;
  error: Error | null;
}

/**
 * Catches render errors in child components and shows a recovery UI.
 * Wraps route-level content so a crash in one page doesn't take down the whole app.
 */
export default class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  private handleReset = () => {
    this.setState({ hasError: false, error: null });
  };

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }

      return (
        <div className="flex flex-col items-center justify-center min-h-[400px] px-6 py-16">
          <div className="bg-warm-white border border-border rounded-2xl p-8 max-w-md w-full text-center shadow-sm">
            <div className="w-12 h-12 rounded-full bg-red-50 flex items-center justify-center mx-auto mb-4">
              <AlertTriangle className="w-6 h-6 text-red-600" />
            </div>
            <h2 className="text-[17px] font-semibold text-ink mb-2">
              Something went wrong
            </h2>
            <p className="text-[13px] text-ink-muted mb-6 leading-relaxed">
              An unexpected error occurred. You can try refreshing, or go back to the dashboard.
            </p>
            {this.state.error && (
              <pre className="text-[11px] text-ink-faint bg-cream rounded-xl p-3 mb-6 overflow-auto max-h-24 text-left">
                {this.state.error.message}
              </pre>
            )}
            <div className="flex items-center justify-center gap-3">
              <button
                onClick={this.handleReset}
                className="inline-flex items-center gap-2 px-4 py-2 text-[13px] font-medium text-teal bg-teal-wash border border-teal-border rounded-xl hover:bg-teal/10 transition-colors cursor-pointer"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                Try again
              </button>
              <a
                href="/app"
                className="inline-flex items-center px-4 py-2 text-[13px] font-medium text-ink-muted bg-cream border border-border rounded-xl hover:bg-cream-dark transition-colors"
              >
                Dashboard
              </a>
            </div>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
