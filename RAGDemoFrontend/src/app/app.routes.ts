import { Routes } from '@angular/router';
import { Chat } from './components/chat/chat';
import { Admin } from './components/admin/admin';
import { Login } from './components/auth/login/login';
import { Register } from './components/auth/register/register';
import { adminGuard } from './guards/admin.guard';

export const routes: Routes = [
  { path: '', redirectTo: '/chat', pathMatch: 'full' },
  { path: 'chat', component: Chat },
  { path: 'login', component: Login },
  { path: 'register', component: Register },
  { path: 'admin', component: Admin, canActivate: [adminGuard] }
];
