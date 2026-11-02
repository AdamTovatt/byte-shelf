import { BrowserRouter, Routes, Route, Navigate, Outlet } from 'react-router-dom';
import { Toaster } from 'react-hot-toast';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { LoginPage } from './pages/LoginPage';
import { ForgotPasswordPage } from './pages/ForgotPasswordPage';
import { ResetPasswordPage } from './pages/ResetPasswordPage';
import { ShelvesPage } from './pages/ShelvesPage';
import { ShelfPage } from './pages/ShelfPage';
import { TrashPage } from './pages/TrashPage';
import { ShelfAccessTab, ShelfGeneralTab, ShelfKeysTab, ShelfSettingsLayout } from './pages/ShelfSettingsPage';
import { UsersPage } from './pages/UsersPage';
import { SettingsPage } from './pages/SettingsPage';
import { ReplicationPage } from './pages/ReplicationPage';
import { NewVersionBanner } from './components/NewVersionBanner';
import { AppShell } from './components/layout/AppShell';
import { useInvalidationStream } from './hooks/useInvalidationStream';
import { SHELF_ROUTE_PATTERNS } from './lib/shelfPaths';
import { ROUTES } from './lib/routes';
import { useCurrentUser, useIsAdmin } from './hooks/useUserQueries';

function ProtectedRoute() {
  const { isAuthenticated } = useAuth();

  // The live-update stream lives with the signed-in shell: one connection keeps every cached query
  // fresh across the whole app.
  useInvalidationStream(isAuthenticated);

  if (!isAuthenticated) {
    return <Navigate to={ROUTES.LOGIN} replace />;
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
  return isAdmin ? <Outlet /> : <Navigate to={ROUTES.HOME} replace />;
}

function AppRoutes() {
  const { isAuthenticated } = useAuth();

  return (
    <Routes>
      <Route path={ROUTES.LOGIN} element={isAuthenticated ? <Navigate to={ROUTES.HOME} replace /> : <LoginPage />} />
      <Route path={ROUTES.FORGOT_PASSWORD} element={<ForgotPasswordPage />} />
      <Route path={ROUTES.RESET_PASSWORD} element={<ResetPasswordPage />} />
      <Route element={<ProtectedRoute />}>
        <Route path={ROUTES.HOME} element={<ShelvesPage />} />
        <Route path={SHELF_ROUTE_PATTERNS.shelf} element={<ShelfPage />} />
        <Route path={SHELF_ROUTE_PATTERNS.files} element={<ShelfPage />} />
        <Route path={SHELF_ROUTE_PATTERNS.trash} element={<TrashPage />} />
        <Route path={ROUTES.SETTINGS} element={<SettingsPage />} />
        <Route element={<AdminRoute />}>
          <Route path={SHELF_ROUTE_PATTERNS.settings} element={<ShelfSettingsLayout />}>
            <Route index element={<Navigate to="general" replace />} />
            <Route path="general" element={<ShelfGeneralTab />} />
            <Route path="access" element={<ShelfAccessTab />} />
            <Route path="keys" element={<ShelfKeysTab />} />
          </Route>
          <Route path={ROUTES.USERS} element={<UsersPage />} />
          <Route path={ROUTES.REPLICATION} element={<ReplicationPage />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to={isAuthenticated ? ROUTES.HOME : ROUTES.LOGIN} replace />} />
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
          // A long unbroken run, such as a path in the server's own message, wraps rather than leaving
          // the toast; names the app puts in a toast are cut in the middle (NameLine), which needs the
          // message to be able to shrink to the toast's width.
          toastOptions={{ className: '!bg-panel !text-fg !border !border-border !text-body-md !shadow-brand-lg [overflow-wrap:anywhere] [&>[role=status]]:min-w-0' }}
        />
        <AppRoutes />
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
