// Entry of the PUBLIC landing page /cheo-uy-tinh — a separate bundle from the admin app:
// no password gate, no admin API calls, no login.
import React from 'react';
import ReactDOM from 'react-dom/client';
import { CheoUyTinhPage } from './CheoUyTinhPage';
import { cheoUyTinhConfig } from './cheoUyTinh.config';
import './landing.css';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <CheoUyTinhPage config={cheoUyTinhConfig} />
  </React.StrictMode>
);
