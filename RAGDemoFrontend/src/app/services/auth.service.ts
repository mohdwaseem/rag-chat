import { Injectable, Inject, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { Router } from '@angular/router';
import { environment } from '../../environments/environment';
import { AuthResponse, AuthMeResponse, LoginRequest, RegisterRequest } from '../models/auth.model';

export interface AuthState {
  isAuthenticated: boolean;
  role: string | null;
  token: string | null;
  expiresAtUtc: string | null;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly tokenKey = 'authToken';
  private readonly roleKey = 'authRole';
  private readonly expiresKey = 'authExpiresAtUtc';
  private readonly apiUrl = environment.apiUrl;

  private authStateSubject = new BehaviorSubject<AuthState>({
    isAuthenticated: false,
    role: null,
    token: null,
    expiresAtUtc: null
  });

  authState$ = this.authStateSubject.asObservable();

  constructor(
    private http: HttpClient,
    private router: Router,
    @Inject(PLATFORM_ID) private platformId: Object
  ) {
    this.restoreSession();
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/auth/login`, request).pipe(
      tap(response => this.setSession(response))
    );
  }

  register(request: RegisterRequest): Observable<any> {
    return this.http.post(`${this.apiUrl}/auth/register`, request);
  }

  me(): Observable<AuthMeResponse> {
    return this.http.get<AuthMeResponse>(`${this.apiUrl}/auth/me`);
  }

  logout(redirectTo: string = '/login'): void {
    this.clearSession();
    this.router.navigate([redirectTo]);
  }

  getToken(): string | null {
    return this.authStateSubject.value.token;
  }

  isAuthenticated(): boolean {
    return this.authStateSubject.value.isAuthenticated;
  }

  isAdmin(): boolean {
    return this.authStateSubject.value.role === 'admin';
  }

  private restoreSession(): void {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    const token = sessionStorage.getItem(this.tokenKey);
    const role = sessionStorage.getItem(this.roleKey);
    const expiresAtUtc = sessionStorage.getItem(this.expiresKey);

    if (!token || !expiresAtUtc) {
      return;
    }

    const expires = new Date(expiresAtUtc);
    if (Number.isNaN(expires.getTime()) || expires <= new Date()) {
      this.clearSession();
      return;
    }

    this.authStateSubject.next({
      isAuthenticated: true,
      role: role ?? 'user',
      token,
      expiresAtUtc
    });
  }

  private setSession(response: AuthResponse): void {
    if (isPlatformBrowser(this.platformId)) {
      sessionStorage.setItem(this.tokenKey, response.token);
      sessionStorage.setItem(this.roleKey, response.role);
      sessionStorage.setItem(this.expiresKey, response.expiresAtUtc);
    }

    this.authStateSubject.next({
      isAuthenticated: true,
      role: response.role,
      token: response.token,
      expiresAtUtc: response.expiresAtUtc
    });
  }

  private clearSession(): void {
    if (isPlatformBrowser(this.platformId)) {
      sessionStorage.removeItem(this.tokenKey);
      sessionStorage.removeItem(this.roleKey);
      sessionStorage.removeItem(this.expiresKey);
    }

    this.authStateSubject.next({
      isAuthenticated: false,
      role: null,
      token: null,
      expiresAtUtc: null
    });
  }
}
