import { Routes } from '@angular/router';
import { Chat } from './components/chat/chat';
import { Admin } from './components/admin/admin';

export const routes: Routes = [
  { path: '', redirectTo: '/chat', pathMatch: 'full' },
  { path: 'chat', component: Chat },
  { path: 'admin', component: Admin }
];
