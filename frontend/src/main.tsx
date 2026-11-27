import { lazy, StrictMode, Suspense } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { Layout } from './components/Layout';
import { ToastProvider } from './components/Toast';
import { bootstrap, canManage } from './data/api';
import Account from './pages/Account';
import Activity from './pages/Activity';
import AddNew from './pages/AddNew';
import Calendar from './pages/Calendar';
import Home from './pages/Home';
import Library from './pages/Library';
import LibraryImport from './pages/LibraryImport';
import Settings from './pages/Settings';
import System from './pages/System';
import TitleDetail from './pages/TitleDetail';
import Wanted from './pages/Wanted';

// The reader pulls in pdf.js and epub.js, so it's loaded only when opened.
const Reader = lazy(() => import('./pages/Reader'));
import './styles.css';

try {
  document.documentElement.dataset.theme = localStorage.getItem('issuarr-theme') || 'dark';
} catch {
  document.documentElement.dataset.theme = 'dark';
}

await bootstrap();

/** Pages only admins use; readers are sent Home. */
const admin = (page: React.ReactNode) => (canManage() ? page : <Navigate to="/" replace />);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <ToastProvider>
        <Routes>
          <Route
            path="read/:issueId"
            element={
              <Suspense fallback={null}>
                <Reader />
              </Suspense>
            }
          />
          <Route element={<Layout />}>
            <Route index element={<Home />} />
            <Route path="library" element={<Library />} />
            <Route path="add" element={admin(<AddNew />)} />
            <Route path="import" element={admin(<LibraryImport />)} />
            <Route path="title/:slug" element={<TitleDetail />} />
            <Route path="calendar" element={<Calendar />} />
            <Route path="activity/*" element={admin(<Activity />)} />
            <Route path="wanted/*" element={admin(<Wanted />)} />
            <Route path="settings" element={admin(<Settings />)} />
            <Route path="settings/:section" element={admin(<Settings />)} />
            <Route path="system" element={admin(<System />)} />
            <Route path="account" element={<Account />} />
          </Route>
        </Routes>
      </ToastProvider>
    </BrowserRouter>
  </StrictMode>,
);
