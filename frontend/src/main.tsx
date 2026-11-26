import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { Layout } from './components/Layout';
import { ToastProvider } from './components/Toast';
import Activity from './pages/Activity';
import AddNew from './pages/AddNew';
import Calendar from './pages/Calendar';
import Library from './pages/Library';
import Settings from './pages/Settings';
import System from './pages/System';
import TitleDetail from './pages/TitleDetail';
import Wanted from './pages/Wanted';
import './styles.css';

try {
  document.documentElement.dataset.theme = localStorage.getItem('issuarr-theme') || 'dark';
} catch {
  document.documentElement.dataset.theme = 'dark';
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <ToastProvider>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<Library />} />
            <Route path="add" element={<AddNew />} />
            <Route path="import" element={<AddNew />} />
            <Route path="title/:slug" element={<TitleDetail />} />
            <Route path="calendar" element={<Calendar />} />
            <Route path="activity/*" element={<Activity />} />
            <Route path="wanted/*" element={<Wanted />} />
            <Route path="settings" element={<Settings />} />
            <Route path="settings/:section" element={<Settings />} />
            <Route path="system" element={<System />} />
          </Route>
        </Routes>
      </ToastProvider>
    </BrowserRouter>
  </StrictMode>,
);
