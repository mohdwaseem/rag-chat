export interface LoginRequest {
  username: string;
  password: string;
}

export interface RegisterRequest {
  username: string;
  password: string;
  role?: 'user' | 'admin';
}

export interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  role: string;
}

export interface AuthMeResponse {
  username: string;
  roles: string[];
  claims: Array<{ type: string; value: string }>;
}
