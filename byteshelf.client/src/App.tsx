import { BrowserRouter, Routes, Route, Navigate, Outlet } from 'react-router-dom';
import { Toaster } from 'react-hot-toast';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { LoginPage } from './pages/LoginPage';
import { ForgotPasswordPage } from './pages/ForgotPasswordPage';
import { ResetPasswordPage } from './pages/ResetPasswordPage';
import { ShelvesPage } from './pages/ShelvesPage';
import { ShelfPage } from './pages/ShelfPage';
import { TrashPage } from './pages/TrashPage';
import { ShelfSettingsPage } from './pages/ShelfSettingsPage';
import { UsersPage } from './pages/UsersPage';
import { SettingsPage } from './pages/SettingsPage';
import { ReplicationPage } from './pages/ReplicationPage';
import { NewVersionBanner } from './components/NewVersionBanner';
import { AppShell } from './components/layout/AppShell';
import { useInvalidationStream } from './hooks/useInvalidationStream';
import { SHELF_ROUTE_PATTERNS } from './lib/shelfPaths';
import { useCurrentUser, useIsAdmin } from './hooks/useUserQueries';

function ProtectedRoute() {
  const { isAuthenticated } = useAuth();

  // The live-update stream lives with the signed-in shell: one connection keeps every cached query
  // fresh across the whole app.
  useInvalidationStream(isAuthenticated);

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}

/** Pages only admins see; anyone else is sent home rather than shown a page the server refuses. */
function AdminRoute() {
  const { isLoading } = useCurrentUser();
  const isAdmin = useIsAdmin();
  if (isLoading) return null;
  return isAdmin ? <Outlet /> : <Navigate to="/" replace />;
}

function AppRoutes() {
  const { isAuthenticated } = useAuth();

  return (
    <Routes>
      <Route path="/login" element={isAuthenticated ? <Navigate to="/" replace /> : <LoginPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route element={<ProtectedRoute />}>
        <Route path="/" element={<ShelvesPage />} />
        <Route path={SHELF_ROUTE_PATTERNS.shelf} element={<ShelfPage />} />
        <Route path={SHELF_ROUTE_PATTERNS.files} element={<ShelfPage />} />
        <Route path={SHELF_ROUTE_PATTERNS.trash} element={<TrashPage />} />
        <Route path="/settings" element={<SettingsPage />} />
        <Route element={<AdminRoute />}>
          <Route path={SHELF_ROUTE_PATTERNS.settings} element={<ShelfSettingsPage />} />
          <Route path="/users" element={<UsersPage />} />
          <Route path="/replication" element={<ReplicationPage />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to={isAuthenticated ? '/' : '/login'} replace />} />
    </Routes>
  );
}

function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <NewVersionBanner />
        <Toaster
          position="bottom-right"
          toastOptions={{ className: '!bg-panel !text-fg !border !border-border !text-body-md !shadow-brand-lg' }}
        />
        <AppRoutes />
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
