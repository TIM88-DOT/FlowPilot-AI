import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "../../hooks/useAuth";

/**
 * Skeleton of the app shell shown while auth is bootstrapping. Only appears on a genuine cold
 * load (no cached user) — returning users render the real app immediately because AuthProvider
 * seeds the user from sessionStorage. A shell silhouette reads as "loading" far better than a
 * blank full-screen spinner.
 */
function AppShellSkeleton() {
  return (
    <div className="min-h-screen bg-cream flex animate-pulse">
      <aside className="hidden lg:flex w-[240px] border-r border-border bg-warm-white flex-col shrink-0">
        <div className="px-4 h-[60px] flex items-center gap-1.5">
          <div className="w-5 h-5 rounded-full bg-cream-dark" />
          <div className="h-3 w-20 rounded bg-cream-dark" />
        </div>
        <div className="px-3 py-2 space-y-1">
          {Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="h-9 rounded-xl bg-cream-dark/60" />
          ))}
        </div>
      </aside>
      <div className="flex-1 flex flex-col min-w-0">
        <header className="h-[60px] border-b border-border bg-warm-white/80" />
        <main className="flex-1 p-6 space-y-4">
          <div className="h-6 w-40 rounded bg-cream-dark" />
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <div key={i} className="h-24 rounded-2xl bg-warm-white border border-border" />
            ))}
          </div>
          <div className="h-64 rounded-2xl bg-warm-white border border-border" />
        </main>
      </div>
    </div>
  );
}

export default function ProtectedRoute() {
  const { isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return <AppShellSkeleton />;
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return <Outlet />;
}
